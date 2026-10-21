using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LabelFrame.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 受信代理转发头信任（LABELFRAME_SERVER_TRUSTED_PROXIES）解析与端到端行为：
/// 条目解析（「*」/ IP / CIDR / 混合非法 / 全非法 / 空白关闭）、未启用不改写来源、
/// 受信对端按 X-Forwarded-For 回溯改写注册 / 心跳 / 领取三落点 lastIp、
/// 非受信对端忽略转发头并按对端去重告警、/api/server/info 的 trustedProxies 两态。
/// </summary>
public sealed class TrustedProxyForwardedHeaderTests : IDisposable
{
    private const string TrustedProxiesEnv = "LABELFRAME_SERVER_TRUSTED_PROXIES";
    private const string RemoteIpHeader = "X-Test-Remote-IP";

    private readonly string _directory;

    public TrustedProxyForwardedHeaderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lfproxy-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", Path.Combine(_directory, "server.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", Path.Combine(_directory, "templates.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", Path.Combine(_directory, "logs.db"));
        Environment.SetEnvironmentVariable(TrustedProxiesEnv, null);
    }

    // ---- 条目解析 ----

    [Fact]
    public void Parse_wildcard_should_trust_all_addresses()
    {
        var set = CaptureParse("*");

        Assert.NotNull(set);
        Assert.True(set.AllowAll);
        Assert.Equal("*", set.Raw);
        Assert.True(set.Contains(IPAddress.Parse("203.0.113.7")));
        Assert.True(set.Contains(IPAddress.Parse("2001:db8::1")));
    }

    [Fact]
    public void Parse_single_ip_should_trust_exact_address_only()
    {
        var set = CaptureParse(" 172.16.0.5 ");

        Assert.NotNull(set);
        Assert.False(set.AllowAll);
        Assert.Equal("172.16.0.5", set.Raw);
        Assert.Empty(set.Networks);
        Assert.True(set.Contains(IPAddress.Parse("172.16.0.5")));
        Assert.False(set.Contains(IPAddress.Parse("172.16.0.6")));
    }

    [Fact]
    public void Parse_cidr_should_trust_network_range()
    {
        var set = CaptureParse("172.16.0.0/12, 2001:db8::/32");

        Assert.NotNull(set);
        Assert.Equal(2, set.Networks.Count);
        Assert.True(set.Contains(IPAddress.Parse("172.31.255.254")));
        Assert.False(set.Contains(IPAddress.Parse("172.32.0.1")));
        Assert.True(set.Contains(IPAddress.Parse("2001:db8::dead:beef")));
        Assert.False(set.Contains(IPAddress.Parse("2001:db9::1")));
    }

    [Fact]
    public void Parse_mixed_entries_should_keep_valid_and_warn_invalid()
    {
        var (set, console) = CaptureParseWithConsole("172.16.0.0/12, definitely-not-an-ip");

        Assert.NotNull(set);
        Assert.Single(set.Networks);
        Assert.Contains("definitely-not-an-ip", console);
        Assert.Contains(TrustedProxiesEnv, console);
    }

    [Fact]
    public void Parse_all_invalid_entries_should_disable_with_warnings()
    {
        var (set, console) = CaptureParseWithConsole("definitely-not-an-ip, 300.300.300.300, 172.16.0.0/33");

        Assert.Null(set);
        Assert.Contains("300.300.300.300", console);
        Assert.Contains("172.16.0.0/33", console);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_blank_should_disable_without_warning(string? raw)
    {
        var (set, console) = CaptureParseWithConsole(raw);

        Assert.Null(set);
        Assert.Equal(string.Empty, console);
    }

    [Fact]
    public void Parse_repeated_and_empty_entries_should_be_tolerated()
    {
        // 同一条目重复书写不去重（多写无害）；连续逗号产生的空条目静默忽略
        var set = CaptureParse("172.16.0.5,172.16.0.5, ,10.0.0.0/8");

        Assert.NotNull(set);
        Assert.Equal(2, set.Proxies.Count);
        Assert.Single(set.Networks);
    }

    // ---- 环境变量接线（ApplyEnvironmentOverrides 归一取值；条目校验在解析处，与宿主启动序列一致） ----

    [Fact]
    public void Environment_override_should_populate_trusted_proxies()
    {
        var (options, set, _) = ApplyEnv(" 172.16.0.0/12, 172.16.0.9 ");

        Assert.Equal("172.16.0.0/12, 172.16.0.9", options.TrustedProxies);
        Assert.NotNull(set);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Environment_blank_should_disable_trusted_proxies(string blank)
    {
        var (options, set, _) = ApplyEnv(blank);

        Assert.Null(options.TrustedProxies);
        Assert.Null(set);
    }

    [Fact]
    public void Environment_unset_should_stay_disabled()
    {
        var (options, set, _) = ApplyEnv(null);

        Assert.Null(options.TrustedProxies);
        Assert.Null(set);
    }

    // ---- 端到端（来源 shim 经 IStartupFilter 插管道最前，先设置 TCP 对端、再交由转发头中间件消费；
    // 「未启用不受影响 / 受信则转发生效」两态互证 shim 与 ForwardedHeaders 相对顺序正确） ----

    [Fact]
    public async Task Disabled_proxies_should_ignore_forwarded_header()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RemoteIpHeader, "192.168.1.50");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "9.9.9.9");

        var device = await RegisterAsync(client, "pc-xff-off");

        Assert.Equal("192.168.1.50", device.GetProperty("lastIp").GetString());
    }

    [Fact]
    public async Task Trusted_proxy_should_apply_forwarded_client_to_register_notify_and_claim()
    {
        Environment.SetEnvironmentVariable(TrustedProxiesEnv, "172.16.0.0/12");
        try
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient();
            // TCP 对端为网段内代理；XFF 最左伪造值必须被忽略，最右真实 IP（IPv4-mapped 形式进头验证归一化）落地
            client.DefaultRequestHeaders.Add(RemoteIpHeader, "172.16.0.5");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "9.9.9.9, ::ffff:10.20.30.40");

            var device = await RegisterAsync(client, "pc-xff-on");
            Assert.Equal("10.20.30.40", device.GetProperty("lastIp").GetString());

            await client.GetFromJsonAsync<JsonElement>("/api/devices/pc-xff-on/jobs/notify?timeout=1");
            await AssertLastIpAsync(client, "pc-xff-on", "10.20.30.40");

            var claimed = await client.GetFromJsonAsync<JsonElement>("/api/devices/pc-xff-on/jobs/pending");
            Assert.Equal(0, claimed.GetArrayLength());
            await AssertLastIpAsync(client, "pc-xff-on", "10.20.30.40");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedProxiesEnv, null);
        }
    }

    [Fact]
    public async Task Wildcard_should_trust_any_peer()
    {
        Environment.SetEnvironmentVariable(TrustedProxiesEnv, "*");
        try
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(RemoteIpHeader, "203.0.113.1");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "::ffff:10.9.8.7");

            var device = await RegisterAsync(client, "pc-xff-any");

            Assert.Equal("10.9.8.7", device.GetProperty("lastIp").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedProxiesEnv, null);
        }
    }

    [Fact]
    public async Task Untrusted_peer_should_keep_tcp_peer_and_warn_once()
    {
        Environment.SetEnvironmentVariable(TrustedProxiesEnv, "172.16.0.0/12");
        try
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(RemoteIpHeader, "203.0.113.99");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "9.9.9.9");

            var console = CaptureConsole(async () =>
            {
                var device = await RegisterAsync(client, "pc-xff-untrusted");
                Assert.Equal("203.0.113.99", device.GetProperty("lastIp").GetString());
                // 同对端二次请求（长轮询心跳高频场景）：告警按对端去重
                await RegisterAsync(client, "pc-xff-untrusted");
            });

            Assert.Contains("203.0.113.99", console);
            Assert.Contains(TrustedProxiesEnv, console);
            Assert.Single(Regex.Matches(console, "不在受信代理列表"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedProxiesEnv, null);
        }
    }

    // ---- /api/server/info 字段两态 ----

    [Fact]
    public async Task Info_endpoint_should_echo_effective_trusted_proxies()
    {
        Environment.SetEnvironmentVariable(TrustedProxiesEnv, "172.16.0.0/12");
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            var body = await client.GetFromJsonAsync<JsonElement>("/api/server/info");

            Assert.Equal("172.16.0.0/12", body.GetProperty("trustedProxies").GetString());
            // 既有字段不回归
            Assert.Equal(ServerOptions.DefaultListenUrl, body.GetProperty("listenUrl").GetString());
            Assert.True(body.TryGetProperty("publicBaseUrl", out _), "响应应含 publicBaseUrl 字段");
            Assert.True(body.TryGetProperty("uiEnabled", out _), "响应应含 uiEnabled 字段");
            Assert.Equal(ServerOptions.ProductVersion, body.GetProperty("version").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedProxiesEnv, null);
        }
    }

    [Fact]
    public async Task Info_endpoint_should_return_null_trusted_proxies_when_disabled()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/api/server/info");

        Assert.True(body.TryGetProperty("trustedProxies", out var trusted), "响应应含 trustedProxies 字段");
        Assert.Equal(JsonValueKind.Null, trusted.ValueKind);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new RemoteIpTestShim(RemoteIpHeader))));

    private static async Task<JsonElement> RegisterAsync(HttpClient client, string deviceId)
    {
        var response = await client.PostAsync(
            "/api/devices",
            new StringContent($$"""{ "deviceId": "{{deviceId}}", "name": "代理联调" }""", Encoding.UTF8, "application/json"));
        Assert.True(response.IsSuccessStatusCode, $"注册失败：{(int)response.StatusCode}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    private static async Task AssertLastIpAsync(HttpClient client, string deviceId, string expectedIp)
    {
        var devices = await client.GetFromJsonAsync<JsonElement>("/api/devices");
        var lastIp = devices.EnumerateArray()
            .Single(d => d.GetProperty("deviceId").GetString() == deviceId)
            .GetProperty("lastIp").GetString();
        Assert.Equal(expectedIp, lastIp);
    }

    private static TrustedProxySet? CaptureParse(string? raw) => CaptureParseWithConsole(raw).Set;

    private static (TrustedProxySet? Set, string ConsoleOutput) CaptureParseWithConsole(string? raw)
    {
        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            return (TrustedProxiesParser.Parse(raw), captured.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static string CaptureConsole(Func<Task> action)
    {
        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            action().GetAwaiter().GetResult();
            return captured.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>环境变量接线辅助：设值后按宿主序列（ApplyEnvironmentOverrides → 受信集合解析）执行，返回选项、解析结果与控制台输出（用后恢复环境变量）。</summary>
    private static (ServerOptions Options, TrustedProxySet? Set, string ConsoleOutput) ApplyEnv(string? envValue)
    {
        Environment.SetEnvironmentVariable(TrustedProxiesEnv, envValue);
        try
        {
            var originalOut = Console.Out;
            var captured = new StringWriter();
            Console.SetOut(captured);
            try
            {
                var options = new ServerOptions();
                options.ApplyEnvironmentOverrides();
                return (options, TrustedProxiesParser.Parse(options.TrustedProxies), captured.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedProxiesEnv, null);
        }
    }

    /// <summary>测试来源模拟：按请求头设置 TCP 对端 IP，经 IStartupFilter 插入管道最前（先于转发头中间件）。</summary>
    private sealed class RemoteIpTestShim(string headerName) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, forward) =>
                {
                    if (context.Request.Headers.TryGetValue(headerName, out var ip))
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                    }

                    return forward();
                });
                next(app);
            };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", null);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", null);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", null);
        Environment.SetEnvironmentVariable(TrustedProxiesEnv, null);
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
