using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// Pending 暂存 TTL 的 HTTP 集成测试（WebApplicationFactory 拉起完整 Program，TTL = 1 小时）。
/// 超期回填经 SQL 直改 created_at 模拟；过期扫描由测试显式触发一次（宿主后台扫描启动延迟 30 秒，不在此等待），
/// 以此同时验证「领取过滤在扫描未运行时依然拦截」（双保险）。
/// </summary>
public sealed class PendingExpirationEndpointsTests : IDisposable
{
    private static readonly string TemplateJson = """
        {
          "name": "it-ttl",
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

    private readonly IsolatedServerEnvironment _environment = new();
    private string _directory => _environment.DirectoryPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PendingExpirationEndpointsTests()
    {
        _environment.Set("LABELFRAME_SERVER_PENDING_TTL_HOURS", "1");
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    private string DbPath => Path.Combine(_directory, "server.db");

    [Fact]
    public async Task Overdue_pending_should_not_be_claimed_even_before_scan()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        var jobId = await RegisterSubmitAndBackdateAsync("ttl-req-1", hours: 2);

        // 扫描从未运行：作业仍显示 Pending（标记是扫描的职责）
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Pending", detail.GetProperty("status").GetString());

        // 但领取查询按 CreatedAt + TTL 过滤，超期作业一律不下发（正确性不依赖扫描周期）
        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-ttl/jobs/pending");
        Assert.Equal(0, claimed.GetArrayLength());

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/jobs?deviceId=pc-ttl");
        Assert.Equal(1, list.GetArrayLength());
    }

    [Fact]
    public async Task Replay_of_expired_job_should_return_expired_without_redelivery()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        var jobId = await RegisterSubmitAndBackdateAsync("ttl-req-2", hours: 2);

        // 显式触发一次过期扫描（等同后台任务的单次执行）
        var scanner = new PendingJobExpirationService(
            new ServerDb(DbPath),
            new ServerOptions { PendingJobTtlHours = 1 },
            TimeProvider.System,
            NullLogger<PendingJobExpirationService>.Instance);
        Assert.Equal(1, await scanner.ScanOnceAsync());

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Expired", detail.GetProperty("status").GetString());
        Assert.Contains("已放弃", detail.GetProperty("errorMessage").GetString());

        // 同一 requestId 重放：只返回既有 Expired 作业（200，非 202），不重新投递
        var replay = await _client.PostAsync("/api/jobs", Json(SubmitBody("ttl-req-2")));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, replayed.GetProperty("jobId").GetString());
        Assert.Equal("Expired", replayed.GetProperty("status").GetString());

        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-ttl/jobs/pending");
        Assert.Equal(0, claimed.GetArrayLength());
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/jobs?deviceId=pc-ttl");
        Assert.Equal(1, list.GetArrayLength());
    }

    [Fact]
    public async Task Notify_should_return_immediately_when_backlog_exists()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        await PostOkAsync("/api/devices", """{ "deviceId": "pc-ttl", "name": "积压机" }""");
        await PostOkAsync("/api/templates", TemplateJson);
        await PostOkAsync("/api/jobs", SubmitBody("ttl-req-3"));

        // 提交时的唤醒脉冲已空放（当时没有等待者）：修复前这里会空等满 timeout 才返回 false
        var stopwatch = Stopwatch.StartNew();
        var notify = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-ttl/jobs/notify?timeout=5");
        stopwatch.Stop();
        Assert.True(notify.GetProperty("hasPending").GetBoolean());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4),
            $"notify 应立即返回，实际耗时 {stopwatch.Elapsed}（长轮询空等说明积压预检失效）");
    }

    private async Task<string> RegisterSubmitAndBackdateAsync(string requestId, int hours)
    {
        await PostOkAsync("/api/devices", """{ "deviceId": "pc-ttl", "name": "积压机" }""");
        await PostOkAsync("/api/templates", TemplateJson);
        var submit = await _client.PostAsync("/api/jobs", Json(SubmitBody(requestId)));
        Assert.True(submit.IsSuccessStatusCode, $"提交失败：{(int)submit.StatusCode}");
        var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("jobId").GetString();
        Assert.Equal("Pending", job.GetProperty("status").GetString());

        // 直改 created_at 模拟超期（与真实时间流逝等价，避免测试等待）
        await using var connection = new SqliteConnection($"Data Source={DbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE server_jobs SET created_at = $time WHERE request_id = $requestId;";
        command.Parameters.AddWithValue("$time", LabelFrame.Core.Data.SqliteSupport.Format(DateTimeOffset.UtcNow.AddHours(-hours)));
        command.Parameters.AddWithValue("$requestId", requestId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return jobId!;
    }

    private static string SubmitBody(string requestId) => """
        { "requestId": "%REQUEST_ID%", "targetDeviceId": "pc-ttl", "templateName": "it-ttl",
          "labels": [ { "data": { "code": "A-01" } } ] }
        """.Replace("%REQUEST_ID%", requestId);

    private async Task PostOkAsync(string url, string json)
    {
        var response = await _client.PostAsync(url, Json(json));
        Assert.True(response.IsSuccessStatusCode, $"{url} → {(int)response.StatusCode}");
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _client.Dispose();
        _factory.Dispose();
        _environment.Dispose();
    }
}

/// <summary>TTL 关闭（0）回归锚点：行为与引入 TTL 前完全一致——超龄 Pending 照常下发领取。</summary>
public sealed class TtlDisabledEndpointsTests : IDisposable
{
    private readonly IsolatedServerEnvironment _environment = new();
    private string _directory => _environment.DirectoryPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TtlDisabledEndpointsTests()
    {
        _environment.Set("LABELFRAME_SERVER_PENDING_TTL_HOURS", "0");
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Ttl_disabled_should_deliver_stale_pending_jobs_as_before()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        await PostAsync("/api/devices", """{ "deviceId": "pc-old", "name": "老机器" }""");
        await PostAsync("/api/templates", """
            {
              "name": "it-ttl",
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
            """);
        var submit = await PostAsync("/api/jobs", """
            { "requestId": "ttl0-req-1", "targetDeviceId": "pc-old", "templateName": "it-ttl",
              "labels": [ { "data": { "code": "A-01" } } ] }
            """);
        Assert.True(submit.IsSuccessStatusCode, $"提交失败：{(int)submit.StatusCode}");
        var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("jobId").GetString();

        // 2020 年创建的 Pending：TTL 关闭时不被过滤，照常领取并完成（现状回归）
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "server.db")}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE server_jobs SET created_at = $time WHERE request_id = $requestId;";
            command.Parameters.AddWithValue("$time", "2020-01-01T00:00:00.0000000+00:00");
            command.Parameters.AddWithValue("$requestId", "ttl0-req-1");
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-old/jobs/pending");
        Assert.Equal(1, claimed.GetArrayLength());

        var report = await _client.PostAsync($"/api/devices/pc-old/jobs/{jobId}/result",
            new StringContent("""{ "status": "Completed", "completedItems": 1, "failedItems": 0 }""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Completed", detail.GetProperty("status").GetString());
    }

    private async Task<HttpResponseMessage> PostAsync(string url, string json)
        => await _client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _client.Dispose();
        _factory.Dispose();
        _environment.Dispose();
    }
}
