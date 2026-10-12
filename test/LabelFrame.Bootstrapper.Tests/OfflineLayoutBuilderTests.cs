using System.Net;
using System.Security.Cryptography;
using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.OfflineLayout;
using LabelFrame.Bootstrapper.Prerequisites;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>
/// 离线布局目录生成器测试（/）：逐组件 urls 顺序回退 + sha256 fail-closed +
/// 幂等复用 + manifest / latest 原样落盘 + 引导 EXE 复制——与 scripts/make-offline-layout.ps1 同语义锚定。
/// </summary>
public sealed class OfflineLayoutBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lf-offline-layout-tests-" + Guid.NewGuid().ToString("N"));

    private sealed class RoutingHttpMessageHandler : HttpMessageHandler
    {
        public List<(string Method, string Url)> Requests { get; } = [];

        private readonly Dictionary<string, Func<byte[]>> _routes;

        public RoutingHttpMessageHandler(Dictionary<string, Func<byte[]>> routes) => _routes = routes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add((request.Method.Method, url));
            if (_routes.TryGetValue(url, out var produce))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(produce()),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) });
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static string Sha256Of(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>构造双组件清单（server-msi 两源 / runtime-webview2 查询直链——固定名兜底路径同时覆盖）。</summary>
    private static (InstallManifest Manifest, string Json, byte[] ServerBytes, byte[] WebView2Bytes) BuildManifest()
    {
        var serverBytes = "server-payload-0.27.0"u8.ToArray();
        var webView2Bytes = "webview2-bootstrapper"u8.ToArray();
        var json = $$"""
        {
          "schemaVersion": 1,
          "labelframeVersion": "0.27.0",
          "generatedAt": "2026-09-17T00:00:00Z",
          "components": [
            {
              "id": "server-msi", "type": "msi", "version": "0.27.0", "dependsOn": [],
              "urls": ["https://main.example/LabelFrame-Server-0.27.0.msi", "https://mirror.example/LabelFrame-Server-0.27.0.msi"],
              "sha256": "{{Sha256Of(serverBytes)}}", "sizeBytes": {{serverBytes.Length}}, "silentArgs": "",
              "topologies": ["standalone"], "notes": "服务端"
            },
            {
              "id": "runtime-webview2", "type": "runtime", "version": "evergreen", "dependsOn": [],
              "urls": ["https://go.example/fwlink/p/?LinkId=2124703"],
              "sha256": "{{Sha256Of(webView2Bytes)}}", "sizeBytes": {{webView2Bytes.Length}}, "silentArgs": "/silent /install",
              "topologies": ["standalone"], "notes": "WebView2"
            }
          ]
        }
        """;
        return (InstallManifest.Parse(json), json, serverBytes, webView2Bytes);
    }

    private string CreateBootstrapperSource()
    {
        var path = Path.Combine(_root, "source", "LabelFrame-Bootstrapper-0.27.0.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "bootstrapper-exe-bytes");
        return path;
    }

    [Fact]
    public async Task Full_build_downloads_components_verifies_hash_and_copies_manifest_latest_and_exe()
    {
        var (manifest, json, serverBytes, webView2Bytes) = BuildManifest();
        var bootstrapper = CreateBootstrapperSource();
        var target = Path.Combine(_root, "layout");
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => serverBytes,
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => webView2Bytes,
        });
        using var client = new HttpClient(http);

        var result = await OfflineLayoutBuilder.BuildAsync(
            manifest, json, "{\"labelframeVersion\":\"0.27.0\"}", target, bootstrapper, client, authenticodeVerifier: AcceptAllVerifier());

        Assert.Equal(2, result.ComponentCount);
        Assert.Equal(2, result.DownloadedCount);
        Assert.Equal(0, result.ReusedCount);
        Assert.Equal("LabelFrame-Bootstrapper-0.27.0.exe", result.BootstrapperFileName);
        Assert.Equal(serverBytes, await File.ReadAllBytesAsync(Path.Combine(target, "LabelFrame-Server-0.27.0.msi")));
        Assert.Equal(webView2Bytes, await File.ReadAllBytesAsync(Path.Combine(target, "MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe")));
        Assert.Equal(json, await File.ReadAllTextAsync(Path.Combine(target, OfflineLayoutNaming.ManifestFileName))); // 官方原样字节
        Assert.Equal("{\"labelframeVersion\":\"0.27.0\"}", await File.ReadAllTextAsync(Path.Combine(target, OfflineLayoutNaming.LatestFileName)));
        Assert.Equal("bootstrapper-exe-bytes", await File.ReadAllTextAsync(Path.Combine(target, "LabelFrame-Bootstrapper-0.27.0.exe")));
    }

    [Fact]
    public async Task Hash_mismatch_on_primary_url_falls_back_to_next_url_in_order()
    {
        // 主源内容被替换（哈希不符）→ 按序换镜像源取干净副本（生成期与安装期换源同语义）
        var (manifest, json, serverBytes, webView2Bytes) = BuildManifest();
        var target = Path.Combine(_root, "layout");
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => "tampered-bytes"u8.ToArray(),
            ["https://mirror.example/LabelFrame-Server-0.27.0.msi"] = () => serverBytes,
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => webView2Bytes,
        });

        using var client = new HttpClient(http);
        var result = await OfflineLayoutBuilder.BuildAsync(manifest, json, null, target, null, client, authenticodeVerifier: AcceptAllVerifier());

        Assert.Equal(2, result.DownloadedCount);
        Assert.Equal(serverBytes, await File.ReadAllBytesAsync(Path.Combine(target, "LabelFrame-Server-0.27.0.msi")));
        Assert.Equal(1, http.Requests.Count(request => request.Url.Contains("main.example", StringComparison.Ordinal))); // 主源尝试一次即哈希不符弃用
    }

    [Fact]
    public async Task All_urls_failing_closes_without_laying_down_component()
    {
        // fail-closed：全源失败（404 / 哈希不符）即异常退出，绝不落位未校验文件（生成侧口径）
        var (manifest, json, _, webView2Bytes) = BuildManifest();
        var target = Path.Combine(_root, "layout");
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => "tampered-bytes"u8.ToArray(),
            ["https://mirror.example/LabelFrame-Server-0.27.0.msi"] = () => "also-bad"u8.ToArray(),
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => webView2Bytes,
        });

        using var client = new HttpClient(http);
        var ex = await Assert.ThrowsAsync<OfflineLayoutException>(
            () => OfflineLayoutBuilder.BuildAsync(manifest, json, null, target, null, client, authenticodeVerifier: AcceptAllVerifier()));

        Assert.Contains("server-msi", ex.Message);
        Assert.False(File.Exists(Path.Combine(target, "LabelFrame-Server-0.27.0.msi")));
        Assert.False(File.Exists(Path.Combine(target, "LabelFrame-Server-0.27.0.msi.download"))); // 临时文件不残留
        Assert.False(File.Exists(Path.Combine(target, OfflineLayoutNaming.ManifestFileName))); // 未完成不落清单
    }

    [Fact]
    public async Task Existing_file_with_matching_hash_is_reused_without_http_request()
    {
        // 幂等重生成：已存在且哈希一致零下载（生成机重复生成 / 局部续传）
        var (manifest, json, serverBytes, webView2Bytes) = BuildManifest();
        var target = Path.Combine(_root, "layout");
        Directory.CreateDirectory(target);
        await File.WriteAllBytesAsync(Path.Combine(target, "LabelFrame-Server-0.27.0.msi"), serverBytes);
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => serverBytes,
            ["https://mirror.example/LabelFrame-Server-0.27.0.msi"] = () => serverBytes,
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => webView2Bytes,
        });

        using var client = new HttpClient(http);
        var result = await OfflineLayoutBuilder.BuildAsync(manifest, json, null, target, null, client, authenticodeVerifier: AcceptAllVerifier());

        Assert.Equal(1, result.DownloadedCount);
        Assert.Equal(1, result.ReusedCount);
        Assert.DoesNotContain(http.Requests, request => request.Url.Contains("LabelFrame-Server", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Existing_tampered_file_is_redownloaded()
    {
        // 布局残留被篡改（哈希不符）→ 重新下载干净副本（不静默沿用）
        var (manifest, json, serverBytes, webView2Bytes) = BuildManifest();
        var target = Path.Combine(_root, "layout");
        Directory.CreateDirectory(target);
        await File.WriteAllBytesAsync(Path.Combine(target, "LabelFrame-Server-0.27.0.msi"), "tampered-old"u8.ToArray());
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => serverBytes,
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => webView2Bytes,
        });

        using var client = new HttpClient(http);
        await OfflineLayoutBuilder.BuildAsync(manifest, json, null, target, null, client, authenticodeVerifier: AcceptAllVerifier());

        Assert.Equal(serverBytes, await File.ReadAllBytesAsync(Path.Combine(target, "LabelFrame-Server-0.27.0.msi")));
    }

    [Fact]
    public async Task Missing_bootstrapper_source_fails_closed()
    {
        var (manifest, json, _, webView2Bytes) = BuildManifest();
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => "server-payload-0.27.0"u8.ToArray(),
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => webView2Bytes,
        });

        using var client = new HttpClient(http);
        await Assert.ThrowsAsync<OfflineLayoutException>(
            () => OfflineLayoutBuilder.BuildAsync(manifest, json, null, Path.Combine(_root, "layout"), Path.Combine(_root, "missing.exe"), client, authenticodeVerifier: AcceptAllVerifier()));
    }

    [Fact]
    public async Task Progress_reports_component_lifecycle()
    {
        var (manifest, json, serverBytes, webView2Bytes) = BuildManifest();
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => serverBytes,
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => webView2Bytes,
        });
        var phases = new List<OfflineLayoutPhase>();
        var progress = new ProgressCollector(phases);

        using var client = new HttpClient(http);
        await OfflineLayoutBuilder.BuildAsync(manifest, json, null, Path.Combine(_root, "layout"), null, client, progress, authenticodeVerifier: AcceptAllVerifier());

        Assert.Contains(OfflineLayoutPhase.Downloading, phases);
        Assert.Contains(OfflineLayoutPhase.Verifying, phases);
        Assert.Contains(OfflineLayoutPhase.Finalizing, phases);
        Assert.Equal(OfflineLayoutPhase.Completed, phases[^1]);
    }

    [Fact]
    public async Task Evergreen_entry_verifies_by_publisher_instead_of_stale_hash_and_reuses_existing_file()
    {
        // evergreen 条目（version=evergreen）按微软 Authenticode 发布者验签——manifest sha256 已因微软轮换
        // 「过期」（与在位文件哈希不符）但签名有效 → 仍然复用零下载（rotation-immune 的生成期锚点）
        var (manifest, json, serverBytes, _) = BuildManifest();
        var target = Path.Combine(_root, "layout");
        Directory.CreateDirectory(target);
        var rotatedBytes = "webview2-bootstrapper-ROTATED-BY-MICROSOFT"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(target, "MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe"), rotatedBytes);
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => serverBytes,
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => "should-not-be-requested"u8.ToArray(),
        });

        using var client = new HttpClient(http);
        var result = await OfflineLayoutBuilder.BuildAsync(
            manifest, json, null, target, null, client, authenticodeVerifier: AcceptAllVerifier());

        Assert.Equal(1, result.DownloadedCount);
        Assert.Equal(1, result.ReusedCount); // webview2 复用（签名有效即接受，无视清单哈希漂移）
        Assert.DoesNotContain(http.Requests, request => request.Url.Contains("fwlink", StringComparison.Ordinal));
        Assert.Equal(rotatedBytes, await File.ReadAllBytesAsync(Path.Combine(target, "MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe")));
    }

    [Fact]
    public async Task Evergreen_entry_with_invalid_signature_on_all_sources_fails_closed()
    {
        // fail-closed：evergreen 源验签全部拒绝（签名无效 / 发布者不符）→ 绝不落位（不装不明文件）
        var (manifest, json, serverBytes, webView2Bytes) = BuildManifest();
        var target = Path.Combine(_root, "layout");
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => serverBytes,
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => webView2Bytes,
        });

        using var client = new HttpClient(http);
        var ex = await Assert.ThrowsAsync<OfflineLayoutException>(
            () => OfflineLayoutBuilder.BuildAsync(
                manifest, json, null, target, null, client,
                authenticodeVerifier: new FakeAuthenticodeVerifier(_ => AuthenticodeVerificationResult.Invalid(
                    unchecked((int)0x80091007), "文件哈希与签名不符（文件可能被改动）。"))));

        Assert.Contains("runtime-webview2", ex.Message);
        Assert.Contains("验签拒绝", ex.Message);
        Assert.False(File.Exists(Path.Combine(target, "MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe")));
    }

    [Fact]
    public async Task Evergreen_entry_with_wrong_publisher_is_rejected()
    {
        // 发布者双条件：签名有效但签名者非 Microsoft Corporation → 拒绝落位
        var (manifest, json, serverBytes, webView2Bytes) = BuildManifest();
        var target = Path.Combine(_root, "layout");
        var http = new RoutingHttpMessageHandler(new Dictionary<string, Func<byte[]>>
        {
            ["https://main.example/LabelFrame-Server-0.27.0.msi"] = () => serverBytes,
            ["https://go.example/fwlink/p/?LinkId=2124703"] = () => webView2Bytes,
        });

        using var client = new HttpClient(http);
        var ex = await Assert.ThrowsAsync<OfflineLayoutException>(
            () => OfflineLayoutBuilder.BuildAsync(
                manifest, json, null, target, null, client,
                authenticodeVerifier: new FakeAuthenticodeVerifier(_ =>
                    new AuthenticodeVerificationResult(true, "CN=Contoso Ltd, O=Contoso, C=US", 0, null))));

        Assert.Contains("发布者不符", ex.Message);
    }

    private static AuthenticodeVerificationResult MicrosoftSignedResult() => new(
        true, "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", 0, null);

    /// <summary>测试用「全部通过」验签器（evergreen 条目模拟微软签名有效；常规条目不进验签路径）。</summary>
    private static FakeAuthenticodeVerifier AcceptAllVerifier() => new(_ => MicrosoftSignedResult());

    private sealed class FakeAuthenticodeVerifier : IAuthenticodeVerifier
    {
        private readonly Func<string, AuthenticodeVerificationResult> _behavior;

        public FakeAuthenticodeVerifier(Func<string, AuthenticodeVerificationResult> behavior) => _behavior = behavior;

        public AuthenticodeVerificationResult Verify(string filePath) => _behavior(filePath);
    }

    private sealed class ProgressCollector(List<OfflineLayoutPhase> phases) : IProgress<OfflineLayoutProgress>
    {
        public void Report(OfflineLayoutProgress value) => phases.Add(value.Phase);
    }
}
