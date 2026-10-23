using System.Net;
using LabelFrame.Server.Api;
using LabelFrame.Updates;
using Microsoft.AspNetCore.Http;

namespace LabelFrame.Server.Tests;

/// <summary>更新发布的权限、不可变快照和设备资格验证。</summary>
public sealed class ClientUpdatesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfupdates-" + Guid.NewGuid().ToString("N"));
    private static readonly ClientUpdatePackage Package = new("1.5.0", Guid.NewGuid().ToString(), "x64", "publisher", "hash", 3, "1.0.0", "2.0.0", 10);
    public ClientUpdatesTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task Storage_failure_does_not_block_later_publication_after_repair()
    {
        var source = Path.Combine(_root, "draft.msi");
        var destination = Path.Combine(_root, "published");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await File.WriteAllTextAsync(destination, "blocked directory");
        using var service = new ClientUpdatesService(destination, new Inspector(Package));
        await Assert.ThrowsAnyAsync<IOException>(() => service.PublishAsync(source, "LabelFrame-Client-1.5.0.msi", [], TestContextToken));
        File.Delete(destination);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var release = await service.PublishAsync(source, "LabelFrame-Client-1.5.0.msi", [], deadline.Token);
        Assert.Equal(release.Id, service.Select("device-a", "1.4.0")?.Id);
    }

    [Fact]
    public async Task Published_snapshot_survives_draft_overwrite_and_cannot_be_republished()
    {
        var source = Path.Combine(_root, "draft.msi");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        using var service = new ClientUpdatesService(Path.Combine(_root, "published"), new Inspector(Package));
        var release = await service.PublishAsync(source, "LabelFrame-Client-1.5.0.msi", [], TestContextToken);
        await File.WriteAllBytesAsync(source, [4, 5, 6]);
        var path = service.DownloadPath(release.Id, "device-a", "1.4.0");
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path!));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PublishAsync(source, release.FileName, [], TestContextToken));
        Assert.Single(service.List());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "published"), "*.msi"));
        using var reloaded = new ClientUpdatesService(Path.Combine(_root, "published"), new Inspector(Package));
        Assert.Equal(release.Id, reloaded.Select("device-a", "1.4.0")?.Id);
    }

    [Fact]
    public async Task Pausing_scope_and_version_control_both_query_and_download()
    {
        var source = Path.Combine(_root, "draft.msi");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        using var service = new ClientUpdatesService(Path.Combine(_root, "published"), new Inspector(Package));
        var release = await service.PublishAsync(source, "LabelFrame-Client-1.5.0.msi", ["device-a"], TestContextToken);
        Assert.Null(service.Select("device-b", "1.4.0"));
        Assert.Null(service.DownloadPath(release.Id, "device-b", "1.4.0"));
        Assert.Null(service.Select("device-a", "1.5.0"));
        Assert.Null(service.Select("device-a", "2.0.0"));
        await service.PauseAsync(release.Id, true, TestContextToken);
        Assert.Null(service.Select("device-a", "1.4.0"));
        Assert.Null(service.DownloadPath(release.Id, "device-a", "1.4.0"));
        await service.PauseAsync(release.Id, false, TestContextToken);
        Assert.NotNull(service.Select("device-a", "1.4.0"));
    }

    [Fact]
    public async Task Rejected_package_or_incompatible_server_never_becomes_published()
    {
        var source = Path.Combine(_root, "draft.msi");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        using var service = new ClientUpdatesService(Path.Combine(_root, "published"), new Inspector(Package with { MinServerVersion = "2.0.0", MaxServerVersion = "3.0.0" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PublishAsync(source, "LabelFrame-Client-1.5.0.msi", [], TestContextToken));
        Assert.Empty(service.List());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "published"), "*.msi"));
    }

    [Theory]
    [InlineData(false, "192.168.1.8", "", true, false)]
    [InlineData(false, "127.0.0.1", "", true, true)]
    [InlineData(true, "192.168.1.8", "https://server.example", true, true)]
    [InlineData(true, "192.168.1.8", "https://evil.example", true, false)]
    [InlineData(true, "192.168.1.8", "", false, false)]
    public void Administration_requires_token_secure_transport_and_same_origin(bool https, string ip, string origin, bool token, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Scheme = https ? "https" : "http";
        context.Request.Host = new HostString("server.example");
        context.Request.Headers.Origin = origin;
        context.Request.Headers.Authorization = "Bearer " + (token ? new string('x', 40) : "wrong");
        Assert.Equal(allowed, ClientUpdatesApi.Authorized(context, new ServerOptions { UpdateAdminToken = new string('x', 40) }));
        Assert.False(ClientUpdatesApi.Authorized(context, new ServerOptions()));
    }

    private static CancellationToken TestContextToken => CancellationToken.None;
    private sealed class Inspector(ClientUpdatePackage package) : IClientUpdatePackageInspector
    {
        public Task<ClientUpdatePackage> InspectAsync(string path, string fileName, CancellationToken cancellationToken = default) => Task.FromResult(package);
    }
}
