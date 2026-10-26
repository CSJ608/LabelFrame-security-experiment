using System.Diagnostics;
using LabelFrame.Core.Jobs;
using Xunit.Abstractions;

namespace LabelFrame.Core.Tests.Jobs;

public class QueueStorePerfTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(50, false)]
    [InlineData(200, false)]
    [InlineData(50, true)]
    [InlineData(200, true)]
    public async Task Measure_claim_complete_and_query_separately(int count, bool lightweight)
    {
        using var db = new TempJobDb();
        var payload = new string('A', 210000);
        var (job, _) = await db.Queue.SubmitAsync("phases", Enumerable.Repeat(payload, count).ToArray());
        long claimTicks = 0, completeTicks = 0, queryTicks = 0;
        long claimBytes = 0, completeBytes = 0, queryBytes = 0;
        for (var i = 0; i < count; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            var claim = await db.Queue.ClaimNextItemAsync();
            claimTicks += Stopwatch.GetTimestamp() - started;
            claimBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.NotNull(claim);
            Assert.Equal(i, claim.Value.Item.Index);
            before = GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            if (lightweight) await db.Queue.CompleteItemWithoutResultAsync(job.Id, claim.Value.Item.Id);
            else await db.Queue.CompleteItemAsync(job.Id, claim.Value.Item.Id);
            completeTicks += Stopwatch.GetTimestamp() - started;
            completeBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            var snapshot = lightweight ? await db.Queue.GetStateAsync(job.Id)
                : LabelJobState.From((await db.Queue.GetAsync(job.Id))!);
            queryTicks += Stopwatch.GetTimestamp() - started;
            queryBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(i + 1, snapshot!.Items.Count(item => item.Status == LabelJobItemStatus.Completed));
        }
        output.WriteLine($"count={count}; lightweight={lightweight}; claimMs={claimTicks * 1000d / Stopwatch.Frequency:F2}; completeMs={completeTicks * 1000d / Stopwatch.Frequency:F2}; queryMs={queryTicks * 1000d / Stopwatch.Frequency:F2}");
        output.WriteLine($"claimBytes={claimBytes}; completeBytes={completeBytes}; queryBytes={queryBytes}");
    }
}
