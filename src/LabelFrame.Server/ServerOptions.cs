namespace LabelFrame.Server;

/// <summary>Server 配置：监听地址、数据库路径与历史清理保留期。</summary>
public sealed class ServerOptions
{
    /// <summary>默认监听地址（本机）。</summary>
    public const string DefaultListenUrl = "http://127.0.0.1:53961";

    /// <summary>默认数据目录：Windows %ProgramData%\LabelFrame\server（服务账户下 LOCALAPPDATA 不可靠）；Linux /var/lib/labelframe/server（systemd 约定）；LABELFRAME_SERVER_* 环境变量优先。</summary>
    public static string DefaultDataDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabelFrame", "server")
        : "/var/lib/labelframe/server";

    /// <summary>默认数据库路径（%ProgramData%\LabelFrame\server\server.db）。</summary>
    public static string DefaultDatabasePath => Path.Combine(DefaultDataDirectory, "server.db");

    /// <summary>监听地址。</summary>
    public string ListenUrl { get; set; } = DefaultListenUrl;

    /// <summary>对外通告基地址（http/https 绝对 URI，尾部斜杠已归一去除；null = 未配置，不通告）。</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>受信代理列表（逗号分隔 IP / CIDR；「*」＝信任全部；null / 空白 = 不启用，来源 IP 保持 TCP 对端）。LABELFRAME_SERVER_TRUSTED_PROXIES 可覆盖。</summary>
    public string? TrustedProxies { get; set; }

    /// <summary>SQLite 数据库路径。</summary>
    public string DatabasePath { get; set; } = DefaultDatabasePath;

    /// <summary>模板库路径（默认 %ProgramData%\LabelFrame\server\templates.db；与单机 WinHost 数据隔离）。</summary>
    public string TemplatesDbPath { get; set; } = Path.Combine(DefaultDataDirectory, "templates.db");

    /// <summary>设备日志库路径（默认 %ProgramData%\LabelFrame\server\logs.db）。</summary>
    public string LogsDbPath { get; set; } = Path.Combine(DefaultDataDirectory, "logs.db");

    /// <summary>渲染 DPI（调试出图 / 预览默认 203）。</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>终态作业（Completed / Failed / Expired）保留天数，超过则定期清理；非终态作业不清理。</summary>
    public int JobRetentionDays { get; set; } = 30;

    /// <summary>设备日志保留天数，超过则定期清理。</summary>
    public int LogRetentionDays { get; set; } = 90;

    /// <summary>历史清理周期（小时）。</summary>
    public int CleanupIntervalHours { get; set; } = 24;

    /// <summary>Pending 作业暂存 TTL（小时）：设备离线期间暂存的作业超过该时长视为「目标不可达」主动放弃（终态 Expired）；0 或负值 = 关闭过期（行为与现状一致）。</summary>
    public int PendingJobTtlHours { get; set; } = 12;

    /// <summary>解析后的 Pending 暂存 TTL；null = 关闭过期（不领取过滤、不扫描标记）。</summary>
    public TimeSpan? PendingJobTtl => PendingJobTtlHours > 0 ? TimeSpan.FromHours(PendingJobTtlHours) : null;

    /// <summary>Claimed 作业超时回收时长（分钟）：领取后超过该时长未回报终态视为「宿主失联」，回收为 Failed（LF_SRV_009）；0 或负值 = 关闭回收（行为与现状一致）。</summary>
    public int ClaimedJobTimeoutMinutes { get; set; } = 30;

    /// <summary>解析后的 Claimed 超时；null = 关闭回收（不扫描标记）。</summary>
    public TimeSpan? ClaimedJobTimeout => ClaimedJobTimeoutMinutes > 0 ? TimeSpan.FromMinutes(ClaimedJobTimeoutMinutes) : null;

    /// <summary>过期 / 超时扫描周期（分钟）：后台任务按此周期把超期 Pending 标记为 Expired、把失联 Claimed 回收为 Failed。</summary>
    public int ExpirationScanIntervalMinutes { get; set; } = 5;

    /// <summary>文本日志文件路径（为空不写文件；Linux 部署挂载到宿主机查看）。按日轮转：实际写入 <名>-yyyyMMdd.log。</summary>
    public string? LogFilePath { get; set; }

    /// <summary>文本日志文件个数保留上限（默认 31；0 或负 = 不清理）。LABELFRAME_SERVER_LOG_FILE_RETENTION_DAYS 可覆盖。
    /// 大小上限启用后单日可产生多个序号文件，实际语义为「文件个数上限」，文本层总占用上界 = 本项 × 单文件上限。</summary>
    public int LogFileRetentionDays { get; set; } = 31;

    /// <summary>文本日志单文件大小上限（MB，默认 50；0 或负 = 不限制）。LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB 可覆盖。超限轮转为当日序号文件。</summary>
    public int LogFileMaxSizeMB { get; set; } = 50;

    /// <summary>logs.db 按量闸阈值（MB，默认 256；0 或负 = 不限量闸、按期时间闸仍生效）。LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB 可覆盖。
    /// 库文件（logs.db + logs.db-wal 合计）超阈值时按大小删最旧直至回到阈值内——不受 <see cref="LogRetentionDays"/> 保留下限约束（有意行为），删除后 VACUUM 真实回收磁盘。</summary>
    public int LogsDbMaxSizeMB { get; set; } = 256;

    /// <summary>产品版本（与打包脚本 -Version 保持一致）。</summary>
    public const string ProductVersion = "1.2.0";

    /// <summary>客户端安装包目录（服务端统一分发客户端安装包；Windows %ProgramData%\\LabelFrame\\server\\client-packages；Linux /var/lib/labelframe/server/client-packages）。</summary>
    public static string DefaultClientPackagesPath => Path.Combine(DefaultDataDirectory, "client-packages");

    /// <summary>PDA（Android 宿主）安装包目录（服务端统一分发 PDA 的 APK；Windows %ProgramData%\\LabelFrame\\server\\pda-packages；Linux /var/lib/labelframe/server/pda-packages）。</summary>
    public static string DefaultPdaPackagesPath => Path.Combine(DefaultDataDirectory, "pda-packages");

    /// <summary>传输插件包目录（插件包上传服务端独立目录；Windows %ProgramData%\LabelFrame\server\plugin-packages；Linux /var/lib/labelframe/server/plugin-packages）。</summary>
    public static string DefaultPluginPackagesPath => Path.Combine(DefaultDataDirectory, "plugin-packages");

    /// <summary>传输插件包目录（存在即列出；目录直放文件或经 API 上传都支持，上传时解析 manifest 展示元数据）。</summary>
    public string PluginPackagesPath { get; set; } = DefaultPluginPackagesPath;
    /// <summary>客户端安装包目录（存在即列出；目录直放文件或经 API 上传都支持）。</summary>
    public string ClientPackagesPath { get; set; } = DefaultClientPackagesPath;
    /// <summary>PDA（Android 宿主）安装包目录（存在即列出；目录直放文件或经 API 上传都支持，上传仅接受 .apk）。</summary>
    public string PdaPackagesPath { get; set; } = DefaultPdaPackagesPath;

    /// <summary>默认服务端管理界面插件目录（Windows %ProgramData%\LabelFrame\server\plugins\web-ui；Linux /var/lib/labelframe/server/plugins/web-ui）。</summary>
    public static string DefaultWebUiPath => Path.Combine(DefaultDataDirectory, "plugins", "web-ui");

    /// <summary>服务端管理界面插件目录（目录存在即托管、放进去即时生效；为空 / 目录不存在 = 无头）。</summary>
    public string? WebUiPath { get; set; } = DefaultWebUiPath;

    /// <summary>应用 LABELFRAME_SERVER_* 环境变量覆盖。</summary>
    public void ApplyEnvironmentOverrides()
    {
        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LISTEN") is { } listen)
        {
            ListenUrl = listen;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_PUBLIC_BASE_URL") is { } publicBaseUrl)
        {
            // 非法值按未配置处理（保持 null）并告警，不阻断启动
            var candidate = publicBaseUrl.Trim();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var publicBaseUri)
                && (publicBaseUri.Scheme == Uri.UriSchemeHttp || publicBaseUri.Scheme == Uri.UriSchemeHttps))
            {
                PublicBaseUrl = candidate.TrimEnd('/');
            }
            else
            {
                Console.WriteLine(
                    $"[LabelFrame] 对外通告基地址无效（LABELFRAME_SERVER_PUBLIC_BASE_URL={publicBaseUrl}）：需为 http/https 绝对地址，已按未配置处理。");
            }
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_TRUSTED_PROXIES") is { } trustedProxies)
        {
            // 空白 = 显式关闭；条目级校验在受信集合解析处逐条告警跳过
            TrustedProxies = string.IsNullOrWhiteSpace(trustedProxies) ? null : trustedProxies.Trim();
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_DB") is { } db)
        {
            DatabasePath = db;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB") is { } templatesDb)
        {
            TemplatesDbPath = templatesDb;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB") is { } logsDb)
        {
            LogsDbPath = logsDb;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_DPI") is { } dpi && int.TryParse(dpi, out var dpiValue))
        {
            Dpi = dpiValue;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_JOB_RETENTION_DAYS") is { } jobDays && int.TryParse(jobDays, out var jobRetention))
        {
            JobRetentionDays = jobRetention;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LOG_RETENTION_DAYS") is { } logDays && int.TryParse(logDays, out var logRetention))
        {
            LogRetentionDays = logRetention;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_CLEANUP_INTERVAL_HOURS") is { } hours && int.TryParse(hours, out var intervalHours))
        {
            CleanupIntervalHours = intervalHours;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_PENDING_TTL_HOURS") is { } ttlHours && int.TryParse(ttlHours, out var pendingTtl))
        {
            PendingJobTtlHours = pendingTtl;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES") is { } timeoutMinutes && int.TryParse(timeoutMinutes, out var claimedTimeout))
        {
            ClaimedJobTimeoutMinutes = claimedTimeout;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_EXPIRATION_SCAN_MINUTES") is { } scanMinutes && int.TryParse(scanMinutes, out var expirationScan))
        {
            ExpirationScanIntervalMinutes = expirationScan;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE") is { } logFile)
        {
            LogFilePath = logFile;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE_RETENTION_DAYS") is { } logRetentionFiles
            && int.TryParse(logRetentionFiles, out var logFileRetention))
        {
            LogFileRetentionDays = logFileRetention;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB") is { } logFileMaxSize
            && int.TryParse(logFileMaxSize, out var logFileMaxSizeValue))
        {
            LogFileMaxSizeMB = logFileMaxSizeValue;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB") is { } logsDbMaxSize
            && int.TryParse(logsDbMaxSize, out var logsDbMaxSizeValue))
        {
            LogsDbMaxSizeMB = logsDbMaxSizeValue;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_PLUGIN_PACKAGES") is { } pluginPackages)
        {
            PluginPackagesPath = string.IsNullOrWhiteSpace(pluginPackages) ? DefaultPluginPackagesPath : pluginPackages;
        }
        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_CLIENT_PACKAGES") is { } clientPackages)
        {
            ClientPackagesPath = string.IsNullOrWhiteSpace(clientPackages) ? DefaultClientPackagesPath : clientPackages;
        }
        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_PDA_PACKAGES") is { } pdaPackages)
        {
            PdaPackagesPath = string.IsNullOrWhiteSpace(pdaPackages) ? DefaultPdaPackagesPath : pdaPackages;
        }

        if (Environment.GetEnvironmentVariable("LABELFRAME_SERVER_WEB_UI") is { } webUi)
        {
            // 环境变量为空 = 显式不启用插件；非空覆盖默认插件目录
            WebUiPath = string.IsNullOrWhiteSpace(webUi) ? null : webUi;
        }
    }
}
