using LabelFrame.Core.Jobs;
using Xunit;

namespace LabelFrame.WinHost.Tests;

public sealed class HostRestartCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lf-restart-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Maintenance_should_reject_dirty_busy_operations_and_unacknowledged_results()
    {
        var activity = new HostActivityState();
        var id = Guid.NewGuid().ToString();
        activity.ReportUi(new(id, true, false));
        Assert.False(activity.TryPrepare(out var dirty));
        Assert.Contains("未保存", dirty);
        activity.ReportUi(new(id, false, true));
        Assert.False(activity.TryPrepare(out _));
        activity.ReportUi(new(id, false, false));
        using (activity.TryBeginOperation()) Assert.False(activity.TryPrepare(out _));
        activity.AddPendingResult("route-job");
        Assert.False(activity.TryPrepare(out var pending));
        Assert.Contains("回报", pending);
        activity.ConfirmResult("route-job");
        Assert.True(activity.TryPrepare(out _));
        Assert.Null(activity.TryBeginOperation());
        Assert.False(activity.ReportUi(new(id, true, false)));
        activity.CancelPreparation();
        using var restored = activity.TryBeginOperation();
        Assert.NotNull(restored);
    }

    [Fact]
    public void Closed_session_should_not_be_reopened_by_late_dirty_report()
    {
        var state = new HostActivityState();
        var id = Guid.NewGuid().ToString();
        state.ReportUi(new(id, false, false, Closed: true, Sequence: 3));
        state.ReportUi(new(id, true, false, Sequence: 2));
        Assert.True(state.TryPrepare(out _));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Printing")]
    [InlineData("Suspended")]
    public async Task Restart_should_reject_incomplete_batch_without_freezing_queue(string status)
    {
        var (store, queue) = await CreateQueueAsync();
        using (queue)
        {
            var (job, _) = await queue.SubmitAsync("batch", ["first", "second"]);
            if (status == "Printing") await queue.ClaimNextItemAsync();
            if (status == "Suspended") await queue.SuspendAsync(job.Id);
            var activity = new HostActivityState();
            var exit = new HostExitCoordinator(_ => {}, forceExit: _ => {});
            using var coordinator = new HostRestartCoordinator(activity, queue, exit);
            coordinator.Bind(_ => throw new InvalidOperationException("不应启动"));
            Assert.Contains("任务尚未完成", await coordinator.RequestAsync(true));
            Assert.False(activity.IsPrepared);
            Assert.False(exit.IsShutdownRequested);
            Assert.True((await queue.SubmitAsync("next", ["third"])).Created);
        }
    }

    [Fact]
    public async Task Launcher_failure_should_restore_submissions_and_allow_retry()
    {
        var (_, queue) = await CreateQueueAsync();
        using (queue)
        {
            var activity = new HostActivityState();
            var exit = new HostExitCoordinator(_ => {}, forceExit: _ => {});
            using var coordinator = new HostRestartCoordinator(activity, queue, exit);
            coordinator.Bind(_ => throw new IOException("等待器无法启动"));
            Assert.Contains("继续运行", await coordinator.RequestAsync(true));
            Assert.False(activity.IsPrepared);
            Assert.False(exit.IsShutdownRequested);
            Assert.True((await queue.SubmitAsync("retry", ["one"])).Created);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_should_wait_for_ui_freeze_and_recheck_late_edits(bool lateDirty)
    {
        var (_, queue) = await CreateQueueAsync();
        using (queue)
        {
            var activity = new HostActivityState();
            var id = Guid.NewGuid().ToString();
            activity.ReportUi(new(id, false, false));
            var exit = new HostExitCoordinator(_ => {}, forceExit: _ => {});
            using var coordinator = new HostRestartCoordinator(activity, queue, exit);
            var launches = 0;
            coordinator.Bind(_ => { launches++; return Task.CompletedTask; });
            var request = coordinator.RequestAsync(true);
            Assert.False(request.IsCompleted);
            Assert.Equal(0, launches);
            Assert.False(activity.ReportUi(new(id, lateDirty, false, Sequence: 1)));
            Assert.True(activity.ReportUi(new(id, lateDirty, false, Sequence: 2, PreparationId: activity.PreparationId)));
            var reason = await request;
            Assert.Equal(lateDirty ? 0 : 1, launches);
            if (lateDirty)
            {
                Assert.Contains("未保存", reason);
                Assert.False(activity.IsPrepared);
                Assert.True((await queue.SubmitAsync("after-cancel", ["one"])).Created);
            }
            else Assert.Null(reason);
        }
    }

    [Fact]
    public async Task Idle_restart_should_prepare_launcher_once_and_freeze_new_jobs()
    {
        var (_, queue) = await CreateQueueAsync();
        using (queue)
        {
            var activity = new HostActivityState();
            var exit = new HostExitCoordinator(_ => {}, forceExit: _ => {});
            using var coordinator = new HostRestartCoordinator(activity, queue, exit);
            var launches = 0;
            coordinator.Bind(showPlugins => { Assert.True(showPlugins); launches++; return Task.CompletedTask; });
            Assert.Null(await coordinator.RequestAsync(true));
            Assert.Null(await coordinator.RequestAsync(true));
            Assert.Equal(1, launches);
            Assert.True(activity.IsPrepared);
            await Assert.ThrowsAsync<LabelJobException>(() => queue.SubmitAsync("blocked", ["one"]));
        }
    }

    [Fact]
    public async Task Update_preparation_uses_same_business_gates_and_restores_queue_when_launcher_fails()
    {
        var (_, queue) = await CreateQueueAsync();
        using (queue)
        {
            var activity = new HostActivityState();
            var exit = new HostExitCoordinator(_ => {}, forceExit: _ => {});
            using var coordinator = new HostRestartCoordinator(activity, queue, exit);
            var launches = 0;
            Task Launch() { launches++; throw new IOException("监督进程未就绪"); }
            activity.AddPendingResult("route-result");
            Assert.Contains("回报", await coordinator.RequestUpdateAsync(Launch, CancellationToken.None));
            Assert.Equal(0, launches);
            activity.ConfirmResult("route-result");
            Assert.Contains("未就绪", await coordinator.RequestUpdateAsync(Launch, CancellationToken.None));
            Assert.Equal(1, launches);
            Assert.False(activity.IsPrepared);
            Assert.False(exit.IsShutdownRequested);
            Assert.True((await queue.SubmitAsync("after-update-failure", ["label"])).Created);
        }
    }

    [Fact]
    public void Recovery_gate_blocks_business_without_freezing_readable_ui_and_survives_preparation_cancel()
    {
        var activity = new HostActivityState();
        activity.BlockForRecovery(true);
        activity.CancelPreparation();
        Assert.True(activity.IsPrepared);
        Assert.Null(activity.PreparationId);
        Assert.Null(activity.TryBeginOperation());
        Assert.False(activity.TryPrepare(out _));
        activity.BlockForRecovery(false);
        using var operation = activity.TryBeginOperation();
        Assert.NotNull(operation);
    }

    [Fact]
    public async Task Recovery_restart_can_retry_health_but_launcher_failure_does_not_release_printing()
    {
        var (_, queue) = await CreateQueueAsync();
        using (queue)
        {
            Assert.True(await queue.TryPrepareForShutdownAsync());
            var activity = new HostActivityState();
            activity.BlockForRecovery(true);
            var exit = new HostExitCoordinator(_ => {}, forceExit: _ => {});
            using var coordinator = new HostRestartCoordinator(activity, queue, exit);
            coordinator.Bind(_ => throw new IOException("等待器未就绪"));
            Assert.Contains("正在停止或维护", await coordinator.RequestAsync(false));
            activity.AllowRecoveryRestart();
            Assert.Contains("未就绪", await coordinator.RequestAsync(false));
            Assert.True(activity.IsRecoveryBlocked);
            Assert.Null(activity.PreparationId);
            await Assert.ThrowsAsync<LabelJobException>(() => queue.SubmitAsync("blocked-after-retry", ["label"]));
            coordinator.Bind(_ => Task.CompletedTask);
            Assert.Null(await coordinator.RequestAsync(false));
            Assert.True(exit.IsShutdownRequested);
        }
    }

    private async Task<(SqliteLabelJobStore Store, LabelJobQueue Queue)> CreateQueueAsync()
    {
        Directory.CreateDirectory(_directory);
        var store = new SqliteLabelJobStore(Path.Combine(_directory, "jobs.db"));
        await store.InitializeAsync();
        return (store, new LabelJobQueue(store));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
