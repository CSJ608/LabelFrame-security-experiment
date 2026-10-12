using Android.Content;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.AndroidHost.Transport.Plugins;

/// <summary>插件通道装配结果（宿主服务启动时一次性装配，随进程存活）。</summary>
public sealed class PluginHostState
{
    /// <summary>传输插件注册表（内置 zebra + 外部插件；外部 ALC 根随注册强持有）。</summary>
    public required TransportPluginRegistry Registry { get; init; }

    /// <summary>插件安装 / 卸载服务（本地 HTTP 插件端点用；平台门 = android）。</summary>
    public required PluginInstaller Installer { get; init; }

    /// <summary>插件上下文（宿主日志 + 插件数据目录）。</summary>
    public required ITransportPluginContext PluginContext { get; init; }

    /// <summary>插件根目录（{FilesDir}/plugins）。</summary>
    public required string PluginsPath { get; init; }

    /// <summary>已装外部插件 id 列表（品牌选项卡扩展用；按装配结果排序）。</summary>
    public required IReadOnlyList<string> ExternalPluginIds { get; init; }
}

/// <summary>PDA 外置插件通道装配：注册内置 Zebra 插件（id = zebra）后扫描 {FilesDir}/plugins/
/// 加载外部插件（轻量档纯托管：包目录单 collectible ALC 整体字节加载，注册表强持有 ALC 根）。</summary>
public static class PluginHost
{
    /// <summary>插件根目录名（应用私有 {FilesDir}/plugins，一插件一子目录）。</summary>
    public const string PluginsDirName = "plugins";

    /// <summary>插件数据目录名（插件状态文件根，{FilesDir}/plugins-data）。</summary>
    public const string PluginDataDirName = "plugins-data";

    /// <summary>装配插件通道（内置 zebra 注册 → 外部目录扫描，冲突跳过 / 失败留痕）。
    /// 任何异常不外抛——插件通道失败不阻断打印服务启动，回退纯内置 zebra 能力。</summary>
    public static PluginHostState Load(Context context)
    {
        var filesDir = context.FilesDir!.AbsolutePath;
        var pluginsPath = Path.Combine(filesDir, PluginsDirName);
        var pluginDataDir = Path.Combine(filesDir, PluginDataDirName);
        Directory.CreateDirectory(pluginDataDir);

        var hostLog = new HostLogWriter();
        var registry = new TransportPluginRegistry();
        var loadErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var externalIds = new List<string>();

        // 内置 zebra 注册：brand = zebra 的路由锚点 + 外部包占用内置 id 的拒绝锚点
        registry.Register(new ZebraBuiltinTransportPlugin(context));

        try
        {
            var load = PluginDirectoryLoader.LoadWithErrors(pluginsPath, hostLog);
            loadErrors = load.Errors.ToDictionary(e => e.AssemblyPath, e => e.Error, StringComparer.OrdinalIgnoreCase);
            foreach (var error in load.Errors)
            {
                HostLog.Warn(HostLog.Tags.Plugin, $"外部插件加载失败（{Path.GetFileName(error.AssemblyPath)}）：{error.Error}");
            }

            foreach (var discovered in load.Plugins)
            {
                // 外部插件不允许覆盖内置 id（zebra 等）；ALC 根随注册强持有
                if (registry.RegisterExternal(discovered.Plugin, discovered.AssemblyPath, hostLog, discovered.LoadContext))
                {
                    externalIds.Add(discovered.Plugin.Id);
                    HostLog.Info(HostLog.Tags.Plugin, $"已加载外部传输插件：{discovered.Plugin.Id}（{discovered.Plugin.DisplayName}，来自 {discovered.AssemblyPath}）");
                }
            }
        }
        catch (Exception ex)
        {
            // 目录扫描异常（IO 等）不阻断宿主：回退纯内置 zebra 能力
            HostLog.Warn(HostLog.Tags.Plugin, $"外部插件目录扫描失败（{pluginsPath}）：{ex.Message}");
        }

        HostLog.Info(HostLog.Tags.Plugin, $"插件通道装配完成：内置 zebra + 外部 {externalIds.Count} 个（目录 {pluginsPath}）");

        return new PluginHostState
        {
            Registry = registry,
            Installer = new PluginInstaller(
                pluginsPath,
                registry,
                hostLog,
                loadErrors,
                // 平台门：PDA 只安装 platforms 含 android 的包；无标记的存量 Windows 包拒绝
                hostPlatform: Core.Transport.Plugins.Package.PluginPlatforms.Android),
            PluginContext = new TransportPluginContext(hostLog, pluginDataDir),
            PluginsPath = pluginsPath,
            ExternalPluginIds = externalIds,
        };
    }
}
