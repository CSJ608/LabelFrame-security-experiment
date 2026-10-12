using System.Text.Json;

namespace LabelFrame.Core.Transport.Plugins.Package;

/// <summary>
/// 插件包 manifest（zip 根 manifest.json）：
/// pluginId / name / version 必填；description / author / minHostVersion 可选
/// （minHostVersion 仅展示、暂不校验）；platforms 可选——字符串数组小写平台 id（windows / android），
/// 无该字段的既有包按 Windows 端解释（<see cref="SupportsPlatform"/>）。
/// </summary>
public sealed record PluginPackageManifest(
    string PluginId,
    string Name,
    string Version,
    string? Description = null,
    string? Author = null,
    string? MinHostVersion = null,
    IReadOnlyList<string>? Platforms = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>解析 manifest JSON；必填字段缺失 / 类型错误抛 <see cref="PluginPackageException"/>（中文消息）。</summary>
    public static PluginPackageManifest Parse(string json)
    {
        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // 不透出英文解析详情；行 / 列位置足以定位，原始异常保留在 InnerException 供日志排障
            var position = ex.LineNumber is >= 0
                ? $"（第 {ex.LineNumber + 1} 行第 {ex.BytePositionInLine + 1} 列附近）"
                : string.Empty;
            throw new PluginPackageException($"manifest.json 不是有效的 JSON{position}，请检查格式后重新导出插件包。", ex);
        }

        if (dto is null)
        {
            throw new PluginPackageException("manifest.json 内容为空。");
        }

        if (string.IsNullOrWhiteSpace(dto.PluginId))
        {
            throw new PluginPackageException("manifest.json 缺少必填字段 pluginId。");
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new PluginPackageException("manifest.json 缺少必填字段 name。");
        }

        if (string.IsNullOrWhiteSpace(dto.Version))
        {
            throw new PluginPackageException("manifest.json 缺少必填字段 version。");
        }

        IReadOnlyList<string>? platforms = null;
        if (dto.Platforms is not null)
        {
            // 空数组语义不明（缺省不标记 = Windows 兼容解释），显式空数组视为非法拒绝
            if (dto.Platforms.Count == 0)
            {
                throw new PluginPackageException("manifest.json 的 platforms 不能是空数组（不需要平台标记时请省略该字段）。");
            }

            var normalized = new List<string>(dto.Platforms.Count);
            foreach (var platform in dto.Platforms)
            {
                var value = PluginPlatforms.Normalize(platform);
                if (value is null)
                {
                    throw new PluginPackageException("manifest.json 的 platforms 含空白平台 id，请填写 windows 或 android。");
                }

                normalized.Add(value);
            }

            platforms = normalized;
        }

        return new PluginPackageManifest(
            dto.PluginId.Trim(),
            dto.Name.Trim(),
            dto.Version.Trim(),
            dto.Description,
            dto.Author,
            dto.MinHostVersion,
            platforms);
    }

    /// <summary>
    /// 包是否面向指定宿主平台（忽略大小写）：无 platforms 字段的既有包按 Windows 端解释（向后兼容）；
    /// 有声明则按集合包含判断（未列入的平台 = 不支持，安装端平台门据此拒绝）。
    /// </summary>
    public bool SupportsPlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            throw new ArgumentException("平台 id 不能为空。", nameof(platform));
        }

        return Platforms is null
            ? string.Equals(PluginPlatforms.Normalize(platform), PluginPlatforms.Windows, StringComparison.Ordinal)
            : Platforms.Contains(PluginPlatforms.Normalize(platform) ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>平台声明的用户可读摘要（中文顿号连接；未标记 = Windows 兼容口径，显示 windows）。</summary>
    public string PlatformsDisplay()
        => Platforms is null || Platforms.Count == 0 ? PluginPlatforms.Windows : string.Join("、", Platforms);

    private sealed record ManifestDto(
        string? PluginId,
        string? Name,
        string? Version,
        string? Description,
        string? Author,
        string? MinHostVersion,
        IReadOnlyList<string>? Platforms = null);
}