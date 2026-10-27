using LabelFrame.Updates;

namespace LabelFrame.Server.Tests;

/// <summary>验证 Android 发布快照、候选资格和有界自报行为。</summary>
public sealed class AndroidUpdatesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfandroidupdates-" + Guid.NewGuid().ToString("N"));
    internal static readonly AndroidUpdatePackage Package = new(AndroidUpdateContract.PackageName, 10502, "1.5.2", 23, 36,
        ["arm64-v8a"], new string('A', 64), new string('B', 64), 3, new(1, "1.5.1", "2.0.0"));
    internal static readonly AndroidUpdateQuery Query = new("pda-a", 10500, 30, "arm64-v8a", new string('A', 64));
    public AndroidUpdatesTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);
    private async Task<string> Draft()
    {
        var path = Path.Combine(_root, "untrusted-name.apk");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        return path;
    }

    [Fact]
    public async Task Snapshot_is_independent_and_same_content_can_be_published_to_another_batch()
    {
        var source = await Draft();
        var directory = Path.Combine(_root, "published");
        using var service = new AndroidUpdatesService(directory, new Inspector(Package));
        var first = await service.PublishAsync(source, ["pda-a"], false, "confirm", CancellationToken.None);
        var second = await service.PublishAsync(source, ["pda-b"], false, "confirm", CancellationToken.None);
        await File.WriteAllBytesAsync(source, [9, 9, 9]);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(service.DownloadPath(first.Id, Query)!));
        Assert.Equal(second.Id, service.Select(Query with { DeviceId = "pda-b" })?.Id);
        using var reloaded = new AndroidUpdatesService(directory, new Inspector(Package with { Sha256 = new string('C', 64) }));
        Assert.Equal(first.Id, reloaded.Select(Query)?.Id);
        await Assert.ThrowsAsync<InvalidDataException>(() => reloaded.PublishAsync(source, ["pda-a"], false, "confirm", CancellationToken.None));
        Assert.Equal(2, reloaded.List().Length);
        Assert.Equal(2, Directory.GetFiles(directory, "*.apk").Length);
    }

    [Fact]
    public async Task Candidate_and_download_both_enforce_pause_batch_version_system_abi_and_publisher()
    {
        using var service = new AndroidUpdatesService(Path.Combine(_root, "published"), new Inspector(Package));
        var release = await service.PublishAsync(await Draft(), ["pda-a"], false, "confirm", CancellationToken.None);
        foreach (var query in new[] { Query with { DeviceId = "other" }, Query with { CurrentVersionCode = 10502 },
                     Query with { CurrentVersionCode = 10600 }, Query with { Sdk = 22 }, Query with { Abi = "x86_64" },
                     Query with { PublisherSha256 = new string('C', 64) }, Query with { CurrentVersionCode = -1 } })
        {
            Assert.Null(service.Select(query));
            Assert.Null(service.DownloadPath(release.Id, query));
        }
        await service.PauseAsync(release.Id, true, CancellationToken.None);
        Assert.Null(service.Select(Query));
        Assert.Null(service.DownloadPath(release.Id, Query));
        await service.PauseAsync(release.Id, false, CancellationToken.None);
        Assert.Equal(release.Id, service.Select(Query)?.Id);
    }

    [Theory]
    [InlineData("maintenance", true)]
    [InlineData("confirm", false)]
    public async Task Silent_mode_and_unconfirmed_global_release_are_rejected(string mode, bool all)
    {
        using var service = new AndroidUpdatesService(Path.Combine(_root, "published"), new Inspector(Package));
        var source = await Draft();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PublishAsync(source, [], all, mode, CancellationToken.None));
        Assert.Empty(service.List());
    }

    [Fact]
    public async Task Storage_failure_releases_gate_and_incompatible_package_is_not_committed()
    {
        var source = await Draft();
        var directory = Path.Combine(_root, "published");
        await File.WriteAllTextAsync(directory, "blocked");
        using var service = new AndroidUpdatesService(directory, new Inspector(Package));
        await Assert.ThrowsAnyAsync<IOException>(() => service.PublishAsync(source, [], true, "confirm", CancellationToken.None));
        File.Delete(directory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.NotNull(await service.PublishAsync(source, [], true, "confirm", deadline.Token));
        using var invalid = new AndroidUpdatesService(Path.Combine(_root, "incompatible"), new Inspector(Package with { Compatibility = new(1, "2.0.0", "3.0.0") }));
        await Assert.ThrowsAsync<InvalidDataException>(() => invalid.PublishAsync(source, [], true, "confirm", CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "incompatible")));
    }

    [Fact]
    public void Reports_are_bounded_timestamped_and_do_not_modify_releases()
    {
        using var service = new AndroidUpdatesService(Path.Combine(_root, "published"), new Inspector(Package));
        var before = DateTimeOffset.UtcNow;
        Assert.False(service.Report(new("pda", 1, new string('x', 65))));
        for (var i = 0; i < 1000; i++) Assert.True(service.Report(new("pda-" + i, 1, "downloaded")));
        Assert.False(service.Report(new("overflow", 1, "healthy")));
        Assert.True(service.Report(new("pda-0", 2, "healthy")));
        Assert.All(service.Reports(), r => Assert.True(r.ReceivedAt >= before));
        Assert.Empty(service.List());
    }

    internal sealed class Inspector(AndroidUpdatePackage package) : IAndroidUpdatePackageInspector
    {
        public Task<AndroidUpdatePackage> InspectAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(package);
    }
}
