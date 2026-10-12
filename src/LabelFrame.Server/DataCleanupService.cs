using LabelFrame.Core.Logs;

namespace LabelFrame.Server;

/// <summary>历史数据定期清理：删除超过保留期的终态作业与设备日志。</summary>
public sealed partial class DataCleanupService : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Error, Message = "历史数据清理失败。")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "历史数据清理完成：删除终态作业 {JobCount} 条、日志 {LogCount} 条。")]
    private static partial void LogCleanupCompleted(ILogger logger, int jobCount, int logCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "日志库量闸执行：按大小删除日志 {LogCount} 条。")]
    private static partial void LogSizeGateCompleted(ILogger logger, int logCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "日志库空间回收异常：{Warning}")]
    private static partial void LogVacuumFailed(ILogger logger, string warning);

    private readonly ServerDb _db;
    private readonly SqliteLogStore _logStore;
    private readonly ServerOptions _options;
    private readonly ILogger<DataCleanupService> _logger;

    public DataCleanupService(ServerDb db, SqliteLogStore logStore, ServerOptions options, ILogger<DataCleanupService> logger)
    {
        _db = db;
        _logStore = logStore;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后延迟 60 秒执行一次，之后按 CleanupIntervalHours 周期执行（：首清在延迟后立即发生
        // 不等首个周期 tick——否则默认周期下升级 / 重启后最长 24h 不做任何清理，量闸同步失效）
        var interval = TimeSpan.FromHours(Math.Max(1, _options.CleanupIntervalHours));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            await CleanupAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LogCleanupFailed(_logger, ex);
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogCleanupFailed(_logger, ex);
            }
        }
    }

    /// <summary>执行一次清理：终态作业按 JobRetentionDays、日志按 LogRetentionDays；
    /// 随后按量闸（LogsDbMaxSizeMB，0 或负值 = 不限）删最旧日志直至库文件回到阈值内——不受保留期下限约束。</summary>
    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var jobCutoff = now.AddDays(-Math.Max(0, _options.JobRetentionDays));
        var jobDeleted = await _db.DeleteTerminalJobsBeforeAsync(jobCutoff, cancellationToken);
        var logCutoff = now.AddDays(-Math.Max(0, _options.LogRetentionDays));
        var logDeleted = await _logStore.DeleteBeforeAsync(logCutoff, cancellationToken);
        LogCleanupCompleted(_logger, jobDeleted, logDeleted);

        if (_options.LogsDbMaxSizeMB > 0)
        {
            var sizeGate = await _logStore.EnforceSizeLimitAsync(
                (long)_options.LogsDbMaxSizeMB * 1024 * 1024, cancellationToken);
            if (sizeGate.DeletedRows > 0)
            {
                LogSizeGateCompleted(_logger, sizeGate.DeletedRows);
            }

            if (sizeGate.VacuumWarning is { } warning)
            {
                // VACUUM / 空库仍超阈等异常经 ILogger 记 Error（服务宿主可能无控制台， 留观项）
                LogVacuumFailed(_logger, warning);
            }
        }
    }
}
