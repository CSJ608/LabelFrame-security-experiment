using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 对外通告基地址（PublicBaseUrl）单元 + 端点测试：
/// LABELFRAME_SERVER_PUBLIC_BASE_URL 解析（合法归一 / 非法告警置空 / 未配置为 null）与 /api/server/info 的 publicBaseUrl 字段。
/// </summary>
public sealed class ServerPublicBaseUrlTests : IDisposable
{
    private readonly string _directory;

    public ServerPublicBaseUrlTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lfbaseurl-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", Path.Combine(_directory, "server.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", Path.Combine(_directory, "templates.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", Path.Combine(_directory, "logs.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_PUBLIC_BASE_URL", null);
    }

    [Fact]
    public void Environment_override_should_normalize_trailing_slash()
    {
        var (value, console) = ApplyOverrides("http://192.168.1.10:53961/");
        Assert.Equal("http://192.168.1.10:53961", value);
        Assert.Equal("https://labels.example.com/base", ApplyOverrides("https://labels.example.com/base/").Value);
        // 仅域名（无路径）同样去除补齐的尾斜杠
        Assert.Equal("http://labels.example.com", ApplyOverrides("http://labels.example.com/").Value);
        Assert.Equal(string.Empty, console);
    }

    [Fact]
    public void Environment_override_should_keep_value_without_trailing_slash()
    {
        Assert.Equal("http://192.168.1.10:53961", ApplyOverrides("http://192.168.1.10:53961").Value);
        Assert.Equal("http://labels.example.com", ApplyOverrides("http://labels.example.com").Value);
    }

    [Theory]
    [InlineData("labels.example.com/base")]
    [InlineData("ftp://192.168.1.10/base")]
    [InlineData("")]
    [InlineData("   ")]
    public void Environment_override_should_reject_invalid_value_with_warning(string invalid)
    {
        var (value, console) = ApplyOverrides(invalid);
        Assert.Null(value);
        Assert.Contains("LABELFRAME_SERVER_PUBLIC_BASE_URL", console);
    }

    [Fact]
    public void Not_configured_should_default_to_null_without_warning()
    {
        var (value, console) = ApplyOverrides(null);
        Assert.Null(value);
        Assert.Equal(string.Empty, console);
    }

    [Fact]
    public async Task Info_endpoint_should_return_configured_public_base_url()
    {
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_PUBLIC_BASE_URL", "http://192.168.1.10:53961/");
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            var body = await client.GetFromJsonAsync<JsonElement>("/api/server/info");
            Assert.Equal("http://192.168.1.10:53961", body.GetProperty("publicBaseUrl").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_PUBLIC_BASE_URL", null);
        }
    }

    [Fact]
    public async Task Info_endpoint_should_return_null_when_not_configured()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/server/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("publicBaseUrl", out var publicBaseUrl), "响应应含 publicBaseUrl 字段");
        Assert.Equal(JsonValueKind.Null, publicBaseUrl.ValueKind);

        // 既有字段不回归
        Assert.Equal(ServerOptions.DefaultListenUrl, body.GetProperty("listenUrl").GetString());
        Assert.Equal(ServerOptions.ProductVersion, body.GetProperty("version").GetString());
        Assert.True(body.TryGetProperty("uiEnabled", out _), "响应应含 uiEnabled 字段");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", null);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", null);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", null);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_PUBLIC_BASE_URL", null);
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

    /// <summary>设环境变量后应用覆盖并捕获控制台告警；返回解析结果与控制台输出（用后恢复环境变量）。</summary>
    private static (string? Value, string ConsoleOutput) ApplyOverrides(string? envValue)
    {
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_PUBLIC_BASE_URL", envValue);
        try
        {
            var originalOut = Console.Out;
            var captured = new StringWriter();
            Console.SetOut(captured);
            try
            {
                var options = new ServerOptions();
                options.ApplyEnvironmentOverrides();
                return (options.PublicBaseUrl, captured.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_PUBLIC_BASE_URL", null);
        }
    }
}
