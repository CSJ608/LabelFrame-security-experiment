namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>Bundle 链包 id ↔ manifest 组件 id 映射（BA 进度 / 完成 / 失败页的展示名来源）。</summary>
/// <remarks>
/// 兼容义务：与 <c>packaging/bootstrapper/Bundle.wxs</c> 的 Chain 包 id 一一对应，改一侧即展示失配。
/// 成对清理包（<c>*PlacementCleanup</c>）映射到同组件；反向表（组件→包，完成页包状态查询用）取落位包优先
/// （成对包中先声明者——登记状态与组件安装语义一致）。
/// </remarks>
public static class ChainPackageMap
{
    /// <summary>链包 id → manifest 组件 id。</summary>
    public static readonly IReadOnlyDictionary<string, string> ComponentByPackageId =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DotNetDesktopRuntime"] = "runtime-desktop",
            ["DotNetAspNetCoreRuntime"] = "runtime-aspnetcore",
            ["WebView2Runtime"] = "runtime-webview2",
            ["ServerMsi"] = "server-msi",
            ["ClientMsi"] = "client-msi",
            ["WebUiPlacement"] = "webui",
            ["WebUiPlacementCleanup"] = "webui",
            ["ZebraPluginPlacement"] = "plugin-zebra",
            ["ZebraPluginPlacementCleanup"] = "plugin-zebra",
            ["TsplPluginPlacement"] = "plugin-tspl",
            ["TsplPluginPlacementCleanup"] = "plugin-tspl",
        };

    /// <summary>manifest 组件 id → 链包 id（反向；同组件多包时取声明序靠前者 = 落位包）。</summary>
    public static readonly IReadOnlyDictionary<string, string> PackageIdByComponent =
        ComponentByPackageId
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
}
