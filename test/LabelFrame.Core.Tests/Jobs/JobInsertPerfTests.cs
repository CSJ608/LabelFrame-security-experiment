using System.Diagnostics;
using LabelFrame.Core.Jobs;
using Xunit.Abstractions;

namespace LabelFrame.Core.Tests.Jobs;

public class JobInsertPerfTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Measure_small_and_large_batches()
    {
        foreach (var size in new[] { 32, 105000 })
        for (var round = 0; round < 4; round++)
        {
            using var db = new TempJobDb();
            var id = Guid.NewGuid().ToString("N");
            var job = new LabelJob
            {
                Id = id, RequestId = id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                Items = Enumerable.Range(0, 200).Select(i => new LabelJobItem
                {
                    Id = $"{id}-{i}", JobId = id, Index = i,
                    Zpl = i % 2 == 0 ? new string('A', size * 2) : "",
                    CommandBytes = i % 2 == 0 ? null : new byte[size],
                }).ToArray(),
            };
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var task = db.Store.CreateJobAsync(job);
            Assert.True(task.IsCompletedSuccessfully);
            await task;
            watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            output.WriteLine($"size={size}; round={round}; ms={watch.Elapsed.TotalMilliseconds:F2}; bytes={allocated}");
            var stored = await db.Store.GetJobAsync(id);
            for (var i = 0; i < job.Items.Count; i++)
            {
                Assert.Equal(job.Items[i].Zpl, stored!.Items[i].Zpl);
                Assert.Equal(job.Items[i].CommandBytes, stored.Items[i].CommandBytes);
            }
        }
    }
}
