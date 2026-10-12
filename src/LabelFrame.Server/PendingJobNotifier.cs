namespace LabelFrame.Server;

/// <summary>设备待领取作业通知：长轮询等待者按设备注册，作业到达时 Notify 立即唤醒全部等待者（等效推送，无 WebSocket 依赖）；超时返回 false。</summary>
public sealed class PendingJobNotifier
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<TaskCompletionSource>> _waiters = new();

    /// <summary>等待本设备出现待领取作业；超时返回 false，被 Notify 唤醒返回 true。</summary>
    public async Task<bool> WaitAsync(string deviceId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource tcs;
        lock (_gate)
        {
            if (!_waiters.TryGetValue(deviceId, out var list))
            {
                list = [];
                _waiters[deviceId] = list;
            }

            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            list.Add(tcs);
        }

        try
        {
            var delay = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(tcs.Task, delay);
            return completed == tcs.Task;
        }
        finally
        {
            lock (_gate)
            {
                if (_waiters.TryGetValue(deviceId, out var list))
                {
                    list.Remove(tcs);
                    if (list.Count == 0)
                    {
                        _waiters.Remove(deviceId);
                    }
                }
            }
        }
    }

    /// <summary>唤醒该设备的全部等待者（作业已入队）。</summary>
    public void Notify(string deviceId)
    {
        List<TaskCompletionSource>? toComplete = null;
        lock (_gate)
        {
            if (_waiters.TryGetValue(deviceId, out var list))
            {
                toComplete = list.ToList();
                _waiters.Remove(deviceId);
            }
        }

        foreach (var tcs in toComplete ?? [])
        {
            tcs.TrySetResult();
        }
    }
}
