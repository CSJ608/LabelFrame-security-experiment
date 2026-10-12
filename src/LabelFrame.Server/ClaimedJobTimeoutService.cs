namespace LabelFrame.Server;

/// <summary>
/// Claimed 作业超时回收扫描：把领取后超过超时时长仍未回报终态的 Claimed 作业回收为 Failed
/// （宿主失联超时，LF_SRV_009），保证宿主崩溃 / 重启（内存映射丢失）后作业不再永久停留 Claimed。
/// 与 PendingJobExpirationService 同构：状态转移任务（周期分钟级），不做数据删除；超时判定只以
/// claimed_at + 服务端时钟为准，与设备在线状态无关（宿主重启后照常心跳，「在线」无法区分映射丢失
/// 与正常打印）。回收不重新投递；迟到的真实回报按幂等重放返回既有终态。
/// </summary>
public sealed partial class ClaimedJobTimeoutService : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Claimed 作业超时回收扫描失败。")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Claimed 作业超时回收扫描完成：回收 Failed {Count} 条。")]
    private static partial void LogScanCompleted(ILogger logger, int count);

    private readonly ServerDb _db;
    private readonly ServerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ClaimedJobTimeoutService> _logger;

    public ClaimedJobTimeoutService(ServerDb db, ServerOptions options, TimeProvider timeProvider, ILogger<ClaimedJobTimeoutService> logger)
    {
        _db = db;
        _options = options;
        _time = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 超时回收关闭（0 / 负值）= 沿用现状（Claimed 永不超时），任务直接退出
        if (_options.ClaimedJobTimeout is not { } timeout)
        {
            return;
        }

        // 启动后延迟 30 秒执行首次扫描（覆盖停机期间失联的作业），之后按扫描周期执行；
        // 延迟本身不参与超时判定（判定时钟在 ScanOnceAsync 内取 TimeProvider），用系统延迟即可
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

    /// <summary>执行一次扫描：把 claimed_at 早于「当前时间 - 超时时长」的 Claimed 作业回收为 Failed。</summary>
    public async Task<int> ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ClaimedJobTimeout is not { } timeout)
        {
            return 0;
        }

        var now = _time.GetUtcNow();
        var reason = $"宿主失联超时（{ServerErrorCodes.HostLostTimeout}）：领取后超过 {timeout.TotalMinutes:0.##} 分钟未回报终态，服务端已按失败回收（结果未知，可能已实际打印）；需重打请用新 requestId 重发，不会自动重新投递。";
        // error_code 落库：终态失败原因可按码追溯（error_message 中文文案维持不变）
        var count = await _db.MarkTimedOutClaimedJobsAsync(now, now - timeout, reason, ServerErrorCodes.HostLostTimeout, cancellationToken);

        // 终态回调登记（三处终态转移点之一）：失联回收同样通知调用方（不漏报），幂等登记
        await _db.EnqueueJobCallbacksAsync(now, cancellationToken: cancellationToken);
        LogScanCompleted(_logger, count);
        return count;
    }
}
