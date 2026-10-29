using System.Security.Cryptography;
using LabelFrame.Updates;

namespace LabelFrame.Server.Tests;

/// <summary>验证发布预览的内容绑定、临时快照和兼容性拒绝。</summary>
public sealed class UpdatePublishingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LabelFrame-publishing-test-" + Guid.NewGuid().ToString("N"));
    public UpdatePublishingTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Preview_is_not_a_release_and_replaced_bytes_cannot_be_published(bool android)
    {
        var source = Path.Combine(_root, android ? "draft.apk" : "draft.msi");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var inspector = new Inspector();
        using var windows = new ClientUpdatesService(Path.Combine(_root, "windows"), inspector);
        using var pda = new AndroidUpdatesService(Path.Combine(_root, "android"), inspector);
        var hash = android ? (await pda.PreviewAsync(source, CancellationToken.None)).Sha256
            : (await windows.PreviewAsync(source, "draft.msi", CancellationToken.None)).Sha256;
        Assert.Empty(windows.List());
        Assert.Empty(pda.List());
        Assert.False(File.Exists(inspector.LastPath));
        await File.WriteAllBytesAsync(source, [4, 5, 6]);
        if (android)
            await Assert.ThrowsAsync<InvalidDataException>(() => pda.PublishAsync(source, [], true, "confirm", CancellationToken.None, hash));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => windows.PublishAsync(source, "draft.msi", [], CancellationToken.None, hash));
        Assert.Empty(windows.List());
        Assert.Empty(pda.List());
        Assert.Empty(Directory.GetFiles(_root, "*.apk", SearchOption.AllDirectories).Where(path => path != source));
        Assert.Empty(Directory.GetFiles(_root, "*.msi", SearchOption.AllDirectories).Where(path => path != source));
    }

    [Fact]
    public async Task Rejected_preview_cleans_snapshot_and_keeps_draft()
    {
        var source = Path.Combine(_root, "draft.apk");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        string? snapshot = null;
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateDraftSnapshot.InspectAsync<int>(source, ".apk", path =>
        {
            snapshot = path;
            throw new InvalidDataException("签名不符");
        }, CancellationToken.None));
        Assert.False(File.Exists(snapshot));
        Assert.True(File.Exists(source));
    }

    private sealed class Inspector : IClientUpdatePackageInspector, IAndroidUpdatePackageInspector
    {
        public string? LastPath { get; private set; }
        private string Hash(string path)
        {
            LastPath = path;
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
        public Task<ClientUpdatePackage> InspectAsync(string path, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ClientUpdatePackage("1.6.0", Guid.NewGuid().ToString(), "x64", "publisher", Hash(path), 3, "1.0.0", "2.0.0", 10));
        public Task<AndroidUpdatePackage> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(AndroidUpdatesTests.Package with { Sha256 = Hash(path) });
    }
}
