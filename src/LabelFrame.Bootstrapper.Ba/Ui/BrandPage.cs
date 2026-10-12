using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>打印机品牌多选页（仅含客户端角色出现）：选项来源仅为清单已有 plugin-&lt;brand&gt; 条目；ZDesigner 驱动名预选 Zebra。</summary>
internal sealed class BrandPage : UserControl, IWizardPage
{
    private readonly WizardSession _session;
    private readonly Label _hintLabel = new();
    private readonly FlowLayoutPanel _brandPanel = new();

    public BrandPage(WizardSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "需要哪些打印机品牌的支持？",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _hintLabel.AutoSize = true;
        _hintLabel.Location = new Point(8, 40);
        _hintLabel.MaximumSize = new Size(650, 0);
        _hintLabel.ForeColor = SystemColors.GrayText;

        _brandPanel.Location = new Point(16, 88);
        _brandPanel.Size = new Size(650, 220);
        _brandPanel.FlowDirection = FlowDirection.TopDown;
        _brandPanel.WrapContents = false;

        Controls.Add(title);
        Controls.Add(_hintLabel);
        Controls.Add(_brandPanel);
    }

    public bool ShouldSkip => !(_session.Preset?.IncludesClient() ?? false);

    public void OnEnter()
    {
        // 清单可能被重新加载（品牌集合变化）：每次进入重建
        _brandPanel.SuspendLayout();
        foreach (Control control in _brandPanel.Controls)
        {
            control.Dispose();
        }

        _brandPanel.Controls.Clear();

        var brands = _session.AvailableBrands;

        _hintLabel.Text = brands.Count == 0
            ? "当前安装清单没有品牌插件条目，可直接进入下一步（品牌插件可稍后在客户端「插件管理」安装）。"
            : "已按本机打印机驱动预选品牌；未勾选的品牌不安装其插件。";

        foreach (var brand in brands)
        {
            var checkBox = new CheckBox
            {
                Text = BrandDisplayName(brand),
                AutoSize = true,
                Checked = _session.SelectedBrands.Contains(brand),
                Tag = brand,
            };
            checkBox.CheckedChanged += (_, _) =>
            {
                if (checkBox.Checked)
                {
                    _session.SelectedBrands.Add(brand);
                }
                else
                {
                    _session.SelectedBrands.Remove(brand);
                }
            };

            _brandPanel.Controls.Add(checkBox);
        }

        _brandPanel.ResumeLayout();
    }

    public bool CanProceed(out string? reason)
    {
        // 品牌多选允许为空（不装任何品牌插件）
        reason = null;
        return true;
    }

    private static string BrandDisplayName(string brand) => brand.ToLowerInvariant() switch
    {
        "zebra" => "Zebra（斑马）",
        _ => brand,
    };
}
