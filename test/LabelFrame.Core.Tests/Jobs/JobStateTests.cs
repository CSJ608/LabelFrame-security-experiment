using System.Text.Json;
using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace LabelFrame.Core.Tests.Jobs;

public class JobStateTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(LabelJobStatus.Pending)]
    [InlineData(LabelJobStatus.Completed)]
    [InlineData(LabelJobStatus.Cancelled)]
    public async Task Completion_should_rollback_when_sibling_status_is_invalid(LabelJobStatus status)
    {
        using var db = new TempJobDb();
        var (job, _) = await db.Queue.SubmitAsync("invalid-status", ["first", "second"]);
        await db.Store.SetJobStatusAsync(job.Id, status);
        await using var connection = new SqliteConnection($"Data Source={db.Path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE job_items SET status = 'unknown' WHERE id = $id;";
        command.Parameters.AddWithValue("$id", job.Items[1].Id);
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => db.Queue.CompleteItemWithoutResultAsync(job.Id, job.Items[0].Id));
        command.CommandText = "SELECT status FROM job_items WHERE id = $id;";
        command.Parameters["$id"].Value = job.Items[0].Id;
        Assert.Equal("Pending", await command.ExecuteScalarAsync());
        command.CommandText = "SELECT status FROM jobs WHERE id = $id;";
        command.Parameters["$id"].Value = job.Id;
        Assert.Equal(status.ToString(), await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Completion_should_not_materialize_sibling_metadata()
    {
        using var db = new TempJobDb();
        var (job, _) = await db.Queue.SubmitAsync("compact-completion", Enumerable.Repeat("payload", 256).ToArray());
        var message = new string('错', 1024);
        await using (var connection = new SqliteConnection($"Data Source={db.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE job_items SET error_code = 'existing', error_message = $message WHERE job_id = $jobId;";
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$jobId", job.Id);
            await command.ExecuteNonQueryAsync();
        }
        await db.Queue.CompleteItemWithoutResultAsync(job.Id, job.Items[0].Id);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var task = db.Queue.CompleteItemWithoutResultAsync(job.Id, job.Items[1].Id);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(task.IsCompletedSuccessfully);
        await task;
        output.WriteLine($"completionBytes={allocated}");
        Assert.True(allocated < 64000, $"完成操作不应构造其他标签的明细与错误文本，实际分配 {allocated} bytes");
        var actual = (await db.Queue.GetAsync(job.Id))!;
        Assert.Equal(LabelJobStatus.Pending, actual.Status);
        Assert.All(actual.Items.Take(2), item =>
        {
            Assert.Equal(LabelJobItemStatus.Completed, item.Status);
            Assert.Null(item.ErrorCode);
            Assert.Null(item.ErrorMessage);
        });
        Assert.All(actual.Items.Skip(2), item =>
        {
            Assert.Equal(LabelJobItemStatus.Pending, item.Status);
            Assert.Equal("existing", item.ErrorCode);
            Assert.Equal(message, item.ErrorMessage);
            Assert.Equal("payload", item.Zpl);
        });
    }

    [Fact]
    public async Task Other_store_implementations_should_use_compatible_full_read_fallback()
    {
        using var db = new TempJobDb();
        using var queue = new LabelJobQueue(new ForwardingStore(db.Store));
        var (job, _) = await queue.SubmitAsync("fallback", ["first", "second"]);
        for (var i = 0; i < 2; i++)
        {
            var item = (await queue.ClaimNextItemAsync())!.Value.Item;
            Assert.Equal(i, item.Index);
            await queue.CompleteItemWithoutResultAsync(job.Id, item.Id);
            await AssertSnapshotMatchesAsync(queue, job.Id);
        }
        Assert.Equal(LabelJobStatus.Completed, (await queue.GetStateAsync(job.Id))!.Status);
        Assert.Equal(new[] { "first", "second" }, (await queue.GetAsync(job.Id))!.Items.Select(i => i.Zpl));
    }

    [Fact]
    public async Task Lightweight_completion_should_survive_restart_and_late_completion_after_cancel()
    {
        using var db = new TempJobDb();
        var (job, _) = await db.Queue.SubmitAsync("recovery", ["first", "second", "third"]);
        var first = (await db.Queue.ClaimNextItemAsync())!.Value;
        await db.Queue.CompleteItemWithoutResultAsync(job.Id, first.Item.Id);
        var inflight = (await db.Queue.ClaimNextItemAsync())!.Value;
        using var reopened = new LabelJobQueue(new SqliteLabelJobStore(db.Path));
        await reopened.MarkInterruptedJobsSuspendedAsync();
        Assert.Null(await reopened.ClaimNextItemAsync());
        await reopened.ResumeAsync(job.Id);
        var recovered = (await reopened.ClaimNextItemAsync())!.Value;
        Assert.Equal(inflight.Item.Id, recovered.Item.Id);
        await reopened.CancelAsync(job.Id);
        await reopened.CompleteItemWithoutResultAsync(job.Id, recovered.Item.Id);
        var state = (await reopened.GetStateAsync(job.Id))!;
        Assert.Equal(LabelJobStatus.Cancelled, state.Status);
        Assert.Equal(new[] { LabelJobItemStatus.Completed, LabelJobItemStatus.Completed, LabelJobItemStatus.Cancelled },
            state.Items.Select(i => i.Status));
        Assert.Null(await reopened.ClaimNextItemAsync());
        await AssertSnapshotMatchesAsync(reopened, job.Id);
    }

    [Fact]
    public async Task Concurrent_readers_should_observe_atomic_final_completion()
    {
        using var db = new TempJobDb();
        var (job, _) = await db.Queue.SubmitAsync("concurrent", Enumerable.Repeat("payload", 50).ToArray());
        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                var item = (await db.Queue.ClaimNextItemAsync())!.Value.Item;
                Assert.Equal(i, item.Index);
                await db.Queue.CompleteItemWithoutResultAsync(job.Id, item.Id);
            }
        });
        var reader = Task.Run(async () =>
        {
            for (var i = 0; i < 200; i++)
            {
                var state = (await db.Queue.GetStateAsync(job.Id))!;
                Assert.Equal(state.Status == LabelJobStatus.Completed,
                    state.Items.All(item => item.Status == LabelJobItemStatus.Completed));
                await Task.Yield();
            }
        });
        await Task.WhenAll(writer, reader);
        Assert.Equal(LabelJobStatus.Completed, (await db.Queue.GetStateAsync(job.Id))!.Status);
    }

    [Fact]
    public async Task Lightweight_completion_should_match_existing_state_transitions()
    {
        using var full = new TempJobDb();
        using var light = new TempJobDb();
        foreach (var status in Enum.GetValues<LabelJobStatus>())
        foreach (var sibling in Enum.GetValues<LabelJobItemStatus>())
        {
            var id = $"{status}-{sibling}";
            foreach (var db in new[] { full, light })
            {
                await db.Queue.SubmitAsync(id, ["first", "second"]);
                var job = (await db.Queue.GetByRequestIdAsync(id))!;
                await db.Store.SetItemStatusAsync(job.Id, job.Items[0].Id, LabelJobItemStatus.Failed, "error", "错误");
                await db.Store.SetItemStatusAsync(job.Id, job.Items[1].Id, sibling, "sibling", "另一张错误");
                await db.Store.SetJobStatusAsync(job.Id, status);
            }
            var original = (await full.Queue.GetByRequestIdAsync(id))!;
            var optimized = (await light.Queue.GetByRequestIdAsync(id))!;
            var expected = await full.Queue.CompleteItemAsync(original.Id, original.Items[0].Id);
            await light.Queue.CompleteItemWithoutResultAsync(optimized.Id, optimized.Items[0].Id);
            var actual = (await light.Queue.GetAsync(optimized.Id))!;
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.Items.Select(i => (i.Index, i.Status, i.Zpl, i.ErrorCode, i.ErrorMessage)),
                actual.Items.Select(i => (i.Index, i.Status, i.Zpl, i.ErrorCode, i.ErrorMessage)));
            if (expected.UpdatedAt == original.UpdatedAt) Assert.Equal(optimized.UpdatedAt, actual.UpdatedAt);
            else Assert.True(actual.UpdatedAt >= optimized.UpdatedAt);
            await AssertSnapshotMatchesAsync(light.Queue, actual.Id);
        }
    }

    [Fact]
    public async Task Snapshot_should_preserve_empty_missing_and_binary_job_metadata()
    {
        using var db = new TempJobDb();
        Assert.Null(await db.Queue.GetStateAsync("absent"));
        await db.Store.CreateJobAsync(new LabelJob { Id = "empty", RequestId = "empty", Items = [] });
        await AssertSnapshotMatchesAsync(db.Queue, "empty");
        var (job, _) = await db.Queue.SubmitAsync("mixed", [LabelJobPayload.OfText("中文"), LabelJobPayload.OfBytes([0, 255, 128])]);
        var first = (await db.Queue.ClaimNextItemAsync())!.Value;
        await db.Queue.FailItemAsync(job.Id, first.Item.Id, "error", "第一张失败");
        await AssertSnapshotMatchesAsync(db.Queue, job.Id);
        await db.Queue.ResumeAsync(job.Id);
        var next = (await db.Queue.ClaimNextItemAsync())!.Value;
        await db.Queue.CompleteItemWithoutResultAsync(job.Id, next.Item.Id);
        Assert.Equal(LabelJobStatus.Failed, (await db.Queue.GetStateAsync(job.Id))!.Status);
        await AssertSnapshotMatchesAsync(db.Queue, job.Id);
        var full = (await db.Queue.GetAsync(job.Id))!;
        Assert.Equal("中文", full.Items[0].Zpl);
        Assert.Equal(new byte[] { 0, 255, 128 }, full.Items[1].CommandBytes);
    }

    [Fact]
    public async Task Completion_failure_should_rollback_both_states_and_release_gate()
    {
        using var db = new TempJobDb();
        var (job, _) = await db.Queue.SubmitAsync("rollback", ["first"]);
        await db.Queue.ClaimNextItemAsync();
        await using var connection = new SqliteConnection($"Data Source={db.Path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER reject_complete BEFORE UPDATE ON jobs BEGIN SELECT RAISE(ABORT, 'rejected'); END;";
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.Queue.CompleteItemWithoutResultAsync(job.Id, job.Items[0].Id));
        var unchanged = (await db.Queue.GetStateAsync(job.Id))!;
        Assert.Equal(LabelJobStatus.Printing, unchanged.Status);
        Assert.Equal(LabelJobItemStatus.Printing, unchanged.Items[0].Status);
        command.CommandText = "DROP TRIGGER reject_complete;";
        await command.ExecuteNonQueryAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Queue.CompleteItemWithoutResultAsync(job.Id, job.Items[0].Id, cancelled.Token));
        await db.Queue.CompleteItemWithoutResultAsync(job.Id, job.Items[0].Id);
        Assert.Equal(LabelJobStatus.Completed, (await db.Queue.GetStateAsync(job.Id))!.Status);
        var missing = await Assert.ThrowsAsync<LabelJobException>(() => db.Queue.CompleteItemWithoutResultAsync(job.Id, "absent"));
        Assert.Equal(JobErrorCodes.JobNotFound, missing.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task State_reads_and_completion_should_not_allocate_batch_payloads(bool binary)
    {
        using var db = new TempJobDb();
        var payload = binary ? LabelJobPayload.OfBytes(new byte[131072]) : LabelJobPayload.OfText(new string('A', 131072));
        var (job, _) = await db.Queue.SubmitAsync("allocation", Enumerable.Repeat(payload, 64).ToArray());
        await db.Queue.GetStateAsync(job.Id);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var query = db.Queue.GetStateAsync(job.Id);
        var queryBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(query.IsCompletedSuccessfully);
        Assert.Equal(64, (await query)!.Items.Count);
        var item = (await db.Queue.ClaimNextItemAsync())!.Value.Item;
        before = GC.GetAllocatedBytesForCurrentThread();
        var complete = db.Queue.CompleteItemWithoutResultAsync(job.Id, item.Id);
        var completeBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(complete.IsCompletedSuccessfully);
        await complete;
        output.WriteLine($"binary={binary}; queryBytes={queryBytes}; completeBytes={completeBytes}");
        Assert.InRange(queryBytes, 1, 1_000_000);
        Assert.InRange(completeBytes, 1, 1_000_000);
        await AssertSnapshotMatchesAsync(db.Queue, job.Id);
    }

    private static async Task AssertSnapshotMatchesAsync(LabelJobQueue queue, string jobId)
    {
        var expected = LabelJobState.From((await queue.GetAsync(jobId))!);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(await queue.GetStateAsync(jobId)));
    }

    private sealed class ForwardingStore(ILabelJobStore inner) : ILabelJobStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => inner.InitializeAsync(cancellationToken);
        public Task<LabelJob> CreateJobAsync(LabelJob job, CancellationToken cancellationToken = default) => inner.CreateJobAsync(job, cancellationToken);
        public Task<LabelJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default) => inner.GetJobAsync(jobId, cancellationToken);
        public Task<LabelJob?> GetJobByRequestIdAsync(string requestId, CancellationToken cancellationToken = default) => inner.GetJobByRequestIdAsync(requestId, cancellationToken);
        public Task<LabelJob?> SetJobStatusAsync(string jobId, LabelJobStatus status, CancellationToken cancellationToken = default) => inner.SetJobStatusAsync(jobId, status, cancellationToken);
        public Task<LabelJob?> SetItemStatusAsync(string jobId, string itemId, LabelJobItemStatus status, string? errorCode, string? errorMessage, CancellationToken cancellationToken = default)
            => inner.SetItemStatusAsync(jobId, itemId, status, errorCode, errorMessage, cancellationToken);
        public Task<IReadOnlyList<LabelJob>> ListJobsByStatusAsync(LabelJobStatus status, CancellationToken cancellationToken = default) => inner.ListJobsByStatusAsync(status, cancellationToken);
        public Task<IReadOnlyList<LabelJob>> ListRecentAsync(int limit, CancellationToken cancellationToken = default) => inner.ListRecentAsync(limit, cancellationToken);
        public Task<bool> HasPendingItemsAsync(CancellationToken cancellationToken = default) => inner.HasPendingItemsAsync(cancellationToken);
    }
}
