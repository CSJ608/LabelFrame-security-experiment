using System.Net;
using LabelFrame.Api;
using LabelFrame.Core.Jobs;
using Microsoft.AspNetCore.Http;

namespace LabelFrame.WinHost.Api;

/// <summary>
/// 打印设置 API：GET/POST /api/host/print-settings（批次作业＋打印 DPI）。
/// 仅回环可写（与 /api/host/config 一致）；保存即生效（更新单例 PrintSettings）。
/// </summary>
public static class PrintSettingsApi
{
    /// <summary>GET：返回当前设置，DPI 一律解析为生效值（未设置时取宿主配置兜底）。</summary>
    public static IResult Get(PrintSettings settings, HostOptions options)
        => Results.Ok(settings.EffectiveSnapshot(options.Dpi));

    /// <summary>
    /// POST：仅回环可写；校验 batchSize ≥ 1、batchIntervalMs ≥ 0、DPI 白名单；保存即生效。
    /// 请求未携带 dpi（旧客户端形态）时保留已存值不覆盖。
    /// </summary>
    public static IResult Post(IPAddress? remoteIp, PrintSettingsDto? request, PrintSettingsStore store, PrintSettings settings)
    {
        if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request is null)
        {
            return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少打印设置。"));
        }

        var dpi = request.Dpi ?? settings.Snapshot().Dpi;
        var problem = PrintSettings.Validate(request.BatchSize, request.BatchIntervalMs, dpi);
        if (problem is not null)
        {
            return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, problem));
        }

        var value = new PrintSettingsDto(request.BatchEnabled, request.BatchSize, request.BatchIntervalMs, dpi);
        store.Save(value);
        settings.Update(value);
        return Results.Ok(value);
    }
}
