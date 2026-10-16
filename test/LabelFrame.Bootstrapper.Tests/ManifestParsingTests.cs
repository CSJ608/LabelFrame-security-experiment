using LabelFrame.Bootstrapper.Manifest;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

public sealed class ManifestParsingTests
{
    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Manifests", fileName));

    [Fact]
    public void Parse_current_manifest_should_load_seven_components()
    {
        // 对齐当前 CI 产物现状：七组件（官方插件 plugin-zebra / plugin-tspl 已收录）、dependsOn 全空
        var manifest = InstallManifest.Parse(LoadFixture("install-manifest.current.json"));

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("0.27.0", manifest.LabelframeVersion);
        Assert.Equal(
            new[] { "server-msi", "client-msi", "webui", "linux-server", "pda-apk", "plugin-zebra", "plugin-tspl" }.ToList(),
            manifest.Components.Select(component => component.Id).ToList());
        Assert.All(manifest.Components, component => Assert.Empty(component.DependsOn));
        Assert.All(manifest.Components, component => Assert.Single(component.Urls));
        var plugin = Assert.Single(manifest.Components, component => component.Id == "plugin-zebra");
        Assert.Equal("lfplugin", plugin.Type);
        Assert.Equal("0.27.0", plugin.Version);  // 官方插件版本 = 主版本
        var tspl = Assert.Single(manifest.Components, component => component.Id == "plugin-tspl");
        Assert.Equal("lfplugin", tspl.Type);
    }

    [Fact]
    public void Parse_pre_plugin_manifest_should_still_be_accepted()
    {
        // 存量 Release 清单（≤0.26，无 plugin 条目）：schema 兼容，旧清单照常解析（品牌页展示「无可选品牌」）
        var manifest = InstallManifest.Parse(LoadFixture("install-manifest.pre-plugin.json"));

        Assert.Equal("0.26.0", manifest.LabelframeVersion);
        Assert.Equal(
            new[] { "server-msi", "client-msi", "webui", "linux-server", "pda-apk" }.ToList(),
            manifest.Components.Select(component => component.Id).ToList());
    }

    [Fact]
    public void Parse_full_manifest_should_load_runtime_and_plugin_entries()
    {
        // 全量形态 fixture：runtime 双条目 + dependsOn 闭包 + plugin-zebra 条目
        var manifest = InstallManifest.Parse(LoadFixture("install-manifest.full.json"));

        Assert.Equal(10, manifest.Components.Count);
        Assert.Contains(manifest.Components, component => component.Id == "runtime-desktop" && component.Type == "runtime");
        Assert.Contains(manifest.Components, component => component.Id == "runtime-aspnetcore" && component.Type == "runtime");
        Assert.Contains(manifest.Components, component => component.Id == "plugin-zebra" && component.Type == "lfplugin");
        var serverMsi = Assert.Single(manifest.Components, component => component.Id == "server-msi");
        Assert.Equal(new[] { "runtime-desktop", "runtime-aspnetcore" }.ToList(), serverMsi.DependsOn.ToList());
        var clientMsi = Assert.Single(manifest.Components, component => component.Id == "client-msi");
        Assert.Equal(new[] { "runtime-desktop", "runtime-aspnetcore", "runtime-webview2" }.ToList(), clientMsi.DependsOn.ToList());
    }

    [Fact]
    public void Parse_schema_version_too_new_should_fail_closed_with_upgrade_hint()
    {
        // 演进规则：更新 schema 的清单拒绝并提示先升级引导程序（fail-closed）
        var json = LoadFixture("install-manifest.current.json").Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");

        var ex = Assert.Throws<InstallManifestFormatException>(() => InstallManifest.Parse(json));
        Assert.Contains("升级引导程序", ex.Message);
    }

    [Theory]
    [InlineData("\"type\": \"msi\"", "\"type\": \"unknown-type\"")]
    [InlineData("\"sha256\": \"3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c\"", "\"sha256\": \"NOT-A-HASH\"")]
    [InlineData("\"sizeBytes\": 11534336", "\"sizeBytes\": 0")]
    [InlineData("\"topologies\": [\"standalone\", \"server-win\", \"offline\"]", "\"topologies\": [\"standalone\", \"server-win\", \"not-a-topology\"]")]
    public void Parse_invalid_component_field_should_reject(string original, string replaced)
    {
        var json = LoadFixture("install-manifest.current.json").Replace(original, replaced);

        Assert.Throws<InstallManifestFormatException>(() => InstallManifest.Parse(json));
    }

    [Fact]
    public void Parse_missing_required_field_should_reject()
    {
        // 将 server-msi 的 sha256 置空（该哈希值在 fixture 中唯一，替换不受行尾影响）
        var json = LoadFixture("install-manifest.current.json")
            .Replace("3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c", string.Empty);

        var ex = Assert.Throws<InstallManifestFormatException>(() => InstallManifest.Parse(json));
        Assert.Contains("sha256", ex.Message);
    }

    [Fact]
    public void Parse_depends_on_missing_reference_should_reject()
    {
        // 依赖只约束同集合内组件——引用不存在的条目即清单非法（首个 dependsOn 属 server-msi）
        var json = new System.Text.RegularExpressions.Regex(@"""dependsOn"": \[\]")
            .Replace(LoadFixture("install-manifest.current.json"), "\"dependsOn\": [\"runtime-desktop\"]", 1);

        var ex = Assert.Throws<InstallManifestFormatException>(() => InstallManifest.Parse(json));
        Assert.Contains("runtime-desktop", ex.Message);
    }

    [Fact]
    public void Parse_unknown_optional_field_should_be_ignored()
    {
        // 演进规则：新增可选字段 = 兼容（旧引导程序忽略未知字段）
        var json = LoadFixture("install-manifest.current.json")
            .Replace("\"generatedAt\": \"2026-09-14T03:00:00Z\",", "\"generatedAt\": \"2026-09-14T03:00:00Z\", \"futureOptionalField\": \"whatever\",");

        var manifest = InstallManifest.Parse(json);
        Assert.Equal(7, manifest.Components.Count);
    }
}
