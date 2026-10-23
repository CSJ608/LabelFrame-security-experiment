using System.Globalization;

namespace LabelFrame.Updates;

/// <summary>客户端升级协议的固定产品边界。</summary>
public static class ClientUpdateContract
{
    public const string UpgradeCode = "EE3F2357-56CE-4C0F-AEC1-4C04B6E6BCDA";
    public const string ProductName = "LabelFrame Client";
    public const long MaxPackageBytes = 64 * 1024 * 1024;
    public const string PipeName = "LabelFrame.ClientUpdater";
    public const string ServiceName = "LabelFrameClientUpdater";
    public static string CertificatePath => Path.Combine(AppContext.BaseDirectory, "update-publisher.cer");

    public static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0
            || version.Major > 255 || version.Minor > 255 || version.Build > 65535 || version.ToString(3) != value)
            throw new InvalidDataException("安装包版本必须是合法的三段 Windows Installer 版本。");
        return version;
    }

    public static bool IsInWindow(TimeOnly now, string start, string end)
    {
        if (!TimeOnly.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !TimeOnly.TryParseExact(end, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) || from == to) return false;
        return from < to ? now >= from && now < to : now >= from || now < to;
    }
}

/// <summary>从已验证 MSI 读取的客户端更新元数据。</summary>
public sealed record ClientUpdatePackage(string Version, string ProductCode, string Architecture, string PublisherSha256,
    string Sha256, long SizeBytes, string MinServerVersion, string MaxServerVersion, int RuntimeMajor);

/// <summary>不可变的更新发布与可暂停状态。</summary>
public sealed record ClientUpdateRelease(string Id, string FileName, ClientUpdatePackage Package,
    string[] Devices, bool Paused, DateTimeOffset PublishedAt);

/// <summary>客户端的本地升级策略。</summary>
public sealed record ClientUpdateSettings(string Mode = "download", string WindowStart = "02:00", string WindowEnd = "04:00")
{
    public void Validate()
    {
        if (Mode is not ("notify" or "download" or "maintenance")) throw new InvalidDataException("请选择有效的客户端更新方式。");
        if (!TimeOnly.TryParseExact(WindowStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(WindowEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || start == end)
            throw new InvalidDataException("维护时间必须是不同的 HH:mm 起止时间。");
    }
}

/// <summary>客户端及服务端展示的更新状态。</summary>
public sealed record ClientUpdateStatus(string DeviceId, string CurrentVersion, string Phase, string? TargetVersion = null,
    string? Reason = null, DateTimeOffset? CheckedAt = null, string? TransactionId = null, long DownloadedBytes = 0, long TotalBytes = 0);

/// <summary>更新包的完整性、发布者及产品读取边界。</summary>
public interface IClientUpdatePackageInspector
{
    Task<ClientUpdatePackage> InspectAsync(string path, string fileName, CancellationToken cancellationToken = default);
}
