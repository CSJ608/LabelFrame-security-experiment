using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 服务端专属端点 HTTP 集成测试（WebApplicationFactory 拉起完整 Program：设备 / 作业 / 领取 / 回报全链路）。
/// 经 LABELFRAME_SERVER_* 环境变量把三库指向临时目录；共享端点（模板 / Excel / 日志）已由 LabelFrame.Api.Tests 覆盖。
/// </summary>
public sealed class ServerEndpointsTests : IDisposable
{
    private static readonly string TemplateJson = """
        {
          "name": "it-模板",
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

    public ServerEndpointsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lfserver-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", Path.Combine(_directory, "server.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", Path.Combine(_directory, "templates.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", Path.Combine(_directory, "logs.db"));
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Healthz_should_return_ok()
    {
        var response = await _client.GetFromJsonAsync<JsonElement>("/healthz");
        Assert.Equal("LabelFrame.Server", response.GetProperty("service").GetString());
        Assert.Equal("ok", response.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Full_routing_loop_should_register_submit_claim_and_report()
    {
        // 1) 设备注册 → 目录可见
        await PostOkAsync("/api/devices", """{ "deviceId": "pc-it", "name": "集成机" }""");
        var devices = await _client.GetFromJsonAsync<JsonElement>("/api/devices");
        Assert.Equal(1, devices.GetArrayLength());
        Assert.Equal("pc-it", devices[0].GetProperty("deviceId").GetString());

        // 2) 建模板（服务端模板库，共享端点经完整 Program 走通）
        await PostOkAsync("/api/templates", TemplateJson);

        // 3) 业务提交（templateName 引用模板库）→ 202 Pending
        var submit = await _client.PostAsync("/api/jobs", Json("""
            { "requestId": "req-it-1", "targetDeviceId": "pc-it", "templateName": "it-模板",
              "labels": [ { "data": { "code": "A-01" } } ] }
            """));
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("jobId").GetString();
        Assert.Equal("Pending", job.GetProperty("status").GetString());

        // 4) 幂等重放：同 requestId 返回同一作业，列表仍只有 1 条
        var replay = await _client.PostAsync("/api/jobs", Json("""
            { "requestId": "req-it-1", "targetDeviceId": "pc-it", "templateName": "it-模板",
              "labels": [ { "data": { "code": "A-01" } } ] }
            """));
        // 端点语义：作业仍为 Pending 时重放同样返回 202；幂等以 jobId 一致为准
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(jobId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString());
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/jobs?deviceId=pc-it");
        Assert.Equal(1, list.GetArrayLength());

        // 6) 领取：载荷含模板与 labels；再次领取为空（已 Claimed）
        // 领取前再查一次列表：既断言可领取前置状态（Pending + 属于本设备），失败时消息自带 DB 现场
        var beforeClaim = await _client.GetFromJsonAsync<JsonElement>("/api/jobs?deviceId=pc-it");
        Assert.True(beforeClaim.GetArrayLength() == 1
            && beforeClaim[0].GetProperty("status").GetString() == "Pending"
            && beforeClaim[0].GetProperty("jobId").GetString() == jobId,
            $"领取前状态异常：{beforeClaim.GetRawText()}");
        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-it/jobs/pending");
        Assert.True(claimed.GetArrayLength() == 1,
            $"领取为空：领取前列表 = {beforeClaim.GetRawText()}，领取结果 = {claimed.GetRawText()}");
        var payload = claimed[0].GetProperty("payload");
        Assert.Equal("it-模板", payload.GetProperty("template").GetProperty("name").GetString());
        Assert.Equal("A-01", payload.GetProperty("labels")[0].GetProperty("data").GetProperty("code").GetString());
        var claimedAgain = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-it/jobs/pending");
        Assert.Equal(0, claimedAgain.GetArrayLength());

        // 7) 回报完成 → 终态可见
        var report = await _client.PostAsync($"/api/devices/pc-it/jobs/{jobId}/result",
            Json("""{ "status": "Completed", "completedItems": 1, "failedItems": 0 }"""));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Completed", detail.GetProperty("status").GetString());

        // 8) 无待打作业时通知按超时返回 false（脉冲唤醒语义由 PendingJobNotifierTests 单元覆盖）
        var notify = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-it/jobs/notify?timeout=1");
        Assert.False(notify.GetProperty("hasPending").GetBoolean());
    }

    [Fact]
    public async Task Submit_to_unknown_device_should_return_404_with_server_code()
    {
        var response = await _client.PostAsync("/api/jobs", Json("""
            { "requestId": "req-x", "targetDeviceId": "no-such-device",
              "template": {
                "contract": { "name": "it", "version": "1.0", "fields": [] },
                "layout": { "name": "l", "contractName": "it", "contractVersion": "1.0", "widthMm": 40, "heightMm": 20, "elements": [] }
              },
              "labels": [ { "data": {} } ] }
            """));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_SRV_001", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Report_from_non_owner_should_be_forbidden()
    {
        await PostOkAsync("/api/devices", """{ "deviceId": "owner", "name": "领取者" }""");
        await PostOkAsync("/api/devices", """{ "deviceId": "other", "name": "无关设备" }""");
        var submit = await _client.PostAsync("/api/jobs", Json("""
            { "requestId": "req-owner-1", "targetDeviceId": "owner", "templateName": "it-模板",
              "labels": [ { "data": { "code": "B" } } ] }
            """));

        // 先建模板再提交（本用例独立于全链路用例）
        if (submit.StatusCode == HttpStatusCode.BadRequest)
        {
            await PostOkAsync("/api/templates", TemplateJson);
            submit = await _client.PostAsync("/api/jobs", Json("""
                { "requestId": "req-owner-1", "targetDeviceId": "owner", "templateName": "it-模板",
                  "labels": [ { "data": { "code": "B" } } ] }
                """));
        }

        var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsync(
            $"/api/devices/other/jobs/{job.GetProperty("jobId").GetString()}/result",
            Json("""{ "status": "Completed", "completedItems": 1 }"""))).StatusCode);
    }

    [Fact]
    public async Task By_ip_lookup_should_prefer_recently_active_device_when_same_ip()
    {
        // 集成复现：同 IP 双设备号（设备号变更后新设备号从同 IP 注册）——
        // by-ip 端点须命中最近活跃（新）行，而非先注册的旧行（旧行 stale 但 last_ip 保留）
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new RemoteIpHeaderStartupFilter("X-Test-Remote-IP"))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "192.168.7.77");

        var first = await client.PostAsync("/api/devices", Json("""{ "deviceId": "pc-old", "name": "旧设备号" }"""));
        Assert.True(first.IsSuccessStatusCode, $"旧设备号注册失败：{(int)first.StatusCode}");
        var second = await client.PostAsync("/api/devices", Json("""{ "deviceId": "pc-new", "name": "新设备号" }"""));
        Assert.True(second.IsSuccessStatusCode, $"新设备号注册失败：{(int)second.StatusCode}");

        var found = await client.GetFromJsonAsync<JsonElement>("/api/devices/by-ip/192.168.7.77");
        Assert.Equal("pc-new", found.GetProperty("deviceId").GetString());
        Assert.Equal("Online", found.GetProperty("status").GetString());
    }

    private async Task PostOkAsync(string url, string json)
    {
        var response = await _client.PostAsync(url, Json(json));
        Assert.True(response.IsSuccessStatusCode, $"{url} → {(int)response.StatusCode}");
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    /// <summary>测试来源模拟：按请求头改写 RemoteIpAddress（模拟同 IP 双设备号注册），插入管道最前。</summary>
    private sealed class RemoteIpHeaderStartupFilter(string headerName) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, forward) =>
                {
                    // TestServer 默认来源为 null——带请求头时按头改写（模拟同一来源 IP 的两次注册）
                    if (context.Request.Headers.TryGetValue(headerName, out var ip))
                    {
                        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip.ToString());
                    }

                    return forward();
                });
                next(app);
            };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _client.Dispose();
        _factory.Dispose();
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", null);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", null);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", null);
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch (IOException)
        {
            // WAL 文件句柄延迟释放时忽略临时目录清理失败
        }
    }
}
