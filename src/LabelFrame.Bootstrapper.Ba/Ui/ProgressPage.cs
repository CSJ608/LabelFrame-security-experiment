using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>
/// 安装进度页：引擎事件映射（CacheAcquireProgress / ExecuteProgress → 总进度；Cache/Execute Package Begin/Complete → 分包状态）；
/// 失败时切换为失败报告（失败步骤 / Burn 日志位置 / 建议动作 + 重试）。下载侧细化：单包下载百分比、
/// 下载源序号与换源提示、缓存命中标注、失败分类差异化提示。
/// </summary>
internal sealed class ProgressPage : UserControl, IWizardPage
{
    /// <summary>链序展示（与 Bundle.wxs Chain 一致，两侧同步）。</summary>
    private static readonly string[] ChainOrder =
    [
        "DotNetDesktopRuntime",
        "WebView2Runtime",
        "ServerMsi",
        "ClientMsi",
        "WebUiPlacement",
        "ZebraPluginPlacement",
    ];

    private readonly WizardSession _session;
    private readonly LabelFrameBootstrapperBa _ba;
    private readonly WizardForm _wizard;

    private readonly Label _phaseLabel = new();
    private readonly Label _downloadHintLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly ListView _listView = new();

    // 失败报告面板（Apply 失败时切换显示）
    private readonly GroupBox _failureBox = new();
    private readonly TextBox _failureDetail = new();
    private readonly Button _retryButton = new();
    private readonly Button _viewLogButton = new();

    private bool _started;
    private string? _failureLogPath;

    public ProgressPage(WizardSession session, LabelFrameBootstrapperBa ba, WizardForm wizard)
    {
        _session = session;
        _ba = ba;
        _wizard = wizard;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "正在安装 LabelFrame",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _phaseLabel.AutoSize = true;
        _phaseLabel.Location = new Point(8, 38);

        // 下载侧提示行（换源 / 缓存命中）：与阶段行错开一行
        _downloadHintLabel.AutoSize = true;
        _downloadHintLabel.Location = new Point(8, 60);
        _downloadHintLabel.MaximumSize = new Size(680, 0);
        _downloadHintLabel.ForeColor = Color.FromArgb(154, 84, 0);

        _progressBar.Location = new Point(8, 84);
        _progressBar.Width = 680;
        _progressBar.Height = 22;

        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.HideSelection = true;
        _listView.Location = new Point(8, 118);
        _listView.Size = new Size(680, 278);
        _listView.Columns.Add("组件", 320);
        _listView.Columns.Add("状态", 340);

        _failureBox.Text = "安装失败";
        _failureBox.Location = new Point(8, 118);
        _failureBox.Size = new Size(680, 278);
        _failureBox.ForeColor = Color.Firebrick;
        _failureBox.Visible = false;

        _failureDetail.Multiline = true;
        _failureDetail.ReadOnly = true;
        _failureDetail.ScrollBars = ScrollBars.Vertical;
        _failureDetail.Location = new Point(12, 22);
        _failureDetail.Size = new Size(656, 190);
        _failureDetail.Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Regular);

        _retryButton.Text = "重试(&R)";
        _retryButton.AutoSize = true;
        _retryButton.Location = new Point(12, 224);
        _retryButton.Click += async (_, _) => await RunInstallAsync(retry: true);

        _viewLogButton.Text = "查看安装日志(&L)";
        _viewLogButton.AutoSize = true;
        _viewLogButton.Location = new Point(110, 224);
        _viewLogButton.Click += (_, _) => OpenBundleLog();

        _failureBox.Controls.Add(_failureDetail);
        _failureBox.Controls.Add(_retryButton);
        _failureBox.Controls.Add(_viewLogButton);

        Controls.Add(title);
        Controls.Add(_phaseLabel);
        Controls.Add(_downloadHintLabel);
        Controls.Add(_progressBar);
        Controls.Add(_listView);
        Controls.Add(_failureBox);
    }

    public bool ShouldSkip => false;

    public void OnEnter()
    {
        _ba.StateChanged += OnStateChanged;
        RefreshView(_ba.State);

        if (!_started)
        {
            _started = true;
            _ = RunInstallAsync(retry: false);
        }
    }

    public bool CanProceed(out string? reason)
    {
        reason = null;
        return true; // 本页不承接「下一步」（完成态由代码导航到完成页）
    }

    /// <summary>启动 / 重试安装：写变量 → Plan → Apply；终态（完成 / 失败）更新 UI。</summary>
    private async Task RunInstallAsync(bool retry)
    {
        _wizard.SetInstallInProgress(true);
        _retryButton.Enabled = false;
        _failureBox.Visible = false;
        _listView.Visible = true;
        _phaseLabel.Text = retry ? "正在重新计划并安装…" : "正在生成安装计划…";
        _progressBar.Value = 0;

        try
        {
            var state = await _ba.ExecuteInstallAsync(_session, _wizard.Handle).ConfigureAwait(true);
            _wizard.SetInstallInProgress(false);
            _retryButton.Enabled = true;

            if (state.Phase == InstallPhase.Completed)
            {
                _wizard.NavigateToComplete();
            }
            else
            {
                ShowFailure(state);
            }
        }
        catch (Exception ex)
        {
            // Plan / Apply 前置阶段异常（超时 / 引擎拒绝等）：构造失败报告
            var state = _ba.FailAs(unchecked((int)0x8000F0DF), $"安装未能开始或中途异常：{ex.Message}");
            _wizard.SetInstallInProgress(false);
            _retryButton.Enabled = true;
            ShowFailure(state);
        }
    }

    /// <summary>失败报告：失败步骤 + 引擎消息、下载侧失败分类差异化提示、Burn 日志位置、建议动作。</summary>
    private void ShowFailure(InstallState state)
    {
        _listView.Visible = false;
        _failureBox.Visible = true;

        var failedPackage = state.Packages.FirstOrDefault(pair => pair.Value.Phase == PackagePhase.Failed);
        var failedStep = failedPackage.Key is null
            ? $"阶段：{DescribePhase(state.Phase)}"
            : $"组件：{DisplayName(failedPackage.Key)}（阶段 {DescribePhase(state.Phase)}，链已停止并回滚本次已执行组件）";

        // 下载侧失败分类（源不可达 / 网络中断 / 校验失败差异化提示；分类缺失时跳过该节）
        var categoryText = failedPackage.Value.Failure is { } category && category != LabelFrame.Bootstrapper.Downloads.DownloadFailureCategory.None
            ? "\n\n失败分类：" + LabelFrame.Bootstrapper.Downloads.DownloadFailureClassifier.Describe(category)
            : string.Empty;

        _failureLogPath = _ba.GetBundleLogPath();
        var logLine = _failureLogPath is null
            ? "安装日志：%TEMP%\\LabelFrame*.log（本次日志路径不可得，可按通配查找）"
            : $"安装日志：{_failureLogPath}";

        var engineMessages = state.Errors.Count == 0
            ? string.Empty
            : "\n\n引擎消息：\n" + string.Join("\n", state.Errors.Take(6));

        _failureDetail.Text =
            $"安装失败（引擎状态 0x{state.ApplyStatus:X8}）。\n\n{failedStep}{categoryText}\n{logLine}{engineMessages}"
            + "\n\n建议动作：\n  1. 点击「重试」重新计划并安装（已装组件会自动跳过，幂等；多源清单会从头重试各源）；\n  2. 查看安装日志定位失败组件与退出码；\n  3. 关闭后右键「以管理员身份运行」引导程序重试；仍失败请携带日志反馈。";
        _phaseLabel.Text = "安装失败。";
        _downloadHintLabel.Text = string.Empty;
    }

    /// <summary>状态快照 → UI（引擎事件线程触发，封送到 UI 线程）。</summary>
    private void OnStateChanged()
    {
        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(() => RefreshView(_ba.State));
        }
        else
        {
            RefreshView(_ba.State);
        }
    }

    private void RefreshView(InstallState state)
    {
        if (_failureBox.Visible)
        {
            return; // 失败报告已定格（重试时复位）
        }

        _progressBar.Value = Math.Max(0, Math.Min(100, state.OverallPercentage));
        _downloadHintLabel.Text = state.DownloadHint ?? string.Empty;
        _phaseLabel.Text = state.Phase switch
        {
            InstallPhase.Planning => "正在生成安装计划（Plan）…",
            InstallPhase.Downloading => $"正在下载组件{DescribeCurrentPackage(state)}…",
            InstallPhase.Installing => $"正在安装组件{(state.CurrentPackageId is null ? string.Empty : $"：{DisplayName(state.CurrentPackageId)}")}…",
            InstallPhase.Completed => "安装完成。",
            InstallPhase.Failed => "安装失败。",
            _ => "准备中…",
        };

        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var packageId in ChainOrder)
        {
            var row = new ListViewItem(DisplayName(packageId));
            row.SubItems.Add(DescribePackage(packageId, state));
            _listView.Items.Add(row);
        }

        _listView.EndUpdate();
    }

    /// <summary>下载阶段当前组件描述（单包百分比 + 源序号）。</summary>
    private string DescribeCurrentPackage(InstallState state)
    {
        if (state.CurrentPackageId is null)
        {
            return string.Empty;
        }

        var percent = state.Packages.TryGetValue(state.CurrentPackageId, out var run) ? run.Percent : 0;
        var source = state.CurrentSourceCount > 0 ? $"，源 {state.CurrentSourceIndex + 1}/{state.CurrentSourceCount}" : string.Empty;
        return $"：{DisplayName(state.CurrentPackageId)}（{percent}%{source}）";
    }

    private string DescribePackage(string packageId, InstallState state)
    {
        if (!state.Packages.TryGetValue(packageId, out var run))
        {
            return InChainForCurrentPlan(packageId) ? "等待中" : "—（本形态未纳入）";
        }

        if (run.FromCache && run.Phase is PackagePhase.Downloading or PackagePhase.Downloaded)
        {
            return "已缓存（跳过下载）";
        }

        return run.Phase switch
        {
            PackagePhase.Downloading => $"下载中… {run.Percent}%",
            PackagePhase.Downloaded => "已下载",
            PackagePhase.Installing => "安装中…",
            PackagePhase.Succeeded => "完成",
            PackagePhase.Failed => $"失败（已回滚）{DescribeFailureBrief(run.Failure)}",
            _ => "等待中",
        };
    }

    /// <summary>失败分类简述（列表行内；完整差异化提示见失败报告）。</summary>
    private static string DescribeFailureBrief(LabelFrame.Bootstrapper.Downloads.DownloadFailureCategory? failure) => failure switch
    {
        LabelFrame.Bootstrapper.Downloads.DownloadFailureCategory.SourceUnreachable => "：源不可达",
        LabelFrame.Bootstrapper.Downloads.DownloadFailureCategory.NetworkInterrupted => "：网络中断",
        LabelFrame.Bootstrapper.Downloads.DownloadFailureCategory.VerificationFailed => "：校验失败",
        _ => string.Empty,
    };

    /// <summary>包展示名：manifest 组件 notes 优先，兜底链包 id（旧清单无 runtime 条目时仍可读）。</summary>
    private string DisplayName(string packageId)
    {
        if (ChainPackageMap.ComponentByPackageId.TryGetValue(packageId, out var componentId)
            && _session.Manifest is not null)
        {
            var component = _session.Manifest.Components.FirstOrDefault(candidate => candidate.Id == componentId);
            if (component is not null && !string.IsNullOrWhiteSpace(component.Notes))
            {
                return component.Notes!; // net48 目标包无 NotNullWhen 标注，IsNullOrWhiteSpace 无法流经可空分析
            }
        }

        return packageId switch
        {
            "DotNetDesktopRuntime" => ".NET 10 Desktop Runtime",
            "WebView2Runtime" => "Microsoft Edge WebView2 运行时",
            "ServerMsi" => "LabelFrame 服务端",
            "ClientMsi" => "LabelFrame 打印客户端",
            "WebUiPlacement" => "服务端管理界面插件",
            "ZebraPluginPlacement" => "Zebra 传输官方插件",
            _ => packageId,
        };
    }

    private bool InChainForCurrentPlan(string packageId) =>
        ChainPackageMap.ComponentByPackageId.TryGetValue(packageId, out var componentId)
        && _session.BuildPlan().Components.Any(item => item.Component.Id == componentId);

    private static string DescribePhase(InstallPhase phase) => phase switch
    {
        InstallPhase.Planning => "安装计划",
        InstallPhase.Downloading => "下载",
        InstallPhase.Installing => "安装执行",
        InstallPhase.Completed => "已完成",
        InstallPhase.Failed => "执行失败",
        _ => "初始",
    };

    private void OpenBundleLog()
    {
        try
        {
            var path = _ba.GetBundleLogPath();
            if (path is null)
            {
                MessageBox.Show(this, "本次运行日志路径不可得，请在 %TEMP% 目录查找 LabelFrame*.log。", "查看安装日志", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (SystemException)
        {
            MessageBox.Show(this, "打开日志失败，请手动查看：\n" + (_failureLogPath ?? "%TEMP%\\LabelFrame*.log"), "查看安装日志", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
