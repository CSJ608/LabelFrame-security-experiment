using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>Burn 变量契约：问卷解析结果 → Bundle 链条件变量（BA 写入引擎、Chain 条件消费）。</summary>
public sealed class BundleVariableMapTests
{
    private static InstallManifest LoadManifest(string fileName) =>
        InstallManifest.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Manifests", fileName)));

    private static TopologyResolver Resolver { get; } = new();

    [Fact]
    public void To_variables_standalone_with_webui_and_zebra_should_set_all_flags()
    {
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.full.json"),
            TopologyPreset.Standalone,
            new TopologyOptions(new HashSet<string>(["zebra"], StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("standalone", variables[BundleVariableMap.PresetVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("1", variables[BundleVariableMap.WebUiVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ZebraPluginVariable]);
    }

    [Fact]
    public void To_variables_standalone_default_should_clear_switch_flags()
    {
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.full.json"),
            TopologyPreset.Standalone,
            new TopologyOptions(new HashSet<string>(StringComparer.Ordinal), IncludeWebUi: false));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("1", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("0", variables[BundleVariableMap.WebUiVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ZebraPluginVariable]);
    }

    [Fact]
    public void To_variables_client_preset_should_set_client_only()
    {
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.full.json"),
            TopologyPreset.Client,
            new TopologyOptions(new HashSet<string>(StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("client", variables[BundleVariableMap.PresetVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ClientVariable]);
        // webui 未标记 client 预设：开关开着也不纳入（topologies 过滤传导到变量）
        Assert.Equal("0", variables[BundleVariableMap.WebUiVariable]);
    }

    [Fact]
    public void To_variables_server_win_should_set_server_and_not_client()
    {
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.full.json"),
            TopologyPreset.ServerWin,
            new TopologyOptions(new HashSet<string>(StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("server-win", variables[BundleVariableMap.PresetVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("1", variables[BundleVariableMap.WebUiVariable]);
    }

    [Fact]
    public void To_variables_current_manifest_with_zebra_selected_should_set_zebra_flag()
    {
        // 当前产物现状（plugin-zebra / plugin-tspl 条目已收录）：勾选 Zebra → InstallPluginZebra 置位、TSPL 不置位（接线主线）
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.current.json"),
            TopologyPreset.Standalone,
            new TopologyOptions(new HashSet<string>(["zebra"], StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("1", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("1", variables[BundleVariableMap.WebUiVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ZebraPluginVariable]);
        Assert.Equal("0", variables[BundleVariableMap.TsplPluginVariable]);
    }

    [Fact]
    public void To_variables_current_manifest_with_tspl_selected_should_set_tspl_flag()
    {
        // 指令集维度品牌选项：勾选 tspl → InstallPluginTspl 置位（同一插件服务 TSC 及兼容机）
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.current.json"),
            TopologyPreset.Standalone,
            new TopologyOptions(new HashSet<string>(["tspl"], StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("1", variables[BundleVariableMap.TsplPluginVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ZebraPluginVariable]);
    }

    [Fact]
    public void To_variables_pre_plugin_manifest_without_plugin_entry_should_clear_zebra_flag()
    {
        // 存量 Release 清单（≤0.26 无 plugin-zebra 条目）：勾选 Zebra 也不置位（选项来源仅为已有条目）
        var plan = Resolver.Resolve(
            LoadManifest("install-manifest.pre-plugin.json"),
            TopologyPreset.Standalone,
            new TopologyOptions(new HashSet<string>(["zebra"], StringComparer.Ordinal), IncludeWebUi: true));
        var variables = BundleVariableMap.ToVariables(plan);

        Assert.Equal("1", variables[BundleVariableMap.ServerVariable]);
        Assert.Equal("1", variables[BundleVariableMap.ClientVariable]);
        Assert.Equal("1", variables[BundleVariableMap.WebUiVariable]);
        Assert.Equal("0", variables[BundleVariableMap.ZebraPluginVariable]);
    }

    [Fact]
    public void Placement_target_directories_should_follow_deploy_conventions()
    {
        // 落位目标目录（DEPLOY §5 / §6）：webui → server\plugins\web-ui；插件 → Client\plugins\<pluginId>
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        Assert.Equal(Path.Combine(programData, "LabelFrame", "server", "plugins", "web-ui"), BundleVariableMap.WebUiTargetDir());
        Assert.Equal(Path.Combine(programData, "LabelFrame", "Client", "plugins", "labelframe-transport-zebra"), BundleVariableMap.PluginZebraTargetDir());
        Assert.Equal(Path.Combine(programData, "LabelFrame", "Client", "plugins", "labelframe-transport-tspl"), BundleVariableMap.PluginTsplTargetDir());
    }

    [Fact]
    public void Chain_package_map_should_cover_contract_component_ids()
    {
        // 链包 → 组件 id 映射（十一包链含三组清理包）：八组件全覆盖（后续品牌按同构扩展）
        Assert.Equal(11, ChainPackageMap.ComponentByPackageId.Count);
        foreach (var componentId in new[] { "runtime-desktop", "runtime-aspnetcore", "runtime-webview2", "server-msi", "client-msi", "webui", "plugin-zebra", "plugin-tspl" })
        {
            Assert.True(ChainPackageMap.PackageIdByComponent.ContainsKey(componentId), $"缺组件映射：{componentId}");
        }

        // 成对清理包映射到同组件（进度页卸载 / 清理阶段同组件名展示）；反向表取落位包优先（登记状态与组件安装语义一致）
        Assert.Equal("webui", ChainPackageMap.ComponentByPackageId["WebUiPlacementCleanup"]);
        Assert.Equal("plugin-zebra", ChainPackageMap.ComponentByPackageId["ZebraPluginPlacementCleanup"]);
        Assert.Equal("plugin-tspl", ChainPackageMap.ComponentByPackageId["TsplPluginPlacementCleanup"]);
        Assert.Equal("WebUiPlacement", ChainPackageMap.PackageIdByComponent["webui"]);
        Assert.Equal("ZebraPluginPlacement", ChainPackageMap.PackageIdByComponent["plugin-zebra"]);
        Assert.Equal("TsplPluginPlacement", ChainPackageMap.PackageIdByComponent["plugin-tspl"]);
    }
}
