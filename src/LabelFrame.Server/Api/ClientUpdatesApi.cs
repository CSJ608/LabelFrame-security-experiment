using LabelFrame.Updates;

namespace LabelFrame.Server.Api;

/// <summary>提供客户端更新管理和设备候选查询端点。</summary>
public static class ClientUpdatesApi
{
    public static void MapClientUpdatesApi(this WebApplication app)
    {
        app.MapGet("/api/client-updates/drafts", async (HttpContext c, ClientPackagesService packages, CancellationToken ct) =>
        {
            if (!SameOrigin(c)) return Results.StatusCode(403);
            var rows = new List<object>();
            foreach (var item in packages.List())
                rows.Add(new { item.FileName, Sha256 = await UpdateDraftSnapshot.HashAsync(packages.GetDownloadPath(item.FileName), ct) });
            return Results.Ok(rows);
        });
        app.MapPost("/api/client-updates/preview", async (HttpContext c, PublishRequest request,
            ClientUpdatesService service, ClientPackagesService packages, CancellationToken ct) =>
        {
            if (!SameOrigin(c)) return Results.StatusCode(403);
            var path = packages.GetDownloadPath(request.FileName);
            if (path is null) return Results.NotFound();
            try { return Results.Ok(await service.PreviewAsync(path, request.FileName, ct)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { return Results.BadRequest(new { message = ex.Message }); }
        });
        app.MapGet("/api/client-updates", (HttpContext c, ClientUpdatesService service) =>
            SameOrigin(c) ? Results.Ok(service.List()) : Results.StatusCode(403));
        app.MapPost("/api/client-updates/publish", async (HttpContext c, PublishRequest request,
            ClientUpdatesService service, ClientPackagesService packages, CancellationToken ct) =>
        {
            if (!SameOrigin(c)) return Results.StatusCode(403);
            var path = packages.GetDownloadPath(request.FileName);
            if (path is null) return Results.NotFound(new { message = "草稿安装包不存在。" });
            try { return Results.Ok(await service.PublishAsync(path, request.FileName, request.Devices ?? [], ct, request.ExpectedSha256)); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });
        app.MapPut("/api/client-updates/{id}/pause", async (HttpContext c, string id, PauseRequest request,
            ClientUpdatesService service, CancellationToken ct) =>
        {
            if (!SameOrigin(c)) return Results.StatusCode(403);
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
        app.MapGet("/api/client-updates/status", (HttpContext c, ClientUpdatesService service) =>
            SameOrigin(c) ? Results.Ok(service.Reports()) : Results.StatusCode(403));
    }

    public static bool SameOrigin(HttpContext context)
    {
        var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (site is "cross-site" or "same-site") return false;
        var origin = context.Request.Headers.Origin.ToString();
        return origin.Length == 0 || (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.GetLeftPart(UriPartial.Authority), $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidQuery(string device, string version)
    {
        if (string.IsNullOrWhiteSpace(device) || device.Length > 128 || version.Length > 32) return false;
        try { ClientUpdateContract.ParseVersion(version); return true; }
        catch (InvalidDataException) { return false; }
    }
    public sealed record PublishRequest(string FileName, string[]? Devices, string? ExpectedSha256 = null);
    public sealed record PauseRequest(bool Paused);
}
