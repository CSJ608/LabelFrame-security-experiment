using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelFrame.Bootstrapper.Manifest;

/// <summary>最新版本指针（<c>latest.json</c>）：最新版本号 + 该版 manifest URL——仅用于「清单新鲜度」提示，不参与组件级比较（组件版本权威 = manifest）。</summary>
public sealed record LatestPointer(string LabelframeVersion, string ManifestUrl)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>解析 latest.json（字段缺失 / 非法返回 null——调用方静默跳过，不阻断主流程）。</summary>
    public static LatestPointer? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var pointer = JsonSerializer.Deserialize<LatestPointer>(json, JsonOptions);
            return string.IsNullOrWhiteSpace(pointer?.LabelframeVersion) ? null : pointer;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>由清单来源推导 latest.json 地址（稳定通道 → 同通道；本地路径 → 同目录；特定版本 Release 等形态返回 null 不推导）。</summary>
    public static string? DeriveSource(string manifestSource)
    {
        const string ManifestFileName = "install-manifest.json";
        const string LatestFileName = "latest.json";
        const string StableChannelMarker = "/releases/latest/download/";

        if (string.IsNullOrWhiteSpace(manifestSource))
        {
            return null;
        }

        if (Uri.TryCreate(manifestSource, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return Path.Combine(Path.GetDirectoryName(uri.LocalPath) ?? string.Empty, LatestFileName);
        }

        if (manifestSource.Contains(StableChannelMarker, StringComparison.OrdinalIgnoreCase)
            && manifestSource.EndsWith(ManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            return manifestSource.Remove(manifestSource.Length - ManifestFileName.Length) + LatestFileName;
        }

        if (!manifestSource.Contains("://", StringComparison.Ordinal))
        {
            // 裸本地路径（相对 / 绝对）
            return Path.Combine(Path.GetDirectoryName(manifestSource) ?? string.Empty, LatestFileName);
        }

        return null;  // 特定版本 Release URL 等：用户显式选择的版本清单，不推导
    }
}
