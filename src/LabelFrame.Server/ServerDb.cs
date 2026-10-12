using LabelFrame.Core.Data;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Server;

/// <summary>Server 的 SQLite 存储：设备目录 + 作业表。</summary>
public sealed class ServerDb
{
    private const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS devices (
            id            TEXT PRIMARY KEY,
            name          TEXT NOT NULL,
            registered_at TEXT NOT NULL,
            last_seen_at  TEXT NOT NULL,
            last_ip       TEXT NULL
        );

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

    private readonly string _connectionString;

    /// <summary>创建 Server 存储。</summary>
    public ServerDb(string databasePath)
    {
        _connectionString = SqliteSupport.BuildConnectionString(databasePath);
    }

    /// <summary>建表。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = CreateTablesSql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await MigrateDevicesLastIpAsync(connection, cancellationToken);
        await MigrateServerJobsCallbackUrlAsync(connection, cancellationToken);
        await MigrateServerJobsErrorCodeAsync(connection, cancellationToken);
    }

    /// <summary>旧库兼容迁移：devices 表缺少 last_ip 列时补列（已存在则跳过；失败静默忽略，不影响启动）。</summary>
    private static async Task MigrateDevicesLastIpAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var hasLastIp = false;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(devices);";
            await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "last_ip", StringComparison.OrdinalIgnoreCase))
                {
                    hasLastIp = true;
                    break;
                }
            }
        }

        if (hasLastIp)
        {
            return;
        }

        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE devices ADD COLUMN last_ip TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // 已存在列等竞态 / 约束差异：静默忽略，保持旧库可启动
        }
    }

    /// <summary>旧库兼容迁移：server_jobs 表缺少 callback_url 列时补列（已存在则跳过；失败静默忽略，不影响启动）。</summary>
    private static async Task MigrateServerJobsCallbackUrlAsync(SqliteConnection connection, CancellationToken cancellationToken)
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
    private static async Task MigrateServerJobsErrorCodeAsync(SqliteConnection connection, CancellationToken cancellationToken)
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

    /// <summary>注册 / 更新设备并刷新心跳。</summary>
    public async Task<Device> UpsertDeviceAsync(Device device, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO devices (id, name, registered_at, last_seen_at, last_ip)
            VALUES ($id, $name, $registeredAt, $lastSeenAt, $lastIp)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                last_seen_at = excluded.last_seen_at,
                last_ip = excluded.last_ip;
            """;
        command.Parameters.AddWithValue("$id", device.Id);
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$registeredAt", SqliteSupport.Format(device.RegisteredAt));
        command.Parameters.AddWithValue("$lastSeenAt", SqliteSupport.Format(device.LastSeenAt));
        command.Parameters.AddWithValue("$lastIp", (object?)device.LastIp ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return device;
    }

    /// <summary>刷新设备心跳；返回受影响行数（0 = 设备不存在，免去先查后写）。</summary>
    public async Task<int> TouchDeviceAsync(string deviceId, DateTimeOffset now, string? lastIp = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE devices SET last_seen_at = $now, last_ip = $lastIp WHERE id = $id;";
        command.Parameters.AddWithValue("$now", SqliteSupport.Format(now));
        command.Parameters.AddWithValue("$lastIp", (object?)lastIp ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", deviceId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>查询设备。</summary>
    public async Task<Device?> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, registered_at, last_seen_at, last_ip FROM devices WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Device
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            RegisteredAt = SqliteSupport.Parse(reader.GetString(2)),
            LastSeenAt = SqliteSupport.Parse(reader.GetString(3)),
            LastIp = reader.IsDBNull(4) ? null : reader.GetString(4),
        };
    }

    /// <summary>设备列表。</summary>
    public async Task<IReadOnlyList<Device>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var devices = new List<Device>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, registered_at, last_seen_at, last_ip FROM devices ORDER BY registered_at;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new Device
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                RegisteredAt = SqliteSupport.Parse(reader.GetString(2)),
                LastSeenAt = SqliteSupport.Parse(reader.GetString(3)),
                LastIp = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }

        return devices;
    }

    /// <summary>
    /// 按 last_ip 精确查找设备（忽略大小写；未找到返回 null）。
    /// 同 IP 多行并存（设备号变更后新行插入、旧行 last_ip 无人清除）时按
    /// 最近活跃优先（last_seen_at DESC，注册时间与 id 作确定性平手序）——旧行 stale、新行活跃必命中新行；
    /// 同 IP 多设备且都活跃（如 NAT 共网出口）时跟随最近一次心跳的设备。
    /// </summary>
    public async Task<Device?> FindDeviceByIpAsync(string ip, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, registered_at, last_seen_at, last_ip
            FROM devices
            WHERE last_ip = $ip COLLATE NOCASE
            ORDER BY last_seen_at DESC, registered_at DESC, id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$ip", ip);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Device
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            RegisteredAt = SqliteSupport.Parse(reader.GetString(2)),
            LastSeenAt = SqliteSupport.Parse(reader.GetString(3)),
            LastIp = reader.IsDBNull(4) ? null : reader.GetString(4),
        };
    }


    /// <summary>创建作业；requestId 已存在时返回已有作业。</summary>
    public async Task<ServerJob?> CreateJobAsync(ServerJob job, CancellationToken cancellationToken = default)
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
        return inserted == 0 ? await GetJobByRequestIdAsync(job.RequestId, cancellationToken) : job;
    }

    /// <summary>按作业标识查询。</summary>
    public Task<ServerJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        => GetJobCoreAsync(jobId, byRequestId: false, cancellationToken);

    /// <summary>按幂等键查询。</summary>
    public Task<ServerJob?> GetJobByRequestIdAsync(string requestId, CancellationToken cancellationToken = default)
        => GetJobCoreAsync(requestId, byRequestId: true, cancellationToken);

    /// <summary>领取：同一事务内刷新心跳 + 把目标设备的未过期 Pending 作业置为 Claimed，返回 (心跳受影响行数, 载荷)。</summary>
    /// <remarks>
    /// 写事务合批：原「Touch + Claim」为两个独立自动提交写事务，20 设备并发下各排队一次
    /// SQLite 单写锁；合并为一个事务后每轮领取少一次锁竞争。并发安全不变——圈定仍由单条
    /// UPDATE ... RETURNING 原子完成，多实例 / 并发下不会重复领取同一作业。
    /// <paramref name="ttlCutoff"/> 非 null 时按 created_at 过滤超期作业（正确性兜底，不依赖过期扫描周期）；null = 不过滤（TTL 关闭）。
    /// </remarks>
    public async Task<(int Touched, IReadOnlyList<ServerJob> Jobs)> TouchAndClaimPendingJobsAsync(
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
    public async Task<ServerJob?> UpdateJobResultAsync(
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

        // UPDATE 已受影响说明行存在，直接同连接回读（省去原先的冗余 id 探测查询）
        return await LoadJobCoreAsync(connection, jobId, cancellationToken);
    }

    /// <summary>
    /// 进度增量更新：仅 Claimed 作业接受，计数按字段取 max 单调递增（乱序 / 迟到 / 重复上报不回退）。
    /// 只更新两个计数字段——不改 status / finished_at / error_message，也不刷新 claimed_at（不干预超时回收计龄）。
    /// 返回受影响行数（0 = 作业已非 Claimed，调用方按竞态重读处理）。
    /// </summary>
    public async Task<int> UpdateJobProgressAsync(
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
    public async Task<IReadOnlyList<ServerJob>> ListJobsAsync(int limit = 100, string? deviceId = null, CancellationToken cancellationToken = default)
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

        // Reader 关闭后在同一连接逐个加载（消除原 1+N 次连接的 N+1）
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
    public async Task<bool> HasDeliverablePendingJobsAsync(string deviceId, DateTimeOffset? ttlCutoff = null, CancellationToken cancellationToken = default)
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
    public async Task<int> MarkExpiredJobsAsync(DateTimeOffset now, DateTimeOffset ttlCutoff, string reason, string? errorCode, CancellationToken cancellationToken = default)
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

    /// <summary>
    /// 把失联 Claimed 作业批量回收为 Failed 终态（原因由调用方给出，含错误码与超时时长；原因码 LF_SRV_009 落 error_code）；返回回收条数。
    /// 与回报的竞态由「status = Claimed」条件收敛：回报先落库（Completed / Failed）则本 UPDATE 不命中；
    /// 回收先落库则回报走幂等重放返回既有终态——两个方向都不会互相覆盖。
    /// </summary>
    public async Task<int> MarkTimedOutClaimedJobsAsync(DateTimeOffset now, DateTimeOffset timeoutCutoff, string reason, string? errorCode, CancellationToken cancellationToken = default)
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

    /// <summary>
    /// 删除终态（Completed / Failed / Expired）且结束 / 创建时间早于截止时间的作业（历史清理用）；
    /// 投递行（job_callbacks）随作业一并删除（先删投递行再删作业行，同一判定口径）。
    /// </summary>
    public async Task<int> DeleteTerminalJobsBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
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

    /// <summary>
    /// 终态回调登记统一出口（幂等）：把已到终态且带 callback_url、尚未登记的作业写入投递表
    /// （job_id 主键 + INSERT OR IGNORE，并发 / 重复登记安全）。三处终态转移点（宿主回报、失联回收、
    /// 超 TTL 过期）都经此登记；<paramref name="jobId"/> 非 null 时只处理该作业（回报路径单作业精准登记）。
    /// 返回新登记条数。
    /// </summary>
    public async Task<int> EnqueueJobCallbacksAsync(DateTimeOffset now, string? jobId = null, CancellationToken cancellationToken = default)
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
    public async Task<IReadOnlyList<JobCallbackTask>> ListDueJobCallbacksAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default)
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
    public async Task<JobCallbackTask?> GetJobCallbackAsync(string jobId, CancellationToken cancellationToken = default)
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
    public async Task<int> UpdateJobCallbackStatusAsync(
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

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        => SqliteSupport.OpenAsync(_connectionString, cancellationToken);
}
