using LabelFrame.Core.IO;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.WinHost.Transport;

/// <summary>
/// 存量升级兼容：启动时检测「已用官方插件配置」（connection.json 引用官方插件 id / 旧别名 / 旧内置模式）
/// 且外置插件未安装时，自动安装随客户端 MSI 附带的官方插件包（plugin-packages\*.lfplugin），本次启动即完成装配。
/// 官方 id 清单：zebra（含旧 id 别名与旧 Mode=Zebra，迭代 63）＋ tspl（迭代 143，决策 #195）。
/// 无附带包（开发目录裸跑 / 自定义部署）时 host.log 中文提示引导安装，不阻断启动。
/// </summary>
public static class OfficialPluginMigration
{
    /// <summary>MSI 附带官方插件包的目录名（相对客户端安装根目录）。</summary>
    public const string BundledPackagesDirName = "plugin-packages";

    /// <summary>官方插件 id 清单（懒激活检测范围；后续官方插件同构追加）。</summary>
    internal static readonly IReadOnlyList<(string PluginId, Func<HostOptions, bool> ConfigReferences)> OfficialPlugins =
    [
        (TransportPluginIdPolicy.ZebraPluginId, ConfigReferencesZebra),
        (TransportPluginIdPolicy.TsplPluginId, static options => ConfigReferencesPluginId(options, TransportPluginIdPolicy.TsplPluginId)),
    ];

    /// <summary>
    /// 执行升级迁移（幂等：插件已装 / 无官方插件配置 / 无附带包均安全跳过）。
    /// 任何异常只记日志不外抛——迁移失败不阻断宿主启动。
    /// </summary>
    /// <param name="options">宿主配置（ConnectionPath / PluginsPath / Transport）。</param>
    /// <param name="hostLogWriter">宿主日志写入器。</param>
    /// <param name="hostInfo">宿主关键事件回调（host.log + 启动信息）。</param>
    /// <param name="appBaseDirectory">附带包查找根目录（默认 AppContext.BaseDirectory；测试注入）。</param>
    public static void Run(HostOptions options, TextWriter hostLogWriter, Action<string> hostInfo, string? appBaseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hostLogWriter);
        ArgumentNullException.ThrowIfNull(hostInfo);

        foreach (var (pluginId, configReferences) in OfficialPlugins)
        {
            try
            {
                MigratePlugin(options, pluginId, configReferences, hostLogWriter, hostInfo, appBaseDirectory ?? AppContext.BaseDirectory);
            }
            catch (Exception ex)
            {
                // 迁移失败不阻断启动：留痕 + 引导提示（宿主按「插件缺失」既有语义回退默认连接）
                hostInfo(
                    $"存量升级迁移失败（官方插件 {pluginId} 自动安装）：{ex.Message}"
                    + "——请从服务端「插件分发」页或 GitHub Release 手动安装后重启客户端。");
            }
        }
    }

    private static void MigratePlugin(
        HostOptions options,
        string pluginId,
        Func<HostOptions, bool> configReferences,
        TextWriter hostLogWriter,
        Action<string> hostInfo,
        string appBaseDirectory)
    {
        if (!configReferences(options))
        {
            return;
        }

        // 幂等：插件已装（含 manifest 的包目录）即跳过
        var safeId = SafeFileName.Normalize(pluginId);
        var installedDir = safeId is null
            ? null
            : Path.Combine(options.PluginsPath, safeId);
        if (installedDir is not null
            && Directory.Exists(installedDir)
            && File.Exists(Path.Combine(installedDir, Core.Transport.Plugins.Package.PluginPackageReader.ManifestFileName)))
        {
            return;
        }

        var packagePath = FindBundledPackage(appBaseDirectory, pluginId);
        if (packagePath is null)
        {
            hostInfo(
                $"检测到已使用官方插件 {pluginId} 的打印配置，但本机未安装该插件，"
                + "且安装目录未找到附带的插件包（plugin-packages\\）——请从服务端「插件分发」页或 GitHub Release 安装对应插件包后重启客户端。");
            return;
        }

        // 复用 PluginInstaller 三层校验（zip + manifest / 内置 id 拒绝 / 临时 ALC 预检核对插件 id）
        var installer = new Core.Transport.Plugins.PluginInstaller(options.PluginsPath, new TransportPluginRegistry(), hostLogWriter);
        using var stream = File.OpenRead(packagePath);
        var view = installer.InstallAsync(stream, Path.GetFileName(packagePath), CancellationToken.None).GetAwaiter().GetResult();
        hostInfo(
            $"存量升级迁移：检测到已使用官方插件的打印配置，已自动安装 {view.PluginId}（{view.Version}，来源：随客户端附带的 {Path.GetFileName(packagePath)}）。");
    }

    /// <summary>是否「已用 zebra 配置」：connection.json 优先（引用官方 id 即命中——旧别名在读取时已归一），否则看 appsettings / 环境变量 Transport。</summary>
    internal static bool ConfigReferencesZebra(HostOptions options)
        => ConfigReferencesPluginId(options, TransportPluginIdPolicy.ZebraPluginId) || options.Transport == TransportMode.Zebra;

    /// <summary>connection.json 是否引用指定插件 id（读取失败交由 TransportManager 既有回退处理，按无持久化配置计）。</summary>
    internal static bool ConfigReferencesPluginId(HostOptions options, string pluginId)
    {
        try
        {
            if (File.Exists(options.ConnectionPath))
            {
                var config = TransportConfig.FromJson(File.ReadAllText(options.ConnectionPath));
                if (config is not null && string.Equals(config.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // connection.json 读取失败（IO 等）交由 TransportManager 既有回退处理，此处按无持久化配置继续判断
        }

        return false;
    }

    /// <summary>查找随客户端附带的官方插件包（plugin-packages\&lt;pluginId&gt;-*.lfplugin；多版本取文件名序最大者）。</summary>
    public static string? FindBundledPackage(string appBaseDirectory, string pluginId)
    {
        var dir = Path.Combine(appBaseDirectory, BundledPackagesDirName);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        return Directory.GetFiles(dir, $"{pluginId}-*.lfplugin")
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
