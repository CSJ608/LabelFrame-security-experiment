using System.Diagnostics;
using LabelFrame.WinHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// HostExitCoordinator（统一退出路径）单元测试（/ ；Issue 事件驱动提前退出）：
/// 「优雅停止 + 稳定窗提前退出 + 限时兜底强退」序列的记账日志（收到退出请求（来源）→ StopApplication 发起 →
/// 宿主停止与否 → 稳定窗观察 / 提前退出 / 宽限超时 → 清理执行 → 最终退出方式）、时序断言
/// （ApplicationStopped 后主流程未返回时 ≤ 稳定窗内清理 + 强退）、订阅时已停止竞态、幂等与异常兜底。
/// 强退动作注入替身（不真正 Environment.Exit），进程内可断言。
/// </summary>
public sealed class HostExitCoordinatorTests
{
    /// <summary>IHostApplicationLifetime 测试替身：记录 StopApplication 调用，可注入异常 / 抑制宿主停止信号。</summary>
    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();
        private int _stopCalls;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public bool ThrowOnStop { get; set; }

        /// <summary>StopApplication 是否发出宿主停止信号（默认发出；置 false 模拟宿主停止本身挂死、ApplicationStopped 永不到来）。</summary>
        public bool StopSignalsStopped { get; set; } = true;

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            Interlocked.Increment(ref _stopCalls);
            if (ThrowOnStop)
            {
                throw new InvalidOperationException("模拟 StopApplication 失败");
            }

            _stopping.Cancel();
            if (StopSignalsStopped)
            {
                _stopped.Cancel();
            }
        }

        public void Dispose()
        {
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly object _logGate = new();

        public List<string> Logs { get; } = [];

        public List<int> Exits { get; } = [];

        public FakeLifetime Lifetime { get; } = new();

        public int CleanupRuns;

        /// <summary>时序敏感用例的等待辅助：轮询记账日志直至目标阶段行出现（CI 满载时看门狗线程调度可达秒级延迟，
        /// 不能以固定睡眠假设序列已推进到目标阶段）。</summary>
        public void WaitForLog(string fragment, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_logGate)
                {
                    if (Logs.Any(l => l.Contains(fragment)))
                    {
                        return;
                    }
                }

                Thread.Sleep(20);
            }

            Assert.Fail($"等待记账阶段行「{fragment}」超时（{timeout.TotalSeconds:0.#} 秒），实际：{string.Join(Environment.NewLine, SnapshotLogs())}");
        }

        public List<string> SnapshotLogs()
        {
            lock (_logGate)
            {
                return Logs.ToList();
            }
        }

        public HostExitCoordinator Create(TimeSpan? gracefulTimeout = null, TimeSpan? stabilizationWindow = null)
            => new(
                message =>
                {
                    lock (_logGate)
                    {
                        Logs.Add(message);
                    }
                },
                gracefulTimeout ?? TimeSpan.FromSeconds(5),
                Exits.Add,
                stabilizationWindow);

        public void AssertLogContains(string fragment)
            => Assert.True(SnapshotLogs().Any(l => l.Contains(fragment)), $"记账日志缺少「{fragment}」，实际：{string.Join(Environment.NewLine, SnapshotLogs())}");

        public void AssertLogNotContains(string fragment)
            => Assert.True(SnapshotLogs().All(l => !l.Contains(fragment)), $"记账日志不应出现「{fragment}」（该行只属宽限兜底路径），实际：{string.Join(Environment.NewLine, SnapshotLogs())}");

        public void Dispose()
        {
            Lifetime.Dispose();
        }
    }

    [Fact]
    public async Task Request_shutdown_should_log_stages_and_stop_application_when_natural_exit_completes()
    {
        var harness = new Harness();
        var coordinator = harness.Create();
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));

        // 主流程在宽限期内自然退出完成（模拟 RunAsync 已返回且清理已执行）——请求发起后再标记，贴合生产时序
        var shutdownTask = coordinator.RequestShutdownAsync("托盘菜单「退出」");
        coordinator.MarkNaturalExitCompleted();

        await shutdownTask;

        Assert.Equal(1, harness.Lifetime.StopCalls);
        harness.AssertLogContains("收到退出请求（来源：托盘菜单「退出」）");
        harness.AssertLogContains("发起 StopApplication");
        harness.AssertLogContains("主流程自然退出完成");
        harness.AssertLogContains("兜底看门狗收手");
        // 自然退出：不强退、强退清理不由此触发（清理由主流程自己经 RunCleanup 执行）
        Assert.Empty(harness.Exits);
        Assert.Equal(0, harness.CleanupRuns);
    }

    [Fact]
    public async Task Request_shutdown_should_force_exit_within_stabilization_window_when_host_stopped_but_main_flow_hung()
    {
        // Issue （时序断言）：宿主停止信号（ApplicationStopped）到来而主流程未返回——
        // 协调器应在稳定窗（默认 500 毫秒）观察期满即进入清理 + 强退，不再烧满宽限兜底。
        // 宽限取 10 秒（远大于稳定窗）作判别尺：若事件驱动路径失效（退化为干等宽限），将出现
        // 「优雅等待超时」记账且耗时 ≈10 秒——路径归属以记账日志互斥判定（「稳定窗提前退出」与
        // 「优雅等待超时」只会出现其一）。时间断言只保留下界（Task.Delay 不会提前，结构性成立）：
        // 观察期必须走满 500 毫秒才可能清理强退；不设上界——CI 满载（2 vCPU + xunit 测试类并行 +
        // 覆盖率收集 + 真实消息循环用例）下线程池调度延迟实测可达 9 秒级，属环境噪声而非被测行为，
        // 本地连续多轮实测退出点稳定在 500~600 毫秒。
        var harness = new Harness();
        var coordinator = harness.Create(
            gracefulTimeout: TimeSpan.FromSeconds(10),
            stabilizationWindow: HostExitCoordinator.DefaultStabilizationWindow);
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));

        var stopwatch = Stopwatch.StartNew();
        // 主流程永不自然退出（RunAsync 僵死场景）；FakeLifetime 在 StopApplication 内同步发出宿主停止信号
        await coordinator.RequestShutdownAsync("HTTP /api/host/shutdown（Web UI 设置页「退出程序」）");
        stopwatch.Stop();

        Assert.Equal(1, harness.Lifetime.StopCalls);
        Assert.Equal(1, harness.CleanupRuns);
        Assert.Equal(0, Assert.Single(harness.Exits));
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(450),
            $"清理 + 强退早于稳定窗（实测 {stopwatch.Elapsed.TotalMilliseconds:0} 毫秒，应 ≥500 毫秒观察期）。");
        harness.AssertLogContains("收到退出请求（来源：HTTP /api/host/shutdown");
        harness.AssertLogContains("宿主已停止（ApplicationStopped）而主流程未返回，进入稳定窗观察（500 毫秒）");
        harness.AssertLogContains("稳定窗（500 毫秒）届满主流程仍未返回，判定卡死——稳定窗提前退出");
        harness.AssertLogContains("执行退出清理（移除托盘图标、释放界面壳）");
        harness.AssertLogContains("退出清理完成");
        harness.AssertLogContains("强制退出：Environment.Exit(0)");
        // 反向锚定：未走宽限兜底（若退化为烧满宽限，此行会出现且耗时 ≈10 秒）
        harness.AssertLogNotContains("优雅等待超时");
    }

    [Fact]
    public async Task Request_shutdown_should_stand_down_when_natural_exit_completes_within_stabilization_window()
    {
        // 稳定窗内主流程自然返回：看门狗收手，不强杀不强退（优雅返回场景不回归）
        var harness = new Harness();
        var coordinator = harness.Create(
            gracefulTimeout: TimeSpan.FromSeconds(10),
            stabilizationWindow: TimeSpan.FromSeconds(5));
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));

        var shutdownTask = coordinator.RequestShutdownAsync("托盘菜单「退出」");
        // 等序列确实进入稳定窗（宿主停止信号已到、观察期开始）后再模拟主流程自然返回——
        // 固定睡眠在 CI 满载（看门狗线程调度延迟秒级）下可能早于序列启动，走到宽限期前的自然退出分支
        harness.WaitForLog("进入稳定窗观察", TimeSpan.FromSeconds(10));
        coordinator.MarkNaturalExitCompleted();
        await shutdownTask;

        Assert.Empty(harness.Exits);
        Assert.Equal(0, harness.CleanupRuns);
        harness.AssertLogContains("宿主已停止（ApplicationStopped）而主流程未返回，进入稳定窗观察（5000 毫秒）");
        harness.AssertLogContains("稳定窗内主流程自然退出，兜底看门狗收手");
    }

    [Fact]
    public async Task Request_shutdown_should_force_exit_promptly_when_host_already_stopped_before_subscription()
    {
        // 竞态：订阅 ApplicationStopped 时宿主已停止（先外部 StopApplication 再发起退出请求）——
        // 「挂回调后补查已置位」应立即命中信号，照常走稳定窗提前退出，而不是退回宽限兜底干等。
        // 判定全以记账日志为准（若竞态检查缺失：信号永不到来 → 干等 10 秒宽限 → 日志为「优雅等待超时」
        // 而非「稳定窗提前退出」）；不设耗时断言——CI 满载下线程池调度延迟实测可达 9 秒级（环境噪声），
        // 提前退出路径本身在挂回调 / 补查即同步命中（Register 在已取消令牌上内联执行回调）。
        var harness = new Harness();
        var coordinator = harness.Create(
            gracefulTimeout: TimeSpan.FromSeconds(10),
            stabilizationWindow: TimeSpan.FromMilliseconds(50));
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));
        harness.Lifetime.StopApplication();

        await coordinator.RequestShutdownAsync("托盘菜单「退出」");

        Assert.Equal(1, harness.CleanupRuns);
        Assert.Equal(0, Assert.Single(harness.Exits));
        harness.AssertLogContains("宿主已停止（ApplicationStopped）而主流程未返回");
        harness.AssertLogContains("稳定窗提前退出");
        harness.AssertLogNotContains("优雅等待超时");
    }

    [Fact]
    public async Task Request_shutdown_should_force_exit_after_graceful_timeout_when_host_stop_never_completes()
    {
        // 宽限兜底保留：宿主停止本身挂死（ApplicationStopped 未在宽限期内到来）——按现状超时清理 + 强退
        var harness = new Harness();
        harness.Lifetime.StopSignalsStopped = false;
        var coordinator = harness.Create(gracefulTimeout: TimeSpan.FromMilliseconds(50));
        coordinator.Bind(harness.Lifetime);
        coordinator.RegisterCleanup(() => Interlocked.Increment(ref harness.CleanupRuns));

        await coordinator.RequestShutdownAsync("托盘菜单「退出」");

        Assert.Equal(1, harness.Lifetime.StopCalls);
        Assert.Equal(1, harness.CleanupRuns);
        Assert.Equal(0, Assert.Single(harness.Exits));
        harness.AssertLogContains("优雅等待超时（0.05 秒");
        harness.AssertLogContains("强制退出：Environment.Exit(0)");
    }

    [Fact]
    public async Task Request_shutdown_should_be_idempotent_for_duplicate_requests()
    {
        var harness = new Harness();
        var coordinator = harness.Create();
        coordinator.Bind(harness.Lifetime);
        coordinator.MarkNaturalExitCompleted();

        await coordinator.RequestShutdownAsync("托盘菜单「退出」");
        await coordinator.RequestShutdownAsync("HTTP /api/host/shutdown");

        Assert.Equal(1, harness.Lifetime.StopCalls);
        Assert.Empty(harness.Exits);
        harness.AssertLogContains("已忽略——退出流程进行中");
    }

    [Fact]
    public async Task Request_shutdown_without_bound_lifetime_should_still_force_exit()
    {
        var harness = new Harness();
        var coordinator = harness.Create(gracefulTimeout: TimeSpan.FromMilliseconds(50));

        // 未 Bind（异常装配场景）：跳过 StopApplication，仍走清理与强退兜底
        await coordinator.RequestShutdownAsync("托盘菜单「退出」");

        Assert.Equal(0, harness.Lifetime.StopCalls);
        Assert.Equal(0, Assert.Single(harness.Exits));
        harness.AssertLogContains("宿主生命周期未绑定");
        harness.AssertLogContains("强制退出：Environment.Exit(0)");
    }

    [Fact]
    public async Task Request_shutdown_should_still_force_exit_when_stop_application_throws()
    {
        var harness = new Harness();
        harness.Lifetime.ThrowOnStop = true;
        var coordinator = harness.Create(gracefulTimeout: TimeSpan.FromMilliseconds(50));
        coordinator.Bind(harness.Lifetime);

        await coordinator.RequestShutdownAsync("托盘菜单「退出」");

        Assert.Equal(1, harness.Lifetime.StopCalls);
        Assert.Equal(0, Assert.Single(harness.Exits));
        harness.AssertLogContains("StopApplication 异常");
        harness.AssertLogContains("强制退出：Environment.Exit(0)");
    }

    [Fact]
    public void Run_cleanup_should_be_idempotent()
    {
        var harness = new Harness();
        var coordinator = harness.Create();
        var runs = 0;
        coordinator.RegisterCleanup(() => runs++);

        coordinator.RunCleanup();
        coordinator.RunCleanup();

        Assert.Equal(1, runs);
        harness.AssertLogContains("执行退出清理");
    }

    [Fact]
    public void Run_cleanup_should_log_and_continue_when_cleanup_throws()
    {
        var harness = new Harness();
        var coordinator = harness.Create();
        coordinator.RegisterCleanup(() => throw new InvalidOperationException("模拟清理失败"));

        coordinator.RunCleanup();

        harness.AssertLogContains("退出清理异常（忽略，继续退出流程）");
    }
}
