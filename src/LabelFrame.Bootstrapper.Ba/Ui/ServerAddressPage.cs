using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>
/// 服务端地址页：仅含客户端的角色出现——「仅打印客户端」必填（已装机从 settings.json 预填）；
/// 「服务端 + 同机客户端」预填本机默认可改；规范化见 <see cref="ServerUrlInput.Normalize"/>，
/// 装后由 BA 写入 settings.json（装完即连上）。
/// </summary>
internal sealed class ServerAddressPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly TextBox _addressTextBox = new();
    private readonly Label _hintLabel = new();
    private bool _userEdited;

    public ServerAddressPage(WizardSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "打印客户端连接哪台服务端？",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        var addressLabel = new Label
        {
            Text = "服务端地址",
            AutoSize = true,
            Location = new Point(8, 48),
        };

        _addressTextBox.Location = new Point(8, 72);
        _addressTextBox.Width = 360;
        _addressTextBox.TextChanged += (_, _) => _userEdited = true;

        _hintLabel.AutoSize = true;
        _hintLabel.Location = new Point(8, 104);
        _hintLabel.MaximumSize = new Size(650, 0);
        _hintLabel.ForeColor = SystemColors.GrayText;

        Controls.Add(title);
        Controls.Add(addressLabel);
        Controls.Add(_addressTextBox);
        Controls.Add(_hintLabel);
    }

    public bool ShouldSkip => !(_session.Preset?.IncludesClient() ?? false);

    public void OnEnter()
    {
        // 预填：同机角色 → 本机默认；已装机器 → settings.json 已配地址；其余留空必填
        if (!_userEdited)
        {
            _addressTextBox.Text = _session.Preset == TopologyPreset.Standalone
                ? ServerUrlInput.LocalServerDefault
                : _session.ExistingServerUrl ?? string.Empty;
        }

        _hintLabel.Text = _session.Preset == TopologyPreset.Standalone
            ? "本机即服务端，默认指向本机，一般无需修改。"
            : "地址由管理员提供，例如 192.168.1.10 或 192.168.1.10:53961。";
    }

    public bool CanProceed(out string? reason)
    {
        var normalized = ServerUrlInput.Normalize(_addressTextBox.Text);
        if (normalized is null)
        {
            reason = "请填写服务端地址（例如 192.168.1.10 或 192.168.1.10:53961）。";
            return false;
        }

        _session.ServerUrl = normalized;
        reason = null;
        return true;
    }
}
