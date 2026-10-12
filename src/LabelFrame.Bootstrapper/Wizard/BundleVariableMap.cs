using LabelFrame.Bootstrapper.Topology;

namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>Burn 变量契约：问卷解析结果与探测 / 落位信息 → Bundle 链条件变量——BA 写入引擎，Chain 内包以 Condition / InstallArguments 消费（变量名与 Bundle.wxs 跨文件契约，两侧同步）。</summary>
public static class BundleVariableMap
{
    /// <summary>预设 manifest id 变量（standalone / server-win / server-docker / server-linux / client）。</summary>
    public const string PresetVariable = "InstallPreset";

    /// <summary>是否安装 Server MSI（链内 server-msi 条件）。</summary>
    public const string ServerVariable = "InstallServer";

    /// <summary>是否安装 Client MSI（链内 client-msi 条件）。</summary>
    public const string ClientVariable = "InstallClient";

    /// <summary>是否带管理界面（webui 落位包条件）。</summary>
    public const string WebUiVariable = "InstallWebUi";

    /// <summary>是否安装 Zebra 品牌插件（plugin-zebra 落位包条件）。</summary>
    public const string ZebraPluginVariable = "InstallPluginZebra";

    /// <summary>.NET 10 Desktop Runtime 是否已装（链内 DetectCondition 消费，BA 于 Detect 前写入）。</summary>
    public const string DesktopRuntimeVariable = "DesktopRuntimeInstalled";

    /// <summary>ASP.NET Core Runtime 是否已装（链内 DetectCondition 消费，BA 于 Detect 前写入）。</summary>
    public const string AspNetCoreRuntimeVariable = "AspNetCoreRuntimeInstalled";

    /// <summary>WebView2 是否已装（链内 DetectCondition 消费，BA 于 Detect 前写入）。</summary>
    public const string WebView2Variable = "WebView2Installed";

    /// <summary>evergreen 源变量：WebView2Runtime 包装包 InstallArguments 以 [变量] 引用——BA 随确认页写入（布局目录本地文件在位优先，否则清单 urls 顺序拼接；空 = 工具官方 fwlink 兜底）。</summary>
    public const string WebView2SourceVariable = "WebView2Source";

    /// <summary>webui 落位目标目录（链内落位包 InstallArguments 以 [变量] 引用）。</summary>
    public const string WebUiTargetDirVariable = "WebUiTargetDir";

    /// <summary>Zebra 插件落位目标目录（链内落位包 InstallArguments 以 [变量] 引用；pluginId 映射见 <see cref="BrandPluginMap"/>）。</summary>
    public const string PluginZebraTargetDirVariable = "PluginZebraTargetDir";

    /// <summary>webui 落位凭据在位（清理包 WebUiPlacementCleanup DetectCondition 消费——BA 启动期读凭据比对 WixBundleVersion 写入）。</summary>
    public const string WebUiPlacementPresentVariable = "WebUiPlacementPresent";

    /// <summary>zebra 插件落位凭据在位（清理包 ZebraPluginPlacementCleanup DetectCondition 消费）。</summary>
    public const string ZebraPluginPlacementPresentVariable = "ZebraPluginPlacementPresent";

    /// <summary>拓扑计划 → 变量集合（string 值；数值变量由 BA 写引擎时转 long）。</summary>
    public static IReadOnlyDictionary<string, string> ToVariables(TopologyPlan plan)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(plan);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }
#endif

        var ids = new HashSet<string>(plan.Components.Select(item => item.Component.Id), StringComparer.Ordinal);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PresetVariable] = plan.Preset.ToManifestId(),
            [ServerVariable] = Flag(ids.Contains("server-msi")),
            [ClientVariable] = Flag(ids.Contains("client-msi")),
            [WebUiVariable] = Flag(ids.Contains("webui")),
            [ZebraPluginVariable] = Flag(ids.Contains("plugin-zebra")),
        };
    }

    /// <summary>webui 落位目标目录（DEPLOY §5：放入即生效）。</summary>
    public static string WebUiTargetDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabelFrame", "server", "plugins", "web-ui");

    /// <summary>Zebra 插件落位目标目录（DEPLOY §6：plugins\&lt;pluginId&gt;；pluginId 映射见 <see cref="BrandPluginMap"/>）。</summary>
    public static string PluginZebraTargetDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabelFrame", "Client", "plugins", BrandPluginMap.PluginIdOf("zebra") ?? "plugin-zebra");

    private static string Flag(bool enabled) => enabled ? "1" : "0";
}
