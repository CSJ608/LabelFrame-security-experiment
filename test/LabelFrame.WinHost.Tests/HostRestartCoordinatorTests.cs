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
