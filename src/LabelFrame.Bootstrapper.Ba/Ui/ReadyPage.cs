using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>
/// 就绪页（/ ③ ⑤）：进入即自动加载安装清单（邻接布局清单 → 本地；否则稳定通道 URL——打开向导即联网的口径变化已记决策表）
/// 成功自动进问卷（基础模式默认角色 = 仅打印客户端）；失败给一句可行动提示并展开手动来源区（浏览本地清单 / 重试）。
/// 「高级选项」角落按钮（D3）：置位 <see cref="WizardSession.AdvancedMode"/> 后前进进入角色页与技术细节。
/// </summary>
internal sealed class ReadyPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly Action _proceed;
    private readonly Label _statusLabel = new();
    private readonly Button _advancedButton = new();
    private readonly Panel _manualPanel = new();
    private readonly TextBox _sourceTextBox = new();
    private readonly Button _browseButton = new();
    private readonly Button _retryButton = new();
    private bool _autoLoadStarted;

    public ReadyPage(WizardSession session, Action proceed)
    {
        _session = session;
        _proceed = proceed;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "安装 LabelFrame",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(8, 48);
        _statusLabel.MaximumSize = new Size(660, 0);

        // 手动来源区（默认折叠；加载失败或从确认页明细「更改」返回时展开）
        _manualPanel.Visible = false;
        _manualPanel.Location = new Point(8, 84);
        _manualPanel.Size = new Size(680, 118);

        var manualHint = new Label
        {
            Text = "安装信息来源（本地清单文件或下载地址）：",
            AutoSize = true,
            Location = new Point(0, 6),
        };
        _sourceTextBox.Location = new Point(0, 30);
        _sourceTextBox.Width = 540;
        _sourceTextBox.Text = _session.ManifestSource;

        _browseButton.Text = "浏览…";
        _browseButton.AutoSize = true;
        _browseButton.Location = new Point(552, 28);
        _browseButton.Click += (_, _) => BrowseLocalManifest();

        _retryButton.Text = "重试";
        _retryButton.AutoSize = true;
        _retryButton.Location = new Point(0, 66);
        _retryButton.Click += async (_, _) => await LoadManifestAsync();

        _manualPanel.Controls.Add(manualHint);
        _manualPanel.Controls.Add(_sourceTextBox);
        _manualPanel.Controls.Add(_browseButton);
        _manualPanel.Controls.Add(_retryButton);

        // 高级选项（D3：就绪页角落入口；链式样式按钮保证 Win32 UI 自动化可 BM_CLICK）
        _advancedButton.Text = "高级选项";
        _advancedButton.FlatStyle = FlatStyle.Flat;
        _advancedButton.ForeColor = Color.FromArgb(22, 84, 160);
        _advancedButton.BackColor = Color.Transparent;
        _advancedButton.FlatAppearance.BorderSize = 0;
        _advancedButton.AutoSize = true;
        _advancedButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _advancedButton.Location = new Point(8, 404);
        _advancedButton.Click += (_, _) =>
        {
            _session.AdvancedMode = true;
            _advancedButton.Enabled = false;
        };

        Controls.Add(title);
        Controls.Add(_statusLabel);
        Controls.Add(_manualPanel);
        Controls.Add(_advancedButton);
    }

    public bool ShouldSkip => false;

    public void OnEnter()
    {
        if (_session.Manifest is not null)
        {
            // 返回本页（后退 / 明细「更改」）：保持已加载状态，不再自动加载与前进
            _statusLabel.ForeColor = SystemColors.ControlText;
            _statusLabel.Text = DescribeReady();
            return;
        }

        if (!_autoLoadStarted)
        {
            _autoLoadStarted = true;
            _ = LoadManifestAsync();
        }
    }

    public bool CanProceed(out string? reason)
    {
        if (_session.Manifest is null)
        {
            reason = "安装信息尚未就绪，请检查网络后重试，或选择本地清单文件。";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>展开手动来源区（加载失败回退 / 确认页明细「更改」返回）。</summary>
    public void ExpandManualSource()
    {
        _manualPanel.Visible = true;
        _sourceTextBox.Text = _session.ManifestSource;
    }

    private void BrowseLocalManifest()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择安装清单（install-manifest.json）",
            Filter = "安装清单 (install-manifest.json)|install-manifest.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _sourceTextBox.Text = dialog.FileName;
            _ = LoadManifestAsync();
        }
    }

    private async Task LoadManifestAsync()
    {
        if (_manualPanel.Visible)
        {
            _session.ManifestSource = _sourceTextBox.Text.Trim();
        }

        _statusLabel.ForeColor = SystemColors.ControlText;
        _statusLabel.Text = "正在准备安装…";
        _advancedButton.Enabled = false;
        _browseButton.Enabled = false;
        _retryButton.Enabled = false;
        try
        {
            await _session.LoadManifestAsync();
            _statusLabel.Text = DescribeReady();

            // 基础模式默认角色 = 仅打印客户端（高级模式由角色页默认选择， ①）
            if (!_session.AdvancedMode)
            {
                _session.Preset ??= TopologyPreset.Client;
            }

            // 加载成功即进入问卷（清单是后续所有步骤的前提）
            _proceed();
        }
        catch (Exception ex)
        {
            // UI 边界兜底（文件不存在 / URL 不可达 / 清单非法等）：可行动提示 + 手动来源区，不打断向导
            _statusLabel.ForeColor = Color.Firebrick;
            _statusLabel.Text = $"获取安装信息失败：{ex.Message}";
            ExpandManualSource();
        }
        finally
        {
            _advancedButton.Enabled = !_session.AdvancedMode;
            _browseButton.Enabled = true;
            _retryButton.Enabled = true;
        }
    }

    private string DescribeReady() =>
        $"已就绪：LabelFrame {_session.Manifest!.LabelframeVersion}（{_session.Manifest.Components.Count} 个组件）。";
}
