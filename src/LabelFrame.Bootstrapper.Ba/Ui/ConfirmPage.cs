using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Upgrade;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>
/// 确认页：默认视图只保留决策三要素——装什么（角色摘要 + 体积）、执行边界一行、升级 / 新鲜度状态行；
/// 「显示明细」折叠区承载技术细节（组件全表含来源 URL 与本机版本、清单来源覆写入口、离线安装说明）。
/// 「下一步」= 开始安装（进入进度页触发引擎 Plan + Apply，执行边界契约）。
/// </summary>
internal sealed class ConfirmPage : UserControl, IWizardPage
{
    private const string ReleasesPageUrl = "https://github.com/marci-labs/LabelFrame/releases";

    private readonly WizardSession _session;
    private readonly LabelFrameBootstrapperBa _ba;
    private readonly Action _returnToSource;
    private readonly Label _summaryLabel = new();
    private readonly Label _banner = new();
    private readonly Label _upgradeLabel = new();
    private readonly Label _freshnessLabel = new();
    private readonly Button _detailToggle = new();
    private readonly Panel _detailPanel = new();
    private readonly ListView _listView = new();
    private readonly Label _sourceLabel = new();
    private readonly Button _changeSourceButton = new();
    private readonly Label _offlineHintLabel = new();

    public ConfirmPage(WizardSession session, LabelFrameBootstrapperBa ba, Action returnToSource)
    {
        _session = session;
        _ba = ba;
        _returnToSource = returnToSource;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "确认安装",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _summaryLabel.AutoSize = true;
        _summaryLabel.Location = new Point(8, 36);
        _summaryLabel.MaximumSize = new Size(680, 0);

        // 执行边界一行（见 ExecuteBoundaryNotice）
        _banner.Text = ExecuteBoundaryNotice.Banner;
        _banner.AutoSize = false;
        _banner.Height = 30;
        _banner.Width = 680;
        _banner.Location = new Point(8, 62);
        _banner.BackColor = Color.FromArgb(255, 244, 230);
        _banner.ForeColor = Color.FromArgb(154, 84, 0);
        _banner.TextAlign = ContentAlignment.MiddleLeft;
        _banner.Padding = new Padding(8, 0, 0, 0);

        // 升级 / 已是最新状态行
        _upgradeLabel.AutoSize = false;
        _upgradeLabel.Height = 30;
        _upgradeLabel.Width = 680;
        _upgradeLabel.Location = new Point(8, 96);
        _upgradeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _upgradeLabel.Padding = new Padding(8, 0, 0, 0);
        _upgradeLabel.BackColor = Color.FromArgb(232, 240, 254);
        _upgradeLabel.ForeColor = Color.FromArgb(22, 84, 160);

        // 清单新鲜度提示（latest.json 消费）：仅落后于通道最新时可见
        _freshnessLabel.AutoSize = false;
        _freshnessLabel.Height = 26;
        _freshnessLabel.Width = 680;
        _freshnessLabel.Location = new Point(8, 130);
        _freshnessLabel.TextAlign = ContentAlignment.MiddleLeft;
        _freshnessLabel.Padding = new Padding(8, 0, 0, 0);
        _freshnessLabel.BackColor = Color.FromArgb(255, 244, 230);
        _freshnessLabel.ForeColor = Color.FromArgb(154, 84, 0);
        _freshnessLabel.Visible = false;

        // 「显示明细」折叠区
        _detailToggle.Text = "显示明细";
        _detailToggle.AutoSize = true;
        _detailToggle.Location = new Point(8, 164);
        _detailToggle.Click += (_, _) =>
        {
            _detailPanel.Visible = !_detailPanel.Visible;
            _detailToggle.Text = _detailPanel.Visible ? "隐藏明细" : "显示明细";
        };

        _detailPanel.Visible = false;
        _detailPanel.Location = new Point(8, 196);
        _detailPanel.Size = new Size(680, 236);

        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.HideSelection = true;
        _listView.Location = new Point(0, 0);
        _listView.Size = new Size(680, 170);
        _listView.Columns.Add("组件", 190);
        _listView.Columns.Add("版本", 60);
        _listView.Columns.Add("本机版本", 104);
        _listView.Columns.Add("体积", 58);
        _listView.Columns.Add("来源 URL", 140);
        _listView.Columns.Add("安装位置", 190);

        _sourceLabel.AutoSize = true;
        _sourceLabel.Location = new Point(0, 178);
        _sourceLabel.MaximumSize = new Size(560, 0);
        _sourceLabel.ForeColor = SystemColors.GrayText;

        _changeSourceButton.Text = "更改…";
        _changeSourceButton.AutoSize = true;
        _changeSourceButton.Location = new Point(600, 174);
        _changeSourceButton.Click += (_, _) => _returnToSource();

        _offlineHintLabel.AutoSize = true;
        _offlineHintLabel.Location = new Point(0, 208);
        _offlineHintLabel.MaximumSize = new Size(680, 0);
        _offlineHintLabel.ForeColor = SystemColors.GrayText;
        _offlineHintLabel.Text = $"无法联网时使用离线安装目录（制作方法见发布页说明：{ReleasesPageUrl}）。";

        _detailPanel.Controls.Add(_listView);
        _detailPanel.Controls.Add(_sourceLabel);
        _detailPanel.Controls.Add(_changeSourceButton);
        _detailPanel.Controls.Add(_offlineHintLabel);

        Controls.Add(title);
        Controls.Add(_summaryLabel);
        Controls.Add(_banner);
        Controls.Add(_upgradeLabel);
        Controls.Add(_freshnessLabel);
        Controls.Add(_detailToggle);
        Controls.Add(_detailPanel);
    }

    public bool ShouldSkip => false;

    public void OnEnter()
    {
        var plan = _session.BuildPlan();
        var assessment = _session.Assessment!;
        var plannedEntries = assessment.Entries
            .Where(entry => plan.Components.Any(item => item.Component.Id == entry.ComponentId))
            .ToList();
        _ba.Log($"确认安装计划：角色 {_session.Preset}，品牌 [{string.Join(",", _session.SelectedBrands)}]，管理界面 {_session.IncludeWebUi}，服务端地址 {_session.ServerUrl ?? "<未采集>"}，组件 [{string.Join(",", plan.Components.Select(item => item.Component.Id))}]，{DescribeRuntimeProbes(plan)}");
        _ba.Log($"升级评估（§6.11）：{UpgradePresentation.Summarize(assessment.Entries)}");

        // 装什么（角色摘要）+ 体积
        _summaryLabel.Text = plan.Components.Count == 0
            ? $"清单版本 {_session.Manifest!.LabelframeVersion}：本问卷结果无下载组件。"
            : $"将安装：{DescribeRole(plan)}，共约 {SizeFormat.Format(plan.TotalSizeBytes)}。";

        // 升级状态行（全新 / 可升级 / 已是最新）+ 新鲜度（落后于通道最新时可见）
        _upgradeLabel.Text = UpgradePresentation.Summarize(plannedEntries);
        var freshness = UpgradePresentation.DescribeFreshness(_session.Manifest!, _session.Latest);
        _freshnessLabel.Visible = freshness is not null;
        _freshnessLabel.Text = freshness ?? string.Empty;

        // 明细：组件全表（来源 URL / 本机版本 / 安装位置）+ 清单来源
        _sourceLabel.Text = $"安装信息来源：{_session.ManifestSource}";
        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var item in plan.Components)
        {
            var component = item.Component;
            var displayName = string.IsNullOrWhiteSpace(component.Notes) ? component.Id : $"{component.Id}（{component.Notes}）";
            if (component.Type == "runtime")
            {
                // 运行时组件标注探测结论（缺失才装、已装跳过——Burn DetectCondition 消费）
                var runtimeInstalled = component.Id switch
                {
                    "runtime-desktop" => _ba.RuntimeStatus.DesktopRuntimeInstalled,
                    "runtime-aspnetcore" => _ba.RuntimeStatus.AspNetCoreRuntimeInstalled,
                    "runtime-webview2" => _ba.RuntimeStatus.WebView2Installed,
                    _ => false,
                };
                displayName += runtimeInstalled ? "【已装则跳过】" : "【缺失将安装】";
            }

            var row = new ListViewItem(displayName)
            {
                ToolTipText = item.InstallTarget,
            };
            row.SubItems.Add(component.Version);
            row.SubItems.Add(DescribeLocalVersion(plannedEntries, component.Id));
            row.SubItems.Add(SizeFormat.Format(component.SizeBytes));
            row.SubItems.Add(component.Urls[0]);
            row.SubItems.Add(item.InstallTarget);
            _listView.Items.Add(row);
        }

        _listView.EndUpdate();
    }

    /// <summary>角色摘要（装什么，用户语言）：服务端 ± 客户端 + 品牌插件 + 管理界面。</summary>
    private string DescribeRole(TopologyPlan plan)
    {
        var hasServer = plan.Components.Any(item => item.Component.Id == "server-msi");
        var hasClient = plan.Components.Any(item => item.Component.Id == "client-msi");
        var brands = string.Join("、", _session.SelectedBrands.Select(BrandDisplayName));
        var parts = new List<string>();
        if (hasServer && hasClient)
        {
            parts.Add($"LabelFrame 服务端 + 打印客户端（同机，客户端指向 {_session.ServerUrl ?? ServerUrlInput.LocalServerDefault}）");
        }
        else if (hasClient)
        {
            parts.Add($"LabelFrame 打印客户端（连接服务端 {_session.ServerUrl ?? "（地址未填写）"}）");
        }
        else if (hasServer)
        {
            parts.Add("LabelFrame 服务端（Windows 服务）");
        }

        if (!string.IsNullOrEmpty(brands))
        {
            parts.Add($"{brands}打印机插件");
        }

        if (_session.IncludeWebUi)
        {
            parts.Add("管理界面");
        }

        return string.Join(" + ", parts);
    }

    private static string BrandDisplayName(string brand) => brand.ToLowerInvariant() switch
    {
        "zebra" => "Zebra（斑马）",
        _ => brand,
    };

    /// <summary>运行时探测腿文案：按当轮计划集合过滤——计划不含的运行时不输出「未装（将安装）」。</summary>
    private string DescribeRuntimeProbes(TopologyPlan plan)
    {
        var notes = new List<string>();
        if (plan.Components.Any(item => item.Component.Id == "runtime-desktop"))
        {
            notes.Add($".NET Desktop Runtime {(_ba.RuntimeStatus.DesktopRuntimeInstalled ? $"已装 {_ba.RuntimeStatus.DesktopRuntimeVersion}（跳过）" : "未装（将安装）")}");
        }

        if (plan.Components.Any(item => item.Component.Id == "runtime-aspnetcore"))
        {
            notes.Add($"ASP.NET Core Runtime {(_ba.RuntimeStatus.AspNetCoreRuntimeInstalled ? $"已装 {_ba.RuntimeStatus.AspNetCoreRuntimeVersion}（跳过）" : "未装（将安装）")}");
        }

        if (plan.Components.Any(item => item.Component.Id == "runtime-webview2"))
        {
            notes.Add($"WebView2 {(_ba.RuntimeStatus.WebView2Installed ? "已装（跳过）" : "未装（将安装）")}");
        }

        return string.Join("，", notes);
    }

    /// <summary>本机版本列（可升级清单）：升级 = 现版本 → 新版本；新装 = —（新装）；已最新 = 版本号（已是最新）。</summary>
    private static string DescribeLocalVersion(IReadOnlyList<ComponentUpgradeEntry> entries, string componentId)
    {
        var entry = entries.FirstOrDefault(candidate => candidate.ComponentId == componentId);
        if (entry is null)
        {
            return "—";
        }

        return entry.Action switch
        {
            ComponentUpgradeAction.Upgrade => $"{entry.InstalledVersion} → {entry.TargetVersion}",
            ComponentUpgradeAction.Install => "—（新装）",
            ComponentUpgradeAction.UpToDate => $"{entry.InstalledVersion ?? "已装"}（已是最新）",
            ComponentUpgradeAction.LocalNewer => $"{entry.InstalledVersion}（本机更新）",
            _ => "—（覆盖更新）", // webui 覆盖重写 / evergreen 已装等不比较形态
        };
    }

    public bool CanProceed(out string? reason)
    {
        if (_session.BuildPlan().Components.Count == 0)
        {
            reason = "本问卷结果无组件可安装，请返回「上一步」检查选项。";
            return false;
        }

        reason = null;
        return true;
    }
}
