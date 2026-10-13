using LabelFrame.Core.Data;

namespace LabelFrame.Server;

/// <summary>job_callbacks 聚合的数据访问：终态回调任务的登记、到期扫描与投递状态流转（登记时按既有口径扫描 server_jobs 表）。</summary>
internal sealed class JobCallbackStore(string connectionString) : ServerDbStore(connectionString)
{
    /// <summary>建表。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS job_callbacks (
                job_id        TEXT PRIMARY KEY,
                request_id    TEXT NOT NULL,
                url           TEXT NOT NULL,
                status        TEXT NOT NULL,
                attempts      INTEGER NOT NULL DEFAULT 0,
                next_retry_at TEXT NOT NULL,
                last_error    TEXT NULL,
                created_at    TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_job_callbacks_due ON job_callbacks(status, next_retry_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>终态回调登记统一出口（幂等：job_id 主键 + INSERT OR IGNORE，并发 / 重复登记安全）。三处终态转移点（宿主回报、失联回收、超 TTL 过期）都经此登记，漏一处即回调漏报；<paramref name="jobId"/> 非 null 时只处理该作业（回报路径单作业精准登记）。返回新登记条数。</summary>
    public async Task<int> EnqueueAsync(DateTimeOffset now, string? jobId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO job_callbacks
                (job_id, request_id, url, status, attempts, next_retry_at, last_error, created_at)
            SELECT id, request_id, callback_url, $pending, 0, $now, NULL, $now
            FROM server_jobs
            WHERE callback_url IS NOT NULL
              AND status IN ('Completed', 'Failed', 'Expired')
              AND ($jobId IS NULL OR id = $jobId)
              AND NOT EXISTS (SELECT 1 FROM job_callbacks WHERE job_callbacks.job_id = server_jobs.id);
            """;
        command.Parameters.AddWithValue("$pending", JobCallbackStatus.Pending.ToString());
        command.Parameters.AddWithValue("$now", SqliteSupport.Format(now));
        command.Parameters.AddWithValue("$jobId", (object?)jobId ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>查询到期的待投递回调任务（Pending 且 next_retry_at 不晚于 now，按到期先后；limit 上限）。</summary>
    public async Task<IReadOnlyList<JobCallbackTask>> ListDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var tasks = new List<JobCallbackTask>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id, request_id, url, status, attempts, next_retry_at, last_error, created_at
            FROM job_callbacks
            WHERE status = $pending AND next_retry_at <= $now
            ORDER BY next_retry_at, job_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pending", JobCallbackStatus.Pending.ToString());
        command.Parameters.AddWithValue("$now", SqliteSupport.Format(now));
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(ReadCallbackTask(reader));
        }

        return tasks;
    }

    /// <summary>查询单个作业的回调投递任务（无回调或未登记时返回 null；作业视图透出投递状态用）。</summary>
    public async Task<JobCallbackTask?> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id, request_id, url, status, attempts, next_retry_at, last_error, created_at
            FROM job_callbacks WHERE job_id = $jobId LIMIT 1;
            """;
        command.Parameters.AddWithValue("$jobId", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCallbackTask(reader) : null;
    }

    /// <summary>更新投递任务状态（成功 Delivered / 失败退避 Pending / 死信 DeadLetter；nextRetryAt 按状态语义填写，lastError 成功时清空）。</summary>
    public async Task<int> UpdateStatusAsync(
        string jobId,
        JobCallbackStatus status,
        int attempts,
        DateTimeOffset nextRetryAt,
        string? lastError,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE job_callbacks
            SET status = $status, attempts = $attempts, next_retry_at = $nextRetryAt, last_error = $lastError
            WHERE job_id = $jobId;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$attempts", attempts);
        command.Parameters.AddWithValue("$nextRetryAt", SqliteSupport.Format(nextRetryAt));
        command.Parameters.AddWithValue("$lastError", (object?)lastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$jobId", jobId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JobCallbackTask ReadCallbackTask(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        JobId = reader.GetString(0),
        RequestId = reader.GetString(1),
        Url = reader.GetString(2),
        Status = Enum.Parse<JobCallbackStatus>(reader.GetString(3)),
        Attempts = reader.GetInt32(4),
        NextRetryAt = SqliteSupport.Parse(reader.GetString(5)),
        LastError = reader.IsDBNull(6) ? null : reader.GetString(6),
        CreatedAt = SqliteSupport.Parse(reader.GetString(7)),
    };
}
