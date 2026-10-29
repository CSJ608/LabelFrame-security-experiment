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
/// Claimed 超时回收的 HTTP 集成测试（WebApplicationFactory 拉起完整 Program，超时 = 30 分钟）。
/// 场景：作业被领取后宿主消失（不回报）→ 超期（claimed_at 经 SQL 直改模拟时间流逝）→ 扫描回收为
/// Failed（LF_SRV_009）→ 业务系统查询得到终态与原因；领取查询无二次投递；迟到回报幂等重放。
/// 过期扫描由测试显式触发一次（宿主后台扫描启动延迟 30 秒，不在此等待）。
/// </summary>
public sealed class ClaimedTimeoutEndpointsTests : IDisposable
{
    private static readonly string TemplateJson = """
        {
          "name": "it-claimed",
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

    public ClaimedTimeoutEndpointsTests()
    {
        _environment.Set("LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES", "30");
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    private string DbPath => Path.Combine(_directory, "server.db");

    [Fact]
    public async Task Host_lost_after_claim_should_end_as_failed_with_reason_and_no_redelivery()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        var jobId = await RegisterSubmitAndClaimAsync("claimed-req-1");
        await BackdateClaimedAtAsync("claimed-req-1", minutes: 31);

        // 显式触发一次超时回收扫描（等同后台任务的单次执行）
        var scanner = new ClaimedJobTimeoutService(
            new ServerDb(DbPath),
            new ServerOptions { ClaimedJobTimeoutMinutes = 30 },
            TimeProvider.System,
            NullLogger<ClaimedJobTimeoutService>.Instance);
        Assert.Equal(1, await scanner.ScanOnceAsync());

        // 业务系统查询：终态 Failed + 原因（错误码 + 结果未知提示）
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Failed", detail.GetProperty("status").GetString());
        var reason = detail.GetProperty("errorMessage").GetString();
        Assert.Contains("LF_SRV_009", reason);
        Assert.Contains("可能已实际打印", reason);
        Assert.Contains("新 requestId", reason);

        // 不被再次投递：领取查询无产出，作业列表仍只有一条
        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-claimed/jobs/pending");
        Assert.Equal(0, claimed.GetArrayLength());
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/jobs?deviceId=pc-claimed");
        Assert.Equal(1, list.GetArrayLength());

        // 同一 requestId 重放只返回既有 Failed 作业（200，非 202），不重新投递
        var replay = await _client.PostAsync("/api/jobs", Json(SubmitBody("claimed-req-1")));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(jobId, replayed.GetProperty("jobId").GetString());
        Assert.Equal("Failed", replayed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Normal_path_claim_then_report_should_still_complete()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        // 正常路径零回归：领取 → 回报终态（不推进时间，远小于 30 分钟超时）
        var jobId = await RegisterSubmitAndClaimAsync("claimed-req-2");

        var report = await _client.PostAsync($"/api/devices/pc-claimed/jobs/{jobId}/result",
            Json("""{ "status": "Completed", "completedItems": 1, "failedItems": 0 }"""));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Completed", detail.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Late_report_after_reclaim_should_replay_failed()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        // 宿主重启后本地队列打完、迟到的真实回报：返回既有 Failed 终态（幂等重放，不覆盖）
        var jobId = await RegisterSubmitAndClaimAsync("claimed-req-3");
        await BackdateClaimedAtAsync("claimed-req-3", minutes: 31);
        var scanner = new ClaimedJobTimeoutService(
            new ServerDb(DbPath),
            new ServerOptions { ClaimedJobTimeoutMinutes = 30 },
            TimeProvider.System,
            NullLogger<ClaimedJobTimeoutService>.Instance);
        Assert.Equal(1, await scanner.ScanOnceAsync());

        var report = await _client.PostAsync($"/api/devices/pc-claimed/jobs/{jobId}/result",
            Json("""{ "status": "Completed", "completedItems": 1, "failedItems": 0 }"""));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reported = await report.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", reported.GetProperty("status").GetString());

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Failed", detail.GetProperty("status").GetString());
        Assert.Contains("LF_SRV_009", detail.GetProperty("errorMessage").GetString());
    }

    private async Task<string> RegisterSubmitAndClaimAsync(string requestId)
    {
        await PostOkAsync("/api/devices", """{ "deviceId": "pc-claimed", "name": "失联机" }""");
        await PostOkAsync("/api/templates", TemplateJson);
        var submit = await _client.PostAsync("/api/jobs", Json(SubmitBody(requestId)));
        Assert.True(submit.IsSuccessStatusCode, $"提交失败：{(int)submit.StatusCode}");
        var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("jobId").GetString();
        Assert.Equal("Pending", job.GetProperty("status").GetString());

        // 宿主领取（Pending → Claimed）后消失：不再心跳、不回报
        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-claimed/jobs/pending");
        Assert.Equal(1, claimed.GetArrayLength());
        Assert.Equal(jobId, claimed[0].GetProperty("jobId").GetString());
        return jobId!;
    }

    private async Task BackdateClaimedAtAsync(string requestId, int minutes)
    {
        // 直改 claimed_at 模拟「领取后超过 N 分钟无回报」（与真实时间流逝等价，避免测试等待）
        await using var connection = new SqliteConnection($"Data Source={DbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE server_jobs SET claimed_at = $time WHERE request_id = $requestId;";
        command.Parameters.AddWithValue("$time", LabelFrame.Core.Data.SqliteSupport.Format(DateTimeOffset.UtcNow.AddMinutes(-minutes)));
        command.Parameters.AddWithValue("$requestId", requestId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static string SubmitBody(string requestId) => """
        { "requestId": "%REQUEST_ID%", "targetDeviceId": "pc-claimed", "templateName": "it-claimed",
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

/// <summary>超时回收关闭（0）回归锚点：失联 Claimed 永不回收（行为与现状一致），迟到的回报照常进入终态。</summary>
public sealed class ClaimedTimeoutDisabledEndpointsTests : IDisposable
{
    private readonly IsolatedServerEnvironment _environment = new();
    private string _directory => _environment.DirectoryPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ClaimedTimeoutDisabledEndpointsTests()
    {
        _environment.Set("LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES", "0");
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Timeout_disabled_should_keep_claimed_forever_and_accept_late_report()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        await PostAsync("/api/devices", """{ "deviceId": "pc-old", "name": "老机器" }""");
        await PostAsync("/api/templates", """
            {
              "name": "it-claimed",
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
            { "requestId": "claimed0-req-1", "targetDeviceId": "pc-old", "templateName": "it-claimed",
              "labels": [ { "data": { "code": "A-01" } } ] }
            """);
        Assert.True(submit.IsSuccessStatusCode, $"提交失败：{(int)submit.StatusCode}");
        var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("jobId").GetString();

        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-old/jobs/pending");
        Assert.Equal(1, claimed.GetArrayLength());

        // 2020 年领取的 Claimed：回收关闭时永不超时（现状回归），迟到的回报照常完成
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "server.db")}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE server_jobs SET claimed_at = $time WHERE request_id = $requestId;";
            command.Parameters.AddWithValue("$time", "2020-01-01T00:00:00.0000000+00:00");
            command.Parameters.AddWithValue("$requestId", "claimed0-req-1");
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var scanner = new ClaimedJobTimeoutService(
            new ServerDb(Path.Combine(_directory, "server.db")),
            new ServerOptions { ClaimedJobTimeoutMinutes = 0 },
            TimeProvider.System,
            NullLogger<ClaimedJobTimeoutService>.Instance);
        Assert.Equal(0, await scanner.ScanOnceAsync());

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Claimed", detail.GetProperty("status").GetString());

        var report = await _client.PostAsync($"/api/devices/pc-old/jobs/{jobId}/result",
            new StringContent("""{ "status": "Completed", "completedItems": 1, "failedItems": 0 }""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        detail = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
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
