using System.IO.Compression;

namespace LabelFrame.Core.Transport.Plugins.Package;

/// <summary>
/// 插件包 zip 读取：根 manifest.json + 插件 DLL；zip-slip 防护（拒绝绝对路径 / 盘符 / .. 段）。
/// 校验失败抛 <see cref="PluginPackageException"/>（中文可行动消息）；
/// 框架 zip 解析抛出的英文异常在此归类转译（非 zip / zip 损坏），不外泄英文原话。
/// </summary>
public static class PluginPackageReader
{
    /// <summary>包内清单文件名。</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>插件包内容：manifest + 包内 DLL 条目名（zip 相对路径）。</summary>
    public sealed record PluginPackageContent(PluginPackageManifest Manifest, IReadOnlyList<string> DllEntries);

    /// <summary>
    /// 读取插件包：校验 zip / 根 manifest / 必填字段 / 条目名安全（zip-slip）。
    /// 失败抛 <see cref="PluginPackageException"/>（中文消息）。
    /// </summary>
    public static PluginPackageContent Read(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        using var archive = OpenArchive(zipBytes);

        var manifestEntry = archive.GetEntry(ManifestFileName)
            ?? throw new PluginPackageException($"插件包缺少根 {ManifestFileName}。");
        string manifestJson;
        try
        {
            using var entryStream = manifestEntry.Open();
            using var reader = new StreamReader(entryStream);
            manifestJson = reader.ReadToEnd();
        }
        catch (InvalidDataException ex)
        {
            // 条目数据损坏（CRC / 压缩数据非法）由 zip 框架以英文抛出 → 归类为 zip 损坏
            throw new PluginPackageException("插件包已损坏（条目数据读取失败），请重新导出插件包后再试。", ex);
        }

        var manifest = PluginPackageManifest.Parse(manifestJson);

        var dlls = new List<string>();
        foreach (var entry in archive.Entries)
        {
            if (!IsSafeEntryName(entry.FullName))
            {
                throw new PluginPackageException($"插件包含不安全的条目名：{entry.FullName}。");
            }

            if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                dlls.Add(entry.FullName);
            }
        }

        if (dlls.Count == 0)
        {
            throw new PluginPackageException("插件包未包含任何 DLL（至少需要实现 ITransportPlugin 的主 DLL）。");
        }

        return new PluginPackageContent(manifest, dlls);
    }

    /// <summary>尝试读取；失败返回 false + 中文原因（服务端列表展示用，invalid 条目仍可删除）。</summary>
    public static bool TryRead(byte[] zipBytes, out PluginPackageContent? content, out string? error)
    {
        try
        {
            content = Read(zipBytes);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            content = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>安全解压到目标目录（zip-slip 防护：仅允许安全相对路径；目录条目跳过）。</summary>
    public static void ExtractTo(byte[] zipBytes, string targetDirectory)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);
        ArgumentNullException.ThrowIfNull(targetDirectory);

        Directory.CreateDirectory(targetDirectory);
        var rootFull = Path.GetFullPath(targetDirectory);

        using var archive = OpenArchive(zipBytes);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // 目录条目
            }

            if (!IsSafeEntryName(entry.FullName))
            {
                throw new PluginPackageException($"插件包含不安全的条目名：{entry.FullName}。");
            }

            var destPath = Path.Combine(rootFull, entry.FullName.Replace('\\', '/'));
            var fullPath = Path.GetFullPath(destPath);
            if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginPackageException($"插件包含越界条目：{entry.FullName}。");
            }

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            try
            {
                using var entryStream = entry.Open();
                using var fileStream = File.Create(fullPath);
                entryStream.CopyTo(fileStream);
            }
            catch (InvalidDataException ex)
            {
                // 条目数据损坏（CRC / 压缩数据非法）由 zip 框架以英文抛出 → 归类为 zip 损坏
                throw new PluginPackageException($"插件包已损坏（条目 {entry.FullName} 数据读取失败），请重新导出插件包后再试。", ex);
            }
        }
    }

    /// <summary>条目名安全校验（zip-slip 防护）：拒绝绝对路径 / 盘符 / . 与 .. 段 / 非法字符。</summary>
    public static bool IsSafeEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return false;
        }

        var normalized = entryName.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            return false;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment is "" or "." or "..")
            {
                return false;
            }

            if (segment.Contains(':') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 打开 zip：缺 zip 魔数（PK 头）判「非 zip」；有魔数但结构读不出（EOCD 缺失 / 中央目录损坏）判「zip 损坏」。
    /// 框架英文异常转中文并保留原始异常为 InnerException。
    /// </summary>
    private static ZipArchive OpenArchive(byte[] zipBytes)
    {
        // zip 局部文件头 / 跨卷 / 空包魔数（PK\x03\x04 / PK\x07\x08 / PK\x05\x06）
        var hasZipMagic = zipBytes.Length >= 4
            && zipBytes[0] == 0x50 && zipBytes[1] == 0x4B
            && ((zipBytes[2] == 0x03 && zipBytes[3] == 0x04)
                || (zipBytes[2] == 0x05 && zipBytes[3] == 0x06)
                || (zipBytes[2] == 0x07 && zipBytes[3] == 0x08));
        if (!hasZipMagic)
        {
            throw new PluginPackageException("文件不是 zip 格式的插件包，无法安装。请使用「导出插件包」生成的 .zip 文件。");
        }

        try
        {
            return new ZipArchive(new MemoryStream(zipBytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new PluginPackageException("插件包已损坏（zip 结构不完整或中央目录损坏），请重新导出插件包后再试。", ex);
        }
    }
}
