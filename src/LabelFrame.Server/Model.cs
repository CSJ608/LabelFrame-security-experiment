namespace LabelFrame.Server;

/// <summary>设备在线状态。</summary>
public enum DeviceStatus
{
    /// <summary>在线（最近心跳在窗口内）。</summary>
    Online,

    /// <summary>离线（超过心跳窗口未上报）。</summary>
    Offline,
}

/// <summary>设备目录条目。</summary>
public sealed class Device
{
    /// <summary>设备标识（宿主生成并注册）。</summary>
    public required string Id { get; init; }

    /// <summary>展示名称。</summary>
    public required string Name { get; init; }

    /// <summary>注册时间（UTC）。</summary>
    public DateTimeOffset RegisteredAt { get; init; }

    /// <summary>最近心跳时间（UTC），由注册 / 轮询刷新。</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>服务端看到的来源 IP（IPv4 文本，注册 / 心跳时刷新；可为空）。</summary>
    public string? LastIp { get; set; }
}

/// <summary>Server 作业状态。</summary>
public enum ServerJobStatus
{
    /// <summary>待设备领取（设备离线时暂存）。</summary>
    Pending,

    /// <summary>已被设备领取，打印中（后续归客户端本地队列管理，服务端不再计龄）。</summary>
    Claimed,

    /// <summary>设备回报完成。</summary>
    Completed,

    /// <summary>设备回报失败（或本地校验 / 编码失败）。</summary>
    Failed,

    /// <summary>暂存超过 TTL 未投递，服务端主动放弃（终态，不重新投递）。</summary>
    Expired,
}

/// <summary>Server 作业：一次请求 = 一个作业，载荷为模板 + labels。</summary>
public sealed class ServerJob
{
    /// <summary>作业标识。</summary>
    public required string Id { get; init; }

    /// <summary>幂等键。</summary>
    public required string RequestId { get; init; }

    /// <summary>目标设备标识（定向投递）。</summary>
    public required string TargetDeviceId { get; init; }

    /// <summary>作业状态。</summary>
    public ServerJobStatus Status { get; set; } = ServerJobStatus.Pending;

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>领取时间（UTC）。</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>结束时间（UTC）。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>标签总数。</summary>
    public int TotalItems { get; init; }

    /// <summary>已完成数。</summary>
    public int CompletedItems { get; set; }

    /// <summary>失败数。</summary>
    public int FailedItems { get; set; }

    /// <summary>失败原因（中文）。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>失败原因码（③ / ：宿主回报可选携带、失联回收 LF_SRV_009、超期放弃 LF_SRV_011；中文存量不迁移，可空）。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>作业载荷（模板 + labels 的 JSON，领取时原样返回宿主）。</summary>
    public required string PayloadJson { get; init; }

    /// <summary>终态回调地址（可选，提交时经 scheme 白名单校验；null = 无回调。不进投递载荷，宿主零感知）。</summary>
    public string? CallbackUrl { get; init; }
}

/// <summary>设备回报的作业结果。</summary>
public sealed record JobResultReport(
    string Status,
    int CompletedItems,
    int FailedItems,
    string? ErrorMessage);