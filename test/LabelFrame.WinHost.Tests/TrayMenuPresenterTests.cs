using LabelFrame.WinHost;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// TrayMenuPresenter（托盘右键菜单显示器）单元测试：
/// Win32 通知图标菜单标准前置模式调用时序——SetForegroundWindow(owner) 先于 TrackPopupMenu、
/// PostMessage(owner, WM_NULL) 后于菜单关闭（TrackPopupMenu 返回）；菜单模态期间重入早退不嵌套；
/// 菜单关闭后可再次弹出。
/// </summary>
public sealed class TrayMenuPresenterTests
{
    private static IntPtr Owner => new(0x1F4); // 非零哨兵 owner 句柄，断言调用目标与传入 owner 一致

    [Fact]
    public void ShowMenu_invokes_standard_foreground_pattern_in_order()
    {
        // 断言链：前置 owner → 菜单弹出（owner 为 owner 窗口、TPM_RETURNCMD）→ 菜单关闭（替身返回）→ WM_NULL 投递
        var calls = new List<string>();
        var owner = Owner;
        var presenter = new TrayMenuPresenter(
            setForegroundWindow: hwnd =>
            {
                calls.Add($"SetForegroundWindow({hwnd})");
                return true;
            },
            trackPopupMenu: (_, flags, _, _, _, hwnd, _) =>
            {
                calls.Add($"TrackPopupMenu(flags=0x{flags:X4}, owner={hwnd})");
                return IntPtr.Zero; // 替身返回即「菜单已关闭」（未选择）
            },
            postMessage: (hwnd, msg, wParam, lParam) =>
            {
                calls.Add($"PostMessage(hwnd={hwnd}, msg=0x{msg:X4}, wParam={wParam}, lParam={lParam})");
                return true;
            });

        var cmd = presenter.ShowMenu(owner);

        Assert.Equal(0, cmd); // 未选择
        Assert.Equal(
            new[]
            {
                $"SetForegroundWindow({owner})",                                // ① 前置 owner 先于菜单弹出
                $"TrackPopupMenu(flags=0x0100, owner={owner})",                 // ② TPM_RETURNCMD 且 owner 窗口一致
                $"PostMessage(hwnd={owner}, msg=0x0000, wParam=0, lParam=0)",   // ③ WM_NULL 后于菜单关闭
            },
            calls);
    }

    [Fact]
    public void ShowMenu_returns_selected_command_id()
    {
        var presenter = new TrayMenuPresenter(
            setForegroundWindow: _ => true,
            trackPopupMenu: (_, _, _, _, _, _, _) => new IntPtr(TrayMenuPresenter.CmdOpen),
            postMessage: (_, _, _, _) => true);

        Assert.Equal(TrayMenuPresenter.CmdOpen, presenter.ShowMenu(Owner));
    }

    [Fact]
    public void ShowMenu_reentrant_call_during_modal_loop_returns_early_without_nested_menu()
    {
        // 衍生隐患消除：TrackPopupMenu 模态循环运行期间（替身尚未返回）再次右键重入 ShowMenu——
        // 早退返回 0，不产生嵌套 TrackPopupMenu，也无关联的前置 / WM_NULL 副作用。
        var owner = Owner;
        var trackCount = 0;
        var setForegroundCount = 0;
        var postNullCount = 0;
        var nestedCmd = -1;
        TrayMenuPresenter presenter = null!;
        presenter = new TrayMenuPresenter(
            setForegroundWindow: _ =>
            {
                Interlocked.Increment(ref setForegroundCount);
                return true;
            },
            trackPopupMenu: (_, _, _, _, _, _, _) =>
            {
                Interlocked.Increment(ref trackCount);
                nestedCmd = presenter.ShowMenu(owner); // 模态循环内重入（如用户再次右键）
                return IntPtr.Zero;
            },
            postMessage: (_, msg, _, _) =>
            {
                if (msg == 0x0000u)
                {
                    Interlocked.Increment(ref postNullCount);
                }

                return true;
            });

        var cmd = presenter.ShowMenu(owner);

        Assert.Equal(0, cmd);
        Assert.Equal(0, nestedCmd); // 重入早退，返回 0（不触发动作分发）
        Assert.Equal(1, trackCount); // 无嵌套 TrackPopupMenu
        Assert.Equal(1, setForegroundCount); // 仅外层一次前置
        Assert.Equal(1, postNullCount); // 仅外层一次 WM_NULL
    }

    [Fact]
    public void ShowMenu_can_show_again_after_previous_menu_closed()
    {
        // 症状 2 关联行为（进程内自证）：前一次菜单关闭（TrackPopupMenu 已返回、标志复位）后
        // 再次弹出正常执行——三件套每轮完整运行。
        var trackCount = 0;
        var postNullCount = 0;
        var presenter = new TrayMenuPresenter(
            setForegroundWindow: _ => true,
            trackPopupMenu: (_, _, _, _, _, _, _) =>
            {
                Interlocked.Increment(ref trackCount);
                return IntPtr.Zero;
            },
            postMessage: (_, msg, _, _) =>
            {
                if (msg == 0x0000u)
                {
                    Interlocked.Increment(ref postNullCount);
                }

                return true;
            });

        presenter.ShowMenu(Owner);
        presenter.ShowMenu(Owner); // 菜单已关闭，可反复弹出

        Assert.Equal(2, trackCount);
        Assert.Equal(2, postNullCount);
    }
}
