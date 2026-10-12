using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.OfflineLayout;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>离线布局目录命名约定测试——生成与消费共用的单点契约锚定。</summary>
public sealed class OfflineLayoutNamingTests
{
    private static ManifestComponent Component(string id, params string[] urls) =>
        new(id, "msi", "0.27.0", [], urls, "3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c", 100, "", ["standalone"], null);

    [Fact]
    public void Release_url_last_segment_is_used_as_file_name()
    {
        // 本仓全部 Release 附件直链形态：末段即官方产物文件名
        var name = OfflineLayoutNaming.TryDeriveFileName(
            Component("server-msi", "https://github.com/marci-labs/LabelFrame/releases/download/v0.27.0/LabelFrame-Server-0.27.0.msi"));

        Assert.Equal("LabelFrame-Server-0.27.0.msi", name);
    }

    [Fact]
    public void Query_only_url_falls_back_to_fixed_name_table()
    {
        // WebView2 Evergreen fwlink：URL 路径段（/p/）无可辨识文件名 → 固定名兜底（与 Bundle 链包 Name 一致）
        var name = OfflineLayoutNaming.TryDeriveFileName(
            Component("runtime-webview2", "https://go.microsoft.com/fwlink/p/?LinkId=2124703"));

        Assert.Equal("MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe", name);
    }

    [Theory]
    [InlineData("https://mirror.example/p/")]           // 末段无扩展名
    [InlineData("https://mirror.example/")]             // 无末段
    [InlineData("ftp:  // mirror.example/some.msi")] // 非 http(s)
    [InlineData("not-a-url")]                           // 非法 URL
    public void Non_derivable_urls_without_fixed_entry_return_null(string url)
    {
        Assert.Null(OfflineLayoutNaming.TryDeriveFileName(Component("some-component", url)));
    }

    [Fact]
    public void Empty_urls_without_fixed_entry_return_null()
    {
        Assert.Null(OfflineLayoutNaming.TryDeriveFileName(Component("some-component")));
    }

    [Fact]
    public void Encoded_segment_is_unescaped()
    {
        var name = OfflineLayoutNaming.TryDeriveFileName(Component("webui", "https://main.example/files/labelframe%2Dserver%2Dwebui-0.27.0.zip"));

        Assert.Equal("labelframe-server-webui-0.27.0.zip", name);
    }

    [Fact]
    public void Path_traversal_like_segment_is_rejected()
    {
        // 防御：恶意清单构造路径穿越形态的「文件名」——拒绝派生（退化为无本地源 / 生成失败，fail-closed）
        Assert.Null(OfflineLayoutNaming.TryDeriveFileName(Component("evil", "https://main.example/..%5C..%5Cevil.msi")));
    }

    [Fact]
    public void Fixed_table_entries_are_stable_contract()
    {
        // 兜底表为跨形态契约（脚本 make-offline-layout.ps1 与核心库共用）——变更即契约变更
        var entry = Assert.Single(OfflineLayoutNaming.FixedFileNames);
        Assert.Equal("runtime-webview2", entry.Key);
        Assert.Equal("MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe", entry.Value);
    }
}
