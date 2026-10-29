using LabelFrame.Updates;
using Microsoft.AspNetCore.Mvc;

namespace LabelFrame.Server.Api;

/// <summary>提供 APK 发布管理和有界设备查询接口。</summary>
public static class AndroidUpdatesApi
{
    public static void MapAndroidUpdatesApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/android-updates");
        group.MapGet("/drafts", async (HttpContext c, PdaPackagesService packages, CancellationToken ct) =>
        {
            if (!ClientUpdatesApi.SameOrigin(c)) return Results.StatusCode(403);
            var rows = new List<object>();
            foreach (var item in packages.List())
                rows.Add(new { item.FileName, Sha256 = await UpdateDraftSnapshot.HashAsync(packages.GetDownloadPath(item.FileName), ct) });
            return Results.Ok(rows);
        });
        group.MapPost("/preview", async (HttpContext c, PublishRequest request,
            AndroidUpdatesService service, PdaPackagesService packages, CancellationToken ct) =>
        {
            if (!ClientUpdatesApi.SameOrigin(c)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(request.FileName)) return Results.BadRequest();
            var path = packages.GetDownloadPath(request.FileName);
            if (path is null) return Results.NotFound();
            try { return Results.Ok(await service.PreviewAsync(path, ct)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { return Results.BadRequest(new { message = ex.Message }); }
        });
        group.MapGet("", (HttpContext c, AndroidUpdatesService service) =>
            ClientUpdatesApi.SameOrigin(c) && !c.Request.Headers.ContainsKey("Authorization")
                ? Results.Ok(service.List()) : Results.StatusCode(403));
        group.MapPost("/publish", async (HttpContext c, PublishRequest request, AndroidUpdatesService service,
            PdaPackagesService packages, CancellationToken ct) =>
        {
            if (!ClientUpdatesApi.SameOrigin(c)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(request.FileName)) return Results.BadRequest(new { message = "请选择 APK 草稿。" });
            var path = packages.GetDownloadPath(request.FileName);
            if (path is null) return Results.NotFound(new { message = "APK 草稿不存在。" });
            try { return Results.Ok(await service.PublishAsync(path, request.Devices ?? [], request.AllDevicesConfirmed, request.Mode ?? "confirm", ct, request.ExpectedSha256)); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { message = ex.Message }); }
        }).WithMetadata(new RequestSizeLimitAttribute(256 * 1024));
        group.MapPut("/{id}/pause", async (HttpContext c, string id, PauseRequest request, AndroidUpdatesService service, CancellationToken ct) =>
        {
            if (!ClientUpdatesApi.SameOrigin(c)) return Results.StatusCode(403);
            var release = await service.PauseAsync(id, request.Paused, ct);
            return release is null ? Results.NotFound() : Results.Ok(release);
        });
        group.MapGet("/candidate", (string deviceId, long currentVersionCode, int sdk, string abi, string publisherSha256, AndroidUpdatesService service) =>
        {
            var query = new AndroidUpdateQuery(deviceId, currentVersionCode, sdk, abi, publisherSha256);
            return query.IsValid() ? Results.Ok(new { release = service.Select(query) }) : Results.BadRequest();
        });
        group.MapGet("/{id}/package", (string id, string deviceId, long currentVersionCode, int sdk, string abi, string publisherSha256, AndroidUpdatesService service) =>
        {
            var query = new AndroidUpdateQuery(deviceId, currentVersionCode, sdk, abi, publisherSha256);
            if (!query.IsValid()) return Results.BadRequest();
            var path = service.DownloadPath(id, query);
            return path is null ? Results.NotFound() : Results.File(path, PdaPackagesService.ApkContentType);
        });
        group.MapPost("/status", (AndroidUpdateStatus status, AndroidUpdatesService service) =>
            service.Report(status) ? Results.Ok() : Results.BadRequest()).WithMetadata(new RequestSizeLimitAttribute(8192));
        group.MapGet("/status", (HttpContext c, AndroidUpdatesService service) =>
            ClientUpdatesApi.SameOrigin(c) ? Results.Ok(service.Reports()) : Results.StatusCode(403));
    }

    public sealed record PublishRequest(string? FileName, string[]? Devices, bool AllDevicesConfirmed = false, string? Mode = "confirm", string? ExpectedSha256 = null);
    public sealed record PauseRequest(bool Paused);
}
