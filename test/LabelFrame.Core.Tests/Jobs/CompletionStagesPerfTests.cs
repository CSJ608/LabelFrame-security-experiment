using System.Diagnostics;
using System.Reflection;
using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace LabelFrame.Core.Tests.Jobs;

public class CompletionStagesPerfTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Measure_completion_transaction_stages()
    {
        var loadState = typeof(SqliteLabelJobStore).GetMethod("LoadStateAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<SqliteConnection, SqliteTransaction?, string, CancellationToken, Task<LabelJobState?>>>();
        var readCompletion = typeof(SqliteLabelJobStore).GetMethod("ReadCompletionStatusAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<SqliteConnection, SqliteTransaction, string, CancellationToken, Task<(bool Exists, LabelJobStatus? NextStatus)>>>();
        for (var round = 1; round <= 4; round++)
        {
            using var db = new TempJobDb();
            var (job, _) = await db.Queue.SubmitAsync("stages", Enumerable.Repeat(new string('A', 210000), 200).ToArray());
            var stages = new Dictionary<string, long>();
            long originalBytes = 0, candidateBytes = 0;
            for (var index = 0; index < 200; index++)
            {
                var next = (await db.Queue.ClaimNextItemAsync())!.Value;
                Assert.Equal(index, next.Item.Index);
                var start = Stopwatch.GetTimestamp();
                await using var connection = await SqliteSupport.OpenAsync(SqliteSupport.BuildConnectionString(db.Path), default);
                Add("open", start);
                start = Stopwatch.GetTimestamp();
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
                Add("begin", start);
                start = Stopwatch.GetTimestamp();
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        UPDATE job_items SET status = 'Completed', error_code = NULL, error_message = NULL,
                            updated_at = $updatedAt WHERE job_id = $jobId AND id = $itemId;
                        """;
                    command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(DateTimeOffset.UtcNow));
                    command.Parameters.AddWithValue("$jobId", job.Id);
                    command.Parameters.AddWithValue("$itemId", next.Item.Id);
                    Assert.Equal(1, await command.ExecuteNonQueryAsync());
                }
                Add("item-update", start);
                LabelJobStatus? status = null, candidate = null;
                foreach (var compact in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
                {
                    var bytes = GC.GetAllocatedBytesForCurrentThread();
                    start = Stopwatch.GetTimestamp();
                    if (compact)
                    {
                        var task = readCompletion(connection, transaction, job.Id, default);
                        Assert.True(task.IsCompletedSuccessfully);
                        var compactState = await task;
                        Assert.True(compactState.Exists);
                        candidate = compactState.NextStatus;
                        Add("compact-state-read", start);
                        candidateBytes += GC.GetAllocatedBytesForCurrentThread() - bytes;
                    }
                    else
                    {
                        var task = loadState(connection, transaction, job.Id, default);
                        Assert.True(task.IsCompletedSuccessfully);
                        var state = (await task)!;
                        status = LabelJobQueue.GetCompletionStatus(state.Status, state.Items.Select(i => i.Status));
                        Add("state-read", start);
                        originalBytes += GC.GetAllocatedBytesForCurrentThread() - bytes;
                    }
                }
                Assert.Equal(status, candidate);
                start = Stopwatch.GetTimestamp();
                if (status is not null)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE jobs SET status = $status, updated_at = $updatedAt WHERE id = $jobId;";
                    command.Parameters.AddWithValue("$status", status.Value.ToString());
                    command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(DateTimeOffset.UtcNow));
                    command.Parameters.AddWithValue("$jobId", job.Id);
                    Assert.Equal(1, await command.ExecuteNonQueryAsync());
                }
                Add("job-update", start);
                start = Stopwatch.GetTimestamp();
                await transaction.CommitAsync();
                Add("commit", start);
            }
            var final = (await db.Queue.GetAsync(job.Id))!;
            Assert.Equal(LabelJobStatus.Completed, final.Status);
            Assert.All(final.Items, item =>
            {
                Assert.Equal(LabelJobItemStatus.Completed, item.Status);
                Assert.Equal(new string('A', 210000), item.Zpl);
            });
            output.WriteLine($"schema=product-split; round={round}; count=200; detailBytes={originalBytes}; compactBytes={candidateBytes}; " + string.Join("; ", stages.Select(s => $"{s.Key}Ms={s.Value * 1000d / Stopwatch.Frequency:F2}")));

            void Add(string name, long start)
                => stages[name] = stages.GetValueOrDefault(name) + Stopwatch.GetTimestamp() - start;
        }
    }
}
