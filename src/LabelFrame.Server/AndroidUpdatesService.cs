using System.Text.Json;
using LabelFrame.Updates;

namespace LabelFrame.Server;

/// <summary>持久化 Android 不可变发布批次并筛选设备更新资格。</summary>
public sealed class AndroidUpdatesService(string directory, IAndroidUpdatePackageInspector inspector) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _reportGate = new();
    private readonly Dictionary<string, AndroidUpdateReport> _reports = new(StringComparer.Ordinal);
    public void Dispose() => _gate.Dispose();

    public AndroidUpdateRelease[] List() => !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*.json")
        .Select(path => JsonSerializer.Deserialize<AndroidUpdateRelease>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Android 发布记录损坏，请管理员修复。"))
        .OrderByDescending(r => r.Package.VersionCode).ThenByDescending(r => r.PublishedAt).ToArray();

    public async Task<AndroidUpdatePackage> PreviewAsync(string source, CancellationToken ct)
    {
        var package = await UpdateDraftSnapshot.InspectAsync(source, ".apk", path => inspector.InspectAsync(path, ct), ct);
        if (!CompatibleServer(package)) throw new InvalidDataException("APK 不兼容当前服务端版本。");
        return package;
    }

    public async Task<AndroidUpdateRelease> PublishAsync(string source, string[] devices, bool allDevicesConfirmed, string mode, CancellationToken ct, string? expectedSha256 = null)
    {
        if (mode != "confirm") throw new InvalidDataException("当前仅支持系统确认安装，静默安装尚未通过安全验证。");
        if (devices.Length > 1000 || devices.Any(d => !AndroidUpdateContract.IsDevice(d)) || (devices.Length == 0 && !allDevicesConfirmed))
            throw new InvalidDataException("请指定有效设备批次；全量发布必须明确确认。");
        await _gate.WaitAsync(ct);
        var id = Guid.NewGuid().ToString("N");
        var destination = Path.Combine(directory, id + ".apk");
        var committed = false;
        try
        {
            Directory.CreateDirectory(directory);
            var releases = List();
            if (releases.Length >= 100) throw new InvalidDataException("Android 发布批次已达 100 个，请管理员归档后再发布。");
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (input.Length is <= 0 or > AndroidUpdateContract.MaxPackageBytes) throw new InvalidDataException("APK 大小超出受控升级限制。");
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > AndroidUpdateContract.MaxPackageBytes) throw new InvalidDataException("APK 大小超出受控升级限制。");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                output.Flush(flushToDisk: true);
            }
            var package = await inspector.InspectAsync(destination, ct);
            UpdateDraftSnapshot.VerifyExpected(package.Sha256, expectedSha256);
            if (!CompatibleServer(package)) throw new InvalidDataException("APK 不兼容当前服务端版本。");
            if (releases.Any(r => r.Package.VersionCode == package.VersionCode && r.Package.Sha256 != package.Sha256))
                throw new InvalidDataException("同一 versionCode 已发布不同内容，不能覆盖。");
            var release = new AndroidUpdateRelease(id, package, devices.Distinct(StringComparer.Ordinal).ToArray(), false, DateTimeOffset.UtcNow);
            await SaveAsync(release, ct);
            committed = true;
            return release;
        }
        finally
        {
            try { if (!committed) File.Delete(destination); }
            finally { _gate.Release(); }
        }
    }

    public async Task<AndroidUpdateRelease?> PauseAsync(string id, bool paused, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var release = List().FirstOrDefault(r => r.Id == id);
            if (release is null) return null;
            release = release with { Paused = paused };
            await SaveAsync(release, ct);
            return release;
        }
        finally { _gate.Release(); }
    }

    private async Task SaveAsync(AndroidUpdateRelease release, CancellationToken ct)
    {
        var path = Path.Combine(directory, release.Id + ".json");
        var temporary = path + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(output, release, Json, ct);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static bool CompatibleServer(AndroidUpdatePackage package)
    {
        var current = AndroidUpdateContract.ParseVersion(ServerOptions.ProductVersion);
        return package.Compatibility.Protocol == 1 && current >= AndroidUpdateContract.ParseVersion(package.Compatibility.MinServerVersion)
            && current < AndroidUpdateContract.ParseVersion(package.Compatibility.MaxServerVersion);
    }

    public AndroidUpdateRelease? Select(AndroidUpdateQuery query) => !query.IsValid() ? null : List().FirstOrDefault(r =>
        r.Mode == "confirm" && !r.Paused && (r.Devices.Length == 0 || r.Devices.Contains(query.DeviceId, StringComparer.Ordinal))
        && r.Package.VersionCode > query.CurrentVersionCode && r.Package.MinSdk <= query.Sdk && r.Package.Abis.Contains(query.Abi, StringComparer.Ordinal)
        && string.Equals(r.Package.PublisherSha256, query.PublisherSha256, StringComparison.OrdinalIgnoreCase) && CompatibleServer(r.Package));

    public string? DownloadPath(string id, AndroidUpdateQuery query)
    {
        var release = Select(query);
        return release?.Id == id ? Path.Combine(directory, release.Id + ".apk") : null;
    }

    public bool Report(AndroidUpdateStatus status)
    {
        if (!AndroidUpdateContract.IsDevice(status.DeviceId) || status.CurrentVersionCode < 0 || status.TargetVersionCode < 0
            || string.IsNullOrWhiteSpace(status.Phase) || status.Phase.Length > 64 || status.Phase.Any(char.IsControl)
            || status.Reason?.Length > 1024 || status.TransactionId?.Length > 64) return false;
        lock (_reportGate)
        {
            if (!_reports.ContainsKey(status.DeviceId) && _reports.Count >= 1000) return false;
            _reports[status.DeviceId] = new(status, DateTimeOffset.UtcNow);
            return true;
        }
    }

    public AndroidUpdateReport[] Reports()
    {
        lock (_reportGate) return _reports.Values.OrderBy(r => r.Status.DeviceId, StringComparer.Ordinal).ToArray();
    }
}

/// <summary>带服务端接收时间的设备自报更新状态。</summary>
public sealed record AndroidUpdateReport(AndroidUpdateStatus Status, DateTimeOffset ReceivedAt);
