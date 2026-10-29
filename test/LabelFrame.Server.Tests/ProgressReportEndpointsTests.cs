using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 进度增量上报 HTTP 端点集成测试：宿主上报后查询视图计数逐步增长
/// 终态仍由 result 端点写入且幂等不回退；错误语义（404 / 403 / 409 / 400）与 result 端点一致。
/// </summary>
public sealed class ProgressReportEndpointsTests : IDisposable
{
    private readonly IsolatedServerEnvironment _environment = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ProgressReportEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Progress_should_grow_query_view_and_result_remains_only_terminator()
    {
        var jobId = await RegisterAndClaimAsync("pc-progress", labels: 3, requestId: "req-prog-1");

        // 打印中两次上报：查询视图计数逐步增长（不再终态一次跳变）
        var first = await PostProgressAsync("pc-progress", jobId, """{ "completedItems": 1, "failedItems": 0 }""");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("completedItems").GetInt32());
        await PostProgressAsync("pc-progress", jobId, """{ "completedItems": 2, "failedItems": 0 }""");

        var mid = await _client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Claimed", mid.GetProperty("status").GetString());
        Assert.Equal(2, mid.GetProperty("completedItems").GetInt32());
        Assert.Equal(0, mid.GetProperty("failedItems").GetInt32());

        // 终态仍由 result 写入；此后 progress 幂等 no-op、计数不变；result 幂等重放返回既有终态
        var result = await _client.PostAsync($"/api/devices/pc-progress/jobs/{jobId}/result",
            Json("""{ "status": "Completed", "completedItems": 3, "failedItems": 0 }"""));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var late = await PostProgressAsync("pc-progress", jobId, """{ "completedItems": 1, "failedItems": 1 }""");
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        var lateBody = await late.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", lateBody.GetProperty("status").GetString());
        Assert.Equal(3, lateBody.GetProperty("completedItems").GetInt32());
        Assert.Equal(0, lateBody.GetProperty("failedItems").GetInt32());

        var replay = await _client.PostAsync($"/api/devices/pc-progress/jobs/{jobId}/result",
            Json("""{ "status": "Failed", "completedItems": 0, "failedItems": 3 }"""));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", replayBody.GetProperty("status").GetString());
        Assert.Equal(3, replayBody.GetProperty("completedItems").GetInt32());
    }

    [Fact]
    public async Task Progress_errors_should_align_with_result_endpoint()
    {
        await PostOkAsync("/api/devices", """{ "deviceId": "pc-a", "name": "领取者" }""");
        await PostOkAsync("/api/devices", """{ "deviceId": "pc-b", "name": "无关设备" }""");

        // Pending（未领取）→ 409 LF_SRV_005
        var submit = await _client.PostAsync("/api/jobs", Json("""
            { "requestId": "req-prog-409", "targetDeviceId": "pc-a",
              "template": {
                "contract": { "name": "it", "version": "1.0", "fields": [] },
                "layout": { "name": "l", "contractName": "it", "contractVersion": "1.0", "widthMm": 40, "heightMm": 20, "elements": [] }
              },
              "labels": [ { "data": {} } ] }
            """));
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var pendingId = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString();
        var pending = await PostProgressAsync("pc-a", pendingId!, """{ "completedItems": 1 }""");
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        Assert.Equal("LF_SRV_005", (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // 作业不存在 → 404；非领取设备 → 403；空体 → 400
        var missing = await PostProgressAsync("pc-a", "no-such-job", """{ "completedItems": 1 }""");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-a/jobs/pending");
        var claimedId = claimed[0].GetProperty("jobId").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await PostProgressAsync("pc-b", claimedId!, """{ "completedItems": 1 }""")).StatusCode);

        var empty = await _client.PostAsync($"/api/devices/pc-a/jobs/{claimedId}/progress", Json("null"));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    private async Task<string> RegisterAndClaimAsync(string deviceId, int labels, string requestId)
    {
        await PostOkAsync("/api/devices", $$"""{ "deviceId": "{{deviceId}}", "name": "进度机" }""");
        var submit = await _client.PostAsync("/api/jobs", Json($$"""
            { "requestId": "{{requestId}}", "targetDeviceId": "{{deviceId}}",
              "template": {
                "contract": { "name": "it", "version": "1.0", "fields": [ { "key": "code", "displayName": "编码", "isRequired": true } ] },
                "layout": { "name": "l", "contractName": "it", "contractVersion": "1.0", "widthMm": 40, "heightMm": 20,
                            "elements": [ { "type": "text", "literal": "固定", "xMm": 1, "yMm": 1, "fontHeightMm": 3 } ] }
              },
              "labels": [ {{string.Join(", ", Enumerable.Range(0, labels).Select(_ => """{ "data": { "code": "A" } }"""))}} ] }
            """));
        Assert.True(submit.IsSuccessStatusCode, $"提交失败：{submit.StatusCode}");
        var jobId = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;

        var claimed = await _client.GetFromJsonAsync<JsonElement>($"/api/devices/{deviceId}/jobs/pending");
        Assert.Equal(1, claimed.GetArrayLength());
        Assert.Equal(jobId, claimed[0].GetProperty("jobId").GetString());
        return jobId;
    }

    private async Task<HttpResponseMessage> PostProgressAsync(string deviceId, string jobId, string body)
        => await _client.PostAsync($"/api/devices/{deviceId}/jobs/{jobId}/progress", Json(body));

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
