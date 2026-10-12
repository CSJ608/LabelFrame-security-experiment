using System.Text;

namespace LabelFrame.Server;

/// <summary>单次回调发送结果：成功（HTTP 2xx）或失败原因（中文，进投递表末次错误）。</summary>
public sealed record JobCallbackSendResult(bool Success, string? Error);

/// <summary>回调发送抽象（测试注入假发送器驱动确定性退避 / 死信用例；产品实现为 HttpJobCallbackSender）。</summary>
public interface IJobCallbackSender
{
    /// <summary>发送一次回调 POST。返回成功与否与失败原因；不抛出业务异常（取消除外）。</summary>
    Task<JobCallbackSendResult> SendAsync(string url, string payloadJson, CancellationToken cancellationToken);
}

/// <summary>
/// HTTP 回调发送器（安全边界）：投递超时 10 秒、不跟随重定向（3xx 原样返回按失败重试）
/// 响应体读弃不落地。发送器为 Server 专属出站通道，不与任何宿主 / 回报路径共享 HttpClient。
/// </summary>
public sealed class HttpJobCallbackSender : IJobCallbackSender, IDisposable
{
    private readonly HttpClient _client;

    public HttpJobCallbackSender()
    {
        _client = new HttpClient(new SocketsHttpHandler
        {
            // 不跟随重定向：回调端点 3xx 不自动跳转（防投递目标被指向任意地址），按失败进入退避重试
            AllowAutoRedirect = false,
        })
        {
            Timeout = JobCallbackDeliveryPolicy.DeliveryTimeout,
        };
    }

    public async Task<JobCallbackSendResult> SendAsync(string url, string payloadJson, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var response = await _client.PostAsync(url, content, cancellationToken);

            // 响应读弃不落地：默认完成选项已把响应体读入内存，此处显式读出后即弃（不落任何存储），
            // 读取全程在 10 秒投递超时窗口内；只以状态码判定成败
            _ = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            return response.IsSuccessStatusCode
                ? new JobCallbackSendResult(true, null)
                : new JobCallbackSendResult(false, $"回调端点返回 HTTP {(int)response.StatusCode}。");
        }
        catch (HttpRequestException ex)
        {
            return new JobCallbackSendResult(false, $"回调端点不可达：{ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient 超时（投递超时窗口耗尽），与调用方取消区分开
            return new JobCallbackSendResult(false, $"回调端点响应超时（{JobCallbackDeliveryPolicy.DeliveryTimeout.TotalSeconds:0} 秒）。");
        }
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// 终态回调投递后台服务：周期扫描 job_callbacks 到期任务并 POST 回调载荷
/// 指数退避、至多 JobCallbackDeliveryPolicy.MaxAttempts 次、超限死信；投递状态持久化——
/// 进程重启后未完成任务继续投递（至少一次语义）。与 PendingJobExpirationService 同构
/// （PeriodicTimer + 注入 TimeProvider，测试以 FakeTimeProvider 驱动确定性）。
/// </summary>
public sealed partial class JobCallbackDeliveryService : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "作业终态回调投递成功：jobId={JobId}，url={Url}，第 {Attempts} 次尝试。")]
    private partial void LogDelivered(string jobId, string url, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "作业终态回调投递失败：jobId={JobId}，url={Url}，第 {Attempts} 次尝试，原因={Error}，下次重试={NextRetryAt:yyyy-MM-dd HH:mm:ss}。")]
    private partial void LogDeliveryFailed(string jobId, string url, int attempts, string error, DateTimeOffset nextRetryAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "作业终态回调转入死信：jobId={JobId}，url={Url}，尝试 {Attempts} 次后放弃。")]
    private partial void LogDeadLettered(string jobId, string url, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "终态回调投递扫描失败。")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);

    private static readonly System.Text.Json.JsonSerializerOptions PayloadJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly ServerDb _db;
    private readonly TimeProvider _time;
    private readonly IJobCallbackSender _sender;
    private readonly ILogger<JobCallbackDeliveryService> _logger;

    public JobCallbackDeliveryService(
        ServerDb db,
        TimeProvider timeProvider,
        IJobCallbackSender sender,
        ILogger<JobCallbackDeliveryService> logger)
    {
        _db = db;
        _time = timeProvider;
        _sender = sender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后延迟 5 秒执行首次扫描（重启恢复：投递状态已持久化，到期任务立即续投），之后按扫描周期执行；
        // 到期判定只看 next_retry_at（持久化时钟源在 ScanOnceAsync 内取 TimeProvider），延迟用系统延迟即可
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(JobCallbackDeliveryPolicy.ScanInterval, _time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogScanFailed(_logger, ex);
            }
        }
    }

    /// <summary>执行一次扫描：投递本批到期任务（ScanBatchSize 上限，超出下一轮继续）；返回本轮处理条数。</summary>
    public async Task<int> ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        // 登记自愈（至少一次语义兜底）：补偿「终态已落库但登记缺失」的窗口（如回报路径在
        // UpdateJobResult 与 EnqueueJobCallbacks 之间进程崩溃）；幂等 SQL，无遗漏时零写入
        await _db.EnqueueJobCallbacksAsync(now, cancellationToken: cancellationToken);

        var due = await _db.ListDueJobCallbacksAsync(now, JobCallbackDeliveryPolicy.ScanBatchSize, cancellationToken);
        foreach (var task in due)
        {
            await DeliverAsync(task, cancellationToken);
        }

        return due.Count;
    }

    private async Task DeliverAsync(JobCallbackTask task, CancellationToken cancellationToken)
    {
        var job = await _db.GetJobAsync(task.JobId, cancellationToken);
        if (job is null)
        {
            // 作业已被历史清理删除：无终态可投，直接死信（正常不应发生——清理随作业删投递行，防御兜底）
            var attempts = task.Attempts + 1;
            await _db.UpdateJobCallbackStatusAsync(task.JobId, JobCallbackStatus.DeadLetter, attempts, _time.GetUtcNow(), "作业已删除，无法构造回调载荷。", cancellationToken);
            LogDeadLettered(task.JobId, task.Url, attempts);
            return;
        }

        var payload = new JobCallbackPayload(
            job.Id,
            job.RequestId,
            job.Status.ToString(),
            job.FinishedAt ?? _time.GetUtcNow(),
            job.TotalItems,
            job.CompletedItems,
            job.FailedItems,
            job.ErrorMessage);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload, PayloadJsonOptions);

        var result = await _sender.SendAsync(task.Url, payloadJson, cancellationToken);
        var attemptsAfter = task.Attempts + 1;
        var now = _time.GetUtcNow();
        if (result.Success)
        {
            await _db.UpdateJobCallbackStatusAsync(task.JobId, JobCallbackStatus.Delivered, attemptsAfter, now, null, cancellationToken);
            LogDelivered(task.JobId, task.Url, attemptsAfter);
            return;
        }

        if (attemptsAfter >= JobCallbackDeliveryPolicy.MaxAttempts)
        {
            await _db.UpdateJobCallbackStatusAsync(task.JobId, JobCallbackStatus.DeadLetter, attemptsAfter, now, result.Error, cancellationToken);
            LogDeadLettered(task.JobId, task.Url, attemptsAfter);
            return;
        }

        var nextRetryAt = JobCallbackDeliveryPolicy.NextRetryAt(now, attemptsAfter);
        await _db.UpdateJobCallbackStatusAsync(task.JobId, JobCallbackStatus.Pending, attemptsAfter, nextRetryAt, result.Error, cancellationToken);
        LogDeliveryFailed(task.JobId, task.Url, attemptsAfter, result.Error ?? "未知原因", nextRetryAt);
    }
}
