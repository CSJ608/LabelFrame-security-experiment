using LabelFrame.Api;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Server;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// server_jobs.error_code 落库（· ，， ③ /）：
/// 旧版本库（无 error_code 列）升级自动补列、历史行为不变；新终态行含 error_code（可空）——
/// 宿主回报可选携带、失联回收 LF_SRV_009、超期放弃 LF_SRV_011。
/// </summary>
public sealed class ServerJobErrorCodeTests
{
    private static LabelContract SampleContract { get; } = new()
    {
        Name = "errcode-label",
        Version = "1.0",
        Fields = [new LabelField { Key = "code", DisplayName = "编码", IsRequired = true }],
    };

    private static LabelLayout SampleLayout { get; } = new()
    {
        Name = "errcode-40x20",
        ContractName = "errcode-label",
        ContractVersion = "1.0",
        WidthMm = 40,
        HeightMm = 20,
        Elements = [new LabelTextElement { SourceKey = "code", XMm = 2, YMm = 2, FontHeightMm = 4, FontWidthMm = 4 }],
    };

    private static string TempDbPath(string prefix)
        => Path.Combine(Path.GetTempPath(), $"lfsrv-errcode-{prefix}-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Initialize_should_migrate_legacy_server_jobs_without_error_code()
    {
        // 旧版本库：server_jobs 无 error_code 列（含 error_message / callback_url，即 形态）
        var path = TempDbPath("mig");
        try
        {
            var legacy = new ServerDb(path);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE server_jobs (
                        id              TEXT PRIMARY KEY,
                        request_id      TEXT NOT NULL UNIQUE,
                        target_device_id TEXT NOT NULL,
                        status          TEXT NOT NULL,
                        created_at      TEXT NOT NULL,
                        claimed_at      TEXT NULL,
                        finished_at     TEXT NULL,
                        total_items     INTEGER NOT NULL,
                        completed_items INTEGER NOT NULL DEFAULT 0,
                        failed_items    INTEGER NOT NULL DEFAULT 0,
                        error_message   TEXT NULL,
                        payload_json    TEXT NOT NULL,
                        callback_url    TEXT NULL
                    );
                    INSERT INTO server_jobs (id, request_id, target_device_id, status, created_at, total_items, payload_json, error_message)
                    VALUES ('legacy-1', 'req-legacy-1', 'dev-1', 'Failed', '2026-01-01T00:00:00+00:00', 1, '{}', '历史中文失败原因');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await legacy.InitializeAsync();

            // 历史行为不变：旧行可读，error_message 中文存量原样保留，error_code 为 NULL（不迁移）
            var job = await legacy.GetJobAsync("legacy-1");
            Assert.NotNull(job);
            Assert.Equal("历史中文失败原因", job!.ErrorMessage);
            Assert.Null(job.ErrorCode);

            // 新终态行可写 error_code（升级后行为完整）
            var updated = await legacy.UpdateJobResultAsync(
                "legacy-1", ServerJobStatus.Failed, 0, 1, "新失败原因", DateTimeOffset.UtcNow, "LF_IO_001");
            Assert.Equal("LF_IO_001", updated!.ErrorCode);
            Assert.Equal("新失败原因", updated.ErrorMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Report_result_should_persist_optional_error_code()
    {
        using var server = new TempServer();
        var job = await CreateAndClaimAsync(server);

        // 宿主回报携带 errorCode（加法）：随终态落库
        var failed = await server.Service.ReportResultAsync(
            "dev-1", job.JobId,
            new ReportResultRequest("Failed", 0, 1, "发送失败：打印机离线。", "LF_IO_001"));
        Assert.Equal("Failed", failed.Status);
        var stored = await server.Db.GetJobAsync(job.JobId);
        Assert.Equal("LF_IO_001", stored!.ErrorCode);
        Assert.Equal("发送失败：打印机离线。", stored.ErrorMessage);

        // 旧宿主（不发送 errorCode）：字段缺省为 null，行为与现状一致
        var second = await CreateAndClaimAsync(server);
        var completed = await server.Service.ReportResultAsync(
            "dev-1", second.JobId, new ReportResultRequest("Completed", 1, 0, null));
        Assert.Equal("Completed", completed.Status);
        Assert.Null((await server.Db.GetJobAsync(second.JobId))!.ErrorCode);
    }

    [Fact]
    public async Task Timeout_recycle_should_store_host_lost_timeout_code()
    {
        using var server = new TempServer(new FakeTimeProvider());
        var job = await CreateAndClaimAsync(server);
        var now = DateTimeOffset.UtcNow;

        var count = await server.Db.MarkTimedOutClaimedJobsAsync(now, now.AddMinutes(1), "宿主失联超时（LF_SRV_009）：…", ServerErrorCodes.HostLostTimeout);

        Assert.Equal(1, count);
        var stored = await server.Db.GetJobAsync(job.JobId);
        Assert.Equal("LF_SRV_009", stored!.ErrorCode);
    }

    [Fact]
    public async Task Pending_expiration_should_store_pending_expired_code()
    {
        using var server = new TempServer();
        await server.Service.RegisterDeviceAsync("dev-1", "设备一");
        var job = await server.Service.SubmitJobAsync(CreateRequest("req-exp-1"));

        var now = DateTimeOffset.UtcNow;
        var count = await server.Db.MarkExpiredJobsAsync(now, now.AddHours(1), "暂存超过 2 小时未投递…", ServerErrorCodes.PendingExpired);

        Assert.Equal(1, count);
        var stored = await server.Db.GetJobAsync(job!.JobId);
        Assert.Equal("LF_SRV_011", stored!.ErrorCode);
    }

    private static SubmitJobRequest CreateRequest(string requestId) => new(
        requestId,
        new TemplateDto(SampleContract, SampleLayout),
        [new LabelDto(new Dictionary<string, string> { ["code"] = "A" })],
        TargetDeviceId: "dev-1");

    private static async Task<ServerJobView> CreateAndClaimAsync(TempServer server)
    {
        await server.Service.RegisterDeviceAsync("dev-1", "设备一");
        var job = await server.Service.SubmitJobAsync(CreateRequest($"req-{Guid.NewGuid():N}"));
        var claimed = await server.Service.ClaimPendingJobsAsync("dev-1");
        Assert.Single(claimed);
        return job!;
    }
}
