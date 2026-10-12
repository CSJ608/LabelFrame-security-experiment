using System.Text.Json.Serialization;
using LabelFrame.Api;
using LabelFrame.Api.Endpoints;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.Core.Logs;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFrame.WinHost;

/// <summary>WinHost 应用装配：DI＋全部端点＋Web UI 静态托管。集成测试经同一装配拉 TestServer（测到的端点组合与生产一致），宿主层职责（日志 / 托盘 / 界面壳 / 退出）留在 Main。</summary>
public static class WinHostApp
{
#if WINDOWS
    private const string ServiceName = "LabelFrame.WinHost";
    private const string HostPlatform = "windows";
    private const bool IsHeadless = false;
#else
    private const string ServiceName = "LabelFrame.ClientHost";
    private const string HostPlatform = "linux";
    private const bool IsHeadless = true;
#endif

    /// <summary>装配并构建应用（含三库初始化与外部插件目录扫描）。</summary>
    /// <param name="options">宿主配置（路径与监听已就绪）。</param>
    /// <param name="hostInfo">宿主日志回调（Main 传 host.log 写入器；测试可传收集器）。</param>
    /// <param name="configureBuilder">Web 层扩展点（测试注入 UseTestServer 等），在最前执行。</param>
    /// <param name="configureServices">服务层扩展点（测试移除后台服务等），在全部服务注册完成后执行——顺序保证 RemoveAll 能生效。</param>
    public static async Task<WebApplication> BuildAsync(
        HostOptions options,
        TextWriter hostLogWriter,
        Action<string> hostInfo,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // 内容根固定为程序目录，保证从任意工作目录启动都能读到 appsettings.json
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseUrls(options.ListenUrl);
        // 插件包上传端点大小上限 64MB（Kestrel 默认约 30MB，超出会返回 413 且无错误体）
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = PluginPackageLimits.MaxBytes;
        });
        configureBuilder?.Invoke(builder);

        var transportRegistry = new TransportPluginRegistry();
#if WINDOWS
        foreach (var plugin in BuiltinTransportPlugins.CreateCorePlugins())
        {
            transportRegistry.Register(plugin);
        }

        transportRegistry.Register(new WinspoolTransportPlugin());

        // 迁移须先于下方外部插件目录扫描执行——本次启动即完成装配
        ZebraPluginMigration.Run(options, hostLogWriter, hostInfo);
#else
        options.Transport = TransportMode.Log;
        transportRegistry.Register(new LogTransportPlugin());
#endif
        var pluginContext = new TransportPluginContext(
            hostLogWriter,
            Path.GetDirectoryName(options.DatabasePath) ?? string.Empty);
#if WINDOWS
        var pluginLoad = PluginDirectoryLoader.LoadWithErrors(options.PluginsPath, hostLogWriter);
        foreach (var discovered in pluginLoad.Plugins)
        {
            // 外部插件不允许覆盖内置插件 ID（冲突时记日志跳过）；ALC 根随注册强持有
            if (transportRegistry.RegisterExternal(discovered.Plugin, discovered.AssemblyPath, hostLogWriter, discovered.LoadContext))
            {
                hostInfo($"已加载外部传输插件：{discovered.Plugin.Id}（{discovered.Plugin.DisplayName}，来自 {discovered.AssemblyPath}）");
            }
        }
#endif

        var transportManager = new TransportManager(transportRegistry, pluginContext, options, hostLogWriter, options.ConnectionPath);

        var hostConfigStore = new HostConfigStore(options.ConfigPath);
        builder.Services.AddSingleton(hostConfigStore);
        var printSettingsStore = new PrintSettingsStore(options.PrintSettingsPath);
        var printSettings = new PrintSettings();
        printSettings.Update(printSettingsStore.Load());
        builder.Services.AddSingleton(printSettingsStore);
        builder.Services.AddSingleton(printSettings);
        var machineServerUrl = options.HasServerUrlEnvironmentOverride ? null : hostConfigStore.LoadServerUrl();
        if (!string.IsNullOrWhiteSpace(machineServerUrl) && !string.Equals(machineServerUrl, options.ServerUrl, StringComparison.OrdinalIgnoreCase))
        {
            options.ServerUrl = machineServerUrl;
            hostInfo($"已加载机器级配置：ServerUrl={options.ServerUrl}");
        }

        var store = new SqliteLabelJobStore(options.DatabasePath);
        await store.InitializeAsync();
        var queue = new LabelJobQueue(store);

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNameCaseInsensitive = true;
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            json.SerializerOptions.Converters.Add(new LabelElementJsonConverter());
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ILabelJobStore>(store);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton<ITransportManager>(transportManager);
        builder.Services.AddSingleton<ITransportPluginRegistry>(transportRegistry);
#if WINDOWS
        builder.Services.AddSingleton(sp => new Core.Transport.Plugins.PluginInstaller(
            options.PluginsPath,
            sp.GetRequiredService<ITransportPluginRegistry>(),
            hostLogWriter,
            pluginLoad.Errors.ToDictionary(e => e.AssemblyPath, e => e.Error, StringComparer.OrdinalIgnoreCase)));
#endif
        builder.Services.AddSingleton<ZplImageEncoder>();
        builder.Services.AddHostedService<JobPrintWorker>();

        var templateStore = new TemplateStore(options.TemplatesDbPath);
        await templateStore.InitializeAsync();
        builder.Services.AddSingleton(templateStore);
        // 渲染器单例：DI 与共享端点（预览 / 调试出图）共用同一实例——预览与打印同源
        var skiaRenderer = new SkiaLabelRenderer();
        builder.Services.AddSingleton<ILabelBitmapRenderer>(skiaRenderer);
        var printRetentionCleaner = new PrintImageRetentionCleaner(options.PrintOutputPath, options.PrintImageRetentionDays, hostLogWriter);
        builder.Services.AddSingleton(sp => new JobSubmissionService(
            queue,
            sp.GetRequiredService<ZplImageEncoder>(),
            options.Dpi,
            skiaRenderer,
            templateStore,
            transportManager,
            transportRegistry,
            pluginContext,
            hostLogWriter,
            options.PrintOutputPath,
            printRetentionCleaner));

        builder.Services.AddCors(corsOptions => corsOptions.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        builder.Services.AddLabelFrameExceptionHandler();

        var logStore = new SqliteLogStore(options.LogsDbPath);
        await logStore.InitializeAsync();
        builder.Services.AddSingleton(logStore);

        builder.Services.AddSingleton(sp => new Routing.ServerRoutingCoordinator(
            sp,
            options,
            hostInfo));

        configureServices?.Invoke(builder.Services);

        var exitCoordinator = new HostExitCoordinator(hostInfo);
        builder.Services.AddSingleton(exitCoordinator);

        var app = builder.Build();
        exitCoordinator.Bind(app.Lifetime);

        var routingCoordinator = app.Services.GetRequiredService<Routing.ServerRoutingCoordinator>();
        routingCoordinator.Bind(app.Lifetime);
        await routingCoordinator.StartWithConfiguredUrlAsync();

        app.UseExceptionHandler();
        app.UseCors();

        app.MapGet("/healthz", (ITransportManager transportManager, ITransportPluginRegistry registry) =>
            Results.Ok(new
            {
                service = ServiceName,
                status = "ok",
                platform = HostPlatform,
                headless = IsHeadless,
                // 旧字段 transport 为兼容旧前端徽标保留
                transport = transportManager.CurrentConfig.Mode.ToString(),
                pluginId = transportManager.CurrentConfig.PluginId,
                displayText = registry.Describe(transportManager.CurrentConfig.PluginId, new TransportPluginParameters(transportManager.CurrentConfig.Params)),
            }));

        // 模板 / 出图 / 日志端点与 Server 共享（实现见 LabelFrame.Api.Endpoints）
        app.MapTemplateApi(new TemplateApiOptions(
            templateStore,
            skiaRenderer,
            options.Dpi,
            JobErrorCodes.InvalidRequest,
            ApiErrorCodes.TemplateNotFound));

        // 图片资源解析与共享出图端点同源：base64 附带优先、按名回退本地模板库
        app.MapRenderApi(new RenderApiOptions(templateStore, skiaRenderer, options.Dpi, JobErrorCodes.InvalidRequest));

        // 补挂该端点前客户端 /api/logs 命中 SPA 回退返回 index.html——必须保持 JSON + LF_API_001
        app.MapLogApi(new LogApiOptions(logStore, JobErrorCodes.InvalidRequest));

        // 宿主专属端点（分组实现见 Api/ 目录）
        app.MapTransportApi();
#if WINDOWS
        app.MapPluginApi();
#endif
        app.MapJobsApi();
        app.MapPrinterApi(options.Dpi);
        app.MapHostApi(hostInfo);

#if WINDOWS
        MapWebUi(app, options);
#endif
        return app;
    }

    /// <summary>Web UI 静态托管：探测 web/dist（配置优先，含仓库开发路径），未找到仅提供 API。</summary>
    private static void MapWebUi(WebApplication app, HostOptions options)
    {
        var webUiPath = ResolveWebUiPath(options);
        if (webUiPath is null)
        {
            Console.WriteLine("[LabelFrame] 未找到 Web UI 构建产物（web/dist），仅提供 API。");
            return;
        }

        var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webUiPath);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        app.MapFallback(async context =>
        {
            var indexFile = Path.Combine(webUiPath, "index.html");
            if (!File.Exists(indexFile))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(indexFile);
        });
        Console.WriteLine($"[LabelFrame] Web UI: {webUiPath}");
    }

    private static string? ResolveWebUiPath(HostOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.WebUiPath) && Directory.Exists(options.WebUiPath))
        {
            return options.WebUiPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "web", "dist"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web", "dist")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "web", "dist")),
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }
}
