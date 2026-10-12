using LabelFrame.Bootstrapper.Downloads;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>
/// 缓存重试策略测试：指数退避调度、连续无进展终止
/// 合法换源（源游标推进）不衰减预算、每轮 Apply 复位、未知包不可重试——
/// 机制锚点：包级兜底重试对无源可换类失败（附加容器获取 0x80070002，失败不进多源回退计数）
/// 曾形成无退避无终止热循环（真机实测 5 分钟 92,993 次重试）。
/// </summary>
public sealed class CacheRetryPolicyTests
{
    private const string PackageId = "WebUiPlacement";

    [Fact]
    public void Stalled_retries_are_granted_with_exponential_backoff_then_exhausted()
    {
        var policy = new CacheRetryPolicy(maxStalledRetries: 3);

        // 纯热循环：多源回退失败计数恒为 0（失败与源无关，无源游标推进）
        var first = policy.ShouldRetry(PackageId, sourceFailureCount: 0);
        var second = policy.ShouldRetry(PackageId, sourceFailureCount: 0);
        var third = policy.ShouldRetry(PackageId, sourceFailureCount: 0);
        var fourth = policy.ShouldRetry(PackageId, sourceFailureCount: 0);

        Assert.True(first.AllowRetry);
        Assert.True(second.AllowRetry);
        Assert.True(third.AllowRetry);
        Assert.False(fourth.AllowRetry);
        Assert.True(fourth.Exhausted);

        // 退避调度：500ms → 1s → 2s（基值 × 2^(N-1)）
        Assert.Equal(TimeSpan.FromMilliseconds(500), first.Delay);
        Assert.Equal(TimeSpan.FromSeconds(1), second.Delay);
        Assert.Equal(TimeSpan.FromSeconds(2), third.Delay);
        Assert.Equal(3, third.GrantedRetries);
    }

    [Fact]
    public void Source_rotation_progress_resets_stalled_budget()
    {
        var policy = new CacheRetryPolicy(maxStalledRetries: 3);

        // 交替形态：每次重试前源游标都前进（获取失败已记录、换源推进）——合法换源重试永不因无进展终止
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            var decision = policy.ShouldRetry(PackageId, sourceFailureCount: attempt);
            Assert.True(decision.AllowRetry);
            Assert.Equal(0, decision.StalledRetries);
        }
    }

    [Fact]
    public void Stall_after_progress_counts_from_zero()
    {
        var policy = new CacheRetryPolicy(maxStalledRetries: 2);

        // 前两轮换源推进（计数 1→2），随后热循环（计数停 2）：无进展预算从零起算，两次后终止
        Assert.True(policy.ShouldRetry(PackageId, 1).AllowRetry);
        Assert.True(policy.ShouldRetry(PackageId, 2).AllowRetry);
        Assert.True(policy.ShouldRetry(PackageId, 2).AllowRetry); // 无进展 1
        Assert.True(policy.ShouldRetry(PackageId, 2).AllowRetry); // 无进展 2
        Assert.False(policy.ShouldRetry(PackageId, 2).AllowRetry); // 无进展 3 > 上限 2 → 终止
    }

    [Fact]
    public void Reset_restores_budget_for_new_apply_round()
    {
        var policy = new CacheRetryPolicy(maxStalledRetries: 3);

        policy.ShouldRetry(PackageId, 0);
        policy.ShouldRetry(PackageId, 0);
        policy.ShouldRetry(PackageId, 0);
        Assert.False(policy.ShouldRetry(PackageId, 0).AllowRetry);

        policy.Reset(); // 进度页「重试」= 全新 Plan + Apply

        var fresh = policy.ShouldRetry(PackageId, 0);
        Assert.True(fresh.AllowRetry);
        Assert.Equal(1, fresh.GrantedRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), fresh.Delay);
    }

    [Fact]
    public void Unknown_or_null_package_is_not_retryable()
    {
        var policy = new CacheRetryPolicy();

        Assert.False(policy.ShouldRetry(null, 0).AllowRetry);
        Assert.False(policy.ShouldRetry(string.Empty, 0).AllowRetry);
    }

    [Fact]
    public void Backoff_is_capped_at_thirty_seconds()
    {
        // 长尾：已授予次数很大时（换源推进 + 混入停滞）退避不超过上限
        Assert.Equal(TimeSpan.FromSeconds(30), CacheRetryPolicy.DelayFor(7));
        Assert.Equal(TimeSpan.FromSeconds(30), CacheRetryPolicy.DelayFor(40));
        Assert.Equal(TimeSpan.Zero, CacheRetryPolicy.DelayFor(0));
    }

    [Fact]
    public void Delay_follows_base_doubling_schedule()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), CacheRetryPolicy.DelayFor(1));
        Assert.Equal(TimeSpan.FromSeconds(1), CacheRetryPolicy.DelayFor(2));
        Assert.Equal(TimeSpan.FromSeconds(2), CacheRetryPolicy.DelayFor(3));
        Assert.Equal(TimeSpan.FromSeconds(4), CacheRetryPolicy.DelayFor(4));
        Assert.Equal(TimeSpan.FromSeconds(8), CacheRetryPolicy.DelayFor(5));
        Assert.Equal(TimeSpan.FromSeconds(16), CacheRetryPolicy.DelayFor(6));
    }
}
