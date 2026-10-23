using System.Net;
using LabelFrame.Api;
using LabelFrame.Core.Jobs;

namespace LabelFrame.WinHost.Api;

/// <summary>本机客户端生命周期与界面活动端点。</summary>
internal static class LifecycleApi
{
    public static void MapLifecycleApi(this WebApplication app)
    {
        app.MapGet("/api/host/lifecycle", (HostActivityState state) => Results.Ok(new
        {
            instanceId = state.InstanceId, restartRequired = state.PluginsChanged, restarting = state.IsPrepared,
        }));

        app.MapPost("/api/host/ui-activity", (HttpContext context, UiActivityReport report, HostActivityState state) =>
        {
            if (!IsLocalRequest(context)) return Results.StatusCode(403);
            if (!Guid.TryParse(report.SessionId, out _)) return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "界面会话标识无效。"));
            var accepted = state.ReportUi(report);
            return Results.Json(new { preparationId = state.PreparationId }, statusCode: accepted ? 200 : 409);
        });

        app.MapPost("/api/host/restart", async (HttpContext context, HostRestartCoordinator coordinator, CancellationToken ct) =>
        {
            if (!IsLocalRequest(context)) return Results.StatusCode(403);
            var reason = await coordinator.RequestAsync(showPlugins: true, ct);
            return reason is null ? Results.Ok(new { restarting = true }) : Results.Conflict(new ErrorView(JobErrorCodes.InvalidRequest, reason));
        });
    }

    internal static bool IsLocalRequest(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)) return false;
        var host = context.Request.Host.Host.Trim('[', ']');
        if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && !(IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address))) return false;
        var origin = context.Request.Headers.Origin.ToString();
        return origin.Length == 0 || (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.GetLeftPart(UriPartial.Authority), $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase));
    }
}
