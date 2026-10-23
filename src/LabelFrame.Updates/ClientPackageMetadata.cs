using System.Globalization;

namespace LabelFrame.Updates;

/// <summary>Windows 和 Linux 共用的 MSI 元数据验证。</summary>
public static class ClientPackageMetadata
{
    public static ClientUpdatePackage Validate(IReadOnlyDictionary<string, string> properties, string architecture,
        string fileName, string publisher, string sha256, long size)
    {
        string Required(string key) => properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new InvalidDataException($"安装包缺少更新元数据 {key}，请使用支持自动升级的新版客户端安装包。");
        if (Required("ProductName") != ClientUpdateContract.ProductName || Required("Manufacturer") != "LabelFrame"
            || !Guid.TryParse(Required("UpgradeCode"), out var family) || family != Guid.Parse(ClientUpdateContract.UpgradeCode))
            throw new InvalidDataException("安装包不是 LabelFrame Client，不能发布为客户端更新。");
        if (!Guid.TryParse(Required("ProductCode"), out _)) throw new InvalidDataException("安装包产品标识无效。");
        if (!architecture.Equals("x64", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("自动升级只支持 x64 客户端安装包。");
        var version = Required("ProductVersion");
        ClientUpdateContract.ParseVersion(version);
        if (!fileName.Equals($"LabelFrame-Client-{version}.msi", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("文件名与安装包真实产品版本不一致。");
        var min = Required("LABELFRAME_MIN_SERVER_VERSION");
        var max = Required("LABELFRAME_MAX_SERVER_VERSION");
        if (ClientUpdateContract.ParseVersion(min) >= ClientUpdateContract.ParseVersion(max))
            throw new InvalidDataException("安装包服务端兼容范围无效。");
        if (!int.TryParse(Required("LABELFRAME_RUNTIME_MAJOR"), NumberStyles.None, CultureInfo.InvariantCulture, out var runtime) || runtime < 10)
            throw new InvalidDataException("安装包运行时要求无效。");
        if (size is <= 0 or > ClientUpdateContract.MaxPackageBytes) throw new InvalidDataException("安装包大小超出自动升级限制。");
        return new(version, Required("ProductCode"), "x64", publisher, sha256, size, min, max, runtime);
    }
}
