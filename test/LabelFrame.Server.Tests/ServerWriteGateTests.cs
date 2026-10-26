using System.Reflection;

namespace LabelFrame.Server.Tests;

/// <summary>验证写入口等待、取消与异常释放不会阻塞读操作或其他存储实例。</summary>
public sealed class ServerWriteGateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lf-write-gate-{Guid.NewGuid():N}");

    [Fact]
    public async Task Queued_write_can_cancel_while_reads_and_other_database_continue()
    {
        var db = new ServerDb(Path.Combine(_directory, "first.db"));
        var other = new ServerDb(Path.Combine(_directory, "second.db"));
        await db.InitializeAsync();
        await other.InitializeAsync();
        await db.UpsertDeviceAsync(Device("first"));
        var gate = (SemaphoreSlim)typeof(ServerDb).GetField("_writeGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(db)!;
        await gate.WaitAsync();
        using var cancel = new CancellationTokenSource();
        try
        {
            var pending = db.TouchDeviceAsync("first", DateTimeOffset.UtcNow, cancellationToken: cancel.Token);
            Assert.False(pending.IsCompleted);
            Assert.NotNull(await db.GetDeviceAsync("first").WaitAsync(TimeSpan.FromSeconds(5)));
            await other.UpsertDeviceAsync(Device("other")).WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(0, gate.CurrentCount);
        }
        finally
        {
            gate.Release();
        }
        Assert.Equal(1, await db.TouchDeviceAsync("first", DateTimeOffset.UtcNow).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Failed_write_releases_gate_for_next_write()
    {
        var db = new ServerDb(Path.Combine(_directory, "failure.db"));
        await db.InitializeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => db.UpsertDeviceAsync(null!));
        await db.UpsertDeviceAsync(Device("recovered")).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(await db.GetDeviceAsync("recovered"));
    }

    [Fact]
    public async Task Separate_instances_still_claim_each_job_only_once()
    {
        var path = Path.Combine(_directory, "shared.db");
        var first = new ServerDb(path);
        var second = new ServerDb(path);
        await first.InitializeAsync();
        await first.UpsertDeviceAsync(Device("target"));
        for (var i = 0; i < 20; i++)
        {
            await first.CreateJobAsync(new ServerJob
            {
                Id = $"job-{i}", RequestId = $"request-{i}", TargetDeviceId = "target",
                CreatedAt = DateTimeOffset.UtcNow, TotalItems = 1, PayloadJson = "{}",
            });
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() =>
            (i % 2 == 0 ? first : second).TouchAndClaimPendingJobsAsync("target", DateTimeOffset.UtcNow, null, 10))));
        var ids = results.SelectMany(r => r.Jobs).Select(j => j.Id).ToArray();
        Assert.Equal(20, ids.Length);
        Assert.Equal(20, ids.Distinct().Count());
    }

    private static Device Device(string id) => new()
    {
        Id = id, Name = id, RegisteredAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
    };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
