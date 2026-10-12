namespace LabelFrame.Server;

/// <summary>
/// Pending 作业过期扫描：把超过暂存 TTL 仍未投递的 Pending 作业标记为 Expired 终态，
/// 保证作业历史可见（不永久显示 Pending）。与 DataCleanupService 分离——清理是删除历史数据
/// （周期小时级），过期是状态转移（周期分钟级即可及时可见）；正确性由领取查询的 TTL 过滤兜底，
/// 不依赖本扫描周期。
/// </summary>
public sealed partial class PendingJobExpirationService : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Pending 作业过期扫描失败。")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pending 作业过期扫描完成：标记 Expired {Count} 条。")]
    private static partial void LogScanCompleted(ILogger logger, int count);

    private readonly ServerDb _db;
    private readonly ServerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PendingJobExpirationService> _logger;

    public PendingJobExpirationService(ServerDb db, ServerOptions options, TimeProvider timeProvider, ILogger<PendingJobExpirationService> logger)
    {
        _db = db;
        _options = options;
        _time = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // TTL 关闭（0 / 负值）= 不做过期（行为与现状一致），任务直接退出
        if (_options.PendingJobTtl is not { } ttl)
        {
            return;
        }

        // 启动后延迟 30 秒执行首次扫描（覆盖停机期间超期的作业），之后按扫描周期执行；
        // 延迟本身不参与过期判定（判定时钟在 ScanOnceAsync 内取 TimeProvider），用系统延迟即可
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _options.ExpirationScanIntervalMinutes)), _time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogScanFailed(_logger, ex);
            }
        }
    }

    /// <summary>执行一次扫描：把 CreatedAt 早于「当前时间 - TTL」的 Pending 作业标记为 Expired。</summary>
    public async Task<int> ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_options.PendingJobTtl is not { } ttl)
        {
            return 0;
        }

        var now = _time.GetUtcNow();
        var reason = $"暂存超过 {ttl.TotalHours:0.##} 小时未投递，服务端已放弃；需重打请用新 requestId 重发。";
        // error_code 落库（③ /）：终态放弃原因可按码追溯（error_message 中文文案维持不变）
        var count = await _db.MarkExpiredJobsAsync(now, now - ttl, reason, ServerErrorCodes.PendingExpired, cancellationToken);

        // 终态回调登记（三处终态转移点之一）：Expired 同样通知调用方（不漏报），幂等登记
        await _db.EnqueueJobCallbacksAsync(now, cancellationToken: cancellationToken);
        LogScanCompleted(_logger, count);
        return count;
    }
}
