using Android.Content;
using LabelFrame.Updates;

namespace LabelFrame.AndroidHost.Updates;

/// <summary>在打印服务生命周期内后台检查和下载待确认更新。</summary>
public sealed class AndroidUpdateMonitor
{
    public sealed record Snapshot(string Phase, string? Reason = null, AndroidUpdateRelease? Release = null, string? PackagePath = null);
    private static Snapshot _current = new("checking");
    public static Snapshot Current => Volatile.Read(ref _current);

    public static async Task RunAsync(Context context, LabelHostConfig config, CancellationToken ct)
    {
        if (config.ServerUrl.Length == 0) { Set(new("unconfigured", "尚未配置更新服务端。")); return; }
        if (!OperatingSystem.IsAndroidVersionAtLeast(28)) { Set(new("unsupported", "受控升级需要 Android 9 或更高版本；请使用同签名手工升级。")); return; }
        try
        {
            var identity = DevicePackageInspector.Identity(context, config.DeviceId);
            var inspector = new DevicePackageInspector(context, identity);
            using var client = new UpdateDownloadClient(config.ServerUrl, identity);
            var directory = Path.Combine(context.CacheDir!.AbsolutePath, "android-update");
            string? lastId = null;
            var attempts = 0;
            var failures = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var operation = await UpdateOperation.Shared.EnterAsync(ct);
                    if (!MaintenanceGate.Shared.IsFrozen)
                    {
                        var release = await client.CandidateAsync(ct);
                        if (release is null)
                        {
                            Set(new("idle"));
                            UpdateDownloadClient.RemoveCandidate(directory);
                            lastId = null;
                            attempts = 0;
                        }
                        else
                        {
                            if (lastId != release.Id) { lastId = release.Id; attempts = 0; Set(new("checking", Release: release)); }
                            if (Current.Release?.Id == release.Id && Current.PackagePath is { } cached && File.Exists(cached))
                                Set(Current with { Phase = "ready", Reason = "后台下载已校验，等待前台确认安装。" });
                            else if (attempts < 3)
                            {
                                attempts++;
                                Set(new("downloading", Release: release));
                                var path = await client.DownloadAsync(release, directory, inspector, ct);
                                Set(new("ready", "后台下载已校验，等待前台确认安装。", release, path));
                            }
                            else Set(new("failed", "此发布下载或校验连续失败三次，已停止自动重试。", release));
                        }
                    }
                    failures = 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    failures = Math.Min(failures + 1, 4);
                    Set(Current with { Phase = "failed", Reason = error.Message, PackagePath = null });
                    HostLog.Warn(HostLog.Tags.Host, $"更新检查或下载失败：{error.Message}");
                }
                try
                {
                    var state = Current;
                    var transaction = AndroidUpdateInstaller.Journal;
                    var install = transaction.Current;
                    var report = UpdateStatusProjection.Create(identity.DeviceId, identity.CurrentVersionCode, state.Phase,
                        state.Reason, state.Release?.Package.VersionCode, install, transaction.Fault, transaction.NeedsRecovery);
                    await client.ReportAsync(report with { Reason = report.Reason is { Length: > 1024 } reason ? reason[..1024] : report.Reason }, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception error) { HostLog.Warn(HostLog.Tags.Host, $"更新状态回报失败：{error.Message}"); }
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 30 * Math.Pow(2, failures)) + Random.Shared.Next(0, 10)), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error) { Set(new("blocked", error.Message)); HostLog.Warn(HostLog.Tags.Host, $"更新检查无法启动：{error.Message}"); }
    }

    private static void Set(Snapshot value) => Volatile.Write(ref _current, value);
}
