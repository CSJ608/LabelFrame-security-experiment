using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost;

/// <summary>传输模式。</summary>
public enum TransportMode
{
    /// <summary>日志模拟（默认，联调用）。</summary>
    Log,

    /// <summary>TCP 9100 网络打印机。</summary>
    Tcp,

    /// <summary>Windows 驱动（USB / 驱动安装的打印机），raw 指令。</summary>
    WindowsDriver,

    /// <summary>Zebra 官方 Link-OS SDK（TCP / USB / 驱动统一连接）。</summary>
    Zebra,
}


/// <summary>WinHost 配置：端口、数据库、传输、中文渲染。</summary>
public sealed class HostOptions
{
    /// <summary>默认监听地址（仅本机）。</summary>
    public const string DefaultListenUrl = "http://127.0.0.1:53960";

    /// <summary>产品版本（与打包脚本 -Version、ServerOptions.ProductVersion 同步更新——客户端「检查更新」比较口径）。</summary>
    public const string ProductVersion = "1.0.0";

    /// <summary>默认数据库目录（%LOCALAPPDATA%\LabelFrame）。</summary>
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "jobs.db");

    /// <summary>Kestrel 监听地址。</summary>
    public string ListenUrl { get; set; } = DefaultListenUrl;

    /// <summary>SQLite 数据库文件路径。</summary>
    public string DatabasePath { get; set; } = DefaultDatabasePath;

    /// <summary>模板库数据库路径（默认 %LOCALAPPDATA%\\LabelFrame\\templates.db）。</summary>
    public string TemplatesDbPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "templates.db");

    /// <summary>传输模式。</summary>
    public TransportMode Transport { get; set; } = TransportMode.Log;


    /// <summary>Zebra SDK 连接类型（Transport=Zebra 时生效）。</summary>
    public ZebraTransportKind ZebraKind { get; set; } = ZebraTransportKind.Tcp;

    /// <summary>Zebra USB 打印机名（为空时自动发现第一台）。</summary>
    public string ZebraUsbName { get; set; } = string.Empty;

    /// <summary>Server 地址（如 http://127.0.0.1:53921）；为空则不启用路由。</summary>
    public string? ServerUrl { get; set; }

    /// <summary>注册到 Server 的设备标识。</summary>
    public string DeviceId { get; set; } = Environment.MachineName;

    /// <summary>注册到 Server 的设备展示名。</summary>
    public string DeviceName { get; set; } = Environment.MachineName;

    /// <summary>Server 轮询间隔（秒）。</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>进度增量上报节流间隔毫秒（默认 1000，且计数有变化才发；0 或负值取默认）。</summary>
    public int ProgressIntervalMs { get; set; } = 1000;

    /// <summary>Serilog 文件日志级别（Trace/Debug/Information/Warning/Error；默认 Information；LABELFRAME_LOG_LEVEL 优先）。</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>Serilog 文件日志目录（默认 %LOCALAPPDATA%\LabelFrame\logs；测试 / 联调可重定向）。</summary>
    public string AppLogDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "logs");

    /// <summary>app-*.log 按日文件保留上限（默认 31；0 或负值 = 不清理；LABELFRAME_APP_LOG_RETENTION_DAYS 优先）。</summary>
    public int AppLogRetentionDays { get; set; } = 31;

    /// <summary>TCP 打印机主机 / IP。</summary>
    public string TcpHost { get; set; } = "127.0.0.1";

    /// <summary>TCP 打印机端口。</summary>
    public int TcpPort { get; set; } = 9100;

    /// <summary>Windows 驱动打印机名（如 "ZDesigner ZD421-203dpi ZPL"）。</summary>
    public string PrinterName { get; set; } = string.Empty;

    /// <summary>打印机 DPI。</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>中文渲染字体族（默认微软雅黑）。</summary>
    public string FontFamily { get; set; } = "Microsoft YaHei";

    /// <summary>可选中文字体文件路径（内嵌 / 本地字体，为空时用系统字体）。</summary>
    public string? FontFilePath { get; set; }

    /// <summary>Web UI 静态目录（前端构建产物，为空时自动探测 web/dist）。</summary>
    public string? WebUiPath { get; set; }

    /// <summary>机器级配置文件路径（默认 %ProgramData%\LabelFrame\Client\settings.json；UI 经 /api/host/config 读写）。</summary>
    public string ConfigPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabelFrame",
        "Client",
        "settings.json");

    /// <summary>设备日志库路径（默认 %LOCALAPPDATA%\LabelFrame\logs.db）。</summary>
    public string LogsDbPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "logs.db");

    /// <summary>外部传输插件目录（默认 %ProgramData%\\LabelFrame\\Client\\plugins；启动时扫描 *.dll）。</summary>
    public string PluginsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabelFrame",
        "Client",
        "plugins");

    /// <summary>批次作业设置文件路径（默认 %LOCALAPPDATA%\LabelFrame\print-settings.json，与 connection.json 同级，用户级）。</summary>
    public string PrintSettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "print-settings.json");

    /// <summary>连接配置文件路径（connection.json；TransportManager 持久化，测试可注入临时路径）。</summary>
    public string ConnectionPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "connection.json");

    /// <summary>Log 模拟打印 PNG 输出目录（默认 %LOCALAPPDATA%\LabelFrame\print）。</summary>
    public string PrintOutputPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "print");

    /// <summary>出图目录（print\&lt;jobId&gt）按天保留上限（默认 31；0 或负值 = 不清理；LABELFRAME_PRINT_IMAGE_RETENTION_DAYS 优先）。</summary>
    public int PrintImageRetentionDays { get; set; } = 31;

    /// <summary>Log 传输 / 宿主日志文件路径（默认 %LOCALAPPDATA%\LabelFrame\host.log）。按日轮转：实际写入 host-yyyyMMdd.log。</summary>
    public string HostLogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "host.log");

    /// <summary>host-*.log 按日文件保留上限（默认 31；0 或负值 = 不清理；LABELFRAME_HOST_LOG_RETENTION_DAYS 优先）。</summary>
    public int HostLogRetentionDays { get; set; } = 31;

    /// <summary>启动时显示界面（默认开＝显示应用窗口，WebView2 不可用回退浏览器；LABELFRAME_OPEN_BROWSER=0 关闭）。--autostart 强制关闭（仅托盘常驻）。</summary>
    public bool OpenBrowser { get; set; } = true;

    /// <summary>系统托盘图标（默认开启，可用 LABELFRAME_TRAY=0 关闭）。</summary>
    public bool EnableTray { get; set; } = true;

    /// <summary>ServerUrl 是否由环境变量明确提供；环境变量优先于持久化机器配置。</summary>
    internal bool HasServerUrlEnvironmentOverride { get; private set; }

    /// <summary>应用 LABELFRAME_* 环境变量覆盖（优先级最高）。</summary>
    public void ApplyEnvironmentOverrides()
    {
        if (GetEnv("LABELFRAME_LISTEN") is { } listen)
        {
            ListenUrl = listen;
        }

        if (GetEnv("LABELFRAME_DB") is { } db)
        {
            DatabasePath = db;
        }

        if (GetEnv("LABELFRAME_TEMPLATES_DB") is { } templatesDb)
        {
            TemplatesDbPath = templatesDb;
        }

        if (GetEnv("LABELFRAME_TRANSPORT") is { } transport)
        {
            Transport = Enum.Parse<TransportMode>(transport, ignoreCase: true);
        }


        if (GetEnv("LABELFRAME_TCP_HOST") is { } tcpHost)
        {
            TcpHost = tcpHost;
        }

        if (GetEnv("LABELFRAME_TCP_PORT") is { } tcpPort && int.TryParse(tcpPort, out var port))
        {
            TcpPort = port;
        }

        if (GetEnv("LABELFRAME_PRINTER") is { } printer)
        {
            PrinterName = printer;
        }

        if (GetEnv("LABELFRAME_ZEBRA_KIND") is { } zebraKind)
        {
            ZebraKind = Enum.Parse<ZebraTransportKind>(zebraKind, ignoreCase: true);
        }

        if (GetEnv("LABELFRAME_ZEBRA_USB") is { } zebraUsb)
        {
            ZebraUsbName = zebraUsb;
        }

        if (GetEnv("LABELFRAME_SERVER_URL") is { } serverUrl)
        {
            ServerUrl = serverUrl;
            HasServerUrlEnvironmentOverride = true;
        }

        if (GetEnv("LABELFRAME_DEVICE_ID") is { } deviceId)
        {
            DeviceId = deviceId;
        }

        if (GetEnv("LABELFRAME_DEVICE_NAME") is { } deviceName)
        {
            DeviceName = deviceName;
        }

        if (GetEnv("LABELFRAME_POLL_INTERVAL") is { } poll && int.TryParse(poll, out var pollSeconds))
        {
            PollIntervalSeconds = pollSeconds;
        }

        if (GetEnv("LABELFRAME_PROGRESS_INTERVAL_MS") is { } progressMs && int.TryParse(progressMs, out var progressInterval))
        {
            ProgressIntervalMs = progressInterval;
        }

        if (GetEnv("LABELFRAME_LOG_LEVEL") is { } logLevel)
        {
            LogLevel = logLevel;
        }

        if (GetEnv("LABELFRAME_APP_LOG_DIR") is { } appLogDir)
        {
            AppLogDirectory = appLogDir;
        }

        if (GetEnv("LABELFRAME_DPI") is { } dpi && int.TryParse(dpi, out var dpiValue))
        {
            Dpi = dpiValue;
        }

        if (GetEnv("LABELFRAME_FONT") is { } font)
        {
            FontFamily = font;
        }

        if (GetEnv("LABELFRAME_FONT_FILE") is { } fontFile)
        {
            FontFilePath = fontFile;
        }

        if (GetEnv("LABELFRAME_WEB_UI") is { } webUi)
        {
            WebUiPath = webUi;
        }

        if (GetEnv("LABELFRAME_LOGS_DB") is { } logsDb)
        {
            LogsDbPath = logsDb;
        }

        if (GetEnv("LABELFRAME_HOST_LOG") is { } hostLog)
        {
            HostLogPath = hostLog;
        }

        if (GetEnv("LABELFRAME_APP_LOG_RETENTION_DAYS") is { } appRetention && int.TryParse(appRetention, out var appRetentionDays))
        {
            AppLogRetentionDays = appRetentionDays;
        }

        if (GetEnv("LABELFRAME_HOST_LOG_RETENTION_DAYS") is { } hostRetention && int.TryParse(hostRetention, out var hostRetentionDays))
        {
            HostLogRetentionDays = hostRetentionDays;
        }

        if (GetEnv("LABELFRAME_PRINT_SETTINGS") is { } printSettings)
        {
            PrintSettingsPath = printSettings;
        }

        if (GetEnv("LABELFRAME_CONNECTION") is { } connectionPath)
        {
            ConnectionPath = connectionPath;
        }

        if (GetEnv("LABELFRAME_PRINT_OUTPUT") is { } printOutputPath)
        {
            PrintOutputPath = printOutputPath;
        }

        if (GetEnv("LABELFRAME_PRINT_IMAGE_RETENTION_DAYS") is { } printRetention && int.TryParse(printRetention, out var printRetentionDays))
        {
            PrintImageRetentionDays = printRetentionDays;
        }

        if (GetEnv("LABELFRAME_CONFIG") is { } configPath)
        {
            ConfigPath = configPath;
        }

        if (GetEnv("LABELFRAME_OPEN_BROWSER") is { } openBrowser)
        {
            OpenBrowser = openBrowser is "1" or "true" or "True";
        }

        if (GetEnv("LABELFRAME_PLUGINS") is { } plugins)
        {
            PluginsPath = plugins;
        }

        if (GetEnv("LABELFRAME_TRAY") is { } tray)
        {
            EnableTray = tray is "1" or "true" or "True";
        }
    }

    private static string? GetEnv(string name) => Environment.GetEnvironmentVariable(name);
}
