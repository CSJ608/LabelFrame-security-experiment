using System.IO.Compression;
using System.Text;
using LabelFrame.Bootstrapper.Placement;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>非 MSI 落位：覆盖解压 + .lfplugin 版本比较（覆盖 / 同版幂等 / 降级拒绝）。</summary>
public sealed class PayloadPlacerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"labelframe-placer-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Place_archive_should_extract_and_replace_target()
    {
        var archive = CreateZip("webui", [("index.html", "v1"), ("assets/app.js", "v1")]);
        var target = Path.Combine(_root, "server", "plugins", "web-ui");

        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlaceArchive(archive, target));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(target, "index.html")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(target, "assets", "app.js")));

        // 覆盖落位：同目录整包替换（陈旧文件清除——目录内容完全由包拥有）
        var archive2 = CreateZip("webui2", [("index.html", "v2"), ("assets/app.css", "v2")]);
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlaceArchive(archive2, target));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(target, "index.html")));
        Assert.False(File.Exists(Path.Combine(target, "assets", "app.js")), "覆盖落位应清除包中不存在的新旧文件");
        Assert.True(File.Exists(Path.Combine(target, "assets", "app.css")));
    }

    [Fact]
    public void Place_plugin_fresh_install_should_place()
    {
        var archive = CreatePluginZip("1.2.0");
        var target = Path.Combine(_root, "Client", "plugins", "labelframe-transport-zebra");

        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(archive, target));
        Assert.True(File.Exists(Path.Combine(target, "manifest.json")));
    }

    [Fact]
    public void Place_plugin_same_version_should_skip_idempotently()
    {
        var target = Path.Combine(_root, "p-same");
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), target));
        var placedAt = File.GetLastWriteTimeUtc(Path.Combine(target, "manifest.json"));

        Assert.Equal(PlacementOutcome.SkippedSameVersion, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), target));
        Assert.True(placedAt == File.GetLastWriteTimeUtc(Path.Combine(target, "manifest.json")), "同版本跳过不得重写文件");
    }

    [Fact]
    public void Place_plugin_newer_version_should_overwrite()
    {
        var target = Path.Combine(_root, "p-newer");
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), target));

        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.3.0"), target));
        Assert.Contains("1.3.0", File.ReadAllText(Path.Combine(target, "manifest.json")));
    }

    [Fact]
    public void Place_plugin_older_version_should_be_refused()
    {
        var target = Path.Combine(_root, "p-older");
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.3.0"), target));

        Assert.Equal(PlacementOutcome.RefusedDowngrade, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), target));
        Assert.Contains("1.3.0", File.ReadAllText(Path.Combine(target, "manifest.json")), StringComparison.Ordinal); // 降级拒绝不得改动已装目录
    }

    [Fact]
    public void Place_plugin_without_manifest_should_throw()
    {
        var archive = CreateZip("bad", [("plugin.dll", "bin")]);
        var ex = Assert.Throws<InvalidOperationException>(() => PayloadPlacer.PlacePlugin(archive, Path.Combine(_root, "p-bad")));
        Assert.Contains("manifest.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Place_missing_archive_should_throw_file_not_found()
    {
        Assert.Throws<FileNotFoundException>(() => PayloadPlacer.PlaceArchive(Path.Combine(_root, "nope.zip"), Path.Combine(_root, "t")));
    }

    [Theory]
    [InlineData("1.0", "1.0.0", 0)]  // Version 语义：缺失段视为 0（1.0 == 1.0.0）
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("2.0.0", "10.0.0", -1)] // 数值比较（非字符串序）
    [InlineData("abc.1", "abc.2", -1)] // 双方不可解析 → Ordinal 字符串比较
    [InlineData("1.0.0", "abc", -1)] // 单侧不可解析 → Ordinal（"1" < "a"）
    public void Compare_plugin_versions_should_follow_decision_123_semantics(string left, string right, int expectedSign)
    {
        var actual = PayloadPlacer.ComparePluginVersions(left, right);
        Assert.Equal(expectedSign, Math.Sign(actual));
    }

    /// <summary>构造落位 zip（根 = 文件列表；可选根 manifest.json）。</summary>
    private string CreateZip(string name, IReadOnlyList<(string Path, string Content)> entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{name}.zip");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }

        return path;
    }

    private string CreatePluginZip(string version) =>
        CreateZip("plugin-" + version, [("manifest.json", $$"""{"pluginId":"labelframe-transport-zebra","name":"Zebra","version":"{{version}}"}"""), ("LabelFrame.TransportPlugin.Zebra.dll", "bin")]);
}
