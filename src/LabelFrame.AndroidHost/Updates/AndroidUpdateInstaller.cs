using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Content.PM;
using LabelFrame.AndroidHost.Transport.Plugins;
using LabelFrame.Core.Jobs;
using LabelFrame.Updates;

namespace LabelFrame.AndroidHost.Updates;

/// <summary>协调系统安装会话、持久冻结与新进程健康恢复。</summary>
public sealed class AndroidUpdateInstaller(Context context, LabelHostConfig config, PluginHostState plugins,
    ILabelJobStore store, RouteJournal routes, CancellationToken lifetime)
{
    public static InstallTransaction Journal { get; private set; } = null!;
    public static AndroidUpdateInstaller? Current { get; private set; }
    private static long _processVersion;
    private static Intent? _confirmation;
    private static string? _confirmationId;
    public static bool HasConfirmation => _confirmation is not null && Journal.Current is { Phase: "confirmation" } state && state.Id == _confirmationId;

    public static void InitializeProcess(Context context)
    {
        Journal = new InstallTransaction(Path.Combine(context.FilesDir!.AbsolutePath, "install-transaction.json"), MaintenanceGate.Shared);
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
            _processVersion = context.PackageManager!.GetPackageInfo(AndroidUpdateContract.PackageName, 0)!.LongVersionCode;
    }

    public void Activate() => Current = this;
    public void Deactivate() { if (ReferenceEquals(Current, this)) Current = null; }
    public bool HasUnknownRoute => routes.ClaimUncertain;
    public RecoveryAudit.Receipt? RouteAcknowledgement => routes.Acknowledgement;

    public async Task RecoverManuallyAsync(string? transactionId, string token, string reason, Func<bool> isForeground, CancellationToken ct)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28)) throw new InvalidOperationException("受控升级需要 Android 9 或更高版本。");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 512) throw new ArgumentException("请填写 1～512 字的人工恢复原因。");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        ct = budget.Token;
        using var operation = await UpdateOperation.Shared.EnterAsync(ct);
        if (!ReferenceEquals(Current, this) || !isForeground()) throw new InvalidOperationException("请在当前服务的前台更新页确认恢复。");
        var identity = DevicePackageInspector.Identity(context, config.DeviceId);
        using var client = new UpdateDownloadClient(config.ServerUrl, identity);
        await client.VerifyAdministratorAsync(token, ct);
        if (!isForeground()) throw new InvalidOperationException("页面已离开前台，请重新确认。");
        if (transactionId is null)
        {
            if (Journal.NeedsRecovery || !await MaintenanceSafety.TryFreezeForRouteRecoveryAsync(MaintenanceGate.Shared, store, routes, ct))
                throw new InvalidOperationException("仍有作业、待回报、配置编辑或安装事务，不能人工解除路由阻断。");
            try { routes.AcknowledgeUncertainty(reason); }
            finally { MaintenanceGate.Shared.ReleaseAfterResolution(); }
        }
        else
        {
            if (Journal.Current is not { } state || state.Id != transactionId || !Journal.NeedsRecovery
                || !MaintenanceGate.Shared.IsFrozen || routes.HasOutstanding || !await MaintenanceSafety.IsQueueIdleAsync(store, ct))
                throw new InvalidOperationException("事务已变化或仍有本地作业、未决路由，不能人工恢复。");
            if (state.Phase is not ("unknown" or "confirmation" or "repair" or "rejected"))
                throw new InvalidOperationException("安装或自动核验仍在进行，请等待结果。");
            var installer = context.PackageManager!.PackageInstaller!;
            if (state.SessionId is { } id && installer.GetSessionInfo(id) is not null) installer.AbandonSession(id);
            identity = DevicePackageInspector.Identity(context, config.DeviceId);
            if (identity.CurrentVersionCode != _processVersion) throw new InvalidOperationException("应用版本已变化，请重新打开应用后处理。");
            Journal.ResumeManually(transactionId, identity.CurrentVersionCode, identity.PublisherSha256,
                state.SessionId is null || installer.GetSessionInfo(state.SessionId.Value) is null, reason);
            _confirmation = null;
            _confirmationId = null;
        }
        HostLog.Warn(HostLog.Tags.Host, "管理员人工恢复维护状态，原异常已存入 recovery-audit；不是升级成功证明。");
    }

    public async Task PrepareAsync(Func<bool> isForeground, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
        budget.CancelAfter(TimeSpan.FromMinutes(5));
        ct = budget.Token;
        if (!OperatingSystem.IsAndroidVersionAtLeast(28)) throw new InvalidOperationException("受控升级需要 Android 9 或更高版本。");
        using var operation = await UpdateOperation.Shared.EnterAsync(ct);
        if (!ReferenceEquals(Current, this)) throw new InvalidOperationException("打印服务代际已变化，请重新确认更新。");
        if (!isForeground()) throw new InvalidOperationException("请在前台更新页确认安装。");
        if (!context.PackageManager!.CanRequestPackageInstalls()) throw new InvalidOperationException("请先在系统设置中显式允许此应用安装更新。");
        var candidate = AndroidUpdateMonitor.Current;
        if (candidate is not { Phase: "ready", Release: { } release, PackagePath: { } path })
            throw new InvalidOperationException("尚无已校验的更新候选。");
        if (!await MaintenanceSafety.TryFreezeAsync(MaintenanceGate.Shared, store, routes, ct))
            throw new InvalidOperationException("仍有作业、未决路由、配置编辑或维护事务，暂不能安装。");
        var began = false;
        try
        {
            var identity = DevicePackageInspector.Identity(context, config.DeviceId);
            var baseline = CaptureBaseline();
            if (!InstallTransaction.SameBaseline(baseline, baseline) || plugins.Registry.GetPlugin(config.PrinterBrand) is null)
                throw new InvalidDataException("当前插件未完整加载或品牌已回退，不能建立健康升级基线。");
            using var client = new UpdateDownloadClient(config.ServerUrl, identity);
            var inspector = new DevicePackageInspector(context, identity);
            var actual = await inspector.InspectAsync(path, ct);
            if (!UpdateDownloadClient.SamePackage(actual, release.Package)) throw new InvalidDataException("安装前 APK 独立复验不匹配。");
            var latest = await client.CandidateAsync(ct);
            if (latest?.Id != release.Id || !UpdateDownloadClient.SamePackage(latest.Package, actual))
                throw new InvalidDataException("发布已暂停、替换或本设备不再有安装资格。");
            if (!isForeground()) throw new InvalidOperationException("页面已离开前台，请重新确认。");
            var transaction = Journal.Begin(release.Id, identity.CurrentVersionCode, actual, baseline);
            began = true;
            var installer = context.PackageManager.PackageInstaller!;
            using var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
            parameters.SetAppPackageName(AndroidUpdateContract.PackageName);
            parameters.SetSize(actual.SizeBytes);
            if (OperatingSystem.IsAndroidVersionAtLeast(31)) parameters.SetRequireUserAction((int)PackageInstallUserAction.Required);
            var sessionId = installer.CreateSession(parameters);
            Journal.SessionCreated(sessionId);
            using var session = installer.OpenSession(sessionId)!;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = session.OpenWrite("base.apk", 0, actual.SizeBytes)!)
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) != 0)
                {
                    total += count;
                    if (total > actual.SizeBytes) throw new InvalidDataException("安装写入超出已校验大小。");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                if (total != actual.SizeBytes || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), actual.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("写入系统会话的 APK 与已验签内容不一致。");
                session.Fsync(output);
            }
            latest = await client.CandidateAsync(ct);
            if (latest?.Id != release.Id || !UpdateDownloadClient.SamePackage(latest.Package, actual) || !isForeground())
                throw new InvalidOperationException("安装提交前发布资格或前台确认已失效。");
            ct.ThrowIfCancellationRequested();
            var result = new Intent(context, typeof(UpdateInstallReceiver));
            result.SetAction("com.labelframe.androidhost.INSTALL_RESULT." + transaction.Id);
            result.PutExtra("transactionId", transaction.Id);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(31)) flags |= PendingIntentFlags.Mutable;
            using var pending = PendingIntent.GetBroadcast(context, sessionId, result, flags)!;
            Journal.BeforeCommit();
            // 提交之后任何异常都可能是结果未知，不能自动解除冻结。
            session.Commit(pending.IntentSender);
        }
        catch
        {
            if (!began && !Journal.NeedsRecovery) MaintenanceGate.Shared.ReleaseAfterResolution();
            if (Journal.Current is { Phase: "preparing" }) TryCancelUncommitted();
            throw;
        }
    }

    public static bool ShowConfirmation(Activity activity)
    {
        if (_confirmation is null || Journal.Current is not { Phase: "confirmation" } state || state.Id != _confirmationId) return false;
        activity.StartActivity(_confirmation);
        _confirmation = null;
        return true;
    }

    public async Task RetryHealthAsync(CancellationToken ct)
    {
        lifetime.ThrowIfCancellationRequested();
        using var operation = await UpdateOperation.Shared.EnterAsync(ct);
        if (_processVersion != Journal.Current?.TargetVersion) throw new InvalidOperationException("必须在目标版本的新进程中重试健康核验。");
        Journal.RetryHealth();
    }

    public static void ReceiveResult(Intent intent)
    {
        var id = intent.GetStringExtra("transactionId") ?? "";
        var sessionId = intent.GetIntExtra(PackageInstaller.ExtraSessionId, -1);
        var status = intent.GetIntExtra(PackageInstaller.ExtraStatus, int.MinValue);
        var phase = status switch { -1 => "confirmation", 0 => "recovering", >= 1 and <= 8 => "rejected", _ => "unknown" };
        if (!Journal.AcceptResult(id, sessionId, phase, intent.GetStringExtra(PackageInstaller.ExtraStatusMessage))) return;
        if (phase == "confirmation")
        {
#pragma warning disable CA1422
            _confirmation = intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CA1422
            _confirmationId = id;
        }
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28)) return;
        while (!ct.IsCancellationRequested)
        {
            using (await UpdateOperation.Shared.EnterAsync(ct))
            {
                var state = Journal.Current;
                if (Journal.Fault is null && state is not null && Journal.NeedsRecovery)
                {
                    try
                    {
                        if (state.Phase == "preparing") TryCancelUncommitted();
                        else if (state.Phase == "rejected")
                        {
                            var installer = context.PackageManager!.PackageInstaller!;
                            if (state.SessionId is { } sessionId && installer.GetSessionInfo(sessionId) is not null) installer.AbandonSession(sessionId);
                            Journal.CompleteCancellation(DevicePackageInspector.Identity(context, config.DeviceId).CurrentVersionCode,
                                state.SessionId is not null && installer.GetSessionInfo(state.SessionId.Value) is null);
                        }
                        else if (_processVersion == state.TargetVersion && state.Phase != "repair")
                        {
                            var identity = DevicePackageInspector.Identity(context, config.DeviceId);
                            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
                                { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 65536 };
                            var body = await http.GetStringAsync($"http://127.0.0.1:{LabelHostConfig.LocalPort}/healthz", ct);
                            using var health = JsonDocument.Parse(body);
                            var apiHealthy = health.RootElement.GetProperty("service").GetString() == "LabelFrame.AndroidHost"
                                && health.RootElement.GetProperty("status").GetString() == "ok"
                                && health.RootElement.GetProperty("version").GetString() == HostInfo.GetVersion(context);
                            var routeHealthy = false;
                            if (state.Original.ServerUrl.Length > 0)
                            {
                                using var poller = new ServerPoller(state.Original.ServerUrl, state.Original.DeviceId, config.DeviceName);
                                await poller.RegisterAsync(ct);
                                routeHealthy = !routes.HasOutstanding;
                            }
                            Journal.CompleteHealth(identity.CurrentVersionCode, identity.PublisherSha256, CaptureBaseline(), apiHealthy,
                                plugins.Registry.GetPlugin(config.PrinterBrand) is not null, routeHealthy);
                        }
                        else if (state.Phase is "submitted" or "confirmation" or "recovering"
                            && DateTimeOffset.UtcNow - state.UpdatedUtc > TimeSpan.FromMinutes(5))
                            Journal.AcceptResult(state.Id, state.SessionId ?? -1, "unknown", "安装超过等待时间且未进入目标版本新进程，保持冻结并等待真实系统结果。");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        HostLog.Warn(HostLog.Tags.Host, "更新恢复核验失败，保持冻结：" + error.Message);
                        if (Journal.Fault is null) Journal.RecoveryFailed(error.Message);
                    }
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }

    private void TryCancelUncommitted()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28) || Journal.Current is not { Phase: "preparing" } state) return;
        var installer = context.PackageManager!.PackageInstaller!;
        if (state.SessionId is { } id && installer.GetSessionInfo(id) is not null) installer.AbandonSession(id);
        Journal.CompleteCancellation(DevicePackageInspector.Identity(context, config.DeviceId).CurrentVersionCode,
            state.SessionId is null || installer.GetSessionInfo(state.SessionId.Value) is null);
    }

    private InstallTransaction.Baseline CaptureBaseline()
    {
        var current = LabelHostConfig.Load(context);
        var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(current)));
        var installed = plugins.Installer.ListInstalled();
        if (installed.Count > 100) throw new InvalidDataException("插件数量超过健康核验上限。");
        var facts = new List<InstallTransaction.Plugin>();
        long remaining = 256 * 1024 * 1024;
        foreach (var plugin in installed)
        {
            var path = plugin.PackageDir is null ? plugins.Registry.GetPlugin(plugin.PluginId)?.AssemblyPath
                ?? Path.Combine(plugins.PluginsPath, plugin.PluginId + ".dll") : Path.Combine(plugins.PluginsPath, plugin.PackageDir);
            facts.Add(new(plugin.PluginId, plugin.Version, HashPluginFiles(path, ref remaining), plugin.Loaded && plugin.LoadError is null));
        }
        return new(digest, current.ServerUrl, current.DeviceId, current.PrinterBrand, facts.ToArray());
    }

    private static string HashPluginFiles(string path, ref long remaining)
    {
        var files = Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray() : [path];
        if (files.Length > 10000) throw new InvalidDataException("插件文件数超过健康核验上限。");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            if ((remaining -= new FileInfo(file).Length) < 0) throw new InvalidDataException("插件总大小超过健康核验上限。");
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(path, file) + "\n"));
            using var input = File.OpenRead(file);
            hash.AppendData(SHA256.HashData(input));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

/// <summary>接收专属系统安装回执但不从后台启动确认界面。</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class UpdateInstallReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        var pending = GoAsync();
        _ = Task.Run(() =>
        {
            try { AndroidUpdateInstaller.ReceiveResult(intent); }
            catch (Exception error) { HostLog.Warn(HostLog.Tags.Host, "安装回执处理失败，保持冻结：" + error.Message); }
            finally { pending?.Finish(); }
        });
    }
}
