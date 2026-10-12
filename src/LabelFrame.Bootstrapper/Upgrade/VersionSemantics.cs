namespace LabelFrame.Bootstrapper.Upgrade;

using LabelFrame.Bootstrapper.Prerequisites;

/// <summary>
/// 统一版本比较：双方可解析 <see cref="Version"/>（容忍 "v" 前缀、<b>缺失段视为 0</b>——1.0 == 1.0.0）则按其比较；
/// 任一不可解析则字符串 Ordinal。<c>PayloadPlacer.ComparePluginVersions</c> 委托本实现——全系统版本比较单一语义，不得另起实现。
/// </summary>
public static class VersionSemantics
{
    /// <summary>比较两个版本字符串。&gt;0 = 左新，&lt;0 = 左旧，0 = 同版本。</summary>
    public static int Compare(string left, string right)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (left is null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right is null)
        {
            throw new ArgumentNullException(nameof(right));
        }
#endif

        var leftVersion = RuntimeProbe.ParseVersion(left);
        var rightVersion = RuntimeProbe.ParseVersion(right);
        if (leftVersion is not null && rightVersion is not null)
        {
            // 缺失段视为 0（Version.CompareTo 把未指定段按 -1 判小，与 1.0 == 1.0.0 语义不符——对齐 Core 的 PluginVersionComparer）
            return Normalize(leftVersion).CompareTo(Normalize(rightVersion));
        }

        return string.Compare(left.Trim(), right.Trim(), StringComparison.Ordinal);
    }

    /// <summary>是否同版本（缺失段视为 0；不可解析时字符串 Ordinal 相等）。</summary>
    public static bool EqualsVersion(string left, string right) => Compare(left, right) == 0;

    private static Version Normalize(Version version)
        => new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
