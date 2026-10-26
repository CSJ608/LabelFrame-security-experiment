using System.Diagnostics;
using LabelFrame.Core.Jobs;
using Xunit.Abstractions;

namespace LabelFrame.Core.Tests.Jobs;

/// <summary>串行对照旧大行与产品分表的真实存储调用。</summary>
public sealed class ProductSplitPerfTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Compare_product_storage_with_legacy_in_alternating_order()
    {
        foreach (var binary in new[] { false, true })
        for (var round = 0; round < 4; round++)
        foreach (var split in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            using var db = new LegacyJobDb();
            var product = new SqliteLabelJobStore(db.Path);
            if (split)
            {
                db.ReleasePool();
                await product.InitializeAsync();
            }
            var queue = new LabelJobQueue(split ? product : db.Store);
            var payload = binary ? LabelJobPayload.OfBytes(Enumerable.Range(0, 105000).Select(i => (byte)i).ToArray())
                : LabelJobPayload.OfText(new string('A', 210000));
            var start = Stopwatch.GetTimestamp();
            var job = (await queue.SubmitAsync("perf", Enumerable.Repeat(payload, 200).ToArray())).Job;
            var submitMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long claimTicks = 0, completeTicks = 0;
            for (var index = 0; index < 200; index++)
            {
                start = Stopwatch.GetTimestamp();
                var claim = (split ? await product.ClaimNextItemAsync(default) : await db.Store.ClaimNextItemAsync(default))!.Value;
                claimTicks += Stopwatch.GetTimestamp() - start;
                Assert.Equal(job.Items[index].Id, claim.Item.Id);
                Assert.Equal(payload.Text, claim.Item.Zpl);
                Assert.Equal(payload.Bytes, claim.Item.CommandBytes);
                start = Stopwatch.GetTimestamp();
                Assert.True(split ? await product.CompleteItemWithoutResultAsync(claim.JobId, claim.Item.Id, default)
                    : await db.Store.CompleteItemWithoutResultAsync(claim.JobId, claim.Item.Id, default));
                completeTicks += Stopwatch.GetTimestamp() - start;
            }
            Assert.Null(split ? await product.ClaimNextItemAsync(default) : await db.Store.ClaimNextItemAsync(default));
            var final = (await queue.GetAsync(job.Id))!;
            Assert.Equal(LabelJobStatus.Completed, final.Status);
            Assert.All(final.Items, item =>
            {
                Assert.Equal(LabelJobItemStatus.Completed, item.Status);
                Assert.Equal(payload.Text, item.Zpl);
                Assert.Equal(payload.Bytes, item.CommandBytes);
            });
            output.WriteLine($"round={round}; split={split}; binary={binary}; submitMs={submitMs:F2}; claimMs={claimTicks * 1000d / Stopwatch.Frequency:F2}; completeMs={completeTicks * 1000d / Stopwatch.Frequency:F2}");
        }
    }
}
