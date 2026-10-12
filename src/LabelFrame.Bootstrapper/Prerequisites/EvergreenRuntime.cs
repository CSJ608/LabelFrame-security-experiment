namespace LabelFrame.Bootstrapper.Prerequisites;

using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.OfflineLayout;

/// <summary>evergreen 载荷源解析结果。</summary>
/// <param name="LocalPath">本地文件源（离线布局目录在位文件；null = 无本地源）。</param>
/// <param name="Urls">远程 URL 序列（清单 urls 顺序；空 = 无远程源）。</param>
public sealed record EvergreenSource(string? LocalPath, IReadOnlyList<string> Urls)
{
    /// <summary>是否为「无任何源」形态（调用方兜底缺省直链）。</summary>
    public bool IsEmpty => LocalPath is null && Urls.Count == 0;
}

/// <summary>
/// evergreen runtime（恒变直链，无可钉哈希）获取编排：源解析（本地布局文件优先 → urls 逐源）与
/// 「下载 / 取本地 → Authenticode 发布者验签」的通用流程；由 PayloadTool 在包执行期（引擎提权上下文）消费。
/// </summary>
public static class EvergreenRuntime
{
    /// <summary>WebView2 的 manifest 组件 id。</summary>
    public const string WebView2ComponentId = "runtime-webview2";

    /// <summary>WebView2 Evergreen 官方直链（无版本 fwlink，内容随微软轮换——验签策略的目标载荷）。</summary>
    public const string WebView2DefaultDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>WebView2 Standalone 安装器静默安装参数（微软官方口径）。</summary>
    public const string WebView2InstallArguments = "/silent /install";

    /// <summary>源串内多源分隔符（BA 侧把清单 urls 拼进单个 Burn 字符串变量）。</summary>
    public const char SourceListSeparator = ';';

    /// <summary>
    /// 解析源串：<c>[WebView2Source]</c> 变量形态——本地路径（非 http(s) 单值）或 <c>;</c> 分隔的 URL 序列；
    /// 空白 = 无源（调用方走 <see cref="WebView2DefaultDownloadUrl"/> 兜底）。
    /// </summary>
    public static EvergreenSource ResolveSource(string? sourceValue)
    {
        if (sourceValue is null || sourceValue.Trim().Length == 0)
        {
            return new EvergreenSource(null, []);
        }

        var trimmed = sourceValue.Trim();
        if (!IsHttpUrl(trimmed) && trimmed.IndexOf(SourceListSeparator) < 0)
        {
            // 单一非 URL 值 = 本地文件路径（布局目录在位文件）
            return new EvergreenSource(trimmed, []);
        }

        var urls = new List<string>();
        string? localPath = null;
        foreach (var token in trimmed.Split(SourceListSeparator))
        {
            var url = token.Trim();
            if (url.Length == 0)
            {
                continue;
            }

            if (IsHttpUrl(url))
            {
                urls.Add(url);
            }
            else if (localPath is null)
            {
                localPath = url; // 首个非 URL 项按本地路径处理（BA 侧约定：本地优先，位于串首）
            }
        }

        return new EvergreenSource(localPath, urls);
    }

    /// <summary>
    /// BA 侧 <c>[WebView2Source]</c> 变量值（纯函数）：布局目录本地文件在位 → 本地路径；否则清单 urls 顺序拼接；
    /// 清单缺 evergreen 条目 → 空串（工具按官方 fwlink 兜底）。
    /// </summary>
    /// <param name="manifest">当轮加载的安装清单（null 视为缺条目）。</param>
    /// <param name="localSourceDirectory">布局目录（清单来源为本地路径时非空；null = 无布局）。</param>
    /// <param name="fileExists">文件在位探测（测试注入；缺省 File.Exists）。</param>
    public static string BuildWebView2SourceValue(
        InstallManifest? manifest,
        string? localSourceDirectory,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var component = manifest?.Components.FirstOrDefault(item =>
            string.Equals(item.Id, WebView2ComponentId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(localSourceDirectory) && component is not null)
        {
            var fileName = OfflineLayoutNaming.TryDeriveFileName(component);
            if (fileName is not null)
            {
                var candidate = Path.Combine(localSourceDirectory, fileName);
                if (fileExists(candidate))
                {
                    return candidate;  // 布局本地文件优先（离线首装零外网）
                }
            }
        }

        return component is null ? string.Empty : string.Join(SourceListSeparator.ToString(), component.Urls);
    }

    private static bool IsHttpUrl(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

/// <summary>evergreen 载荷获取结果。</summary>
/// <param name="Outcome">结果类别。</param>
/// <param name="VerifiedPath">验签通过的安装器路径（成功时非空：本地源原路径或下载临时文件）。</param>
/// <param name="SignerSubject">验签通过的签名者主体（日志留痕）。</param>
/// <param name="FromLocalSource">验签通过源是否为本地文件（true = 未联网）。</param>
/// <param name="AnySignatureRejected">任一源曾发生「验签拒绝」（签名无效或发布者不符——区别于下载 / 源不可达失败）。</param>
/// <param name="Failures">逐源失败原因（全源耗尽时的诊断材料）。</param>
public sealed record EvergreenAcquireResult(
    EvergreenAcquireOutcome Outcome,
    string? VerifiedPath,
    string? SignerSubject,
    bool FromLocalSource,
    bool AnySignatureRejected,
    IReadOnlyList<string> Failures);

/// <summary>evergreen 载荷获取结果类别。</summary>
public enum EvergreenAcquireOutcome
{
    /// <summary>本地文件源验签通过（零联网）。</summary>
    VerifiedLocal,

    /// <summary>下载副本验签通过。</summary>
    VerifiedDownloaded,

    /// <summary>全部源耗尽（fail-closed，不装不明文件）。</summary>
    Failed,
}

/// <summary>
/// evergreen 载荷获取器：按源序（本地优先 → urls）获取并做 Authenticode 发布者验签
/// 任一源验签通过即返回；验签失败 / 下载失败换下一源，全源耗尽 fail-closed。
/// 下载与文件探测以委托注入（测试锚定）；验签器注入（测试用假验签器）。
/// </summary>
public sealed class EvergreenPayloadAcquirer
{
    private readonly IAuthenticodeVerifier _verifier;
    private readonly string _expectedPublisher;
    private readonly Func<string, string, bool> _tryDownload;
    private readonly Func<string, bool> _fileExists;

    /// <param name="verifier">Authenticode 验签器。</param>
    /// <param name="expectedPublisher">期望发布者 CN（WebView2 = <see cref="AuthenticodePublisherPolicy.MicrosoftCorporation"/>）。</param>
    /// <param name="tryDownload">下载委托（url, 目标路径）→ 是否成功；缺省 HttpClient 流式下载。</param>
    /// <param name="fileExists">文件在位探测（缺省 File.Exists）。</param>
    public EvergreenPayloadAcquirer(
        IAuthenticodeVerifier verifier,
        string expectedPublisher,
        Func<string, string, bool>? tryDownload = null,
        Func<string, bool>? fileExists = null)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _expectedPublisher = expectedPublisher ?? throw new ArgumentNullException(nameof(expectedPublisher));
        _tryDownload = tryDownload ?? TryDownloadDefault;
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>获取并验签（目标路径仅用于下载落盘；本地源直接原路径验签）。</summary>
    public EvergreenAcquireResult Acquire(EvergreenSource source, string downloadPath)
    {
        var failures = new List<string>();
        var signatureRejected = false;

        if (source.LocalPath is not null)
        {
            if (_fileExists(source.LocalPath))
            {
                var verified = VerifyForPublisher(source.LocalPath, failures, ref signatureRejected);
                if (verified is not null)
                {
                    return new EvergreenAcquireResult(
                        EvergreenAcquireOutcome.VerifiedLocal, verified.Path, verified.SignerSubject, true, signatureRejected, failures);
                }
            }
            else
            {
                failures.Add($"本地源文件不在位：{source.LocalPath}");
            }
        }

        foreach (var url in source.Urls)
        {
            if (!_tryDownload(url, downloadPath))
            {
                failures.Add($"下载失败：{url}");
                continue;
            }

            var downloaded = VerifyForPublisher(downloadPath, failures, ref signatureRejected);
            if (downloaded is not null)
            {
                return new EvergreenAcquireResult(
                    EvergreenAcquireOutcome.VerifiedDownloaded, downloaded.Path, downloaded.SignerSubject, false, signatureRejected, failures);
            }
        }

        return new EvergreenAcquireResult(EvergreenAcquireOutcome.Failed, null, null, false, signatureRejected, failures);
    }

    /// <summary>发布者验签通过的单文件（路径 + 签名者主体）。</summary>
    private sealed record VerifiedFile(string Path, string SignerSubject);

    /// <summary>发布者验签（签名链完整 + 签名者 CN 期望值双条件）；拒绝时记入失败清单并返回 null。</summary>
    private VerifiedFile? VerifyForPublisher(
        string path, List<string> failures, ref bool signatureRejected)
    {
        var result = _verifier.Verify(path);
        if (!result.SignatureValid)
        {
            signatureRejected = true;
            failures.Add($"验签拒绝（{path}）：{result.FailureReason ?? "签名无效"}");
            return null;
        }

        if (!AuthenticodePublisherPolicy.IsAllowedPublisher(result.SignerSubject, _expectedPublisher))
        {
            signatureRejected = true;
            failures.Add($"发布者不符（{path}）：期望 CN={_expectedPublisher}，实际主体 = {result.SignerSubject}");
            return null;
        }

        return new VerifiedFile(path, result.SignerSubject!);
    }

    /// <summary>缺省下载（HttpClient 流式；失败返回 false 由调用方换源——每次调用新建客户端，避免跨源状态）。</summary>
    private static bool TryDownloadDefault(string url, string destinationPath)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            using var response = client
                .GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead)
                .GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            using var sourceStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var targetStream = File.Create(destinationPath);
            var buffer = new byte[81920];
            int read;
            while ((read = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                targetStream.Write(buffer, 0, read);
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
