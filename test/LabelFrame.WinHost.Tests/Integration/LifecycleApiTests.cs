using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LabelFrame.WinHost.Integration;

public sealed class LifecycleApiTests : WinHostIntegrationTestBase
{
    [Fact]
    public async Task Activity_endpoint_should_protect_all_ui_sessions_and_reject_remote_origins()
    {
        var session = Guid.NewGuid().ToString();
        using var report = await Client.PostAsJsonAsync("/api/host/ui-activity", new UiActivityReport(session, true, false));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var activity = Services.GetRequiredService<HostActivityState>();
        Assert.False(activity.TryPrepare(out _));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/host/restart");
        request.Headers.Add("Origin", "https://untrusted.example");
        using var denied = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var rebound = new HttpRequestMessage(HttpMethod.Post, "/api/host/restart");
        rebound.Headers.Host = "rebind.invalid";
        rebound.Headers.Add("Origin", "http://rebind.invalid");
        using var reboundDenied = await Client.SendAsync(rebound);
        Assert.Equal(HttpStatusCode.Forbidden, reboundDenied.StatusCode);

        using var close = await Client.PostAsJsonAsync("/api/host/ui-activity", new UiActivityReport(session, false, false, Closed: true));
        Assert.True(activity.TryPrepare(out _));
        activity.CancelPreparation();
    }

    [Fact]
    public async Task Plugin_marker_should_survive_list_refresh_and_clear_with_new_process_state()
    {
        var state = Services.GetRequiredService<HostActivityState>();
        state.MarkPluginsChanged();
        var first = await JsonAsync("/api/host/lifecycle");
        Assert.True(first.GetProperty("restartRequired").GetBoolean());
        var second = await JsonAsync("/api/host/lifecycle");
        Assert.Equal(first.GetProperty("instanceId").GetString(), second.GetProperty("instanceId").GetString());
        Assert.False(new HostActivityState().PluginsChanged);
    }

    [Fact]
    public async Task Preparation_should_reject_mutations_until_cancelled()
    {
        var state = Services.GetRequiredService<HostActivityState>();
        Assert.True(state.TryPrepare(out _));
        using var blocked = await Client.PostAsJsonAsync("/api/host/config", new { serverUrl = "http://localhost:53961" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        state.CancelPreparation();
        using var allowed = await Client.PostAsJsonAsync("/api/host/print-settings", new { batchSize = 0, batchIntervalMs = 0 });
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, allowed.StatusCode);
    }
}
