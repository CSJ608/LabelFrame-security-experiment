using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 本机 IPv4 候选只读端点 HTTP 集成测试：
/// GET /api/server/ipv4-candidates 返回 { candidates: [...] }，全部为合法非回环 IPv4，私网优先排序。
/// </summary>
public sealed class ServerIpv4CandidatesEndpointsTests : IDisposable
{
    private readonly IsolatedServerEnvironment _environment = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ServerIpv4CandidatesEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_should_return_valid_ipv4_candidates_json()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/server/ipv4-candidates")).StatusCode);
        await IsolatedServerEnvironment.LoginAsync(_client);
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
        _environment.Dispose();
    }
}
