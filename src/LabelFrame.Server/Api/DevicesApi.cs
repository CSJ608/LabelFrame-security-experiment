using LabelFrame.Api;
using LabelFrame.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.Server.Api;

/// <summary>设备端点：注册 / 心跳、目录（含在线状态）、按 IP 查找。</summary>
internal static class DevicesApi
{
    public static IEndpointRouteBuilder MapDevicesApi(this IEndpointRouteBuilder app)
    {
// ---- 设备注册 / 目录 ----
app.MapPost("/api/devices", async (RegisterDeviceRequest? request, HttpContext context, ServerService svc, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        return Results.Ok(await svc.RegisterDeviceAsync(request.DeviceId, request.Name, ServerService.NormalizeRemoteIp(context.Connection.RemoteIpAddress), ct));
    }
    catch (ServerException ex)
    {
        return Results.BadRequest(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters));
    }
});

app.MapGet("/api/devices", async (ServerService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListDevicesAsync(ct)));

app.MapGet("/api/devices/by-ip/{ip}", async (string ip, ServerService svc, CancellationToken ct) =>
{
    var device = await svc.FindDeviceByIpAsync(ip, ct);
    if (device is not null)
    {
        return Results.Ok(device);
    }

    // 模板化消息：params 供展示端按码表翻译插值
    var parameters = new Dictionary<string, string> { ["ip"] = ip };
    return Results.NotFound(new ErrorView(
        ServerErrorCodes.DeviceNotFoundByIp,
        LabelFrame.Core.Errors.ErrorMessageTemplates.Format("按 IP 未找到设备：{ip}。", parameters),
        Params: parameters));
});

        return app;
    }
}
