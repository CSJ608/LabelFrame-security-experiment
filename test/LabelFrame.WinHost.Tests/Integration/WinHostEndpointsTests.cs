using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.TransportPlugin.Sample;
using LabelFrame.WinHost;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using static LabelFrame.WinHost.Integration.WinHostIntegrationTestBase;

namespace LabelFrame.WinHost.Integration;

/// <summary>
/// WinHost 端点 HTTP 集成测试：用与生产完全相同的 WinHostApp 装配拉起 TestServer；
/// 移除后台打印 Worker（作业状态全程确定性）；经 X-Test-Remote-IP 中间件模拟回环 / 非回环来源
/// （仅测试装配注入，生产行为不受影响）。
/// </summary>
public sealed class WinHostEndpointsTests : WinHostIntegrationTestBase
{
    [Fact]
    public async Task Healthz_should_report_log_plugin()
    {
        var body = await JsonAsync("/healthz");
        Assert.Equal("LabelFrame.WinHost", body.GetProperty("service").GetString());
        Assert.Equal("log", body.GetProperty("pluginId").GetString());
    }

    [Fact]
    public async Task Host_shutdown_should_reject_when_a_print_item_is_in_flight()
    {
        var queue = Services.GetRequiredService<LabelFrame.Core.Jobs.LabelJobQueue>();
        await queue.SubmitAsync($"shutdown-busy-{Guid.NewGuid():N}", ["^XA^XZ"]);
        await queue.ClaimNextItemAsync();

        using var response = await Client.PostAsync("/api/host/shutdown", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("正在打印", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Transport_get_should_list_builtin_plugins_and_post_invalid_should_400()
    {
        var config = await JsonAsync("/api/transport");
        Assert.Equal("log", config.GetProperty("pluginId").GetString());
        Assert.Contains(config.GetProperty("availablePlugins").EnumerateArray(),
            p => p.GetProperty("id").GetString() == "tcp9100");

        // 端点约定：切换 / 测试结果统一 200 + TransportApplyResponse（ok=false + 原因）
        var response = await Client.PostAsync("/api/transport", Json("""{ "pluginId": "no-such-plugin" }"""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Contains("不存在", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Transport_post_test_only_should_not_switch()
    {
        var response = await Client.PostAsync("/api/transport",
            Json("""{ "pluginId": "log", "testOnly": true }"""));
        Assert.True(response.IsSuccessStatusCode);
        var body = await JsonAsync(response);
        Assert.True(body.GetProperty("ok").GetBoolean());
        // testOnly 不切换：当前连接仍为 log
        Assert.Equal("log", (await JsonAsync("/api/transport")).GetProperty("pluginId").GetString());
    }

    [Fact]
    public async Task Transport_plugins_should_expose_supports_document_compile_flag()
    {
        // 能力位 DTO 透出（向后兼容增量字段）：GET /api/transport.availablePlugins
        // 与 /api/transport/plugins 均含 supportsDocumentCompile；内置插件全部为 false（无品牌编译器装配）
        var config = await JsonAsync("/api/transport");
        Assert.All(config.GetProperty("availablePlugins").EnumerateArray(),
            p => Assert.False(p.GetProperty("supportsDocumentCompile").GetBoolean()));

        var plugins = await JsonAsync("/api/transport/plugins");
        Assert.All(plugins.EnumerateArray(),
            p => Assert.False(p.GetProperty("supportsDocumentCompile").GetBoolean()));
    }

    [Fact]
    public async Task Transport_post_native_print_mode_without_capability_should_fail()
    {
        // 保存层校验：native 仅对能力位为真的插件合法；HTTP 端点走既有校验错误响应（ok=false + 中文提示）
        var response = await Client.PostAsync("/api/transport",
            Json("""{ "pluginId": "log", "params": { "printMode": "native" } }"""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Contains("原生指令", body.GetProperty("message").GetString());
        Assert.Contains("切回图片或更换插件", body.GetProperty("message").GetString());

        // 连接未切换
        Assert.Equal("log", (await JsonAsync("/api/transport")).GetProperty("pluginId").GetString());
    }

    [Fact]
    public async Task Jobs_full_lifecycle_should_be_deterministic_without_worker()
    {
        // 1) 提交自包含作业（3 张）
        var submit = await Client.PostAsync("/api/jobs", Json($$"""
            { "requestId": "it-1", "template": {
                "contract": { "name": "it", "version": "1.0", "fields": [ { "key": "code", "displayName": "编码", "isRequired": true, "type": "text" } ] },
                "layout": { "name": "l", "contractName": "it", "contractVersion": "1.0", "widthMm": 40, "heightMm": 20,
                            "elements": [ { "type": "text", "sourceKey": "code", "xMm": 2, "yMm": 2, "fontHeightMm": 4 } ] } },
              "labels": [ { "data": { "code": "A-1" } }, { "data": { "code": "A-2" } }, { "data": { "code": "A-3" } } ] }
            """));
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var job = await JsonAsync(submit);
        var jobId = job.GetProperty("jobId").GetString();
        Assert.Equal("Pending", job.GetProperty("status").GetString());

        // 2) 详情：3 张全部 Pending（Worker 已移除，状态确定性）
        var detail = await JsonAsync($"/api/jobs/{jobId}");
        Assert.Equal(3, detail.GetProperty("totalItems").GetInt32());
        Assert.All(detail.GetProperty("items").EnumerateArray(), i => Assert.Equal("Pending", i.GetProperty("status").GetString()));

        // 3) 幂等重放：同 requestId 返回同作业
        var replay = await Client.PostAsync("/api/jobs", Json($$"""
            { "requestId": "it-1", "template": {
                "contract": { "name": "it", "version": "1.0", "fields": [ { "key": "code", "displayName": "编码", "isRequired": true, "type": "text" } ] },
                "layout": { "name": "l", "contractName": "it", "contractVersion": "1.0", "widthMm": 40, "heightMm": 20,
                            "elements": [ { "type": "text", "sourceKey": "code", "xMm": 2, "yMm": 2, "fontHeightMm": 4 } ] } },
              "labels": [ { "data": { "code": "A-1" } } ] }
            """));
        Assert.Equal(jobId, (await JsonAsync(replay)).GetProperty("jobId").GetString());

        // 4) 挂起 → 恢复 → 取消
        Assert.Equal("Suspended", (await JsonAsync(await Client.PostAsync($"/api/jobs/{jobId}/suspend", null))).GetProperty("status").GetString());
        Assert.Equal("Pending", (await JsonAsync(await Client.PostAsync($"/api/jobs/{jobId}/resume", null))).GetProperty("status").GetString());
        var cancelled = await JsonAsync(await Client.PostAsync($"/api/jobs/{jobId}/cancel", null));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());

        // 5) 未知作业：404 + LF_JOB_001
        var missing = await Client.GetAsync("/api/jobs/no-such-job");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("LF_JOB_001", (await JsonAsync(missing)).GetProperty("code").GetString());

        // 6) 列表按 limit 返回
        var list = await JsonAsync("/api/jobs?limit=5");
        Assert.Equal(1, list.GetArrayLength());
    }

    [Fact]
    public async Task Jobs_submit_without_request_id_should_400()
    {
        var response = await Client.PostAsync("/api/jobs", Json("""{ "template": null, "labels": [] }"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Jobs_submit_malformed_json_should_be_400_with_bad_body_code()
    {
        // 此前落入全局异常处理器 → 500 LF_INTERNAL_001；现为调用方错误 400 + LF_API_BAD_BODY
        var response = await Client.PostAsync("/api/jobs", Json("""{ "requestId": "w-1", "labels": """));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("LF_API_BAD_BODY", body.GetProperty("code").GetString());
        Assert.Contains("JSON", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Jobs_submit_non_utf8_body_should_be_400_with_bad_body_code()
    {
        // GBK 编码的中文值：对 UTF-8 JSON 解析是非法字节序列
        var content = new ByteArrayContent([0x7B, 0x22, 0x6C, 0x61, 0x62, 0x65, 0x6C, 0x73, 0x22, 0x3A, 0x22, 0xD6, 0xD0, 0xCE, 0xC4, 0x22, 0x7D]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await Client.PostAsync("/api/jobs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("LF_API_BAD_BODY", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Host_config_get_post_and_loopback_guard()
    {
        // GET：默认 serverUrl 为空 + deviceId 透出 + 只读 version（客户端「检查更新」比较口径）
        var config = await JsonAsync("/api/host/config");
        Assert.Equal(string.Empty, config.GetProperty("serverUrl").GetString());
        Assert.NotNull(config.GetProperty("deviceId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(config.GetProperty("version").GetString()));

        // 非回环来源（测试中间件模拟）：403，不写配置
        Client.DefaultRequestHeaders.Add(RemoteIpHeader, "192.168.50.10");
        var forbidden = await Client.PostAsync("/api/host/config", Json("""{ "serverUrl": "http://10.0.0.1:53961" }"""));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Client.DefaultRequestHeaders.Remove(RemoteIpHeader);

        // 回环来源：合法地址保存并生效（后续 GET 读回；version 持续透出且不可经 POST 改写）
        var ok = await Client.PostAsync("/api/host/config", Json("""{ "serverUrl": "http://127.0.0.1:53961" }"""));
        Assert.True(ok.IsSuccessStatusCode);
        var roundtrip = await JsonAsync("/api/host/config");
        Assert.Equal("http://127.0.0.1:53961", roundtrip.GetProperty("serverUrl").GetString());
        Assert.Equal(HostOptions.ProductVersion, roundtrip.GetProperty("version").GetString());

        // 非法地址：400
        var bad = await Client.PostAsync("/api/host/config", Json("""{ "serverUrl": "not-a-url" }"""));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Shutdown_from_non_loopback_should_be_forbidden()
    {
        // 只测 403 分支：回环 POST 会真的停掉应用（留给真实验收）
        Client.DefaultRequestHeaders.Add(RemoteIpHeader, "10.1.1.1");
        var response = await Client.PostAsync("/api/host/shutdown", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Print_settings_endpoints_should_roundtrip_defaults()
    {
        var settings = await JsonAsync("/api/host/print-settings");
        Assert.False(settings.GetProperty("batchEnabled").GetBoolean());
        Assert.Equal(10, settings.GetProperty("batchSize").GetInt32());
        Assert.Equal(500, settings.GetProperty("batchIntervalMs").GetInt32());

        var invalid = await Client.PostAsync("/api/host/print-settings", Json("""{ "batchEnabled": true, "batchSize": 0 }"""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Plugins_install_and_uninstall_should_roundtrip()
    {
        Assert.Equal(0, (await JsonAsync("/api/plugins/installed")).GetArrayLength());

        // 安装样例插件包（manifest + Sample DLL）
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(BuildSamplePackage());
        form.Add(file, "file", "sample.lfplugin");
        var install = await Client.PostAsync("/api/plugins/install", form);
        Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());
        var installed = await JsonAsync("/api/plugins/installed");
        Assert.Equal(1, installed.GetArrayLength());
        Assert.Equal("sample", installed[0].GetProperty("pluginId").GetString());
        // 安装后需重启生效：本次运行未加载（loaded=false，来源为安装包）
        Assert.False(installed[0].GetProperty("loaded").GetBoolean());
        Assert.Equal("package", installed[0].GetProperty("source").GetString());

        // 卸载（删目录，重启生效）
        var uninstall = await Client.PostAsync("/api/plugins/uninstall", Json("""{ "pluginId": "sample" }"""));
        Assert.True(uninstall.IsSuccessStatusCode);

        // 缺少插件 ID：400
        var bad = await Client.PostAsync("/api/plugins/uninstall", Json("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Plugins_install_non_zip_should_be_400_with_chinese_message()
    {
        // 非 zip 文件：400 + LF_PLUGIN_INVALID + 纯中文可行动消息（不直出英文框架原话）
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("this is definitely not a zip archive"));
        form.Add(file, "file", "bad.lfplugin");

        var response = await Client.PostAsync("/api/plugins/install", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("LF_PLUGIN_INVALID", body.GetProperty("code").GetString());
        var message = body.GetProperty("message").GetString();
        Assert.Contains("不是 zip 格式", message);
        // 此前直出 .NET 英文原话（"End of Central Directory record could not be found."）
        Assert.DoesNotContain("End of Central Directory", message);
    }

    [Fact]
    public async Task Printer_status_should_report_log_transport_online()
    {
        var status = await JsonAsync("/api/printer/status");
        Assert.True(status.GetProperty("isOnline").GetBoolean());
    }

    [Fact]
    public async Task Printer_test_should_send_via_log_transport()
    {
        // Log 传输不失败：测试页正常发送（回归保护——错误分类不影响成功路径）
        var response = await Client.PostAsync("/api/printer/test", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.True(body.GetProperty("sent").GetBoolean());
        Assert.True(body.GetProperty("bytes").GetInt32() > 0);
    }

    [Fact]
    public async Task Logs_endpoints_should_return_json_and_store_per_line()
    {
        // 客户端实例 GET /api/logs 返回 JSON（补挂 MapLogApi 前命中 SPA 回退返回 index.html）
        var empty = await Client.GetAsync("/api/logs");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal("application/json", empty.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, (await JsonAsync(empty)).GetArrayLength());

        // POST 两行 → GET 回查为两条独立记录（按行拆分入库）
        var push = await Client.PostAsync("/api/logs",
            Json("""{ "deviceId": "pda-1", "lines": ["第一行", "第二行"] }"""));
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        Assert.Equal(2, (await JsonAsync(push)).GetProperty("received").GetInt32());

        var logs = await JsonAsync("/api/logs");
        Assert.Equal(2, logs.GetArrayLength());
        // 查询按 id 倒序（最新在前），两行内容各自独立
        Assert.Equal("第二行", logs[0].GetProperty("line").GetString());
        Assert.Equal("第一行", logs[1].GetProperty("line").GetString());

        // 缺少 deviceId：400 + LF_API_001（与宿主其他共享端点错误码一致）
        var bad = await Client.PostAsync("/api/logs", Json("""{ "lines": ["x"] }"""));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("LF_API_001", (await JsonAsync(bad)).GetProperty("code").GetString());
    }
}

/// <summary>集成测试装配：临时目录 HostOptions + 生产 WinHostApp + TestServer。</summary>
public abstract class WinHostIntegrationTestBase : IDisposable
{
    public const string RemoteIpHeader = "X-Test-Remote-IP";

    private readonly string _directory;
    private readonly WebApplication _app;
    protected HttpClient Client { get; }
    protected IServiceProvider Services => _app.Services;

    protected WinHostIntegrationTestBase()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lfhost-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        var options = new HostOptions
        {
            ListenUrl = "http://127.0.0.1:0",
            DatabasePath = Path.Combine(_directory, "jobs.db"),
            TemplatesDbPath = Path.Combine(_directory, "templates.db"),
            LogsDbPath = Path.Combine(_directory, "logs.db"),
            PluginsPath = Path.Combine(_directory, "plugins"),
            ConfigPath = Path.Combine(_directory, "settings.json"),
            PrintSettingsPath = Path.Combine(_directory, "print-settings.json"),
            ConnectionPath = Path.Combine(_directory, "connection.json"),
            HostLogPath = Path.Combine(_directory, "host.log"),
            Transport = TransportMode.Log,
            OpenBrowser = false,
            EnableTray = false,
        };

        _app = WinHostApp.BuildAsync(options, TextWriter.Null, _ => { }, builder =>
        {
            builder.WebHost.UseTestServer();
            // 测试专用：管道最前注入来源模拟中间件（生产行为不受影响；回环判定逻辑是端点自己的）
            builder.Services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(RemoteIpHeader));
        }, services =>
        {
            // 服务注册完成后移除后台打印 Worker（第二段扩展点保证 RemoveAll 在 AddHostedService 之后执行）
            services.RemoveAll<IHostedService>();
        }).GetAwaiter().GetResult();

        _app.StartAsync().GetAwaiter().GetResult();
        var server = (TestServer)_app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        Client = server.CreateClient();
    }

    protected async Task<JsonElement> JsonAsync(string url)
    {
        using var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>() is { ValueKind: not JsonValueKind.Undefined } body
            ? body
            : throw new InvalidOperationException("空响应");
    }

    protected static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    /// <summary>样例插件包（与 PluginInstallerTests 同构：manifest + Sample DLL）。</summary>
    protected static byte[] BuildSamplePackage(string pluginId = "sample")
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
            using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                w.Write($$"""{"pluginId":"{{pluginId}}","name":"示例插件","version":"1.0.0"}""");
            }

            var dll = zip.CreateEntry("LabelFrame.TransportPlugin.Sample.dll");
            using var dllStream = dll.Open();
            dllStream.Write(File.ReadAllBytes(typeof(SampleTransportPlugin).Assembly.Location));
        }
        return ms.ToArray();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败忽略
        }
    }

    /// <summary>测试来源模拟：按请求头改写 RemoteIpAddress（模拟非回环来源），插入管道最前。</summary>
    private sealed class RemoteIpStartupFilter(string headerName) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, forward) =>
                {
                    // TestServer 默认来源为 null（非回环）——测试统一模拟本机用户（回环），
                    // 带请求头时按头改写（模拟非回环来源）
                    context.Connection.RemoteIpAddress = context.Request.Headers.TryGetValue(headerName, out var ip)
                        ? System.Net.IPAddress.Parse(ip.ToString())
                        : System.Net.IPAddress.Loopback;
                    return forward();
                });
                next(app);
            };
    }
}
