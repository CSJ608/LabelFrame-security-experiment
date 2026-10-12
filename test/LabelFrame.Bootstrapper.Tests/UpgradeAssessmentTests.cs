using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Prerequisites;
using LabelFrame.Bootstrapper.Upgrade;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>升级评估矩阵（DESIGN 组件级口径表）：已装快照 × manifest 目标版本 → 逐组件动作与整体判定。</summary>
public sealed class UpgradeAssessmentTests
{
    private static readonly RuntimeProbeResult RuntimeNone = new(false, null, false, null, false);

    private static ManifestComponent Component(string id, string type, string version) => new(
        id, type, version, [], ["https://example.invalid/a"], new string('a', 64), 1, null, ["standalone"], null);

    private static InstallManifest ManifestOf(params ManifestComponent[] components) =>
        new(1, "0.27.0", "2026-09-14T00:00:00Z", components);

    private static LocalInstallSnapshot SnapshotOf(
        string? server = null,
        string? client = null,
        string? plugin = null,
        RuntimeProbeResult? runtime = null) => new(server, client, plugin, runtime ?? RuntimeNone);

    [Fact]
    public void Nothing_installed_yields_all_install_and_not_up_to_date()
    {
        var manifest = ManifestOf(Component("server-msi", "msi", "0.27.0"), Component("client-msi", "msi", "0.27.0"));

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf());

        Assert.All(assessment.Entries.Where(entry => entry.ComponentId is "server-msi" or "client-msi"),
            entry => Assert.Equal(ComponentUpgradeAction.Install, entry.Action));
        Assert.False(assessment.AnyInstalled);
        Assert.False(assessment.HasUpgradable);
        Assert.False(assessment.IsUpToDate);
    }

    [Fact]
    public void Older_installed_components_yield_upgrade()
    {
        var manifest = ManifestOf(Component("server-msi", "msi", "0.27.0"), Component("client-msi", "msi", "0.27.0"));

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf(server: "0.26.0", client: "0.26.0"));

        Assert.True(assessment.AnyInstalled);
        Assert.True(assessment.HasUpgradable);
        Assert.False(assessment.IsUpToDate);
        Assert.All(assessment.Entries.Where(entry => entry.ComponentId is "server-msi" or "client-msi"),
            entry =>
            {
                Assert.Equal(ComponentUpgradeAction.Upgrade, entry.Action);
                Assert.Equal("0.26.0", entry.InstalledVersion);
                Assert.Equal("0.27.0", entry.TargetVersion);
            });
    }

    [Fact]
    public void Same_versions_yield_up_to_date()
    {
        var manifest = ManifestOf(Component("server-msi", "msi", "0.27.0"));

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf(server: "0.27.0"));

        Assert.True(assessment.AnyInstalled);
        Assert.False(assessment.HasUpgradable);
        Assert.True(assessment.IsUpToDate);
        Assert.Equal(ComponentUpgradeAction.UpToDate, assessment.Entries.Single(entry => entry.ComponentId == "server-msi").Action);
    }

    [Fact]
    public void Equal_versions_with_missing_segments_are_up_to_date()
    {
        // 统一比较器：缺失段视为 0——0.27 == 0.27.0
        var manifest = ManifestOf(Component("client-msi", "msi", "0.27.0"));

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf(client: "0.27"));

        Assert.True(assessment.IsUpToDate);
        Assert.Equal(ComponentUpgradeAction.UpToDate, assessment.Entries.Single().Action);
    }

    [Fact]
    public void Newer_local_msi_yields_local_newer()
    {
        var manifest = ManifestOf(Component("client-msi", "msi", "0.26.0"));

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf(client: "0.27.0"));

        // 本机更新：无待升级项（整体「已是最新」成立），条目动作为 LocalNewer（提示清单旧于本机）
        Assert.False(assessment.HasUpgradable);
        Assert.True(assessment.IsUpToDate);
        Assert.Equal(ComponentUpgradeAction.LocalNewer, assessment.Entries.Single().Action);
    }

    [Fact]
    public void Uninstalled_component_does_not_affect_up_to_date_verdict()
    {
        // 分离部署只装服务端：未装的 client-msi 属「新装」，不影响服务端「已是最新」判定
        var manifest = ManifestOf(Component("server-msi", "msi", "0.27.0"), Component("client-msi", "msi", "0.27.0"));

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf(server: "0.27.0"));

        Assert.True(assessment.IsUpToDate);
        Assert.Equal(ComponentUpgradeAction.Install, assessment.Entries.Single(entry => entry.ComponentId == "client-msi").Action);
    }

    [Fact]
    public void Plugin_entry_compares_installed_plugin_version()
    {
        var manifest = ManifestOf(Component("plugin-zebra", "lfplugin", "0.27.0"));

        var older = UpgradeAssessor.Assess(manifest, SnapshotOf(plugin: "0.26.0"));
        Assert.Equal(ComponentUpgradeAction.Upgrade, older.Entries.Single().Action);

        var same = UpgradeAssessor.Assess(manifest, SnapshotOf(plugin: "0.27.0"));
        Assert.True(same.IsUpToDate);
        Assert.Equal(ComponentUpgradeAction.UpToDate, same.Entries.Single().Action);

        var none = UpgradeAssessor.Assess(manifest, SnapshotOf());
        Assert.Equal(ComponentUpgradeAction.Install, none.Entries.Single().Action);
    }

    [Fact]
    public void Desktop_runtime_newer_than_manifest_is_up_to_date()
    {
        // 共享系统组件：本机更新即满足（执行侧 DetectCondition 只判 ≥ 10.0.0， 表）
        var manifest = ManifestOf(Component("runtime-desktop", "runtime", "10.0.12"));
        var runtime = new RuntimeProbeResult(true, "10.0.13", false, null, false);

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf(runtime: runtime));

        Assert.Equal(ComponentUpgradeAction.UpToDate, assessment.Entries.Single().Action);
        Assert.True(assessment.IsUpToDate);
    }

    [Fact]
    public void Desktop_runtime_older_than_manifest_yields_upgrade()
    {
        var manifest = ManifestOf(Component("runtime-desktop", "runtime", "10.0.12"));
        var runtime = new RuntimeProbeResult(true, "10.0.8", false, null, false);

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf(runtime: runtime));

        Assert.Equal(ComponentUpgradeAction.Upgrade, assessment.Entries.Single().Action);
    }

    [Fact]
    public void AspNetCore_runtime_follows_same_shared_component_semantics_as_desktop()
    {
        // runtime-aspnetcore 与 runtime-desktop 同为共享系统组件——本机更新即满足、落后即升级
        var manifest = ManifestOf(Component("runtime-aspnetcore", "runtime", "10.0.12"));

        var newer = UpgradeAssessor.Assess(manifest, SnapshotOf(runtime: new RuntimeProbeResult(false, null, true, "10.0.13", false)));
        Assert.Equal(ComponentUpgradeAction.UpToDate, newer.Entries.Single().Action);
        Assert.Equal("10.0.13", newer.Entries.Single().InstalledVersion);

        var older = UpgradeAssessor.Assess(manifest, SnapshotOf(runtime: new RuntimeProbeResult(false, null, true, "10.0.8", false)));
        Assert.Equal(ComponentUpgradeAction.Upgrade, older.Entries.Single().Action);

        var missing = UpgradeAssessor.Assess(manifest, SnapshotOf());
        Assert.Equal(ComponentUpgradeAction.Install, missing.Entries.Single().Action);
    }

    [Fact]
    public void Webview2_evergreen_never_version_compares()
    {
        var manifest = ManifestOf(Component("runtime-webview2", "runtime", "evergreen"));

        var installed = UpgradeAssessor.Assess(manifest, SnapshotOf(runtime: new RuntimeProbeResult(false, null, false, null, true)));
        Assert.Equal(ComponentUpgradeAction.UpToDate, installed.Entries.Single().Action);
        Assert.True(installed.IsUpToDate);
        Assert.Null(installed.Entries.Single().InstalledVersion); // 只判存在，无版本串

        var missing = UpgradeAssessor.Assess(manifest, SnapshotOf());
        Assert.Equal(ComponentUpgradeAction.Install, missing.Entries.Single().Action);
    }

    [Fact]
    public void Webui_and_non_local_components_are_not_tracked()
    {
        var manifest = ManifestOf(
            Component("server-msi", "msi", "0.27.0"),
            Component("webui", "webui-zip", "0.27.0"),
            Component("linux-server", "archive", "0.27.0"),
            Component("pda-apk", "apk", "0.27.0"));

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf(server: "0.27.0"));

        Assert.All(assessment.Entries.Where(entry => entry.ComponentId != "server-msi"),
            entry => Assert.Equal(ComponentUpgradeAction.NotTracked, entry.Action));
        Assert.True(assessment.IsUpToDate); // 仅有的已装组件（server）最新，不参与比较的条目不拖累判定
    }

    [Fact]
    public void Mixed_state_reports_both_upgrade_and_fresh_components()
    {
        var manifest = ManifestOf(
            Component("server-msi", "msi", "0.27.0"),
            Component("client-msi", "msi", "0.27.0"),
            Component("plugin-zebra", "lfplugin", "0.27.0"));

        var assessment = UpgradeAssessor.Assess(manifest, SnapshotOf(server: "0.27.0", client: "0.26.0"));

        Assert.False(assessment.IsUpToDate);
        Assert.Equal(ComponentUpgradeAction.UpToDate, assessment.Entries.Single(entry => entry.ComponentId == "server-msi").Action);
        Assert.Equal(ComponentUpgradeAction.Upgrade, assessment.Entries.Single(entry => entry.ComponentId == "client-msi").Action);
        Assert.Equal(ComponentUpgradeAction.Install, assessment.Entries.Single(entry => entry.ComponentId == "plugin-zebra").Action);
    }

    [Theory]
    [InlineData("0.26.0", "0.27.0", -1)]
    [InlineData("0.27.0", "0.26.0", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("v0.27.0", "0.27.0", 0)] // 容忍 v 前缀（RuntimeProbe.ParseVersion 语义）
    [InlineData("2.0.0", "10.0.0", -1)] // 数值比较（非字符串序）
    [InlineData("abc.1", "abc.2", -1)] // 双方不可解析 → Ordinal
    public void Version_semantics_follows_unified_comparator(string left, string right, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(VersionSemantics.Compare(left, right)));
    }

    [Fact]
    public void Plugin_version_compare_delegates_to_unified_semantics()
    {
        // 既有签名委托统一实现（/ 单一语义）：行为不变
        Assert.Equal(0, Math.Sign(Placement.PayloadPlacer.ComparePluginVersions("1.0", "1.0.0")));
        Assert.Equal(1, Math.Sign(Placement.PayloadPlacer.ComparePluginVersions("1.0.1", "1.0.0")));
        Assert.Equal(-1, Math.Sign(Placement.PayloadPlacer.ComparePluginVersions("2.0.0", "10.0.0")));
    }
}
