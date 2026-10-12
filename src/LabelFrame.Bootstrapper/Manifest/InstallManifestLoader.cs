namespace LabelFrame.Bootstrapper.Manifest;

using System.Net.Http;

/// <summary>安装清单加载器：来源支持本地路径（含离线全量包内嵌清单场景）或 URL（稳定通道 / 指定版本 Release）。</summary>
/// <remarks>
/// dry-run 契约锚点（DESIGN 实现要点、Issue）：清单获取是引导全程唯一的网络访问——
/// 只读 GET、不带任何写语义；本地路径则零网络访问。下载 / 安装能力归 / ，本轮不存在。
/// </remarks>
public static class InstallManifestLoader
{
    private static readonly HttpClient SharedClient = new();

    /// <summary>加载并解析清单。<paramref name="http"/> 可注入用于测试（记录请求以断言「仅一次 GET」）。</summary>
    public static async Task<InstallManifest> LoadAsync(string source, HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        var json = await LoadTextAsync(source, http, cancellationToken).ConfigureAwait(false);
        return InstallManifest.Parse(json);
    }

    /// <summary>加载原始文本（本地路径 = 文件读取；URL = 单次只读 GET）——latest.json 等轻量伴随文件复用同一只读口径。</summary>
    public static async Task<string> LoadTextAsync(string source, HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InstallManifestFormatException("来源为空：请填写本地文件路径或 URL。");
        }

        // 清单为小文件：本地路径同步读取即可（net48 腿无异步文件 API）；URL 走 HttpClient（net48 / net10 同一重载形态）
        return IsHttpUrl(source)
            ? await DownloadAsync(source, http ?? SharedClient, cancellationToken).ConfigureAwait(false)
            : File.ReadAllText(source);
    }

    private static async Task<string> DownloadAsync(string url, HttpClient http, CancellationToken cancellationToken)
    {
        // 只读 GET（无网络写入； 测试断言锚点）
        using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InstallManifestFormatException($"下载清单失败：HTTP {(int)response.StatusCode}（{url}）。");
        }

#if NET10_0_OR_GREATER
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        // net48 腿 ReadAsStringAsync 无 CancellationToken 重载（CA2016 同因豁免）
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
    }

    /// <summary>来源是否为 http（s） URL（否则按本地文件路径处理；本地清单 → 布局目录隐式优先源判定共用）。</summary>
    public static bool IsHttpUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
