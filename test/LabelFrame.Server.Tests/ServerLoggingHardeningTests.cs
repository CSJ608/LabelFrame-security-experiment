using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 日志基础设施加固端点级测试：
/// ① LABELFRAME_SERVER_LOG_FILE 指向无效路径时宿主正常启动（/healthz 可用）；
/// ② 提交 → 认领 → 回报全流程在按日文件 server-yyyyMMdd.log 留下创建 / 认领 / 终态三条业务行（含 jobId / requestId / deviceId）。
/// </summary>
public sealed class ServerLoggingHardeningTests : IDisposable
{
    private static readonly string TemplateJson = """
        {
          "name": "logit-模板",
          "group": "测试",
          "contract": {
            "name": "logit", "version": "1.0",
            "fields": [ { "key": "code", "displayName": "编码", "isRequired": true, "type": "text" } ]
          },
          "layout": {
            "name": "l", "contractName": "logit", "contractVersion": "1.0",
            "widthMm": 40, "heightMm": 20,
            "elements": [ { "type": "text", "literal": "固定", "xMm": 1, "yMm": 1, "fontHeightMm": 3 } ]
          }
        }
        """;

    private readonly IsolatedServerEnvironment _environment = new();
    private string _directory => _environment.DirectoryPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ServerLoggingHardeningTests()
    {
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE", Path.Combine(_directory, "server.log"));
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Full_routing_loop_should_write_business_event_lines_to_dated_log_file()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        // 1) 注册设备 → 建模板 → 提交（第一条：创建行，requestId → jobId）
        await PostOkAsync("/api/devices", """{ "deviceId": "pc-logit", "name": "日志验证机" }""");
        await PostOkAsync("/api/templates", TemplateJson);
        var submit = await _client.PostAsync("/api/jobs", Json("""
            { "requestId": "req-logit-1", "targetDeviceId": "pc-logit", "templateName": "logit-模板",
              "labels": [ { "data": { "code": "A-01" } } ] }
            """));
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("jobId").GetString();

        // 2) 认领（第二条：认领行，含 deviceId）
        var claimed = await _client.GetFromJsonAsync<JsonElement>("/api/devices/pc-logit/jobs/pending");
        Assert.Equal(1, claimed.GetArrayLength());

        // 3) 回报终态（第三条：终态行，含状态与计数）
        var report = await _client.PostAsync($"/api/devices/pc-logit/jobs/{jobId}/result",
            Json("""{ "status": "Completed", "completedItems": 1, "failedItems": 0 }"""));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);

        // 4) 按日文件含三条业务行（含标识字段；幂等重放不重复记录创建行）
        var replay = await _client.PostAsync("/api/jobs", Json("""
            { "requestId": "req-logit-1", "targetDeviceId": "pc-logit", "templateName": "logit-模板",
              "labels": [ { "data": { "code": "A-01" } } ] }
            """));
        Assert.True(replay.IsSuccessStatusCode);

        var logFile = Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(logFile), $"业务日志文件未创建：{logFile}");
        var content = ReadShared(logFile);
        Assert.Contains("作业已创建", content);
        Assert.Contains($"jobId={jobId}", content);
        Assert.Contains("requestId=req-logit-1", content);
        Assert.Contains("目标设备=pc-logit", content);
        Assert.Contains("作业已被认领", content);
        Assert.Contains("设备=pc-logit", content);
        Assert.Contains("作业终态", content);
        Assert.Contains("状态=Completed", content);
        Assert.Equal(1, CountOccurrences(content, "作业已创建")); // 幂等重放不重复记录
    }

    [Fact]
    public async Task Invalid_log_file_path_should_not_break_startup()
    {
        // 日志路径无效（父路径是文件）→ 宿主正常启动（/healthz 200），文件通道跳过
        var blocker = Path.Combine(_directory, "blocker.txt");
        await File.WriteAllTextAsync(blocker, "占位文件");
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE", Path.Combine(blocker, "logs", "server.log"));
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            using var client = factory.CreateClient();
            var response = await client.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE", Path.Combine(_directory, "server.log"));
        }
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        for (var index = text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>以兼容写入句柄的共享方式读取（文件通道仍持写句柄时 File.ReadAllText 会因共享冲突失败）。</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
