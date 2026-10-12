using LabelFrame.Bootstrapper.Wizard;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>
/// WizardForegroundActivator（向导首显主动置前器）单元测试（单测部分）：
/// 组合置前调用时序——已在前台零扰动；未在前台时「线程挂接 → 最小化 → 还原 → 会话切换 → 前置请求 → 解除挂接」，
/// 置前被拒转任务栏强闪烁兜底（FLASHW_ALL | FLASHW_TIMERNOFG）；同线程 / 挂接失败 / 无前台窗口的防御分支。
/// 断言形态对齐 WinHost TrayMenuPresenterTests 先例：全 Win32 调用点注入替身，录制调用序列逐条比对。
/// </summary>
public sealed class WizardForegroundActivatorTests
{
    private static IntPtr Window => new(0x3E8);   // 目标窗口哨兵句柄
    private static IntPtr Other => new(0x7D0);    // 他人前台窗口哨兵句柄
    private const uint OwnThread = 0x64;          // 目标窗口线程 id
    private const uint ForegroundThread = 0xC8;   // 他人前台线程 id

    /// <summary>前台查询替身：按序返回预设队列（首轮探测 → 组合后复核），耗尽后返回末值。</summary>
    private sealed class ForegroundQueue
    {
        private readonly Queue<IntPtr> _values;

        public ForegroundQueue(params IntPtr[] values) => _values = new Queue<IntPtr>(values);

        public IntPtr Next()
        {
            lock (this)
            {
                return _values.Count > 1 ? _values.Dequeue() : _values.Peek();
            }
        }
    }

    /// <summary>录制组合拳调用序列的替身包（成功形态：挂接成功、前置请求返回 true）。</summary>
    private sealed class RecordingCalls
    {
        public List<string> Items { get; } = [];

        public WizardForegroundActivator Build(
            ForegroundQueue foreground,
            bool attachResult = true,
            bool setForegroundResult = true,
            uint ownThread = OwnThread,
            uint foregroundThread = ForegroundThread)
        {
            return new WizardForegroundActivator(
                getForegroundWindow: () => foreground.Next(),
                getWindowThread: window =>
                {
                    var thread = window == Window ? ownThread : foregroundThread;
                    Items.Add($"GetWindowThread({window})=0x{thread:X}");
                    return thread;
                },
                attachThreadInput: (attach, attachTo, attachFlag) =>
                {
                    Items.Add($"AttachThreadInput(0x{attach:X}, 0x{attachTo:X}, {attachFlag})={attachResult}");
                    return attachResult;
                },
                showWindow: (window, command) =>
                {
                    Items.Add($"ShowWindow({window}, {command})");
                    return true;
                },
                switchToThisWindow: (window, altTab) =>
                {
                    Items.Add($"SwitchToThisWindow({window}, altTab={altTab})");
                },
                setForegroundWindow: window =>
                {
                    Items.Add($"SetForegroundWindow({window})={setForegroundResult}");
                    return setForegroundResult;
                },
                flashWindowEx: (window, flags, count) =>
                {
                    Items.Add($"FlashWindowEx({window}, flags=0x{flags:X}, count={count})");
                    return true;
                });
        }
    }

    [Fact]
    public void Activate_when_already_foreground_returns_without_disturbance()
    {
        // 正例锚点：正常链（双击且未切走）首显即前台——除首次探测外零调用零扰动（不闪烁、不最小化）
        var calls = new RecordingCalls();
        var activator = calls.Build(new ForegroundQueue(Window));

        var outcome = activator.Activate(Window);

        Assert.Equal(WizardForegroundOutcome.AlreadyForeground, outcome);
        Assert.Empty(calls.Items); // 前台探测不计入组合拳序列（内部早退），无任何窗口操作副作用
    }

    [Fact]
    public void Activate_runs_combined_strategy_in_order_and_takes_foreground()
    {
        // 组合拳全序列：挂接 → 最小化 → 还原 → 切换 → 前置请求 → 解除挂接 → 前台复核，无闪烁
        var calls = new RecordingCalls();
        var activator = calls.Build(new ForegroundQueue(Other, Window));

        var outcome = activator.Activate(Window);

        Assert.Equal(WizardForegroundOutcome.Activated, outcome);
        Assert.Equal(
            new[]
            {
                $"GetWindowThread({Other})=0x{ForegroundThread:X}",                              // ① 探测前台线程
                $"GetWindowThread({Window})=0x{OwnThread:X}",                                   //    与本窗口线程
                $"AttachThreadInput(0x{OwnThread:X}, 0x{ForegroundThread:X}, True)=True",       // ② 输入队列挂接（挂接方 = 本线程）
                $"ShowWindow({Window}, 6)",                                                     // ③ SW_MINIMIZE
                $"ShowWindow({Window}, 9)",                                                     // ④ SW_RESTORE（前台授予关键拳）
                $"SwitchToThisWindow({Window}, altTab=True)",                                   // ⑤ 会话切换
                $"SetForegroundWindow({Window})=True",                                          // ⑥ 前置请求
                $"AttachThreadInput(0x{OwnThread:X}, 0x{ForegroundThread:X}, False)=True",      // ⑦ 解除挂接（finally）
            },
            calls.Items);
        Assert.DoesNotContain(calls.Items, item => item.StartsWith("FlashWindowEx")); // 成功不触发兜底
    }

    [Fact]
    public void Activate_rejected_request_falls_back_to_taskbar_flash()
    {
        // 兜底形态（备选结论）：前置请求被拒且前台复核仍非本窗 → FLASHW_ALL | FLASHW_TIMERNOFG（0x0F）强闪烁
        // 解除挂接不因失败缺席（finally）
        var calls = new RecordingCalls();
        var activator = calls.Build(new ForegroundQueue(Other, Other), setForegroundResult: false);

        var outcome = activator.Activate(Window);

        Assert.Equal(WizardForegroundOutcome.FlashFallback, outcome);
        Assert.Equal($"FlashWindowEx({Window}, flags=0xF, count=8)", calls.Items[^1]);
        Assert.Contains($"AttachThreadInput(0x{OwnThread:X}, 0x{ForegroundThread:X}, False)=True", calls.Items);
    }

    [Fact]
    public void Activate_request_true_but_foreground_still_other_reports_activated()
    {
        // 前置请求返回 true 即认定成功（SetForegroundWindow 语义：已交付前台），不再闪烁；复核探测因短路不发生
        var calls = new RecordingCalls();
        var activator = calls.Build(new ForegroundQueue(Other, Other), setForegroundResult: true);

        var outcome = activator.Activate(Window);

        Assert.Equal(WizardForegroundOutcome.Activated, outcome);
        Assert.DoesNotContain(calls.Items, item => item.StartsWith("FlashWindowEx"));
    }

    [Fact]
    public void Activate_same_thread_skips_attach_but_runs_combo()
    {
        // 前台窗口与本窗口同线程（自家窗口）：无需挂接 / 解除，组合拳其余动作照常
        var calls = new RecordingCalls();
        var activator = calls.Build(new ForegroundQueue(Other, Window), ownThread: ForegroundThread, foregroundThread: ForegroundThread);

        var outcome = activator.Activate(Window);

        Assert.Equal(WizardForegroundOutcome.Activated, outcome);
        Assert.DoesNotContain(calls.Items, item => item.StartsWith("AttachThreadInput"));
        Assert.Contains($"ShowWindow({Window}, 6)", calls.Items);
        Assert.Contains($"ShowWindow({Window}, 9)", calls.Items);
    }

    [Fact]
    public void Activate_attach_failure_still_attempts_combo_and_skips_detach()
    {
        // 挂接失败（他线程拒绝共享输入队列）：组合拳照常尝试（最小化还原一拳独立于挂接），失败挂接不解除（finally 守卫）
        var calls = new RecordingCalls();
        var activator = calls.Build(new ForegroundQueue(Other, Window), attachResult: false);

        var outcome = activator.Activate(Window);

        Assert.Equal(WizardForegroundOutcome.Activated, outcome);
        Assert.Contains($"AttachThreadInput(0x{OwnThread:X}, 0x{ForegroundThread:X}, True)=False", calls.Items);
        Assert.Equal(1, calls.Items.Count(item => item.StartsWith("AttachThreadInput"))); // 仅一次挂接尝试，无解除（失败挂接不可解除）
    }

    [Fact]
    public void Activate_without_foreground_window_runs_combo_without_attach()
    {
        // 无前台窗口（句柄为零，如切换真空期）：无从挂接，组合拳照常
        var calls = new RecordingCalls();
        var activator = calls.Build(new ForegroundQueue(IntPtr.Zero, Window));

        var outcome = activator.Activate(Window);

        Assert.Equal(WizardForegroundOutcome.Activated, outcome);
        Assert.DoesNotContain(calls.Items, item => item.StartsWith("AttachThreadInput"));
        Assert.Contains($"SetForegroundWindow({Window})=True", calls.Items);
    }
}
