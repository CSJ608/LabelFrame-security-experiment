namespace LabelFrame.Server;

/// <summary>终态回调投递状态。</summary>
public enum JobCallbackStatus
{
    /// <summary>待投递（含退避等待中的重试；next_retry_at 到期后再次尝试）。</summary>
    Pending,

    /// <summary>已成功投递（HTTP 2xx，终态）。</summary>
    Delivered,

    /// <summary>死信：尝试次数达到上限，不再重试（终态）。</summary>
    DeadLetter,
}

/// <summary>终态回调投递任务（job_callbacks 表一行；每作业至多一条，job_id 主键幂等）。</summary>
public sealed class JobCallbackTask
{
    /// <summary>作业标识（主键）。</summary>
    public required string JobId { get; init; }

    /// <summary>幂等键（提交 requestId，回调载荷透传给调用方去重）。</summary>
    public required string RequestId { get; init; }

    /// <summary>回调地址（提交时经 scheme 白名单校验的 http/https 绝对地址）。</summary>
    public required string Url { get; init; }

    /// <summary>投递状态。</summary>
    public JobCallbackStatus Status { get; set; } = JobCallbackStatus.Pending;

    /// <summary>已尝试次数（成功 / 失败都递增）。</summary>
    public int Attempts { get; set; }

    /// <summary>下次可尝试时间（退避排程；到期前不投递）。</summary>
    public DateTimeOffset NextRetryAt { get; set; }

    /// <summary>末次失败原因（中文；成功后清空）。</summary>
    public string? LastError { get; set; }

    /// <summary>登记时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>回调投递策略集中常量（默认值经用户确认，集中一处便于后续调参）。</summary>
public static class JobCallbackDeliveryPolicy
{
    /// <summary>至多尝试 5 次，超限转死信。</summary>
    public const int MaxAttempts = 5;

    /// <summary>单次投递超时（覆盖连接与响应读取；不跟随重定向，3xx 按失败重试）。</summary>
    public static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

    /// <summary>指数退避基值：第 n 次失败后等待 基值 × 2^(n-1)，即 30s / 1m / 2m / 4m。</summary>
    public static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>后台投递扫描周期（秒级——终态后 60 秒内送达的验收口径要求远小于周期上限）。</summary>
    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(5);

    /// <summary>单轮扫描最多处理的投递任务数（背压上限，超出部分下一轮继续）。</summary>
    public const int ScanBatchSize = 20;

    /// <summary>按「刚失败的第 failedAttempts 次尝试」计算下次重试时间（指数退避，位移封顶防溢出）。</summary>
    public static DateTimeOffset NextRetryAt(DateTimeOffset now, int failedAttempts)
        => now + InitialRetryDelay * (1 << Math.Min(Math.Max(failedAttempts - 1, 0), 10));
}

/// <summary>回调 URL 校验（提交即拒，：仅 http/https 绝对地址）。</summary>
public static class JobCallbackUrl
{
    /// <summary>是否为允许的回调地址：绝对 URI 且 scheme 为 http / https（大小写不敏感）。</summary>
    public static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

/// <summary>
/// 终态回调载荷（POST 给调用方的 JSON，camelCase 序列化）。requestId 即幂等键——
/// 至少一次语义下回调可能重复 POST，调用方按 requestId / jobId 去重。
/// </summary>
public sealed record JobCallbackPayload(
    string JobId,
    string RequestId,
    string Status,
    DateTimeOffset CompletedAt,
    int TotalItems,
    int CompletedItems,
    int FailedItems,
    string? ErrorMessage);
