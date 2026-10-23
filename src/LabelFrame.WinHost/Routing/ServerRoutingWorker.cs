using System.Collections.Concurrent;
using LabelFrame.Api;
using LabelFrame.Core.Jobs;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;

namespace LabelFrame.WinHost.Routing;

/// <summary>
/// Server 路由 Worker：周期注册（心跳）→ 长轮询领取定向作业 → 投入本地队列打印 → 终态回报 / 节流上报进度。
/// 回报与进度由独立循环负责（终态后约 1s 内回报，不被长轮询阻塞）；进度上报失败静默降级。
/// </summary>
public sealed class ServerRoutingWorker : BackgroundService
{
    private readonly IServerJobPoller _poller;
    private readonly JobSubmissionService _submission;
    private readonly LabelJobQueue _queue;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _reportInterval;
    private readonly TimeSpan _progressInterval;
    private readonly TimeProvider _time;
    private readonly ILogger<ServerRoutingWorker> _logger;
    private readonly ConcurrentDictionary<string, string> _localToServer = new();
    private readonly ConcurrentDictionary<string, ProgressThrottle> _progress = new();
    private readonly ConcurrentDictionary<string, ServerJobResult> _rejectedResults = new();
    private readonly HostActivityState _activity;
    private readonly string _routeId = Guid.NewGuid().ToString("N");

    /// <summary>长轮询通知超时（服务端挂起等待作业，作业到达立即唤醒）。</summary>
    public static readonly TimeSpan NotifyTimeout = TimeSpan.FromSeconds(20);

    /// <summary>完成回报周期：独立于长轮询，本地作业终态后尽快回报。</summary>
    public static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(1);

    /// <summary>进度上报默认节流间隔（计数有变化才发，无变化零流量）。</summary>
    public static readonly TimeSpan DefaultProgressInterval = TimeSpan.FromSeconds(1);

    /// <summary>创建路由 Worker。</summary>
    public ServerRoutingWorker(
        IServerJobPoller poller,
        JobSubmissionService submission,
        LabelJobQueue queue,
        TimeSpan interval,
        ILogger<ServerRoutingWorker> logger)
        : this(poller, submission, queue, interval, logger, TimeProvider.System)
    {
    }

    /// <summary>创建路由 Worker（注入 TimeProvider 与节流参数：进度 / 回报测试用 FakeTimeProvider 或短间隔确定性驱动）。</summary>
    public ServerRoutingWorker(
        IServerJobPoller poller,
        JobSubmissionService submission,
        LabelJobQueue queue,
        TimeSpan interval,
        ILogger<ServerRoutingWorker> logger,
        TimeProvider timeProvider,
        TimeSpan? progressInterval = null,
        TimeSpan? reportInterval = null,
        HostActivityState? activity = null)
    {
        _poller = poller;
        _submission = submission;
        _queue = queue;
        _interval = interval;
        _logger = logger;
        _time = timeProvider;
        _progressInterval = progressInterval ?? DefaultProgressInterval;
        _reportInterval = reportInterval ?? ReportInterval;
        _activity = activity ?? new HostActivityState();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 回报 / 进度循环与主循环并行：主循环负责注册 / 长轮询 / 领取 / 投入队列，
        // 回报循环每周期检查本地作业状态——终态立即回报、进行中节流上报进度
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var reportLoop = ReportLoopAsync(linked.Token);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _poller.RegisterAsync(stoppingToken);
                    // 长轮询等待通知：作业到达立即返回，随后立刻领取（等效推送）；超时也照常领取一次兜底
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var signaled = await _poller.WaitForJobAsync(NotifyTimeout, stoppingToken);
                        using var operation = _activity.TryBeginOperation();
                        if (operation is null)
                        {
                            await Task.Delay(100, stoppingToken);
                            continue;
                        }
                        var jobs = await _poller.FetchPendingAsync(stoppingToken);
                        foreach (var job in jobs) _activity.AddPendingResult(_routeId + job.JobId);
                        foreach (var job in jobs)
                        {
                            await HandleClaimedJobAsync(job, stoppingToken);
                        }

                        if (!signaled)
                        {
                            // 超时：继续下一轮等待（连续挂起，设备在线由 notify 端点心跳维持）
                            continue;
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Server 路由异常，稍后重试。");
                    try
                    {
                        await Task.Delay(_interval, _time, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            linked.Cancel();
            try
            {
                await reportLoop;
            }
            catch (OperationCanceledException)
            {
                // 取消路径：回报循环已结束
            }
        }
    }

    /// <summary>回报 / 进度循环：周期检查本地作业——终态回报结果，进行中节流上报进度（不依赖长轮询）。</summary>
    private async Task ReportLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ReportAsync(cancellationToken);
                await Task.Delay(_reportInterval, _time, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "回报 Server 作业结果异常，稍后重试。");
                try
                {
                    await Task.Delay(_reportInterval, _time, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task HandleClaimedJobAsync(ServerJobPayload job, CancellationToken cancellationToken)
    {
        var request = new SubmitJobRequest(
            job.RequestId,
            job.Template, // 透传 Server 附带模板（含 Name / Images base64）
            job.Labels);

        var result = await _submission.SubmitAsync(request, cancellationToken);
        if (result.Job is null)
        {
            _logger.LogWarning("Server 作业 {JobId} 本地提交失败：{Code} {Message}", job.JobId, result.ErrorCode, result.ErrorMessage);
            _rejectedResults[job.JobId] = new ServerJobResult("Failed", 0, job.TotalItems, result.ErrorMessage, result.ErrorCode);
            return;
        }

        // 幂等重放：同一 requestId 可能返回既有本地作业
        _localToServer[result.Job.Id] = job.JobId;
        _progress[result.Job.Id] = new ProgressThrottle();
        _logger.LogInformation("Server 作业 {JobId} 已投入本地队列 {LocalJobId}（{Items} 张）", job.JobId, result.Job.Id, job.TotalItems);
    }

    /// <summary>终态回报 + 进行中进度上报；进度失败按作业吞掉（静默降级），不影响同批其他作业与终态回报。</summary>
    private async Task ReportAsync(CancellationToken cancellationToken)
    {
        foreach (var (jobId, result) in _rejectedResults.ToArray())
        {
            await _poller.ReportResultAsync(jobId, result, cancellationToken);
            _rejectedResults.TryRemove(jobId, out _);
            _activity.ConfirmResult(_routeId + jobId);
        }
        foreach (var (localJobId, serverJobId) in _localToServer.ToArray())
        {
            var local = await _queue.GetAsync(localJobId, cancellationToken);
            if (local is null)
            {
                continue;
            }

            var completed = local.Items.Count(i => i.Status == LabelJobItemStatus.Completed);
            var failed = local.Items.Count(i => i.Status is LabelJobItemStatus.Failed or LabelJobItemStatus.Cancelled);

            if (local.Status is LabelJobStatus.Completed or LabelJobStatus.Failed or LabelJobStatus.Cancelled)
            {
                var status = local.Status == LabelJobStatus.Completed ? "Completed" : "Failed";
                // 失败明细取首个带原因的标签（消息与原因码同源），随回报透传给 Server 落 error_code
                var failedItem = local.Items.FirstOrDefault(i => i.ErrorMessage is not null);
                await _poller.ReportResultAsync(
                    serverJobId,
                    new ServerJobResult(status, completed, failed, failedItem?.ErrorMessage, failedItem?.ErrorCode),
                    cancellationToken);
                _localToServer.TryRemove(localJobId, out _);
                _activity.ConfirmResult(_routeId + serverJobId);
                _progress.TryRemove(localJobId, out _);
                _logger.LogInformation("Server 作业 {ServerJobId} 已回报：{Status}", serverJobId, status);
                continue;
            }

            // 打印中：节流上报进度（计数有变化才发）；失败静默降级——不重试风暴、不阻塞主链路，下一轮携带最新计数自然重试
            var throttle = _progress.GetOrAdd(localJobId, _ => new ProgressThrottle());
            var now = _time.GetUtcNow();
            if (!throttle.ShouldReport(completed, failed, now, _progressInterval))
            {
                continue;
            }

            try
            {
                await _poller.ReportProgressAsync(serverJobId, new ServerJobProgress(completed, failed), cancellationToken);
                throttle.MarkReported(completed, failed, now);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Server 作业 {ServerJobId} 进度上报失败（Server 不可达时静默降级，下一轮重试）。", serverJobId);
            }
        }
    }
}
