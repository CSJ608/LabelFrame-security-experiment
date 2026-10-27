using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LabelFrame.Updates;

namespace LabelFrame.AndroidHost.Updates;

/// <summary>从固定服务端读取候选并有界下载到应用私有缓存。</summary>
public sealed class UpdateDownloadClient : IDisposable
{
    public static void RemoveCandidate(string directory)
    {
        try { File.Delete(Path.Combine(directory, "candidate.apk")); }
        catch (DirectoryNotFoundException) { }
    }

    private readonly HttpClient _http;
    private readonly string _base;
    private readonly string _query;
    private readonly AndroidUpdateQuery _identity;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public UpdateDownloadClient(string serverUrl, AndroidUpdateQuery query, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !query.IsValid())
            throw new InvalidDataException("升级服务端地址或本机身份无效。");
        _base = serverUrl.TrimEnd('/') + "/api/android-updates";
        _identity = query;
        _query = "?deviceId=" + Uri.EscapeDataString(query.DeviceId)
            + "&currentVersionCode=" + query.CurrentVersionCode.ToString(CultureInfo.InvariantCulture)
            + "&sdk=" + query.Sdk.ToString(CultureInfo.InvariantCulture) + "&abi=" + Uri.EscapeDataString(query.Abi)
            + "&publisherSha256=" + query.PublisherSha256;
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10) })
            { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 256 * 1024 };
    }

    public async Task<AndroidUpdateRelease?> CandidateAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _http.GetAsync(_base + "/candidate" + _query, timeout.Token);
        response.EnsureSuccessStatusCode();
        var envelope = JsonSerializer.Deserialize<CandidateEnvelope>(await response.Content.ReadAsStringAsync(timeout.Token), Json)
            ?? throw new InvalidDataException("更新候选响应为空。");
        if (envelope.Release is { } release)
        {
            ValidateRelease(release);
            using var infoResponse = await _http.GetAsync(_base[..^"/android-updates".Length] + "/server/info", timeout.Token);
            infoResponse.EnsureSuccessStatusCode();
            var info = JsonSerializer.Deserialize<ServerInfo>(await infoResponse.Content.ReadAsStringAsync(timeout.Token), Json);
            var version = AndroidUpdateContract.ParseVersion(info?.Version);
            if (release.Package.Compatibility.Protocol != 1 || version < AndroidUpdateContract.ParseVersion(release.Package.Compatibility.MinServerVersion)
                || version >= AndroidUpdateContract.ParseVersion(release.Package.Compatibility.MaxServerVersion))
                throw new InvalidDataException("更新 APK 与当前服务端协议版本不兼容。");
        }
        return envelope.Release;
    }

    public async Task VerifyAdministratorAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("请输入原服务端的更新管理口令。");
        var uri = new Uri(_base);
        if (uri.Scheme != "https" && !uri.IsLoopback)
            throw new InvalidOperationException("管理口令仅通过 HTTPS 或回环连接发送，请使用已配置的安全服务端地址。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, _base);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (body.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("管理验证响应无效。");
    }

    public async Task<string> DownloadAsync(AndroidUpdateRelease release, string privateDirectory,
        IAndroidUpdatePackageInspector inspector, CancellationToken ct)
    {
        ValidateRelease(release);
        Directory.CreateDirectory(privateDirectory);
        var partial = Path.Combine(privateDirectory, "candidate.partial");
        var destination = Path.Combine(privateDirectory, "candidate.apk");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var headers = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            headers.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await _http.GetAsync(_base + "/" + release.Id + "/package" + _query, HttpCompletionOption.ResponseHeadersRead, headers.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Package.SizeBytes)
                throw new InvalidDataException("APK 下载长度与发布快照不一致。");
            await using (var input = await response.Content.ReadAsStreamAsync(budget.Token))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new byte[81920];
                long total = 0;
                while (true)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                    idle.CancelAfter(TimeSpan.FromSeconds(30));
                    var count = await input.ReadAsync(bytes, idle.Token);
                    if (count == 0) break;
                    total += count;
                    if (total > release.Package.SizeBytes) throw new InvalidDataException("APK 下载超出发布大小。");
                    await output.WriteAsync(bytes.AsMemory(0, count), budget.Token);
                }
                if (total != release.Package.SizeBytes) throw new InvalidDataException("APK 下载被截断。");
                output.Flush(true);
            }
            await using (var input = File.OpenRead(partial))
                if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input, budget.Token)), release.Package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("APK 下载摘要不一致。");
            var actual = await inspector.InspectAsync(partial, budget.Token);
            if (!SamePackage(actual, release.Package)) throw new InvalidDataException("APK 真实内容与发布元数据不一致。");
            File.Move(partial, destination, true);
            return destination;
        }
        finally { File.Delete(partial); }
    }

    public async Task ReportAsync(AndroidUpdateStatus status, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _http.PostAsJsonAsync(_base + "/status", status, timeout.Token);
        response.EnsureSuccessStatusCode();
    }

    public static bool SamePackage(AndroidUpdatePackage a, AndroidUpdatePackage b) => a.PackageName == b.PackageName
        && a.VersionCode == b.VersionCode && a.VersionName == b.VersionName && a.MinSdk == b.MinSdk && a.TargetSdk == b.TargetSdk
        && a.Abis.Order(StringComparer.Ordinal).SequenceEqual(b.Abis.Order(StringComparer.Ordinal))
        && string.Equals(a.PublisherSha256, b.PublisherSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase) && a.SizeBytes == b.SizeBytes && a.Compatibility == b.Compatibility;

    private void ValidateRelease(AndroidUpdateRelease release)
    {
        if (!Guid.TryParseExact(release.Id, "N", out _) || release.Mode != "confirm" || release.Paused
            || release.Package is not { } p || p.PackageName != AndroidUpdateContract.PackageName
            || p.VersionCode <= 0 || p.SizeBytes is <= 0 or > AndroidUpdateContract.MaxPackageBytes
            || !AndroidUpdateContract.IsDigest(p.Sha256) || !AndroidUpdateContract.IsDigest(p.PublisherSha256)
            || p.Abis is not { Length: > 0 } || p.Abis.Any(a => !AndroidUpdateContract.IsAbi(a)) || p.Compatibility is null)
            throw new InvalidDataException("更新发布元数据无效。");
        if (p.VersionCode <= _identity.CurrentVersionCode || p.MinSdk > _identity.Sdk || !p.Abis.Contains(_identity.Abi, StringComparer.Ordinal)
            || !string.Equals(p.PublisherSha256, _identity.PublisherSha256, StringComparison.OrdinalIgnoreCase)
            || release.Devices is null || (release.Devices.Length != 0 && !release.Devices.Contains(_identity.DeviceId, StringComparer.Ordinal)))
            throw new InvalidDataException("更新发布不适用于本机身份或系统版本。");
    }

    public void Dispose() => _http.Dispose();
    private sealed record CandidateEnvelope(AndroidUpdateRelease? Release);
    private sealed record ServerInfo(string? Version);
}
