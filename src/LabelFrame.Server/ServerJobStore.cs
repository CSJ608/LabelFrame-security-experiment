using LabelFrame.Core.Data;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Server;

/// <summary>server_jobs 聚合的数据访问：作业的创建、领取、进度与终态流转（领取与历史清理按既有事务口径连带触碰 devices / job_callbacks 表）。</summary>
internal sealed class ServerJobStore(string connectionString) : ServerDbStore(connectionString)
{
    /// <summary>建表并执行旧库兼容迁移。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS server_jobs (
                id              TEXT PRIMARY KEY,
                request_id      TEXT NOT NULL UNIQUE,
                target_device_id TEXT NOT NULL,
                status          TEXT NOT NULL,
                created_at      TEXT NOT NULL,
                claimed_at      TEXT NULL,
                finished_at     TEXT NULL,
                total_items     INTEGER NOT NULL,
                completed_items INTEGER NOT NULL DEFAULT 0,
                failed_items    INTEGER NOT NULL DEFAULT 0,
                error_message   TEXT NULL,
                error_code      TEXT NULL,
                payload_json    TEXT NOT NULL,
                callback_url    TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_server_jobs_status_device ON server_jobs(status, target_device_id);
            CREATE INDEX IF NOT EXISTS ix_server_jobs_device ON server_jobs(target_device_id, created_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await MigrateCallbackUrlAsync(connection, cancellationToken);
        await MigrateErrorCodeAsync(connection, cancellationToken);
    }

    /// <summary>旧库兼容迁移：server_jobs 表缺少 callback_url 列时补列（已存在则跳过；失败静默忽略，不影响启动）。</summary>
    private static async Task MigrateCallbackUrlAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var hasCallbackUrl = false;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(server_jobs);";
            await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "callback_url", StringComparison.OrdinalIgnoreCase))
                {
                    hasCallbackUrl = true;
                    break;
                }
            }
        }

        if (hasCallbackUrl)
        {
            return;
        }

        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE server_jobs ADD COLUMN callback_url TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // 已存在列等竞态 / 约束差异：静默忽略，保持旧库可启动
        }
    }

    /// <summary>旧库兼容迁移：server_jobs 表缺少 error_code 列时补列（已存在则跳过；失败静默忽略，不影响启动）。error_message 中文存量不迁移（历史行 error_code 为 NULL）。</summary>
    private static async Task MigrateErrorCodeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var hasErrorCode = false;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(server_jobs);";
            await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "error_code", StringComparison.OrdinalIgnoreCase))
                {
                    hasErrorCode = true;
                    break;
                }
            }
        }

        if (hasErrorCode)
        {
            return;
        }

        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE server_jobs ADD COLUMN error_code TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // 已存在列等竞态 / 约束差异：静默忽略，保持旧库可启动
        }
    }

    /// <summary>创建作业；requestId 已存在时返回已有作业。</summary>
    public async Task<ServerJob?> CreateAsync(ServerJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO server_jobs
                (id, request_id, target_device_id, status, created_at, claimed_at, finished_at,
                 total_items, completed_items, failed_items, error_message, payload_json, callback_url)
            VALUES
                ($id, $requestId, $targetDeviceId, $status, $createdAt, NULL, NULL,
                 $totalItems, 0, 0, NULL, $payloadJson, $callbackUrl);
            """;
        command.Parameters.AddWithValue("$id", job.Id);
        command.Parameters.AddWithValue("$requestId", job.RequestId);
        command.Parameters.AddWithValue("$targetDeviceId", job.TargetDeviceId);
        command.Parameters.AddWithValue("$status", job.Status.ToString());
        command.Parameters.AddWithValue("$createdAt", SqliteSupport.Format(job.CreatedAt));
        command.Parameters.AddWithValue("$totalItems", job.TotalItems);
        command.Parameters.AddWithValue("$payloadJson", job.PayloadJson);
        command.Parameters.AddWithValue("$callbackUrl", (object?)job.CallbackUrl ?? DBNull.Value);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        return inserted == 0 ? await GetByRequestIdAsync(job.RequestId, cancellationToken) : job;
    }

    /// <summary>按作业标识查询。</summary>
    public Task<ServerJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
        => GetJobCoreAsync(jobId, byRequestId: false, cancellationToken);

    /// <summary>按幂等键查询。</summary>
    public Task<ServerJob?> GetByRequestIdAsync(string requestId, CancellationToken cancellationToken = default)
        => GetJobCoreAsync(requestId, byRequestId: true, cancellationToken);

    /// <summary>领取：同一事务内刷新心跳 + 把目标设备的未过期 Pending 作业置为 Claimed，返回 (心跳受影响行数, 载荷)。</summary>
    /// <remarks>心跳与圈定须同一写事务；圈定为单条 UPDATE ... RETURNING 原子完成，多实例 / 并发不会重复领取。<paramref name="ttlCutoff"/> 非 null 时按 created_at 过滤超期作业（正确性兜底，不依赖过期扫描周期）；null = 不过滤（TTL 关闭）。</remarks>
    public async Task<(int Touched, IReadOnlyList<ServerJob> Jobs)> TouchAndClaimPendingAsync(
        string deviceId,
        DateTimeOffset now,
        string? lastIp,
        int limit,
        DateTimeOffset? ttlCutoff = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        int touched;
        List<string> claimedIds = [];
        await using (var touch = connection.CreateCommand())
        {
            touch.Transaction = transaction;
            touch.CommandText = "UPDATE devices SET last_seen_at = $now, last_ip = $lastIp WHERE id = $id;";
            touch.Parameters.AddWithValue("$now", SqliteSupport.Format(now));
            touch.Parameters.AddWithValue("$lastIp", (object?)lastIp ?? DBNull.Value);
            touch.Parameters.AddWithValue("$id", deviceId);
            touched = await touch.ExecuteNonQueryAsync(cancellationToken);
        }

        if (touched > 0)
        {
            await using var claim = connection.CreateCommand();
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE server_jobs
                SET status = $claimed, claimed_at = $claimedAt
                WHERE id IN (
                    SELECT id FROM server_jobs
                    WHERE status = $pending AND target_device_id = $deviceId
                      AND ($ttlCutoff IS NULL OR created_at >= $ttlCutoff)
                    ORDER BY created_at, id LIMIT $limit
                )
                RETURNING id;
                """;
            claim.Parameters.AddWithValue("$claimed", ServerJobStatus.Claimed.ToString());
            claim.Parameters.AddWithValue("$claimedAt", SqliteSupport.Format(now));
            claim.Parameters.AddWithValue("$pending", ServerJobStatus.Pending.ToString());
            claim.Parameters.AddWithValue("$deviceId", deviceId);
            claim.Parameters.AddWithValue("$ttlCutoff", (object?)(ttlCutoff is null ? null : SqliteSupport.Format(ttlCutoff.Value)) ?? DBNull.Value);
            claim.Parameters.AddWithValue("$limit", limit);
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimedIds.Add(reader.GetString(0));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // Reader 关闭后在同一连接逐个加载载荷（圈定已提交，读不占用写事务）
        var jobs = new List<ServerJob>(claimedIds.Count);
        foreach (var id in claimedIds)
        {
            jobs.Add((await LoadJobCoreAsync(connection, id, cancellationToken))!);
        }

        return (touched, jobs);
    }

    /// <summary>更新作业结果。</summary>
    /// <param name="errorCode">失败原因码（宿主回报可选携带；成功回报传 null）。</param>
    public async Task<ServerJob?> UpdateResultAsync(
        string jobId,
        ServerJobStatus status,
        int completedItems,
        int failedItems,
        string? errorMessage,
        DateTimeOffset now,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE server_jobs
            SET status = $status, completed_items = $completedItems, failed_items = $failedItems,
                error_message = $errorMessage, error_code = $errorCode, finished_at = $finishedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$completedItems", completedItems);
        command.Parameters.AddWithValue("$failedItems", failedItems);
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$finishedAt", SqliteSupport.Format(now));
        command.Parameters.AddWithValue("$id", jobId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            return null;
        }

        return await LoadJobCoreAsync(connection, jobId, cancellationToken);
    }

    /// <summary>进度增量更新：仅 Claimed 接受，计数按字段取 max 单调递增（乱序 / 重复上报不回退）；不刷新 claimed_at（超时回收按其计龄）。返回受影响行数（0 = 已非 Claimed，调用方按竞态重读）。</summary>
    public async Task<int> UpdateProgressAsync(
        string jobId,
        int completedItems,
        int failedItems,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE server_jobs
            SET completed_items = MAX(completed_items, $completedItems),
                failed_items = MAX(failed_items, $failedItems)
            WHERE id = $id AND status = $claimed;
            """;
        command.Parameters.AddWithValue("$completedItems", Math.Max(0, completedItems));
        command.Parameters.AddWithValue("$failedItems", Math.Max(0, failedItems));
        command.Parameters.AddWithValue("$id", jobId);
        command.Parameters.AddWithValue("$claimed", ServerJobStatus.Claimed.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>作业列表（按创建时间倒序；可选 deviceId 过滤——客户端只看自己的作业，服务端 UI 不传看全部）。</summary>
    public async Task<IReadOnlyList<ServerJob>> ListAsync(int limit = 100, string? deviceId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        List<string> ids = [];
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = string.IsNullOrWhiteSpace(deviceId)
                ? "SELECT id FROM server_jobs ORDER BY created_at DESC, id LIMIT $limit;"
                : "SELECT id FROM server_jobs WHERE target_device_id = $deviceId ORDER BY created_at DESC, id LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                command.Parameters.AddWithValue("$deviceId", deviceId);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetString(0));
            }
        }

        var jobs = new List<ServerJob>(ids.Count);
        foreach (var id in ids)
        {
            var job = await LoadJobCoreAsync(connection, id, cancellationToken);
            if (job is not null)
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    /// <summary>查询设备当前是否有未过期 Pending 作业（notify 挂起前积压预检；ttlCutoff null = 不过滤）。</summary>
    public async Task<bool> HasDeliverablePendingAsync(string deviceId, DateTimeOffset? ttlCutoff = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM server_jobs
                WHERE status = $pending AND target_device_id = $deviceId
                  AND ($ttlCutoff IS NULL OR created_at >= $ttlCutoff)
            );
            """;
        command.Parameters.AddWithValue("$pending", ServerJobStatus.Pending.ToString());
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue("$ttlCutoff", (object?)(ttlCutoff is null ? null : SqliteSupport.Format(ttlCutoff.Value)) ?? DBNull.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is long exists && exists != 0;
    }

    /// <summary>把超期 Pending 作业批量标记为 Expired 终态（失败原因由调用方给出，含具体 TTL 时长；原因码 LF_SRV_011 落 error_code）；返回标记条数。</summary>
    public async Task<int> MarkExpiredAsync(DateTimeOffset now, DateTimeOffset ttlCutoff, string reason, string? errorCode, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE server_jobs
            SET status = $expired, finished_at = $now, error_message = $reason, error_code = $errorCode
            WHERE status = $pending AND created_at < $cutoff;
            """;
        command.Parameters.AddWithValue("$expired", ServerJobStatus.Expired.ToString());
        command.Parameters.AddWithValue("$pending", ServerJobStatus.Pending.ToString());
        command.Parameters.AddWithValue("$now", SqliteSupport.Format(now));
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$errorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$cutoff", SqliteSupport.Format(ttlCutoff));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>把失联 Claimed 批量回收为 Failed 终态（原因码 LF_SRV_009 落 error_code）；返回回收条数。与回报的竞态由「status = Claimed」条件收敛：回报先落库则本 UPDATE 不命中，回收先落库则回报幂等重放返回既有终态——互不覆盖。</summary>
    public async Task<int> MarkTimedOutClaimedAsync(DateTimeOffset now, DateTimeOffset timeoutCutoff, string reason, string? errorCode, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE server_jobs
            SET status = $failed, finished_at = $now, error_message = $reason, error_code = $errorCode
            WHERE status = $claimed AND COALESCE(claimed_at, created_at) < $cutoff;
            """;
        command.Parameters.AddWithValue("$failed", ServerJobStatus.Failed.ToString());
        command.Parameters.AddWithValue("$claimed", ServerJobStatus.Claimed.ToString());
        command.Parameters.AddWithValue("$now", SqliteSupport.Format(now));
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$errorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$cutoff", SqliteSupport.Format(timeoutCutoff));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>删除早于截止时间的终态作业（历史清理用）；投递行（job_callbacks）随作业一并删除（同一判定口径）。</summary>
    public async Task<int> DeleteTerminalBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using (var callbacks = connection.CreateCommand())
        {
            callbacks.CommandText = """
                DELETE FROM job_callbacks
                WHERE job_id IN (
                    SELECT id FROM server_jobs
                    WHERE status IN ('Completed', 'Failed', 'Expired')
                      AND COALESCE(finished_at, created_at) < $cutoff
                );
                """;
            callbacks.Parameters.AddWithValue("$cutoff", SqliteSupport.Format(cutoff));
            await callbacks.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM server_jobs
            WHERE status IN ('Completed', 'Failed', 'Expired')
              AND COALESCE(finished_at, created_at) < $cutoff;
            """;
        command.Parameters.AddWithValue("$cutoff", SqliteSupport.Format(cutoff));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<ServerJob?> GetJobCoreAsync(string key, bool byRequestId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = byRequestId
            ? "SELECT id FROM server_jobs WHERE request_id = $key LIMIT 1;"
            : "SELECT id FROM server_jobs WHERE id = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        var id = await command.ExecuteScalarAsync(cancellationToken) as string;
        return id is null ? null : await LoadJobCoreAsync(connection, id, cancellationToken);
    }

    private static async Task<ServerJob?> LoadJobCoreAsync(SqliteConnection connection, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, request_id, target_device_id, status, created_at, claimed_at, finished_at,
                   total_items, completed_items, failed_items, error_message, error_code, payload_json, callback_url
            FROM server_jobs WHERE id = $id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ServerJob
        {
            Id = reader.GetString(0),
            RequestId = reader.GetString(1),
            TargetDeviceId = reader.GetString(2),
            Status = Enum.Parse<ServerJobStatus>(reader.GetString(3)),
            CreatedAt = SqliteSupport.Parse(reader.GetString(4)),
            ClaimedAt = reader.IsDBNull(5) ? null : SqliteSupport.Parse(reader.GetString(5)),
            FinishedAt = reader.IsDBNull(6) ? null : SqliteSupport.Parse(reader.GetString(6)),
            TotalItems = reader.GetInt32(7),
            CompletedItems = reader.GetInt32(8),
            FailedItems = reader.GetInt32(9),
            ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
            ErrorCode = reader.IsDBNull(11) ? null : reader.GetString(11),
            PayloadJson = reader.GetString(12),
            CallbackUrl = reader.IsDBNull(13) ? null : reader.GetString(13),
        };
    }
}
