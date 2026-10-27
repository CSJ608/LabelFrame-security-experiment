using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.AndroidHost.Api;
using LabelFrame.Core.Layout;

namespace LabelFrame.AndroidHost;

/// <summary>设备领取到的 Server 作业。</summary>
public sealed record PendingJob(string JobId, string RequestId, int TotalItems, TemplateDto Template, IReadOnlyList<LabelDto> Labels);

/// <summary>回报结果。</summary>
public sealed record JobResult(string Status, int CompletedItems, int FailedItems, string? ErrorMessage);

/// <summary>进度增量上报（计数口径与结果回报一致）。</summary>
public sealed record JobProgress(int CompletedItems, int FailedItems);

/// <summary>Server 轮询客户端：注册 / 心跳、长轮询通知、领取定向作业、回报结果（与 WinHost 同构，内联实现）。</summary>
public sealed class ServerPoller : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly string _serverUrl;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly JsonSerializerOptions _json;

    /// <summary>创建轮询客户端。</summary>
    public ServerPoller(string serverUrl, string deviceId, string? deviceName = null)
    {
        // 超时按请求单独控制（notify 长轮询可达 20s+，不能被全局 Timeout 掐断）
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 64 * 1024 * 1024 };
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

    /// <summary>Server 基地址（去除尾部斜杠；日志目标用）。</summary>
    public string ServerUrl => _serverUrl;

    /// <summary>注册设备（同时作为心跳）。</summary>
    public Task RegisterAsync(CancellationToken cancellationToken = default)
        => PostAsync($"{_serverUrl}/api/devices", new { deviceId = _deviceId, name = _deviceName }, RequestTimeout, cancellationToken);

    /// <summary>长轮询等待本设备待领取作业；服务端在作业到达时立即返回 hasPending=true，否则超时返回 false。</summary>
    public async Task<bool> WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var seconds = (int)Math.Clamp(timeout.TotalSeconds, 1, 30);
        using var response = await GetAsync($"{_serverUrl}/api/devices/{_deviceId}/jobs/notify?timeout={seconds}", timeout + TimeSpan.FromSeconds(5), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NotifyResult>(_json, cancellationToken);
        return body?.HasPending ?? false;
    }

    /// <summary>领取本设备的定向作业。</summary>
    public async Task<IReadOnlyList<PendingJob>> FetchPendingAsync(CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync($"{_serverUrl}/api/devices/{_deviceId}/jobs/pending", RequestTimeout, cancellationToken);
        var jobs = await response.Content.ReadFromJsonAsync<List<ClaimedJobDto>>(_json, cancellationToken)
            ?? throw new InvalidDataException("路由领取响应为空，不能确认领取结果。");
        if (jobs.Any(j => string.IsNullOrWhiteSpace(j.JobId) || string.IsNullOrWhiteSpace(j.RequestId)
            || j.Payload?.Template is null || j.Payload.Labels is null))
            throw new InvalidDataException("路由领取响应不完整，不能丢弃未识别作业。");
        return jobs
            .Select(j => new PendingJob(j.JobId!, j.RequestId!, j.TotalItems, j.Payload!.Template!, j.Payload.Labels!))
            .ToList();
    }

    /// <summary>回报作业结果。</summary>
    public Task ReportResultAsync(string jobId, JobResult result, CancellationToken cancellationToken = default)
        => PostAsync(
            $"{_serverUrl}/api/devices/{_deviceId}/jobs/{jobId}/result",
            new
            {
                status = result.Status,
                completedItems = result.CompletedItems,
                failedItems = result.FailedItems,
                errorMessage = result.ErrorMessage,
            },
            RequestTimeout,
            cancellationToken);

    /// <summary>进度增量上报（与 WinHost 同语义）。</summary>
    public Task ReportProgressAsync(string jobId, JobProgress progress, CancellationToken cancellationToken = default)
        => PostAsync(
            $"{_serverUrl}/api/devices/{_deviceId}/jobs/{jobId}/progress",
            new
            {
                completedItems = progress.CompletedItems,
                failedItems = progress.FailedItems,
            },
            RequestTimeout,
            cancellationToken);

    private async Task<HttpResponseMessage> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        var response = await _http.GetAsync(url, cts.Token);
        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }

    private async Task PostAsync(string url, object payload, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        using var response = await _http.PostAsJsonAsync(url, payload, _json, cts.Token);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>长轮询通知响应。</summary>
    private sealed record NotifyResult(bool HasPending);

    private sealed record ClaimedJobDto(string? JobId, string? RequestId, int TotalItems, JobPayloadDto? Payload);

    private sealed record JobPayloadDto(TemplateDto? Template, IReadOnlyList<LabelDto>? Labels);
}
