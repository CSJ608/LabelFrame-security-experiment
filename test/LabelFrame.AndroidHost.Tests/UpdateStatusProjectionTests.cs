using LabelFrame.AndroidHost.Updates;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public sealed class UpdateStatusProjectionTests
{
    private static readonly InstallTransaction.State Previous = new(1, "old", "old-release", 1, 2, "hash", "signer",
        new("config", "server", "device", "brand", []), "succeeded", 1, DateTimeOffset.UtcNow, null);

    [Theory]
    [InlineData("ready")]
    [InlineData("failed")]
    [InlineData("checking")]
    public void NewDownloadOverridesOldSuccess(string phase)
    {
        var report = UpdateStatusProjection.Create("device", 2, phase, "new reason", 3, Previous, null, false);
        Assert.Equal(phase, report.Phase);
        Assert.Equal(3, report.TargetVersionCode);
        Assert.Null(report.TransactionId);
        Assert.Equal("new reason", report.Reason);
    }

    [Fact]
    public void ActiveTransactionAndStorageFaultTakePriority()
    {
        var pending = Previous with { Phase = "confirmation" };
        Assert.Equal("confirmation", UpdateStatusProjection.Create("device", 1, "ready", null, 3, pending, null, true).Phase);
        Assert.Equal("repair", UpdateStatusProjection.Create("device", 1, "idle", null, null, pending, "fault", true).Phase);
        Assert.Equal("succeeded", UpdateStatusProjection.Create("device", 2, "idle", null, null, Previous, null, false).Phase);
    }
}
