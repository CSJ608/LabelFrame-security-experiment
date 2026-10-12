using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LabelFrame.Bootstrapper.Prerequisites;

namespace LabelFrame.Bootstrapper.Placement;

/// <summary>落位决策结果（lfplugin 的 版本比较口径：覆盖 / 同版幂等 / 降级拒绝）。</summary>
public enum PlacementOutcome
{
    /// <summary>已解压落位（新装或新版本覆盖）。</summary>
    Placed,

    /// <summary>同版本幂等跳过（不重复解压，日志留痕）。</summary>
    SkippedSameVersion,

    /// <summary>降级拒绝（需先卸载再安装）。</summary>
    RefusedDowngrade,
}

/// <summary>卸载清理结果（容错口径 = 清理失败不阻断卸载主链，调用方记日志即可）。</summary>
public enum PlacementCleanupOutcome
{
    /// <summary>落位目录已删除（父目录按「已空才移除」口径尽力处理）。</summary>
    Cleaned,

    /// <summary>目标目录本就不存在（幂等成功——未装 / 已清理 / Bundle 变量指向空目标均归此类）。</summary>
    AlreadyAbsent,

    /// <summary>删除失败（文件占用 / 权限等）——尽力而为口径：日志留痕，不视作卸载失败。</summary>
    FailedBestEffort,
}

/// <summary>
/// 非 MSI 组件落位（机制 A：ExePackage 包装解压工具；本类为工具内核，net48 / net10 双腿可测）。
/// 目标目录约定对齐 DEPLOY §5 / §6 与 <c>InstallTargets</c>：webui zip → <c>plugins\web-ui</c>；<c>.lfplugin</c> → <c>plugins\&lt;pluginId&gt;\</c>。
/// </summary>
/// <remarks>
/// zip 均按「清空目标目录后整包解压」执行（目录内容完全由包拥有，避免覆盖残留陈旧文件）；
/// <c>.lfplugin</c> 额外执行 版本比较——包内 manifest.json.version 与已装目录 manifest.json.version：
/// 新版本覆盖、相同版本幂等跳过、旧版本拒绝。比较语义对齐（双方可解析 <see cref="Version"/> 按其比较，否则字符串 Ordinal）。
/// 卸载清理：<see cref="CleanPlacement"/> 与落位动作同目标对称——只清 Bundle 落位目录，父目录已空才移除。
/// </remarks>
public static class PayloadPlacer
{
    /// <summary>webui zip：覆盖落位（无独立版本概念，重跑重写同内容）。</summary>
    /// <param name="bundleVersion">Bundle 版本：非空时落位成功后写落位凭据文件（「落位凭据」）；空 = 不写（无 Bundle 语境的调用）。</param>
    public static PlacementOutcome PlaceArchive(string archivePath, string targetDir, string? bundleVersion = null)
    {
        ValidateArchive(archivePath);
        ReplaceTarget(archivePath, targetDir);
        WritePlacementMarker(targetDir, bundleVersion);
        return PlacementOutcome.Placed;
    }

    /// <summary>.lfplugin：包内 manifest.json 版本与已装版本比较后落位。</summary>
    /// <param name="bundleVersion">Bundle 版本：非空时落位成功后写落位凭据文件；同版幂等跳过与降级拒绝不写（目录未变）。</param>
    public static PlacementOutcome PlacePlugin(string archivePath, string targetDir, string? bundleVersion = null)
    {
        ValidateArchive(archivePath);

        var incomingVersion = ReadPluginVersionFromArchive(archivePath)
            ?? throw new InvalidOperationException($"插件包 {archivePath} 缺少根 manifest.json 或其 version 字段，拒绝落位。");

        var installedManifestPath = Path.Combine(targetDir, "manifest.json");
        if (File.Exists(installedManifestPath))
        {
            var installedVersion = ReadPluginVersionFromFile(installedManifestPath);
            if (installedVersion is not null)
            {
                var order = ComparePluginVersions(incomingVersion, installedVersion);
                if (order == 0)
                {
                    return PlacementOutcome.SkippedSameVersion;
                }

                if (order < 0)
                {
                    return PlacementOutcome.RefusedDowngrade;
                }
            }
        }

        ReplaceTarget(archivePath, targetDir);
        WritePlacementMarker(targetDir, bundleVersion);
        return PlacementOutcome.Placed;
    }

    /// <summary>
    /// 卸载清理：删除 Bundle 落位的目标目录——清理边界 = 只清本目录
    /// <c>plugins</c> 父目录内其余内容（平铺手动 DLL、第三方插件子目录）永不触碰；父目录仅在清理后已空时一并移除
    /// （非空即保留 = 用户资产在则留）。目录不存在 = 幂等成功；删除失败 = 尽力而为（<see cref="PlacementCleanupOutcome.FailedBestEffort"/>，
    /// 调用方日志留痕、不视作卸载失败）。凭据文件随目录消亡（无记账漂移）。
    /// </summary>
    public static PlacementCleanupOutcome CleanPlacement(string targetDir)
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

        if (!Directory.Exists(targetDir))
        {
            return PlacementCleanupOutcome.AlreadyAbsent;
        }

        try
        {
            Directory.Delete(targetDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 文件占用 / 权限等：清理失败不阻断卸载主链（残留目录重装覆盖 / 手动清理兜底）
            Console.Error.WriteLine($"落位目录清理失败（尽力而为，不阻断卸载）：{targetDir}——{ex.Message}");
            return PlacementCleanupOutcome.FailedBestEffort;
        }

        TryRemoveParentIfEmpty(targetDir);
        return PlacementCleanupOutcome.Cleaned;
    }

    /// <summary>父目录「已空才移除」：仅对落位约定的 <c>plugins</c> 父目录尝试非递归删除（非空抛 IOException 即放弃，用户资产在则保留）。</summary>
    private static void TryRemoveParentIfEmpty(string targetDir)
    {
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(targetDir));
            if (string.IsNullOrEmpty(parent)
                || !string.Equals(Path.GetFileName(parent), "plugins", StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(parent))
            {
                return;
            }

            Directory.Delete(parent, recursive: false); // 非空即抛 IOException——放弃即正确语义
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 父目录非空（含用户资产）或删除失败：保留，属预期路径
        }
    }

    /// <summary>
    /// 写落位凭据文件（「落位凭据」）：内容 = Bundle 版本——「Bundle 落位清单」的凭据形态
    /// 只有 Bundle 落位的目录携带；清理包检测比对凭据与 WixBundleVersion 判定 Present。尽力而为：写入失败日志留痕不阻断落位
    /// （后续卸载清理退化为不触发，重装覆盖兜底）。
    /// </summary>
    private static void WritePlacementMarker(string targetDir, string? bundleVersion)
    {
        if (string.IsNullOrWhiteSpace(bundleVersion))
        {
            return;
        }

        try
        {
            File.WriteAllText(Path.Combine(targetDir, PlacementMarker.FileName), bundleVersion, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"落位凭据写入失败（尽力而为，不阻断落位）：{targetDir}——{ex.Message}");
        }
    }

    /// <summary>读取包内（zip 根）manifest.json 的 version（不存在 / 非法返回 null）。</summary>
    internal static string? ReadPluginVersionFromArchive(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException($"插件包 {archivePath} 缺少根 manifest.json（.lfplugin 契约：pluginId / name / version 必填）。");
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
            ? version.GetString()
            : null;
    }

    private static string? ReadPluginVersionFromFile(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        return document.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
            ? version.GetString()
            : null;
    }

    /// <summary>版本比较（语义；起委托统一实现 <see cref="LabelFrame.Bootstrapper.Upgrade.VersionSemantics"/>—— / 单一语义，签名保留兼容既有调用与测试）。&gt;0 = 左新，&lt;0 = 左旧。</summary>
    public static int ComparePluginVersions(string left, string right)
        => LabelFrame.Bootstrapper.Upgrade.VersionSemantics.Compare(left, right);

    private static void ValidateArchive(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"落位包不存在：{archivePath}", archivePath);
        }
    }

    /// <summary>清空目标目录后整包解压（目录内容完全由包拥有；解压原子性以「先解压到同级临时目录再替换」近似）。</summary>
    private static void ReplaceTarget(string archivePath, string targetDir)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(targetDir));
        if (string.IsNullOrEmpty(parent))
        {
            throw new InvalidOperationException($"目标目录不合法：{targetDir}");
        }

        Directory.CreateDirectory(parent);

        var staging = targetDir + ".staging";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        try
        {
            ZipFile.ExtractToDirectory(archivePath, staging);

            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.Move(staging, targetDir);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (IOException)
                {
                    // 残留清理失败不阻断落位结果（下次落位会先行清理）
                }
            }
        }
    }
}
