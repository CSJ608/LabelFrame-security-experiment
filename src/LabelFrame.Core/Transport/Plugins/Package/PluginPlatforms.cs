namespace LabelFrame.Core.Transport.Plugins.Package;

/// <summary>
/// 插件包平台标记：manifest 可选字段 <c>platforms</c> 的平台 id 常量与判定；
/// 无该字段的既有包按 Windows 端解释（存量包零迁移，向后兼容）。
/// 平台 id 用平台名而非 RID——加载按字节进 ALC，不依赖 RID 元数据。
/// </summary>
public static class PluginPlatforms
{
    /// <summary>Windows 平台 id（WinHost / Windows 客户端）。</summary>
    public const string Windows = "windows";

    /// <summary>Android 平台 id（AndroidHost / PDA）。</summary>
    public const string Android = "android";

    /// <summary>归一化平台 id（小写去空白）；空值返回 null。</summary>
    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
