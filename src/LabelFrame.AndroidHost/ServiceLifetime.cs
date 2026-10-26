namespace LabelFrame.AndroidHost;

/// <summary>串行管理后台服务代际的初始化、取消和资源排空。</summary>
internal sealed class ServiceLifetime(SemaphoreSlim generations) : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private bool _stopped;
    private bool _finished;
    private Task? _completion;
    private Task? _cancellation;

    internal Task Start(Func<CancellationToken, Task> initialize, Action<CancellationToken> activate,
        Func<Task> drain, Action<Exception> failed, Action? prepare = null)
    {
        lock (_sync)
        {
            if (_completion is null) ObjectDisposedException.ThrowIf(_stopped || _finished, this);
            if (_completion is null)
            {
                try { prepare?.Invoke(); }
                catch (Exception error)
                {
                    _completion = Task.CompletedTask;
                    _finished = true;
                    _stop.Dispose();
                    failed(error);
                    return _completion;
                }
            }
            return _completion ??= Task.Run(() => RunAsync(initialize, activate, drain, failed));
        }
    }

    internal void Stop()
    {
        lock (_sync)
        {
            if (_stopped) return;
            _stopped = true;
            if (!_finished) _cancellation ??= _stop.CancelAsync();
        }
    }

    internal void IfActive(Action action)
    {
        lock (_sync)
            if (!_stopped) action();
    }

    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            if (_completion is null && !_finished)
            {
                _finished = true;
                _stop.Dispose();
            }
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> initialize, Action<CancellationToken> activate,
        Func<Task> drain, Action<Exception> failed)
    {
        var acquired = false;
        var token = _stop.Token;
        try
        {
            await generations.WaitAsync(token).ConfigureAwait(false);
            acquired = true;
            token.ThrowIfCancellationRequested();
            await initialize(token).ConfigureAwait(false);
            lock (_sync)
            {
                token.ThrowIfCancellationRequested();
                activate(token);
            }
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            failed(error);
        }
        finally
        {
            Task cancellation;
            lock (_sync) cancellation = _cancellation ??= _stop.CancelAsync();
            try
            {
                try { await cancellation.ConfigureAwait(false); }
                catch (Exception error) { failed(error); }
                if (acquired) await drain().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failed(error);
            }
            finally
            {
                lock (_sync)
                {
                    _finished = true;
                    _stop.Dispose();
                }
                if (acquired) generations.Release();
            }
        }
    }
}
