using LabelFrame.Core.IO;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.WinHost.Transport;

/// <summary>
/// 存量升级兼容：启动时检测「已用 zebra 配置」（connection.json 引用官方 id / 旧别名 / 旧 Mode=Zebra，
/// 或 appsettings / 环境变量 Transport=Zebra）且外置插件未安装时，自动安装随客户端 MSI 附带的
/// 官方插件包（plugin-packages\*.lfplugin），本次启动即完成装配。
/// 无附带包（开发目录裸跑 / 自定义部署）时 host.log 中文提示引导安装，不阻断启动。
/// </summary>
public static class ZebraPluginMigration
{
    /// <summary>MSI 附带官方插件包的目录名（相对客户端安装根目录）。</summary>
    public const string BundledPackagesDirName = "plugin-packages";

    /// <summary>
    /// 执行升级迁移（幂等：插件已装 / 无 zebra 配置 / 无附带包均安全跳过）。
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

        try
        {
            if (!ConfigReferencesZebra(options))
            {
                return;
            }

            // 幂等：插件已装（含 manifest 的包目录）即跳过
            var safeId = SafeFileName.Normalize(TransportPluginIdPolicy.ZebraPluginId);
            var installedDir = safeId is null
                ? null
                : Path.Combine(options.PluginsPath, safeId);
            if (installedDir is not null
                && Directory.Exists(installedDir)
                && File.Exists(Path.Combine(installedDir, Core.Transport.Plugins.Package.PluginPackageReader.ManifestFileName)))
            {
                return;
            }

            var packagePath = FindBundledPackage(appBaseDirectory ?? AppContext.BaseDirectory);
            if (packagePath is null)
            {
                hostInfo(
                    $"检测到已使用 Zebra 打印配置，但本机未安装 Zebra 官方插件（{TransportPluginIdPolicy.ZebraPluginId}），"
                    + "且安装目录未找到附带的插件包（plugin-packages\\）——请从服务端「插件分发」页或 GitHub Release 安装 labelframe-transport-zebra 插件包后重启客户端。");
                return;
            }

            // 复用 PluginInstaller 三层校验（zip + manifest / 内置 id 拒绝 / 临时 ALC 预检核对插件 id）
            var installer = new Core.Transport.Plugins.PluginInstaller(options.PluginsPath, new TransportPluginRegistry(), hostLogWriter);
            using var stream = File.OpenRead(packagePath);
            var view = installer.InstallAsync(stream, Path.GetFileName(packagePath), CancellationToken.None).GetAwaiter().GetResult();
            hostInfo(
                $"存量升级迁移：检测到已使用 Zebra 打印配置，已自动安装官方插件 {view.PluginId}（{view.Version}，来源：随客户端附带的 {Path.GetFileName(packagePath)}）。");
        }
        catch (Exception ex)
        {
            // 迁移失败不阻断启动：留痕 + 引导提示（宿主按「插件缺失」既有语义回退默认连接）
            hostInfo(
                $"存量升级迁移失败（Zebra 官方插件 {TransportPluginIdPolicy.ZebraPluginId} 自动安装）：{ex.Message}"
                + "——请从服务端「插件分发」页或 GitHub Release 手动安装后重启客户端。");
        }
    }

    /// <summary>是否「已用 zebra 配置」：connection.json 优先（引用官方 id 即命中——旧别名在读取时已归一），否则看 appsettings / 环境变量 Transport。</summary>
    internal static bool ConfigReferencesZebra(HostOptions options)
    {
        try
        {
            if (File.Exists(options.ConnectionPath))
            {
                var config = TransportConfig.FromJson(File.ReadAllText(options.ConnectionPath));
                if (config is not null
                    && (string.Equals(config.PluginId, TransportPluginIdPolicy.ZebraPluginId, StringComparison.OrdinalIgnoreCase)
                        || config.Mode == TransportMode.Zebra))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // connection.json 读取失败（IO 等）交由 TransportManager 既有回退处理，此处按无持久化配置继续判断
        }

        return options.Transport == TransportMode.Zebra;
    }

    /// <summary>查找随客户端附带的官方插件包（plugin-packages\labelframe-transport-zebra-*.lfplugin；多版本取文件名序最大者）。</summary>
    public static string? FindBundledPackage(string appBaseDirectory)
    {
        var dir = Path.Combine(appBaseDirectory, BundledPackagesDirName);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        return Directory.GetFiles(dir, $"{TransportPluginIdPolicy.ZebraPluginId}-*.lfplugin")
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
