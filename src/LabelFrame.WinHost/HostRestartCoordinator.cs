using LabelFrame.Core.Jobs;

namespace LabelFrame.WinHost;

/// <summary>安全重启的准备、启动等待器与退出协调。</summary>
public sealed class HostRestartCoordinator(HostActivityState activity, LabelJobQueue queue, HostExitCoordinator exit) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Func<bool, Task>? _prepareLauncher;
    private bool _requested;

    public void Bind(Func<bool, Task> prepareLauncher) => _prepareLauncher = prepareLauncher;
    public void Dispose() => _gate.Dispose();

    public async Task<string?> RequestAsync(bool showPlugins, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_requested) return null;
            if (activity.IsPrepared || exit.IsShutdownRequested) return "客户端正在停止或维护，请稍后重试。";
            if (_prepareLauncher is null) return "当前运行方式不支持重启，请退出后手动启动客户端。";
            if (!activity.TryPrepare(out var reason)) return reason;
            var queuePrepared = false;
            try
            {
                queuePrepared = await queue.TryPrepareForRestartAsync(cancellationToken);
                if (!queuePrepared) return "打印任务尚未完成，请完成或取消剩余任务后再重启。";
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (!activity.IsUiFrozen(out reason))
                {
                    if (reason is not null) return reason;
                    if (DateTime.UtcNow >= deadline) return "部分界面未确认暂停编辑，请关闭或刷新其他客户端页面后重试。";
                    await Task.Delay(50, cancellationToken);
                }
                if (reason is not null) return reason;
                await _prepareLauncher(showPlugins);
                _requested = true;
                _ = exit.RequestShutdownAsync("重启客户端", TimeSpan.FromMilliseconds(300));
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return $"无法准备重启，客户端继续运行：{ex.Message}";
            }
            finally
            {
                if (!queuePrepared || !exit.IsShutdownRequested)
                {
                    if (queuePrepared) await queue.CancelShutdownPreparationAsync(CancellationToken.None);
                    activity.CancelPreparation();
                }
            }
        }
        finally { _gate.Release(); }
    }
}
