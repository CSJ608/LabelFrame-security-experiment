using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LabelFrame.Api.Tests;

/// <summary>
/// 共享全局异常处理器分类测试：
/// 请求体反序列化失败（非法 JSON / 非 UTF-8 / 类型不匹配）→ 400 + LF_API_BAD_BODY + 中文消息；
/// 其余未捕获异常 → 500 + LF_INTERNAL_001（ApiErrorCodes 常量，全仓唯一字面量定义处）。
/// </summary>
public sealed class GlobalExceptionHandlerTests : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public GlobalExceptionHandlerTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLabelFrameExceptionHandler();
        var app = builder.Build();
        app.UseExceptionHandler();
        // 与宿主同构的 JSON 体端点（触发 minimal API 模型绑定包装 BadHttpRequestException）
        app.MapPost("/echo", (EchoRequest? request) => Results.Ok(request));
        // 未分类异常端点（走 500 兜底路径）
        app.MapPost("/boom", () => { throw new InvalidOperationException("boom"); });
        _app = app;
        _app.StartAsync().GetAwaiter().GetResult();
        _client = app.GetTestClient();
    }

    [Fact]
    public async Task Malformed_json_body_should_be_400_with_bad_body_code()
    {
        using var response = await _client.PostAsync("/echo", Json("""{ "count": """));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadErrorAsync(response);
        Assert.Equal(ApiErrorCodes.BadBody, body.GetProperty("code").GetString());
        Assert.Contains("JSON", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Non_utf8_body_should_be_400_with_bad_body_code()
    {
        // GBK 编码的中文值：对 UTF-8 JSON 解析是非法字节序列
        var content = new ByteArrayContent([0x7B, 0x22, 0x63, 0x6F, 0x75, 0x6E, 0x74, 0x22, 0x3A, 0x22, 0xD6, 0xD0, 0xCE, 0xC4, 0x22, 0x7D]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var response = await _client.PostAsync("/echo", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadErrorAsync(response);
        Assert.Equal(ApiErrorCodes.BadBody, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Type_mismatch_body_should_be_400_with_bad_body_code()
    {
        // count 为 int，传字符串 → 反序列化类型不匹配
        using var response = await _client.PostAsync("/echo", Json("""{ "count": "not-a-number" }"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadErrorAsync(response);
        Assert.Equal(ApiErrorCodes.BadBody, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Valid_body_should_still_pass_through()
    {
        using var response = await _client.PostAsync("/echo", Json("""{ "count": 3 }"""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Unhandled_exception_should_be_500_with_internal_code()
    {
        using var response = await _client.PostAsync("/boom", Json("null"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await ReadErrorAsync(response);
        // 走常量（ApiErrorCodes.InternalError = LF_INTERNAL_001，全仓唯一字面量定义处）
        Assert.Equal(ApiErrorCodes.InternalError, body.GetProperty("code").GetString());
        // 不透出内部信息：消息为固定中文兜底文案，不含异常消息 / 堆栈
        var message = body.GetProperty("message").GetString();
        Assert.Contains("服务器内部错误", message);
        Assert.DoesNotContain("boom", message);
    }

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<JsonElement>(stream);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>测试用请求体（count 用于构造类型不匹配）。</summary>
    public sealed record EchoRequest(int Count);
}
