using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabelFrame.Api;

/// <summary>
/// 全局异常处理：统一 ErrorView（问题码 + 中文提示），不向客户端透出堆栈与内部路径。
/// 分类：请求体反序列化失败属调用方错误 → 400 + LF_API_BAD_BODY
/// 其余未捕获异常 → 500 + LF_INTERNAL_001。
/// </summary>
public sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "未处理异常：{Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    // 调用方错误记 Warning 即可（非服务端故障）；原始解析异常（含行 / 列位置）随日志保留供排障
    [LoggerMessage(Level = LogLevel.Warning, Message = "请求绑定失败（调用方错误，返回 400）：{Method} {Path} {ContentType}")]
    private static partial void LogRequestBodyRejected(ILogger logger, Exception exception, string method, string path, string? contentType);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // 客户端断开导致的取消不是服务错误：静默结束
            return true;
        }

        // 请求绑定失败（非法 JSON / 非 UTF-8 / 类型不匹配 / 简单参数解析失败）：minimal API 模型绑定包装为
        // BadHttpRequestException（StatusCode=400 语义），属调用方错误——不再落入 500 兜底误导业务方排查服务端；
        // 原始异常详情（含 JsonException 位置）只进日志，不透出客户端。
        if (exception is BadHttpRequestException or JsonException)
        {
            LogRequestBodyRejected(_logger, exception, httpContext.Request.Method, httpContext.Request.Path.Value ?? string.Empty,
                httpContext.Request.ContentType);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(
                new ErrorView(
                    ApiErrorCodes.BadBody,
                    "请求体或参数无效：不是合法的 JSON、编码不是 UTF-8 或参数类型不匹配。请检查请求内容与 Content-Type（application/json; charset=utf-8）后重试。"),
                cancellationToken);
            return true;
        }

        LogUnhandled(_logger, exception, httpContext.Request.Method, httpContext.Request.Path.Value ?? string.Empty);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            new ErrorView(ApiErrorCodes.InternalError, "服务器内部错误，请查看服务端日志。"),
            cancellationToken);
        return true;
    }
}

/// <summary>宿主接入扩展：注册全局异常处理器（需在 Build 后调用 <c>app.UseExceptionHandler()</c> 激活）。</summary>
public static class GlobalExceptionHandlerExtensions
{
    public static IServiceCollection AddLabelFrameExceptionHandler(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        // 无参 UseExceptionHandler() 的必需配套：无路径且未注册 ProblemDetails 服务时中间件构建即抛错
        services.AddProblemDetails();
        // minimal API 参数绑定失败（非法 JSON / 非 UTF-8 / 类型不匹配）默认仅在 Development 抛 BadHttpRequestException，
        // Production 下由框架短路为「400 空 body」——无法给出统一 ErrorView。此处固定开启，
        // 保证任何环境都交给上面的全局异常处理器分类为 400 + LF_API_BAD_BODY。
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        return services;
    }
}
