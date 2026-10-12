using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 本机 IPv4 候选只读端点 HTTP 集成测试（·）：
/// GET /api/server/ipv4-candidates 返回 { candidates: [...] }，全部为合法非回环 IPv4，私网优先排序。
/// </summary>
public sealed class ServerIpv4CandidatesEndpointsTests : IDisposable
{
    private readonly string _directory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ServerIpv4CandidatesEndpointsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lfipv4-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", Path.Combine(_directory, "server.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", Path.Combine(_directory, "templates.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", Path.Combine(_directory, "logs.db"));
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_should_return_valid_ipv4_candidates_json()
    {
        using var response = await _client.GetAsync("/api/server/ipv4-candidates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("candidates", out var candidates), "响应应含 candidates 字段");
        Assert.Equal(JsonValueKind.Array, candidates.ValueKind);

        foreach (var item in candidates.EnumerateArray())
        {
            var ip = item.GetString();
            Assert.NotNull(ip);
            Assert.True(IPAddress.TryParse(ip, out var address), $"候选 {ip} 应为合法 IP");
            Assert.Equal(AddressFamily.InterNetwork, address.AddressFamily);
            Assert.False(IPAddress.IsLoopback(address), $"候选 {ip} 不应为回环地址");
        }
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
