using LabelFrame.WinHost.Transport;
using LabelFrame.WinHost.Ui;

namespace LabelFrame.WinHost;

/// <summary>WinHost：本地打印服务（作业队列 + HTTP API + 打印 Worker）。</summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
#if WINDOWS
        if (args.Contains("--update-service", StringComparer.OrdinalIgnoreCase))
        {
            await Updates.ClientUpdaterService.RunAsync();
            return;
        }
        if (RestartProcess.IsWaiter(args))
        {
            await RestartProcess.RunWaiterAsync(args);
            return;
        }
#endif
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args.Where(a => !a.Equals("--autostart", StringComparison.OrdinalIgnoreCase)
                && !a.Equals("--restart-plugins", StringComparison.OrdinalIgnoreCase)).ToArray(),
            ContentRootPath = AppContext.BaseDirectory,
        });
        var options = new HostOptions();
        builder.Configuration.GetSection("WinHost").Bind(options);
        options.ApplyEnvironmentOverrides();
        if (args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
        {
            options.OpenBrowser = false;
        }
        if (args.Contains("--restart-plugins", StringComparer.OrdinalIgnoreCase)) options.OpenBrowser = true;

#if !WINDOWS
        // Linux 首版是容器化无头测试客户端，传输能力固定为 Log。
        options.Transport = TransportMode.Log;
        options.OpenBrowser = false;
        options.EnableTray = false;
        options.WebUiPath = null;
#endif

#if WINDOWS
        // Serilog 装配必须经 BuildAsync 的 configureBuilder 挂到真实 builder——
        // 挂在本 builder（只用于配置绑定、从不 Build）上文件日志不会生效（曾出的缺陷根因）。
#endif

        var hostLogWriter = OpenHostLogWriter(options);
        void HostInfo(string message)
        {
            try
            {
                hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                hostLogWriter.Flush();
#if !WINDOWS
                Console.WriteLine($"[LabelFrame] {message}");
#endif
            }
            catch
            {
                // 日志写入失败不影响启动
            }
        }

#if WINDOWS
        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (singleInstance is null)
        {
            HostInfo("检测到 LabelFrame 已在运行，已请求显示界面窗口，本次启动退出。");
            SingleInstanceGuard.SignalActivate();
            return;
        }
#endif

        HostInfo($"LabelFrame 启动：监听 {options.ListenUrl}，DPI {options.Dpi}，OpenBrowser={options.OpenBrowser}，ServerUrl={options.ServerUrl ?? "(未配置路由)"}");

        // 启动时顺带执行一次出图目录保留清理（无后台任务；清理器永不抛出）
        new PrintImageRetentionCleaner(options.PrintOutputPath, options.PrintImageRetentionDays, hostLogWriter).CleanupExpired();

#if WINDOWS
        var app = await WinHostApp.BuildAsync(
            options,
            hostLogWriter,
            HostInfo,
            configureBuilder: b => SerilogSetup.Apply(b, options));
        // 启动自检：经 ILogger 写一条启动事件——文件通道失效时文件不出现，原因见 serilog-self.log
        app.Logger.LogInformation("文件日志自检：Serilog 文件通道已装配（级别 {LogLevel}，目录 {LogDirectory}）。", options.LogLevel, options.AppLogDirectory);
#else
        var app = await WinHostApp.BuildAsync(options, hostLogWriter, HostInfo);
#endif

#if WINDOWS
        // 界面壳：窗口形态（WebView2）优先，运行时不可用回退浏览器
        var localUiUrl = new Uri(UiUrl.ToLocalUiUrl(options.ListenUrl));
        if (args.Contains("--restart-plugins")) localUiUrl = new Uri(localUiUrl, "#/settings/settings.plugins");
        IUiShell? uiShell = null;
        if (options.OpenBrowser || options.EnableTray)
        {
            var windowShell = WindowUiShell.TryStart(
                localUiUrl,
                showInitially: options.OpenBrowser,
                hideOnClose: options.EnableTray,
                singleInstance.ActivateEvent,
                HostInfo);
            if (windowShell is null)
            {
                HostInfo("WebView2 运行时不可用，界面回退默认浏览器形态（可从微软官网安装 WebView2 运行时恢复窗口形态）。");
                uiShell = new BrowserUiShell(localUiUrl, openInitially: options.OpenBrowser, HostInfo);
            }
            else
            {
                uiShell = windowShell;
            }
        }
#endif

        app.Lifetime.ApplicationStopping.Register(() => HostInfo("ApplicationStopping"));
        app.Lifetime.ApplicationStopped.Register(() => HostInfo("ApplicationStopped"));

        var exit = app.Services.GetRequiredService<HostExitCoordinator>();

#if WINDOWS
        using var tray = new TrayIconService(HostInfo);
        var restart = app.Services.GetRequiredService<HostRestartCoordinator>();
        restart.Bind(showPlugins => RestartProcess.PrepareAsync(args, uiShell?.IsVisible ?? options.OpenBrowser, showPlugins));
        app.Services.GetRequiredService<Updates.ClientUpdateAgent>().Bind(args, () => uiShell?.IsVisible ?? options.OpenBrowser);
        if (options.EnableTray)
        {
            tray.Start(
                () => uiShell?.OpenUi(),
                () => exit.RequestShutdownAsync("托盘菜单「退出」"),
                async () =>
                {
                    var reason = await restart.RequestAsync(showPlugins: false);
                    if (reason is not null)
                    {
                        HostInfo($"重启未执行：{reason}");
                        MessageBox.Show(reason, "LabelFrame", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
            HostInfo("系统托盘已启用（双击或右键托盘图标可打开界面，退出请用托盘菜单）。");
        }

        // 退出清理统一注册：主流程 finally 与强退兜底共用同一入口（托盘 WM_QUIT 投递 + 界面壳释放）
        exit.RegisterCleanup(() =>
        {
            tray.Dispose();
            uiShell?.Dispose();
        });
#endif

#if WINDOWS
        var exitCode = 0;
        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            // WinExe 无控制台：失败原因写 host.log 并以消息框展示中文原因
            HostInfo($"LabelFrame 启动失败：{ex}");
            exitCode = 1;
            var friendly = DescribeStartupFailure(ex, options.ListenUrl);
            HostInfo(friendly);
            if (uiShell is null)
            {
                // 无界面壳（界面与托盘全关）时消息框无处展示，失败提示须同时写 stderr
                try
                {
                    Console.Error.WriteLine($"[LabelFrame] {friendly}");
                }
                catch
                {
                    // 无控制台附着时忽略
                }
            }
            else
            {
                uiShell.ShowFatalError(friendly);
            }
        }
#else
        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            HostInfo($"LabelFrame 启动失败：{ex}");
            Console.WriteLine($"[LabelFrame] {DescribeStartupFailure(ex, options.ListenUrl)}");
        }
#endif
        finally
        {
            // RunAsync 在托盘 / 界面消息循环在场时可能不自然返回——能走到本 finally 的都是自然路径；
            // 完成清理后标记自然退出（看门狗收手）
            exit.RunCleanup();
            HostInfo("宿主退出流程完成。");
            exit.MarkNaturalExitCompleted();
#if WINDOWS
            Environment.Exit(exitCode);
#endif
        }
    }

    /// <summary>启动失败的用户可读原因（端口占用等常见场景转中文可行动提示，其余透出原始信息）。</summary>
    public static string DescribeStartupFailure(Exception exception, string listenUrl)
    {
        if (IsPortInUse(exception))
        {
            return $"LabelFrame 启动失败：{listenUrl} 端口被占用。请关闭占用该端口的程序，或修改安装目录下 appsettings.json 的 ListenUrl 后重试。";
        }

        return $"LabelFrame 启动失败：{exception.Message}（详情见本机日志 host.log）";
    }

    private static bool IsPortInUse(Exception exception)
    {
        return exception is Microsoft.AspNetCore.Connections.AddressInUseException
            || exception.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException
            || exception is System.Net.Sockets.SocketException
            {
                SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse,
            }
            || exception.InnerException is System.Net.Sockets.SocketException
            {
                SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse,
            };
    }

    internal sealed class UnsupportedStatusProvider : Core.Transport.IPrinterStatusProvider
    {
        public Task<Core.Transport.PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new Core.Transport.PrinterStatusInfo(false, false, false, "当前传输不支持状态查询。"));
    }

    /// <summary>Log 传输 / 宿主日志写入器：host.log 按日轮转（保留 31 个）；打开失败回退空写入器不阻断启动。</summary>
    private static TextWriter OpenHostLogWriter(HostOptions options)
    {
        try
        {
            return TextWriter.Synchronized(new DailyRotatingFileWriter(options.HostLogPath, options.HostLogRetentionDays));
        }
        catch
        {
            return TextWriter.Null;
        }
    }
}
