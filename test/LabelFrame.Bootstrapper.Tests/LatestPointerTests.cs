using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Upgrade;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>latest.json 指针：解析与来源推导规则（生成契约见 generate-install-manifest.ps1）。</summary>
public sealed class LatestPointerTests
{
    [Fact]
    public void Parse_valid_pointer()
    {
        var pointer = LatestPointer.Parse(
            """{ "labelframeVersion": "0.27.0", "manifestUrl": "https://github.com/marci-labs/LabelFrame/releases/download/v0.27.0/install-manifest.json" }""");

        Assert.NotNull(pointer);
        Assert.Equal("0.27.0", pointer.LabelframeVersion);
        Assert.Equal("https://github.com/marci-labs/LabelFrame/releases/download/v0.27.0/install-manifest.json", pointer.ManifestUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("""{ "manifestUrl": "https://example.invalid/install-manifest.json" }""")] // 缺 labelframeVersion
    [InlineData("""{ "labelframeVersion": "", "manifestUrl": "x" }""")]
    public void Parse_invalid_yields_null(string? json)
    {
        Assert.Null(LatestPointer.Parse(json!));
    }

    [Fact]
    public void Parse_ignores_unknown_members()
    {
        // 演进规则：新增可选字段 = 兼容
        var pointer = LatestPointer.Parse("""{ "labelframeVersion": "0.27.0", "manifestUrl": "u", "extra": 1 }""");
        Assert.NotNull(pointer);
        Assert.Equal("0.27.0", pointer.LabelframeVersion);
    }

    [Theory]
    [InlineData("https://github.com/marci-labs/LabelFrame/releases/latest/download/install-manifest.json",
        "https://github.com/marci-labs/LabelFrame/releases/latest/download/latest.json")] // 稳定通道（GitHub latest 语义）
    [InlineData("C:\\offline\\install-manifest.json", "C:\\offline\\latest.json")] // 本地路径（离线包内嵌）
    [InlineData("install-manifest.json", "latest.json")] // 裸相对路径
    [InlineData("https://github.com/marci-labs/LabelFrame/releases/download/v0.26.0/install-manifest.json",
        null)]  // 特定版本 Release URL：用户显式选择的版本清单，不推导
    [InlineData("https://example.invalid/other.json", null)] // 其他形态
    [InlineData("", null)]
    public void Derive_source_follows_consumption_rules(string source, string? expected)
    {
        Assert.Equal(expected, LatestPointer.DeriveSource(source));
    }

    [Fact]
    public void Freshness_hint_only_when_manifest_is_older()
    {
        var manifest = new InstallManifest(1, "0.26.0", "2026-09-14T00:00:00Z", []);
        var latest = new LatestPointer("0.27.0", "u");

        Assert.Contains("不是最新", UpgradePresentation.DescribeFreshness(manifest, latest));

        Assert.Null(UpgradePresentation.DescribeFreshness(manifest with { LabelframeVersion = "0.27.0" }, latest)); // 同版
        Assert.Null(UpgradePresentation.DescribeFreshness(manifest, null)); // 无指针
    }

    [Fact]
    public void Summarize_presentation_covers_three_states()
    {
        ComponentUpgradeEntry Entry(string id, string target, string? installed, ComponentUpgradeAction action) =>
            new(id, target, installed, action);

        // 全新安装
        Assert.Contains("全新安装", UpgradePresentation.Summarize([Entry("server-msi", "0.27.0", null, ComponentUpgradeAction.Install)]));

        // 可升级清单（组件、现版本 → 新版本）
        var summary = UpgradePresentation.Summarize([
            Entry("server-msi", "0.27.0", "0.26.0", ComponentUpgradeAction.Upgrade),
            Entry("client-msi", "0.27.0", "0.26.0", ComponentUpgradeAction.Upgrade),
        ]);
        Assert.Contains("可用更新", summary);
        Assert.Contains("服务端 0.26.0 → 0.27.0", summary);
        Assert.Contains("打印客户端 0.26.0 → 0.27.0", summary);

        // 已是最新
        var upToDate = UpgradePresentation.Summarize([Entry("client-msi", "0.27.0", "0.27.0", ComponentUpgradeAction.UpToDate)]);
        Assert.Contains("已是最新", upToDate);
        Assert.Contains("打印客户端 0.27.0", upToDate);
    }

    [Fact]
    public void Confirm_banner_prefers_upgrade_then_up_to_date()
    {
        ComponentUpgradeEntry Entry(string id, string target, string? installed, ComponentUpgradeAction action) =>
            new(id, target, installed, action);

        Assert.Contains("执行升级", UpgradePresentation.ConfirmBanner(
            [Entry("client-msi", "0.27.0", "0.26.0", ComponentUpgradeAction.Upgrade)]));
        Assert.Contains("已是最新", UpgradePresentation.ConfirmBanner(
            [Entry("client-msi", "0.27.0", "0.27.0", ComponentUpgradeAction.UpToDate)]));
        Assert.Null(UpgradePresentation.ConfirmBanner(
            [Entry("client-msi", "0.27.0", null, ComponentUpgradeAction.Install)])); // 全新安装：维持既有「确认前只读」横幅
    }
}
