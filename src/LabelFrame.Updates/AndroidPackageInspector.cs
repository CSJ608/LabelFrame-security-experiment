using System.ComponentModel;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LabelFrame.Updates;

/// <summary>通过固定 Android 工具链验证 APK 内容并读取发布元数据。</summary>
public sealed class AndroidPackageInspector(string? javaPath, string? apksignerJarPath, string? aapt2Path, string? trustedPublisher)
    : IAndroidUpdatePackageInspector
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public async Task<AndroidUpdatePackage> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!AndroidUpdateContract.IsDigest(trustedPublisher)) throw new InvalidDataException("尚未配置可信 Android 发布证书 SHA256。");
        var bridge = Path.Combine(AppContext.BaseDirectory, "ApkVerifierBridge.java");
        foreach (var tool in new[] { javaPath, apksignerJarPath, aapt2Path, bridge })
            if (string.IsNullOrWhiteSpace(tool) || !Path.IsPathFullyQualified(tool) || !File.Exists(tool))
                throw new InvalidDataException("Android 发布校验工具未配置：需要 JDK 17+、apksigner.jar 与 aapt2 的绝对路径。");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > AndroidUpdateContract.MaxPackageBytes) throw new InvalidDataException("APK 大小超出受控升级限制。");
        var before = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        try
        {
            var fingerprint = Encoding.UTF8.GetString(await InspectionProcess.RunAsync(javaPath!, ["-cp", apksignerJarPath!, bridge, Path.GetFullPath(path)], cancellationToken)).Trim();
            if (!AndroidUpdateContract.IsDigest(fingerprint) || !string.Equals(fingerprint, trustedPublisher, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("APK 发布证书与预配置可信指纹不一致。");
            var badging = Encoding.UTF8.GetString(await InspectionProcess.RunAsync(aapt2Path!, ["dump", "badging", Path.GetFullPath(path)], cancellationToken));
            var tree = Encoding.UTF8.GetString(await InspectionProcess.RunAsync(aapt2Path!, ["dump", "xmltree", "--file", "AndroidManifest.xml", Path.GetFullPath(path)], cancellationToken));
            file.Position = 0;
            using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > 20000 || zip.Entries.GroupBy(e => e.FullName, StringComparer.Ordinal).Any(g => g.Count() != 1))
                throw new InvalidDataException("APK 条目过多或存在重复路径。");
            var entry = zip.GetEntry(AndroidUpdateContract.MetadataEntry);
            if (entry is null || entry.Length is <= 0 or > 4096) throw new InvalidDataException("APK 缺少有效的已签名更新协议元数据。");
            AndroidUpdateMetadata metadata;
            await using (var input = entry.Open())
            {
                var bytes = new byte[4097];
                var read = await input.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken);
                if (read > 4096) throw new InvalidDataException("APK 更新元数据过大。");
                metadata = JsonSerializer.Deserialize<AndroidUpdateMetadata>(bytes.AsSpan(0, read), Json)
                    ?? throw new InvalidDataException("APK 更新元数据为空。");
            }
            var package = ParseMetadata(badging, tree, metadata, fingerprint.ToUpperInvariant(), before, file.Length);
            var actualAbis = zip.Entries.Where(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) && e.FullName.EndsWith(".so", StringComparison.Ordinal))
                .Select(e => e.FullName.Split('/')).Where(p => p.Length == 3).Select(p => p[1]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (!actualAbis.SequenceEqual(package.Abis.Order(StringComparer.Ordinal))) throw new InvalidDataException("APK 原生架构元数据不一致。");
            file.Position = 0;
            if (Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)) != before) throw new InvalidDataException("APK 校验期间内容发生变化。");
            return package;
        }
        catch (JsonException ex) { throw new InvalidDataException("APK 更新元数据格式无效。", ex); }
        catch (FormatException ex) { throw new InvalidDataException("APK 数值元数据无效。", ex); }
        catch (OverflowException ex) { throw new InvalidDataException("APK 数值元数据超出范围。", ex); }
        catch (Win32Exception ex) { throw new InvalidDataException("Android 校验工具启动失败。", ex); }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidDataException("Android 校验工具执行超时。", ex); }
    }

    internal static AndroidUpdatePackage ParseMetadata(string badging, string tree, AndroidUpdateMetadata metadata, string publisher, string hash, long size)
    {
        static Match Match(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        static string Field(string text, string pattern)
        {
            var match = Match(text, pattern);
            return match.Success ? match.Groups[1].Value : throw new InvalidDataException("APK 缺少必要的真实元数据。");
        }
        var package = Field(badging, @"^package: name='([^']+)'");
        if (package != AndroidUpdateContract.PackageName || Match(badging, @"^application-debuggable|^package:.*\bsplit='").Success
            || Match(tree, @"^\s+A: (?:.*:)?(?:isSplitRequired|isFeatureSplit)\([^)]*\)=true").Success)
            throw new InvalidDataException("仅允许非调试、非拆分的 LabelFrame AndroidHost APK。");
        var low = uint.Parse(Field(tree, @"^    A: .*:versionCode\([^)]*\)=(\d+)\s*$"), CultureInfo.InvariantCulture);
        var majorMatch = Match(tree, @"^    A: .*:versionCodeMajor\([^)]*\)=(\d+)\s*$");
        if (!majorMatch.Success && tree.Contains(":versionCodeMajor(", StringComparison.Ordinal)) throw new InvalidDataException("APK 版本高位无效。");
        var major = majorMatch.Success ? uint.Parse(majorMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        if (major > int.MaxValue || (low == 0 && major == 0)) throw new InvalidDataException("APK versionCode 无效。");
        var code = ((long)major << 32) | low;
        var name = Field(badging, @"^package:.*\bversionName='([^']*)'");
        var min = int.Parse(Field(badging, @"^(?:minSdkVersion|sdkVersion):'(\d+)'"), CultureInfo.InvariantCulture);
        var target = int.Parse(Field(badging, @"^targetSdkVersion:'(\d+)'"), CultureInfo.InvariantCulture);
        var native = Match(badging, @"^native-code:(.*)$");
        var abis = native.Success ? Regex.Matches(native.Groups[1].Value, "'([^']+)'", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray() : [];
        if (name.Length is 0 or > 128 || name.Any(char.IsControl) || min < 23 || target < min || target > 100 || abis.Length == 0
            || abis.Any(a => !AndroidUpdateContract.IsAbi(a)) || metadata.Protocol != 1
            || AndroidUpdateContract.ParseVersion(metadata.MinServerVersion) >= AndroidUpdateContract.ParseVersion(metadata.MaxServerVersion))
            throw new InvalidDataException("APK 系统、架构或升级协议范围无效。");
        return new(package, code, name, min, target, abis, publisher, hash, size, metadata);
    }
}
