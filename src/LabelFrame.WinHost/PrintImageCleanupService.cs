using Microsoft.Extensions.Hosting;

namespace LabelFrame.WinHost;

/// <summary>在宿主启动及空闲期间周期清理受管模拟图片。</summary>
internal sealed class PrintImageCleanupService(PrintImageRetentionCleaner cleaner, TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        cleaner.CleanupExpired();
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) cleaner.CleanupExpired();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 宿主退出时结束定时等待。
        }
    }
}
