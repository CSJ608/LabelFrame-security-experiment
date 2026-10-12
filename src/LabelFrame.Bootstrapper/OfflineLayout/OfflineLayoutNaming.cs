namespace LabelFrame.Bootstrapper.OfflineLayout;

using LabelFrame.Bootstrapper.Manifest;

/// <summary>离线布局目录命名约定（DESIGN）——生成（脚本与引导 <c>--layout</c>）与消费（本地源解析）共用。</summary>
/// <remarks>
/// <para>
/// 目录固定文件：清单 = <see cref="ManifestFileName"/>（官方原样字节）、指针 = <see cref="LatestFileName"/>（可选）；
/// 组件文件名 = <b>urls[0] URL 路径末段</b>（须含扩展名——本仓全部 Release 附件直链与 runtime 钉定直链均满足）；
/// 查询型直链（URL 路径无可辨识文件名，如 WebView2 fwlink）按 <see cref="FixedFileNames"/> 固定名兜底；
/// 两者皆不可得返回 null（生成侧 fail-closed 拒绝，消费侧该组件退化为纯 urls）。
/// </para>
/// <para>
/// 派生结果做文件名合法性防御（拒绝路径分隔符 / 非法字符，防恶意清单构造路径穿越）；
/// 兜底表按组件 id 精确匹配，后续新组件（URL 无文件名形态）按同构扩表。
/// </para>
/// </remarks>
public static class OfflineLayoutNaming
{
    /// <summary>布局目录内安装清单文件名（官方 manifest 原样落盘）。</summary>
    public const string ManifestFileName = "install-manifest.json";

    /// <summary>布局目录内最新版本指针文件名（可选，官方 latest.json 原样落盘）。</summary>
    public const string LatestFileName = "latest.json";

    /// <summary>查询型直链（URL 路径无可辨识文件名）的固定名兜底表（组件 id → 布局文件名）。</summary>
    public static readonly IReadOnlyDictionary<string, string> FixedFileNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // WebView2 Evergreen fwlink（https://go.microsoft.com/fwlink/p/?LinkId=2124703）——与 Bundle 链包 Name 一致
            ["runtime-webview2"] = "MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe",
        };

    /// <summary>推导组件在布局目录中的文件名（urls[0] 路径末段 → 固定名兜底 → null 不可推导）。</summary>
    public static string? TryDeriveFileName(ManifestComponent component)
    {
        var fromUrl = TryFileNameFromUrl(component.Urls.Count > 0 ? component.Urls[0] : null);
        if (fromUrl is not null)
        {
            return fromUrl;
        }

        return FixedFileNames.TryGetValue(component.Id, out var fixedName) ? fixedName : null;
    }

    /// <summary>URL 路径末段派生文件名：须为绝对 http(s) URL、末段含扩展名（形如「名.扩展」）且为合法文件名。</summary>
    private static string? TryFileNameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var lastSegment = uri.Segments.Length > 0 ? uri.Segments[uri.Segments.Length - 1].TrimEnd('/') : string.Empty;
        if (string.IsNullOrEmpty(lastSegment) || lastSegment.IndexOf('.') < 1)
        {
            return null; // 无扩展名（如 fwlink 路径段 /p/）——非可辨识文件名
        }

        var decoded = Uri.UnescapeDataString(lastSegment);
        return decoded.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? null : decoded;
    }
}
