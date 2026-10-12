using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.WinHost.Api;

/// <summary>机器级端点：ServerUrl 配置（仅回环可写）、批次打印设置、本机服务关闭。</summary>
internal static class HostApi
{
    public static IEndpointRouteBuilder MapHostApi(this IEndpointRouteBuilder app, Action<string> hostInfo)
    {
    // GET 响应含只读 version（客户端「检查更新」比较口径；POST 忽略该字段）
    app.MapGet("/api/host/config", (HostOptions options) =>
        Results.Ok(new Api.HostConfigDto(options.ServerUrl ?? string.Empty, options.DeviceId, options.DeviceName, LocalIpAddresses.EnumerateIpv4(), HostOptions.ProductVersion)));

    app.MapPost("/api/host/config", async (HttpContext context, Api.HostConfigRequest? request, HostConfigStore store, HostOptions options, Routing.ServerRoutingCoordinator routing) =>
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ServerUrl))
        {
            return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少 serverUrl。"));
        }

        var serverUrl = request.ServerUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "serverUrl 格式不正确（http://主机:端口）。"));
        }

        store.SaveServerUrl(serverUrl);
        options.ServerUrl = serverUrl;
        // 保存即生效：路由热切换——地址未变化时幂等无动作（不打断进行中的注册 / 长轮询）
        await routing.ApplyServerUrlAsync(serverUrl, context.RequestAborted);
        hostInfo($"机器级配置已更新：ServerUrl={serverUrl}");
        return Results.Ok(new Api.HostConfigDto(serverUrl, options.DeviceId, options.DeviceName, LocalIpAddresses.EnumerateIpv4(), HostOptions.ProductVersion));
    });
    // 批次作业设置：仅回环可写、保存即生效
    app.MapGet("/api/host/print-settings", (PrintSettings printSettings) =>
        Api.PrintSettingsApi.Get(printSettings));

    app.MapPost("/api/host/print-settings", (HttpContext context, PrintSettingsDto? request, PrintSettingsStore store, PrintSettings printSettings) =>
        Api.PrintSettingsApi.Post(context.Connection.RemoteIpAddress, request, store, printSettings));

    // 本机服务关闭（与托盘菜单共用统一退出路径）
    app.MapPost("/api/host/shutdown", async (HttpContext context, HostExitCoordinator exit, LabelJobQueue queue, CancellationToken cancellationToken) =>
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // 队列锁内原子拒绝正在打印的退出请求，并停止新提交 / 领取，避免检查与开始打印之间的竞态
        if (!await queue.TryPrepareForShutdownAsync(cancellationToken))
        {
            return Results.Conflict(new ErrorView(JobErrorCodes.InvalidTransition, "当前有标签正在打印。请等待本张完成后重试升级。"));
        }

        // 200ms 缓冲让本响应先送达客户端，再进入「优雅停止 + 限时兜底强退」序列
        _ = exit.RequestShutdownAsync("HTTP /api/host/shutdown（Web UI 设置页「退出程序」）", TimeSpan.FromMilliseconds(200));
        return Results.Ok(new { shuttingDown = true });
    });

        return app;
    }
}
