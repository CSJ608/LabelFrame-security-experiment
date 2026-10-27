using LabelFrame.AndroidHost.Updates;
using LabelFrame.Core.Jobs;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public sealed class MaintenanceSafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LabelFrame-maintenance-test-" + Guid.NewGuid().ToString("N"));
    private RouteJournal Journal => new(Path.Combine(_directory, "route.json"), "http://localhost", "device");
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Theory]
    [InlineData(LabelJobStatus.Pending)]
    [InlineData(LabelJobStatus.Printing)]
    [InlineData(LabelJobStatus.Suspended)]
    public async Task Unfinished_whole_batch_prevents_install_even_between_items(LabelJobStatus status)
    {
        var gate = new MaintenanceGate();
        var store = new Store(s => Task.FromResult<IReadOnlyList<LabelJob>>(s == status ? [new() { Id = "job", RequestId = "request", Status = status, Items = [] }] : []));
        Assert.False(await MaintenanceSafety.TryFreezeAsync(gate, store, Journal, default));
        Assert.False(gate.IsFrozen);
    }

    [Fact]
    public async Task Queue_read_failure_releases_only_uncommitted_freeze()
    {
        var gate = new MaintenanceGate();
        var store = new Store(_ => throw new IOException("无法读取作业库"));
        await Assert.ThrowsAsync<IOException>(() => MaintenanceSafety.TryFreezeAsync(gate, store, Journal, default));
        Assert.False(gate.IsFrozen);
    }

    [Fact]
    public async Task Claim_uncertainty_prevents_install()
    {
        var journal = Journal;
        journal.BeginClaim();
        var gate = new MaintenanceGate();
        Assert.False(await MaintenanceSafety.TryFreezeAsync(gate, new Store(_ => Task.FromResult<IReadOnlyList<LabelJob>>([])), Journal, default));
        Assert.False(gate.IsFrozen);
    }

    [Theory]
    [InlineData(LabelJobStatus.Pending)]
    [InlineData(LabelJobStatus.Printing)]
    [InlineData(LabelJobStatus.Suspended)]
    public async Task Manual_route_recovery_still_blocks_unfinished_jobs(LabelJobStatus status)
    {
        var journal = Journal;
        journal.BeginClaim();
        journal.AbandonClaim();
        var gate = new MaintenanceGate();
        var store = new Store(s => Task.FromResult<IReadOnlyList<LabelJob>>(s == status ? [new() { Id = "job", RequestId = "request", Status = status, Items = [] }] : []));
        Assert.False(await MaintenanceSafety.TryFreezeForRouteRecoveryAsync(gate, store, journal, default));
        Assert.False(gate.IsFrozen);
        Assert.True(journal.ClaimUncertain);
    }

    [Fact]
    public async Task Manual_route_recovery_freezes_only_when_no_known_work_or_editor_remains()
    {
        var journal = Journal;
        var gate = new MaintenanceGate();
        var store = new Store(_ => Task.FromResult<IReadOnlyList<LabelJob>>([]));
        journal.BeginClaim();
        Assert.False(await MaintenanceSafety.TryFreezeForRouteRecoveryAsync(gate, store, journal, default));
        journal.AbandonClaim();
        using (gate.TryEnterEditor())
            Assert.False(await MaintenanceSafety.TryFreezeForRouteRecoveryAsync(gate, store, journal, default));
        Assert.True(await MaintenanceSafety.TryFreezeForRouteRecoveryAsync(gate, store, journal, default));
        Assert.Null(gate.TryEnterWrite());
    }

    [Fact]
    public async Task Queue_inspection_runs_with_new_writes_blocked_and_success_keeps_freeze()
    {
        var gate = new MaintenanceGate();
        var checkedStates = new List<LabelJobStatus>();
        var store = new Store(s =>
        {
            Assert.Null(gate.TryEnterWrite());
            Assert.Null(gate.TryEnterEditor());
            checkedStates.Add(s);
            return Task.FromResult<IReadOnlyList<LabelJob>>([]);
        });
        Assert.True(await MaintenanceSafety.TryFreezeAsync(gate, store, Journal, default));
        Assert.Equal(3, checkedStates.Count);
        Assert.True(gate.IsFrozen);
    }

    [Fact]
    public async Task Another_attempt_cannot_release_restored_freeze()
    {
        var gate = new MaintenanceGate();
        gate.RestoreFrozen();
        Assert.False(await MaintenanceSafety.TryFreezeAsync(gate, new Store(_ => throw new InvalidOperationException()), Journal, default));
        Assert.True(gate.IsFrozen);
    }

    private sealed class Store(Func<LabelJobStatus, Task<IReadOnlyList<LabelJob>>> list) : ILabelJobStore
    {
        public Task<IReadOnlyList<LabelJob>> ListJobsByStatusAsync(LabelJobStatus status, CancellationToken ct = default) => list(status);
        public Task InitializeAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LabelJob> CreateJobAsync(LabelJob job, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LabelJob?> GetJobAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LabelJob?> GetJobByRequestIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LabelJob?> SetJobStatusAsync(string id, LabelJobStatus status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LabelJob?> SetItemStatusAsync(string id, string itemId, LabelJobItemStatus status, string? code, string? message, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LabelJob>> ListRecentAsync(int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPendingItemsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
