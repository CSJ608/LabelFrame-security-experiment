using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LabelFrame.Server.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LabelFrame.Server.Tests;

/// <summary>通过纯内存 HTTP 宿主验证管理员边界与会话生命周期。</summary>
public sealed class AdminSecurityTests
{
    private const string Password = "Synthetic-only-admin-test-password";
    private static readonly string Hash = new PasswordHasher<string>().HashPassword("admin", Password);

    [Theory]
    [InlineData("GET", "/api/templates", ServerAccess.Admin)]
    [InlineData("POST", "/api/templates", ServerAccess.Admin)]
    [InlineData("POST", "/api/client-packages", ServerAccess.Admin)]
    [InlineData("DELETE", "/api/pda-packages/{fileName}", ServerAccess.Admin)]
    [InlineData("PUT", "/api/android-updates/{id}/pause", ServerAccess.Admin)]
    [InlineData("GET", "/api/client-updates/status", ServerAccess.Admin)]
    [InlineData("GET", "/api/server/ipv4-candidates", ServerAccess.Admin)]
    [InlineData("GET", "/api/new-endpoint", ServerAccess.Admin)]
    [InlineData("POST", "/api/auth/logout", ServerAccess.Admin)]
    [InlineData("GET", "/API/CLIENT-PACKAGES/", ServerAccess.Public)]
    [InlineData("POST", "/api/jobs", ServerAccess.Integration)]
    [InlineData("GET", "/api/jobs", ServerAccess.Admin)]
    [InlineData("GET", "/api/devices/{deviceId}/jobs/pending", ServerAccess.Device)]
    [InlineData("POST", "/api/android-updates/status", ServerAccess.Device)]
    public void Route_classification_is_method_specific_and_defaults_closed(string method, string route, ServerAccess expected) =>
        Assert.Equal(expected, ServerEndpointAccess.Classify(method, route));

    [Theory]
    [InlineData("/api/templates")]
    [InlineData("/API/TEMPLATES/")]
    [InlineData("/api/templates?download=true")]
    [InlineData("/api/unregistered")]
    public async Task Anonymous_admin_routes_are_denied(string path)
    {
        await using var host = await Host.CreateAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/client-packages")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync("/api/jobs", new { })).StatusCode);
    }

    [Fact]
    public async Task Login_csrf_logout_and_replay_have_real_http_enforcement()
    {
        await using var host = await Host.CreateAsync();
        var anonymousToken = await host.SessionAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/api/auth/login", new { password = Password })).StatusCode);
        host.Client.DefaultRequestHeaders.Add(AdminSecurity.CsrfHeader, anonymousToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/auth/login", new { password = "wrong" })).StatusCode);
        var login = await host.Client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var sessionCookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("LabelFrame.Admin.Session=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        host.AcceptCookies(login);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/api/templates", new { })).StatusCode);
        host.Client.DefaultRequestHeaders.Remove(AdminSecurity.CsrfHeader);
        host.Client.DefaultRequestHeaders.Add(AdminSecurity.CsrfHeader, await host.SessionAsync());
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync("/api/templates", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        // 故意保留旧 Cookie，验证服务端撤销而非浏览器删除的效果。
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/templates")).StatusCode);
    }

    [Fact]
    public async Task Unconfigured_password_never_opens_management()
    {
        await using var host = await Host.CreateAsync(configured: false);
        host.Client.DefaultRequestHeaders.Add(AdminSecurity.CsrfHeader, await host.SessionAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/auth/login", new { password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/templates")).StatusCode);
    }

    [Fact]
    public async Task Login_attempt_limit_recovers_after_window()
    {
        await using var host = await Host.CreateAsync();
        host.Client.DefaultRequestHeaders.Add(AdminSecurity.CsrfHeader, await host.SessionAsync());
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/auth/login", new { password = "wrong" })).StatusCode);
        var limited = await host.Client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync("/api/auth/login", new { password = Password })).StatusCode);
    }

    [Theory]
    [InlineData("Origin", "https://evil.example")]
    [InlineData("Origin", "https://localhost/path")]
    [InlineData("Origin", "https://user@localhost")]
    [InlineData("Origin", "https://localhost?value=x")]
    [InlineData("Origin", "null")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    public async Task Cross_origin_and_malformed_origins_cannot_read_csrf(string header, string value)
    {
        await using var host = await Host.CreateAsync();
        host.Client.DefaultRequestHeaders.Add(header, value);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Theory]
    [InlineData("http", "192.0.2.10", null, null, HttpStatusCode.Forbidden)]
    [InlineData("http", "192.0.2.10", "X-Forwarded-Proto", "https", HttpStatusCode.Forbidden)]
    [InlineData("http", "127.0.0.1", "X-Forwarded-For", "127.0.0.1", HttpStatusCode.Forbidden)]
    [InlineData("http", "127.0.0.1", "Forwarded", "proto=https", HttpStatusCode.Forbidden)]
    [InlineData("http", "127.0.0.1", null, null, HttpStatusCode.OK)]
    [InlineData("http", "::1", null, null, HttpStatusCode.OK)]
    [InlineData("https", "192.0.2.10", null, null, HttpStatusCode.OK)]
    public async Task Transport_uses_actual_connection_not_host_or_forwarded_headers(string scheme, string peer,
        string? header, string? value, HttpStatusCode expected)
    {
        await using var host = await Host.CreateAsync(scheme, peer);
        if (header is not null) host.Client.DefaultRequestHeaders.Add(header, value);
        Assert.Equal(expected, (await host.Client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Session_expires_and_restart_store_cannot_restore_it()
    {
        await using var host = await Host.CreateAsync();
        host.Client.DefaultRequestHeaders.Add(AdminSecurity.CsrfHeader, await host.SessionAsync());
        host.AcceptCookies(await host.Client.PostAsJsonAsync("/api/auth/login", new { password = Password }));
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/templates")).StatusCode);
        await using var restarted = await Host.CreateAsync();
        restarted.Client.DefaultRequestHeaders.Add("Cookie", host.Client.DefaultRequestHeaders.GetValues("Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await restarted.Client.GetAsync("/api/templates")).StatusCode);
        host.Clock.Advance(TimeSpan.FromHours(8));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/templates")).StatusCode);
    }

    [Fact]
    public async Task Ticket_store_is_bounded_and_revoked_key_cannot_be_renewed()
    {
        var clock = new FakeTimeProvider();
        var store = new AdminTicketStore(clock);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("test")),
            new AuthenticationProperties { ExpiresUtc = clock.GetUtcNow().AddHours(8) }, AdminSecurity.Scheme);
        var key = await store.StoreAsync(ticket);
        await store.RemoveAsync(key);
        await store.RenewAsync(key, ticket);
        Assert.Null(await store.RetrieveAsync(key));
        for (var i = 0; i < 128; i++) await store.StoreAsync(ticket);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StoreAsync(ticket));
        clock.Advance(TimeSpan.FromHours(8));
        Assert.NotEmpty(await store.StoreAsync(ticket));
    }

    private sealed class Host(WebApplication app, HttpClient client, FakeTimeProvider clock) : IAsyncDisposable
    {
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
        public HttpClient Client { get; } = client;
        public FakeTimeProvider Clock { get; } = clock;

        public static async Task<Host> CreateAsync(string scheme = "https", string peer = "192.0.2.10", bool configured = true)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var clock = new FakeTimeProvider();
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddAdminSecurity(configured ? Hash : null);
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                await next(context);
            });
            app.CaptureAdminTransport();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAdminBoundary();
            app.MapAdminSecurity();
            app.MapGet("/api/templates", () => Results.Ok());
            app.MapPost("/api/templates", () => Results.Ok()).DisableAntiforgery();
            app.MapGet("/api/client-packages", () => Results.Ok());
            app.MapPost("/api/jobs", () => Results.Ok());
            app.MapGet("/api/unregistered", () => Results.Ok());
            await app.StartAsync();
            var client = app.GetTestClient();
            client.BaseAddress = new Uri($"{scheme}://localhost");
            return new Host(app, client, clock);
        }

        public async Task<string> SessionAsync()
        {
            var response = await Client.GetAsync("/api/auth/session");
            response.EnsureSuccessStatusCode();
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
            AcceptCookies(response);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!;
        }

        public void AcceptCookies(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return;
            foreach (var cookie in cookies)
            {
                var pair = cookie.Split(';')[0];
                _cookies[pair[..pair.IndexOf('=', StringComparison.Ordinal)]] = pair;
            }
            Client.DefaultRequestHeaders.Remove("Cookie");
            Client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", _cookies.Values));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
