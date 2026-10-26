using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Tests.Jobs;

public class SqliteQueueClaimTests
{
    [Fact]
    public async Task Reopened_store_should_preserve_binary_payload_and_recover_inflight_item()
    {
        using var db = new TempJobDb();
        byte[] bytes = [0, 255, 128, 10, 13];
        var (job, _) = await db.Queue.SubmitAsync("binary-restart", [LabelJobPayload.OfBytes(bytes)]);
        var first = await db.Queue.ClaimNextItemAsync();
        Assert.Equal(bytes, first!.Value.Item.CommandBytes);
        first.Value.Item.CommandBytes![0] = 42;
        using var reopened = new LabelJobQueue(new SqliteLabelJobStore(db.Path));
        await reopened.MarkInterruptedJobsSuspendedAsync();
        Assert.Null(await reopened.ClaimNextItemAsync());
        await reopened.ResumeAsync(job.Id);
        var recovered = await reopened.ClaimNextItemAsync();
        Assert.Equal(first.Value.Item.Id, recovered!.Value.Item.Id);
        Assert.Equal(bytes, recovered.Value.Item.CommandBytes);
        Assert.Null(recovered.Value.Item.ErrorCode);
        Assert.Null(recovered.Value.Item.ErrorMessage);
        var completed = await reopened.CompleteItemAsync(job.Id, recovered.Value.Item.Id);
        Assert.Equal(LabelJobStatus.Completed, completed.Status);
        Assert.Null(await reopened.ClaimNextItemAsync());
    }

    [Fact]
    public async Task Failed_job_update_should_rollback_item_and_release_queue()
    {
        using var db = new TempJobDb();
        var (job, _) = await db.Queue.SubmitAsync("rollback", ["first", "second"]);
        await using var connection = new SqliteConnection($"Data Source={db.Path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER reject_claim BEFORE UPDATE ON jobs
            BEGIN SELECT RAISE(ABORT, 'claim rejected'); END;
            """;
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.Queue.ClaimNextItemAsync());
        var unchanged = await db.Store.GetJobAsync(job.Id);
        Assert.Equal(LabelJobStatus.Pending, unchanged!.Status);
        Assert.All(unchanged.Items, i => Assert.Equal(LabelJobItemStatus.Pending, i.Status));
        command.CommandText = "DROP TRIGGER reject_claim;";
        await command.ExecuteNonQueryAsync();
        Assert.Equal(0, (await db.Queue.ClaimNextItemAsync())!.Value.Item.Index);
    }

    [Fact]
    public async Task Cancelled_claim_should_not_change_state_or_hold_gate()
    {
        using var db = new TempJobDb();
        var (job, _) = await db.Queue.SubmitAsync("cancel", ["first"]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Queue.ClaimNextItemAsync(cancellation.Token));
        Assert.Equal(LabelJobStatus.Pending, (await db.Store.GetJobAsync(job.Id))!.Status);
        Assert.Equal(0, (await db.Queue.ClaimNextItemAsync())!.Value.Item.Index);
    }

    [Fact]
    public async Task Separate_queues_should_not_claim_same_item()
    {
        using var db = new TempJobDb();
        using var other = new LabelJobQueue(new SqliteLabelJobStore(db.Path));
        await db.Queue.SubmitAsync("concurrent", Enumerable.Range(0, 20).Select(i => $"text-{i}").ToArray());
        var claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(
            () => (i % 2 == 0 ? db.Queue : other).ClaimNextItemAsync())));
        Assert.All(claims, claim => Assert.NotNull(claim));
        Assert.Equal(Enumerable.Range(0, 20), claims.Select(c => c!.Value.Item.Index).Order());
        Assert.Equal(20, claims.Select(c => c!.Value.Item.Id).Distinct().Count());
        Assert.Null(await db.Queue.ClaimNextItemAsync());
    }

    [Fact]
    public async Task Claim_should_preserve_job_tie_order_and_skip_suspended_jobs()
    {
        using var db = new TempJobDb();
        var createdAt = DateTimeOffset.UtcNow;
        foreach (var id in new[] { "b", "a", "c" })
        {
            await db.Store.CreateJobAsync(new LabelJob
            {
                Id = id, RequestId = id, CreatedAt = createdAt, UpdatedAt = createdAt,
                Items = [new LabelJobItem { Id = $"{id}-0", JobId = id, Zpl = id }],
            });
        }
        await db.Queue.SuspendAsync("a");
        Assert.Equal("b", (await db.Queue.ClaimNextItemAsync())!.Value.JobId);
        await db.Queue.ResumeAsync("a");
        Assert.Equal("a", (await db.Queue.ClaimNextItemAsync())!.Value.JobId);
        Assert.Equal("c", (await db.Queue.ClaimNextItemAsync())!.Value.JobId);
        Assert.Null(await db.Queue.ClaimNextItemAsync());
    }
}
