using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using LabelFrame.Updates;

namespace LabelFrame.AndroidHost.Updates;

/// <summary>以本机安装身份和官方验签器独立校验下载 APK。</summary>
[SupportedOSPlatform("android28.0")]
public sealed class DevicePackageInspector(Context context, AndroidUpdateQuery identity) : IAndroidUpdatePackageInspector
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static AndroidUpdateQuery Identity(Context context, string deviceId)
    {
        var installed = context.PackageManager!.GetPackageInfo(AndroidUpdateContract.PackageName, PackageInfoFlags.SigningCertificates)
            ?? throw new InvalidDataException("无法读取本机安装身份。");
        var signing = installed.SigningInfo ?? throw new InvalidDataException("本机签名信息缺失。");
        var signers = signing.GetApkContentsSigners();
        if (signing.HasMultipleSigners || signing.HasPastSigningCertificates || signers is not { Length: 1 })
            throw new InvalidDataException("暂不支持多签名或签名轮转的宿主升级。");
        var fingerprint = Convert.ToHexString(SHA256.HashData(signers[0].ToByteArray()!));
        var abi = Build.SupportedAbis?.FirstOrDefault(AndroidUpdateContract.IsAbi)
            ?? throw new InvalidDataException("本机 ABI 不受支持。");
        return new(deviceId, installed.LongVersionCode, (int)Build.VERSION.SdkInt, abi, fingerprint);
    }

    public Task<AndroidUpdatePackage> InspectAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run(() => Inspect(path, cancellationToken), cancellationToken);

    private AndroidUpdatePackage Inspect(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > AndroidUpdateContract.MaxPackageBytes) throw new InvalidDataException("APK 大小无效。");
        var hash = Convert.ToHexString(SHA256.HashData(file));
        var signer = Com.Labelframe.Updates.ApkVerification.Verify(path, identity.Sdk);
        if (!string.Equals(signer, identity.PublisherSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("APK 签名与已安装宿主不一致，禁止卸载或清数据绕过。");
        ct.ThrowIfCancellationRequested();
        var archive = context.PackageManager!.GetPackageArchiveInfo(path, 0)
            ?? throw new InvalidDataException("无法解析 APK 真实 Manifest。");
        var app = archive.ApplicationInfo ?? throw new InvalidDataException("APK 应用信息缺失。");
        if (archive.PackageName != AndroidUpdateContract.PackageName || archive.LongVersionCode <= identity.CurrentVersionCode
            || (app.Flags & ApplicationInfoFlags.Debuggable) != 0 || archive.SplitNames is { Count: > 0 }
            || (int)app.MinSdkVersion > identity.Sdk || (int)app.MinSdkVersion < 23)
            throw new InvalidDataException("APK 产品、版本、调试标记、拆分或系统要求不满足升级条件。");
        file.Position = 0;
        using var zip = new ZipArchive(file, ZipArchiveMode.Read, true);
        if (zip.Entries.Count > 20000 || zip.Entries.GroupBy(e => e.FullName, StringComparer.Ordinal).Any(g => g.Count() != 1))
            throw new InvalidDataException("APK 条目超限或存在重复路径。");
        var abis = zip.Entries.Where(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) && e.FullName.EndsWith(".so", StringComparison.Ordinal))
            .Select(e => e.FullName.Split('/')).Where(p => p.Length == 3).Select(p => p[1]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!abis.Contains(identity.Abi, StringComparer.Ordinal) || abis.Any(a => !AndroidUpdateContract.IsAbi(a)))
            throw new InvalidDataException("APK 原生架构不兼容本设备。");
        var entry = zip.GetEntry(AndroidUpdateContract.MetadataEntry);
        if (entry is null || entry.Length is <= 0 or > 4096) throw new InvalidDataException("APK 更新协议元数据缺失或超限。");
        AndroidUpdateMetadata metadata;
        using (var input = entry.Open())
        {
            var bytes = new byte[4097];
            var length = input.ReadAtLeast(bytes, bytes.Length, false);
            if (length > 4096) throw new InvalidDataException("APK 更新协议元数据超限。");
            metadata = JsonSerializer.Deserialize<AndroidUpdateMetadata>(bytes.AsSpan(0, length), Json)
                ?? throw new InvalidDataException("APK 更新协议为空。");
        }
        if (metadata.Protocol != 1 || AndroidUpdateContract.ParseVersion(metadata.MinServerVersion) >= AndroidUpdateContract.ParseVersion(metadata.MaxServerVersion))
            throw new InvalidDataException("APK 更新协议范围无效。");
        file.Position = 0;
        if (Convert.ToHexString(SHA256.HashData(file)) != hash) throw new InvalidDataException("APK 校验期间发生变更。");
        ct.ThrowIfCancellationRequested();
        return new(archive.PackageName!, archive.LongVersionCode, archive.VersionName ?? "", (int)app.MinSdkVersion,
            (int)app.TargetSdkVersion, abis, signer!, hash, file.Length, metadata);
    }
}
