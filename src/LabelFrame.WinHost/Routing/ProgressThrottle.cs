namespace LabelFrame.WinHost.Routing;

/// <summary>
/// 单作业进度上报节流：计数与上次成功上报相同不发（无变化零流量）
/// 且距上次成功上报不足最小间隔不发；失败不 MarkReported，下一轮自然重试。
/// 初始态视为「0/0 已上报」——作业刚投入队列时不产生 0/0 的空上报。
/// 时间经 TimeProvider 注入（FakeTimeProvider 确定性驱动）。
/// </summary>
public sealed class ProgressThrottle
{
    private (int Completed, int Failed) _lastReported = (0, 0);
    private DateTimeOffset _lastReportedAt;

    /// <summary>是否应上报：计数有变化 且 距上次成功上报已达最小间隔。</summary>
    public bool ShouldReport(int completed, int failed, DateTimeOffset now, TimeSpan minInterval)
        => (completed, failed) != _lastReported && now - _lastReportedAt >= minInterval;

    /// <summary>记录一次成功上报（失败路径不调用，保持可重试）。</summary>
    public void MarkReported(int completed, int failed, DateTimeOffset now)
    {
        _lastReported = (completed, failed);
        _lastReportedAt = now;
    }
}
