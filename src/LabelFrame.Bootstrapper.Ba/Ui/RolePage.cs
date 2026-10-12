using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>本机角色页（仅高级模式出现，基础模式由就绪页默认「仅打印客户端」直接越过）。</summary>
internal sealed class RolePage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly Dictionary<TopologyPreset, RadioButton> _radioButtons = [];

    public RolePage(WizardSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "这台电脑的用途是什么？",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        var roles = new[]
        {
            TopologyPreset.Client,
            TopologyPreset.Standalone,
            TopologyPreset.ServerWin,
        };

        var y = 48;
        foreach (var role in roles)
        {
            var radio = new RadioButton
            {
                Text = role.DisplayName(),
                AutoSize = true,
                Location = new Point(16, y),
                Tag = role,
            };
            var description = new Label
            {
                Text = role.Description(),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(36, y + 24),
                MaximumSize = new Size(620, 0),
            };
            radio.CheckedChanged += (_, _) =>
            {
                if (radio.Checked)
                {
                    _session.Preset = role;
                }
            };

            _radioButtons[role] = radio;
            Controls.Add(radio);
            Controls.Add(description);
            y += 56;
        }

        Controls.Add(title);
    }

    public bool ShouldSkip => !_session.AdvancedMode;

    public void OnEnter()
    {
        // 回显已选角色；首次进入默认「仅打印客户端」（导航往返）
        var preset = _session.Preset ?? TopologyPreset.Client;
        _session.Preset ??= preset;
        if (_radioButtons.TryGetValue(preset, out var radio))
        {
            radio.Checked = true;
        }
    }

    public bool CanProceed(out string? reason)
    {
        if (_session.Preset is null)
        {
            reason = "请选择这台电脑的用途。";
            return false;
        }

        reason = null;
        return true;
    }
}
