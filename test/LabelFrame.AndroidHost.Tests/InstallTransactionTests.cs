using LabelFrame.AndroidHost.Updates;
using LabelFrame.Updates;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public sealed class InstallTransactionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LabelFrame-install-test-" + Guid.NewGuid().ToString("N"));
    private string JournalPath => Path.Combine(_directory, "install.json");
    private readonly MaintenanceGate _gate = new();
    private static readonly string Digest = new('A', 64);
    private static InstallTransaction.Baseline Baseline => new(Digest, "http://localhost", "device", "zebra", [new("plugin", "1", Digest, true)]);
    private static AndroidUpdatePackage Package => new(AndroidUpdateContract.PackageName, 2, "2", 28, 36, ["arm64-v8a"], Digest, Digest, 1, new(1, "1.0", "2.0"));

    private InstallTransaction Start()
    {
        var journal = new InstallTransaction(JournalPath, _gate);
        Assert.True(_gate.TryFreeze());
        journal.Begin(Guid.NewGuid().ToString("N"), 1, Package, Baseline);
        return journal;
    }

    [Theory]
    [InlineData("submitted")]
    [InlineData("confirmation")]
    [InlineData("unknown")]
    [InlineData("recovering")]
    [InlineData("rejected")]
    public void Unresolved_restart_restores_freeze_before_any_writer(string phase)
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.BeforeCommit();
        if (phase != "submitted") Assert.True(journal.AcceptResult(journal.Current!.Id, 42, phase, "reason"));
        var restartedGate = new MaintenanceGate();
        var restarted = new InstallTransaction(JournalPath, restartedGate);
        Assert.True(restarted.NeedsRecovery);
        Assert.Null(restartedGate.TryEnterWrite());
        Assert.Null(restartedGate.TryEnterEditor());
        Assert.Equal(phase, restarted.Current!.Phase);
    }

    [Fact]
    public void Healthy_new_version_persists_success_before_unfreezing()
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.BeforeCommit();
        journal.CompleteHealth(2, Digest, Baseline, true, true, true);
        Assert.False(_gate.IsFrozen);
        var reopened = new InstallTransaction(JournalPath, new());
        Assert.False(reopened.NeedsRecovery);
        Assert.Equal("succeeded", reopened.Current!.Phase);
        Assert.False(journal.AcceptResult(journal.Current!.Id, 42, "confirmation", null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Manual_recovery_is_audited_not_success_and_late_callbacks_are_ignored(long version)
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.BeforeCommit();
        journal.AcceptResult(journal.Current!.Id, 42, "unknown", "原始未知结果");
        journal.ResumeManually(journal.Current.Id, version, Digest, true, "已核对现场，接受风险");
        Assert.False(_gate.IsFrozen);
        Assert.Equal("manually-resumed", journal.Current.Phase);
        Assert.NotNull(journal.Current.ManualRecovery);
        var audit = Directory.GetFiles(Path.Combine(_directory, "recovery-audit")).Single();
        Assert.Contains("\"Phase\":\"unknown\"", File.ReadAllText(audit));
        Assert.False(new InstallTransaction(JournalPath, new()).NeedsRecovery);
        Assert.False(journal.AcceptResult(journal.Current.Id, 42, "recovering", null));
        Assert.False(journal.AcceptResult(journal.Current.Id, 42, "rejected", null));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("version")]
    [InlineData("signer")]
    [InlineData("transaction")]
    [InlineData("active")]
    public void Manual_recovery_cannot_skip_installation_identity_or_active_transaction(string fault)
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.BeforeCommit();
        if (fault != "active") journal.AcceptResult(journal.Current!.Id, 42, "unknown", null);
        Assert.Throws<InvalidOperationException>(() => journal.ResumeManually(fault == "transaction" ? "other" : journal.Current!.Id,
            fault == "version" ? 3 : 1, fault == "signer" ? new('B', 64) : Digest, fault != "session", "已核对"));
        Assert.True(_gate.IsFrozen);
        Assert.True(journal.NeedsRecovery);
    }

    [Fact]
    public void Manual_recovery_audit_write_failure_keeps_freeze()
    {
        var journal = Start();
        journal.RecoveryFailed("待核对");
        File.WriteAllText(Path.Combine(_directory, "recovery-audit"), "blocked");
        Assert.Throws<IOException>(() => journal.ResumeManually(journal.Current!.Id, 1, Digest, true, "已核对"));
        Assert.True(_gate.IsFrozen);
        Assert.True(new InstallTransaction(JournalPath, new()).NeedsRecovery);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("signer")]
    [InlineData("config")]
    [InlineData("server")]
    [InlineData("device")]
    [InlineData("brand")]
    [InlineData("plugin-missing")]
    [InlineData("plugin-version")]
    [InlineData("plugin-bytes")]
    [InlineData("plugin-load")]
    [InlineData("api")]
    [InlineData("brand-load")]
    [InlineData("route")]
    public void Every_health_fact_is_required(string fault)
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.BeforeCommit();
        var actual = fault switch
        {
            "config" => Baseline with { ConfigDigest = new('B', 64) },
            "server" => Baseline with { ServerUrl = "http://other" },
            "device" => Baseline with { DeviceId = "other" },
            "brand" => Baseline with { Brand = "other" },
            "plugin-missing" => Baseline with { Plugins = [] },
            "plugin-version" => Baseline with { Plugins = [Baseline.Plugins[0] with { Version = "2" }] },
            "plugin-bytes" => Baseline with { Plugins = [Baseline.Plugins[0] with { Digest = new('B', 64) }] },
            "plugin-load" => Baseline with { Plugins = [Baseline.Plugins[0] with { Loaded = false }] },
            _ => Baseline
        };
        Assert.Throws<InvalidDataException>(() => journal.CompleteHealth(fault == "version" ? 1 : 2,
            fault == "signer" ? new('B', 64) : Digest, actual, fault != "api", fault != "brand-load", fault != "route"));
        Assert.True(_gate.IsFrozen);
    }

    [Fact]
    public void Missing_session_is_not_proof_of_cancellation_after_commit()
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.BeforeCommit();
        Assert.Throws<InvalidOperationException>(() => journal.CompleteCancellation(1, true));
        Assert.True(_gate.IsFrozen);
        Assert.True(journal.AcceptResult(journal.Current!.Id, 42, "rejected", "cancelled"));
        Assert.Throws<InvalidOperationException>(() => journal.CompleteCancellation(1, false));
        Assert.Throws<InvalidOperationException>(() => journal.CompleteCancellation(2, true));
        journal.CompleteCancellation(1, true);
        Assert.False(_gate.IsFrozen);
        Assert.Equal("cancelled", new InstallTransaction(JournalPath, new()).Current!.Phase);
    }

    [Fact]
    public void Uncommitted_preparation_can_be_cancelled_after_session_removal()
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.CompleteCancellation(1, true);
        Assert.False(_gate.IsFrozen);
    }

    [Fact]
    public void Wrong_transaction_or_session_and_late_pending_callback_are_ignored()
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.BeforeCommit();
        Assert.False(journal.AcceptResult("wrong", 42, "rejected", null));
        Assert.False(journal.AcceptResult(journal.Current!.Id, 43, "rejected", null));
        Assert.True(journal.AcceptResult(journal.Current.Id, 42, "recovering", null));
        Assert.False(journal.AcceptResult(journal.Current.Id, 42, "confirmation", null));
        Assert.Equal("recovering", journal.Current.Phase);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("temporary")]
    [InlineData("oversized")]
    [InlineData("missing-fields")]
    [InlineData("directory")]
    [InlineData("temporary-directory")]
    public void Invalid_disk_state_fails_closed(string fault)
    {
        Directory.CreateDirectory(_directory);
        if (fault == "directory") Directory.CreateDirectory(JournalPath);
        else if (fault == "temporary-directory") Directory.CreateDirectory(JournalPath + ".tmp");
        else if (fault == "temporary") File.WriteAllText(JournalPath + ".tmp", "pending");
        else File.WriteAllText(JournalPath, fault == "oversized" ? new string('x', 256 * 1024 + 1) : fault == "missing-fields" ? "{}" : "{");
        var journal = new InstallTransaction(JournalPath, _gate);
        Assert.NotNull(journal.Fault);
        Assert.True(journal.NeedsRecovery);
        Assert.True(_gate.IsFrozen);
    }

    [Fact]
    public void Failed_completion_write_never_unfreezes_or_loses_old_state()
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.BeforeCommit();
        Directory.CreateDirectory(JournalPath + ".tmp");
        var error = Record.Exception(() => journal.CompleteHealth(2, Digest, Baseline, true, true, true));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.True(_gate.IsFrozen);
        Assert.NotNull(journal.Fault);
        Assert.Equal("submitted", journal.Current!.Phase);
    }

    [Fact]
    public async Task Download_and_install_share_exclusive_cancellable_operation()
    {
        using var operation = new UpdateOperation();
        var download = await operation.EnterAsync(default);
        var install = operation.EnterAsync(default);
        Assert.False(install.IsCompleted);
        using var cts = new CancellationTokenSource();
        var cancelled = operation.EnterAsync(cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        download.Dispose();
        download.Dispose();
        using var installed = await install;
    }

    [Fact]
    public void Health_failure_requires_explicit_retry_and_never_reinstalls()
    {
        var journal = Start();
        journal.SessionCreated(42);
        journal.BeforeCommit();
        journal.RecoveryFailed("offline");
        Assert.True(_gate.IsFrozen);
        Assert.Equal("repair", new InstallTransaction(JournalPath, new()).Current!.Phase);
        journal.RetryHealth();
        Assert.Equal("recovering", journal.Current!.Phase);
        Assert.Equal(42, journal.Current.SessionId);
        Assert.True(_gate.IsFrozen);
        Assert.Throws<InvalidOperationException>(journal.BeforeCommit);
    }

    [Fact]
    public void Preparation_without_commit_cannot_claim_success()
    {
        var journal = Start();
        Assert.Throws<InvalidDataException>(() => journal.CompleteHealth(2, Digest, Baseline, true, true, true));
        Assert.Throws<InvalidOperationException>(journal.BeforeCommit);
        Assert.True(_gate.IsFrozen);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
