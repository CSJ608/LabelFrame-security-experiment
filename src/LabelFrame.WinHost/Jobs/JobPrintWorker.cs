using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Jobs;

/// <summary>
/// 打印 Worker：串行领取作业中的下一张标签并通过传输发送；
/// 发送失败记 Failed 并由队列决定挂起 / 结束。每台打印机一次只处理一张。
/// 按「批次作业」设置节流——每发满 N 张后、下一张发送前暂停间隔（claim-then-delay），
/// 本机作业与服务端作业统一生效；批次计数内存态、按作业重置（每个作业首张不等待批间间隔）、不持久化。
/// </summary>
public sealed class JobPrintWorker : BackgroundService
{
    /// <summary>空转等待兜底周期：信号唤醒为主（作业入队即醒），超时兜底防信号遗漏（正常路径不触发）。</summary>
    private static readonly TimeSpan SafetyPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>探测有 Pending 项但领取落空（挂起作业等）时的回退等待：信号可提前唤醒。</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(200);

    private readonly LabelJobQueue _queue;
    private readonly ITransportManager _transportManager;
    private readonly ILogger<JobPrintWorker> _logger;
    private readonly PrintSettings _printSettings;
    private readonly TimeProvider _time;

    /// <summary>批次节流内存计数：当前作业内发送成功张数，切换作业时归零（作业首张不等待批间间隔）。</summary>
    private int _sendsSinceBatch;

    /// <summary>最近一次发送所属作业：领取到不同作业时将批次计数归零（按作业重置，决议）。</summary>
    private string? _lastSentJobId;

    /// <summary>创建打印 Worker。</summary>
    public JobPrintWorker(LabelJobQueue queue, ITransportManager transportManager, ILogger<JobPrintWorker> logger, PrintSettings printSettings)
        : this(queue, transportManager, logger, printSettings, TimeProvider.System)
    {
    }

    /// <summary>创建打印 Worker（注入 TimeProvider：节流测试用 FakeTimeProvider 确定性推进）。</summary>
    public JobPrintWorker(LabelJobQueue queue, ITransportManager transportManager, ILogger<JobPrintWorker> logger, PrintSettings printSettings, TimeProvider timeProvider)
    {
        _queue = queue;
        _transportManager = transportManager;
        _logger = logger;
        _printSettings = printSettings;
        _time = timeProvider;
    }

    /// <summary>打印目标摘要（失败日志用）：插件参数字典压成 k=v 逗号串，空参数回退插件 ID 自述。</summary>
    private static string DescribeTarget(Transport.TransportConfig connection)
        => connection.Params.Count == 0
            ? "(无参数)"
            : string.Join(",", connection.Params.Select(kv => $"{kv.Key}={kv.Value}"));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _queue.MarkInterruptedJobsSuspendedAsync(stoppingToken);
            _logger.LogInformation("启动完成：中断作业已恢复为挂起状态。");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时恢复中断作业失败。");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 空转等待信号唤醒（提交 / 恢复 / 重打入队即醒）；无信号时超时兜底轮询。
                // 唤醒后仍先轻量探测（EXISTS）再走完整领取，避免无谓加载 Pending/Printing 作业（含 ZPL 文本）。
                if (!await _queue.HasPendingItemsAsync(stoppingToken))
                {
                    await _queue.WaitForPendingWakeAsync(SafetyPollInterval, stoppingToken);
                    continue;
                }

                var next = await _queue.ClaimNextItemAsync(stoppingToken);
                if (next is null)
                {
                    // 探测与领取之间被并发领走，或存在挂起作业（Item 为 Pending 但作业不可领取）——
                    // 信号可提前唤醒，超时兜底保持既有 200ms 周期
                    await _queue.WaitForPendingWakeAsync(IdleDelay, stoppingToken);
                    continue;
                }

                var (jobId, item) = next.Value;

                // 批间计数按作业重置：切换到不同作业时归零，保证每个作业的首张立即发送
                // （作业内仍按 batchSize / 批间间隔节流；连续作业之间不再有批间停顿）
                if (!string.Equals(_lastSentJobId, jobId, StringComparison.Ordinal))
                {
                    _lastSentJobId = jobId;
                    Volatile.Write(ref _sendsSinceBatch, 0);
                }

                // 批次节流（发送前暂停）：领取到下一张后、发送前，若已发送数满批次倍数则先延迟
                var settings = _printSettings.Snapshot();
                var sent = Volatile.Read(ref _sendsSinceBatch);
                if (BatchPrintPolicy.ShouldPauseBeforeSend(settings, sent))
                {
                    _logger.LogInformation("批次节流：已发送 {SentCount} 张，暂停 {IntervalMs} 毫秒后继续。", sent, settings.BatchIntervalMs);
                    await Task.Delay(TimeSpan.FromMilliseconds(settings.BatchIntervalMs), _time, stoppingToken);
                }

                using var sendLease = await _queue.TryBeginPrintSendAsync(stoppingToken);
                if (sendLease is null)
                {
                    return;
                }

                _logger.LogInformation("开始打印作业 {JobId} 第 {Index} 张。", jobId, item.Index);
                try
                {
                    // 每次发送前取当前连接（切换后下一张生效；正在发送的这一张不受影响）
                    await _transportManager.CurrentTransport.SendAsync(item.Zpl, stoppingToken);
                    await _queue.CompleteItemAsync(jobId, item.Id, stoppingToken);
                    Volatile.Write(ref _sendsSinceBatch, sent + 1);
                    _logger.LogInformation("作业 {JobId} 第 {Index} 张打印完成。", jobId, item.Index);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // 失败上下文结构化：作业 / 项索引 / 传输插件 / 打印目标 / 错误码，排障可直接检索
                    var connection = _transportManager.CurrentConfig;
                    _logger.LogError(
                        ex,
                        "作业 {JobId} 第 {Index} 张发送失败（插件 {PluginId}，目标 {Target}，错误码 {ErrorCode}）。",
                        jobId,
                        item.Index,
                        connection.PluginId,
                        DescribeTarget(connection),
                        JobErrorCodes.TransportSendFailed);
                    await _queue.FailItemAsync(
                        jobId,
                        item.Id,
                        JobErrorCodes.TransportSendFailed,
                        $"发送到打印机失败：{ex.Message}",
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打印 Worker 异常，稍后重试。");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _time, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
