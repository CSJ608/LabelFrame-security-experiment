namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>Bundle 链包 id ↔ manifest 组件 id 映射（DESIGN 链序表；BA 进度 / 完成 / 失败页的展示名来源）。</summary>
/// <remarks>
/// 与 <c>packaging/bootstrapper/Bundle.wxs</c> 的 Chain 包 id 一一对应（后续品牌插件按 <c>&lt;Brand&gt;PluginPlacement</c> 同构扩展）。
/// 新增成对清理包（<c>*PlacementCleanup</c> / <c>*PluginPlacementCleanup</c>）映射到同组件——
/// 进度页按包→组件映射展示（卸载 / Modify 清理阶段同组件名）；反向表（组件→包，完成页包状态查询用）取**落位包**优先
/// （成对包中先声明者，登记状态与组件安装语义一致）。
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
        };

    /// <summary>manifest 组件 id → 链包 id（反向；同组件多包时取声明序靠前者 = 落位包）。</summary>
    public static readonly IReadOnlyDictionary<string, string> PackageIdByComponent =
        ComponentByPackageId
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
}
