using LabelFrame.Api;
using LabelFrame.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.Server.Api;

/// <summary>服务端作业端点：业务提交（幂等 / targetIp）、集中查询、宿主长轮询通知 / 领取 / 回报。</summary>
internal static class ServerJobsApi
{
    public static IEndpointRouteBuilder MapServerJobsApi(this IEndpointRouteBuilder app)
    {
// ---- 作业提交 / 查询 ----
app.MapPost("/api/jobs", async (SubmitJobRequest? request, ServerService svc, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        var job = await svc.SubmitJobAsync(request, ct);
        return job.Status == "Pending"
            ? Results.Accepted((string?)null, job)
            : Results.Ok(job);
    }
    catch (ServerException ex)
    {
        // 设备未注册 / 按 IP 未找到设备均 404（by-ip 拆码 LF_SRV_012 后两码都映射 404，HTTP 语义不变）
        return ex.Code is ServerErrorCodes.DeviceNotFound or ServerErrorCodes.DeviceNotFoundByIp
            ? Results.NotFound(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters))
            : Results.BadRequest(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters));
    }
});

app.MapGet("/api/jobs", async (int? limit, string? deviceId, ServerService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListJobsAsync(limit ?? 100, deviceId, ct)));

app.MapGet("/api/jobs/{jobId}", async (string jobId, ServerService svc, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await svc.GetJobAsync(jobId, ct));
    }
    catch (ServerException ex)
    {
        return Results.NotFound(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters));
    }
});

// ---- 设备领取 / 回报 ----
// 长轮询通知：作业到达立即返回 hasPending=true（等效推送）；同时刷新心跳保活。
app.MapGet("/api/devices/{deviceId}/jobs/notify", async (string deviceId, int? timeout, HttpContext context, ServerService svc, PendingJobNotifier notifier, CancellationToken ct) =>
{
    try
    {
        var seconds = Math.Clamp(timeout ?? 20, 1, 30);
        await svc.TouchDeviceAsync(deviceId, DateTimeOffset.UtcNow, ServerService.NormalizeRemoteIp(context.Connection.RemoteIpAddress), ct);
        // 挂起前先查一次未过期 Pending（积压预检）：提交脉冲早于本请求到达时已空放，
        // 不预检会导致纯积压清空场景每批空等满超时；有积压立即返回，不进入长轮询。
        if (await svc.HasDeliverablePendingJobsAsync(deviceId, ct))
        {
            return Results.Ok(new { hasPending = true });
        }
        var hasPending = await notifier.WaitAsync(deviceId, TimeSpan.FromSeconds(seconds), ct);
        return Results.Ok(new { hasPending });
    }
    catch (ServerException ex)
    {
        return Results.NotFound(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters));
    }
});

app.MapGet("/api/devices/{deviceId}/jobs/pending", async (string deviceId, HttpContext context, ServerService svc, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await svc.ClaimPendingJobsAsync(deviceId, ServerService.NormalizeRemoteIp(context.Connection.RemoteIpAddress), ct));
    }
    catch (ServerException ex)
    {
        return Results.NotFound(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters));
    }
});

app.MapPost("/api/devices/{deviceId}/jobs/{jobId}/result", async (string deviceId, string jobId, ReportResultRequest? report, ServerService svc, CancellationToken ct) =>
{
    if (report is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        return Results.Ok(await svc.ReportResultAsync(deviceId, jobId, report, ct));
    }
    catch (ServerException ex)
    {
        return ex.Code switch
        {
            ServerErrorCodes.JobNotFound => Results.NotFound(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters)),
            ServerErrorCodes.NotJobOwner => Results.Json(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters), statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Conflict(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters)),
        };
    }
});

// 进度增量上报：仅 Claimed 接受，计数 max 单调；终态幂等 no-op。错误语义与 result 端点一致。
app.MapPost("/api/devices/{deviceId}/jobs/{jobId}/progress", async (string deviceId, string jobId, ReportProgressRequest? progress, ServerService svc, CancellationToken ct) =>
{
    if (progress is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        return Results.Ok(await svc.ReportProgressAsync(deviceId, jobId, progress, ct));
    }
    catch (ServerException ex)
    {
        return ex.Code switch
        {
            ServerErrorCodes.JobNotFound => Results.NotFound(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters)),
            ServerErrorCodes.NotJobOwner => Results.Json(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters), statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Conflict(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters)),
        };
    }
});

        return app;
    }
}
