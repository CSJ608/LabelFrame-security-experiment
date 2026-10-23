using LabelFrame.Updates;
using LabelFrame.WinHost.Api;

namespace LabelFrame.WinHost.Updates;

/// <summary>仅限本机同源的客户端升级设置与操作端点。</summary>
internal static class ClientUpdateApi
{
    public static void MapClientUpdateApi(this WebApplication app)
    {
        app.MapGet("/api/host/update", (HttpContext context, ClientUpdateAgent agent) =>
            LifecycleApi.IsLocalRequest(context) ? Results.Ok(new { settings = agent.Settings, status = agent.Status }) : Results.StatusCode(403));
        app.MapPut("/api/host/update/settings", async (HttpContext context, ClientUpdateSettings settings, ClientUpdateAgent agent, CancellationToken ct) =>
        {
            if (!LifecycleApi.IsLocalRequest(context)) return Results.StatusCode(403);
            try { await agent.SaveSettingsAsync(settings, ct); return Results.Ok(settings); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        app.MapPost("/api/host/update/check", async (HttpContext context, ClientUpdateAgent agent, CancellationToken ct) =>
        {
            if (!LifecycleApi.IsLocalRequest(context)) return Results.StatusCode(403);
            await agent.CheckAsync(ct);
            return Results.Ok(agent.Status);
        });
        app.MapPost("/api/host/update/install", async (HttpContext context, ClientUpdateAgent agent, CancellationToken ct) =>
        {
            if (!LifecycleApi.IsLocalRequest(context)) return Results.StatusCode(403);
            var reason = await agent.InstallAsync(false, ct);
            return reason is null ? Results.Ok(new { restarting = true }) : Results.Conflict(new { message = reason });
        });
    }
}
