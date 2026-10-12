using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Tests.TestInfrastructure;
using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>问卷会话（UI 无关状态机）：清单加载、品牌预选联动、组件集合计算的前置校验。</summary>
public sealed class WizardSessionTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Manifests", fileName);

    [Fact]
    public async Task Load_manifest_with_zdesigner_printer_and_plugin_entry_should_preselect_zebra()
    {
        var session = new WizardSession(installedPrinterNames: () => ["ZDesigner ZD421-203dpi ZPL"])
        {
            ManifestSource = FixturePath("install-manifest.full.json"),
        };

        await session.LoadManifestAsync();

        Assert.True(session.SelectedBrands.SetEquals(["zebra"]));
        Assert.Equal(new[] { "zebra" }.ToList(), session.AvailableBrands.ToList());
        Assert.NotNull(session.Manifest);
    }

    [Fact]
    public async Task Load_pre_plugin_manifest_should_select_no_brand()
    {
        // 存量 Release 清单形态（≤0.26 无 plugin 条目）：检测到 Zebra 驱动也不预选（无条目可选）
        var session = new WizardSession(installedPrinterNames: () => ["ZDesigner ZD421-203dpi ZPL"])
        {
            ManifestSource = FixturePath("install-manifest.pre-plugin.json"),
        };

        await session.LoadManifestAsync();

        Assert.Empty(session.SelectedBrands);
        Assert.Empty(session.AvailableBrands);
    }

    [Fact]
    public async Task Load_manifest_without_zebra_driver_should_select_no_brand()
    {
        var session = new WizardSession(installedPrinterNames: () => ["HP LaserJet M404"])
        {
            ManifestSource = FixturePath("install-manifest.full.json"),
        };

        await session.LoadManifestAsync();

        Assert.Empty(session.SelectedBrands);
    }

    [Fact]
    public async Task Load_manifest_too_new_schema_should_fail_closed()
    {
        var session = new WizardSession { ManifestSource = FixturePath("install-manifest.current.json") };
        using var handler = new RecordingHttpMessageHandler(
            File.ReadAllText(FixturePath("install-manifest.current.json")).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 9"));
        using var http = new HttpClient(handler);

        // 本地路径来源 + 注入 http 不会触发下载——直接换 URL 来源验证 fail-closed
        session.ManifestSource = "https://example.invalid/install-manifest.json";
        var ex = await Assert.ThrowsAsync<InstallManifestFormatException>(() => session.LoadManifestAsync(http));

        Assert.Contains("升级引导程序", ex.Message);
        Assert.Null(session.Manifest);
    }

    [Fact]
    public async Task Build_plan_should_answer_the_full_questionnaire()
    {
        // 端到端：加载 → 预设 → 品牌预选 → 开关 → 组件集合（dry-run 预览的数据链）
        var session = new WizardSession(installedPrinterNames: () => ["ZDesigner ZD421-203dpi ZPL", "Microsoft Print to PDF"])
        {
            ManifestSource = FixturePath("install-manifest.full.json"),
            Preset = TopologyPreset.Client,
        };
        await session.LoadManifestAsync();
        session.IncludeWebUi = true; // client 预设不适用，开关被 topologies 过滤

        var plan = session.BuildPlan();

        Assert.Equal(TopologyPreset.Client, plan.Preset);
        // client 预设含 aspnetcore（二次，——WinHost 亦 Sdk.Web 隐式依赖）
        Assert.Equal(
            new[] { "runtime-desktop", "runtime-aspnetcore", "runtime-webview2", "client-msi", "plugin-zebra" }.ToList(),
            plan.Components.Select(item => item.Component.Id).ToList());
    }

    [Fact]
    public void Build_plan_without_manifest_should_reject_with_actionable_message()
    {
        var session = new WizardSession { Preset = TopologyPreset.Standalone };

        var ex = Assert.Throws<InvalidOperationException>(() => session.BuildPlan());
        Assert.Contains("清单", ex.Message);
    }

    [Fact]
    public async Task Build_plan_without_preset_should_reject_with_actionable_message()
    {
        var session = new WizardSession { ManifestSource = FixturePath("install-manifest.current.json") };
        await session.LoadManifestAsync();

        var ex = Assert.Throws<InvalidOperationException>(() => session.BuildPlan());
        Assert.Contains("本机角色", ex.Message);
    }

    // ---- 隐式优先源目录：本地清单所在目录 = 布局目录 ----

    [Fact]
    public async Task Local_manifest_source_resolves_local_source_directory()
    {
        var session = new WizardSession { ManifestSource = FixturePath("install-manifest.full.json") };

        await session.LoadManifestAsync();

        Assert.Equal(
            Path.GetDirectoryName(Path.GetFullPath(FixturePath("install-manifest.full.json"))),
            session.LocalSourceDirectory);
    }

    [Fact]
    public async Task Url_manifest_source_has_no_local_source_directory()
    {
        // 回归锚点：URL 清单（默认稳定通道形态）→ 无本地源，纯 urls，行为与现状一致
        using var handler = new RecordingHttpMessageHandler(File.ReadAllText(FixturePath("install-manifest.current.json")));
        using var http = new HttpClient(handler);
        var session = new WizardSession
        {
            ManifestSource = "https://github.com/marci-labs/LabelFrame/releases/latest/download/install-manifest.json",
        };

        await session.LoadManifestAsync(http);

        Assert.NotNull(session.Manifest);
        Assert.Null(session.LocalSourceDirectory);
    }
}
