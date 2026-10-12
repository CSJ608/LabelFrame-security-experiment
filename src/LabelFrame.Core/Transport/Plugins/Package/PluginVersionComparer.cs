namespace LabelFrame.Core.Transport.Plugins.Package;

/// <summary>
/// 插件版本比较（官方插件覆盖安装率先支持版本比较）。
/// 比较语义：双方均可解析为 <see cref="Version"/> 时按其比较；否则按字符串 Ordinal 比较
/// （与主版本演进约定配套——官方插件版本始终是主版本形态，解析失败仅出现在第三方异常版本号场景）。
/// </summary>
public static class PluginVersionComparer
{
    /// <summary>比较插件版本：返回值语义同 string.Compare（负 = left 较旧，0 = 相同，正 = left 较新）。</summary>
    public static int Compare(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            throw new ArgumentException("版本号不能为空。", nameof(left));
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            throw new ArgumentException("版本号不能为空。", nameof(right));
        }

        if (Version.TryParse(left.Trim(), out var leftVersion) && Version.TryParse(right.Trim(), out var rightVersion))
        {
            // 缺失段视为 0（1.0 == 1.0.0，避免 Version.CompareTo 把未指定段按 -1 判小）
            return Normalize(leftVersion).CompareTo(Normalize(rightVersion));
        }

        return string.Compare(left.Trim(), right.Trim(), StringComparison.Ordinal);
    }

    private static Version Normalize(Version version)
        => new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
