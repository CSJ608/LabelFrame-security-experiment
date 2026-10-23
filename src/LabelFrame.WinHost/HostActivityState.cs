namespace LabelFrame.WinHost;

/// <summary>客户端维护操作与业务、界面活动的互斥状态。</summary>
public sealed class HostActivityState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UiActivityReport> _sessions = [];
    private readonly HashSet<string> _pendingResults = [];
    private int _operations;
    private bool _prepared;
    private bool _pluginsChanged;
    private string? _preparationId;
    private bool _untrackedUi;
    private bool _recoveryBlocked;
    private bool _recoveryRestartAllowed;
    private DateTimeOffset _lastInteraction = DateTimeOffset.UtcNow;

    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    public bool PluginsChanged { get { lock (_gate) return _pluginsChanged; } }
    public bool IsPrepared { get { lock (_gate) return _prepared || _recoveryBlocked; } }
    public bool IsRecoveryBlocked { get { lock (_gate) return _recoveryBlocked; } }
    public bool CanRestartRecovery { get { lock (_gate) return _recoveryBlocked && _recoveryRestartAllowed; } }
    public DateTimeOffset LastInteraction { get { lock (_gate) return _lastInteraction; } }
    public void BlockForRecovery(bool blocked) { lock (_gate) { _recoveryBlocked = blocked; _recoveryRestartAllowed = false; } }
    public void AllowRecoveryRestart() { lock (_gate) _recoveryRestartAllowed = true; }
    public string? PreparationId { get { lock (_gate) return _preparationId; } }

    public void MarkPluginsChanged() { lock (_gate) _pluginsChanged = true; }

    public bool ReportUi(UiActivityReport report)
    {
        lock (_gate)
        {
            if (_prepared && !report.Closed && report.PreparationId != _preparationId) return false;
            if (_sessions.TryGetValue(report.SessionId, out var existing) && report.Sequence < existing.Sequence) return true;
            if (report.InteractionUnixMs > (existing?.InteractionUnixMs ?? 0)) _lastInteraction = DateTimeOffset.UtcNow;
            if (_sessions.Count < 128 || _sessions.ContainsKey(report.SessionId))
                _sessions[report.SessionId] = report;
            else { _untrackedUi = true; return false; }
            return true;
        }
    }

    public IDisposable? TryBeginOperation()
    {
        lock (_gate)
        {
            if (_prepared || _recoveryBlocked) return null;
            _operations++;
            return new OperationLease(this);
        }
    }

    public void AddPendingResult(string id) { lock (_gate) _pendingResults.Add(id); }
    public void ConfirmResult(string id) { lock (_gate) _pendingResults.Remove(id); }

    public bool TryPrepare(out string? reason, bool allowRecoveryRestart = false)
    {
        lock (_gate)
        {
            reason = _recoveryBlocked && !allowRecoveryRestart ? "更新恢复检查未通过，请管理员修复后重新启动。"
                : _untrackedUi ? "界面会话过多，无法确认所有编辑状态，请手动保存并退出客户端。"
                : _sessions.Values.Any(s => !s.Closed && s.Dirty)
                ? "界面有未保存的更改，请先保存或放弃更改后再重启。"
                : _sessions.Values.Any(s => !s.Closed && s.Busy) || _operations > 0
                    ? "客户端正在处理操作，请稍后重试重启。"
                    : _pendingResults.Count > 0
                        ? "打印结果尚未回报服务端，请等待连接恢复并完成回报后再重启。"
                        : null;
            if (reason is not null) return false;
            _prepared = true;
            _preparationId = Guid.NewGuid().ToString("N");
            return true;
        }
    }

    public bool IsUiFrozen(out string? reason)
    {
        lock (_gate)
        {
            reason = _sessions.Values.Any(s => !s.Closed && (s.Dirty || s.Busy))
                ? "界面有未保存的更改或正在处理操作，请完成后再重启。" : null;
            return _sessions.Values.All(s => s.Closed || s.PreparationId == _preparationId);
        }
    }

    public void CancelPreparation() { lock (_gate) { _prepared = false; _preparationId = null; } }

    private sealed class OperationLease(HostActivityState owner) : IDisposable
    {
        private HostActivityState? _owner = owner;
        public void Dispose()
        {
            var state = Interlocked.Exchange(ref _owner, null);
            if (state is not null) lock (state._gate) state._operations--;
        }
    }
}

/// <summary>单个界面会话的编辑与在途操作状态。</summary>
public sealed record UiActivityReport(string SessionId, bool Dirty, bool Busy, bool Closed = false, long Sequence = 0, string? PreparationId = null, long InteractionUnixMs = 0);
