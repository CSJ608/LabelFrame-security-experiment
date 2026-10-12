using LabelFrame.Api;
using LabelFrame.Core.Errors;
using Microsoft.Extensions.Logging;

namespace LabelFrame.Server;

/// <summary>
/// Server 业务服务：设备注册 / 心跳、作业定向投递（宿主轮询领取）、结果回报、集中查询。
/// 设备离线时作业在 Server 暂存（Pending），上线轮询即领取。
/// 业务事件日志：作业创建 / 认领 / 回报终态各一条 INFO（作业粒度，含标识字段）
/// 默认 INFO，可经标准 Logging:LogLevel 按类别 LabelFrame.Server.ServerService 降级。
/// </summary>
public sealed partial class ServerService : IDisposable
{
    /// <summary>在线窗口：超过该时长未心跳视为离线。</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(2);

    private readonly ServerDb _db;
    private readonly LabelFrame.Core.Templates.TemplateStore _templates;
    private readonly PendingJobNotifier? _notifier;
    private readonly ServerOptions? _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>创建业务服务。</summary>
    /// <param name="templates">服务端模板库（templateName 引用提交用；可为空则不启用引用）。</param>
    /// <param name="notifier">待领取作业通知器（长轮询推送用；为空则不通知）。</param>
    /// <param name="options">服务端配置（Pending 暂存 TTL；为空则关闭过期，行为与现状一致）。</param>
    /// <param name="timeProvider">时间源（测试注入 FakeTimeProvider 保证确定性；默认系统时钟）。</param>
    /// <param name="logger">业务事件日志（为空则不记录——单测直连场景）。</param>
    public ServerService(
        ServerDb db,
        LabelFrame.Core.Templates.TemplateStore? templates = null,
        PendingJobNotifier? notifier = null,
        ServerOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<ServerService>? logger = null)
    {
        _db = db;
        _templates = templates!;
        _notifier = notifier;
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    // ---- 业务事件日志：[LoggerMessage] 源生成，默认 INFO、级别可配 ----

    [LoggerMessage(Level = LogLevel.Information, Message = "作业已创建：jobId={JobId}，requestId={RequestId}，目标设备={TargetDeviceId}，标签数={TotalItems}")]
    private partial void LogJobCreated(string jobId, string requestId, string targetDeviceId, int totalItems);

    [LoggerMessage(Level = LogLevel.Information, Message = "作业已被认领：jobId={JobId}，设备={DeviceId}，requestId={RequestId}，标签数={TotalItems}")]
    private partial void LogJobClaimed(string jobId, string deviceId, string requestId, int totalItems);

    [LoggerMessage(Level = LogLevel.Information, Message = "作业终态：jobId={JobId}，设备={DeviceId}，requestId={RequestId}，状态={Status}，完成={CompletedItems}，失败={FailedItems}，原因={ErrorMessage}")]
    private partial void LogJobFinished(string jobId, string deviceId, string requestId, string status, int completedItems, int failedItems, string? errorMessage);

    /// <summary>Pending 暂存 TTL 截止时间：早于该时间创建且仍 Pending 的作业视为超期；TTL 关闭时为 null（不过滤）。</summary>
    private DateTimeOffset? PendingTtlCutoff(DateTimeOffset now)
        => _options?.PendingJobTtl is { } ttl ? now - ttl : null;

    /// <summary>模板化领域异常构造（③ /）：模板 + 参数，message 渲染后与旧内插文案等价，参数随异常透传 ErrorView.params。</summary>
    private static ServerException Templated(string code, string template, Dictionary<string, string> parameters)
        => new(code, ErrorMessageTemplates.Format(template, parameters), parameters);

    /// <summary>注册 / 更新设备并刷新心跳。</summary>
    public async Task<DeviceView> RegisterDeviceAsync(string? deviceId, string? name, string? lastIp = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 deviceId。");
        }

        var now = _time.GetUtcNow();
        var device = await _db.UpsertDeviceAsync(new Device
        {
            Id = deviceId,
            Name = string.IsNullOrWhiteSpace(name) ? deviceId : name,
            RegisteredAt = now,
            LastSeenAt = now,
            LastIp = NormalizeIpText(lastIp),
        }, cancellationToken);
        return ToView(device, now);
    }

    /// <summary>刷新设备心跳（长轮询通知端点用作在线保活）。设备未注册时抛出 DeviceNotFound。</summary>
    public async Task TouchDeviceAsync(string deviceId, DateTimeOffset now, string? lastIp = null, CancellationToken cancellationToken = default)
    {
        var affected = await _db.TouchDeviceAsync(deviceId, now, NormalizeIpText(lastIp), cancellationToken);
        if (affected == 0)
        {
            throw Templated(ServerErrorCodes.DeviceNotFound, "设备未注册：{deviceId}。", new() { ["deviceId"] = deviceId });
        }
    }

    /// <summary>设备目录（含在线状态）。</summary>
    public async Task<IReadOnlyList<DeviceView>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var devices = await _db.ListDevicesAsync(cancellationToken);
        return devices.Select(d => ToView(d, now)).ToList();
    }

    /// <summary>按 last_ip 精确查找设备（忽略大小写；未找到返回 null）。</summary>
    public async Task<DeviceView?> FindDeviceByIpAsync(string ip, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeIpText(ip);
        if (normalized is null)
        {
            return null;
        }

        var now = _time.GetUtcNow();
        var device = await _db.FindDeviceByIpAsync(normalized, cancellationToken);
        return device is null ? null : ToView(device, now);
    }

    /// <summary>提交作业（幂等 requestId）；目标设备未注册时拒绝。</summary>
    public async Task<ServerJobView> SubmitJobAsync(SubmitJobRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 requestId（幂等键）。");
        }

        // 终态回调地址校验：scheme 白名单仅 http/https，提交即拒（作业不入队）——
        // 空串 / 纯空白 / 裸字符串 / file:// 等一律拒绝（从 LF_SRV_002 拆出专属码 LF_SRV_013，参数键 callbackUrl 单码单键集）
        if (request.CallbackUrl is not null && !JobCallbackUrl.IsAllowed(request.CallbackUrl))
        {
            throw Templated(
                ServerErrorCodes.InvalidCallbackUrl,
                "callbackUrl 无效（仅支持 http/https 地址）：{callbackUrl}。",
                new() { ["callbackUrl"] = request.CallbackUrl });
        }

        var callbackUrl = string.IsNullOrWhiteSpace(request.CallbackUrl) ? null : request.CallbackUrl.Trim();

        // 目标设备解析：targetDeviceId 优先；未提供时按 targetIp 查找（找不到 404）。
        var targetDeviceId = request.TargetDeviceId;
        if (string.IsNullOrWhiteSpace(targetDeviceId))
        {
            if (string.IsNullOrWhiteSpace(request.TargetIp))
            {
                throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 targetDeviceId / targetIp（定向投递目标）。");
            }

            var normalizedIp = NormalizeIpText(request.TargetIp) ?? request.TargetIp;
            var byIp = await _db.FindDeviceByIpAsync(normalizedIp, cancellationToken);
            if (byIp is null)
            {
                // 按 IP 未找到与「设备未注册（LF_SRV_001 {deviceId}）」语义不同——拆码 LF_SRV_012，各自单参数键集
                throw Templated(ServerErrorCodes.DeviceNotFoundByIp, "按 IP 未找到设备：{ip}。", new() { ["ip"] = request.TargetIp });
            }

            targetDeviceId = byIp.Id;
        }

        var template = await ResolveTemplateAsync(request, cancellationToken);
        if (template is null)
        {
            throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 template（contract + layout）或 templateName。");
        }

        if (request.Labels is null || request.Labels.Count == 0)
        {
            throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 labels（至少一张）。");
        }

        // 门只覆盖提交路径：幂等 requestId 为「查询-再插入」两步，进程内串行化避免并发重放双双插入
        //（DB 层 request_id UNIQUE 兜底跨进程重复）；注册 / 领取 / 回报路径无 check-then-act，已不再串行化
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await _db.GetDeviceAsync(targetDeviceId, cancellationToken) is null)
            {
                throw Templated(ServerErrorCodes.DeviceNotFound, "目标设备未注册：{deviceId}。", new() { ["deviceId"] = targetDeviceId });
            }

            var existing = await _db.GetJobByRequestIdAsync(request.RequestId, cancellationToken);
            if (existing is not null)
            {
                return await ToJobViewAsync(existing, cancellationToken);
            }

            var payloadJson = System.Text.Json.JsonSerializer.Serialize(
                new JobPayload(template, request.Labels),
                RoutingJson.Options);

            var job = await _db.CreateJobAsync(new ServerJob
            {
                Id = Guid.NewGuid().ToString("N"),
                RequestId = request.RequestId,
                TargetDeviceId = targetDeviceId,
                Status = ServerJobStatus.Pending,
                CreatedAt = _time.GetUtcNow(),
                TotalItems = request.Labels.Count,
                PayloadJson = payloadJson,
                CallbackUrl = callbackUrl,
            }, cancellationToken);
            LogJobCreated(job!.Id, job.RequestId, targetDeviceId, job.TotalItems);
            _notifier?.Notify(targetDeviceId);
            return await ToJobViewAsync(job!, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>设备领取作业：同一事务内刷新心跳并把该设备的未过期 Pending 作业置为 Claimed（超期作业不下发，正确性兜底不依赖过期扫描周期）。</summary>
    public async Task<IReadOnlyList<ClaimedJob>> ClaimPendingJobsAsync(string deviceId, string? lastIp = null, CancellationToken cancellationToken = default)
    {
        // 并发安全由 DB 层保证（圈定为单条 UPDATE ... RETURNING 原子操作），无需进程内串行化；
        // 心跳与圈定合并为单个写事务（写合批，减少一次单写锁排队）
        var now = _time.GetUtcNow();
        var (touched, jobs) = await _db.TouchAndClaimPendingJobsAsync(
            deviceId, now, NormalizeIpText(lastIp), limit: 10, PendingTtlCutoff(now), cancellationToken);
        if (touched == 0)
        {
            throw Templated(ServerErrorCodes.DeviceNotFound, "设备未注册：{deviceId}。", new() { ["deviceId"] = deviceId });
        }

        foreach (var job in jobs)
        {
            LogJobClaimed(job.Id, deviceId, job.RequestId, job.TotalItems);
        }

        return jobs.Select(job => new ClaimedJob(
            job.Id,
            job.RequestId,
            job.TotalItems,
            System.Text.Json.JsonSerializer.Deserialize<JobPayload>(job.PayloadJson, RoutingJson.Options)!)).ToList();
    }

    /// <summary>设备当前是否有未过期 Pending 作业（notify 挂起前积压预检；判定与领取过滤一致）。</summary>
    public Task<bool> HasDeliverablePendingJobsAsync(string deviceId, CancellationToken cancellationToken = default)
        => _db.HasDeliverablePendingJobsAsync(deviceId, PendingTtlCutoff(_time.GetUtcNow()), cancellationToken);

    /// <summary>设备回报作业结果。</summary>
    public async Task<ServerJobView> ReportResultAsync(string deviceId, string jobId, ReportResultRequest report, CancellationToken cancellationToken = default)
    {
        var job = await _db.GetJobAsync(jobId, cancellationToken)
                ?? throw Templated(ServerErrorCodes.JobNotFound, "作业不存在：{jobId}。", new() { ["jobId"] = jobId });
        if (job.TargetDeviceId != deviceId)
        {
                throw Templated(
                    ServerErrorCodes.NotJobOwner,
                    "设备 {deviceId} 不是作业 {jobId} 的领取者。",
                    new() { ["deviceId"] = deviceId, ["jobId"] = jobId });
        }

        // 幂等重放：终态作业直接返回
        if (job.Status is ServerJobStatus.Completed or ServerJobStatus.Failed)
        {
                return await ToJobViewAsync(job, cancellationToken);
        }

        if (job.Status != ServerJobStatus.Claimed)
        {
                throw Templated(
                    ServerErrorCodes.InvalidTransition,
                    "作业 {jobId} 当前状态 {status} 不允许回报结果。",
                    new() { ["jobId"] = jobId, ["status"] = job.Status.ToString() });
        }

        var isCompleted = string.Equals(report.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        var updated = await _db.UpdateJobResultAsync(
                jobId,
                isCompleted ? ServerJobStatus.Completed : ServerJobStatus.Failed,
                report.CompletedItems ?? 0,
                report.FailedItems ?? 0,
                report.ErrorMessage,
                _time.GetUtcNow(),
                report.ErrorCode,
                cancellationToken);
        LogJobFinished(jobId, deviceId, job.RequestId, updated!.Status.ToString(), report.CompletedItems ?? 0, report.FailedItems ?? 0, report.ErrorMessage);

        // 终态回调登记（三处终态转移点之一）：异步投递，不内联在回报路径中——
        // 外部端点慢 / 挂不影响宿主回报；幂等登记，重复到达安全
        if (updated.CallbackUrl is not null)
        {
            await _db.EnqueueJobCallbacksAsync(_time.GetUtcNow(), jobId, cancellationToken);
        }

        return await ToJobViewAsync(updated!, cancellationToken);
    }

    /// <summary>
    /// 设备进度增量上报：仅 Claimed 接受，计数按字段取 max 单调递增；只描述过程——
    /// 不改终态字段、不刷新 claimed_at（不干预超时回收计龄）。终态作业幂等 no-op 返回当前视图。
    /// </summary>
    public async Task<ServerJobView> ReportProgressAsync(string deviceId, string jobId, ReportProgressRequest progress, CancellationToken cancellationToken = default)
    {
        var job = await _db.GetJobAsync(jobId, cancellationToken)
                ?? throw Templated(ServerErrorCodes.JobNotFound, "作业不存在：{jobId}。", new() { ["jobId"] = jobId });
        if (job.TargetDeviceId != deviceId)
        {
                throw Templated(
                    ServerErrorCodes.NotJobOwner,
                    "设备 {deviceId} 不是作业 {jobId} 的领取者。",
                    new() { ["deviceId"] = deviceId, ["jobId"] = jobId });
        }

        // 幂等 no-op：终态作业（含已被超时回收的 Failed）直接返回既有视图，计数不再变化
        if (job.Status is ServerJobStatus.Completed or ServerJobStatus.Failed or ServerJobStatus.Expired)
        {
            return await ToJobViewAsync(job, cancellationToken);
        }

        if (job.Status != ServerJobStatus.Claimed)
        {
                throw Templated(
                    ServerErrorCodes.InvalidTransition,
                    "作业 {jobId} 当前状态 {status} 不允许上报进度。",
                    new() { ["jobId"] = jobId, ["status"] = job.Status.ToString() });
        }

        var affected = await _db.UpdateJobProgressAsync(
            jobId,
            progress.CompletedItems ?? 0,
            progress.FailedItems ?? 0,
            cancellationToken);
        if (affected == 0)
        {
            // 与终态回报 / 超时回收的竞态：领取后已转终态，按幂等 no-op 返回当前视图
            job = await _db.GetJobAsync(jobId, cancellationToken) ?? job;
        }
        else
        {
            job = (await _db.GetJobAsync(jobId, cancellationToken))!;
        }

        return await ToJobViewAsync(job, cancellationToken);
    }

    /// <summary>查询作业（含设备在线状态）。</summary>
    public async Task<ServerJobView> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = await _db.GetJobAsync(jobId, cancellationToken)
            ?? throw Templated(ServerErrorCodes.JobNotFound, "作业不存在：{jobId}。", new() { ["jobId"] = jobId });
        return await ToJobViewAsync(job, cancellationToken);
    }

    /// <summary>作业列表（倒序；可选 deviceId 过滤——客户端只看自己的作业，服务端 UI 不传看全部）。</summary>
    public async Task<IReadOnlyList<ServerJobView>> ListJobsAsync(int limit = 100, string? deviceId = null, CancellationToken cancellationToken = default)
    {
        var jobs = await _db.ListJobsAsync(limit, deviceId, cancellationToken);
        var views = new List<ServerJobView>();
        foreach (var job in jobs)
        {
            views.Add(await ToJobViewAsync(job, cancellationToken));
        }

        return views;
    }

    private async Task<ServerJobView> ToJobViewAsync(ServerJob job, CancellationToken cancellationToken)
    {
        var device = await _db.GetDeviceAsync(job.TargetDeviceId, cancellationToken);
        var deviceStatus = device is null
            ? DeviceStatus.Offline
            : IsOnline(device, _time.GetUtcNow()) ? DeviceStatus.Online : DeviceStatus.Offline;

        // 终态回调投递状态：无回调地址的作业不查投递表（绝大多数作业零开销）
        JobCallbackTask? callback = job.CallbackUrl is null ? null : await _db.GetJobCallbackAsync(job.Id, cancellationToken);
        return new ServerJobView(
            job.Id,
            job.RequestId,
            job.TargetDeviceId,
            job.Status.ToString(),
            job.CreatedAt,
            job.TotalItems,
            job.CompletedItems,
            job.FailedItems,
            job.ErrorMessage,
            deviceStatus.ToString(),
            callback?.Status.ToString(),
            callback?.Attempts,
            callback?.LastError);
    }

    private static DeviceView ToView(Device device, DateTimeOffset now) => new(
        device.Id,
        device.Name,
        device.RegisteredAt,
        device.LastSeenAt,
        IsOnline(device, now) ? DeviceStatus.Online.ToString() : DeviceStatus.Offline.ToString(),
        device.LastIp);

    private static bool IsOnline(Device device, DateTimeOffset now)
        => now - device.LastSeenAt <= OnlineWindow + ClockSkew;

    /// <summary>规范化来源 IP：IPv4-mapped IPv6（::ffff:a.b.c.d）统一为 IPv4 文本；其余原样。</summary>
    public static string? NormalizeRemoteIp(System.Net.IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        return address.ToString();
    }

    /// <summary>规范化 IP 文本：可解析的 IP 按 NormalizeRemoteIp 处理；解析失败原样返回（查找不命中）。</summary>
    public static string? NormalizeIpText(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        var trimmed = ip.Trim();
        return System.Net.IPAddress.TryParse(trimmed, out var address)
            ? NormalizeRemoteIp(address)
            : trimmed;
    }

    /// <summary>解析提交模板：templateName 引用服务端模板库（含图片 base64）；否则用自包含模板。</summary>
    private async Task<TemplateDto?> ResolveTemplateAsync(SubmitJobRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TemplateName))
        {
            if (_templates is null)
            {
                throw new ServerException(ServerErrorCodes.InvalidRequest, "服务端未启用模板库，不能按 templateName 提交。");
            }

            var package = await _templates.GetAsync(request.TemplateName, cancellationToken);
            if (package is null)
            {
                throw Templated(
                    ServerErrorCodes.TemplateNotFound,
                    "模板不存在：{templateName}。",
                    new() { ["templateName"] = request.TemplateName });
            }

            return new TemplateDto(
                package.Contract,
                package.Layout,
                package.Name,
                package.Images.ToDictionary(kv => kv.Key, kv => System.Convert.ToBase64String(kv.Value)));
        }

        if (request.Template?.Contract is null || request.Template.Layout is null)
        {
            return null;
        }

        return request.Template;
    }

    /// <summary>释放内部信号量（宿主停机时由 DI 容器触发）。</summary>
    public void Dispose() => _gate.Dispose();
}
