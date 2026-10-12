namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>
/// 向导导航状态机（UI 无关，BA 向导壳复用）：绝对页索引 + 惰性装配 + 越界防御。
/// </summary>
/// <typeparam name="TPage">向导分页类型（BA 侧为 <c>IWizardPage</c>）。</typeparam>
/// <remarks>
/// 原 WizardForm 以「增量」语义导航（<c>target = currentIndex + delta</c>）
/// 构造期 <c>Navigate(0)</c> 意图「进入首页」，实际计算 <c>-1 + 0 = -1</c> 被越界守卫静默拒绝——首页从未装配、
/// 索引停在 -1，向导带着空白内容区进入消息循环，点「下一步」即 <see cref="ArgumentOutOfRangeException"/>。
/// 本类改用「绝对索引」语义收口装配入口（<see cref="TryNavigateTo"/>，0 = 首页），显式暴露装配状态
/// （<see cref="IsStarted"/>）与未装配访问的防御性异常，导航行为全部可单测（BA 工程为 net48 WinForms，
/// 状态机留在本库才能被 net10.0-windows 测试腿覆盖）。
/// </remarks>
public sealed class WizardNavigator<TPage> where TPage : class
{
    private readonly Func<TPage>[] _pageFactories;
    private readonly List<TPage> _pages = [];

    /// <summary>按页序传入工厂（惰性调用：首次导航到该页时才装配）。</summary>
    public WizardNavigator(IReadOnlyList<Func<TPage>> pageFactories)
    {
        if (pageFactories.Count == 0)
        {
            throw new ArgumentException("向导至少需要一页。", nameof(pageFactories));
        }

        _pageFactories = [.. pageFactories];
    }

    /// <summary>当前页索引；尚未装配任何页面时为 -1（见 <see cref="IsStarted"/>）。</summary>
    public int CurrentIndex { get; private set; } = -1;

    /// <summary>总页数。</summary>
    public int PageCount => _pageFactories.Length;

    /// <summary>首页是否已装配——消息循环准入条件：装配失败不得带着 -1 索引进入消息循环。</summary>
    public bool IsStarted => CurrentIndex >= 0;

    /// <summary>当前是否停在最后一页。</summary>
    public bool IsLastPage => CurrentIndex == PageCount - 1;

    /// <summary>已装配页数（惰性装配进度，测试锚点）。</summary>
    public int CreatedPageCount => _pages.Count;

    /// <summary>当前页；未装配时抛出防御性异常（显式失败并携带索引诊断，而非索引越界）。</summary>
    public TPage CurrentPage => CurrentIndex >= 0 && CurrentIndex < _pages.Count
        ? _pages[CurrentIndex]
        : throw new InvalidOperationException($"向导尚未装配页面（当前索引 {CurrentIndex}），禁止访问当前页。");

    /// <summary>窥视指定页（按需惰性装配但不导航）：两层问卷——向导壳查询目标页 <c>ShouldSkip</c> 以越过不适用页。</summary>
    public TPage Peek(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageFactories.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"页索引越界（0~{_pageFactories.Length - 1}）。");
        }

        while (_pages.Count <= pageIndex)
        {
            var page = _pageFactories[_pages.Count]();
            if (page is null)
            {
                throw new InvalidOperationException($"第 {_pages.Count + 1} 页工厂返回 null，页面装配失败。");
            }

            _pages.Add(page);
        }

        return _pages[pageIndex];
    }

    /// <summary>
    /// 导航到<b>绝对</b>页索引（0 = 首页装配入口；目标页按需惰性装配，已装配页复用实例）。
    /// 越界返回 false 且状态不变；页工厂异常向上传播（不吞）——装配停留在已装配页，重试从同一索引重新装配。
    /// </summary>
    public bool TryNavigateTo(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageFactories.Length)
        {
            return false;
        }

        while (_pages.Count <= pageIndex)
        {
            var page = _pageFactories[_pages.Count]();
            if (page is null)
            {
                throw new InvalidOperationException($"第 {_pages.Count + 1} 页工厂返回 null，页面装配失败。");
            }

            _pages.Add(page);
        }

        CurrentIndex = pageIndex;
        return true;
    }
}
