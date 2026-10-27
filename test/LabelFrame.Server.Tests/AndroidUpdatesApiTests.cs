using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.Server.Api;
using LabelFrame.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFrame.Server.Tests;

/// <summary>验证 Android 更新管理权限和真实 HTTP 候选下载边界。</summary>
public sealed class AndroidUpdatesApiTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfandroidapi-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private const string Token = "android-test-only-administration-token";
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new ServerOptions { UpdateAdminToken = Token });
        builder.Services.AddSingleton(new PdaPackagesService(Path.Combine(_root, "drafts")));
        builder.Services.AddSingleton(new AndroidUpdatesService(Path.Combine(_root, "published"), new AndroidUpdatesTests.Inspector(AndroidUpdatesTests.Package)));
        _app = builder.Build();
        _app.MapAndroidUpdatesApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("https://localhost");
        await File.WriteAllBytesAsync(Path.Combine(_root, "drafts", "candidate.apk"), [1, 2, 3]);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        Directory.Delete(_root, true);
    }

    private void Authorize() => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    private static object Request => new { fileName = "candidate.apk", devices = new[] { "pda-a" } };
    private static string Query => "deviceId=pda-a&currentVersionCode=10500&sdk=30&abi=arm64-v8a&publisherSha256=" + new string('A', 64);

    [Fact]
    public async Task Token_origin_and_transport_are_enforced_for_management()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/android-updates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/android-updates/publish", Request)).StatusCode);
        Authorize();
        _client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/android-updates/publish", Request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/android-updates/status")).StatusCode);
        _client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("http://localhost/api/android-updates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/android-updates")).StatusCode);
    }

    [Fact]
    public async Task Published_candidate_download_and_pause_use_the_same_qualification()
    {
        Authorize();
        var response = await _client.PostAsJsonAsync("/api/android-updates/publish", Request);
        response.EnsureSuccessStatusCode();
        var release = (await response.Content.ReadFromJsonAsync<AndroidUpdateRelease>())!;
        var candidate = await _client.GetFromJsonAsync<JsonElement>("/api/android-updates/candidate?" + Query);
        Assert.Equal(release.Id, candidate.GetProperty("release").GetProperty("id").GetString());
        var download = $"/api/android-updates/{release.Id}/package?{Query}";
        Assert.Equal(new byte[] { 1, 2, 3 }, await _client.GetByteArrayAsync(download));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(download.Replace("pda-a", "pda-b", StringComparison.Ordinal))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/android-updates/{release.Id}/pause", new { paused = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(download)).StatusCode);
    }

    [Fact]
    public async Task Invalid_queries_silent_policy_and_missing_filename_are_rejected()
    {
        Authorize();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/android-updates/publish", new { devices = new[] { "pda-a" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/android-updates/publish", new { fileName = "candidate.apk", devices = new[] { "pda-a" }, mode = "maintenance" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/android-updates/candidate?" + Query.Replace("10500", "-1", StringComparison.Ordinal))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/android-updates/status", new { deviceId = "pda-a", currentVersionCode = 1, phase = new string('x', 65) })).StatusCode);
    }
}
