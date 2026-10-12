using System.Runtime.InteropServices;

namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>向导首显主动置前结果。</summary>
public enum WizardForegroundOutcome
{
    /// <summary>窗体已在前台（启动链前台授予成立，系统已交付）——零调用零扰动。</summary>
    AlreadyForeground,

    /// <summary>组合置前成功：窗体已取得前台。</summary>
    Activated,

    /// <summary>组合置前被拒（前台锁未授予）——已触发任务栏强闪烁兜底（闪至成为前台）。</summary>
    FlashFallback,
}

/// <summary>
/// 向导首显主动置前器：安装引导 BA 的窗口此前完全依赖系统默认前台授予链
/// 启动者无前台权（计划任务 / 后台进程触发、Explorer 未持前台、下载后立即切走）时向导静默不前置，
/// 用户以为安装无响应（复现 S1 / S3 / S3b；S3c 预试证明单一 <c>SetForegroundWindow</c>——
/// 即使配合 <see cref="AttachThreadInput"/>——也会被前台锁拒绝）。
/// 本类实现用户拍板的组合拳（选项）：已在前台则零扰动；否则
/// <see cref="AttachThreadInput"/>（本 UI 线程挂接当前前台线程输入队列）→
/// <see cref="ShowWindow"/>(SW_MINIMIZE→SW_RESTORE)（还原动作的前台授予独立于置前配额）→
/// <see cref="SwitchToThisWindow"/> → <see cref="SetForegroundWindow"/>，仍被拒时
/// <see cref="FlashWindowEx"/>(FLASHW_ALL | FLASHW_TIMERNOFG) 任务栏强闪烁兜底（闪至前置，自停）。
/// 线程 / 窗口前置 / 闪烁三类共七个 Win32 调用点全部可注入替身，
/// 供单元测试断言调用时序（单测部分），对齐 WinHost <c>TrayMenuPresenter</c> 可测性先例。
/// </summary>
public sealed class WizardForegroundActivator
{
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const uint FlashwAll = 0x00000003;       // FLASHW_CAPTION | FLASHW_TRAY
    private const uint FlashwTimerNoFg = 0x0000000C; // FLASHW_TIMERNOFG：闪至成为前台（闪烁自停，兜底「强提示可感知」口径）
    private const uint FlashCount = 8;               // TIMERNOFG 下系统忽略计数，仅保形（参数形状稳定）

    /// <summary>前台窗口查询（Win32 GetForegroundWindow 形态）。</summary>
    public delegate IntPtr GetForegroundWindowProc();

    /// <summary>窗口线程查询（Win32 GetWindowThreadProcessId 的线程 id 投影）。</summary>
    public delegate uint GetWindowThreadProc(IntPtr window);

    /// <summary>输入队列挂接（Win32 AttachThreadInput 形态）。</summary>
    public delegate bool AttachThreadInputProc(uint idAttach, uint idAttachTo, bool attach);

    /// <summary>窗口显示状态切换（Win32 ShowWindow 形态）。</summary>
    public delegate bool ShowWindowProc(IntPtr window, int command);

    /// <summary>会话切换（Win32 SwitchToThisWindow 形态）。</summary>
    public delegate void SwitchToThisWindowProc(IntPtr window, bool altTab);

    /// <summary>窗口前置请求（Win32 SetForegroundWindow 形态）。</summary>
    public delegate bool SetForegroundWindowProc(IntPtr window);

    /// <summary>任务栏 / 标题闪烁（Win32 FlashWindowEx 的窗口级投影）。</summary>
    public delegate bool FlashWindowExProc(IntPtr window, uint flags, uint count);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr window, bool altTab);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashwInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashwInfo info);

    private readonly GetForegroundWindowProc _getForegroundWindow;
    private readonly GetWindowThreadProc _getWindowThread;
    private readonly AttachThreadInputProc _attachThreadInput;
    private readonly ShowWindowProc _showWindow;
    private readonly SwitchToThisWindowProc _switchToThisWindow;
    private readonly SetForegroundWindowProc _setForegroundWindow;
    private readonly FlashWindowExProc _flashWindowEx;

    /// <summary>
    /// 创建置前器。
    /// </summary>
    /// <param name="getForegroundWindow">前台窗口查询（默认 Win32 GetForegroundWindow；测试注入替身）。</param>
    /// <param name="getWindowThread">窗口线程查询（默认 Win32 GetWindowThreadProcessId；测试注入替身）。</param>
    /// <param name="attachThreadInput">输入队列挂接（默认 Win32 AttachThreadInput；测试注入替身）。</param>
    /// <param name="showWindow">窗口显示状态切换（默认 Win32 ShowWindow；测试注入替身）。</param>
    /// <param name="switchToThisWindow">会话切换（默认 Win32 SwitchToThisWindow；测试注入替身）。</param>
    /// <param name="setForegroundWindow">窗口前置请求（默认 Win32 SetForegroundWindow；测试注入替身）。</param>
    /// <param name="flashWindowEx">任务栏 / 标题闪烁（默认 Win32 FlashWindowEx；测试注入替身）。</param>
    public WizardForegroundActivator(
        GetForegroundWindowProc? getForegroundWindow = null,
        GetWindowThreadProc? getWindowThread = null,
        AttachThreadInputProc? attachThreadInput = null,
        ShowWindowProc? showWindow = null,
        SwitchToThisWindowProc? switchToThisWindow = null,
        SetForegroundWindowProc? setForegroundWindow = null,
        FlashWindowExProc? flashWindowEx = null)
    {
        _getForegroundWindow = getForegroundWindow ?? GetForegroundWindow;
        _getWindowThread = getWindowThread ?? GetWindowThreadId;
        _attachThreadInput = attachThreadInput ?? AttachThreadInput;
        _showWindow = showWindow ?? ShowWindow;
        _switchToThisWindow = switchToThisWindow ?? SwitchToThisWindow;
        _setForegroundWindow = setForegroundWindow ?? SetForegroundWindow;
        _flashWindowEx = flashWindowEx ?? FlashWindow;
    }

    /// <summary>
    /// 置前结果中文描述（Burn 日志 / 诊断输出用）。
    /// </summary>
    public static string Describe(WizardForegroundOutcome outcome) => outcome switch
    {
        WizardForegroundOutcome.AlreadyForeground => "已在前台（系统授予链成立，零扰动）",
        WizardForegroundOutcome.Activated => "组合置前成功（挂接 + 最小化还原 + 切换 + 前置请求）",
        _ => "组合置前被前台锁拒绝，已转任务栏强闪烁兜底（闪至前置）",
    };

    /// <summary>
    /// 主动置前指定窗口（须在该窗口的 UI 线程上调用——<see cref="AttachThreadInputProc"/> 挂接的是调用线程的输入队列）。
    /// 正常链（用户双击且未切走）下窗体首显即在前台，本方法零调用零扰动（不回归的保证）。
    /// </summary>
    /// <param name="window">目标窗口句柄。</param>
    /// <returns>置前结果（<see cref="WizardForegroundOutcome"/>）。</returns>
    public WizardForegroundOutcome Activate(IntPtr window)
    {
        var foreground = _getForegroundWindow();
        if (foreground == window)
        {
            return WizardForegroundOutcome.AlreadyForeground;
        }

        // 挂接：本 UI 线程与当前前台线程共享输入队列——前置请求从前台线程视角发出
        //    无前台窗口（句柄为零）或同线程（自家窗口）时无需挂接
        var foregroundThread = foreground == IntPtr.Zero ? 0 : _getWindowThread(foreground);
        var ownThread = _getWindowThread(window);
        var attached = foregroundThread != 0
            && foregroundThread != ownThread
            && _attachThreadInput(ownThread, foregroundThread, true);
        bool requested;
        try
        {
            // 状态扰动：最小化 → 还原——系统对还原动作的前台授予独立于 SetForegroundWindow 配额
            //    是无前台权进程取得前台的关键一拳（S3c 证明缺它单靠挂接 + 前置请求会被拒）
            _showWindow(window, SwMinimize);
            _showWindow(window, SwRestore);
            // 会话切换（等效 Alt-Tab 目标）＋ 直接前置请求
            _switchToThisWindow(window, true);
            requested = _setForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                _attachThreadInput(ownThread, foregroundThread, false);
            }
        }

        if (requested || _getForegroundWindow() == window)
        {
            return WizardForegroundOutcome.Activated;
        }

        // 兜底：任务栏 + 标题强闪烁，闪至成为前台自停（FLASHW_TIMERNOFG）——置前彻底被拒时的可感知强提示
        _flashWindowEx(window, FlashwAll | FlashwTimerNoFg, FlashCount);
        return WizardForegroundOutcome.FlashFallback;
    }

    /// <summary>GetWindowThreadProcessId 的线程 id 投影（进程 id 出参弃用）。</summary>
    private static uint GetWindowThreadId(IntPtr window) => GetWindowThreadProcessId(window, out _);

    /// <summary>FlashWindowEx 的窗口级投影（结构体组包内联）。</summary>
    private static bool FlashWindow(IntPtr window, uint flags, uint count)
    {
        var info = new FlashwInfo
        {
            Size = (uint)Marshal.SizeOf<FlashwInfo>(),
            Window = window,
            Flags = flags,
            Count = count,
            Timeout = 0,
        };
        return FlashWindowEx(ref info);
    }
}
