using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 终态回调的 HTTP 集成测试（WebApplicationFactory 拉起完整 Program）：
/// 非法 scheme 提交 4xx 拒绝（既有错误码体系 + 作业不入队）；合法 callbackUrl 受理 202 且
/// 终态后作业视图透出投递状态。后台真实投递服务在场（回调地址指向回环保留端口，连接即拒，不外联）。
/// </summary>
public sealed class JobCallbackEndpointsTests : IDisposable
{
    private static readonly string TemplateJson = """
        {
          "name": "it-cb",
          "group": "测试",
          "contract": {
            "name": "it", "version": "1.0",
            "fields": [ { "key": "code", "displayName": "编码", "isRequired": true, "type": "text" } ]
          },
          "layout": {
            "name": "l", "contractName": "it", "contractVersion": "1.0",
            "widthMm": 40, "heightMm": 20,
            "elements": [ { "type": "text", "literal": "固定", "xMm": 1, "yMm": 1, "fontHeightMm": 3 } ]
          }
        }
        """;

    private readonly string _directory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public JobCallbackEndpointsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lfserver-cb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", Path.Combine(_directory, "server.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", Path.Combine(_directory, "templates.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", Path.Combine(_directory, "logs.db"));
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("example.com/hook")]
    public async Task Submit_with_non_http_callback_url_should_be_rejected_and_not_queued(string callbackUrl)
    {
        await PostOkAsync("/api/devices", """{ "deviceId": "pc-cb", "name": "回调机" }""");
        await PostOkAsync("/api/templates", TemplateJson);

        var response = await _client.PostAsync("/api/jobs", Json($$"""
            { "requestId": "req-cb-bad", "targetDeviceId": "pc-cb", "templateName": "it-cb",
              "callbackUrl": "{{callbackUrl}}",
              "labels": [ { "data": { "code": "A-01" } } ] }
            """));

        // 4xx 中文错误、走既有错误码体系（callbackUrl 校验拆出专属码 LF_SRV_013）
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ServerErrorCodes.InvalidCallbackUrl, error.GetProperty("code").GetString());
        Assert.Contains("callbackUrl", error.GetProperty("message").GetString());

        // 作业不入队
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/jobs?deviceId=pc-cb");
        Assert.Equal(0, list.GetArrayLength());
    }

    [Fact]
    public async Task Submit_with_valid_callback_url_should_be_accepted_and_expose_delivery_state()
    {
        await PostOkAsync("/api/devices", """{ "deviceId": "pc-cb", "name": "回调机" }""");
        await PostOkAsync("/api/templates", TemplateJson);

        // 合法 http 回调地址（回环保留端口：后台真实投递连接即拒，测试不依赖其结果、不外联）
        var submit = await _client.PostAsync("/api/jobs", Json("""
            { "requestId": "req-cb-ok", "targetDeviceId": "pc-cb", "templateName": "it-cb",
              "callbackUrl": "http://127.0.0.1:1/hook",
              "labels": [ { "data": { "code": "A-01" } } ] }
            """));
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("jobId").GetString();
        Assert.Equal("Pending", job.GetProperty("status").GetString());

        // 未到终态：无投递任务，视图字段为 null
        var before = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal(JsonValueKind.Null, before.GetProperty("callbackStatus").ValueKind);

        // 领取 + 回报终态 → 投递任务登记，视图可见投递状态与尝试计数
        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-cb/jobs/pending");
        Assert.Equal(jobId, claimed[0].GetProperty("jobId").GetString());
        var report = await _client.PostAsync($"/api/devices/pc-cb/jobs/{jobId}/result", Json("""
            { "status": "Completed", "completedItems": 1, "failedItems": 0 }
            """));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);

        var after = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Completed", after.GetProperty("status").GetString());
        Assert.Equal("Pending", after.GetProperty("callbackStatus").GetString());
        Assert.Equal(JsonValueKind.Number, after.GetProperty("callbackAttempts").ValueKind);
    }

    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // 测试清理失败不影响结果
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", null);
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", null);
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", null);
        }
    }

    private async Task<HttpResponseMessage> PostOkAsync(string url, string json)
    {
        var response = await _client.PostAsync(url, Json(json));
        Assert.True(response.IsSuccessStatusCode, $"{url} 失败：{response.StatusCode}");
        return response;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
