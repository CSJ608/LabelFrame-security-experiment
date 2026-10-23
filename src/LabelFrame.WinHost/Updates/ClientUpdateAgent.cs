using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Updates;
using LabelFrame.WinHost.Routing;
using LabelFrame.WinHost.Transport;
using LabelFrame.WinHost.Ui;

namespace LabelFrame.WinHost.Updates;

/// <summary>后台检查、下载、维护安装与真实启动恢复的客户端更新代理。</summary>
internal sealed class ClientUpdateAgent(HostOptions options, HostActivityState activity, HostRestartCoordinator restart,
    LabelJobQueue queue, ITransportManager transport, PluginInstaller plugins, ServerRoutingCoordinator routing,
    ILogger<ClientUpdateAgent> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(45) };
    private string DataDirectory => Path.Combine(Path.GetDirectoryName(options.DatabasePath)!, "updates");
    private string JournalPath => Path.Combine(DataDirectory, "recovery.json");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    private string FailedPath => Path.Combine(DataDirectory, "failed-release.json");
    private ClientUpdateSettings _settings = new();
    private ClientUpdateStatus _status = new(options.DeviceId, HostOptions.ProductVersion, "starting");
    private ClientUpdateRelease? _candidate;
    private string? _failedRelease;
    private string[] _arguments = [];
    private Func<bool> _visible = () => true;
    private DateTimeOffset _nextCheck;
    private DateTimeOffset? _checkedAt;
    private int _failures;
    private RecoveryJournal? _recovery;
    public ClientUpdateSettings Settings => _settings;
    public ClientUpdateStatus Status => _status;
    public void Bind(string[] arguments, Func<bool> visible) { _arguments = arguments; _visible = visible; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(DataDirectory);
        _nextCheck = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(5, 61));
        try { if (File.Exists(FailedPath)) _failedRelease = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(FailedPath, ct), Json); }
        catch (Exception ex) when (ex is IOException or JsonException)
        { _failedRelease = "invalid"; _settings = new("notify"); SetStatus("failed", "更新失败记录损坏，请管理员检查后重新保存设置。"); }
        try
        {
            if (File.Exists(SettingsPath)) _settings = JsonSerializer.Deserialize<ClientUpdateSettings>(await File.ReadAllTextAsync(SettingsPath, ct), Json)!;
            _settings.Validate();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or NullReferenceException)
        {
            _settings = new("notify");
            SetStatus("failed", "更新设置损坏，已改为仅提醒，请重新保存更新方式。");
        }
        if (!File.Exists(JournalPath)) return;
        activity.BlockForRecovery(true);
        if (!await queue.TryPrepareForShutdownAsync(ct)) throw new InvalidDataException("更新恢复时发现未收尾打印，禁止自动继续，请管理员检查。");
        try
        {
            _recovery = JsonSerializer.Deserialize<RecoveryJournal>(await File.ReadAllTextAsync(JournalPath, ct), Json)
                ?? throw new InvalidDataException("更新恢复记录为空。");
            if (!Guid.TryParseExact(_recovery.TransactionId, "N", out _)) throw new InvalidDataException("更新事务标识无效。");
            if (_recovery.Parameters is null || _recovery.Plugins is null || _recovery.ServerUrl is null || _recovery.PluginId is null
                || !Guid.TryParseExact(_recovery.ReleaseId, "N", out _)) throw new InvalidDataException("更新恢复快照无效。");
            SetStatus("recovering", "正在核对实际版本、插件、连接和路由注册。", _recovery.TargetVersion, _recovery.TransactionId);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        { _recovery = null; SetStatus("repair-required", "更新恢复记录损坏，已暂停打印，请管理员修复。"); activity.AllowRecoveryRestart(); }
    }

    public async Task SaveSettingsAsync(ClientUpdateSettings settings, CancellationToken ct)
    {
        settings.Validate();
        await _gate.WaitAsync(ct);
        try { await SaveJsonAsync(SettingsPath, settings, ct); _settings = settings; _nextCheck = DateTimeOffset.MinValue; }
        finally { _gate.Release(); }
    }

    public async Task CheckAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await CheckCoreAsync(ct); }
        finally { _gate.Release(); }
    }

    private string ServerUrl => routing.CurrentServerUrl?.TrimEnd('/') ?? "";
    private string Query => $"deviceId={Uri.EscapeDataString(options.DeviceId)}&currentVersion={HostOptions.ProductVersion}";

    private async Task<ClientUpdateRelease?> FetchCandidateAsync(CancellationToken ct)
    {
        _checkedAt = DateTimeOffset.UtcNow;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        ct = timeout.Token;
        using var response = await _http.GetAsync($"{ServerUrl}/api/client-updates/candidate?{Query}", HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound || response.Content.Headers.ContentType?.MediaType != "application/json")
            throw new NotSupportedException("服务端不支持受控更新，请先升级服务端；仍可手工下载安装包。");
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[131073];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await body.ReadAsync(buffer.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > 131072) throw new InvalidDataException("更新发布响应过大。");
        var candidate = JsonSerializer.Deserialize<CandidateResponse>(buffer.AsSpan(0, count), Json)?.Release;
        if (candidate is null) return null;
        if (candidate.Package is null || candidate.Devices is null || !Guid.TryParseExact(candidate.Id, "N", out _) || candidate.Paused || candidate.FileName != $"LabelFrame-Client-{candidate.Package.Version}.msi"
            || candidate.Package.SizeBytes is <= 0 or > ClientUpdateContract.MaxPackageBytes || candidate.Package.Sha256.Length != 64
            || candidate.Package.RuntimeMajor != 10 || candidate.Package.Architecture != "x64"
            || ClientUpdateContract.ParseVersion(candidate.Package.Version) <= ClientUpdateContract.ParseVersion(HostOptions.ProductVersion)
            || (candidate.Devices.Length > 0 && !candidate.Devices.Contains(options.DeviceId, StringComparer.Ordinal)))
            throw new InvalidDataException("服务端更新元数据不适用于此客户端。");
        return candidate;
    }

    private async Task CheckCoreAsync(CancellationToken ct)
    {
        if (activity.IsPrepared) return;
        if (ServerUrl.Length == 0) { _candidate = null; SetStatus("disconnected", "配置并连接服务端后才能检查更新。"); return; }
        try
        {
            var candidate = await FetchCandidateAsync(ct);
            _candidate = candidate;
            _nextCheck = DateTimeOffset.UtcNow.AddMinutes(10).AddSeconds(Random.Shared.Next(60));
            if (candidate is null) { _failures = 0; SetStatus("up-to-date", "当前没有适用于此设备的已发布新版。"); return; }
            if (_failedRelease == candidate.Id || _failedRelease == "invalid") { SetStatus("failed", "此版本安装曾失败，请先处理原因，再手动重试。", candidate.Package.Version); return; }
            SetStatus("available", null, candidate.Package.Version);
            if (_settings.Mode != "notify") await DownloadAsync(candidate, ct);
            _failures = 0;
        }
        catch (NotSupportedException ex) { _candidate = null; SetStatus("unsupported", ex.Message); _nextCheck = DateTimeOffset.UtcNow.AddMinutes(30); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _candidate = null;
            _failures++;
            SetStatus("offline", "检查或下载失败，将自动重试；可检查网络和服务端发布状态。");
            _nextCheck = DateTimeOffset.UtcNow.AddMinutes(Math.Min(30, Math.Pow(2, Math.Min(_failures, 5)))).AddSeconds(Random.Shared.Next(30));
            logger.LogWarning("客户端更新检查失败：{Reason}", ex.Message);
        }
    }

    private string PackagePath(ClientUpdateRelease candidate) => Path.Combine(DataDirectory, candidate.Id + ".msi");
    private async Task DownloadAsync(ClientUpdateRelease candidate, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        ct = timeout.Token;
        var path = PackagePath(candidate);
        if (File.Exists(path))
        {
            await using var cached = File.OpenRead(path);
            if (new FileInfo(path).Length == candidate.Package.SizeBytes
                && string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(cached, ct)), candidate.Package.Sha256, StringComparison.OrdinalIgnoreCase))
            { SetStatus("ready", null, candidate.Package.Version); return; }
            await cached.DisposeAsync();
            File.Delete(path);
        }
        var temporary = path + ".tmp";
        try
        {
            using var response = await _http.GetAsync($"{ServerUrl}/api/client-updates/{candidate.Id}/package?{Query}", HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[65536];
                long total = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, ct);
                    if (count == 0) break;
                    total += count;
                    if (total > candidate.Package.SizeBytes) throw new InvalidDataException("下载包超出发布大小。");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    _status = new(options.DeviceId, HostOptions.ProductVersion, "downloading", candidate.Package.Version,
                        CheckedAt: _checkedAt, DownloadedBytes: total, TotalBytes: candidate.Package.SizeBytes);
                }
            }
            var package = await new WindowsPackageInspector(ClientUpdateContract.CertificatePath).InspectAsync(temporary, candidate.FileName, ct);
            if (package != candidate.Package) throw new InvalidDataException("下载包与发布元数据不一致。");
            File.Move(temporary, path, true);
            foreach (var old in Directory.EnumerateFiles(DataDirectory, "*.msi").Where(old => old != path)) File.Delete(old);
            SetStatus("ready", null, candidate.Package.Version);
        }
        finally { File.Delete(temporary); }
    }

    public async Task<string?> InstallAsync(bool automatic, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (automatic && ClientUpdatePolicy.PostponeReason(_settings, TimeOnly.FromDateTime(DateTime.Now), _visible(),
                activity.LastInteraction, DateTimeOffset.UtcNow) is { } postponed) return Defer(postponed);
            var candidate = _candidate;
            if (candidate is null) return "请先检查更新。";
            if (automatic && _failedRelease == candidate.Id) return "此版本已失败，等待人工处理。";
            if (!automatic) { _failedRelease = null; File.Delete(FailedPath); }
            await DownloadAsync(candidate, ct);
            var current = await FetchCandidateAsync(ct);
            if (current != candidate && (current is null || current.Id != candidate.Id || current.Package != candidate.Package))
                return Defer("发布已暂停或更新，请重新检查。");
            var preparing = false;
            var reason = await restart.RequestUpdateAsync(async () =>
            {
                preparing = true;
                ProtectedUpdatePaths.Verify(ProtectedUpdatePaths.Executable);
                var snapshot = plugins.ListInstalled();
                if (snapshot.Any(p => !p.Loaded || p.LoadError is not null)) throw new InvalidDataException("已有插件未加载，请修复后再安装客户端更新。");
                using var pipe = new NamedPipeClientStream(".", ClientUpdateContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                await pipe.ConnectAsync(5000, timeout.Token);
                UpdaterPipeIdentity.Verify(pipe.SafePipeHandle);
                using var parent = Process.GetCurrentProcess();
                await UpdatePipeProtocol.WriteAsync(pipe, new UpdateInstallRequest(candidate.FileName, candidate.Package.Sha256,
                    candidate.Package.SizeBytes, parent.StartTime.ToUniversalTime().Ticks), timeout.Token);
                await using (var package = File.OpenRead(PackagePath(candidate))) await package.CopyToAsync(pipe, timeout.Token);
                await pipe.FlushAsync(timeout.Token);
                var accepted = await UpdatePipeProtocol.ReadAsync<UpdateInstallResponse>(pipe, timeout.Token);
                if (!accepted.Accepted || !Guid.TryParseExact(accepted.TransactionId, "N", out _)) throw new InvalidDataException(accepted.Reason ?? "安装服务未接受升级。");
                var config = transport.CurrentConfig;
                var journal = new RecoveryJournal(accepted.TransactionId!, candidate.Id, HostOptions.ProductVersion, candidate.Package.Version,
                    config.PluginId, config.Params, snapshot.Where(p => p.Loaded).Select(p => p.PluginId).ToArray(), ServerUrl);
                await SaveJsonAsync(JournalPath, journal, timeout.Token);
                await UpdateSupervisor.PrepareRelaunchAsync(accepted.TransactionId!, _arguments, _visible(), DataDirectory, timeout.Token);
                try
                {
                    using var commitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await UpdatePipeProtocol.WriteAsync(pipe, new UpdateCommitRequest(accepted.TransactionId!), commitTimeout.Token);
                    await UpdatePipeProtocol.ReadAsync<UpdateInstallResponse>(pipe, commitTimeout.Token);
                }
                catch (Exception ex)
                {
                    // 不变量：提交意图后保持业务冻结并交给已就绪的原用户等待器恢复。
                    logger.LogWarning("安装提交确认未收到，保留恢复事务：{Reason}", ex.Message);
                }
                SetStatus("installing", null, candidate.Package.Version, accepted.TransactionId);
                using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ReportAsync(reportTimeout.Token);
            }, ct);
            if (reason is not null)
            {
                if (!preparing) return Defer(reason);
                _failedRelease = candidate.Id;
                await SaveJsonAsync(FailedPath, candidate.Id, CancellationToken.None);
                SetStatus("failed", reason, candidate.Package.Version);
                return reason;
            }
            return null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _failedRelease = _candidate?.Id;
            if (_failedRelease is not null) await SaveJsonAsync(FailedPath, _failedRelease, CancellationToken.None);
            SetStatus("failed", "安装准备失败，客户端继续运行；请检查安装服务与日志后手动重试。", _candidate?.Package.Version);
            logger.LogWarning("客户端更新准备失败：{Reason}", ex.Message);
            return _status.Reason;
        }
        finally { _gate.Release(); }
    }

    private string Defer(string reason) { SetStatus("deferred", reason, _candidate?.Package.Version); return reason; }
    private void SetStatus(string phase, string? reason = null, string? target = null, string? transaction = null)
        => _status = new(options.DeviceId, HostOptions.ProductVersion, phase, target, reason, _checkedAt, transaction);
    private static async Task SaveJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(value, Json), ct);
        File.Move(path + ".tmp", path, true);
    }

    private async Task ReportAsync(CancellationToken ct)
    {
        if (ServerUrl.Length == 0) return;
        try { using var response = await _http.PostAsJsonAsync(ServerUrl + "/api/client-updates/status", _status, Json, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { logger.LogDebug("更新状态暂未送达：{Reason}", ex.Message); }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        if (_recovery is not { } recovery) return;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        try
        {
            var resultPath = Path.Combine(ProtectedUpdatePaths.Root, recovery.TransactionId, "result.json");
            var result = JsonSerializer.Deserialize<UpdateInstallResult>(await File.ReadAllTextAsync(resultPath, ct), Json)
                ?? throw new InvalidDataException("缺少安装结果。");
            if (result.TargetVersion != recovery.TargetVersion) throw new InvalidDataException("安装结果与恢复事务不匹配。");
            var installed = ClientUpdaterService.InstalledVersion();
            var expected = result.ExitCode is 0 or 3010 ? recovery.TargetVersion : recovery.OldVersion;
            if (HostOptions.ProductVersion != expected || installed != expected) throw new InvalidDataException("安装版本与实际运行版本不一致。");
            var config = transport.CurrentConfig;
            if (config.PluginId != recovery.PluginId || config.Params.Count != recovery.Parameters.Count
                || recovery.Parameters.Any(p => !config.Params.TryGetValue(p.Key, out var value) || value != p.Value))
                throw new InvalidDataException("更新后打印连接发生变化，禁止自动继续打印。");
            var installedPlugins = plugins.ListInstalled();
            if (installedPlugins.Any(p => !p.Loaded || p.LoadError is not null)
                || recovery.Plugins.Any(id => !installedPlugins.Any(p => p.PluginId == id && p.Loaded)))
                throw new InvalidDataException("更新后插件未全部恢复。");
            while (true)
            {
                try
                {
                    using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    probeTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    using var health = await _http.GetAsync(new Uri(new Uri(UiUrl.ToLocalUiUrl(options.ListenUrl)), "/healthz"), probeTimeout.Token);
                    health.EnsureSuccessStatusCode();
                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
                {
                    if (DateTimeOffset.UtcNow >= deadline) throw new InvalidDataException("更新后本机 API 尚未恢复。");
                    await Task.Delay(250, ct);
                }
            }
            while (recovery.ServerUrl.Length > 0 && (!routing.HasRegistered || ServerUrl != recovery.ServerUrl))
            {
                if (DateTimeOffset.UtcNow >= deadline) throw new InvalidDataException("更新后服务端路由注册尚未恢复。");
                await Task.Delay(250, ct);
            }
            if (result.ExitCode is not (0 or 3010))
            {
                _failedRelease = recovery.ReleaseId;
                await SaveJsonAsync(FailedPath, _failedRelease, ct);
            }
            File.Delete(JournalPath);
            _recovery = null;
            SetStatus(result.ExitCode is 0 or 3010 ? "completed" : "failed",
                result.ExitCode == 3010 ? "客户端已更新，系统提示稍后需重启操作系统；本程序不会自动重启系统。"
                    : result.ExitCode == 0 ? "更新及恢复检查通过。" : "安装失败，旧版已完整恢复；请管理员检查安装日志后重试。",
                recovery.TargetVersion, recovery.TransactionId);
            _nextCheck = DateTimeOffset.UtcNow.AddMinutes(30);
            await queue.CancelShutdownPreparationAsync(ct);
            activity.BlockForRecovery(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        { SetStatus("repair-required", "更新恢复检查失败，打印已暂停：" + ex.Message, recovery.TargetVersion, recovery.TransactionId); _recovery = null; activity.AllowRecoveryRestart(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000, stoppingToken);
        await RecoverAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!activity.IsPrepared)
            {
                if (DateTimeOffset.UtcNow >= _nextCheck) await CheckAsync(stoppingToken);
                if (_settings.Mode == "maintenance" && _candidate is not null && _status.Phase is "ready" or "deferred")
                    await InstallAsync(true, stoppingToken);
            }
            await ReportAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }

    public override void Dispose() { _http.Dispose(); _gate.Dispose(); base.Dispose(); }
    private sealed record CandidateResponse(ClientUpdateRelease? Release);
    private sealed record RecoveryJournal(string TransactionId, string ReleaseId, string OldVersion, string TargetVersion, string PluginId,
        Dictionary<string, string> Parameters, string[] Plugins, string ServerUrl);
}
