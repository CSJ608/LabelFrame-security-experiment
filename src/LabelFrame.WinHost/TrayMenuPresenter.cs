using System.Runtime.InteropServices;

namespace LabelFrame.WinHost;

/// <summary>
/// 托盘右键菜单显示器。
/// 实现 Win32 通知图标上下文菜单标准前置模式（MSDN TrackPopupMenu Remarks / KB135788）：
/// TrackPopupMenu 前 <see cref="SetForegroundWindow"/> 前置隐藏 owner 窗口（外部点击自动收起菜单），
/// 菜单关闭后 <see cref="PostMessage"/> 投递 WM_NULL 清除菜单模态状态（owner 前置状态变化后菜单仍可反复弹出）；
/// 菜单模态运行期间重入 <see cref="ShowMenu"/> 直接早退，消除嵌套 TrackPopupMenu 的未定义行为。
/// 窗口前置 / 菜单弹出 / 消息投递三个 Win32 调用点可注入替身，
/// 供单元测试断言调用时序，对齐 <see cref="TrayQuitSignaler"/> 可测性先例。
/// </summary>
public sealed class TrayMenuPresenter
{
    /// <summary>菜单命令 Id：打开界面。</summary>
    public const int CmdOpen = 1;

    /// <summary>菜单命令 Id：退出。</summary>
    public const int CmdExit = 2;

    private const uint WM_NULL = 0x0000;
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_TOPALIGN = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    /// <summary>菜单弹出（Win32 TrackPopupMenu 形态）。</summary>
    public delegate IntPtr TrackPopupMenuProc(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    /// <summary>窗口前置（Win32 SetForegroundWindow 形态）。</summary>
    public delegate bool SetForegroundWindowProc(IntPtr hWnd);

    /// <summary>消息投递（Win32 PostMessage 形态）。</summary>
    public delegate bool PostMessageProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern IntPtr TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly SetForegroundWindowProc _setForegroundWindow;
    private readonly TrackPopupMenuProc _trackPopupMenu;
    private readonly PostMessageProc _postMessage;
    private int _showing; // 菜单模态运行标志（Interlocked 读写）

    /// <summary>
    /// 创建托盘菜单显示器。
    /// </summary>
    /// <param name="setForegroundWindow">窗口前置（默认 Win32 SetForegroundWindow；测试注入替身）。</param>
    /// <param name="trackPopupMenu">菜单弹出（默认 Win32 TrackPopupMenu；测试注入替身）。</param>
    /// <param name="postMessage">消息投递（默认 Win32 PostMessage；测试注入替身）。</param>
    public TrayMenuPresenter(
        SetForegroundWindowProc? setForegroundWindow = null,
        TrackPopupMenuProc? trackPopupMenu = null,
        PostMessageProc? postMessage = null)
    {
        _setForegroundWindow = setForegroundWindow ?? SetForegroundWindow;
        _trackPopupMenu = trackPopupMenu ?? TrackPopupMenu;
        _postMessage = postMessage ?? PostMessage;
    }

    /// <summary>
    /// 在光标处弹出托盘右键菜单并返回所选命令 Id（0 = 取消选择或重入早退）。
    /// 须在托盘消息循环线程（owner 窗口线程）上调用。
    /// </summary>
    /// <param name="owner">托盘隐藏消息窗口（菜单 owner，也是前置与 WM_NULL 投递目标）。</param>
    public int ShowMenu(IntPtr owner)
    {
        // TrackPopupMenu 模态循环运行期间派发的消息可能再次触发右键（重入本方法）——
        // 嵌套 TrackPopupMenu 行为未定义，直接早退。
        if (Interlocked.Exchange(ref _showing, 1) == 1)
        {
            return 0;
        }

        try
        {
            GetCursorPos(out var pt);
            var menu = CreatePopupMenu();
            try
            {
                AppendMenu(menu, MF_STRING, new IntPtr(CmdOpen), "打开界面");
                AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, string.Empty);
                AppendMenu(menu, MF_STRING, new IntPtr(CmdExit), "退出");

                // 标准前置模式三件套（KB135788）：
                // 前置 owner——owner 非前台时系统不会在外部点击时收起菜单（症状 1）
                _setForegroundWindow(owner);
                // 模态弹出，返回即菜单已关闭（TPM_RETURNCMD：选中命令 Id 作为返回值）
                var cmd = _trackPopupMenu(menu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_TOPALIGN, pt.x, pt.y, 0, owner, IntPtr.Zero);
                // 投 WM_NULL 清除菜单模态状态——否则后续弹出可能被系统立即取消（症状 2）。
                _postMessage(owner, WM_NULL, IntPtr.Zero, IntPtr.Zero);
                return (int)cmd.ToInt64();
            }
            finally
            {
                DestroyMenu(menu);
            }
        }
        finally
        {
            Volatile.Write(ref _showing, 0);
        }
    }
}
