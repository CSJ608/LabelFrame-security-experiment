using System.Diagnostics;
using LabelFrame.WinHost;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// TrayQuitSignaler（托盘退出信号器）单元测试：
/// WM_QUIT 投递到登记的托盘线程、未登记不投递、幂等、Join 限时；
/// 以及 TrayIconService 真实消息循环退出回收（WM_QUIT → 消息循环结束 → NIM_DELETE 在托盘线程内执行）。
/// </summary>
public sealed class TrayQuitSignalerTests
{
    private static Thread CreateFinishedThread()
    {
        var thread = new Thread(() => { });
        thread.Start();
        thread.Join();
        return thread;
    }

    [Fact]
    public void SignalQuit_posts_wm_quit_to_registered_thread()
    {
        var posts = new List<(uint ThreadId, uint Msg, IntPtr WParam, IntPtr LParam)>();
        var signaler = new TrayQuitSignaler(
            log: null,
            postThreadMessage: (threadId, msg, wParam, lParam) =>
            {
                lock (posts)
                {
                    posts.Add((threadId, msg, wParam, lParam));
                }

                return true;
            });

        Assert.Equal(0u, signaler.LoopThreadId); // 未登记前为 0
        signaler.RegisterLoopThread(4242, CreateFinishedThread());
        Assert.Equal(4242u, signaler.LoopThreadId);

        Assert.True(signaler.SignalQuit(TimeSpan.FromMilliseconds(200)));

        var post = Assert.Single(posts);
        Assert.Equal(4242u, post.ThreadId);
        Assert.Equal(0x0012u, post.Msg); // WM_QUIT
        Assert.Equal(IntPtr.Zero, post.WParam);
        Assert.Equal(IntPtr.Zero, post.LParam);
    }

    [Fact]
    public void SignalQuit_without_registration_does_not_post()
    {
        var posts = new List<uint>();
        var signaler = new TrayQuitSignaler(
            log: null,
            postThreadMessage: (threadId, _, _, _) =>
            {
                lock (posts)
                {
                    posts.Add(threadId);
                }

                return true;
            });

        Assert.False(signaler.SignalQuit(TimeSpan.FromMilliseconds(50)));
        Assert.Empty(posts);
    }

    [Fact]
    public void SignalQuit_is_idempotent()
    {
        var postCount = 0;
        var signaler = new TrayQuitSignaler(
            log: null,
            postThreadMessage: (_, _, _, _) =>
            {
                Interlocked.Increment(ref postCount);
                return true;
            });
        signaler.RegisterLoopThread(7, CreateFinishedThread());

        Assert.True(signaler.SignalQuit(TimeSpan.FromMilliseconds(200)));
        Assert.False(signaler.SignalQuit(TimeSpan.FromMilliseconds(200)));

        Assert.Equal(1, postCount);
    }

    [Fact]
    public void Dispose_posts_wm_quit_with_default_join_timeout()
    {
        var posts = new List<(uint ThreadId, uint Msg)>();
        var logs = new List<string>();
        var signaler = new TrayQuitSignaler(
            log: logs.Add,
            postThreadMessage: (threadId, msg, _, _) =>
            {
                lock (posts)
                {
                    posts.Add((threadId, msg));
                }

                return true;
            });
        signaler.RegisterLoopThread(9, CreateFinishedThread());

        signaler.Dispose();

        var post = Assert.Single(posts);
        Assert.Equal(9u, post.ThreadId);
        Assert.Equal(0x0012u, post.Msg);
        Assert.Contains(logs, l => l.Contains("投递 WM_QUIT"));
    }

    [Fact]
    public void SignalQuit_join_is_bounded_when_thread_does_not_exit()
    {
        using var release = new ManualResetEventSlim(false);
        var stuckThread = new Thread(() => release.Wait()) { IsBackground = true };
        stuckThread.Start();
        try
        {
            var signaler = new TrayQuitSignaler(log: null, postThreadMessage: (_, _, _, _) => true);
            signaler.RegisterLoopThread(11, stuckThread);

            var stopwatch = Stopwatch.StartNew();
            Assert.True(signaler.SignalQuit(TimeSpan.FromMilliseconds(100)));
            stopwatch.Stop();

            // Join 限时生效：线程未退出也不长时间阻塞退出流程
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"SignalQuit 阻塞了 {stopwatch.Elapsed}");
        }
        finally
        {
            release.Set();
            stuckThread.Join(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void TrayIconService_dispose_should_terminate_tray_message_loop()
    {
        // 真实消息循环交互：Start 后托盘线程登记 → Dispose 投递 WM_QUIT →
        // GetMessage 返回 0 退出循环 → NIM_DELETE / DestroyWindow 在托盘线程内执行 → 线程结束。
        // 不依赖任务栏存在（Shell_NotifyIcon 结果不断言），只验证退出信号链路。
        var logs = new List<string>();
        var signaler = new TrayQuitSignaler(log: null); // 真实 Win32 PostThreadMessage
        using var service = new TrayIconService(logs.Add, signaler);
        service.Start(() => { }, () => Task.CompletedTask);

        // 等待托盘线程登记（窗口创建 + NIM_ADD 完成后）
        var registerDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (signaler.LoopThreadId == 0 && DateTime.UtcNow < registerDeadline)
        {
            Thread.Sleep(20);
        }

        Assert.NotEqual(0u, signaler.LoopThreadId);

        service.Dispose();

        // 消息循环应在限时内退出（强退清理路径的时间上界）
        var exitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (signaler.IsLoopThreadAlive && DateTime.UtcNow < exitDeadline)
        {
            Thread.Sleep(20);
        }

        Assert.False(signaler.IsLoopThreadAlive);
    }
}
