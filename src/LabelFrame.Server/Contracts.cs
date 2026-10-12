using LabelFrame.Api;

namespace LabelFrame.Server;

// 通用契约（SubmitJobRequest / TemplateDto / LabelDto / ErrorView / 模板与日志 DTO）在 LabelFrame.Api 共享库，
// 本文件只保留服务端专属类型（设备 / 作业视图 / 领取与回报）。

/// <summary>设备注册请求。</summary>
public sealed record RegisterDeviceRequest(string? DeviceId, string? Name);

/// <summary>设备视图。</summary>
public sealed record DeviceView(
    string DeviceId,
    string Name,
    DateTimeOffset RegisteredAt,
    DateTimeOffset LastSeenAt,
    string Status,
    string? LastIp = null);

/// <summary>作业视图。</summary>
/// <param name="CallbackStatus">终态回调投递状态（Pending / Delivered / DeadLetter；null = 无回调）。</param>
/// <param name="CallbackAttempts">回调已尝试次数（无回调为 null）。</param>
/// <param name="CallbackLastError">回调末次失败原因（中文；无回调或已成功为 null）。</param>
public sealed record ServerJobView(
    string JobId,
    string RequestId,
    string TargetDeviceId,
    string Status,
    DateTimeOffset CreatedAt,
    int TotalItems,
    int CompletedItems,
    int FailedItems,
    string? ErrorMessage,
    string DeviceStatus,
    string? CallbackStatus = null,
    int? CallbackAttempts = null,
    string? CallbackLastError = null);

/// <summary>设备领取到的作业（含载荷）。</summary>
public sealed record ClaimedJob(string JobId, string RequestId, int TotalItems, JobPayload Payload);

/// <summary>作业载荷：模板 + labels。</summary>
public sealed record JobPayload(TemplateDto Template, IReadOnlyList<LabelDto> Labels);

/// <summary>设备回报结果（POST 体）。</summary>
/// <param name="ErrorCode">失败原因码（可选，加法—— ③ / ：落 server_jobs.error_code 供按码追溯；旧宿主不发送即为 null）。</param>
public sealed record ReportResultRequest(string? Status, int? CompletedItems, int? FailedItems, string? ErrorMessage, string? ErrorCode = null);

/// <summary>设备进度增量上报（POST 体）。计数按字段取 max 单调递增，只描述过程不改终态。</summary>
public sealed record ReportProgressRequest(int? CompletedItems, int? FailedItems);
