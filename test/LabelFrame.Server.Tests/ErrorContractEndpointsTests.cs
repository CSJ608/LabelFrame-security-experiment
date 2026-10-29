using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 错误响应契约 HTTP 集成测试：
/// 请求体反序列化失败（非法 JSON / 非 UTF-8 / 类型不匹配）→ 400 + LF_API_BAD_BODY；
/// 非归属设备回报 / 进度 → 403 + ErrorView（LF_SRV_004，非空 body）；
/// 插件包上传非法 zip → 400 + 中文消息（不直出英文框架原话）。
/// </summary>
public sealed class ErrorContractEndpointsTests : IDisposable
{
    private readonly IsolatedServerEnvironment _environment = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ErrorContractEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Submit_malformed_json_should_be_400_with_bad_body_code()
    {
        using var response = await _client.PostAsync("/api/jobs", Json("""{ "requestId": "e-1", "labels": """));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_API_BAD_BODY", body.GetProperty("code").GetString());
        Assert.Contains("JSON", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Submit_non_utf8_body_should_be_400_with_bad_body_code()
    {
        // GBK 编码的中文值：对 UTF-8 JSON 解析是非法字节序列（此前落入全局异常处理器 500）
        var content = new ByteArrayContent([0x7B, 0x22, 0x6C, 0x61, 0x62, 0x65, 0x6C, 0x73, 0x22, 0x3A, 0x22, 0xD6, 0xD0, 0xCE, 0xC4, 0x22, 0x7D]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var response = await _client.PostAsync("/api/jobs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_API_BAD_BODY", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submit_type_mismatch_body_should_be_400_with_bad_body_code()
    {
        // labels 应为数组，传字符串 → 类型不匹配
        using var response = await _client.PostAsync("/api/jobs", Json("""{ "labels": "not-an-array" }"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_API_BAD_BODY", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Result_and_progress_from_non_owner_should_be_403_with_error_view()
    {
        var jobId = await RegisterAndClaimAsync("pc-owner", "pc-stranger");

        // 非归属设备回报终态：403 + ErrorView（非空 body，LF_SRV_004）
        var result = await _client.PostAsync($"/api/devices/pc-stranger/jobs/{jobId}/result",
            Json("""{ "status": "Completed", "completedItems": 1, "failedItems": 0 }"""));
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        var resultBody = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_SRV_004", resultBody.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(resultBody.GetProperty("message").GetString()));

        // 非归属设备进度上报：同语义（错误语义与 result 端点一致）
        var progress = await _client.PostAsync($"/api/devices/pc-stranger/jobs/{jobId}/progress",
            Json("""{ "completedItems": 1, "failedItems": 0 }"""));
        Assert.Equal(HttpStatusCode.Forbidden, progress.StatusCode);
        var progressBody = await progress.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_SRV_004", progressBody.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(progressBody.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Plugin_package_upload_non_zip_should_be_400_with_chinese_message()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("this is definitely not a zip archive"));
        form.Add(file, "file", "bad.lfplugin");

        var response = await _client.PostAsync("/api/plugin-packages", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_SRV_002", body.GetProperty("code").GetString());
        var message = body.GetProperty("message").GetString();
        Assert.Contains("不是 zip 格式", message);
        // 不直出 .NET 英文框架原话（此前为 "End of Central Directory record could not be found."）
        Assert.DoesNotContain("End of Central Directory", message);
    }

    private async Task<string> RegisterAndClaimAsync(string ownerId, string strangerId)
    {
        await PostOkAsync("/api/devices", $$"""{ "deviceId": "{{ownerId}}", "name": "领取者" }""");
        await PostOkAsync("/api/devices", $$"""{ "deviceId": "{{strangerId}}", "name": "无关设备" }""");

        var submit = await _client.PostAsync("/api/jobs", Json($$"""
            { "requestId": "err-403-1", "targetDeviceId": "{{ownerId}}",
              "template": {
                "contract": { "name": "it", "version": "1.0", "fields": [ { "key": "code", "displayName": "编码", "isRequired": true } ] },
                "layout": { "name": "l", "contractName": "it", "contractVersion": "1.0", "widthMm": 40, "heightMm": 20,
                            "elements": [ { "type": "text", "literal": "固定", "xMm": 1, "yMm": 1, "fontHeightMm": 3 } ] }
              },
              "labels": [ { "data": { "code": "A" } } ] }
            """));
        Assert.True(submit.IsSuccessStatusCode, $"提交失败：{submit.StatusCode}");
        var jobId = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;

        var claimed = await _client.GetFromJsonAsync<JsonElement>($"/api/devices/{ownerId}/jobs/pending");
        Assert.Equal(jobId, claimed[0].GetProperty("jobId").GetString());
        return jobId;
    }

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
