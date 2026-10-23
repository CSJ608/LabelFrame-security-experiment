using System.Net;
using System.Net.Http.Json;
using LabelFrame.Updates;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LabelFrame.WinHost.Integration;

/// <summary>客户端更新接口的本机保护与设置持久化验证。</summary>
public sealed class ClientUpdateApiTests : WinHostIntegrationTestBase
{
    [Fact]
    public async Task Update_settings_default_to_download_and_reject_invalid_windows_or_remote_requests()
    {
        var initial = await JsonAsync("/api/host/update");
        Assert.Equal("download", initial.GetProperty("settings").GetProperty("mode").GetString());
        using var invalid = await Client.PutAsJsonAsync("/api/host/update/settings", new ClientUpdateSettings("maintenance", "02:00", "02:00"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var save = await Client.PutAsJsonAsync("/api/host/update/settings", new ClientUpdateSettings("maintenance", "23:00", "03:00"));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var stored = await JsonAsync("/api/host/update");
        Assert.Equal("23:00", stored.GetProperty("settings").GetProperty("windowStart").GetString());
        using var remote = new HttpRequestMessage(HttpMethod.Get, "/api/host/update");
        remote.Headers.Add(RemoteIpHeader, "192.168.1.20");
        using var denied = await Client.SendAsync(remote);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/api/host/update/install");
        crossOrigin.Headers.Add("Origin", "https://other.example");
        using var originDenied = await Client.SendAsync(crossOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, originDenied.StatusCode);
    }

    [Fact]
    public async Task Installation_is_rejected_without_a_candidate_and_maintenance_blocks_settings_writes()
    {
        using var missing = await Client.PostAsJsonAsync("/api/host/update/install", new { });
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        var activity = Services.GetRequiredService<HostActivityState>();
        Assert.True(activity.TryPrepare(out _));
        using var blocked = await Client.PutAsJsonAsync("/api/host/update/settings", new ClientUpdateSettings("notify"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        activity.CancelPreparation();
    }
}
