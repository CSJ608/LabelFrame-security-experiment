namespace LabelFrame.Bootstrapper.Ba.Ui;

using System.Windows.Forms;
using LabelFrame.Bootstrapper.OfflineLayout;
using LabelFrame.Bootstrapper.Wizard;

/// <summary>
/// 离线布局目录生成进度窗：<c>--layout</c> 模式的轻量单窗呈现——
/// 总进度（组件 i/N）+ 当前组件行（下载字节数 / 复用 / 校验中）+ 取消（取消即中止下载、非零退出）。
/// </summary>
/// <remarks>
/// 生成完成（成功 / 失败 / 取消）后由 <see cref="AttachCompletion"/> 自动关闭消息循环；
/// 结果对话框与退出码由 BA 统一呈现（本窗只承载过程）。线程约定：<see cref="Report"/> 可从任意线程调用（封送 UI 线程）。
/// </remarks>
internal sealed class LayoutProgressForm : Form
{
    private readonly Label _overallLabel = new();
    private readonly Label _detailLabel = new();
    private readonly ProgressBar _componentBar = new();
    private readonly Button _cancelButton = new();
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>首显主动置前器：与向导同一缺陷类——无前台权启动时进度窗静默不前置。</summary>
    private readonly WizardForegroundActivator _foregroundActivator = new();

    private readonly Action<string>? _log;

    /// <summary>
    /// 创建进度窗。
    /// </summary>
    /// <param name="log">Burn 日志写入（可选；首显置前结果留痕）。</param>
    public LayoutProgressForm(Action<string>? log = null)
    {
        _log = log;
        Text = "LabelFrame 离线布局目录生成";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 168);
        ShowInTaskbar = false;

        _overallLabel.AutoSize = true;
        _overallLabel.Location = new Point(16, 16);

        _detailLabel.AutoSize = false;
        _detailLabel.Size = new Size(528, 40);
        _detailLabel.Location = new Point(16, 44);

        _componentBar.Dock = DockStyle.None;
        _componentBar.SetBounds(16, 92, 528, 20);

        _cancelButton.Text = "取消";
        _cancelButton.AutoSize = true;
        _cancelButton.Location = new Point(472, 124);
        _cancelButton.Click += (_, _) =>
        {
            _cancellation.Cancel();
            _cancelButton.Enabled = false;
            _detailLabel.Text = "正在取消…";
        };

        Controls.Add(_overallLabel);
        Controls.Add(_detailLabel);
        Controls.Add(_componentBar);
        Controls.Add(_cancelButton);
    }

    /// <summary>取消令牌（取消按钮触发；窗体关闭同样触发——取消生成并非零退出）。</summary>
    public CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>首显主动置前：与 <see cref="WizardForm"/> 同一组合拳（详见其 OnShown 注释）。</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var outcome = _foregroundActivator.Activate(Handle);
        _log?.Invoke($"布局进度窗首显置前：{WizardForegroundActivator.Describe(outcome)}。");
    }

    /// <summary>报告生成进度（任意线程可调；封送 UI 线程呈现）。</summary>
    public void Report(OfflineLayoutProgress progress)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action<OfflineLayoutProgress>(Report), progress);
            return;
        }

        var overall = $"组件 {progress.ComponentIndex + 1}/{progress.ComponentCount}";
        switch (progress.Phase)
        {
            case OfflineLayoutPhase.Reused:
                _overallLabel.Text = overall;
                _detailLabel.Text = $"{progress.ComponentId}：已存在且哈希与清单一致，复用（不重复下载）。";
                _componentBar.Value = 100;
                break;
            case OfflineLayoutPhase.Downloading:
                _overallLabel.Text = overall;
                _detailLabel.Text = $"{progress.ComponentId}：正在下载（{FormatBytes(progress.BytesReceived)} / {FormatBytes(progress.TotalBytes)}）\n{progress.CurrentUrl}";
                _componentBar.Maximum = 100;
                _componentBar.Value = progress.TotalBytes > 0
                    ? (int)Math.Min(100, 100 * progress.BytesReceived / progress.TotalBytes)
                    : 0;
                break;
            case OfflineLayoutPhase.Verifying:
                _overallLabel.Text = overall;
                _detailLabel.Text = $"{progress.ComponentId}：正在校验 SHA-256…";
                break;
            case OfflineLayoutPhase.Finalizing:
                _overallLabel.Text = $"组件 {progress.ComponentCount}/{progress.ComponentCount}";
                _detailLabel.Text = "正在写入 install-manifest.json / latest.json 并复制引导 EXE…";
                _componentBar.Value = 100;
                break;
            case OfflineLayoutPhase.Completed:
                _componentBar.Value = 100;
                break;
            default:
                break;
        }
    }

    /// <summary>绑定完成事件（后台任务结束即关闭消息循环；本窗不呈现结果——统一由 BA 结果对话框承担）。</summary>
    public void AttachCompletion(Task completion)
    {
        _ = completion.ContinueWith(
            _ => CloseIfAlive(),
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cancellation.Cancel(); // 关闭窗体 = 取消生成（消息循环由 AttachCompletion 或用户关闭结束）
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation.Dispose();
        }

        base.Dispose(disposing);
    }

    private void CloseIfAlive()
    {
        if (!IsDisposed && IsHandleCreated)
        {
            Close();
        }
    }

    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024d:0.0} MB"
        : $"{bytes / 1024d:0} KB";
}
