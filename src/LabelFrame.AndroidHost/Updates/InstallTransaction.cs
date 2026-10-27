using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Updates;

namespace LabelFrame.AndroidHost.Updates;

/// <summary>持久保存安装提交边界和升级恢复所需的原始事实。</summary>
public sealed class InstallTransaction
{
    public sealed record Plugin(string Id, string Version, string Digest, bool Loaded);
    public sealed record Baseline(string ConfigDigest, string ServerUrl, string DeviceId, string Brand, Plugin[] Plugins);
    public sealed record State(int Format, string Id, string ReleaseId, long OriginalVersion, long TargetVersion,
        string PackageDigest, string PublisherDigest, Baseline Original, string Phase, int? SessionId,
        DateTimeOffset UpdatedUtc, string? Reason, RecoveryAudit.Receipt? ManualRecovery = null);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly object _sync = new();
    private readonly string _path;
    private readonly MaintenanceGate _gate;
    private State? _current;
    private string? _fault;
    public State? Current { get { lock (_sync) return _current; } }
    public string? Fault { get { lock (_sync) return _fault; } }
    public bool NeedsRecovery { get { lock (_sync) return _fault is not null || _current is { Phase: not ("succeeded" or "cancelled" or "manually-resumed") }; } }

    public InstallTransaction(string path, MaintenanceGate gate)
    {
        _path = path;
        _gate = gate;
        try
        {
            if (File.Exists(path + ".tmp") || Directory.Exists(path + ".tmp") || Directory.Exists(path))
                throw new InvalidDataException("安装事务有未完成写入或路径异常，保持维护冻结。");
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("安装事务超过读取上限。");
                _current = JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json)
                    ?? throw new InvalidDataException("安装事务为空。");
                Validate(_current);
            }
        }
        catch (Exception error) { _fault = error.Message; }
        if (NeedsRecovery) _gate.RestoreFrozen();
    }

    public State Begin(string releaseId, long originalVersion, AndroidUpdatePackage package, Baseline baseline)
    {
        lock (_sync)
        {
            if (!_gate.IsFrozen || NeedsRecovery) throw new InvalidOperationException("安装必须先冻结业务且无未决事务。");
            var next = new State(1, Guid.NewGuid().ToString("N"), releaseId, originalVersion, package.VersionCode,
                package.Sha256, package.PublisherSha256, baseline, "preparing", null, DateTimeOffset.UtcNow, null);
            Validate(next);
            Save(next);
            return next;
        }
    }

    public void SessionCreated(int sessionId) => Change(s => s.Phase == "preparing" && s.SessionId is null && sessionId >= 0
        ? s with { SessionId = sessionId } : throw new InvalidOperationException("安装会话不能替换。"));

    public void BeforeCommit() => Change(s => s.Phase == "preparing" && s.SessionId is not null
        ? s with { Phase = "submitted" } : throw new InvalidOperationException("安装尚未准备完成。"));

    public bool AcceptResult(string id, int sessionId, string phase, string? reason)
    {
        lock (_sync)
        {
            if (_fault is not null || _current is not { } s || s.Id != id || s.SessionId != sessionId
                || s.Phase is "succeeded" or "cancelled" or "manually-resumed" || phase is not ("confirmation" or "recovering" or "unknown" or "rejected")) return false;
            // 迟到的待确认回调不能覆盖已收到的安装结果。
            if (phase == "confirmation" && s.Phase is not ("submitted" or "confirmation")) return false;
            Save(s with { Phase = phase, Reason = Limit(reason) });
            return true;
        }
    }

    public void RecoveryFailed(string reason) => Change(s => s with { Phase = "repair", Reason = Limit(reason) });
    public void RetryHealth() => Change(s => s.Phase == "repair"
        ? s with { Phase = "recovering", Reason = null } : throw new InvalidOperationException("当前不是健康核验失败状态。"));

    public void CompleteHealth(long actualVersion, string publisher, Baseline actual, bool apiHealthy, bool brandLoaded, bool routeHealthy)
    {
        lock (_sync)
        {
            var s = RequireActive();
            if (s.Phase == "preparing" || actualVersion != s.TargetVersion
                || !string.Equals(publisher, s.PublisherDigest, StringComparison.OrdinalIgnoreCase)
                || !SameBaseline(s.Original, actual) || !apiHealthy || !brandLoaded
                || (s.Original.ServerUrl.Length > 0 && !routeHealthy))
                throw new InvalidDataException("升级后版本、签名、API、原配置、插件或原服务端恢复核验未通过。");
            Save(s with { Phase = "succeeded", Reason = null });
            _gate.ReleaseAfterResolution();
        }
    }

    public void CompleteCancellation(long actualVersion, bool sessionGone)
    {
        lock (_sync)
        {
            var s = RequireActive();
            if (!sessionGone || actualVersion != s.OriginalVersion || s.Phase is not ("preparing" or "rejected"))
                throw new InvalidOperationException("系统会话仍存在或实际版本已变化，不能解除维护冻结。");
            Save(s with { Phase = "cancelled", Reason = "已核实系统会话终止且原版本未变。" });
            _gate.ReleaseAfterResolution();
        }
    }

    public static bool SameBaseline(Baseline a, Baseline b) => a.ConfigDigest == b.ConfigDigest
        && a.ServerUrl == b.ServerUrl && a.DeviceId == b.DeviceId && a.Brand == b.Brand
        && a.Plugins.All(p => p.Loaded) && b.Plugins.All(p => p.Loaded)
        && a.Plugins.OrderBy(p => p.Id, StringComparer.Ordinal).SequenceEqual(b.Plugins.OrderBy(p => p.Id, StringComparer.Ordinal));

    public void ResumeManually(string expectedId, long actualVersion, string publisher, bool sessionGone, string reason)
    {
        lock (_sync)
        {
            var s = RequireActive();
            if (s.Id != expectedId || !sessionGone || s.Phase is not ("unknown" or "confirmation" or "repair" or "rejected")
                || (actualVersion != s.OriginalVersion && actualVersion != s.TargetVersion)
                || !string.Equals(publisher, s.PublisherDigest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("安装仍在进行、事务已变化或本机身份异常，不能人工恢复。");
            var receipt = RecoveryAudit.Save(_path, reason, s);
            Save(s with { Phase = "manually-resumed", ManualRecovery = receipt, Reason = "管理员接受风险后恢复，未核验升级成功。" });
            _gate.ReleaseAfterResolution();
        }
    }

    private void Change(Func<State, State> change) { lock (_sync) Save(change(RequireActive())); }
    private State RequireActive() => _fault is null && _current is { Phase: not ("succeeded" or "cancelled" or "manually-resumed") } s
        ? s : throw new InvalidOperationException("没有可处理的安装事务或事务存储故障。");
    private static string? Limit(string? text) => text is { Length: > 1024 } ? text[..1024] : text;
    private static void Validate(State s)
    {
        if (s.Format != 1 || !Guid.TryParseExact(s.Id, "N", out _) || !Guid.TryParseExact(s.ReleaseId, "N", out _)
            || s.OriginalVersion <= 0 || s.TargetVersion <= s.OriginalVersion || !AndroidUpdateContract.IsDigest(s.PackageDigest)
            || !AndroidUpdateContract.IsDigest(s.PublisherDigest) || s.Original is not { Plugins: not null } b
            || !AndroidUpdateContract.IsDigest(b.ConfigDigest) || string.IsNullOrWhiteSpace(b.DeviceId)
            || b.ServerUrl is null || string.IsNullOrWhiteSpace(b.Brand) || b.Plugins.Length > 100
            || b.Plugins.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || p.Version is null || !AndroidUpdateContract.IsDigest(p.Digest))
            || b.Plugins.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != b.Plugins.Length
            || s.Phase is not ("preparing" or "submitted" or "confirmation" or "recovering" or "unknown" or "rejected" or "repair" or "succeeded" or "cancelled" or "manually-resumed")
            || (s.Phase == "manually-resumed" && s.ManualRecovery is null)
            || s.SessionId is < 0 || (s.Phase is "submitted" or "confirmation" or "recovering" or "succeeded" && s.SessionId is null)
            || s.UpdatedUtc == default)
            throw new InvalidDataException("安装事务格式或恢复事实无效。");
    }

    private void Save(State next)
    {
        if (_fault is not null) throw new IOException("安装事务存储故障，不能继续提交或解除冻结。");
        try
        {
            next = next with { UpdatedUtc = DateTimeOffset.UtcNow };
            Validate(next);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
            if (bytes.Length > 256 * 1024) throw new InvalidDataException("安装事务超过写入上限。");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            using (var stream = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(_path + ".tmp", _path, true);
            _current = next;
        }
        catch (Exception error)
        {
            _fault = error.Message;
            _gate.RestoreFrozen();
            throw;
        }
    }
}
