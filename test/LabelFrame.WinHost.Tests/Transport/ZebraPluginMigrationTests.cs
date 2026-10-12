using System.Text;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// 存量升级兼容：已用 zebra 配置 + 外置插件未装 → 自动安装随客户端附带的官方插件包
/// 无配置 / 已装 / 无附带包 / 附带包损坏等边界行为。
/// </summary>
public class ZebraPluginMigrationTests
{
    private static (HostOptions Options, string Root, List<string> Messages) Create(
        string? connectionJson = null,
        TransportMode transport = TransportMode.Log)
    {
        var root = Path.Combine(Path.GetTempPath(), $"lfzebramigrate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var options = new HostOptions
        {
            Transport = transport,
            ConnectionPath = Path.Combine(root, "connection.json"),
            PluginsPath = Path.Combine(root, "plugins"),
        };
        if (connectionJson is not null)
        {
            File.WriteAllText(options.ConnectionPath, connectionJson);
        }

        var messages = new List<string>();
        return (options, root, messages);
    }

    /// <summary>放置随客户端附带的官方插件包（真实插件程序集 + SDK 伴生依赖，模拟 MSI plugin-packages\ 内容）。</summary>
    private static string PlaceBundledPackage(string root, string fileName = "labelframe-transport-zebra-0.27.0.lfplugin")
    {
        var dir = Path.Combine(root, ZebraPluginMigration.BundledPackagesDirName);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, OfficialPackageFactory.Build("0.27.0"));
        return path;
    }


    [Fact]
    public void Run_with_legacy_zebra_alias_config_and_bundled_package_should_auto_install()
    {
        // 存量升级主线：旧内置时代 pluginId "zebra"（≤0.26 connection.json）→ 自动装外置包，本次启动即装配
        var (options, root, messages) = Create(connectionJson: """{"PluginId":"zebra","Params":{"kind":"Tcp","host":"10.0.0.9","port":"9100"}}""");
        try
        {
            PlaceBundledPackage(root);

            ZebraPluginMigration.Run(options, TextWriter.Null, messages.Add, root);

            var installedManifest = Path.Combine(options.PluginsPath, "labelframe-transport-zebra", PluginPackageReader.ManifestFileName);
            Assert.True(File.Exists(installedManifest));
            var view = Assert.Single(messages, m => m.Contains("存量升级迁移"));
            Assert.Contains("0.27.0", view);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_with_official_id_config_should_auto_install()
    {
        var (options, root, messages) = Create(connectionJson: """{"PluginId":"labelframe-transport-zebra","Params":{"kind":"Driver","printerName":"ZDesigner ZD421"}}""");
        try
        {
            PlaceBundledPackage(root);

            ZebraPluginMigration.Run(options, TextWriter.Null, messages.Add, root);

            Assert.True(File.Exists(Path.Combine(options.PluginsPath, "labelframe-transport-zebra", PluginPackageReader.ManifestFileName)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_with_legacy_mode_zebra_config_should_auto_install()
    {
        // 更旧的连接配置形态（只有 Mode=Zebra 平铺字段）
        var (options, root, _) = Create(connectionJson: """{"Mode":"Zebra","ZebraKind":"Tcp","TcpHost":"10.0.0.9","TcpPort":9100}""");
        try
        {
            PlaceBundledPackage(root);

            ZebraPluginMigration.Run(options, TextWriter.Null, _ => { }, root);

            Assert.True(File.Exists(Path.Combine(options.PluginsPath, "labelframe-transport-zebra", PluginPackageReader.ManifestFileName)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_with_transport_zebra_environment_should_auto_install()
    {
        // 无 connection.json，appsettings / 环境变量 Transport=Zebra
        var (options, root, _) = Create(transport: TransportMode.Zebra);
        try
        {
            PlaceBundledPackage(root);

            ZebraPluginMigration.Run(options, TextWriter.Null, _ => { }, root);

            Assert.True(File.Exists(Path.Combine(options.PluginsPath, "labelframe-transport-zebra", PluginPackageReader.ManifestFileName)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_without_zebra_config_should_not_install()
    {
        var (options, root, messages) = Create(connectionJson: """{"PluginId":"tcp9100","Params":{"host":"10.0.0.9","port":"9100"}}""");
        try
        {
            PlaceBundledPackage(root);

            ZebraPluginMigration.Run(options, TextWriter.Null, messages.Add, root);

            Assert.False(Directory.Exists(options.PluginsPath));
            Assert.Empty(messages);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_with_plugin_already_installed_should_skip()
    {
        // 幂等：插件已装（含 manifest 的包目录）即跳过——附带包不再消费
        var (options, root, messages) = Create(connectionJson: """{"PluginId":"zebra","Params":{"kind":"Tcp","host":"10.0.0.9"}}""");
        try
        {
            PlaceBundledPackage(root);
            var installedDir = Path.Combine(options.PluginsPath, "labelframe-transport-zebra");
            Directory.CreateDirectory(installedDir);
            File.WriteAllText(Path.Combine(installedDir, PluginPackageReader.ManifestFileName), """{"pluginId":"labelframe-transport-zebra","name":"Zebra 品牌传输（官方）","version":"0.26.0"}""");

            ZebraPluginMigration.Run(options, TextWriter.Null, messages.Add, root);

            var manifest = PluginPackageManifest.Parse(File.ReadAllText(Path.Combine(installedDir, PluginPackageReader.ManifestFileName)));
            Assert.Equal("0.26.0", manifest.Version); // 未被附带包（0.27.0）覆盖——升级安装走插件管理 / 引导
            Assert.DoesNotContain(messages, m => m.Contains("存量升级迁移"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_without_bundled_package_should_log_guidance_and_not_install()
    {
        var (options, root, messages) = Create(connectionJson: """{"PluginId":"zebra","Params":{"kind":"Tcp","host":"10.0.0.9"}}""");
        try
        {
            ZebraPluginMigration.Run(options, TextWriter.Null, messages.Add, root);

            Assert.False(Directory.Exists(options.PluginsPath));
            var guidance = Assert.Single(messages);
            Assert.Contains("labelframe-transport-zebra", guidance);
            Assert.Contains("插件", guidance);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_with_corrupt_bundled_package_should_not_throw()
    {
        // 附带包损坏（安装校验失败）→ 留痕 + 引导提示，不阻断启动
        var (options, root, messages) = Create(connectionJson: """{"PluginId":"zebra","Params":{"kind":"Tcp","host":"10.0.0.9"}}""");
        try
        {
            var dir = Path.Combine(root, ZebraPluginMigration.BundledPackagesDirName);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "labelframe-transport-zebra-0.27.0.lfplugin"), Encoding.UTF8.GetBytes("not a zip"));

            ZebraPluginMigration.Run(options, TextWriter.Null, messages.Add, root);

            var failure = Assert.Single(messages);
            Assert.Contains("存量升级迁移失败", failure);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void FindBundledPackage_should_pick_latest_version_name()
    {
        var (options, root, _) = Create();
        try
        {
            var dir = Path.Combine(root, ZebraPluginMigration.BundledPackagesDirName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "labelframe-transport-zebra-0.26.0.lfplugin"), "old");
            File.WriteAllText(Path.Combine(dir, "labelframe-transport-zebra-0.27.0.lfplugin"), "new");
            File.WriteAllText(Path.Combine(dir, "other-plugin-1.0.0.lfplugin"), "other");

            var found = ZebraPluginMigration.FindBundledPackage(root);

            Assert.EndsWith("labelframe-transport-zebra-0.27.0.lfplugin", found);
        }
        finally
        {
            TryDeleteDirectory(root);
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
        catch
        {
            // 临时目录清理失败不影响测试结果
        }
    }
}
