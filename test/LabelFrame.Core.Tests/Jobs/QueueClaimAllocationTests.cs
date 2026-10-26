using LabelFrame.Core.Jobs;
using Xunit.Abstractions;

namespace LabelFrame.Core.Tests.Jobs;

public class QueueClaimAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Claim_should_read_only_selected_payload(bool binary)
    {
        using var db = new TempJobDb();
        var payload = binary
            ? LabelJobPayload.OfBytes(Enumerable.Repeat((byte)0xa5, 131072).ToArray())
            : LabelJobPayload.OfText(new string('A', 131072));
        await db.Queue.SubmitAsync("large", Enumerable.Repeat(payload, 64).ToArray());
        await db.Queue.ClaimNextItemAsync();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var claimTask = db.Queue.ClaimNextItemAsync();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // 此预算只覆盖同步完成的 SQLite 调用，异步切线程不得冒充低分配。
        Assert.True(claimTask.IsCompletedSuccessfully);
        var claimed = await claimTask;
        output.WriteLine($"binary={binary}, allocated={allocated}");
        Assert.NotNull(claimed);
        Assert.Equal(1, claimed.Value.Item.Index);
        Assert.Equal(LabelJobItemStatus.Printing, claimed.Value.Item.Status);
        if (binary) Assert.Equal(payload.Bytes, claimed.Value.Item.CommandBytes);
        else Assert.Equal(payload.Text, claimed.Value.Item.Zpl);
        Assert.InRange(allocated, 1, 2_000_000);
    }
}
