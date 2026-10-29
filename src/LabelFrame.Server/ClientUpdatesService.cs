using System.Collections.Concurrent;
using System.Text.Json;
using LabelFrame.Updates;

namespace LabelFrame.Server;

/// <summary>保存不可变客户端发布快照、推广范围与设备升级状态。</summary>
public sealed class ClientUpdatesService(string directory, IClientUpdatePackageInspector inspector) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, ClientUpdateReport> _reports = new(StringComparer.Ordinal);
    public void Dispose() => _gate.Dispose();

    public ClientUpdateRelease[] List()
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.json").Select(path =>
            JsonSerializer.Deserialize<ClientUpdateRelease>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("更新发布记录损坏，请管理员修复。"))
            .OrderByDescending(r => ClientUpdateContract.ParseVersion(r.Package.Version)).ToArray();
    }

    public async Task<ClientUpdatePackage> PreviewAsync(string source, string fileName, CancellationToken ct)
    {
        var package = await UpdateDraftSnapshot.InspectAsync(source, ".msi", path => inspector.InspectAsync(path, fileName, ct), ct);
        var server = ClientUpdateContract.ParseVersion(ServerOptions.ProductVersion);
        if (server < ClientUpdateContract.ParseVersion(package.MinServerVersion) || server >= ClientUpdateContract.ParseVersion(package.MaxServerVersion))
            throw new InvalidDataException("安装包不兼容当前服务端版本。");
        return package;
    }

    public async Task<ClientUpdateRelease> PublishAsync(string source, string fileName, string[] devices, CancellationToken ct, string? expectedSha256 = null)
    {
        if (devices.Length > 1000 || devices.Any(d => string.IsNullOrWhiteSpace(d) || d.Length > 128))
            throw new InvalidDataException("推广设备标识无效，最多指定 1000 台设备。");
        await _gate.WaitAsync(ct);
        var id = Guid.NewGuid().ToString("N");
        var destination = Path.Combine(directory, id + ".msi");
        var committed = false;
        try
        {
            Directory.CreateDirectory(directory);
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (input.Length is <= 0 or > ClientUpdateContract.MaxPackageBytes) throw new InvalidDataException("安装包大小超出自动升级限制。");
                await input.CopyToAsync(output, ct);
            }
            var package = await inspector.InspectAsync(destination, fileName, ct);
            UpdateDraftSnapshot.VerifyExpected(package.Sha256, expectedSha256);
            var server = ClientUpdateContract.ParseVersion(ServerOptions.ProductVersion);
            if (server < ClientUpdateContract.ParseVersion(package.MinServerVersion) || server >= ClientUpdateContract.ParseVersion(package.MaxServerVersion))
                throw new InvalidDataException("安装包不兼容当前服务端版本。");
            if (List().Any(r => r.Package.Version == package.Version)) throw new InvalidDataException("此版本已发布，不能覆盖或重复发布。");
            var release = new ClientUpdateRelease(id, $"LabelFrame-Client-{package.Version}.msi", package, devices.Distinct(StringComparer.Ordinal).ToArray(), false, DateTimeOffset.UtcNow);
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

    public async Task<ClientUpdateRelease?> PauseAsync(string id, bool paused, CancellationToken ct)
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

    private async Task SaveAsync(ClientUpdateRelease release, CancellationToken ct)
    {
        var path = Path.Combine(directory, release.Id + ".json");
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(release, Json), ct);
        File.Move(temporary, path, true);
    }

    public ClientUpdateRelease? Select(string device, string currentVersion) => List().FirstOrDefault(r =>
        !r.Paused && (r.Devices.Length == 0 || r.Devices.Contains(device, StringComparer.Ordinal))
        && ClientUpdateContract.ParseVersion(ServerOptions.ProductVersion) >= ClientUpdateContract.ParseVersion(r.Package.MinServerVersion)
        && ClientUpdateContract.ParseVersion(ServerOptions.ProductVersion) < ClientUpdateContract.ParseVersion(r.Package.MaxServerVersion)
        && ClientUpdateContract.ParseVersion(r.Package.Version) > ClientUpdateContract.ParseVersion(currentVersion));

    public string? DownloadPath(string id, string device, string currentVersion)
    {
        var release = Select(device, currentVersion);
        return release?.Id == id ? Path.Combine(directory, release.Id + ".msi") : null;
    }

    public bool Report(ClientUpdateStatus status)
    {
        if (string.IsNullOrWhiteSpace(status.DeviceId) || status.DeviceId.Length > 128 || string.IsNullOrWhiteSpace(status.Phase)
            || status.CurrentVersion is null || status.Phase.Length > 64
            || status.Reason?.Length > 1024 || status.TargetVersion?.Length > 32 || status.CurrentVersion.Length > 32
            || status.TransactionId?.Length > 64 || (!_reports.ContainsKey(status.DeviceId) && _reports.Count >= 1000)) return false;
        _reports[status.DeviceId] = new(status, DateTimeOffset.UtcNow);
        return true;
    }
    public ClientUpdateReport[] Reports() => _reports.Values.OrderBy(r => r.Status.DeviceId, StringComparer.Ordinal).ToArray();
}

/// <summary>包含服务端接收时间的客户端自报状态。</summary>
public sealed record ClientUpdateReport(ClientUpdateStatus Status, DateTimeOffset ReceivedAt);
