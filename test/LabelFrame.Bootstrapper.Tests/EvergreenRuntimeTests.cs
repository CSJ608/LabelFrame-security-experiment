using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Prerequisites;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>
/// evergreen 载荷获取编排测试：源解析（[WebView2Source] 变量形态）、BA 侧源变量构造
/// （布局本地文件优先 → urls）、获取器编排（本地优先 / 逐源回退 / 发布者验签 fail-closed）。
/// </summary>
public sealed class EvergreenRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lf-evergreen-tests-" + Guid.NewGuid().ToString("N"));

    public EvergreenRuntimeTests()
    {
        Directory.CreateDirectory(_root); // 下载目标路径的父目录（获取器测试）
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    // ---- 源解析（EvergreenRuntime.ResolveSource） ----

    [Fact]
    public void Empty_source_resolves_to_no_source()
    {
        foreach (var value in new string?[] { null, "", "   " })
        {
            var source = EvergreenRuntime.ResolveSource(value);
            Assert.True(source.IsEmpty);
            Assert.Null(source.LocalPath);
            Assert.Empty(source.Urls);
        }
    }

    [Fact]
    public void Single_non_url_value_resolves_to_local_path()
    {
        var source = EvergreenRuntime.ResolveSource(@"D:\layout\MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe");
        Assert.Equal(@"D:\layout\MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe", source.LocalPath);
        Assert.Empty(source.Urls);
    }

    [Fact]
    public void Url_list_resolves_in_order_and_skips_empty_tokens()
    {
        var source = EvergreenRuntime.ResolveSource("https://a.example/w2.exe;https://b.example/w2.exe;;https://c.example/w2.exe");
        Assert.Null(source.LocalPath);
        Assert.Equal(
            ["https://a.example/w2.exe", "https://b.example/w2.exe", "https://c.example/w2.exe"],
            source.Urls);
    }

    [Fact]
    public void Local_path_first_token_wins_over_urls_in_mixed_source()
    {
        var source = EvergreenRuntime.ResolveSource(@"C:\layout\w2.exe;https://mirror.example/w2.exe");
        Assert.Equal(@"C:\layout\w2.exe", source.LocalPath);
        Assert.Equal(["https://mirror.example/w2.exe"], source.Urls);
    }

    // ---- BA 侧源变量构造（EvergreenRuntime.BuildWebView2SourceValue） ----

    [Fact]
    public void Layout_file_present_takes_priority_over_urls()
    {
        var manifest = ParseManifest("""
        {
          "schemaVersion": 1, "labelframeVersion": "0.29.0", "generatedAt": "2026-09-20T00:00:00Z",
          "components": [
            { "id": "runtime-webview2", "type": "runtime", "version": "evergreen", "dependsOn": [],
              "urls": ["https://go.example/fwlink/p/?LinkId=2124703"], "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sizeBytes": 1,
              "silentArgs": "/silent /install", "topologies": ["client"], "notes": "" }
          ]
        }
        """);
        var layoutFile = Path.Combine(_root, "MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe");
        File.WriteAllText(layoutFile, "signed-copy");

        var value = EvergreenRuntime.BuildWebView2SourceValue(manifest, _root);

        Assert.Equal(layoutFile, value); // 布局本地文件优先（离线首装零外网）
    }

    [Fact]
    public void Layout_file_absent_falls_back_to_manifest_urls_in_order()
    {
        var manifest = ParseManifest("""
        {
          "schemaVersion": 1, "labelframeVersion": "0.29.0", "generatedAt": "2026-09-20T00:00:00Z",
          "components": [
            { "id": "runtime-webview2", "type": "runtime", "version": "evergreen", "dependsOn": [],
              "urls": ["https://go.example/fwlink/p/?LinkId=2124703", "https://mirror.example/w2.exe"],
              "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sizeBytes": 1, "silentArgs": "/silent /install", "topologies": ["client"], "notes": "" }
          ]
        }
        """);

        var value = EvergreenRuntime.BuildWebView2SourceValue(manifest, _root); // 目录在但文件不在位
        Assert.Equal("https://go.example/fwlink/p/?LinkId=2124703;https://mirror.example/w2.exe", value);

        // 无布局目录 → 同样纯 urls（布局文件优先仅在目录在位时生效）
        Assert.Equal("https://go.example/fwlink/p/?LinkId=2124703;https://mirror.example/w2.exe", EvergreenRuntime.BuildWebView2SourceValue(manifest, null));
    }

    [Fact]
    public void Manifest_without_evergreen_entry_yields_empty_value()
    {
        var manifest = ParseManifest("""
        {
          "schemaVersion": 1, "labelframeVersion": "0.29.0", "generatedAt": "2026-09-20T00:00:00Z",
          "components": [
            { "id": "server-msi", "type": "msi", "version": "0.29.0", "dependsOn": [],
              "urls": ["https://main.example/LabelFrame-Server-0.29.0.msi"], "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sizeBytes": 1,
              "silentArgs": "", "topologies": ["standalone"], "notes": "" }
          ]
        }
        """);

        // 空 = 工具按官方 fwlink 兜底（fwlink 是稳定公开常量）
        Assert.Equal(string.Empty, EvergreenRuntime.BuildWebView2SourceValue(manifest, null));
        Assert.Equal(string.Empty, EvergreenRuntime.BuildWebView2SourceValue(null, null));
    }

    // ---- 获取编排（EvergreenPayloadAcquirer） ----

    private static AuthenticodeVerificationResult MicrosoftSigned() => new(
        true, "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", 0, null);

    private static AuthenticodeVerificationResult Tampered() =>
        AuthenticodeVerificationResult.Invalid(unchecked((int)0x80091007), "文件哈希与签名不符（文件可能被改动）。");

    [Fact]
    public void Local_file_with_valid_signature_is_used_without_download()
    {
        var local = Path.Combine(_root, "w2.exe");
        Directory.CreateDirectory(_root);
        File.WriteAllText(local, "signed");
        var downloads = new List<string>();
        var acquirer = new EvergreenPayloadAcquirer(
            new FakeVerifier(_ => MicrosoftSigned()),
            AuthenticodePublisherPolicy.MicrosoftCorporation,
            tryDownload: (url, path) =>
            {
                downloads.Add(url);
                return false;
            },
            fileExists: _ => true);

        var result = acquirer.Acquire(EvergreenRuntime.ResolveSource(local), Path.Combine(_root, "download.exe"));

        Assert.Equal(EvergreenAcquireOutcome.VerifiedLocal, result.Outcome);
        Assert.Equal(local, result.VerifiedPath);
        Assert.True(result.FromLocalSource);
        Assert.Empty(downloads); // 本地源验签通过 → 零联网
    }

    [Fact]
    public void Local_verification_failure_falls_back_to_urls()
    {
        // 布局文件被篡改 → 验签拒绝 → 按序落回 urls（与 换源语义一致）
        var local = Path.Combine(_root, "w2.exe");
        var url = "https://mirror.example/w2.exe";
        var verifyCalls = new List<string>();
        var acquirer = new EvergreenPayloadAcquirer(
            new FakeVerifier(path =>
            {
                verifyCalls.Add(path);
                return path == local ? Tampered() : MicrosoftSigned();
            }),
            AuthenticodePublisherPolicy.MicrosoftCorporation,
            tryDownload: (u, path) =>
            {
                File.WriteAllText(path, "downloaded");
                return true;
            },
            fileExists: _ => true);

        var result = acquirer.Acquire(EvergreenRuntime.ResolveSource(local + ";" + url), Path.Combine(_root, "download.exe"));

        Assert.Equal(EvergreenAcquireOutcome.VerifiedDownloaded, result.Outcome);
        Assert.True(result.AnySignatureRejected); // 本地验签拒绝留痕
        Assert.Equal(2, verifyCalls.Count);
        Assert.Contains(local, result.Failures.Single());
    }

    [Fact]
    public void Url_verification_failure_advances_to_next_source_until_success()
    {
        var urls = "https://bad.example/w2.exe;https://good.example/w2.exe";
        var attempts = new List<string>();
        var acquirer = new EvergreenPayloadAcquirer(
            new FakeVerifier(path =>
            {
                // 验签发生在下载之后（attempts 已含当前源）：bad 源内容拒绝、good 源内容通过
                var currentUrl = attempts[^1];
                return currentUrl.Contains("bad.example", StringComparison.Ordinal) ? Tampered() : MicrosoftSigned();
            }),
            AuthenticodePublisherPolicy.MicrosoftCorporation,
            tryDownload: (url, path) =>
            {
                attempts.Add(url);
                File.WriteAllText(path, "downloaded");
                return true;
            },
            fileExists: _ => false);

        var result = acquirer.Acquire(EvergreenRuntime.ResolveSource(urls), Path.Combine(_root, "download.exe"));

        Assert.Equal(EvergreenAcquireOutcome.VerifiedDownloaded, result.Outcome);
        Assert.Equal(["https://bad.example/w2.exe", "https://good.example/w2.exe"], attempts);
        Assert.True(result.AnySignatureRejected);
    }

    [Fact]
    public void Valid_signature_from_wrong_publisher_is_rejected()
    {
        var acquirer = new EvergreenPayloadAcquirer(
            new FakeVerifier(_ => new AuthenticodeVerificationResult(true, "CN=Microsoft Windows, O=Microsoft Corporation, C=US", 0, null)),
            AuthenticodePublisherPolicy.MicrosoftCorporation,
            tryDownload: (_, path) =>
            {
                File.WriteAllText(path, "downloaded");
                return true;
            },
            fileExists: _ => false);

        var result = acquirer.Acquire(EvergreenRuntime.ResolveSource("https://a.example/w2.exe"), Path.Combine(_root, "download.exe"));

        Assert.Equal(EvergreenAcquireOutcome.Failed, result.Outcome);
        Assert.True(result.AnySignatureRejected);
        Assert.Contains("发布者不符", result.Failures.Single());
    }

    [Fact]
    public void All_sources_exhausted_reports_download_failures_without_signature_flag()
    {
        var acquirer = new EvergreenPayloadAcquirer(
            new FakeVerifier(_ => MicrosoftSigned()),
            AuthenticodePublisherPolicy.MicrosoftCorporation,
            tryDownload: (_, _) => false,
            fileExists: _ => false);

        var result = acquirer.Acquire(EvergreenRuntime.ResolveSource("https://a.example/w2.exe;https://b.example/w2.exe"), Path.Combine(_root, "download.exe"));

        Assert.Equal(EvergreenAcquireOutcome.Failed, result.Outcome);
        Assert.False(result.AnySignatureRejected); // 全为下载失败（区分 41 与 40 退出码）
        Assert.Equal(2, result.Failures.Count);
    }

    private static InstallManifest ParseManifest(string json) => InstallManifest.Parse(json);

    private sealed class FakeVerifier : IAuthenticodeVerifier
    {
        private readonly Func<string, AuthenticodeVerificationResult> _behavior;

        public FakeVerifier(Func<string, AuthenticodeVerificationResult> behavior) => _behavior = behavior;

        public AuthenticodeVerificationResult Verify(string filePath) => _behavior(filePath);
    }
}
