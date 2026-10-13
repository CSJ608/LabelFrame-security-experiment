using LabelFrame.Core.Data;

namespace LabelFrame.Server;

/// <summary>Server 的 SQLite 存储门面：按聚合组合 devices / server_jobs / job_callbacks 三个数据访问类，保持既有公共 API 不变。</summary>
public sealed class ServerDb
{
    private readonly ServerDeviceStore _devices;
    private readonly ServerJobStore _jobs;
    private readonly JobCallbackStore _callbacks;

    /// <summary>创建 Server 存储。</summary>
    public ServerDb(string databasePath)
    {
        var connectionString = SqliteSupport.BuildConnectionString(databasePath);
        _devices = new ServerDeviceStore(connectionString);
        _jobs = new ServerJobStore(connectionString);
        _callbacks = new JobCallbackStore(connectionString);
    }

    /// <summary>建表并执行旧库兼容迁移。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _devices.InitializeAsync(cancellationToken);
        await _jobs.InitializeAsync(cancellationToken);
        await _callbacks.InitializeAsync(cancellationToken);
    }

    /// <summary>注册 / 更新设备并刷新心跳。</summary>
    public Task<Device> UpsertDeviceAsync(Device device, CancellationToken cancellationToken = default)
        => _devices.UpsertAsync(device, cancellationToken);

    /// <summary>刷新设备心跳；返回受影响行数（0 = 设备不存在，免去先查后写）。</summary>
    public Task<int> TouchDeviceAsync(string deviceId, DateTimeOffset now, string? lastIp = null, CancellationToken cancellationToken = default)
        => _devices.TouchAsync(deviceId, now, lastIp, cancellationToken);

    /// <summary>查询设备。</summary>
    public Task<Device?> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        => _devices.GetAsync(deviceId, cancellationToken);

    /// <summary>设备列表。</summary>
    public Task<IReadOnlyList<Device>> ListDevicesAsync(CancellationToken cancellationToken = default)
        => _devices.ListAsync(cancellationToken);

    /// <summary>按 last_ip 精确查找设备（忽略大小写；同 IP 多行取最近活跃）。</summary>
    public Task<Device?> FindDeviceByIpAsync(string ip, CancellationToken cancellationToken = default)
        => _devices.FindByIpAsync(ip, cancellationToken);

    /// <summary>创建作业；requestId 已存在时返回已有作业。</summary>
    public Task<ServerJob?> CreateJobAsync(ServerJob job, CancellationToken cancellationToken = default)
        => _jobs.CreateAsync(job, cancellationToken);

    /// <summary>按作业标识查询。</summary>
    public Task<ServerJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        => _jobs.GetAsync(jobId, cancellationToken);

    /// <summary>按幂等键查询。</summary>
    public Task<ServerJob?> GetJobByRequestIdAsync(string requestId, CancellationToken cancellationToken = default)
        => _jobs.GetByRequestIdAsync(requestId, cancellationToken);

    /// <summary>领取：同一事务内刷新心跳 + 把目标设备的未过期 Pending 作业置为 Claimed，返回 (心跳受影响行数, 载荷)。</summary>
    public Task<(int Touched, IReadOnlyList<ServerJob> Jobs)> TouchAndClaimPendingJobsAsync(
        string deviceId,
        DateTimeOffset now,
        string? lastIp,
        int limit,
        DateTimeOffset? ttlCutoff = null,
        CancellationToken cancellationToken = default)
        => _jobs.TouchAndClaimPendingAsync(deviceId, now, lastIp, limit, ttlCutoff, cancellationToken);

    /// <summary>更新作业结果。</summary>
    public Task<ServerJob?> UpdateJobResultAsync(
        string jobId,
        ServerJobStatus status,
        int completedItems,
        int failedItems,
        string? errorMessage,
        DateTimeOffset now,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
        => _jobs.UpdateResultAsync(jobId, status, completedItems, failedItems, errorMessage, now, errorCode, cancellationToken);

    /// <summary>进度增量更新（仅 Claimed 接受，计数单调递增）。</summary>
    public Task<int> UpdateJobProgressAsync(
        string jobId,
        int completedItems,
        int failedItems,
        CancellationToken cancellationToken = default)
        => _jobs.UpdateProgressAsync(jobId, completedItems, failedItems, cancellationToken);

    /// <summary>作业列表（按创建时间倒序；可选 deviceId 过滤）。</summary>
    public Task<IReadOnlyList<ServerJob>> ListJobsAsync(int limit = 100, string? deviceId = null, CancellationToken cancellationToken = default)
        => _jobs.ListAsync(limit, deviceId, cancellationToken);

    /// <summary>查询设备当前是否有未过期 Pending 作业。</summary>
    public Task<bool> HasDeliverablePendingJobsAsync(string deviceId, DateTimeOffset? ttlCutoff = null, CancellationToken cancellationToken = default)
        => _jobs.HasDeliverablePendingAsync(deviceId, ttlCutoff, cancellationToken);

    /// <summary>把超期 Pending 作业批量标记为 Expired 终态；返回标记条数。</summary>
    public Task<int> MarkExpiredJobsAsync(DateTimeOffset now, DateTimeOffset ttlCutoff, string reason, string? errorCode, CancellationToken cancellationToken = default)
        => _jobs.MarkExpiredAsync(now, ttlCutoff, reason, errorCode, cancellationToken);

    /// <summary>把失联 Claimed 批量回收为 Failed 终态；返回回收条数。</summary>
    public Task<int> MarkTimedOutClaimedJobsAsync(DateTimeOffset now, DateTimeOffset timeoutCutoff, string reason, string? errorCode, CancellationToken cancellationToken = default)
        => _jobs.MarkTimedOutClaimedAsync(now, timeoutCutoff, reason, errorCode, cancellationToken);

    /// <summary>删除早于截止时间的终态作业及其回调投递行。</summary>
    public Task<int> DeleteTerminalJobsBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => _jobs.DeleteTerminalBeforeAsync(cutoff, cancellationToken);

    /// <summary>终态回调登记统一出口（幂等）；返回新登记条数。</summary>
    public Task<int> EnqueueJobCallbacksAsync(DateTimeOffset now, string? jobId = null, CancellationToken cancellationToken = default)
        => _callbacks.EnqueueAsync(now, jobId, cancellationToken);

    /// <summary>查询到期的待投递回调任务。</summary>
    public Task<IReadOnlyList<JobCallbackTask>> ListDueJobCallbacksAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default)
        => _callbacks.ListDueAsync(now, limit, cancellationToken);

    /// <summary>查询单个作业的回调投递任务（未登记返回 null）。</summary>
    public Task<JobCallbackTask?> GetJobCallbackAsync(string jobId, CancellationToken cancellationToken = default)
        => _callbacks.GetAsync(jobId, cancellationToken);

    /// <summary>更新投递任务状态。</summary>
    public Task<int> UpdateJobCallbackStatusAsync(
        string jobId,
        JobCallbackStatus status,
        int attempts,
        DateTimeOffset nextRetryAt,
        string? lastError,
        CancellationToken cancellationToken = default)
        => _callbacks.UpdateStatusAsync(jobId, status, attempts, nextRetryAt, lastError, cancellationToken);
}
