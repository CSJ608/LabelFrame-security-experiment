namespace LabelFrame.Bootstrapper.Placement;

/// <summary>
/// 落位凭据（DESIGN 「落位凭据」）：<c>.labelframe-bundle-placement</c> 文件（内容 = Bundle 版本）。
/// 「Bundle 落位清单」的凭据形态——只有 Bundle 落位的目录携带该文件；用户经客户端「插件管理」（<c>PluginInstaller</c> 通道）
/// 安装的官方插件与一切手动放置资产无此文件，永不被 Bundle 卸载清理（清理边界的技术保证）。凭据随目录消亡（清理 = 删目录）。
/// </summary>
public static class PlacementMarker
{
    /// <summary>凭据文件名（落位目录根；隐藏点文件，加载器按 DLL 扫描 / web 静态根均不受影响）。</summary>
    public const string FileName = ".labelframe-bundle-placement";

    /// <summary>读落位目录凭据的 Bundle 版本（目录无凭据 / 不可读返回 null = 非 Bundle 落位或已清理）。</summary>
    public static string? BundleVersionOf(string targetDir)
    {
#if NET10_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDir);
#else
        // net48 腿无 ArgumentException.ThrowIfNullOrWhiteSpace（.NET 8+ API）
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            throw new ArgumentException("落位目标目录不得为空。", nameof(targetDir));
        }
#endif

        try
        {
            var markerPath = Path.Combine(targetDir, FileName);
            return File.Exists(markerPath)
                ? File.ReadAllText(markerPath).Trim()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null; // 探测只读语义：不可读按「无凭据」处理（fail-closed 到不清理，用户资产安全侧）
        }
    }

    /// <summary>清理包 DetectCondition 判定：凭据与 Bundle 版本一致才视为「本版本 Bundle 落位在位」（Present）；版本不可得（null）恒假（fail-closed）。</summary>
    public static bool IsPlacedByBundle(string targetDir, string? bundleVersion)
    {
        var recorded = BundleVersionOf(targetDir);
        if (recorded is null || bundleVersion is null)
        {
            return false;
        }

        return string.Equals(recorded.Trim(), bundleVersion.Trim(), StringComparison.Ordinal);
    }
}
