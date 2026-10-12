using System.IO.Compression;
using System.Text;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.TransportPlugin.Sample;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

public class PluginInstallerTests
{
    private static (PluginInstaller Installer, TransportPluginRegistry Registry, string PluginsDir) Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfinstall-{Guid.NewGuid():N}");
        var registry = TestTransportRegistry.Create();
        return (new PluginInstaller(dir, registry, TextWriter.Null), registry, dir);
    }

    /// <summary>构建插件包 zip：manifest.json + Sample 插件 DLL。</summary>
    private static byte[] BuildPackage(string pluginId = "sample", string name = "示例插件（测试）", string version = "1.0.0", string? manifestJson = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
            using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                w.Write(manifestJson ?? $$"""{"pluginId":"{{pluginId}}","name":"{{name}}","version":"{{version}}","description":"测试包"}""");
                }
            
            var dll = zip.CreateEntry("LabelFrame.TransportPlugin.Sample.dll");
            using var dllStream = dll.Open();
            var bytes = File.ReadAllBytes(typeof(SampleTransportPlugin).Assembly.Location);
            dllStream.Write(bytes);
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task Install_valid_package_should_create_package_dir_and_return_view()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            var view = await installer.InstallAsync(new MemoryStream(BuildPackage()), "sample.lfplugin", CancellationToken.None);

            Assert.Equal("sample", view.PluginId);
            Assert.Equal("示例插件（测试）", view.Name);
            Assert.Equal("1.0.0", view.Version);
            Assert.Equal("package", view.Source);
            Assert.False(view.Loaded); // 未重启：注册表尚未装配

            var dir = Path.Combine(pluginsDir, "sample");
            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(Path.Combine(dir, PluginPackageReader.ManifestFileName)));
            Assert.True(File.Exists(Path.Combine(dir, "LabelFrame.TransportPlugin.Sample.dll")));
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_invalid_zip_should_throw()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            await Assert.ThrowsAsync<PluginPackageException>(() => installer.InstallAsync(new MemoryStream(Encoding.UTF8.GetBytes("not a zip")), "bad.lfplugin", CancellationToken.None));
            Assert.False(Directory.Exists(pluginsDir));
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_non_zip_message_should_be_chinese_and_actionable()
    {
        // 四类高频之一：非 zip → 具体中文可行动消息（不直出英文框架原话）
        var (installer, _, pluginsDir) = Create();
        try
        {
            var ex = await Assert.ThrowsAsync<PluginPackageException>(() =>
                installer.InstallAsync(new MemoryStream(Encoding.UTF8.GetBytes("plain text file, not a zip")), "bad.txt", CancellationToken.None));
            Assert.Contains("不是 zip 格式", ex.Message);
            Assert.DoesNotContain("End of Central Directory", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_corrupted_zip_message_should_be_chinese_and_actionable()
    {
        // 四类高频之二：有 zip 魔数但结构损坏（截断的 zip）→ 具体中文消息
        var (installer, _, pluginsDir) = Create();
        try
        {
            var valid = BuildPackage();
            // 截断后半段：保留局部文件头魔数、丢失中央目录 → zip 损坏
            var corrupted = valid.Take(valid.Length / 2).ToArray();

            var ex = await Assert.ThrowsAsync<PluginPackageException>(() =>
                installer.InstallAsync(new MemoryStream(corrupted), "broken.lfplugin", CancellationToken.None));
            Assert.Contains("已损坏", ex.Message);
            Assert.DoesNotContain("End of Central Directory", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_invalid_manifest_json_message_should_be_chinese_with_position()
    {
        // 四类高频之三：manifest 缺失或非法 → 具体中文消息（含定位提示，不透出英文解析详情）
        var (installer, _, pluginsDir) = Create();
        try
        {
            var ex = await Assert.ThrowsAsync<PluginPackageException>(() =>
                installer.InstallAsync(new MemoryStream(BuildPackage(manifestJson: """{"pluginId": "x", "name": }""")), "bad-manifest.lfplugin", CancellationToken.None));
            Assert.Contains("manifest.json", ex.Message);
            Assert.Contains("不是有效的 JSON", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_invalid_dll_message_should_be_chinese_and_actionable()
    {
        // 四类高频之四：DLL 无效（不是 .NET 程序集）→ 具体中文消息
        var (installer, _, pluginsDir) = Create();
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
                using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                {
                    w.Write("""{"pluginId":"sample","name":"x","version":"1.0.0"}""");
                }
                var dll = zip.CreateEntry("Fake.Plugin.dll");
                using var dllStream = dll.Open();
                dllStream.Write(Encoding.UTF8.GetBytes("this text file pretends to be a managed dll"));
            }

            var ex = await Assert.ThrowsAsync<PluginPackageException>(() =>
                installer.InstallAsync(new MemoryStream(ms.ToArray()), "bad-dll.lfplugin", CancellationToken.None));
            Assert.Contains("Fake.Plugin.dll", ex.Message);
            Assert.Contains("不是有效的 .NET 程序集", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_builtin_id_should_be_rejected()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            var ex = await Assert.ThrowsAsync<PluginPackageException>(() =>
                installer.InstallAsync(new MemoryStream(BuildPackage(pluginId: "log")), "log.lfplugin", CancellationToken.None));
            Assert.Contains("内置插件", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_id_mismatch_should_be_rejected()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            // manifest.pluginId=other，但包内 DLL 实际实现 id=sample
            var ex = await Assert.ThrowsAsync<PluginPackageException>(() =>
                installer.InstallAsync(new MemoryStream(BuildPackage(pluginId: "other")), "other.lfplugin", CancellationToken.None));
            Assert.Contains("不一致", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_no_plugin_implementation_should_be_rejected()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
                using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                {
                    w.Write("""{"pluginId":"sample","name":"x","version":"1.0.0"}""");
                }
                zip.CreateEntry("README.txt").Open().Dispose(); // 无 DLL
            }

            var ex = await Assert.ThrowsAsync<PluginPackageException>(() =>
                installer.InstallAsync(new MemoryStream(ms.ToArray()), "x.lfplugin", CancellationToken.None));
            Assert.Contains("DLL", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_overwrite_same_plugin_id_should_replace_files()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            await installer.InstallAsync(new MemoryStream(BuildPackage(version: "1.0.0", name: "旧版")), "a.lfplugin", CancellationToken.None);
            var view = await installer.InstallAsync(new MemoryStream(BuildPackage(version: "2.0.0", name: "新版")), "b.lfplugin", CancellationToken.None);

            Assert.Equal("2.0.0", view.Version);
            Assert.Equal("新版", view.Name);
            var dir = Path.Combine(pluginsDir, "sample");
            Assert.True(Directory.Exists(dir));
            Assert.Contains(Path.GetFileName(typeof(SampleTransportPlugin).Assembly.Location), Directory.GetFiles(dir).Select(Path.GetFileName));
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Uninstall_should_remove_package_dir()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            await installer.InstallAsync(new MemoryStream(BuildPackage()), "a.lfplugin", CancellationToken.None);
            installer.Uninstall("sample");

            Assert.False(Directory.Exists(Path.Combine(pluginsDir, "sample")));
            Assert.Throws<PluginPackageException>(() => installer.Uninstall("sample")); // 再次卸载报未安装
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public void Uninstall_not_installed_should_throw()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            var ex = Assert.Throws<PluginPackageException>(() => installer.Uninstall("no-such"));
            Assert.Contains("未安装", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Uninstall_manual_dir_without_manifest_should_be_rejected()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            Directory.CreateDirectory(Path.Combine(pluginsDir, "manual"));
            File.WriteAllText(Path.Combine(pluginsDir, "manual", "x.dll"), "not real");

            var ex = Assert.Throws<PluginPackageException>(() => installer.Uninstall("manual"));
            Assert.Contains("手动放置", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
        public async Task List_should_show_package_and_manual_with_loaded_state()
    {
        var (installer, registry, pluginsDir) = Create();
        try
        {
            // 手动平铺 DLL（非插件文件 → 重启后也不会装配）
            Directory.CreateDirectory(pluginsDir);
            File.WriteAllText(Path.Combine(pluginsDir, "manual.dll"), "not a real plugin assembly");

            // 安装包
            await installer.InstallAsync(new MemoryStream(BuildPackage()), "a.lfplugin", CancellationToken.None);

            var before = installer.ListInstalled();
            Assert.Equal(2, before.Count);
            var pkg = Assert.Single(before.Where(v => v.Source == "package"));
            var manual = Assert.Single(before.Where(v => v.Source == "manual"));
            Assert.False(pkg.Loaded); // 未重启
            Assert.False(manual.Loaded);
            Assert.Equal("manual", manual.PluginId);

            // 模拟重启后：从插件目录重新装配注册表 → 安装包 Loaded=true；manual.dll 非插件 → 仍 false
            foreach (var discovered in PluginDirectoryLoader.Load(pluginsDir, TextWriter.Null))
            {
                registry.RegisterExternal(discovered.Plugin, discovered.AssemblyPath, TextWriter.Null, discovered.LoadContext);
            }

            var after = installer.ListInstalled();
            var pkgAfter = Assert.Single(after.Where(v => v.Source == "package"));
            var manualAfter = Assert.Single(after.Where(v => v.Source == "manual"));
            Assert.True(pkgAfter.Loaded);
            Assert.False(manualAfter.Loaded);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public void List_should_report_load_error_for_broken_package_dll()
    {
        // 场景：manifest 正常但 DLL 损坏（启动加载失败），lastLoadErrors 透出原因
        var dir = Path.Combine(Path.GetTempPath(), $"lfinstall-{Guid.NewGuid():N}");
        var registry = TestTransportRegistry.Create();
        var packageDir = Path.Combine(dir, "sample");
        Directory.CreateDirectory(packageDir);
        var brokenDll = Path.Combine(packageDir, "LabelFrame.TransportPlugin.Sample.dll");
        try
        {
            File.WriteAllText(Path.Combine(packageDir, "manifest.json"), """{"pluginId":"sample","name":"示例插件","version":"1.0.0"}""");
            File.WriteAllText(brokenDll, "not a real assembly");

            var installer = new PluginInstaller(dir, registry, TextWriter.Null,
                new Dictionary<string, string> { [brokenDll] = "测试加载失败原因：缺依赖" });

            var pkg = Assert.Single(installer.ListInstalled());
            Assert.Equal("package", pkg.Source);
            Assert.False(pkg.Loaded);
            Assert.Contains("测试加载失败原因：缺依赖", pkg.LoadError);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void List_should_report_load_error_for_broken_manual_dll()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfinstall-{Guid.NewGuid():N}");
        var registry = TestTransportRegistry.Create();
        Directory.CreateDirectory(dir);
        var manualDll = Path.Combine(dir, "broken-manual.dll");
        try
        {
            File.WriteAllText(manualDll, "garbage");

            var installer = new PluginInstaller(dir, registry, TextWriter.Null,
                new Dictionary<string, string> { [manualDll] = "测试加载失败原因：非托管依赖缺失" });

            var manual = Assert.Single(installer.ListInstalled());
            Assert.Equal("manual", manual.Source);
            Assert.False(manual.Loaded);
            Assert.Contains("测试加载失败原因：非托管依赖缺失", manual.LoadError);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    // ---- 官方插件体系：官方 id 放行 + 覆盖安装版本比较 + 真实装配 ----

    [Fact]
    public async Task Install_official_package_should_pass_builtin_check_and_load_after_restart()
    {
        // 官方 id（labelframe-transport-zebra）不再与内置冲突（zebra 已外置）；安装后重启装配可用（装配面）
        var (installer, registry, pluginsDir) = Create();
        try
        {
            var view = await installer.InstallAsync(new MemoryStream(OfficialPackageFactory.Build("0.27.0")), "zebra-official.lfplugin", CancellationToken.None);

            Assert.Equal("labelframe-transport-zebra", view.PluginId);
            Assert.Equal("0.27.0", view.Version);
            var dir = Path.Combine(pluginsDir, "labelframe-transport-zebra");
            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(Path.Combine(dir, PluginPackageReader.ManifestFileName)));

            // 模拟重启：外部插件目录装配 → 官方插件注册（id 与 describe 等价内置时代行为）
            var loaded = PluginDirectoryLoader.Load(pluginsDir, TextWriter.Null);
            var plugin = Assert.Single(loaded, item => item.Plugin.Id == "labelframe-transport-zebra").Plugin;
            Assert.Equal("Zebra", plugin.DisplayName);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_official_newer_version_should_overwrite_older()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            await installer.InstallAsync(new MemoryStream(OfficialPackageFactory.Build("0.26.0")), "a.lfplugin", CancellationToken.None);
            var view = await installer.InstallAsync(new MemoryStream(OfficialPackageFactory.Build("0.27.0")), "b.lfplugin", CancellationToken.None);

            Assert.Equal("0.27.0", view.Version);
            var manifest = PluginPackageManifest.Parse(
                File.ReadAllText(Path.Combine(pluginsDir, "labelframe-transport-zebra", PluginPackageReader.ManifestFileName)));
            Assert.Equal("0.27.0", manifest.Version);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_official_same_version_should_be_idempotent_skip()
    {
        // 同版本幂等：不重复解压（目录不动——marker 文件保留），返回已装视图
        var (installer, _, pluginsDir) = Create();
        try
        {
            await installer.InstallAsync(new MemoryStream(OfficialPackageFactory.Build("0.27.0")), "a.lfplugin", CancellationToken.None);
            var dir = Path.Combine(pluginsDir, "labelframe-transport-zebra");
            var marker = Path.Combine(dir, "marker.txt");
            File.WriteAllText(marker, "keep");

            var view = await installer.InstallAsync(new MemoryStream(OfficialPackageFactory.Build("0.27.0")), "b.lfplugin", CancellationToken.None);

            Assert.Equal("0.27.0", view.Version);
            Assert.True(File.Exists(marker)); // 未重建目录
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_official_older_version_should_be_rejected_as_downgrade()
    {
        var (installer, _, pluginsDir) = Create();
        try
        {
            await installer.InstallAsync(new MemoryStream(OfficialPackageFactory.Build("0.27.0")), "a.lfplugin", CancellationToken.None);

            var ex = await Assert.ThrowsAsync<PluginPackageException>(() =>
                installer.InstallAsync(new MemoryStream(OfficialPackageFactory.Build("0.26.0")), "b.lfplugin", CancellationToken.None));
            Assert.Contains("降级", ex.Message);
            Assert.Contains("0.27.0", ex.Message);

            // 已装版本不受影响
            var manifest = PluginPackageManifest.Parse(
                File.ReadAllText(Path.Combine(pluginsDir, "labelframe-transport-zebra", PluginPackageReader.ManifestFileName)));
            Assert.Equal("0.27.0", manifest.Version);
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_thirdparty_same_version_should_still_overwrite()
    {
        // 第三方插件维持「覆盖安装不做版本比较」：同版本重装仍替换目录（marker 消失）
        var (installer, _, pluginsDir) = Create();
        try
        {
            await installer.InstallAsync(new MemoryStream(BuildPackage(version: "1.0.0")), "a.lfplugin", CancellationToken.None);
            var marker = Path.Combine(pluginsDir, "sample", "marker.txt");
            File.WriteAllText(marker, "stale");

            var view = await installer.InstallAsync(new MemoryStream(BuildPackage(version: "1.0.0")), "b.lfplugin", CancellationToken.None);

            Assert.Equal("1.0.0", view.Version);
            Assert.False(File.Exists(marker)); // 目录被重建
        }
        finally
        {
            TryDeleteDirectory(pluginsDir);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 插件 dll 被 collectible ALC 锁定，进程退出后由系统清理
        }
    }
}
