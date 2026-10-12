namespace LabelFrame.WinHost.Routing;

/// <summary>Server 路由客户端接口（便于测试替换）。</summary>
public interface IServerJobPoller
{
    /// <summary>注册设备（同时作为心跳）。</summary>
    Task RegisterAsync(CancellationToken cancellationToken = default);

    /// <summary>领取本设备的定向作业。</summary>
    Task<IReadOnlyList<ServerJobPayload>> FetchPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>长轮询等待本设备出现待领取作业（服务端挂起，作业到达立即返回；超时返回 false）。</summary>
    Task<bool> WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>回报作业结果。</summary>
    Task ReportResultAsync(string jobId, ServerJobResult result, CancellationToken cancellationToken = default);

    /// <summary>进度增量上报（仅 Claimed 语义在服务端校验；计数 max 单调）。</summary>
    Task ReportProgressAsync(string jobId, ServerJobProgress progress, CancellationToken cancellationToken = default);
}