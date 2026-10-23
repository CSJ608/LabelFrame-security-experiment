using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LabelFrame.WinHost.Ui;

/// <summary>
/// 窗口形态界面壳：独立 STA UI 线程 + WinForms 主窗体 + WebView2 控件加载本地 UI 地址（未就绪先加载态）。
/// 关窗 = 隐藏到托盘、服务常驻；WebView2 运行时不可用时工厂返回 null（调用方回退 <see cref="BrowserUiShell"/>）。
/// </summary>
internal sealed class WindowUiShell : IUiShell
{
    private volatile bool _isVisible;
    public bool IsVisible => _isVisible;
    private readonly UiShellForm _form;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _threadDone = new(false);
    private RegisteredWaitHandle? _activateRegistration;
    private volatile bool _browserFallback;

    private WindowUiShell(UiShellForm form, Thread thread)
    {
        _form = form;
        _thread = thread;
    }

    /// <summary>WebView2 Evergreen 运行时是否可用（缺失时 MSI 引导安装 + 应用回退浏览器）。</summary>
    public static bool IsWebView2RuntimeAvailable()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString() is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启动窗口壳。WebView2 运行时不可用返回 null（调用方回退浏览器形态）。</summary>
    /// <param name="uiUrl">本地 UI 地址。</param>
    /// <param name="showInitially">启动即显示窗口（--autostart 仅托盘不弹窗）。</param>
    /// <param name="hideOnClose">用户关窗 = 隐藏到托盘（无托盘时关窗即退出宿主）。</param>
    /// <param name="activateEvent">单实例激活事件（二次启动 → 显示并前置窗口）。</param>
    /// <param name="log">host.log 写入回调。</param>
    public static WindowUiShell? TryStart(Uri uiUrl, bool showInitially, bool hideOnClose, EventWaitHandle activateEvent, Action<string> log)
    {
        if (!IsWebView2RuntimeAvailable())
        {
            return null;
        }

        var form = new UiShellForm(uiUrl, hideOnClose, log);
        var ready = new ManualResetEventSlim(false);
        WindowUiShell shell = null!;
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // 显式安装 WinForms 同步上下文：OnLoad 起即有 await 续体，未安装时续体会落到线程池，
            // WebView2 控件报「CoreWebView2Controller members can only be accessed from the UI thread」（本机冒烟实证）
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            try
            {
                shell._activateRegistration = ThreadPool.RegisterWaitForSingleObject(
                    activateEvent,
                    (state, _) =>
                    {
                        var f = (UiShellForm)state!;
                        if (f.IsDisposed || !f.IsHandleCreated)
                        {
                            return;
                        }

                        try
                        {
                            f.BeginInvoke(new Action(f.ShowAndActivate));
                        }
                        catch (InvalidOperationException)
                        {
                            // 窗体句柄已释放（退出中），忽略
                        }
                    },
                    form,
                    Timeout.Infinite,
                    executeOnlyOnce: false);

                _ = form.Handle; // 强制创建句柄（未显示的窗体也可跨线程 BeginInvoke）
                // 不设 MainForm：Application.Run 会自动显示 MainForm，与「--autostart 仅托盘不弹窗」冲突；
                // 改为 FormClosed 手动结束消息循环
                var context = new ApplicationContext();
                form.FormClosed += static (_, _) => Application.ExitThread();
                if (showInitially)
                {
                    // 经消息队列投递显示：确保 OnLoad 及其后的 await 续体都在消息泵运行后处理
                    form.BeginInvoke(new Action(form.ShowWithDefaultSize));
                }

                ready.Set();
                Application.Run(context);
            }
            catch (Exception ex)
            {
                log($"界面窗口线程异常退出，界面改以浏览器形态打开：{ex.Message}");
            }
            finally
            {
                shell._activateRegistration?.Unregister(null);
                ready.Set();
                shell._threadDone.Set();
            }
        })
        {
            Name = "LabelFrame.Ui",
            IsBackground = true,
        };

        shell = new WindowUiShell(form, thread);
        form.VisibleChanged += (_, _) => shell._isVisible = form.Visible;
        // WebView2 / WinForms 控件要求 STA；.NET 线程默认 MTA，必须在 Start 前设置
        // （缺失时 CoreWebView2Environment 初始化报 RPC_E_CHANGED_MODE 0x80010106）
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(10)))
        {
            log("界面窗口启动超时，界面将以浏览器形态打开。");
            shell._browserFallback = true;
        }

        return shell;
    }

    /// <inheritdoc />
    public void OpenUi()
    {
        if (_browserFallback || _form.BrowserFallback || _form.IsDisposed)
        {
            OpenSystemBrowser(_form.UiAddress);
            return;
        }

        try
        {
            _ = _form.BeginInvoke(new Action(_form.ShowAndActivate));
        }
        catch (InvalidOperationException)
        {
            OpenSystemBrowser(_form.UiAddress);
        }
    }

    /// <inheritdoc />
    public void ShowFatalError(string message)
    {
        // WinExe 无控制台：消息框自持消息泵，任意线程可直接调用
        MessageBox.Show(message, "LabelFrame", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>宿主退出：关闭窗口（绕过隐藏语义）并等待 UI 线程结束（WebView2 子进程随句柄释放退出）。</summary>
    public void Dispose()
    {
        _activateRegistration?.Unregister(null);
        if (!_form.IsDisposed && _form.IsHandleCreated)
        {
            try
            {
                _ = _form.BeginInvoke(new Action(_form.CloseForShutdown));
            }
            catch (InvalidOperationException)
            {
                // UI 线程已退出
            }
        }

        _threadDone.Wait(TimeSpan.FromSeconds(3));
        _threadDone.Dispose();
    }

    internal static void OpenSystemBrowser(Uri uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打开浏览器失败忽略（界面服务仍在，可手动访问地址）
        }
    }

    /// <summary>主窗体：标题 LabelFrame / 应用图标 / 1280×800 起步 / WebView2 铺满 + 加载态覆盖层。</summary>
    internal sealed class UiShellForm : Form
    {
        private readonly Uri _uiUrl;
        private readonly bool _hideOnClose;
        private readonly Action<string> _log;
        private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
        private readonly Label _status = new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "正在启动 LabelFrame 界面…",
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei", 12F),
        };
        private bool _allowClose;
        private bool _statusHidden;

        public UiShellForm(Uri uiUrl, bool hideOnClose, Action<string> log)
        {
            _uiUrl = uiUrl;
            _hideOnClose = hideOnClose;
            _log = log;
            Text = "LabelFrame";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 800);
            try
            {
                // 深色任务栏可辨优先用白色变体（与托盘一致）；资源异常回退 exe 应用图标
                var icon = TrayIconService.TryLoadTrayIcon(_log)
                    ?? (string.IsNullOrEmpty(Environment.ProcessPath)
                        ? null
                        : System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath));
                if (icon is not null)
                {
                    Icon = icon;
                }
            }
            catch
            {
                // 图标缺失不影响窗口功能（沿用窗体默认图标）
            }

            Controls.Add(_webView);
            Controls.Add(_status); // 后加入者在上层：加载态覆盖 WebView2，导航成功后隐藏
        }

        public Uri UiAddress => _uiUrl;

        /// <summary>WebView2 初始化失败，已回退浏览器形态（壳据此把后续 OpenUi 引向浏览器）。</summary>
        public bool BrowserFallback { get; private set; }

        /// <summary>显示并前置窗口（仅 UI 线程调用；单实例激活 / 托盘入口共用）。</summary>
        public void ShowAndActivate()
        {
            if (!Visible)
            {
                ShowWithDefaultSize();
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
            // 后台进程直接激活会被系统拒绝置前：置顶再还原强制获得前台
            TopMost = true;
            TopMost = false;
        }

        /// <summary>
        /// 显示前重申默认客户区尺寸：WebView2 初始化与窗体首次布局存在竞态，
        /// 首显窗口可能停在未展开尺寸（本机实测 864×293 而非 1280×800）；显示后再校验一次保证展开。
        /// </summary>
        internal void ShowWithDefaultSize()
        {
            ClientSize = new Size(1280, 800);
            Show();
            if (ClientSize.Width < 1000 || ClientSize.Height < 600)
            {
                ClientSize = new Size(1280, 800);
            }
        }

        /// <summary>宿主退出路径关窗（绕过「关窗即隐藏」语义）。</summary>
        public void CloseForShutdown()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                // 用户数据目录必须在可写位置（Program Files 下不可写）
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LabelFrame",
                    "webview2");
                Directory.CreateDirectory(userDataFolder);
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder).ConfigureAwait(true);
                await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
                _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                // HTTP 未就绪时窗口先显示加载态，就绪后自动导航（本地服务启动可能略晚于窗口）
                var healthUrl = new Uri(_uiUrl, "healthz");
                var ready = await Task.Run(() => UiReadiness.WaitUntilReadyAsync(
                    UiReadiness.HttpProbe(healthUrl, TimeSpan.FromMilliseconds(500)),
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromSeconds(60))).ConfigureAwait(true);
                if (!ready)
                {
                    _status.Text = "界面服务启动超时，请查看本机日志（host.log）后重新启动 LabelFrame。";
                    _log("界面就绪等待超时（60 秒），未导航到本地 UI。");
                    return;
                }

                _webView.CoreWebView2.Navigate(_uiUrl.ToString());
            }
            catch (Exception ex)
            {
                // 运行时初始化失败（如安装后被卸载）→ 回退默认浏览器 + host.log 记录原因
                BrowserFallback = true;
                _log($"WebView2 界面初始化失败，回退默认浏览器：{ex.Message}");
                OpenSystemBrowser(_uiUrl);
                CloseForShutdown();
            }
        }

        /// <summary>窗口内导航边界：本机地址（含监听地址指向的来源，如配置了本机局域网 IP）留在窗口内，其余走系统浏览器。</summary>
        private bool IsAllowedNavigation(Uri uri)
        {
            return UiUrl.IsLocalNavigation(uri)
                || string.Equals(uri.Authority, _uiUrl.Authority, StringComparison.OrdinalIgnoreCase);
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
                || uri.Scheme is "about" or "data" or "blob" or "chrome-devtools")
            {
                return;
            }

            if (IsAllowedNavigation(uri))
            {
                return;
            }

            // 防误导航：外链（帮助 / 下载等）交给系统浏览器
            e.Cancel = true;
            OpenSystemBrowser(uri);
        }

        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // 窗口不弹二级窗：允许的地址收进当前窗口，外链走系统浏览器
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && IsAllowedNavigation(uri))
            {
                _webView.CoreWebView2.Navigate(uri.ToString());
            }
            else
            {
                OpenSystemBrowser(new Uri(e.Uri));
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_statusHidden)
            {
                return;
            }

            if (e.IsSuccess)
            {
                _statusHidden = true;
                _status.Visible = false;
            }
            else if (e.NavigationId != 0)
            {
                _status.Text = "界面加载失败，请查看本机日志（host.log）后重新启动 LabelFrame。";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // 关窗 = 隐藏到托盘、服务常驻（真正退出走托盘「退出」/ 宿主停止）；
            // 无托盘模式没有恢复入口，关窗即退出宿主
            var hide = !_allowClose && _hideOnClose && e.CloseReason == CloseReason.UserClosing;
            _log($"界面窗口 FormClosing：CloseReason={e.CloseReason}，allowClose={_allowClose}，hideOnClose={_hideOnClose}，处理={(hide ? "隐藏到托盘" : "关闭窗口")}");
            if (hide)
            {
                e.Cancel = true;
                Hide();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _log($"界面窗口已关闭（CloseReason={e.CloseReason}）。");
        }
    }
}
