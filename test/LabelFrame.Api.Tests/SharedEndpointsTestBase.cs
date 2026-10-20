using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Api;
using LabelFrame.Api.Endpoints;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Logs;
using LabelFrame.Core.Templates;
using LabelFrame.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFrame.Api.Tests;

/// <summary>共享端点 HTTP 集成测试：TestServer 拉起真实路由（模板 / 出图 / Excel / 日志），断言状态码与错误形状。</summary>
public abstract class SharedEndpointsTestBase : IDisposable
{
    private readonly List<TestServerApp> _apps = [];

    protected static readonly string TemplateJson = """
        {
          "name": "集成测试模板",
          "group": "测试",
          "contract": {
            "name": "it-label", "version": "1.0",
            "fields": [ { "key": "code", "displayName": "编码", "isRequired": true, "type": "text" } ]
          },
          "layout": {
            "name": "it-layout", "contractName": "it-label", "contractVersion": "1.0",
            "widthMm": 100, "heightMm": 60,
            "elements": [ { "type": "text", "literal": "固定文本", "xMm": 5, "yMm": 5, "fontHeightMm": 5 } ]
          },
          "testData": { "code": "IT-001" }
        }
        """;

    /// <summary>拉起共享端点宿主：真实路由 + 临时 SQLite 模板库 / 日志库 + Skia 渲染。</summary>
    protected async Task<HttpClient> CreateClientAsync(int dpi = 203)
    {
        var templatesDb = Path.Combine(Path.GetTempPath(), $"lfapi-tpl-{Guid.NewGuid():N}.db");
        var logsDb = Path.Combine(Path.GetTempPath(), $"lfapi-log-{Guid.NewGuid():N}.db");
        var templates = new TemplateStore(templatesDb);
        await templates.InitializeAsync();
        var logs = new SqliteLogStore(logsDb);
        await logs.InitializeAsync();
        var renderer = new SkiaLabelRenderer();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNameCaseInsensitive = true;
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            json.SerializerOptions.Converters.Add(new LabelElementJsonConverter());
        });

        var app = builder.Build();
        app.MapTemplateApi(new TemplateApiOptions(templates, renderer, () => dpi, JobErrorCodesStub.InvalidRequest, JobErrorCodesStub.TemplateNotFound));
        app.MapRenderApi(new RenderApiOptions(templates, renderer, () => dpi, JobErrorCodesStub.InvalidRequest));
        app.MapImportApi(new ImportApiOptions(JobErrorCodesStub.InvalidRequest));
        app.MapLogApi(new LogApiOptions(logs, JobErrorCodesStub.InvalidRequest));
        await app.StartAsync();

        var client = app.GetTestClient();
        _apps.Add(new TestServerApp(app, client, templatesDb, logsDb));
        return client;
    }

    protected static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<JsonElement>(stream);
    }

    protected static int PngWidth(byte[] png) => BitConverter.ToInt32(png.Skip(16).Take(4).Reverse().ToArray());

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var entry in _apps)
        {
            entry.Client.Dispose();
            entry.App.DisposeAsync().AsTask().GetAwaiter().GetResult();
            TryDelete(entry.TemplatesDb);
            TryDelete(entry.LogsDb);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 临时文件清理失败忽略 */ }
    }

    private sealed record TestServerApp(WebApplication App, HttpClient Client, string TemplatesDb, string LogsDb);

    /// <summary>测试问题码（模拟宿主前缀，验证端点透传宿主自定义码）。</summary>
    private static class JobErrorCodesStub
    {
        public const string InvalidRequest = "LF_API_001";
        public const string TemplateNotFound = "LF_TPL_001";
    }
}
