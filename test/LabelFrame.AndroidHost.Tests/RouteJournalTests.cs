using LabelFrame.AndroidHost.Api;
using LabelFrame.AndroidHost.Updates;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public sealed class RouteJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LabelFrame-route-test-" + Guid.NewGuid().ToString("N"));
    private string JournalPath => Path.Combine(_directory, "route.json");
    private RouteJournal Open(string server = "http://localhost:53961", bool legacy = false) => new(JournalPath, server, "device", legacy);
    private static PendingJob Job(string id = "server-job") => new(id, "request-" + id, 1, new(null, null), [new(new Dictionary<string, string> { ["code"] = "中文" })]);
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void Fresh_journal_is_durable_and_empty()
    {
        Assert.False(Open().HasOutstanding);
        Assert.True(File.Exists(JournalPath));
        Assert.False(Open().HasOutstanding);
    }

    [Fact]
    public void Manual_acknowledgement_retains_original_and_survives_restart_but_new_failure_blocks()
    {
        var journal = Open(legacy: true);
        journal.AcknowledgeUncertainty("管理员核对现场后接受历史未回报风险");
        Assert.False(Open().HasOutstanding);
        Assert.NotNull(Open().Acknowledgement);
        var audit = Directory.GetFiles(Path.Combine(_directory, "recovery-audit")).Single();
        Assert.Contains("\"ClaimUncertain\":true", File.ReadAllText(audit));
        Assert.Contains("\"Acknowledgement\":null", File.ReadAllText(audit));
        journal = Open();
        journal.BeginClaim();
        journal.CompleteClaim([]);
        Assert.False(Open().HasOutstanding);
        journal.BeginClaim();
        journal.AbandonClaim();
        Assert.True(Open().HasOutstanding);
        Assert.Null(Open().Acknowledgement);
        Assert.True(File.Exists(audit));
    }

    [Fact]
    public void Manual_acknowledgement_cannot_skip_active_claim_or_payload()
    {
        var journal = Open(legacy: true);
        journal.BeginClaim();
        Assert.Throws<InvalidOperationException>(() => journal.AcknowledgeUncertainty("已核对"));
        journal.CompleteClaim([Job()]);
        Assert.Throws<InvalidOperationException>(() => journal.AcknowledgeUncertainty("已核对"));
        Assert.True(Open().HasOutstanding);
    }

    [Fact]
    public void Invalid_reason_or_audit_failure_preserves_unknown_state()
    {
        var journal = Open(legacy: true);
        Assert.Throws<ArgumentException>(() => journal.AcknowledgeUncertainty(" "));
        Assert.Throws<ArgumentException>(() => journal.AcknowledgeUncertainty(new string('x', 513)));
        File.WriteAllText(Path.Combine(_directory, "recovery-audit"), "blocked");
        Assert.Throws<IOException>(() => journal.AcknowledgeUncertainty("已核对"));
        Assert.True(Open().HasOutstanding);
    }

    [Fact]
    public void Claim_marker_survives_crash_and_cannot_be_cleared_by_another_process()
    {
        Open().BeginClaim();
        var recovered = Open();
        Assert.True(recovered.ClaimUncertain);
        Assert.Throws<InvalidOperationException>(() => recovered.CompleteClaim([]));
        recovered.BeginClaim();
        recovered.CompleteClaim([]);
        Assert.True(Open().ClaimUncertain);
    }

    [Fact]
    public void Empty_successful_claim_clears_only_its_own_marker()
    {
        var journal = Open();
        journal.BeginClaim();
        journal.CompleteClaim([]);
        Assert.False(Open().HasOutstanding);
    }

    [Fact]
    public void Failed_claim_allows_future_routing_but_never_clears_install_block()
    {
        var journal = Open();
        journal.BeginClaim();
        journal.AbandonClaim();
        journal.BeginClaim();
        journal.CompleteClaim([Job()]);
        journal.Submitted("server-job", "local-job", null);
        journal.Reported("server-job");
        Assert.True(Open().HasOutstanding);
    }

    [Fact]
    public void Payload_mapping_and_result_ack_survive_restarts()
    {
        var journal = Open();
        journal.BeginClaim();
        journal.CompleteClaim([Job()]);
        journal = Open();
        Assert.Equal("中文", journal.Snapshot()[0].Job.Labels[0].Data!["code"]);
        Assert.Null(journal.Snapshot()[0].LocalId);
        journal.Submitted("server-job", "local-job", null);
        journal = Open();
        Assert.True(journal.HasOutstanding);
        Assert.Equal("local-job", journal.Snapshot()[0].LocalId);
        journal.Reported("server-job");
        Assert.False(Open().HasOutstanding);
    }

    [Fact]
    public void Failed_submission_waits_for_durable_result_ack()
    {
        var journal = Open();
        journal.BeginClaim();
        journal.CompleteClaim([Job()]);
        journal.Submitted("server-job", null, new("Failed", 0, 1, "模板无效"));
        Assert.Equal("模板无效", Open().Snapshot()[0].Failure!.ErrorMessage);
        Assert.True(Open().HasOutstanding);
    }

    [Fact]
    public void New_claim_cannot_overwrite_pending_batch()
    {
        var journal = Open();
        journal.BeginClaim();
        journal.CompleteClaim([Job()]);
        Assert.Throws<InvalidOperationException>(() => journal.BeginClaim());
        Assert.Throws<InvalidOperationException>(() => journal.CompleteClaim([]));
    }

    [Fact]
    public void Duplicate_server_ids_leave_claim_uncertain()
    {
        var journal = Open();
        journal.BeginClaim();
        Assert.Throws<InvalidDataException>(() => journal.CompleteClaim([Job(), Job()]));
        Assert.True(Open().ClaimUncertain);
    }

    [Fact]
    public void Pending_origin_cannot_be_reassigned()
    {
        Open().BeginClaim();
        Assert.Throws<InvalidDataException>(() => Open("http://other"));
        Assert.True(Open().ClaimUncertain);
    }

    [Fact]
    public void Empty_origin_can_change()
    {
        Open();
        Assert.False(Open("http://other").HasOutstanding);
    }

    [Fact]
    public void Legacy_route_mapping_is_not_assumed_empty()
    {
        Assert.True(Open(legacy: true).ClaimUncertain);
        Assert.True(Open().ClaimUncertain);
    }

    [Fact]
    public void Interrupted_file_write_blocks_recovery()
    {
        Open();
        File.WriteAllText(JournalPath + ".tmp", "incomplete");
        Assert.Throws<InvalidDataException>(() => Open());
    }

    [Fact]
    public void Missing_uncertainty_field_is_corruption_not_idle()
    {
        Open();
        File.WriteAllText(JournalPath, "{\"Format\":1,\"ServerUrl\":\"http://localhost:53961\",\"DeviceId\":\"device\",\"Entries\":[]}");
        Assert.Throws<System.Text.Json.JsonException>(() => Open());
    }

    [Fact]
    public void Persistence_failure_never_releases_in_memory_block()
    {
        var journal = Open();
        Directory.CreateDirectory(JournalPath + ".tmp");
        var error = Record.Exception(() => journal.BeginClaim());
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.True(journal.HasOutstanding);
        Assert.Throws<InvalidOperationException>(() => journal.BeginClaim());
    }
}
