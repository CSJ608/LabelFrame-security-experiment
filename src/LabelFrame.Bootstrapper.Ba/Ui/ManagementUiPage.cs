using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>管理界面开关页（仅服务端角色出现）：webui 组件落位服务端 plugins/web-ui——服务端 + 同机客户端默认关（客户端本机界面已完整），独立服务端建议开。</summary>
internal sealed class ManagementUiPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly CheckBox _switchCheckBox = new();
    private readonly Label _descriptionLabel = new();
    private bool _userToggled;

    public ManagementUiPage(WizardSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "是否安装服务端管理界面？",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _switchCheckBox.Text = "安装管理界面（服务端网页，供浏览器访问）";
        _switchCheckBox.AutoSize = true;
        _switchCheckBox.Location = new Point(16, 48);
        _switchCheckBox.CheckedChanged += (_, _) =>
        {
            _userToggled = true;
            _session.IncludeWebUi = _switchCheckBox.Checked;
        };

        _descriptionLabel.AutoSize = true;
        _descriptionLabel.Location = new Point(36, 80);
        _descriptionLabel.MaximumSize = new Size(620, 0);
        _descriptionLabel.ForeColor = SystemColors.GrayText;

        Controls.Add(title);
        Controls.Add(_switchCheckBox);
        Controls.Add(_descriptionLabel);
    }

    public bool ShouldSkip => !(_session.Preset?.IncludesServer() ?? false);

    public void OnEnter()
    {
        var preset = _session.Preset;

        // 开关适用性与建议：服务端 + 同机客户端默认不装（客户端本机界面已完整）；独立服务端建议装
        var descriptions = new Dictionary<TopologyPreset, string>
        {
            [TopologyPreset.Standalone] = "这台电脑的客户端本机界面已完整，一般无需另装管理界面。",
            [TopologyPreset.ServerWin] = "建议安装：服务端默认无界面，装好后可从局域网内任意浏览器访问管理。",
        };

        _descriptionLabel.Text = preset is { } value && descriptions.TryGetValue(value, out var text) ? text : string.Empty;

        // 首次进入按角色默认值（解析器也会按 topologies 过滤，此处让问卷状态与预期一致）
        if (!_userToggled)
        {
            _switchCheckBox.Checked = preset == TopologyPreset.ServerWin;
        }
    }

    public bool CanProceed(out string? reason)
    {
        reason = null;
        return true;
    }
}
