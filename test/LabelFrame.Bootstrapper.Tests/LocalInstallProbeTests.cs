using LabelFrame.Bootstrapper.Prerequisites;
using LabelFrame.Bootstrapper.Upgrade;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>本机已装探测：委托注入下的 MSI 版本选取、插件 manifest 读取与异常兜底。</summary>
public sealed class LocalInstallProbeTests
{
    private static RuntimeProbeResult Runtime => new(true, "10.0.12", true, "10.0.12", true);

    [Fact]
    public void No_msi_products_yield_null_versions()
    {
        var probe = new LocalInstallProbe(
            msiProductsByUpgradeCode: _ => [],
            msiProductVersion: _ => throw new InvalidOperationException("不应查询版本"));

        var snapshot = probe.Probe(Runtime);

        Assert.Null(snapshot.ServerMsiVersion);
        Assert.Null(snapshot.ClientMsiVersion);
    }

    [Fact]
    public void Msi_products_are_matched_by_upgrade_code()
    {
        var queries = new List<string>();
        var probe = new LocalInstallProbe(
            msiProductsByUpgradeCode: code =>
            {
                queries.Add(code);
                return code == LocalInstallProbe.ClientUpgradeCode ? ["{B36199C2-6C6F-4F70-BFE5-92A9A21C4B7E}"] : [];
            },
            msiProductVersion: productCode => productCode == "{B36199C2-6C6F-4F70-BFE5-92A9A21C4B7E}" ? "0.26.0" : null);

        var snapshot = probe.Probe(Runtime);

        Assert.Null(snapshot.ServerMsiVersion);
        Assert.Equal("0.26.0", snapshot.ClientMsiVersion);
        Assert.Equal([LocalInstallProbe.ServerUpgradeCode, LocalInstallProbe.ClientUpgradeCode], queries); // Probe 顺序：服务端 → 客户端
    }

    [Fact]
    public void Multiple_products_same_family_pick_highest_version()
    {
        // 同族多产品（残留 / 多上下文）取最高（组件级口径）
        var probe = new LocalInstallProbe(
            msiProductsByUpgradeCode: _ => ["{00000000-0000-0000-0000-000000000001}", "{00000000-0000-0000-0000-000000000002}"],
            msiProductVersion: productCode => productCode.EndsWith("0001") ? "0.25.0" : "0.26.0");

        var snapshot = probe.Probe(Runtime);

        Assert.Equal("0.26.0", snapshot.ServerMsiVersion);
        Assert.Equal("0.26.0", snapshot.ClientMsiVersion);
    }

    [Fact]
    public void Product_with_unreadable_version_is_skipped()
    {
        var probe = new LocalInstallProbe(
            msiProductsByUpgradeCode: _ => ["{00000000-0000-0000-0000-000000000001}", "{00000000-0000-0000-0000-000000000002}"],
            msiProductVersion: productCode => productCode.EndsWith("0001") ? null : "0.26.0");

        var snapshot = probe.Probe(Runtime);

        Assert.Equal("0.26.0", snapshot.ClientMsiVersion);
    }

    [Fact]
    public void Plugin_version_read_from_placement_manifest()
    {
        var probe = new LocalInstallProbe(
            msiProductsByUpgradeCode: _ => [],
            pluginManifestTextReader: path => path.EndsWith("labelframe-transport-zebra\\manifest.json", StringComparison.Ordinal)
                ? """{ "pluginId": "labelframe-transport-zebra", "name": "Zebra", "version": "0.26.0" }"""
                : null);

        var snapshot = probe.Probe(Runtime);

        Assert.Equal("0.26.0", snapshot.ZebraPluginVersion);
    }

    [Theory]
    [InlineData(null)] // 目录 / 文件不存在
    [InlineData("")] // 空文件
    [InlineData("not-json")] // 损坏
    [InlineData("""{ "pluginId": "labelframe-transport-zebra" }""")] // 缺 version 字段
    public void Invalid_plugin_manifest_yields_null(string? text)
    {
        var probe = new LocalInstallProbe(
            msiProductsByUpgradeCode: _ => [],
            pluginManifestTextReader: _ => text);

        Assert.Null(probe.Probe(Runtime).ZebraPluginVersion);
    }

    [Fact]
    public void Runtime_result_is_passed_through()
    {
        var probe = new LocalInstallProbe(msiProductsByUpgradeCode: _ => []);
        var runtime = new RuntimeProbeResult(false, null, false, null, true);

        var snapshot = probe.Probe(runtime);

        Assert.Same(runtime, snapshot.Runtime);
    }
}
