using System.IO.Compression;
using System.Text;
using LabelFrame.Core.Transport.Plugins.Package;

namespace LabelFrame.Server.Tests;

public class PluginPackagesServiceTests
{
    private static (PluginPackagesService Service, string Dir) Create()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfplugins-pkg-{Guid.NewGuid():N}");
        return (new PluginPackagesService(dir), dir);
    }

    private static byte[] BuildPackage(string pluginId = "sample", string name = "示例插件", string version = "1.0.0", string? manifestJson = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
            using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                w.Write(manifestJson ?? $$"""{"pluginId":"{{pluginId}}","name":"{{name}}","version":"{{version}}","description":"测试包"}""");
            }
            zip.CreateEntry("LabelFrame.TransportPlugin.Sample.dll").Open().Dispose();
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task Save_then_list_should_return_metadata_with_valid_true()
    {
        var (svc, dir) = Create();
        try
        {
            var saved = await svc.SaveAsync("sample-1.0.0.lfplugin", new MemoryStream(BuildPackage()));
            Assert.True(saved.Valid);
            Assert.Equal("sample", saved.PluginId);
            Assert.Equal("示例插件", saved.Name);
            Assert.Equal("1.0.0", saved.Version);

            var list = svc.List();
            var view = Assert.Single(list);
            Assert.Equal("sample-1.0.0.lfplugin", view.FileName);
            Assert.True(view.Valid);
            Assert.StartsWith("/api/plugin-packages/", view.Url);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_invalid_zip_should_throw_and_not_persist()
    {
        var (svc, dir) = Create();
        try
        {
            await Assert.ThrowsAsync<PluginPackageException>(() => svc.SaveAsync("bad.lfplugin", new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_missing_manifest_or_field_should_throw()
    {
        var (svc, dir) = Create();
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                zip.CreateEntry("other.json").Open().Dispose();
            }

            await Assert.ThrowsAsync<PluginPackageException>(() => svc.SaveAsync("no-manifest.lfplugin", new MemoryStream(ms.ToArray())));
            await Assert.ThrowsAsync<PluginPackageException>(() => svc.SaveAsync("no-field.lfplugin", new MemoryStream(BuildPackage(manifestJson: """{"pluginId":"x","name":"y"}"""))));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Traversal_file_name_should_be_rejected()
    {
        var (svc, dir) = Create();
        try
        {
            await Assert.ThrowsAsync<PluginPackageException>(() => svc.SaveAsync("..\\..\\evil.lfplugin", new MemoryStream(BuildPackage())));
            Assert.Null(svc.GetDownloadPath("..\\..\\evil.lfplugin"));
            Assert.Null(svc.GetDownloadPath("../evil.lfplugin"));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_existing_should_return_true_and_remove()
    {
        var (svc, dir) = Create();
        try
        {
            await svc.SaveAsync("a.lfplugin", new MemoryStream(BuildPackage()));
            Assert.True(svc.Delete("a.lfplugin"));
            Assert.Null(svc.Get("a.lfplugin"));
            Assert.False(svc.Delete("a.lfplugin"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Get_missing_should_return_null()
    {
        var (svc, dir) = Create();
        try
        {
            Assert.Null(svc.Get("no-such.lfplugin"));
            Assert.Null(svc.GetDownloadPath("no-such.lfplugin"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task List_should_mark_preplaced_invalid_file_valid_false()
    {
        var (svc, dir) = Create();
        try
        {
            // 目录直放非法文件（不经 API 上传）：解析失败 → valid:false + 原因，仍列出可删除
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "broken.lfplugin"), "garbage");

            var list = svc.List();
            var view = Assert.Single(list);
            Assert.False(view.Valid);
            Assert.False(string.IsNullOrWhiteSpace(view.InvalidReason));
            Assert.Null(view.PluginId);

            Assert.True(svc.Delete("broken.lfplugin"));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_over_limit_should_be_rejected()
    {
        var (svc, dir) = Create();
        try
        {
            // 65MB 空字节（超 64MB 上限；不落盘）
            var big = new byte[PluginPackageLimits.MaxBytes + 1];
            await Assert.ThrowsAsync<PluginPackageException>(() => svc.SaveAsync("big.lfplugin", new MemoryStream(big)));
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- 官方插件放行策略（⑤）----

    [Fact]
    public async Task Save_official_plugin_id_should_be_allowed()
    {
        var (svc, dir) = Create();
        try
        {
            // 官方插件 id（labelframe- 前缀）放行：官方插件经服务端集中分发
            var saved = await svc.SaveAsync(
                "labelframe-transport-zebra-0.27.0.lfplugin",
                new MemoryStream(BuildPackage(pluginId: "labelframe-transport-zebra", name: "Zebra 品牌传输（官方）", version: "0.27.0")));

            Assert.True(saved.Valid);
            Assert.Equal("labelframe-transport-zebra", saved.PluginId);
            Assert.Single(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("log")]
    [InlineData("tcp9100")]
    [InlineData("winspool")]
    public async Task Save_reserved_builtin_id_should_be_rejected(string pluginId)
    {
        var (svc, dir) = Create();
        try
        {
            // 内置传输保留 id 拒绝上传：客户端安装会与内置插件冲突（与客户端安装侧校验互为纵深）
            var ex = await Assert.ThrowsAsync<PluginPackageException>(() =>
                svc.SaveAsync($"{pluginId}.lfplugin", new MemoryStream(BuildPackage(pluginId: pluginId))));
            Assert.Contains("内置", ex.Message);
            Assert.Empty(svc.List());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}