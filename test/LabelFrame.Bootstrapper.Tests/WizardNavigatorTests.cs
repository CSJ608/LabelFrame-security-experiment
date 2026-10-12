using LabelFrame.Bootstrapper.Wizard;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>
/// 向导导航状态机（验收回流）：原 WizardForm 增量导航 <c>Navigate（0）</c>
/// 被越界守卫静默拒绝（-1 + 0 = -1），首页从未装配、点「下一步」即索引越界——
/// 本套锁定绝对索引装配 / 惰性创建 / 前进后退复用 / 越界防御 / 未装配防御 / 工厂异常不吞。
/// </summary>
public sealed class WizardNavigatorTests
{
    private sealed class RecordingFactories
    {
        public List<string> Pages { get; } = [];

        public List<Func<string>> For(int count) =>
            [.. Enumerable.Range(0, count).Select(i => (Func<string>)(() =>
            {
                Pages.Add($"page-{i}");
                return $"page-{i}";
            }))];
    }

    [Fact]
    public void Constructor_without_pages_should_throw()
    {
        var ex = Assert.Throws<ArgumentException>(() => new WizardNavigator<string>([]));

        Assert.Contains("至少需要一页", ex.Message);
    }

    [Fact]
    public void Before_assembly_index_should_be_unstarted()
    {
        var navigator = new WizardNavigator<string>(new RecordingFactories().For(5));

        Assert.Equal(-1, navigator.CurrentIndex);
        Assert.False(navigator.IsStarted);
        Assert.False(navigator.IsLastPage);
        Assert.Equal(0, navigator.CreatedPageCount);
        Assert.Equal(5, navigator.PageCount);
    }

    [Fact]
    public void Navigate_to_first_page_should_assemble_welcome()
    {
        // 原缺陷回归锚点：构造后导航到首页必须装配成功（原增量语义 Navigate(0) 在此处静默失败）
        var factories = new RecordingFactories();
        var navigator = new WizardNavigator<string>(factories.For(5));

        var navigated = navigator.TryNavigateTo(0);

        Assert.True(navigated);
        Assert.True(navigator.IsStarted);
        Assert.Equal(0, navigator.CurrentIndex);
        Assert.Equal("page-0", navigator.CurrentPage);
        Assert.Equal(1, navigator.CreatedPageCount);
        Assert.Equal(new[] { "page-0" }, factories.Pages);
    }

    [Fact]
    public void Navigate_forward_should_assemble_pages_lazily_in_order()
    {
        var factories = new RecordingFactories();
        var navigator = new WizardNavigator<string>(factories.For(5));
        navigator.TryNavigateTo(0);

        Assert.True(navigator.TryNavigateTo(1));
        Assert.True(navigator.TryNavigateTo(2));

        Assert.Equal(2, navigator.CurrentIndex);
        Assert.Equal("page-2", navigator.CurrentPage);
        Assert.Equal(3, navigator.CreatedPageCount);
        Assert.Equal(new[] { "page-0", "page-1", "page-2" }, factories.Pages);
    }

    [Fact]
    public void Step_through_all_pages_should_reach_last()
    {
        var navigator = new WizardNavigator<string>(new RecordingFactories().For(5));
        navigator.TryNavigateTo(0);

        for (var index = 1; index < 5; index++)
        {
            Assert.True(navigator.TryNavigateTo(index));
        }

        Assert.Equal(4, navigator.CurrentIndex);
        Assert.True(navigator.IsLastPage);
        Assert.Equal("page-4", navigator.CurrentPage);
        Assert.Equal(5, navigator.CreatedPageCount);
    }

    [Fact]
    public void Navigate_back_should_reuse_created_page_instance()
    {
        var navigator = new WizardNavigator<string>(new RecordingFactories().For(5));
        navigator.TryNavigateTo(0);
        navigator.TryNavigateTo(2);
        var page2 = navigator.CurrentPage;

        Assert.True(navigator.TryNavigateTo(1));
        Assert.True(navigator.TryNavigateTo(2));

        // 已装配页复用实例（不重复调工厂）
        Assert.Same(page2, navigator.CurrentPage);
        Assert.Equal(3, navigator.CreatedPageCount);
    }

    [Fact]
    public void Navigate_out_of_range_should_reject_and_keep_state()
    {
        var navigator = new WizardNavigator<string>(new RecordingFactories().For(5));
        navigator.TryNavigateTo(2);

        Assert.False(navigator.TryNavigateTo(-1));
        Assert.False(navigator.TryNavigateTo(5));
        Assert.False(navigator.TryNavigateTo(int.MinValue));
        Assert.False(navigator.TryNavigateTo(int.MaxValue));

        // 越界拒绝后状态不变、无副作用装配
        Assert.Equal(2, navigator.CurrentIndex);
        Assert.Equal(3, navigator.CreatedPageCount);
        Assert.Equal("page-2", navigator.CurrentPage);
    }

    [Fact]
    public void Navigate_to_same_page_should_be_valid_and_reenter()
    {
        // 绝对语义下导航到当前页是合法操作（重进页面刷新），与「增量 0」静默失败不同
        var navigator = new WizardNavigator<string>(new RecordingFactories().For(5));
        navigator.TryNavigateTo(1);

        Assert.True(navigator.TryNavigateTo(1));
        Assert.Equal(1, navigator.CurrentIndex);
        Assert.Equal("page-1", navigator.CurrentPage);
    }

    [Fact]
    public void Current_page_before_assembly_should_throw_defensive_exception()
    {
        // 验收缺陷的直接防御：未装配状态取当前页必须是显式 InvalidOperationException（携带索引诊断），
        // 而不是原 RequestNext 的 ArgumentOutOfRangeException（_pages[-1]）
        var navigator = new WizardNavigator<string>(new RecordingFactories().For(5));

        var ex = Assert.Throws<InvalidOperationException>(() => navigator.CurrentPage);

        Assert.IsNotType<ArgumentOutOfRangeException>(ex);
        Assert.Contains("当前索引 -1", ex.Message);
    }

    [Fact]
    public void Page_factory_failure_should_propagate_and_allow_retry()
    {
        // 异常不得被吞：装配失败向上传播，状态停留在已装配页，重试从同一索引重新装配
        var attempts = 0;
        var factories = new List<Func<string>>
        {
            () => "page-0",
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("装配失败（首次）");
                }

                return "page-1";
            },
        };
        var navigator = new WizardNavigator<string>(factories);
        navigator.TryNavigateTo(0);

        Assert.Throws<InvalidOperationException>(() => navigator.TryNavigateTo(1));
        Assert.Equal(0, navigator.CurrentIndex);
        Assert.Equal("page-0", navigator.CurrentPage);

        Assert.True(navigator.TryNavigateTo(1));
        Assert.Equal("page-1", navigator.CurrentPage);
    }

    [Fact]
    public void Page_factory_returning_null_should_throw()
    {
        var navigator = new WizardNavigator<string>([() => null!]);

        var ex = Assert.Throws<InvalidOperationException>(() => navigator.TryNavigateTo(0));

        Assert.Contains("装配失败", ex.Message);
        Assert.False(navigator.IsStarted);
    }
}
