using System.Text.RegularExpressions;

namespace LabelFrame.Updates;

/// <summary>Android 更新包与设备候选的固定边界。</summary>
public static class AndroidUpdateContract
{
    public const string PackageName = "com.labelframe.androidhost";
    public const long MaxPackageBytes = 64 * 1024 * 1024;
    public const string MetadataEntry = "assets/labelframe-update.json";
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => char.IsAsciiHexDigit(c));
    public static bool IsDevice(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
        && !value.Any(char.IsControl);
    public static bool IsAbi(string? value) => value is "arm64-v8a" or "armeabi-v7a" or "x86" or "x86_64";
    public static Version ParseVersion(string? value)
    {
        if (value is null || value.Length > 32 || !Regex.IsMatch(value, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            || !Version.TryParse(value, out var version)) throw new InvalidDataException("Android 更新兼容版本必须为三段数字。");
        return version;
    }
}

/// <summary>签名 APK 内的更新协议和服务端兼容范围。</summary>
public sealed record AndroidUpdateMetadata(int Protocol, string MinServerVersion, string MaxServerVersion);

/// <summary>经内容验签读取的真实 APK 元数据。</summary>
public sealed record AndroidUpdatePackage(string PackageName, long VersionCode, string VersionName, int MinSdk, int TargetSdk,
    string[] Abis, string PublisherSha256, string Sha256, long SizeBytes, AndroidUpdateMetadata Compatibility);

/// <summary>内容与设备批次不可变的 Android 更新发布。</summary>
public sealed record AndroidUpdateRelease(string Id, AndroidUpdatePackage Package, string[] Devices, bool Paused, DateTimeOffset PublishedAt,
    string Mode = "confirm");

/// <summary>客户端候选查询的兼容性自报。</summary>
public sealed record AndroidUpdateQuery(string DeviceId, long CurrentVersionCode, int Sdk, string Abi, string PublisherSha256)
{
    public bool IsValid() => AndroidUpdateContract.IsDevice(DeviceId) && CurrentVersionCode >= 0 && Sdk is >= 23 and <= 100
        && AndroidUpdateContract.IsAbi(Abi) && AndroidUpdateContract.IsDigest(PublisherSha256);
}

/// <summary>设备自报的更新状态与事务标识。</summary>
public sealed record AndroidUpdateStatus(string DeviceId, long CurrentVersionCode, string Phase, long? TargetVersionCode = null,
    string? Reason = null, string? TransactionId = null);

/// <summary>Android APK 完整验签与真实元数据读取接口。</summary>
public interface IAndroidUpdatePackageInspector
{
    Task<AndroidUpdatePackage> InspectAsync(string path, CancellationToken cancellationToken = default);
}
