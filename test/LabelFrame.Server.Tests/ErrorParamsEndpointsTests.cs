using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 错误消息模板化端点集成：
/// params 缺省时 HTTP 响应原文与旧形态逐字节等价；带参错误（按 IP 查设备 / 作业不存在）透出 params 且中文 message 等价；
/// WMS 终态回调载荷（schema）字段集零变化（不含 params）。
/// </summary>
public sealed class ErrorParamsEndpointsTests : IDisposable
{
    /// <summary>minimal API 实际 JSON 选项口径：Web 默认 + ASP.NET Core Http.Json 的宽松非 ASCII 转义（中文原样输出）。</summary>
    private static readonly System.Text.Json.JsonSerializerOptions WebDefaults = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _directory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ErrorParamsEndpointsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lferrorparams-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", Path.Combine(_directory, "server.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", Path.Combine(_directory, "templates.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", Path.Combine(_directory, "logs.db"));
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Error_response_without_params_should_be_byte_equal_to_legacy_shape()
    {
        // params 缺省 → HTTP 响应原文与旧形态逐字节相等（三字段形态快照的序列化输出）
        using var response = await _client.PostAsync("/api/jobs", Json("""{ "requestId": "" }"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        // 旧形态快照（引入 params 前的 ErrorView）以 minimal API Web 默认选项序列化的期望字节
        var expected = System.Text.Json.JsonSerializer.Serialize(
            new LegacyErrorViewSnapshot("LF_SRV_002", "缺少 requestId（幂等键）。"), WebDefaults);
        Assert.Equal(expected, raw);
    }

    /// <summary>引入 params 前的 ErrorView 形态快照（现状锚点）。</summary>
    private sealed record LegacyErrorViewSnapshot(string Code, string Message, string? FieldKey = null);

    [Fact]
    public async Task Find_device_by_unknown_ip_should_return_params_and_equivalent_message()
    {
        using var response = await _client.GetAsync("/api/devices/by-ip/10.99.99.99");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // 按 IP 未找到拆码 LF_SRV_012（原 LF_SRV_001 与「设备未注册 {deviceId}」双键变体混用）
        Assert.Equal("LF_SRV_012", body.GetProperty("code").GetString());
        // 后端中文 message 与旧内插文案等价（中文兜底）
        Assert.Equal("按 IP 未找到设备：10.99.99.99。", body.GetProperty("message").GetString());
        // params：扁平字符串键值对象，键名与模板占位符一致
        var parameters = body.GetProperty("params");
        Assert.Equal(JsonValueKind.Object, parameters.ValueKind);
        Assert.Equal("10.99.99.99", parameters.GetProperty("ip").GetString());
        Assert.Single(parameters.EnumerateObject());
    }

    [Fact]
    public async Task Submit_with_unknown_ip_should_return_params_from_domain_exception()
    {
        // 领域异常（ServerException）路径：Parameters 随异常透传到 ErrorView.params
        using var response = await _client.PostAsync(
            "/api/jobs",
            Json("""{ "requestId": "ep-1", "targetIp": "10.88.88.88", "template": { "contract": { "name": "t", "version": "1", "fields": [] }, "layout": { "name": "l", "contractName": "t", "contractVersion": "1", "widthMm": 40, "heightMm": 20, "elements": [] } }, "labels": [ { "data": {} } ] }"""));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("LF_SRV_012", body.GetProperty("code").GetString());
        Assert.Equal("按 IP 未找到设备：10.88.88.88。", body.GetProperty("message").GetString());
        Assert.Equal("10.88.88.88", body.GetProperty("params").GetProperty("ip").GetString());
    }

    [Fact]
    public async Task Submit_with_invalid_callback_url_should_return_dedicated_code_and_params()
    {
        // callbackUrl 校验从 LF_SRV_002 拆出专属码 LF_SRV_013（单码单参数键集）
        using var response = await _client.PostAsync(
            "/api/jobs",
            Json("""{ "requestId": "ep-cb", "targetDeviceId": "any", "callbackUrl": "file://x", "template": { "contract": { "name": "t", "version": "1", "fields": [] }, "layout": { "name": "l", "contractName": "t", "contractVersion": "1", "widthMm": 40, "heightMm": 20, "elements": [] } }, "labels": [ { "data": {} } ] }"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("LF_SRV_013", body.GetProperty("code").GetString());
        Assert.Equal("callbackUrl 无效（仅支持 http/https 地址）：file://x。", body.GetProperty("message").GetString());
        Assert.Equal("file://x", body.GetProperty("params").GetProperty("callbackUrl").GetString());
        Assert.Single(body.GetProperty("params").EnumerateObject());
    }

    [Fact]
    public async Task Get_unknown_template_should_return_unified_template_name_key()
    {
        // 回归锚点：模板库共享端点变体的参数键必须与作业提交链一致（templateName）——
        // en 码表词条 "Template not found: {{templateName}}." 对两个变体均可插值，不再回退中文
        using var response = await _client.GetAsync("/api/templates/不存在的模板X");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("LF_SRV_006", body.GetProperty("code").GetString());
        Assert.Equal("模板不存在:不存在的模板X。", body.GetProperty("message").GetString());
        var parameters = body.GetProperty("params");
        Assert.Equal("不存在的模板X", parameters.GetProperty("templateName").GetString());
        Assert.Single(parameters.EnumerateObject());
    }

    [Fact]
    public async Task Submit_with_unknown_template_name_should_return_template_name_key()
    {
        // 回归锚点：作业提交链变体（与上一测试同码同键集；提交链按既有映射返回 400）
        using var response = await _client.PostAsync(
            "/api/jobs",
            Json("""{ "requestId": "ep-tpl", "targetDeviceId": "any", "templateName": "no-such-template", "labels": [ { "data": {} } ] }"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("LF_SRV_006", body.GetProperty("code").GetString());
        Assert.Equal("模板不存在：no-such-template。", body.GetProperty("message").GetString());
        var parameters = body.GetProperty("params");
        Assert.Equal("no-such-template", parameters.GetProperty("templateName").GetString());
        Assert.Single(parameters.EnumerateObject());
    }

    [Fact]
    public async Task Get_unknown_job_should_return_params()
    {
        using var response = await _client.GetAsync("/api/jobs/no-such-job");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("LF_SRV_003", body.GetProperty("code").GetString());
        Assert.Equal("作业不存在：no-such-job。", body.GetProperty("message").GetString());
        Assert.Equal("no-such-job", body.GetProperty("params").GetProperty("jobId").GetString());
    }

    [Fact]
    public void Callback_payload_schema_should_stay_unchanged_without_params()
    {
        // WMS 终态回调载荷字段集零变化——不含 params / errorCode，字段名与顺序即契约
        var payload = new JobCallbackPayload(
            "job-1", "req-1", "Failed", DateTimeOffset.Parse("2026-09-29T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), 3, 2, 1, "发送失败");
        var json = JsonSerializer.Serialize(payload, WebDefaults);

        var body = JsonDocument.Parse(json).RootElement;
        string[] fields = [.. body.EnumerateObject().Select(p => p.Name)];
        string[] expected =
            ["jobId", "requestId", "status", "completedAt", "totalItems", "completedItems", "failedItems", "errorMessage"];
        Assert.Equal(expected, fields);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

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
