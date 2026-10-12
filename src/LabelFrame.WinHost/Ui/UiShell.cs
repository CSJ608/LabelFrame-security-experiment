namespace LabelFrame.WinHost.Ui;

/// <summary>
/// 界面壳：宿主展示界面的通道抽象。
/// 窗口形态 = WebView2 应用窗口；WebView2 运行时不可用时回退浏览器形态（功能不受损）。
/// </summary>
internal interface IUiShell : IDisposable
{
    /// <summary>显示界面并前置：窗口形态显示 / 激活应用窗口；浏览器形态打开默认浏览器。线程安全。</summary>
    void OpenUi();

    /// <summary>宿主启动失败的用户可见提示（中文消息框；WinExe 无控制台）。</summary>
    void ShowFatalError(string message);
}

/// <summary>浏览器形态界面壳：WebView2 运行时不可用时的兜底（前的唯一形态）。</summary>
internal sealed class BrowserUiShell : IUiShell
{
    private readonly Uri _uiUrl;
    private readonly Action<string>? _log;

    /// <param name="openInitially">启动时即打开界面（对齐旧 OpenBrowser 语义：延迟 1 秒等服务就绪）。</param>
    public BrowserUiShell(Uri uiUrl, bool openInitially, Action<string>? log = null)
    {
        _uiUrl = uiUrl;
        _log = log;
        if (openInitially)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000).ConfigureAwait(false);
                OpenUi();
            });
        }
    }

    /// <inheritdoc />
    public void OpenUi()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _uiUrl.ToString(),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log?.Invoke($"打开浏览器失败：{ex.Message}");
        }
    }

    /// <inheritdoc />
    public void ShowFatalError(string message) => MessageBox.Show(message, "LabelFrame", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
