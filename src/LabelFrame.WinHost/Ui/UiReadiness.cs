namespace LabelFrame.WinHost.Ui;

/// <summary>就绪等待：界面窗口先显示加载态，按固定间隔探测本地 HTTP 服务，就绪后再导航。探测委托可注入。</summary>
public static class UiReadiness
{
    /// <summary>循环探测直到成功或超时。返回是否就绪；探测异常一律视为未就绪（不中断等待）。</summary>
    public static async Task<bool> WaitUntilReadyAsync(
        Func<Task<bool>> probe,
        TimeSpan interval,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await probe().ConfigureAwait(false))
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 仅调用方取消才中断等待；探测自身的超时（HttpClient Timeout 抛 TaskCanceled）视为未就绪继续等
                throw;
            }
            catch
            {
                // 探测失败（连接拒绝 / 单次超时）= 服务尚未就绪，继续等待
            }

            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            var remaining = deadline - Environment.TickCount64;
            var delay = remaining < (long)interval.TotalMilliseconds ? remaining : (long)interval.TotalMilliseconds;
            if (delay > 0)
            {
                await Task.Delay((int)Math.Min(delay, int.MaxValue), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>HTTP GET 探测（任意 HTTP 响应都算服务已监听；仅连接层失败才算未就绪）。本机探测禁用系统代理。</summary>
    public static Func<Task<bool>> HttpProbe(Uri url, TimeSpan perAttemptTimeout)
    {
        return async () =>
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = perAttemptTimeout };
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            return true;
        };
    }
}
