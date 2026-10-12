using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>向导壳：分步导航（就绪 →（角色，高级）→ 打印机 → 服务端地址 →（管理界面，服务端角色）→ 确认 → 安装进度 → 完成 / 失败报告；不适用页自动越过）。</summary>
/// <remarks>
/// 导航索引 / 惰性装配 / 越界防御由 <see cref="WizardNavigator{TPage}"/> 承担（可单测），本类只做 WinForms 呈现与按钮接线。
/// <see cref="NavigateTo"/> 为<b>绝对</b>页索引（增量语义曾致首页装配被越界守卫静默吞掉——验收回流缺陷，不可回退）。
/// 两层问卷：先以 <see cref="IWizardPage.ShouldSkip"/> 越过不适用页（前进向后找、后退向前找）。
/// 确认页「下一步」= 开始安装；执行期间「上一步 / 取消」禁用（中断由引擎 Quit 承担，不自造取消语义）。
/// </remarks>
internal sealed class WizardForm : Form
{
    private const int ProgressPageIndex = 6;

    private readonly WizardSession _session;
    private readonly LabelFrameBootstrapperBa _ba;

    private readonly Label _stepLabel = new();
    private readonly Panel _contentPanel = new();
    private readonly Button _backButton = new();
    private readonly Button _nextButton = new();
    private readonly Button _cancelButton = new();

    private readonly WizardNavigator<IWizardPage> _navigator;

    /// <summary>首显主动置前器：启动链无前台权时向导静默不前置——用户以为安装无响应。</summary>
    private readonly WizardForegroundActivator _foregroundActivator = new();

    /// <summary>会话由 BA 创建注入：布局目录隐式检测后 BA 已改写默认清单来源，本类不自建会话。</summary>
    public WizardForm(LabelFrameBootstrapperBa ba, WizardSession session)
    {
        _ba = ba;
        _session = session;

        Text = "LabelFrame 安装引导";
        StartPosition = FormStartPosition.CenterScreen;
        // 顶部 44px 品牌条占位——高度 560 → 604 保持既有内容区不变（页面布局不回归）
        MinimumSize = new Size(760, 604);
        Size = new Size(760, 604);

        // 窗体图标取 BA EXE 内嵌 Win32 图标（csproj ApplicationIcon；net48 无 Environment.ProcessPath，
        // 用 Application.ExecutablePath）；提取失败降级默认图标，品牌缺失不阻断安装。
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or OutOfMemoryException)
        {
            // 降级：保持 WinForms 默认图标（EXE 资源异常不可预期——磁盘映像 / 映像加壳等场景）
        }

        _navigator = new WizardNavigator<IWizardPage>(
        [
            () => new ReadyPage(_session, RequestNext),
            () => new RolePage(_session),
            () => new BrandPage(_session),
            () => new ServerAddressPage(_session),
            () => new ManagementUiPage(_session),
            () => new ConfirmPage(_session, _ba, ReturnToSource),
            () => new ProgressPage(_session, _ba, this),
            () => new CompletePage(_session, _ba),
        ]);

        // 顶部品牌条（lockup 母版 1480×280 等比缩至 169×32，左缘 16px 对齐步骤条）
        var brandBand = new Panel { Dock = DockStyle.Top, Height = 44 };
        var brandLockup = LoadBrandLockup();
        if (brandLockup is not null)
        {
            var brandPictureBox = new PictureBox
            {
                Image = brandLockup,
                Size = new Size(169, 32),
                Location = new Point(16, 6),
                SizeMode = PictureBoxSizeMode.Zoom,
            };
            brandBand.Controls.Add(brandPictureBox);
        }

        // 顶部步骤指示
        _stepLabel.Dock = DockStyle.Top;
        _stepLabel.Height = 36;
        _stepLabel.TextAlign = ContentAlignment.MiddleLeft;
        _stepLabel.Padding = new Padding(16, 0, 0, 0);
        _stepLabel.ForeColor = Color.FromArgb(89, 89, 89);

        // 分页内容区
        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.Padding = new Padding(16);

        // 底部导航
        var navPanel = new Panel { Dock = DockStyle.Bottom, Height = 52 };
        _backButton.Text = "上一步(&B)";
        _backButton.AutoSize = true;
        _backButton.Location = new Point(16, 10);
        _backButton.Click += (_, _) => NavigateTo(_navigator.CurrentIndex - 1, forward: false);

        _cancelButton.Text = "取消";
        _cancelButton.AutoSize = true;
        _cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _cancelButton.Location = new Point(ClientSize.Width - 100, 10);
        _cancelButton.Click += (_, _) => Close();
        navPanel.Resize += (_, _) => _cancelButton.Location = new Point(navPanel.Width - _cancelButton.Width - 24, 10);

        _nextButton.Text = "下一步(&N)";
        _nextButton.AutoSize = true;
        _nextButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _nextButton.Location = new Point(ClientSize.Width - 210, 10);
        _nextButton.Click += (_, _) => RequestNext();
        navPanel.Resize += (_, _) => _nextButton.Location = new Point(_cancelButton.Left - _nextButton.Width - 12, 10);

        navPanel.Controls.Add(_backButton);
        navPanel.Controls.Add(_nextButton);
        navPanel.Controls.Add(_cancelButton);

        Controls.Add(_contentPanel);
        Controls.Add(_stepLabel);
        Controls.Add(navPanel);
        // Dock 布局按 Controls 逆序停靠：品牌条最后加入 → 最先停靠，占据最顶部（步骤条随之下移）
        Controls.Add(brandBand);

        NavigateTo(0);

        // 装配自检：首页未就位即抛出（由 BA Run 兜底为显式失败 + 诊断），绝不带着 -1 索引进入消息循环
        if (!_navigator.IsStarted)
        {
            throw new InvalidOperationException($"向导首页装配未完成（当前索引 {_navigator.CurrentIndex}），禁止启动。");
        }
    }

    /// <summary>是否处于安装执行中（进度页运行期）：禁用关闭与导航。</summary>
    internal bool InstallInProgress { get; private set; }

    /// <summary>
    /// 头部 lockup 从嵌入资源装载（csproj LogicalName = bootstrapper-lockup.png）。
    /// 资源缺失 / 解码失败一律降级返回 null（无品牌条）——安装器不因品牌资产异常而失败。
    /// </summary>
    private static Bitmap? LoadBrandLockup()
    {
        try
        {
            using var stream = typeof(WizardForm).Assembly.GetManifestResourceStream("bootstrapper-lockup.png");
            return stream is null ? null : new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// 首显主动置前（机制见 <see cref="WizardForegroundActivator"/>）：已在前台零扰动（正常双击链不回归），
    /// 被拒时任务栏强闪烁兜底；结果写 Burn 日志（无前台权场景的机器可读证据行）。
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var outcome = _foregroundActivator.Activate(Handle);
        _ba.Log($"向导首显置前：{WizardForegroundActivator.Describe(outcome)}。");
    }

    /// <summary>就绪页加载清单成功后自动进入下一页（避免连点两次）。</summary>
    private void RequestNext()
    {
        if (!_navigator.IsStarted)
        {
            // 防御（正常流程不可达）：未装配状态禁止取当前页——显式抛出可诊断异常，而非索引越界
            throw new InvalidOperationException($"向导尚未装配页面（当前索引 {_navigator.CurrentIndex}），无法执行「下一步」。");
        }

        var page = _navigator.CurrentPage;
        if (!page.CanProceed(out var reason))
        {
            if (!string.IsNullOrEmpty(reason))
            {
                MessageBox.Show(this, reason, "无法继续", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        if (_navigator.IsLastPage)
        {
            // 完成页（最后一步）：关闭向导结束会话
            Close();
            return;
        }

        NavigateTo(_navigator.CurrentIndex + 1);
    }

    /// <summary>进入 / 离开执行态（进度页回调）：执行期间锁定「上一步 / 取消」并拦截关闭。</summary>
    internal void SetInstallInProgress(bool inProgress)
    {
        InstallInProgress = inProgress;
        _backButton.Enabled = !inProgress && _navigator.CurrentIndex > 0;
        _cancelButton.Enabled = !inProgress;
    }

    /// <summary>执行期拦截直接关闭（Alt+F4 / 标题栏 ×）：进度页终态（完成 / 失败）后才允许关闭。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (InstallInProgress)
        {
            e.Cancel = true;
            MessageBox.Show(this, "正在安装，请等待当前操作完成（失败或成功后可关闭）。", "安装进行中", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// 导航到<b>绝对</b>页索引（0 = 首页）；先按 <see cref="IWizardPage.ShouldSkip"/> 越过不适用页
    /// （前进向后找、后退向前找），越界 / 非法索引静默拒绝（保持当前页，状态机负责防御）。
    /// </summary>
    private void NavigateTo(int pageIndex, bool forward = true)
    {
        while (pageIndex >= 0 && pageIndex < _navigator.PageCount && _navigator.Peek(pageIndex).ShouldSkip)
        {
            pageIndex += forward ? 1 : -1;
        }

        if (!_navigator.TryNavigateTo(pageIndex))
        {
            return;
        }

        SuspendLayout();
        try
        {
            if (_contentPanel.Controls.Count > 0)
            {
                _contentPanel.Controls.RemoveAt(0);
            }

            var page = _navigator.CurrentPage;
            _contentPanel.Controls.Add((Control)page);

            _stepLabel.Text = StepLabel(_navigator.CurrentIndex);
            _backButton.Enabled = !InstallInProgress && _navigator.CurrentIndex > 0;
            // 进度页无「下一步」（终态后由代码导航到完成页）；完成页「下一步」= 完成
            _nextButton.Visible = _navigator.CurrentIndex != ProgressPageIndex;
            _nextButton.Text = _navigator.IsLastPage ? "完成" : "下一步(&N)";

            page.OnEnter();
        }
        finally
        {
            ResumeLayout();
        }
    }

    /// <summary>进度页 → 完成页（Apply 成功后由进度页调用）。</summary>
    internal void NavigateToComplete() => NavigateTo(_navigator.PageCount - 1);

    /// <summary>确认页明细区「更改」→ 回就绪页并展开手动来源区（高级路径：覆写清单来源后重载）。</summary>
    private void ReturnToSource()
    {
        if (_navigator.Peek(0) is ReadyPage ready)
        {
            ready.ExpandManualSource();
        }

        NavigateTo(0, forward: false);
    }

    /// <summary>步骤指示（语义名而非序号：两层问卷下适用页随角色变化，序号会跳号）。</summary>
    private string StepLabel(int pageIndex) => pageIndex switch
    {
        0 => "准备",
        1 => "选择用途",
        2 => "选择打印机",
        3 => "服务端地址",
        4 => "管理界面",
        5 => "确认安装",
        ProgressPageIndex => "正在安装…",
        _ when pageIndex == _navigator.PageCount - 1 => "安装完成",
        _ => string.Empty,
    };
}
