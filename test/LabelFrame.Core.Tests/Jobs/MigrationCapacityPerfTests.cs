using System.Diagnostics;
using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace LabelFrame.Core.Tests.Jobs;

/// <summary>测量历史作业库转换时间与数据库目录的采样占用。</summary>
public sealed class MigrationCapacityPerfTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Measure_current_database_roundtrip_with_history(bool binary)
    {
        using var db = new LegacyJobDb();
        var jobs = new List<string>();
        var payload = binary ? LabelJobPayload.OfBytes(Enumerable.Range(0, 105000).Select(i => (byte)i).ToArray())
            : LabelJobPayload.OfText(new string('文', 70000));
        for (var batch = 0; batch < 5; batch++)
        {
            var job = (await db.Queue.SubmitAsync("history-" + batch,
                Enumerable.Repeat(payload, 200).ToArray())).Job;
            jobs.Add(job.Id);
        }
        using (var connection = new SqliteConnection(SqliteSupport.BuildConnectionString(db.Path)))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE jobs SET status='Completed'; UPDATE job_items SET status='Completed';";
            command.ExecuteNonQuery();
        }
        db.ReleasePool();

        foreach (var toSplit in new[] { true, false })
        {
            var before = DirectoryBytes(db.DirectoryPath);
            using var done = new CancellationTokenSource();
            var samples = new System.Collections.Concurrent.ConcurrentQueue<long>();
            var sampler = Task.Run(async () =>
            {
                while (!done.IsCancellationRequested)
                {
                    samples.Enqueue(DirectoryBytes(db.DirectoryPath));
                    try { await Task.Delay(10, done.Token); }
                    catch (OperationCanceledException) when (done.IsCancellationRequested) { break; }
                }
            });
            var start = Stopwatch.GetTimestamp();
            string? backup;
            try
            {
                backup = await JobStorageMigration.ConvertAsync(db.Path, toSplit,
                    checkpoint: (_, _) => samples.Enqueue(DirectoryBytes(db.DirectoryPath)));
            }
            finally
            {
                done.Cancel();
                await sampler;
            }
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert.NotNull(backup);
            var after = DirectoryBytes(db.DirectoryPath);
            var sampledPeak = Math.Max(Math.Max(before, after), samples.Max());
            output.WriteLine($"binary={binary}; direction={(toSplit ? "split" : "legacy")}; items=1000; elapsedMs={elapsed:F2}; beforeBytes={before}; afterBytes={after}; sampledPeakBytes={sampledPeak}; sampledExtraBytes={sampledPeak - before}; backupBytes={new FileInfo(backup).Length}; samples={samples.Count}; sampleIntervalMs=10");

            ILabelJobStore reader = toSplit ? new SqliteLabelJobStore(db.Path) : db.Store;
            await reader.InitializeAsync();
            foreach (var id in jobs)
            {
                var job = (await reader.GetJobAsync(id))!;
                Assert.Equal(LabelJobStatus.Completed, job.Status);
                Assert.Equal(200, job.Items.Count);
                Assert.All(job.Items, item =>
                {
                    Assert.Equal(LabelJobItemStatus.Completed, item.Status);
                    Assert.Equal(payload.Text, item.Zpl);
                    Assert.Equal(payload.Bytes, item.CommandBytes);
                });
            }
            db.ReleasePool();
        }
    }

    private static long DirectoryBytes(string path)
    {
        long total = 0;
        foreach (var name in Directory.EnumerateFiles(path))
        {
            try { total += new FileInfo(name).Length; }
            catch (FileNotFoundException) { }
        }
        return total;
    }
}
