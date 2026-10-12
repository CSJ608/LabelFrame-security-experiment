using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.Api;
using LabelFrame.Core.Layout;
using LabelFrame.WinHost.Api;

namespace LabelFrame.WinHost.Routing;

/// <summary>设备领取到的 Server 作业。</summary>
public sealed record ServerJobPayload(
    string JobId,
    string RequestId,
    int TotalItems,
    TemplateDto Template,
    IReadOnlyList<LabelDto> Labels);

/// <summary>回报给 Server 的作业结果。</summary>
/// <param name="ErrorCode">失败原因码（可选；无码失败为 null。注意：当前 HTTP 载荷未携带该字段）。</param>
public sealed record ServerJobResult(string Status, int CompletedItems, int FailedItems, string? ErrorMessage, string? ErrorCode = null);

/// <summary>上报给 Server 的作业进度（计数口径与结果回报一致：完成 + 失败 / 取消）。</summary>
public sealed record ServerJobProgress(int CompletedItems, int FailedItems);

/// <summary>Server 作业载荷 DTO（与 Server API 响应同构）。</summary>
internal sealed record ClaimedJobDto(string? JobId, string? RequestId, int TotalItems, JobPayloadDto? Payload);

/// <summary>Server 载荷 DTO。</summary>
internal sealed record JobPayloadDto(TemplateDto? Template, IReadOnlyList<LabelDto>? Labels);

/// <summary>Server 路由客户端：注册 / 心跳、领取定向作业、回报结果。</summary>
public sealed class ServerJobPoller : IServerJobPoller
{
    private readonly HttpClient _http;
    private readonly string _serverUrl;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly JsonSerializerOptions _json;

    /// <summary>创建轮询客户端。</summary>
    public ServerJobPoller(HttpClient http, string serverUrl, string deviceId, string? deviceName = null)
    {
        _http = http;
        _serverUrl = serverUrl.TrimEnd('/');
        _deviceId = deviceId;
        _deviceName = string.IsNullOrWhiteSpace(deviceName) ? deviceId : deviceName;
        _json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter(),
                new LabelElementJsonConverter(),
            },
        };
    }

    /// <summary>注册设备（同时作为心跳）。</summary>
    public async Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"{_serverUrl}/api/devices",
            new { deviceId = _deviceId, name = _deviceName },
            _json,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>领取本设备的定向作业（Server 会把 Pending 置为 Claimed）。</summary>
    public async Task<IReadOnlyList<ServerJobPayload>> FetchPendingAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"{_serverUrl}/api/devices/{_deviceId}/jobs/pending", cancellationToken);
        response.EnsureSuccessStatusCode();

        var jobs = await response.Content.ReadFromJsonAsync<List<ClaimedJobDto>>(_json, cancellationToken)
            ?? [];
        return jobs
            .Where(j => j.JobId is not null && j.RequestId is not null && j.Payload?.Template is not null && j.Payload.Labels is not null)
            .Select(j => new ServerJobPayload(
                j.JobId!,
                j.RequestId!,
                j.TotalItems,
                j.Payload!.Template!,
                j.Payload.Labels!))
            .ToList();
    }

    /// <summary>长轮询等待本设备待领取作业；服务端在作业到达时立即返回 hasPending=true，否则超时返回 false。</summary>
    public async Task<bool> WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var seconds = (int)Math.Clamp(timeout.TotalSeconds, 1, 30);
        var response = await _http.GetAsync($"{_serverUrl}/api/devices/{_deviceId}/jobs/notify?timeout={seconds}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<NotifyResult>(_json, cancellationToken);
        return body?.HasPending ?? false;
    }

    /// <summary>回报作业结果。</summary>
    public async Task ReportResultAsync(string jobId, ServerJobResult result, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"{_serverUrl}/api/devices/{_deviceId}/jobs/{jobId}/result",
            new
            {
                status = result.Status,
                completedItems = result.CompletedItems,
                failedItems = result.FailedItems,
                errorMessage = result.ErrorMessage,
            },
            _json,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>进度增量上报。</summary>
    public async Task ReportProgressAsync(string jobId, ServerJobProgress progress, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"{_serverUrl}/api/devices/{_deviceId}/jobs/{jobId}/progress",
            new
            {
                completedItems = progress.CompletedItems,
                failedItems = progress.FailedItems,
            },
            _json,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>长轮询通知响应。</summary>
internal sealed record NotifyResult(bool HasPending);
