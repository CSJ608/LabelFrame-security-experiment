using LabelFrame.Core.Errors;

namespace LabelFrame.Core.Jobs;

/// <summary>作业队列：幂等提交、逐张状态、挂起 / 恢复 / 取消、批内顺序。</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:类型名不应以后缀结尾",
    Justification = "域类型语义即打印队列，命名直白优先")]
public sealed class LabelJobQueue : IDisposable
{
    private readonly ILabelJobStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _pendingWake = new(0, int.MaxValue);
    private bool _shutdownPrepared;
    private int _activeSends;

    /// <summary>模板化领域异常构造：模板 + 参数，参数随异常透传 ErrorView.params。</summary>
    private static LabelJobException Templated(string code, string template, Dictionary<string, string> parameters)
        => new(code, ErrorMessageTemplates.Format(template, parameters), parameters);

    private sealed class PrintSendLease(LabelJobQueue owner) : IDisposable
    {
        private LabelJobQueue? _owner = owner;

        public void Dispose()
        {
            var queue = Interlocked.Exchange(ref _owner, null);
            if (queue is not null)
            {
                Interlocked.Decrement(ref queue._activeSends);
            }
        }
    }

    /// <summary>创建作业队列。</summary>
    public LabelJobQueue(ILabelJobStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>提交作业（文本指令载荷）：requestId 已存在时返回已有作业（幂等）。</summary>
    /// <param name="requestId">幂等键。</param>
    /// <param name="zplLabels">每张标签的打印机指令（字段名沿用 zpl：图片模式 = ^GF 位图 ZPL，原生指令模式 = 品牌原生指令；批内顺序）。</param>
    /// <returns>作业与是否新建（false 表示 requestId 重放返回已有作业）。</returns>
    public Task<(LabelJob Job, bool Created)> SubmitAsync(string requestId, IReadOnlyList<string> zplLabels, CancellationToken cancellationToken = default)
        => SubmitAsync(requestId, zplLabels.Select(LabelJobPayload.OfText).ToList(), cancellationToken);

    /// <summary>提交作业（文本 / 二进制双形态载荷，决策 #195）：requestId 已存在时返回已有作业（幂等）。</summary>
    /// <param name="requestId">幂等键。</param>
    /// <param name="payloads">每张标签的回放载荷（文本或二进制二选一；批内顺序）。</param>
    /// <returns>作业与是否新建（false 表示 requestId 重放返回已有作业）。</returns>
    public async Task<(LabelJob Job, bool Created)> SubmitAsync(string requestId, IReadOnlyList<LabelJobPayload> payloads, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(payloads);
        if (payloads.Count == 0)
        {
            throw new ArgumentException("作业至少包含一张标签。", nameof(payloads));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_shutdownPrepared)
            {
                throw new LabelJobException(JobErrorCodes.InvalidTransition, "宿主正在安全退出，暂不接受新打印作业。");
            }

            var existing = await _store.GetJobByRequestIdAsync(requestId, cancellationToken);
            if (existing is not null)
            {
                return (existing, Created: false);
            }

            var now = DateTimeOffset.UtcNow;
            var jobId = Guid.NewGuid().ToString("N");
            var job = new LabelJob
            {
                Id = jobId,
                RequestId = requestId,
                Status = LabelJobStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                Items = payloads
                    .Select((payload, index) => new LabelJobItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        JobId = jobId,
                        Index = index,
                        Status = LabelJobItemStatus.Pending,
                        Zpl = payload.Bytes is null ? payload.Text : string.Empty,
                        CommandBytes = payload.Bytes,
                    })
                    .ToList(),
            };

            var createdJob = await _store.CreateJobAsync(job, cancellationToken);
            SignalPendingWake();
            return (createdJob, Created: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 等待「出现新待打项」的唤醒信号；超时返回仅作兜底轮询（正常路径不触发）。
    /// 信号语义是「可能有变化」而非「一定可领取」——调用方返回后仍需自行探测 / 领取。
    /// </summary>
    public Task<bool> WaitForPendingWakeAsync(TimeSpan safetyPollDelay, CancellationToken cancellationToken = default)
        => _pendingWake.WaitAsync(safetyPollDelay, cancellationToken);

    /// <summary>按作业标识查询。</summary>
    public Task<LabelJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
        => _store.GetJobAsync(jobId, cancellationToken);

    /// <summary>按幂等键查询既有作业（不存在返回 null）。</summary>
    public Task<LabelJob?> GetByRequestIdAsync(string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return _store.GetJobByRequestIdAsync(requestId, cancellationToken);
    }

    /// <summary>是否存在待打 Item（轻量探测，不加载作业与指令）。</summary>
    public Task<bool> HasPendingItemsAsync(CancellationToken cancellationToken = default)
        => _store.HasPendingItemsAsync(cancellationToken);

    /// <summary>取下一个待打 Item：最旧 Pending 作业中序号最小的 Pending Item，并置为 Printing；无待打时返回 null。</summary>
    public async Task<(string JobId, LabelJobItem Item)?> ClaimNextItemAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_shutdownPrepared)
            {
                return null;
            }

            var activeJobs = new List<LabelJob>();
            activeJobs.AddRange(await _store.ListJobsByStatusAsync(LabelJobStatus.Pending, cancellationToken));
            activeJobs.AddRange(await _store.ListJobsByStatusAsync(LabelJobStatus.Printing, cancellationToken));
            var job = activeJobs
                .Where(j => j.Items.Any(i => i.Status == LabelJobItemStatus.Pending))
                .OrderBy(j => j.CreatedAt)
                .ThenBy(j => j.Id)
                .FirstOrDefault();
            if (job is null)
            {
                return null;
            }

            var item = job.Items
                .Where(i => i.Status == LabelJobItemStatus.Pending)
                .OrderBy(i => i.Index)
                .First();

            await _store.SetItemStatusAsync(job.Id, item.Id, LabelJobItemStatus.Printing, null, null, cancellationToken);
            await _store.SetJobStatusAsync(job.Id, LabelJobStatus.Printing, cancellationToken);

            var fresh = await _store.GetJobAsync(job.Id, cancellationToken);
            var freshItem = fresh!.Items.First(i => i.Id == item.Id);
            return (job.Id, freshItem);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 原子准备宿主退出：在队列锁内确认没有正在发送的标签，并阻止后续提交 / 领取。
    /// 若当前打印仍在进行则不改变队列状态，调用方应拒绝本次退出请求。
    /// </summary>
    public async Task<bool> TryPrepareForShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_shutdownPrepared)
            {
                return true;
            }

            if (Volatile.Read(ref _activeSends) > 0)
            {
                return false;
            }

            var printingJobs = await _store.ListJobsByStatusAsync(LabelJobStatus.Printing, cancellationToken);
            if (printingJobs.Any(job => job.Items.Any(item => item.Status == LabelJobItemStatus.Printing)))
            {
                return false;
            }

            _shutdownPrepared = true;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>登记一次真实传输区间；返回 null 表示退出已准备好，不得再开始发送。</summary>
    public async Task<IDisposable?> TryBeginPrintSendAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_shutdownPrepared)
            {
                return null;
            }

            Interlocked.Increment(ref _activeSends);
            return new PrintSendLease(this);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Item 打印完成。</summary>
    public async Task<LabelJob> CompleteItemAsync(string jobId, string itemId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.SetItemStatusAsync(jobId, itemId, LabelJobItemStatus.Completed, null, null, cancellationToken)
                ?? throw Templated(JobErrorCodes.JobNotFound, "作业不存在：{jobId}。", new() { ["jobId"] = jobId });

            if (job.Status is LabelJobStatus.Cancelled or LabelJobStatus.Completed)
            {
                return job;
            }

            if (job.Items.All(i => i.Status == LabelJobItemStatus.Completed))
            {
                return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Completed, cancellationToken) ?? job;
            }

            if (!job.Items.Any(i => i.Status == LabelJobItemStatus.Pending) && job.Status is LabelJobStatus.Pending or LabelJobStatus.Printing)
            {
                return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Failed, cancellationToken) ?? job;
            }

            return job;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Item 发送失败：仍有未打 Item 时挂起作业，否则作业结束为 Failed。</summary>
    public async Task<LabelJob> FailItemAsync(string jobId, string itemId, string errorCode, string errorMessage, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.SetItemStatusAsync(jobId, itemId, LabelJobItemStatus.Failed, errorCode, errorMessage, cancellationToken)
                ?? throw Templated(JobErrorCodes.JobNotFound, "作业不存在：{jobId}。", new() { ["jobId"] = jobId });

            if (job.Status is LabelJobStatus.Cancelled or LabelJobStatus.Completed)
            {
                return job;
            }

            if (job.Items.Any(i => i.Status == LabelJobItemStatus.Pending))
            {
                return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Suspended, cancellationToken) ?? job;
            }

            return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Failed, cancellationToken) ?? job;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>挂起作业（允许 Pending / Printing → Suspended）。</summary>
    public async Task<LabelJob> SuspendAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.GetJobAsync(jobId, cancellationToken)
                ?? throw Templated(JobErrorCodes.JobNotFound, "作业不存在：{jobId}。", new() { ["jobId"] = jobId });
            if (job.Status is not (LabelJobStatus.Pending or LabelJobStatus.Printing))
            {
                throw Templated(JobErrorCodes.InvalidTransition, "作业当前状态 {status} 不允许挂起。", new() { ["status"] = job.Status.ToString() });
            }

            return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Suspended, cancellationToken) ?? job;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>恢复挂起作业（Suspended → Pending，且必须有未打 Item）。</summary>
    public async Task<LabelJob> ResumeAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.GetJobAsync(jobId, cancellationToken)
                ?? throw Templated(JobErrorCodes.JobNotFound, "作业不存在：{jobId}。", new() { ["jobId"] = jobId });
            if (job.Status != LabelJobStatus.Suspended)
            {
                throw Templated(JobErrorCodes.InvalidTransition, "作业当前状态 {status} 不允许恢复。", new() { ["status"] = job.Status.ToString() });
            }

            if (!job.Items.Any(i => i.Status == LabelJobItemStatus.Pending))
            {
                throw new LabelJobException(JobErrorCodes.InvalidTransition, "作业没有可续打的标签，无法恢复。");
            }

            var resumed = await _store.SetJobStatusAsync(jobId, LabelJobStatus.Pending, cancellationToken) ?? job;
            SignalPendingWake();
            return resumed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>取消作业：剩余 Pending / Printing Item 置 Cancelled。</summary>
    public async Task<LabelJob> CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.GetJobAsync(jobId, cancellationToken)
                ?? throw Templated(JobErrorCodes.JobNotFound, "作业不存在：{jobId}。", new() { ["jobId"] = jobId });
            if (job.Status is LabelJobStatus.Completed or LabelJobStatus.Cancelled or LabelJobStatus.Failed)
            {
                throw Templated(JobErrorCodes.InvalidTransition, "作业当前状态 {status} 不允许取消。", new() { ["status"] = job.Status.ToString() });
            }

            foreach (var item in job.Items.Where(i => i.Status is LabelJobItemStatus.Pending or LabelJobItemStatus.Printing))
            {
                await _store.SetItemStatusAsync(job.Id, item.Id, LabelJobItemStatus.Cancelled, null, null, cancellationToken);
            }

            return await _store.SetJobStatusAsync(jobId, LabelJobStatus.Cancelled, cancellationToken) ?? job;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>失败项单独重打：把指定序号的 Failed Item 重置为 Pending。</summary>
    public async Task<LabelJob> RetryItemAsync(string jobId, int itemIndex, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var job = await _store.GetJobAsync(jobId, cancellationToken)
                ?? throw Templated(JobErrorCodes.JobNotFound, "作业不存在：{jobId}。", new() { ["jobId"] = jobId });
            if (job.Status == LabelJobStatus.Completed || job.Status == LabelJobStatus.Cancelled)
            {
                throw Templated(JobErrorCodes.InvalidTransition, "作业当前状态 {status} 不允许重打。", new() { ["status"] = job.Status.ToString() });
            }

            if (itemIndex < 0 || itemIndex >= job.Items.Count)
            {
                // 条目级越界与「作业状态不允许」（LF_JOB_002 {status}）语义不同，用独立问题码与参数集
                throw Templated(JobErrorCodes.ItemNotFound, "作业没有第 {itemIndex} 张标签。", new() { ["itemIndex"] = itemIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }

            var item = job.Items[itemIndex];
            if (item.Status != LabelJobItemStatus.Failed)
            {
                // 与 LF_JOB_002（{status}）区分：单码单参数键集（{itemIndex} + {itemStatus}）
                throw Templated(JobErrorCodes.ItemNotRetriable, "第 {itemIndex} 张状态为 {itemStatus}，仅 Failed 可重打。", new() { ["itemIndex"] = itemIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), ["itemStatus"] = item.Status.ToString() });
            }

            await _store.SetItemStatusAsync(job.Id, item.Id, LabelJobItemStatus.Pending, null, null, cancellationToken);
            if (job.Status == LabelJobStatus.Failed)
            {
                await _store.SetJobStatusAsync(job.Id, LabelJobStatus.Pending, cancellationToken);
            }
            else if (job.Status == LabelJobStatus.Suspended)
            {
                // 挂起作业重打后保持挂起（由调用方决定是否恢复），状态无需改动
            }

            SignalPendingWake();
            return (await _store.GetJobAsync(jobId, cancellationToken))!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>服务启动时调用：把 in-flight（Printing）作业置 Suspended、在途 Item 重置为 Pending（恢复后续打优先保证不漏打）。</summary>
    public async Task MarkInterruptedJobsSuspendedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var interrupted = await _store.ListJobsByStatusAsync(LabelJobStatus.Printing, cancellationToken);
            foreach (var job in interrupted)
            {
                foreach (var item in job.Items.Where(i => i.Status == LabelJobItemStatus.Printing))
                {
                    await _store.SetItemStatusAsync(job.Id, item.Id, LabelJobItemStatus.Pending, null, null, cancellationToken);
                }

                await _store.SetJobStatusAsync(job.Id, LabelJobStatus.Suspended, cancellationToken);
                SignalPendingWake();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>发出唤醒信号：写入已提交后才调用（先信号后提交会让 Worker 探测落空、信号被消费而错过唤醒）。</summary>
    private void SignalPendingWake() => _pendingWake.Release();

    /// <summary>释放内部信号量（宿主停机时由 DI 容器触发）。</summary>
    public void Dispose()
    {
        _gate.Dispose();
        _pendingWake.Dispose();
    }
}
