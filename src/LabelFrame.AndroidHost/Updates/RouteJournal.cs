using System.Text.Json;
using LabelFrame.AndroidHost.Api;

namespace LabelFrame.AndroidHost.Updates;

/// <summary>持久保存路由领取边界、待提交载荷与待回报映射。</summary>
public sealed class RouteJournal
{
    private static readonly JsonSerializerOptions Json = new(HostJson.Options) { RespectRequiredConstructorParameters = true };
    private readonly object _sync = new();
    private readonly string _path;
    private State _state;
    private bool _claimStarted;
    private bool _uncertainBeforeClaim;
    private RecoveryAudit.Receipt? _acknowledgementBeforeClaim;
    private bool _storageFault;
    public sealed record Entry(PendingJob Job, string? LocalId = null, JobResult? Failure = null);
    public sealed record State(int Format, string ServerUrl, string DeviceId, bool ClaimUncertain, Entry[] Entries,
        RecoveryAudit.Receipt? Acknowledgement = null);

    public RouteJournal(string path, string serverUrl, string deviceId, bool legacyRouteStateUnknown = false)
    {
        _path = path;
        if (File.Exists(path + ".tmp")) throw new InvalidDataException("路由日志存在未完成写入，须核对原服务端领取状态。");
        if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024 * 1024)
            throw new InvalidDataException("路由日志超过安全读取上限。");
        _state = File.Exists(path)
            ? JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("路由日志为空，不能确认领取状态。")
            : new(1, serverUrl.TrimEnd('/'), deviceId, legacyRouteStateUnknown, []);
        if (_state.Format != 1 || _state.Entries is null || _state.Entries.Any(e => e.Job is null)
            || _state.Entries.Select(e => e.Job.JobId).Distinct().Count() != _state.Entries.Length)
            throw new InvalidDataException("路由日志损坏，不能确认领取状态。");
        if (_state.ServerUrl != serverUrl.TrimEnd('/') || _state.DeviceId != deviceId)
        {
            if (HasOutstanding)
                throw new InvalidDataException("原服务端仍有未决路由，请恢复原配置后处理，不能迁移回报对象。");
            Save(new(1, serverUrl.TrimEnd('/'), deviceId, false, []));
        }
        if (!File.Exists(path)) Save(_state);
    }

    public bool HasOutstanding { get { lock (_sync) return HasPendingWork || ClaimUncertain; } }
    public bool HasPendingWork { get { lock (_sync) return _storageFault || _claimStarted || _state.Entries.Length > 0; } }
    public bool ClaimUncertain { get { lock (_sync) return _storageFault || (_state.ClaimUncertain && _state.Acknowledgement is null); } }
    public RecoveryAudit.Receipt? Acknowledgement { get { lock (_sync) return _state.Acknowledgement; } }
    public Entry[] Snapshot() { lock (_sync) return [.. _state.Entries]; }

    public void AcknowledgeUncertainty(string reason)
    {
        lock (_sync)
        {
            if (HasPendingWork || !ClaimUncertain) throw new InvalidOperationException("仅可人工处理历史未知领取，不能跳过在途领取或待回报。");
            var receipt = RecoveryAudit.Save(_path, reason, _state);
            Save(_state with { Acknowledgement = receipt });
        }
    }

    public void BeginClaim()
    {
        lock (_sync)
        {
            if (_storageFault || _claimStarted || _state.Entries.Length > 0)
                throw new InvalidOperationException("路由尚有未决领取或回报，暂不领取新批次。");
            _uncertainBeforeClaim = _state.ClaimUncertain;
            _acknowledgementBeforeClaim = _state.Acknowledgement;
            Save(_state with { ClaimUncertain = true, Acknowledgement = null });
            _claimStarted = true;
        }
    }

    public void CompleteClaim(IReadOnlyList<PendingJob> jobs)
    {
        lock (_sync)
        {
            if (!_claimStarted || _storageFault) throw new InvalidOperationException("没有可完成的本进程领取。");
            if (jobs.Select(j => j.JobId).Distinct().Count() != jobs.Count)
                throw new InvalidDataException("服务端返回重复作业，领取结果需核对。");
            Save(_state with { ClaimUncertain = _uncertainBeforeClaim, Acknowledgement = _acknowledgementBeforeClaim,
                Entries = jobs.Select(j => new Entry(j)).ToArray() });
            _claimStarted = false;
        }
    }

    public void AbandonClaim() { lock (_sync) _claimStarted = false; }

    public void Submitted(string serverId, string? localId, JobResult? failure)
    {
        lock (_sync)
        {
            if ((localId is null) == (failure is null)) throw new ArgumentException("提交结果必须为本地映射或明确失败。");
            if (!_state.Entries.Any(e => e.Job.JobId == serverId)) throw new InvalidOperationException("路由记录不存在。");
            Save(_state with { Entries = _state.Entries.Select(e => e.Job.JobId == serverId ? e with { LocalId = localId, Failure = failure } : e).ToArray() });
        }
    }

    public void Reported(string serverId)
    {
        lock (_sync) Save(_state with { Entries = _state.Entries.Where(e => e.Job.JobId != serverId).ToArray() });
    }

    private void Save(State next)
    {
        if (_storageFault) throw new IOException("路由日志写入曾失败，须重启并核对磁盘状态。");
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
            if (bytes.Length > 128 * 1024 * 1024) throw new InvalidDataException("路由日志超过安全写入上限。");
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, _path, true);
            _state = next;
        }
        catch { _storageFault = true; throw; }
    }
}
