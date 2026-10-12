using System.ServiceProcess;
using LabelFrame.Bootstrapper.Wizard;
using WixToolset.BootstrapperApplicationApi;

namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>
/// 完成页：结果摘要（装了什么 / 版本 / 跳过项 / 服务状态）+ 下一步指引（打开客户端 / 访问管理界面 /
/// PDA 到下载中心扫码）与重启提示。
/// </summary>
internal sealed class CompletePage : UserControl, IWizardPage
{
    private const string ManagementUiUrl = "http://127.0.0.1:53961/";
    private const string ServerServiceName = "LabelFrameServer";
    private const string ClientExeRelativePath = @"LabelFrame\Client\LabelFrame.WinHost.exe";

    private readonly WizardSession _session;
    private readonly LabelFrameBootstrapperBa _ba;
    private readonly ListView _listView = new();
    private readonly Label _summaryLabel = new();
    private readonly Button _openClientButton = new();
    private readonly Button _openManagementButton = new();
    private readonly Label _nextStepsLabel = new();
    private readonly Label _restartLabel = new();

    public CompletePage(WizardSession session, LabelFrameBootstrapperBa ba)
    {
        _session = session;
        _ba = ba;
        Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "安装完成",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _summaryLabel.AutoSize = true;
        _summaryLabel.Location = new Point(8, 38);

        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.HideSelection = true;
        _listView.Location = new Point(8, 64);
        _listView.Size = new Size(680, 220);
        _listView.Columns.Add("组件", 400);
        _listView.Columns.Add("版本", 80);
        _listView.Columns.Add("结果", 180);

        _openClientButton.Text = "打开打印客户端(&C)";
        _openClientButton.AutoSize = true;
        _openClientButton.Location = new Point(8, 296);
        _openClientButton.Click += (_, _) => OpenClient();

        _openManagementButton.Text = "打开管理界面(&M)";
        _openManagementButton.AutoSize = true;
        _openManagementButton.Location = new Point(170, 296);
        _openManagementButton.Click += (_, _) => OpenUrl(ManagementUiUrl);

        _nextStepsLabel.AutoSize = true;
        _nextStepsLabel.Location = new Point(8, 332);
        _nextStepsLabel.MaximumSize = new Size(680, 0);

        _restartLabel.AutoSize = true;
        _restartLabel.Location = new Point(8, 396);
        _restartLabel.MaximumSize = new Size(680, 0);
        _restartLabel.ForeColor = Color.FromArgb(154, 84, 0);

        Controls.Add(title);
        Controls.Add(_summaryLabel);
        Controls.Add(_listView);
        Controls.Add(_openClientButton);
        Controls.Add(_openManagementButton);
        Controls.Add(_nextStepsLabel);
        Controls.Add(_restartLabel);
    }

    public bool ShouldSkip => false;

    public void OnEnter()
    {
        var plan = _session.BuildPlan();
        var state = _ba.State;

        var installedServer = plan.Components.Any(item => item.Component.Id == "server-msi");
        var installedClient = plan.Components.Any(item => item.Component.Id == "client-msi");
        var installedWebUi = plan.Components.Any(item => item.Component.Id == "webui");

        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var item in plan.Components)
        {
            var row = new ListViewItem(DescribeComponent(item.Component.Id));
            row.SubItems.Add(item.Component.Version);
            row.SubItems.Add(DescribeOutcome(item.Component, state));
            _listView.Items.Add(row);
        }

        _listView.EndUpdate();

        _summaryLabel.Text = $"LabelFrame {_session.Manifest!.LabelframeVersion} 安装完成。"
            + (installedServer ? $"服务端 Windows 服务「{ServerServiceName}」状态：{DescribeServiceStatus()}。" : string.Empty);

        _openClientButton.Visible = installedClient;
        _openManagementButton.Visible = installedWebUi;

        var steps = new List<string>();
        if (installedClient)
        {
            steps.Add(_session.ServerUrl is { } url
                ? $"打印客户端可从开始菜单或托盘启动；已按向导连接服务端 {url}，如需更换可在客户端「设置」页修改。"
                : "打印客户端可从开始菜单或托盘启动。");
        }

        if (installedWebUi)
        {
            steps.Add($"管理界面：{ManagementUiUrl}（本机访问；局域网内用服务端 IP 访问）。");
            steps.Add("PDA（Android 宿主）装机：打开管理界面「下载中心」页，用 PDA 扫码下载 APK 安装。");
        }
        else if (installedServer)
        {
            steps.Add("PDA（Android 宿主）装机：需要管理界面时先安装 web-ui 组件（重跑本向导勾选管理界面），再从「下载中心」扫码下载。");
        }

        if (!installedServer && !installedClient && !installedWebUi)
        {
            steps.Add("本形态无本机安装组件（Docker / Linux 指引形态）——按上一步页面给出的指引部署。");
        }

        _nextStepsLabel.Text = "下一步：\n" + string.Join("\n", steps.Select(step => $"  · {step}"));
        _restartLabel.Visible = state.Restart != ApplyRestart.None;
        _restartLabel.Text = state.Restart != ApplyRestart.None
            ? "部分组件要求重启系统后才能完全生效，请尽快重启。"
            : string.Empty;
    }

    public bool CanProceed(out string? reason)
    {
        reason = null;
        return true;
    }

    private static string DescribeComponent(string componentId) => componentId switch
    {
        "runtime-desktop" => ".NET 10 Desktop Runtime（x64）",
        "runtime-aspnetcore" => "ASP.NET Core Runtime（x64）",
        "runtime-webview2" => "Microsoft Edge WebView2 运行时",
        "server-msi" => "LabelFrame 服务端（Windows 服务）",
        "client-msi" => "LabelFrame 打印客户端",
        "webui" => "服务端管理界面插件",
        "plugin-zebra" => "Zebra 传输官方插件",
        _ => componentId,
    };

    private string DescribeOutcome(LabelFrame.Bootstrapper.Manifest.ManifestComponent component, InstallState state)
    {
        if (component.Type == "runtime")
        {
            // 三条运行时各自对齐探测腿（曾误落 WebView2 探测腿致展示裸 id）
            var alreadyInstalled = component.Id switch
            {
                "runtime-desktop" => _ba.RuntimeStatus.DesktopRuntimeInstalled,
                "runtime-aspnetcore" => _ba.RuntimeStatus.AspNetCoreRuntimeInstalled,
                "runtime-webview2" => _ba.RuntimeStatus.WebView2Installed,
                _ => false,
            };
            if (alreadyInstalled)
            {
                return "已装，跳过";
            }
        }

        if (ChainPackageMap.PackageIdByComponent.TryGetValue(component.Id, out var packageId)
            && state.Packages.TryGetValue(packageId, out var run))
        {
            return run.Phase switch
            {
                PackagePhase.Succeeded => "已安装",
                PackagePhase.Downloaded or PackagePhase.Installing or PackagePhase.Downloading => "已处理",
                _ => "已处理",
            };
        }

        return "已处理"; // 非 Burn 链承载组件（如 linux-server 归档展示条目）
    }

    private static string DescribeServiceStatus()
    {
        try
        {
            using var service = new ServiceController(ServerServiceName);
            return service.Status switch
            {
                ServiceControllerStatus.Running => "运行中",
                ServiceControllerStatus.StartPending => "正在启动",
                ServiceControllerStatus.Stopped => "已停止（可在 services.msc 启动）",
                _ => $"{service.Status}",
            };
        }
        catch (Exception)
        {
            return "未查询到（可稍后在 services.msc 查看）";
        }
    }

    private void OpenClient()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ClientExeRelativePath);
            if (!File.Exists(path))
            {
                MessageBox.Show(this, $"未找到客户端程序：{path}", "打开打印客户端", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
            });
        }
        catch (SystemException ex)
        {
            MessageBox.Show(this, $"启动客户端失败：{ex.Message}", "打开打印客户端", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch (SystemException)
        {
            MessageBox.Show(this, $"请手动访问：{url}", "打开管理界面", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
