using System.Net;
using System.Security.Cryptography;
using System.Text;
using LabelFrame.Updates;

namespace LabelFrame.Server.Api;

/// <summary>受权限保护的升级发布与客户端候选查询端点。</summary>
public static class ClientUpdatesApi
{
    public static void MapClientUpdatesApi(this WebApplication app)
    {
        app.MapGet("/api/client-updates", (HttpContext c, ServerOptions options, ClientUpdatesService service) =>
            Authorized(c, options) ? Results.Ok(service.List()) : Results.StatusCode(403));
        app.MapPost("/api/client-updates/publish", async (HttpContext c, PublishRequest request, ServerOptions options,
            ClientUpdatesService service, ClientPackagesService packages, CancellationToken ct) =>
        {
            if (!Authorized(c, options)) return Results.StatusCode(403);
            var path = packages.GetDownloadPath(request.FileName);
            if (path is null) return Results.NotFound(new { message = "草稿安装包不存在。" });
            try { return Results.Ok(await service.PublishAsync(path, request.FileName, request.Devices ?? [], ct)); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        app.MapPut("/api/client-updates/{id}/pause", async (HttpContext c, string id, PauseRequest request,
            ServerOptions options, ClientUpdatesService service, CancellationToken ct) =>
        {
            if (!Authorized(c, options)) return Results.StatusCode(403);
            var release = await service.PauseAsync(id, request.Paused, ct);
            return release is null ? Results.NotFound() : Results.Ok(release);
        });
        app.MapGet("/api/client-updates/candidate", (string deviceId, string currentVersion, ClientUpdatesService service) =>
        {
            if (!ValidQuery(deviceId, currentVersion)) return Results.BadRequest();
            return Results.Ok(new { release = service.Select(deviceId, currentVersion) });
        });
        app.MapGet("/api/client-updates/{id}/package", (string id, string deviceId, string currentVersion, ClientUpdatesService service) =>
        {
            if (!ValidQuery(deviceId, currentVersion)) return Results.BadRequest();
            var path = service.DownloadPath(id, deviceId, currentVersion);
            return path is null ? Results.NotFound() : Results.File(path, "application/octet-stream");
        });
        app.MapPost("/api/client-updates/status", (ClientUpdateStatus status, ClientUpdatesService service) =>
            service.Report(status) ? Results.Ok() : Results.BadRequest());
        app.MapGet("/api/client-updates/status", (HttpContext c, ServerOptions options, ClientUpdatesService service) =>
            Authorized(c, options) ? Results.Ok(service.Reports()) : Results.StatusCode(403));
    }

    public static bool Authorized(HttpContext context, ServerOptions options)
    {
        var secret = options.UpdateAdminToken;
        if (secret is null || secret.Length < 32 || secret.Length > 256) return false;
        if (!context.Request.IsHttps && !(context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip))) return false;
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || !string.Equals(uri.GetLeftPart(UriPartial.Authority), $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))) return false;
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var actual = Encoding.UTF8.GetBytes(authorization[7..]);
        var expected = Encoding.UTF8.GetBytes(secret);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool ValidQuery(string device, string version)
    {
        if (string.IsNullOrWhiteSpace(device) || device.Length > 128 || version.Length > 32) return false;
        try { ClientUpdateContract.ParseVersion(version); return true; }
        catch (InvalidDataException) { return false; }
    }
    public sealed record PublishRequest(string FileName, string[]? Devices);
    public sealed record PauseRequest(bool Paused);
}
