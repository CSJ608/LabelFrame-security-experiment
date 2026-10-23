using Microsoft.Extensions.Hosting;

namespace LabelFrame.WinHost;

/// <summary>
/// 宿主统一退出协调器：托盘「退出」与 /api/host/shutdown 共用「优雅停止 + 事件驱动提前退出 + 限时兜底强退」——
/// 宿主已停止而主流程未返回（托盘 / 界面消息循环卡死）时不再烧满宽限，仅留稳定窗即清理强退；
/// 两条退出入口不得有强弱退差异；各阶段写 host.log 记账。
/// </summary>
public sealed class HostExitCoordinator
{
    /// <summary>优雅等待上限（宽限兜底；仅兜底极端挂死，不决定常见路径耗时）。</summary>
    public static readonly TimeSpan DefaultGracefulTimeout = TimeSpan.FromSeconds(2.5);

    /// <summary>宿主已停止后的主流程观察稳定窗（卡死判定缓冲；500 毫秒常量，不打配置面）。</summary>
    public static readonly TimeSpan DefaultStabilizationWindow = TimeSpan.FromMilliseconds(500);

    private readonly Action<string> _log;
    private readonly TimeSpan _gracefulTimeout;
    private readonly TimeSpan _stabilizationWindow;
    private readonly Action<int> _forceExit;
    private readonly TaskCompletionSource _naturalExitCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _cleanupGate = new();
    private IHostApplicationLifetime? _lifetime;
    private Action? _cleanup;
    private bool _cleanupDone;
    private int _shutdownRequested;

    public bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;

    /// <param name="log">host.log 记账回调。</param>
    /// <param name="gracefulTimeout">优雅等待上限（默认 <see cref="DefaultGracefulTimeout"/>）。</param>
    /// <param name="forceExit">强退动作（默认 Environment.Exit；测试注入替身以便进程内观测）。</param>
    /// <param name="stabilizationWindow">宿主停止后的主流程观察稳定窗（默认 <see cref="DefaultStabilizationWindow"/>；测试注入取值以便时序断言）。</param>
    public HostExitCoordinator(Action<string> log, TimeSpan? gracefulTimeout = null, Action<int>? forceExit = null, TimeSpan? stabilizationWindow = null)
    {
        _log = log;
        _gracefulTimeout = gracefulTimeout ?? DefaultGracefulTimeout;
        _forceExit = forceExit ?? Environment.Exit;
        _stabilizationWindow = stabilizationWindow ?? DefaultStabilizationWindow;
    }

    /// <summary>绑定宿主生命周期（应用 Build 后由装配方调用一次；退出请求据此发起 StopApplication）。</summary>
    public void Bind(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    /// <summary>
    /// 注册退出清理（托盘 WM_QUIT 投递、界面壳 Dispose 等）。
    /// 自然退出路径（Main 的 finally）与强退兜底路径经 <see cref="RunCleanup"/> 共用同一清理，幂等执行。
    /// </summary>
    public void RegisterCleanup(Action cleanup)
    {
        _cleanup = cleanup;
    }

    /// <summary>
    /// 收到退出请求（来源：托盘菜单 / HTTP）。幂等：首个请求执行「优雅停止 + 提前退出 + 兜底强退」序列，
    /// 后续请求只记账不重复执行。返回的 Task 在序列结束时完成（强退路径实际不会完成——进程已退出）。
    /// </summary>
    /// <param name="source">请求来源（写入 host.log 记账）。</param>
    /// <param name="delayBeforeStop">停止宿主前的缓冲（HTTP 路径让响应先送达客户端；默认无）。</param>
    public Task RequestShutdownAsync(string source, TimeSpan delayBeforeStop = default)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 1)
        {
            _log($"退出请求（来源：{source}）已忽略——退出流程进行中。");
            return Task.CompletedTask;
        }

        return Task.Run(() => ShutdownSequenceAsync(source, delayBeforeStop));
    }

    /// <summary>主流程自然退出完成（RunAsync 已返回且清理已执行，即将退出进程）——兜底看门狗据此收手。</summary>
    public void MarkNaturalExitCompleted()
    {
        _naturalExitCompleted.TrySetResult();
        if (Volatile.Read(ref _shutdownRequested) == 1)
        {
            _log("主流程自然退出完成（RunAsync 已返回、清理已执行）。");
        }
    }

    /// <summary>执行注册的退出清理（幂等；自然退出与强退兜底共用，并发调用会等待首次执行完成）。</summary>
    public void RunCleanup()
    {
        lock (_cleanupGate)
        {
            if (_cleanupDone)
            {
                return;
            }

            _cleanupDone = true;
            var cleanup = _cleanup;
            _cleanup = null;
            _log("执行退出清理（移除托盘图标、释放界面壳）…");
            try
            {
                cleanup?.Invoke();
                _log("退出清理完成。");
            }
            catch (Exception ex)
            {
                _log($"退出清理异常（忽略，继续退出流程）：{ex}");
            }
        }
    }

    private async Task ShutdownSequenceAsync(string source, TimeSpan delayBeforeStop)
    {
        _log($"收到退出请求（来源：{source}）。");
        if (delayBeforeStop > TimeSpan.Zero)
        {
            await Task.Delay(delayBeforeStop).ConfigureAwait(false);
        }

        // 订阅 ApplicationStopped 作为「宿主已停止」信号：先挂回调再查已置位，
        // 覆盖「订阅时宿主已停止」的竞态（两者任一命中都置位）
        var lifetime = _lifetime;
        var hostStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedRegistration = default(CancellationTokenRegistration);
        if (lifetime is not null)
        {
            stoppedRegistration = lifetime.ApplicationStopped.Register(() => hostStopped.TrySetResult());
            if (lifetime.ApplicationStopped.IsCancellationRequested)
            {
                hostStopped.TrySetResult();
            }
        }

        try
        {
            if (lifetime is null)
            {
                _log("宿主生命周期未绑定，跳过 StopApplication（直接进入清理与强退兜底）。");
            }
            else
            {
                try
                {
                    _log("发起 StopApplication（停止宿主与全部后台服务）。");
                    lifetime.StopApplication();
                }
                catch (Exception ex)
                {
                    _log($"StopApplication 异常（继续走退出兜底）：{ex}");
                }
            }

            var naturalExit = _naturalExitCompleted.Task;
            var gracePeriod = Task.Delay(_gracefulTimeout);
            // 自然退出优先于宿主停止信号（两者均已完成时按参数序取 naturalExit）；
            // 宽限计时与宿主停止信号并行
            var finished = await Task.WhenAny(naturalExit, hostStopped.Task, gracePeriod).ConfigureAwait(false);
            if (finished == naturalExit)
            {
                _log("主流程自然退出确认，兜底看门狗收手（退出收尾由主流程完成）。");
                return;
            }

            if (finished == hostStopped.Task)
            {
                // 宿主已停止而主流程（RunAsync）仍未返回（托盘 / 界面消息循环卡死场景）：
                // 不再烧满固定宽限，仅留稳定窗观察——期间自然退出则看门狗收手，
                // 届满仍未返回即判定卡死，提前执行清理与强退
                _log($"宿主已停止（ApplicationStopped）而主流程未返回，进入稳定窗观察（{_stabilizationWindow.TotalMilliseconds:0} 毫秒）。");
                var stabilized = await Task.WhenAny(naturalExit, Task.Delay(_stabilizationWindow)).ConfigureAwait(false);
                if (stabilized == naturalExit)
                {
                    _log("稳定窗内主流程自然退出，兜底看门狗收手（退出收尾由主流程完成）。");
                    return;
                }

                _log($"稳定窗（{_stabilizationWindow.TotalMilliseconds:0} 毫秒）届满主流程仍未返回，判定卡死——稳定窗提前退出：执行清理后强制退出。");
                RunCleanup();
                _log("强制退出：Environment.Exit(0)。");
                _forceExit(0);
                return;
            }

            // 宽限期内既未自然返回也未收到 ApplicationStopped（宿主停止本身挂死）：
            // 显式清理（托盘 NIM_DELETE / 界面壳释放）后强制退出，进程不再僵死、图标不再残留
            _log($"优雅等待超时（{_gracefulTimeout.TotalSeconds:0.##} 秒，RunAsync 未返回或清理未完成），执行清理后强制退出。");
            RunCleanup();
            _log("强制退出：Environment.Exit(0)。");
            _forceExit(0);
        }
        finally
        {
            stoppedRegistration.Dispose();
        }
    }
}
