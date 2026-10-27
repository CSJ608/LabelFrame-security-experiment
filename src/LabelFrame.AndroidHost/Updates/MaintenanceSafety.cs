using LabelFrame.Core.Jobs;

namespace LabelFrame.AndroidHost.Updates;

/// <summary>在冻结新写入后核对整批作业及路由未决状态。</summary>
public static class MaintenanceSafety
{
    public static async Task<bool> TryFreezeAsync(MaintenanceGate gate, ILabelJobStore store, RouteJournal journal, CancellationToken ct)
        => await TryFreezeCoreAsync(gate, store, journal, false, ct);

    public static async Task<bool> TryFreezeForRouteRecoveryAsync(MaintenanceGate gate, ILabelJobStore store, RouteJournal journal, CancellationToken ct)
        => await TryFreezeCoreAsync(gate, store, journal, true, ct);

    private static async Task<bool> TryFreezeCoreAsync(MaintenanceGate gate, ILabelJobStore store, RouteJournal journal, bool acceptUnknown, CancellationToken ct)
    {
        if (!gate.TryFreeze()) return false;
        var safe = false;
        try
        {
            if (acceptUnknown ? journal.HasPendingWork : journal.HasOutstanding) return false;
            if (!await IsQueueIdleAsync(store, ct)) return false;
            ct.ThrowIfCancellationRequested();
            safe = true;
            return true;
        }
        finally { if (!safe) gate.ReleaseAfterResolution(); }
    }

    public static async Task<bool> IsQueueIdleAsync(ILabelJobStore store, CancellationToken ct)
    {
        foreach (var status in new[] { LabelJobStatus.Pending, LabelJobStatus.Printing, LabelJobStatus.Suspended })
            if ((await store.ListJobsByStatusAsync(status, ct)).Count != 0) return false;
        ct.ThrowIfCancellationRequested();
        return true;
    }
}
