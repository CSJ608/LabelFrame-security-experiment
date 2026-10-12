using System.IO.Compression;
using System.Text;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.TransportPlugin.Fake;
using LabelFrame.TransportPlugin.Sample;

namespace LabelFrame.Core.Tests.Transport;

/// <summary>
/// fake 插件跨端单测矩阵（/ ，对齐 fake 编译器矩阵模式）：
/// manifest platforms 契约（解析 / 向后兼容语义）＋ 三层校验平台门（windows / android 双向）＋
/// 安装（含坏包拒绝：损坏 zip / manifest 缺失 / 内置 id 冲突 / pluginId 不一致 / 无 DLL）＋
/// 官方插件版本比较（android 宿主路径）＋ 加载 / 发现 / 装配 / 发送（fake 插件全链路）。
/// </summary>
public class PluginInstallerCrossPlatformTests
{
    private const string FakePluginId = "labelframe-transport-fake";

    private static (PluginInstaller Installer, TransportPluginRegistry Registry, string PluginsDir) Create(
        string hostPlatform = PluginPlatforms.Android)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfxplat-{Guid.NewGuid():N}");
        var registry = new TransportPluginRegistry();
        // 内置传输注册（模拟宿主注册表：PDA 内置 zebra；平台门测试用无内置冲突包）
        registry.Register(new ZebraStubPlugin());
        return (new PluginInstaller(dir, registry, TextWriter.Null, hostPlatform: hostPlatform), registry, dir);
    }

    /// <summary>构建插件包 zip：manifest.json（可含 platforms）+ 指定 DLL 集合。</summary>
    private static byte[] BuildPackage(string manifestJson, params (string EntryName, byte[] Bytes)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
            using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                w.Write(manifestJson);
            }

            foreach (var (entryName, bytes) in files)
            {
                var entry = zip.CreateEntry(entryName);
                using var target = entry.Open();
                target.Write(bytes);
            }
        }

        return ms.ToArray();
    }

    private static byte[] FakeDllBytes => File.ReadAllBytes(typeof(FakeTransportPlugin).Assembly.Location);

    private static byte[] SampleDllBytes => File.ReadAllBytes(typeof(SampleTransportPlugin).Assembly.Location);

    private static string FakeManifest(string version = "1.0.0", string pluginId = FakePluginId, string? platformsJson = ",\"platforms\":[\"android\"]")
        => $$"""{"pluginId":"{{pluginId}}","name":"假想品牌（通道验证）","version":"{{version}}"{{platformsJson}}}""";

    private static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // 临时目录清理失败不影响测试结果
        }
    }

    // manifest platforms 契约（可选字段 + 单包单端 + 向后兼容）

    [Fact]
    public void Manifest_without_platforms_should_parse_null_and_mean_windows_only()
    {
        var manifest = PluginPackageManifest.Parse("""{"pluginId":"sample","name":"示例","version":"1.0.0"}""");

        Assert.Null(manifest.Platforms);
        Assert.True(manifest.SupportsPlatform(PluginPlatforms.Windows)); // 无字段既有包按 Windows 端解释
        Assert.False(manifest.SupportsPlatform(PluginPlatforms.Android)); // PDA 拒绝存量 Windows 包
    }

    [Fact]
    public void Manifest_with_android_platforms_should_support_android_only()
    {
        var manifest = PluginPackageManifest.Parse("""{"pluginId":"p","name":"n","version":"1.0.0","platforms":["android"]}""");

        Assert.Equal(new[] { "android" }, manifest.Platforms);
        Assert.True(manifest.SupportsPlatform(PluginPlatforms.Android));
        Assert.False(manifest.SupportsPlatform(PluginPlatforms.Windows));
    }

    [Fact]
    public void Manifest_platforms_should_normalize_case_and_whitespace()
    {
        var manifest = PluginPackageManifest.Parse("""{"pluginId":"p","name":"n","version":"1.0.0","platforms":[" Windows "]}""");

        Assert.Equal(new[] { "windows" }, manifest.Platforms);
        Assert.True(manifest.SupportsPlatform("WINDOWS")); // 判定忽略大小写
    }

    [Fact]
    public void Manifest_empty_platforms_array_should_reject()
    {
        // 空数组语义不明（缺省 = Windows 兼容解释），显式空数组非法
        var ex = Assert.Throws<PluginPackageException>(
            () => PluginPackageManifest.Parse("""{"pluginId":"p","name":"n","version":"1.0.0","platforms":[]}"""));
        Assert.Contains("空数组", ex.Message);
    }

    [Fact]
    public void Manifest_blank_platform_entry_should_reject()
    {
        var ex = Assert.Throws<PluginPackageException>(
            () => PluginPackageManifest.Parse("""{"pluginId":"p","name":"n","version":"1.0.0","platforms":[" "]}"""));
        Assert.Contains("空白平台 id", ex.Message);
    }

    // 平台门（三层校验 b：宿主平台不在声明集合内拒绝）

    [Fact]
    public async Task Install_android_package_on_android_host_should_succeed()
    {
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            var view = await installer.InstallAsync(new MemoryStream(BuildPackage(FakeManifest(), ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes))), "fake.lfplugin", CancellationToken.None);

            Assert.Equal(FakePluginId, view.PluginId);
            Assert.False(view.Loaded); // 未重启：注册表尚未装配
            Assert.True(File.Exists(Path.Combine(pluginsDir, FakePluginId, "LabelFrame.TransportPlugin.Fake.dll")));
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_android_package_on_windows_host_should_reject_with_platform_message()
    {
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Windows);
        try
        {
            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(new MemoryStream(BuildPackage(FakeManifest(), ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes))), "fake.lfplugin", CancellationToken.None));
            Assert.Contains("平台", ex.Message);
            Assert.False(Directory.Exists(Path.Combine(pluginsDir, FakePluginId)));
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_unmarked_legacy_package_on_windows_host_should_succeed()
    {
        // 向后兼容：无 platforms 字段的存量包（如既有 zebra 官方包）在 Windows 宿主照常安装
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Windows);
        try
        {
            var manifest = """{"pluginId":"sample","name":"示例插件（测试）","version":"1.0.0"}""";
            var view = await installer.InstallAsync(new MemoryStream(BuildPackage(manifest, ("LabelFrame.TransportPlugin.Sample.dll", SampleDllBytes))), "sample.lfplugin", CancellationToken.None);
            Assert.Equal("sample", view.PluginId);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_unmarked_legacy_package_on_android_host_should_reject()
    {
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            var manifest = """{"pluginId":"sample","name":"示例插件（测试）","version":"1.0.0"}""";
            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(new MemoryStream(BuildPackage(manifest, ("LabelFrame.TransportPlugin.Sample.dll", SampleDllBytes))), "sample.lfplugin", CancellationToken.None));
            Assert.Contains("平台", ex.Message);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    // ---- 坏包拒绝（三层校验其余维度：损坏 zip / manifest 缺失 / 内置 id 冲突 / id 不一致 / 无 DLL / 非 zip） ----

    [Fact]
    public async Task Install_corrupted_zip_should_reject()
    {
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            var valid = BuildPackage(FakeManifest(), ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes));
            var corrupted = valid.Take(valid.Length / 2).ToArray();
            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(new MemoryStream(corrupted), "broken.lfplugin", CancellationToken.None));
            Assert.Contains("已损坏", ex.Message);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_zip_without_manifest_should_reject()
    {
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry("LabelFrame.TransportPlugin.Fake.dll");
                using var target = entry.Open();
                target.Write(FakeDllBytes);
            }

            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(new MemoryStream(ms.ToArray()), "nomanifest.lfplugin", CancellationToken.None));
            Assert.Contains("manifest.json", ex.Message);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_not_a_zip_should_reject()
    {
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("plain text, not a zip")), "bad.lfplugin", CancellationToken.None));
            Assert.Contains("不是 zip 格式", ex.Message);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_package_without_dll_should_reject()
    {
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(new MemoryStream(BuildPackage(FakeManifest())), "nodll.lfplugin", CancellationToken.None));
            Assert.Contains("未包含任何 DLL", ex.Message);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_non_assembly_dll_should_reject()
    {
        // 三层校验第 层（ALC 预检）：DLL 不是有效 .NET 程序集 → 可行动中文提示
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(
                    new MemoryStream(BuildPackage(FakeManifest(), ("LabelFrame.TransportPlugin.Fake.dll", System.Text.Encoding.UTF8.GetBytes("not an assembly")))),
                    "invalid.lfplugin", CancellationToken.None));
            Assert.Contains("不是有效的 .NET 程序集", ex.Message);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_builtin_id_conflict_should_reject()
    {
        // 内置 id 拒绝按宿主注册表动态判定（PDA 内置 zebra）
        var (installer, registry, pluginsDir) = Create(PluginPlatforms.Android);
        Assert.False(registry.GetPlugin("zebra")!.IsExternal);
        try
        {
            var manifest = FakeManifest(pluginId: "zebra");
            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(new MemoryStream(BuildPackage(manifest, ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes))), "zebra.lfplugin", CancellationToken.None));
            Assert.Contains("内置插件冲突", ex.Message);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Install_manifest_id_mismatch_should_reject()
    {
        // 三层校验第 层：ALC 预检发现的真实插件 id 与 manifest.pluginId 不一致拒绝
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            var manifest = FakeManifest(pluginId: "some-other-id");
            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(new MemoryStream(BuildPackage(manifest, ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes))), "mismatch.lfplugin", CancellationToken.None));
            Assert.Contains("不一致", ex.Message);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    // 官方插件版本比较（语义双端一致——android 宿主路径）

    [Fact]
    public async Task Install_official_same_version_should_be_idempotent_and_downgrade_rejected_on_android()
    {
        var (installer, _, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            await installer.InstallAsync(new MemoryStream(BuildPackage(FakeManifest("1.0.0"), ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes))), "fake.lfplugin", CancellationToken.None);

            // 同版本幂等跳过
            var again = await installer.InstallAsync(new MemoryStream(BuildPackage(FakeManifest("1.0.0"), ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes))), "fake.lfplugin", CancellationToken.None);
            Assert.Equal("1.0.0", again.Version);

            // 降级拒绝（官方前缀插件）
            var ex = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(new MemoryStream(BuildPackage(FakeManifest("0.9.0"), ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes))), "fake.lfplugin", CancellationToken.None));
            Assert.Contains("拒绝降级", ex.Message);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    // ---- 加载 / 发现 / 装配 / 发送（fake 插件全链路——Spike 的单测面代理） ----

    [Fact]
    public async Task Installed_fake_package_should_load_discover_assemble_and_send()
    {
        var (installer, registry, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            await installer.InstallAsync(new MemoryStream(BuildPackage(FakeManifest(), ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes))), "fake.lfplugin", CancellationToken.None);

            // 模拟重启装配：目录扫描（单 ALC 整体加载）→ 注册表（外部 + ALC 根强持有）
            var load = PluginDirectoryLoader.LoadWithErrors(pluginsDir, TextWriter.Null);
            Assert.Empty(load.Errors);
            var discovered = Assert.Single(load.Plugins, p => p.Plugin.Id == FakePluginId);
            Assert.True(registry.RegisterExternal(discovered.Plugin, discovered.AssemblyPath, TextWriter.Null, discovered.LoadContext));
            Assert.True(registry.GetPlugin(FakePluginId)!.IsExternal);

            // 已装列表透出加载状态
            var view = Assert.Single(installer.ListInstalled(), v => v.PluginId == FakePluginId);
            Assert.True(view.Loaded);

            // 装配传输并发送：fake 传输落盘留痕（发送内容进插件数据目录 sink 文件）
            var dataDir = Path.Combine(Path.GetTempPath(), $"lfxplat-data-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataDir);
            try
            {
                var context = new TransportPluginContext(TextWriter.Null, dataDir);
                var transport = registry.CreateTransport(
                    FakePluginId,
                    new TransportPluginParameters(new Dictionary<string, string> { ["host"] = "10.1.2.3", ["port"] = "9100" }),
                    context);
                await transport.SendAsync("^XA^FO10,10^FS^XZ", CancellationToken.None);

                var sink = Path.Combine(dataDir, FakeTransportPlugin.SinkFileName);
                Assert.True(File.Exists(sink));
                Assert.Contains("^XA^FO10,10^FS^XZ", File.ReadAllText(sink));

                // 状态 / 连接测试能力面（IPrinterStatusProvider / ITestableTransport）
                var status = await ((Core.Transport.IPrinterStatusProvider)transport).GetStatusAsync();
                Assert.True(status.IsOnline);
            }
            finally
            {
                Cleanup(dataDir);
            }
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    [Fact]
    public async Task Installed_broken_dll_package_should_report_load_error_without_blocking()
    {
        // 装后损坏场景：安装时 DLL 有效（三层校验过）→ 磁盘上被破坏 → 重启装配失败留痕（loadError 透出），不阻断宿主
        var (installer, registry, pluginsDir) = Create(PluginPlatforms.Android);
        try
        {
            await installer.InstallAsync(
                new MemoryStream(BuildPackage(FakeManifest(), ("LabelFrame.TransportPlugin.Fake.dll", FakeDllBytes))),
                "fake.lfplugin", CancellationToken.None);

            // 破坏包内主 DLL（模拟磁盘损坏 / 半途覆盖）
            File.WriteAllText(Path.Combine(pluginsDir, FakePluginId, "LabelFrame.TransportPlugin.Fake.dll"), "not an assembly");

            var load = PluginDirectoryLoader.LoadWithErrors(pluginsDir, TextWriter.Null);
            Assert.Empty(load.Plugins); // 单插件失败不产出
            Assert.NotEmpty(load.Errors); // 留痕

            // 已装列表以启动装配失败映射透出原因（宿主构造 installer 时传入 loadErrors——与 WinHost / PDA 装配同构）
            var withErrors = new PluginInstaller(
                pluginsDir, registry, TextWriter.Null,
                load.Errors.ToDictionary(e => e.AssemblyPath, e => e.Error, StringComparer.OrdinalIgnoreCase),
                hostPlatform: PluginPlatforms.Android);
            var view = Assert.Single(withErrors.ListInstalled(), v => v.PluginId == FakePluginId);
            Assert.False(view.Loaded);
            Assert.NotNull(view.LoadError);
        }
        finally
        {
            Cleanup(pluginsDir);
        }
    }

    /// <summary>内置 zebra 插件替身（注册表内置 id 冲突判定的锚点；不建连——PDA 侧真实实现于 AndroidHost）。</summary>
    private sealed class ZebraStubPlugin : ITransportPlugin
    {
        public string Id => "zebra";
        public string DisplayName => "Zebra（内置替身）";
        public string Description => "测试用内置替身。";
        public IReadOnlyList<TransportParameterSpec> Parameters => [];
        public string Describe(TransportPluginParameters parameters) => "zebra";
        public Core.Transport.IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
            => throw new NotSupportedException("测试替身不创建传输。");
    }
}
