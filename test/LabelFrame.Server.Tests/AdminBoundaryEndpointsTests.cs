using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.Server;
using LabelFrame.Server.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFrame.Server.Tests;

/// <summary>在全路径隔离的完整 Server 上验证实际端点的匿名边界。</summary>
public sealed class AdminBoundaryEndpointsTests : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AdminBoundaryEndpointsTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var directory = Path.Combine(root!.FullName, "work", "security-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var (key, path) in new Dictionary<string, string>
        {
            ["DB"] = "server.db", ["TEMPLATES_DB"] = "templates.db", ["LOGS_DB"] = "logs.db",
            ["LOG_FILE"] = "server.log", ["CLIENT_PACKAGES"] = "client-packages", ["PDA_PACKAGES"] = "pda-packages",
            ["PLUGIN_PACKAGES"] = "plugin-packages", ["CLIENT_UPDATES"] = "client-updates",
            ["ANDROID_UPDATES"] = "android-updates", ["WEB_UI"] = "web-ui",
        }) Set($"LABELFRAME_SERVER_{key}", Path.Combine(directory, path));
        Set("LABELFRAME_SERVER_LISTEN", "http://127.0.0.1:0");
        Set("LABELFRAME_SERVER_PUBLIC_BASE_URL", "https://download.example.invalid/labels");
        Set("LABELFRAME_SERVER_TRUSTED_PROXIES", "");
        Set("Server__AdminPasswordHash", new PasswordHasher<string>().HashPassword("admin", "Synthetic-endpoint-test-password"));
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Every_actual_admin_endpoint_rejects_anonymous_before_binding_or_writing()
    {
        var routes = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var count = 0;
        foreach (var route in routes)
        {
            var pattern = route.RoutePattern.RawText;
            if (pattern is null || !pattern.StartsWith("/api/", StringComparison.Ordinal)) continue;
            foreach (var method in route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
            {
                if (ServerEndpointAccess.Classify(method, pattern) != ServerAccess.Admin) continue;
                var path = pattern;
                foreach (var parameter in route.RoutePattern.Parameters) path = path.Replace($"{{{parameter.Name}}}", "synthetic", StringComparison.Ordinal);
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                var response = await _client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {pattern}: {response.StatusCode}");
                count++;
            }
        }
        Assert.Equal(34, count);
    }

    [Fact]
    public async Task Public_download_and_info_are_available_without_internal_configuration()
    {
        var info = await _client.GetFromJsonAsync<JsonElement>("/api/server/info");
        Assert.Equal(3, info.EnumerateObject().Count());
        Assert.False(info.TryGetProperty("listenUrl", out _));
        Assert.False(info.TryGetProperty("trustedProxies", out _));
        Assert.Equal("https://download.example.invalid/labels", info.GetProperty("publicBaseUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/client-packages")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/pda-packages")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/server/ipv4-candidates")).StatusCode);
    }

    [Theory]
    [InlineData("https://user:secret@example.invalid/path")]
    [InlineData("https://example.invalid/path?token=secret")]
    [InlineData("https://example.invalid/#secret")]
    [InlineData("file:///private")]
    public void Public_address_never_echoes_credentials(string value) => Assert.Null(ServerPublicInfo.SafeBaseUrl(value));

    [Fact]
    public async Task Real_loopback_login_can_read_management_and_logout_revokes_it()
    {
        Assert.True(_client.BaseAddress!.IsLoopback);
        Assert.DoesNotContain(_client.BaseAddress.Port, new[] { 53960, 53961, 53970 });
        var session = await _client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        _client.DefaultRequestHeaders.Add(AdminSecurity.CsrfHeader, session.GetProperty("csrfToken").GetString());
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { password = "Synthetic-endpoint-test-password" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/templates")).StatusCode);
        _client.DefaultRequestHeaders.Remove(AdminSecurity.CsrfHeader);
        session = await _client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        _client.DefaultRequestHeaders.Add(AdminSecurity.CsrfHeader, session.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/templates")).StatusCode);
    }

    [Fact]
    public void Every_storage_option_is_within_the_experiment()
    {
        var options = _factory.Services.GetRequiredService<ServerOptions>();
        foreach (var path in new[] { options.DatabasePath, options.TemplatesDbPath, options.LogsDbPath, options.LogFilePath,
            options.ClientPackagesPath, options.PdaPackagesPath, options.PluginPackagesPath,
            options.ClientUpdatesPath, options.AndroidUpdatesPath, options.WebUiPath })
            Assert.Contains(Path.Combine("work", "security-tests"), path!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("http://127.0.0.1:0", options.ListenUrl);
    }

    private void Set(string name, string value)
    {
        _previous[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        foreach (var (key, value) in _previous) Environment.SetEnvironmentVariable(key, value);
    }
}
