using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.OfflineLayout;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Downloads;

/// <summary>多源回退决策结果（BA 消费引擎获取解析事件，按清单 urls 顺序提供下一源；/ 补本地源形态）。</summary>
/// <param name="Decision">决策：提供源（覆写引擎下载 URL / 本地源）或保持引擎当前源。</param>
/// <param name="Url">决策为 <see cref="SourceFallbackDecision.UseSource"/> 且为远程源时的目标 URL（本地源 / 其余为 null）。</param>
/// <param name="LocalPath">决策为 <see cref="SourceFallbackDecision.UseSource"/> 且为本地源时的布局目录文件路径（远程源 / 其余为 null）。</param>
/// <param name="SourceIndex">本次源在有效源序（[布局本地文件] ++ urls）中的下标（0 起；未知包为 -1）。</param>
/// <param name="SourceCount">该组件有效源总数（未知包为 0）。</param>
/// <param name="Rotated">本次是否发生换源（失败后前进到下一源，UI「换源提示」依据）。</param>
public sealed record SourceFallbackOutcome(
    SourceFallbackDecision Decision,
    string? Url,
    string? LocalPath,
    int SourceIndex,
    int SourceCount,
    bool Rotated)
{
    /// <summary>本次源是否为布局目录本地文件（远程源为 false）。</summary>
    public bool IsLocal => LocalPath is not null;
}

/// <summary>多源回退决策枚举。</summary>
public enum SourceFallbackDecision
{
    /// <summary>提供有效源序 urls[min(失败数, count-1)] 处的源（远程 = 覆写引擎下载 URL；本地 = <c>SetLocalSource</c>）。</summary>
    UseSource,

    /// <summary>不干预（源已耗尽：保留引擎当前 URL，交由失败报告处置）。</summary>
    KeepEngineUrl,
}

/// <summary>
/// 多源回退状态机（/ Issue 「范围修订 v2」；UI 无关，BA 事件层薄接线，本类承载全部决策逻辑供 net10 测试锚定）。
/// </summary>
/// <remarks>
/// <para>
/// 契约（DESIGN + + 本地源扩展）：清单组件 <c>urls</c> 数组<b>顺序即优先级、逐源回退</b>
/// 离线布局目录（本地清单所在目录）作为<b>隐式优先源目录</b>——布局文件在位时有效源序 = [本地文件] ++ urls
/// （第 0 次获取解析提供本地文件，失败后按既有推进语义落到 urls[0]…），manifest schema 零修改。
/// </para>
/// <para>
/// 推进语义：每个链包维护<b>失败计数</b>（获取失败与获取后校验失败各计一次）——第 N 次获取解析时提供有效源序
/// min(N, count-1) 处的源（第 0 次即首源，通常与 Bundle 构建期 DownloadUrl 相同）；计数达到有效源总数即<b>源耗尽</b>
/// （不再覆写、不再驱动重试）。计数仅在新建 Apply（重试按钮）时复位；包级 / 引擎自身的获取重试不复位
/// （换源正是在重试轮次间发生）。事件线程调用（Burn 引擎回调单线程），无需加锁。
/// </para>
/// <para>
/// sha256 口径：本地源与远程源同样强制——本地文件由 BA 经 <c>IEngine.SetLocalSource</c> 交给引擎
/// 引擎按包内嵌摘要（构建期实测 = manifest sha256）校验，篡改即获取 / 校验失败按序换源，全源耗尽 fail-closed。
/// 布局文件不在位 / 清单来源为 URL → 有效源序退化为纯 urls（无布局目录在线安装行为与现状一致）。
/// </para>
/// <para>
/// WiX 版本对应：Burn v3 的 <c>ResolveSource</c> 事件（BA 以 <c>DownloadSource</c> 提供备选源）在 v4+（本仓 v7）
/// 演进为「<c>CacheAcquireBegin</c> / <c>CacheAcquireComplete</c> 事件内调用 <c>IEngine.SetDownloadSource</c> +
/// 失败时事件返回 <c>Retry</c> 动作」（引擎源码 <c>AcquireContainerOrPayload</c> 的获取 / 重试循环）——
/// 本类即该回退链路的决策核心；本地源为同事件入口的 <c>SetLocalSource</c> 对应。
/// </para>
/// </remarks>
public sealed class CacheSourceFallback
{
    private readonly Dictionary<string, int> _failuresByPackage = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _urlsByPackage;
    private readonly Dictionary<string, string> _localPathsByPackage;

    private CacheSourceFallback(
        Dictionary<string, IReadOnlyList<string>> urlsByPackage,
        Dictionary<string, string> localPathsByPackage)
    {
        _urlsByPackage = urlsByPackage;
        _localPathsByPackage = localPathsByPackage;
    }

    /// <summary>
    /// 从安装清单构建（链包 id → 该组件有效源序；映射表 = <see cref="ChainPackageMap"/>，清单缺组件则该包无回退能力）。
    /// <paramref name="layoutDirectory"/> = 布局目录（本地清单所在目录，null = 无本地源）：组件布局文件（命名约定
    /// <see cref="OfflineLayoutNaming"/>）在位时作为该包有效源序首位。
    /// </summary>
    public static CacheSourceFallback FromManifest(InstallManifest manifest, string? layoutDirectory = null)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(manifest);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }
#endif

        var urlsByPackage = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var localPathsByPackage = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in manifest.Components)
        {
            if (!ChainPackageMap.PackageIdByComponent.TryGetValue(component.Id, out var packageId))
            {
                continue;
            }

            urlsByPackage[packageId] = component.Urls;

            // 本地源登记：仅当布局目录给定且组件布局文件实际在位（Apply 开始时静态判定，全程一致）
            if (!string.IsNullOrWhiteSpace(layoutDirectory))
            {
                var fileName = OfflineLayoutNaming.TryDeriveFileName(component);
                if (fileName is not null)
                {
                    var localPath = Path.Combine(layoutDirectory, fileName);
                    if (File.Exists(localPath))
                    {
                        localPathsByPackage[packageId] = localPath;
                    }
                }
            }
        }

        return new CacheSourceFallback(urlsByPackage, localPathsByPackage);
    }

    /// <summary>获取解析决策：按该包当前失败计数提供有效源序 [本地文件] ++ urls 内的源（本地 / 远程）或保持引擎源（耗尽 / 未知包）。</summary>
    /// <param name="packageOrContainerId">引擎获取解析事件的包（或容器）id——七包链中恒为链包 id。</param>
    public SourceFallbackOutcome ResolveAcquire(string? packageOrContainerId)
    {
        if (packageOrContainerId is null
            || !_urlsByPackage.TryGetValue(packageOrContainerId, out var urls)
            || urls.Count == 0)
        {
            return new SourceFallbackOutcome(SourceFallbackDecision.KeepEngineUrl, null, null, -1, 0, Rotated: false);
        }

        var hasLocal = _localPathsByPackage.TryGetValue(packageOrContainerId, out var localPath);
        var effectiveCount = urls.Count + (hasLocal ? 1 : 0);
        var failures = _failuresByPackage.TryGetValue(packageOrContainerId, out var count) ? count : 0;
        var index = Math.Min(failures, effectiveCount - 1);
        var exhausted = failures >= effectiveCount;
        if (exhausted)
        {
            return new SourceFallbackOutcome(SourceFallbackDecision.KeepEngineUrl, null, null, index, effectiveCount, Rotated: false);
        }

        if (hasLocal && failures == 0)
        {
            // 首选 = 布局目录本地文件（隐式优先源， 源解析顺序契约）
            return new SourceFallbackOutcome(SourceFallbackDecision.UseSource, null, localPath, 0, effectiveCount, Rotated: false);
        }

        var urlIndex = hasLocal ? failures - 1 : failures;
        return new SourceFallbackOutcome(
            SourceFallbackDecision.UseSource,
            urls[Math.Min(urlIndex, urls.Count - 1)],
            null,
            index,
            effectiveCount,
            Rotated: failures > 0);
    }

    /// <summary>记录一次获取失败（下载 / 拷贝失败——引擎 CacheAcquireComplete 非零；本地源拷贝失败同样推进）。</summary>
    public void RecordAcquireFailure(string? packageOrContainerId) => RecordFailure(packageOrContainerId);

    /// <summary>记录一次校验失败（下载完成但哈希不符——引擎 CacheVerifyComplete 非零；本地源副本哈希不符同样换源重取）。</summary>
    public void RecordVerifyFailure(string? packageOrContainerId) => RecordFailure(packageOrContainerId);

    /// <summary>该包是否还有未尝试的源（失败重试的驱动条件：真才覆写引擎 Retry，避免无源空转）。</summary>
    public bool HasUntriedSources(string? packageOrContainerId) =>
        packageOrContainerId is not null
        && _urlsByPackage.TryGetValue(packageOrContainerId, out var urls)
        && (_failuresByPackage.TryGetValue(packageOrContainerId, out var failures) ? failures : 0)
            < urls.Count + (_localPathsByPackage.ContainsKey(packageOrContainerId) ? 1 : 0);

    /// <summary>该包当前失败计数（源耗尽判定与失败报告「已尝试 N/M 源」展示）。</summary>
    public int FailureCount(string? packageOrContainerId) =>
        packageOrContainerId is not null && _failuresByPackage.TryGetValue(packageOrContainerId, out var failures) ? failures : 0;

    /// <summary>失败计数快照（链包 id → 失败次数；失败报告展示用）。</summary>
    public IReadOnlyDictionary<string, int> FailureSnapshot() =>
        new Dictionary<string, int>(_failuresByPackage, StringComparer.Ordinal);

    /// <summary>全部失败计数复位（新建 Apply——进度页「重试」触发全新 Plan + Apply 时由 BA 调用）。</summary>
    public void Reset() => _failuresByPackage.Clear();

    private void RecordFailure(string? packageOrContainerId)
    {
        if (packageOrContainerId is null || !_urlsByPackage.ContainsKey(packageOrContainerId))
        {
            return; // 未知包（如 Bundle 自身载荷）不参与回退计数
        }

        _failuresByPackage[packageOrContainerId] = FailureCount(packageOrContainerId) + 1;
    }
}
