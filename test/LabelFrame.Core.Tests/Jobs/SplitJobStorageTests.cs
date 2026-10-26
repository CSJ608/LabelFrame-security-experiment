using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Tests.Jobs;

/// <summary>验证产品分表初始化、载荷完整性及事务边界。</summary>
public sealed class SplitJobStorageTests
{
    [Fact]
    public async Task Legacy_code_resumes_current_jobs_after_reverse_migration()
    {
        using var db = new LegacyJobDb();
        var history = (await db.Queue.SubmitAsync("history", ["done"])).Job;
        var oldClaim = (await db.Queue.ClaimNextItemAsync())!.Value;
        await db.Queue.CompleteItemAsync(oldClaim.JobId, oldClaim.Item.Id);
        db.ReleasePool();
        var store = new SqliteLabelJobStore(db.Path);
        await store.InitializeAsync();
        var queue = new LabelJobQueue(store);
        var current = (await queue.SubmitAsync("new", new[] { LabelJobPayload.OfBytes([0, 128, 255]), LabelJobPayload.OfText("剩余") })).Job;
        var first = (await queue.ClaimNextItemAsync())!.Value;
        await queue.CompleteItemAsync(first.JobId, first.Item.Id);
        db.ReleasePool();
        await JobStorageMigration.ConvertAsync(db.Path, false);
        await db.Store.InitializeAsync();
        Assert.Equal(LabelJobStatus.Completed, (await db.Queue.GetAsync(history.Id))!.Status);
        Assert.Equal(current.Id, (await db.Queue.SubmitAsync("new", ["不得覆盖"])).Job.Id);
        var next = (await db.Queue.ClaimNextItemAsync())!.Value;
        Assert.Equal(current.Items[1].Id, next.Item.Id);
        Assert.Equal("剩余", next.Item.Zpl);
        await db.Queue.CompleteItemAsync(next.JobId, next.Item.Id);
        Assert.Null(await db.Queue.ClaimNextItemAsync());
        var restored = (await db.Queue.GetAsync(current.Id))!;
        Assert.Equal(LabelJobStatus.Completed, restored.Status);
        Assert.Equal(new byte[] { 0, 128, 255 }, restored.Items[0].CommandBytes);
    }

    [Theory]
    [InlineData("DELETE FROM job_payloads;")]
    [InlineData("INSERT INTO job_payloads VALUES ('orphan','text',NULL);")]
    [InlineData("PRAGMA user_version=7;")]
    public async Task Repeated_initialization_rejects_inconsistent_split_data(string sql)
    {
        using var db = new TempJobDb();
        await db.Queue.SubmitAsync("validate", ["one"]);
        using var connection = Open(db.Path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Store.InitializeAsync());
    }

    [Fact]
    public async Task Repeated_initialization_does_not_require_exclusive_lock()
    {
        using var db = new TempJobDb();
        var job = await db.Queue.SubmitAsync("repeat", ["text"]);
        using var connection = Open(db.Path);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM jobs;";
        Assert.Equal(1L, command.ExecuteScalar());
        await new SqliteLabelJobStore(db.Path).InitializeAsync();
        Assert.NotNull(await db.Store.GetJobAsync(job.Job.Id));
    }

    [Theory]
    [InlineData("full")]
    [InlineData("state")]
    [InlineData("claim")]
    [InlineData("complete")]
    public async Task Missing_payload_is_not_silently_omitted(string operation)
    {
        using var db = new TempJobDb();
        var result = await db.Queue.SubmitAsync("missing", ["first", "second"]);
        var job = result.Job;
        using var connection = Open(db.Path);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM job_payloads WHERE item_id=$id;";
        command.Parameters.AddWithValue("$id", job.Items[0].Id);
        command.ExecuteNonQuery();
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            switch (operation)
            {
                case "full": await db.Store.GetJobAsync(job.Id); break;
                case "state": await db.Store.GetStateAsync(job.Id, default); break;
                case "claim": await db.Store.ClaimNextItemAsync(default); break;
                case "complete": await db.Store.CompleteItemWithoutResultAsync(job.Id, job.Items[1].Id, default); break;
            }
        });
        command.CommandText = "SELECT count(*) FROM job_items WHERE status <> 'Pending';";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Payload_insert_failure_rolls_back_job_and_items()
    {
        using var db = new TempJobDb();
        using var connection = Open(db.Path);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER reject_payload BEFORE INSERT ON job_payloads BEGIN SELECT RAISE(ABORT,'test'); END;";
        command.ExecuteNonQuery();
        await Assert.ThrowsAsync<SqliteException>(() => db.Queue.SubmitAsync("rollback", ["one", "two"]));
        command.CommandText = "SELECT (SELECT count(*) FROM jobs)+(SELECT count(*) FROM job_items)+(SELECT count(*) FROM job_payloads);";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, ForeignKeys = false,
        }.ToString());
        connection.Open();
        return connection;
    }
}
