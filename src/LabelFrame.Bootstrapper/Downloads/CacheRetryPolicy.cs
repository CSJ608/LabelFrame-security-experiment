namespace LabelFrame.Bootstrapper.Downloads;

/// <summary>缓存重试判定结果。</summary>
/// <param name="AllowRetry">是否允许驱动引擎重试（假 = 连续无进展次数超限，应停止重试交引擎失败处置）。</param>
/// <param name="Delay">本次重试前的指数退避等待（事件回调内阻塞即节流引擎缓存线程）。</param>
/// <param name="GrantedRetries">该包累计已授予的重试次数（退避指数）。</param>
/// <param name="StalledRetries">该包当前连续无进展重试次数（终止判定输入）。</param>
public sealed record CacheRetryDecision(
    bool AllowRetry,
    TimeSpan Delay,
    int GrantedRetries,
    int StalledRetries)
{
    /// <summary>重试预算耗尽（连续无进展超限）——停止驱动重试，交引擎按失败回滚进失败报告页。</summary>
    public bool Exhausted => !AllowRetry;
}

/// <summary>
/// 缓存重试策略：BA 三个缓存重试点（包级 <c>CachePackageComplete</c> 兜底 Retry
/// 载荷获取 <c>CacheAcquireComplete</c> Retry / 校验 <c>CacheVerifyComplete</c> RetryAcquisition）共用每包预算。
/// </summary>
/// <remarks>
/// <para>
/// 机制根源（真机实测）：包级兜底重试只判「清单内有无未试源」、不判「重试间是否推进」——内嵌容器载荷的获取失败
/// （如 <c>WixAttachedContainer 0x80070002</c>）无源可换、不进多源回退失败计数，未试源集合永不消耗 →
/// 无退避无终止热循环（5 分钟 92,993 次重试、Burn 日志 219–240MB、向导滞留进度页永不出现失败页）。
/// </para>
/// <para>
/// 判定语义：**指数退避**（基值 500ms × 2^(已授予次数-1)，上限 30s）+ **连续无进展终止**（默认上限 3 次）。
/// 「进展」= 多源回退失败计数（<see cref="CacheSourceFallback.FailureCount"/>）自上次判定以来前进——
/// 合法换源重试（获取 / 校验失败已记录、源游标推进）不衰减预算；纯热循环（无源可换类失败）连续三次即终止。
/// 计数仅在新建 Apply（进度页「重试」按钮 = 全新 Plan + Apply）时整体复位（<see cref="Reset"/>）。
/// </para>
/// <para>引擎事件回调线程独占访问（Burn 回调单线程），无需加锁——与 <see cref="CacheSourceFallback"/> 同口径。</para>
/// </remarks>
public sealed class CacheRetryPolicy
{
    /// <summary>退避基值（首次重试前等待 500ms）。</summary>
    public static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>退避上限（单次重试最长等待 30s——重试间仍在推进的极端慢源场景不误伤）。</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>默认连续无进展重试上限（纯热循环三轮即断——退避后仍无进展说明失败与源无关，重试无意义）。</summary>
    public const int DefaultMaxStalledRetries = 3;

    private readonly int _maxStalledRetries;
    private readonly Dictionary<string, PackageRetryState> _stateByPackage = new(StringComparer.Ordinal);

    /// <param name="maxStalledRetries">连续无进展重试上限（缺省 <see cref="DefaultMaxStalledRetries"/>）。</param>
    public CacheRetryPolicy(int maxStalledRetries = DefaultMaxStalledRetries)
    {
        _maxStalledRetries = maxStalledRetries;
    }

    /// <summary>重试判定：按该包多源回退失败计数判定本次重试是否放行与退避等待。</summary>
    /// <param name="packageOrContainerId">链包 id（未知 / 空值按不可重试处理）。</param>
    /// <param name="sourceFailureCount">该包当前多源回退失败计数（判定「进展」的比较基线；判定后作为下次基线记录）。</param>
    public CacheRetryDecision ShouldRetry(string? packageOrContainerId, int sourceFailureCount)
    {
        if (string.IsNullOrEmpty(packageOrContainerId))
        {
            return new CacheRetryDecision(false, TimeSpan.Zero, 0, 0);
        }

        var state = GetOrCreate(packageOrContainerId!);
        state.StalledRetries = sourceFailureCount > state.LastSourceFailures ? 0 : state.StalledRetries + 1;
        state.LastSourceFailures = sourceFailureCount;

        if (state.StalledRetries > _maxStalledRetries)
        {
            return new CacheRetryDecision(false, TimeSpan.Zero, state.GrantedRetries, state.StalledRetries);
        }

        state.GrantedRetries++;
        return new CacheRetryDecision(true, DelayFor(state.GrantedRetries), state.GrantedRetries, state.StalledRetries);
    }

    /// <summary>第 N 次授予的重试的退避等待（500ms × 2^()，上限 30s）。</summary>
    public static TimeSpan DelayFor(int grantedRetryCount) =>
        grantedRetryCount <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Math.Min(BaseDelay.Ticks * (1L << Math.Min(grantedRetryCount - 1, 40)), MaxDelay.Ticks));

    /// <summary>全部计数复位（新建 Apply——进度页「重试」触发全新 Plan + Apply 时由 BA 调用）。</summary>
    public void Reset() => _stateByPackage.Clear();

    private PackageRetryState GetOrCreate(string packageOrContainerId)
    {
        if (!_stateByPackage.TryGetValue(packageOrContainerId, out var state))
        {
            state = new PackageRetryState();
            _stateByPackage[packageOrContainerId] = state;
        }

        return state;
    }

    private sealed class PackageRetryState
    {
        /// <summary>累计已授予的重试次数（退避指数，Apply 轮内单调递增）。</summary>
        public int GrantedRetries;

        /// <summary>当前连续无进展重试次数（进展即归零）。</summary>
        public int StalledRetries;

        /// <summary>上次判定时的多源回退失败计数（进展比较基线；0 起步——首判无记录即视为无进展）。</summary>
        public int LastSourceFailures;
    }
}
