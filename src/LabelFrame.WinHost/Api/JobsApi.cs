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

/// <summary>本机作业端点：提交 / 查询 / 挂起恢复取消 / 失败项重打（Log 模拟打印附带出图目录信息）。</summary>
internal static class JobsApi
{
    public static IEndpointRouteBuilder MapJobsApi(this IEndpointRouteBuilder app)
    {
    app.MapPost("/api/jobs", async (SubmitJobRequest? request, JobSubmissionService service, ITransportManager transportManager, HostOptions options, CancellationToken ct) =>
    {
        if (request is null)
        {
            return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "请求体不能为空。"));
        }

        var result = await service.SubmitAsync(request, ct);
        if (result.Job is null)
        {
            return Results.BadRequest(new ErrorView(result.ErrorCode!, result.ErrorMessage!, result.FieldKey));
        }

        var jobView = EnrichPrintInfo(JobViews.From(result.Job), result.Job!.Id, transportManager, options.PrintOutputPath);
        return result.Created
            ? Results.Accepted((string?)null, jobView)
            : Results.Ok(jobView);
    });

    // 作业列表（作业历史页；单机降级用，形状与 Server 兼容）
    app.MapGet("/api/jobs", async (int? limit, ILabelJobStore store, ITransportManager transportManager, HostOptions options, CancellationToken ct) =>
    {
        var jobs = await store.ListRecentAsync(Math.Clamp(limit ?? 100, 1, 500), ct);
        return Results.Ok(jobs.Select(j => EnrichPrintInfo(JobViews.From(j), j.Id, transportManager, options.PrintOutputPath)));
    });

    app.MapGet("/api/jobs/{jobId}", async (string jobId, LabelJobQueue queue, ITransportManager transportManager, HostOptions options, CancellationToken ct) =>
    {
        var job = await queue.GetAsync(jobId, ct);
        if (job is not null)
        {
            return Results.Ok(EnrichPrintInfo(JobViews.From(job), job.Id, transportManager, options.PrintOutputPath));
        }

        // 模板化消息：渲染结果与旧内插文案等价，params 供展示端按码表翻译插值
        var parameters = new Dictionary<string, string> { ["jobId"] = jobId };
        return Results.NotFound(new ErrorView(
            JobErrorCodes.JobNotFound,
            LabelFrame.Core.Errors.ErrorMessageTemplates.Format("作业不存在：{jobId}。", parameters),
            Params: parameters));
    });

    app.MapPost("/api/jobs/{jobId}/suspend", async (string jobId, LabelJobQueue queue, ITransportManager transportManager, HostOptions options, CancellationToken ct) =>
        await TransitionAsync(jobId, queue.SuspendAsync, transportManager, options.PrintOutputPath, ct));

    app.MapPost("/api/jobs/{jobId}/resume", async (string jobId, LabelJobQueue queue, ITransportManager transportManager, HostOptions options, CancellationToken ct) =>
        await TransitionAsync(jobId, queue.ResumeAsync, transportManager, options.PrintOutputPath, ct));

    app.MapPost("/api/jobs/{jobId}/cancel", async (string jobId, LabelJobQueue queue, ITransportManager transportManager, HostOptions options, CancellationToken ct) =>
        await TransitionAsync(jobId, queue.CancelAsync, transportManager, options.PrintOutputPath, ct));

    app.MapPost("/api/jobs/{jobId}/items/{itemIndex:int}/retry", async (string jobId, int itemIndex, LabelJobQueue queue, ITransportManager transportManager, HostOptions options, CancellationToken ct) =>
    {
        try
        {
            var job = await queue.RetryItemAsync(jobId, itemIndex, ct);
            return Results.Ok(EnrichPrintInfo(JobViews.From(job), job.Id, transportManager, options.PrintOutputPath));
        }
        catch (LabelJobException ex) when (ex.Code == JobErrorCodes.JobNotFound)
        {
            return Results.NotFound(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters));
        }
        catch (LabelJobException ex)
        {
            return Results.Conflict(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters));
        }
    });

        return app;
    }

    /// <summary>Log 模拟打印：把 PNG 目录与张数附到作业视图，便于前端展示「打印图片在哪」。</summary>
    internal static JobView EnrichPrintInfo(JobView view, string jobId, ITransportManager transportManager, string printOutputPath)
    {
        if (transportManager.CurrentConfig.Mode != TransportMode.Log)
        {
            return view;
        }

        var dir = Path.Combine(printOutputPath, jobId);
        var count = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").Length : 0;
        return view with { PrintImageDir = dir, PrintImageCount = count };
    }

    private static async Task<IResult> TransitionAsync(
        string jobId,
        Func<string, CancellationToken, Task<LabelJob>> action,
        ITransportManager transportManager,
        string printOutputPath,
        CancellationToken ct)
    {
        try
        {
            var job = await action(jobId, ct);
            return Results.Ok(EnrichPrintInfo(JobViews.From(job), job.Id, transportManager, printOutputPath));
        }
        catch (LabelJobException ex) when (ex.Code == JobErrorCodes.JobNotFound)
        {
            return Results.NotFound(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters));
        }
        catch (LabelJobException ex)
        {
            return Results.Conflict(new ErrorView(ex.Code, ex.Message, Params: ex.Parameters));
        }
    }
}
