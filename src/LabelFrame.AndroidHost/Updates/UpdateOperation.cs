namespace LabelFrame.AndroidHost.Updates;

/// <summary>串行化候选缓存变更与安装准备操作。</summary>
public sealed class UpdateOperation : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public static UpdateOperation Shared { get; } = new();
    public void Dispose() => _gate.Dispose();

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        return new Lease(_gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
