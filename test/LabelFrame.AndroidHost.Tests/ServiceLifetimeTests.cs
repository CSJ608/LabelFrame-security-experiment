using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public sealed class ServiceLifetimeTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Prepare_runs_once_on_caller_before_background_initialization()
    {
        using var generations = new SemaphoreSlim(1, 1);
        using var lifetime = new ServiceLifetime(generations);
        var entered = Signal();
        var caller = Environment.CurrentManagedThreadId;
        var prepareThread = 0;
        var prepared = 0;
        var initializeThread = 0;
        Task Initialize(CancellationToken _)
        {
            initializeThread = Environment.CurrentManagedThreadId;
            Assert.Equal(1, prepared);
            entered.SetResult();
            return Task.CompletedTask;
        }
        void Prepare() { prepareThread = Environment.CurrentManagedThreadId; prepared++; }
        var completion = lifetime.Start(Initialize, _ => { }, () => Task.CompletedTask, _ => { }, Prepare);
        Assert.Equal(caller, prepareThread);
        Assert.Same(completion, lifetime.Start(Initialize, _ => { }, () => Task.CompletedTask, _ => { }, Prepare));
        await Wait(entered.Task);
        Assert.NotEqual(caller, initializeThread);
        Assert.Equal(1, prepared);
        lifetime.Stop();
        await Wait(completion);
    }

    [Fact]
    public async Task Prepare_failure_prevents_background_work_and_preserves_failure_callback_guard()
    {
        using var generations = new SemaphoreSlim(1, 1);
        using var lifetime = new ServiceLifetime(generations);
        var expected = new DllNotFoundException("隔离原生加载失败");
        Exception? observed = null;
        var initialized = false;
        var activated = false;
        var drained = false;
        var completion = lifetime.Start(_ => { initialized = true; return Task.CompletedTask; },
            _ => activated = true, () => { drained = true; return Task.CompletedTask; },
            error => observed = error, () => throw expected);
        await Wait(completion);
        Assert.Same(expected, observed);
        Assert.False(initialized);
        Assert.False(activated);
        Assert.False(drained);
        Assert.Equal(1, generations.CurrentCount);
        var active = false;
        lifetime.IfActive(() => active = true);
        Assert.True(active);
        lifetime.Stop();
        lifetime.IfActive(() => Assert.Fail("销毁后的失败回调不应执行"));
    }

    [Fact]
    public async Task Slow_synchronous_initialization_runs_off_caller_and_never_activates_after_destroy()
    {
        using var generations = new SemaphoreSlim(1, 1);
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var caller = Environment.CurrentManagedThreadId;
        var initThread = caller;
        var starts = 0;
        var drains = 0;
        var lifetime = new ServiceLifetime(generations);
        var completion = lifetime.Start(_ =>
        {
            initThread = Environment.CurrentManagedThreadId;
            entered.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None));
            return Task.CompletedTask;
        }, _ => starts++, () => { drains++; return Task.CompletedTask; }, error => throw new InvalidOperationException("初始化失败", error));
        try
        {
            await Wait(entered.Task);
            Assert.NotEqual(caller, initThread);
            Assert.Equal(0, starts);
            lifetime.Stop();
            lifetime.Stop();
            Assert.False(completion.IsCompleted);
        }
        finally { release.Set(); }
        await Wait(completion);
        Assert.Equal(0, starts);
        Assert.Equal(1, drains);
        lifetime.Stop();
    }

    [Fact]
    public async Task Restart_waits_for_old_initialization_and_all_cleanup_before_new_worker()
    {
        using var generations = new SemaphoreSlim(1, 1);
        var initEntered = Signal();
        var initRelease = Signal();
        var drainEntered = Signal();
        var drainRelease = Signal();
        var newStarted = Signal();
        var first = new ServiceLifetime(generations);
        var second = new ServiceLifetime(generations);
        var oldStarts = 0;
        var old = first.Start(async _ => { initEntered.SetResult(); await initRelease.Task; },
            _ => oldStarts++, async () => { drainEntered.SetResult(); await drainRelease.Task; }, _ => { });
        await Wait(initEntered.Task);
        first.Stop();
        var next = second.Start(_ => { newStarted.SetResult(); return Task.CompletedTask; },
            _ => { }, () => Task.CompletedTask, _ => { });
        try
        {
            Assert.False(newStarted.Task.IsCompleted);
            initRelease.SetResult();
            await Wait(drainEntered.Task);
            Assert.False(newStarted.Task.IsCompleted);
            Assert.Equal(0, oldStarts);
        }
        finally { initRelease.TrySetResult(); drainRelease.TrySetResult(); }
        await Wait(newStarted.Task);
        second.Stop();
        await Wait(Task.WhenAll(old, next));
    }

    [Fact]
    public async Task Duplicate_start_creates_one_worker_and_stop_waits_for_worker_exit()
    {
        using var generations = new SemaphoreSlim(1, 1);
        var activated = Signal();
        var workerExit = Signal();
        var draining = Signal();
        var starts = 0;
        var lifetime = new ServiceLifetime(generations);
        Task Initialize(CancellationToken _) => Task.CompletedTask;
        void Activate(CancellationToken _) { starts++; activated.SetResult(); }
        async Task Drain() { draining.SetResult(); await workerExit.Task; }
        var first = lifetime.Start(Initialize, Activate, Drain, _ => { });
        Assert.Same(first, lifetime.Start(Initialize, Activate, Drain, _ => { }));
        await Wait(activated.Task);
        lifetime.Stop();
        await Wait(draining.Task);
        Assert.False(first.IsCompleted);
        workerExit.SetResult();
        await Wait(first);
        Assert.Equal(1, starts);
        Assert.Equal(1, generations.CurrentCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initialization_or_activation_failure_is_observed_and_drained(bool activationFailure)
    {
        using var generations = new SemaphoreSlim(1, 1);
        var expected = new IOException("模拟空间查询失败");
        expected.Data["JobStorageBackupPath"] = "隔离备份";
        Exception? observed = null;
        var activated = false;
        var drained = false;
        var lifetime = new ServiceLifetime(generations);
        await Wait(lifetime.Start(_ => activationFailure ? Task.CompletedTask : Task.FromException(expected),
            _ => { activated = true; throw expected; },
            () => { drained = true; return Task.CompletedTask; }, error => observed = error));
        Assert.Same(expected, observed);
        Assert.Equal(activationFailure, activated);
        Assert.True(drained);
        Assert.Equal(1, generations.CurrentCount);
        var lateCallback = false;
        lifetime.Stop();
        lifetime.IfActive(() => lateCallback = true);
        Assert.False(lateCallback);
    }

    [Fact]
    public async Task Stop_does_not_run_blocking_cancellation_callbacks_on_caller()
    {
        using var generations = new SemaphoreSlim(1, 1);
        using var release = new ManualResetEventSlim();
        var activated = Signal();
        var cancelling = Signal();
        var drained = false;
        using var lifetime = new ServiceLifetime(generations);
        CancellationTokenRegistration registration = default;
        var completion = lifetime.Start(token =>
        {
            registration = token.Register(() =>
            {
                cancelling.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None));
            });
            return Task.CompletedTask;
        }, _ => activated.SetResult(), () => { drained = true; return Task.CompletedTask; }, _ => { });
        try
        {
            await Wait(activated.Task);
            lifetime.Stop();
            await Wait(cancelling.Task);
            Assert.False(drained);
            Assert.False(completion.IsCompleted);
        }
        finally { release.Set(); }
        await Wait(completion);
        registration.Dispose();
        Assert.True(drained);
    }

    [Fact]
    public async Task Destroy_while_waiting_never_initializes_or_cleans_another_instance()
    {
        using var generations = new SemaphoreSlim(0, 1);
        var lifetime = new ServiceLifetime(generations);
        var initialized = false;
        var drained = false;
        var completion = lifetime.Start(_ => { initialized = true; return Task.CompletedTask; },
            _ => { }, () => { drained = true; return Task.CompletedTask; }, _ => { });
        lifetime.Stop();
        await Wait(completion);
        Assert.False(initialized);
        Assert.False(drained);
        Assert.Equal(0, generations.CurrentCount);
    }
}
