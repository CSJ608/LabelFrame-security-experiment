using LabelFrame.WinHost.Routing;
using Microsoft.Extensions.Time.Testing;

namespace LabelFrame.WinHost.Tests.Routing;

/// <summary>进度节流组件确定性测试（FakeTimeProvider）：变化才发 + 最小间隔 + 失败可重试。</summary>
public class ProgressThrottleTests
{
    [Fact]
    public void Unchanged_counts_should_not_report()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var throttle = new ProgressThrottle();
        var interval = TimeSpan.FromSeconds(1);

        // 初始态视为 0/0 已上报：作业刚投入队列不产生 0/0 空上报
        Assert.False(throttle.ShouldReport(0, 0, time.GetUtcNow(), interval));

        // 计数变化且达间隔 → 放行并记录
        time.Advance(interval);
        Assert.True(throttle.ShouldReport(1, 0, time.GetUtcNow(), interval));
        throttle.MarkReported(1, 0, time.GetUtcNow());

        // 计数未变化：间隔再长也不发（无变化零流量）
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(throttle.ShouldReport(1, 0, time.GetUtcNow(), interval));
    }

    [Fact]
    public void Changed_counts_within_interval_should_be_suppressed_until_elapsed()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var throttle = new ProgressThrottle();
        var interval = TimeSpan.FromSeconds(1);

        time.Advance(interval);
        Assert.True(throttle.ShouldReport(1, 0, time.GetUtcNow(), interval));
        throttle.MarkReported(1, 0, time.GetUtcNow());

        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.False(throttle.ShouldReport(2, 0, time.GetUtcNow(), interval));

        time.Advance(TimeSpan.FromMilliseconds(700));
        Assert.True(throttle.ShouldReport(2, 0, time.GetUtcNow(), interval));
    }

    [Fact]
    public void Failed_report_should_keep_state_retryable()
    {
        // 上报失败不 MarkReported：同一计数下一轮仍放行（Server 不可达静默降级后自然重试）
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var throttle = new ProgressThrottle();
        var interval = TimeSpan.FromSeconds(1);

        time.Advance(interval);
        Assert.True(throttle.ShouldReport(1, 0, time.GetUtcNow(), interval));

        time.Advance(interval);
        Assert.True(throttle.ShouldReport(1, 0, time.GetUtcNow(), interval));
    }
}
