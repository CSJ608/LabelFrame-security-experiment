namespace LabelFrame.AndroidHost.Updates;

/// <summary>协调业务写入、编辑会话与安装冻结的进程级互斥门闩。</summary>
public sealed class MaintenanceGate
{
    private readonly object _sync = new();
    private int _writers;
    private int _editors;
    private bool _frozen;
    public static MaintenanceGate Shared { get; } = new();
    public bool IsFrozen { get { lock (_sync) return _frozen; } }

    public IDisposable? TryEnterWrite()
    {
        lock (_sync)
        {
            if (_frozen) return null;
            _writers++;
            return new Lease(() => { lock (_sync) _writers--; });
        }
    }

    public IDisposable? TryEnterEditor()
    {
        lock (_sync)
        {
            if (_frozen) return null;
            _editors++;
            return new Lease(() => { lock (_sync) _editors--; });
        }
    }

    public bool TryFreeze()
    {
        lock (_sync)
        {
            if (_frozen || _writers != 0 || _editors != 0) return false;
            _frozen = true;
            return true;
        }
    }

    public void RestoreFrozen() { lock (_sync) _frozen = true; }
    public void ReleaseAfterResolution() { lock (_sync) _frozen = false; }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
