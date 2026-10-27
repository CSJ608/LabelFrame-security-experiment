using LabelFrame.AndroidHost.Updates;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public sealed class MaintenanceGateTests
{
    [Fact]
    public void Writer_and_editor_block_freeze_until_both_released()
    {
        var gate = new MaintenanceGate();
        using var writer = gate.TryEnterWrite();
        using var editor = gate.TryEnterEditor();
        Assert.False(gate.TryFreeze());
        writer!.Dispose();
        Assert.False(gate.TryFreeze());
        editor!.Dispose();
        Assert.True(gate.TryFreeze());
        Assert.Null(gate.TryEnterWrite());
        Assert.Null(gate.TryEnterEditor());
    }

    [Fact]
    public void Lease_double_dispose_does_not_decrement_other_lease()
    {
        var gate = new MaintenanceGate();
        var first = gate.TryEnterWrite()!;
        using var second = gate.TryEnterWrite();
        first.Dispose();
        first.Dispose();
        Assert.False(gate.TryFreeze());
    }

    [Fact]
    public void Restored_freeze_blocks_until_explicit_resolution()
    {
        var gate = new MaintenanceGate();
        gate.RestoreFrozen();
        Assert.False(gate.TryFreeze());
        Assert.Null(gate.TryEnterWrite());
        gate.ReleaseAfterResolution();
        Assert.NotNull(gate.TryEnterWrite());
    }

    [Fact]
    public async Task Freeze_and_write_are_mutually_exclusive_under_race()
    {
        for (var i = 0; i < 100; i++)
        {
            var gate = new MaintenanceGate();
            using var start = new ManualResetEventSlim();
            var writing = Task.Run(() => { start.Wait(); return gate.TryEnterWrite(); });
            var freezing = Task.Run(() => { start.Wait(); return gate.TryFreeze(); });
            start.Set();
            using var lease = await writing;
            Assert.NotEqual(lease is not null, await freezing);
        }
    }
}
