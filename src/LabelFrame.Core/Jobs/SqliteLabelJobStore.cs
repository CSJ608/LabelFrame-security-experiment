using LabelFrame.Core.Data;
using LabelFrame.Core.Errors;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Jobs;

/// <summary>SQLite 作业存储：表 jobs / job_items，request_id 唯一索引实现幂等，服务重启不丢作业。</summary>
public sealed class SqliteLabelJobStore : ILabelJobStore
{
    private const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS jobs (
            id         TEXT PRIMARY KEY,
            request_id TEXT NOT NULL UNIQUE,
            status     TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS job_items (
            id            TEXT PRIMARY KEY,
            job_id        TEXT NOT NULL REFERENCES jobs(id),
            item_index    INTEGER NOT NULL,
            status        TEXT NOT NULL,
            zpl           TEXT NOT NULL,
            command_bytes BLOB NULL,
            error_code    TEXT NULL,
            error_message TEXT NULL,
            updated_at    TEXT NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_job_items_job_index ON job_items(job_id, item_index);
        CREATE INDEX IF NOT EXISTS ix_job_items_job_id ON job_items(job_id);
        """;

    private readonly string _connectionString;

    /// <summary>创建 SQLite 作业存储。</summary>
    /// <param name="databasePath">数据库文件路径（父目录不存在时自动创建）。</param>
    public SqliteLabelJobStore(string databasePath)
    {
        _connectionString = SqliteSupport.BuildConnectionString(databasePath);
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = CreateTablesSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await MigrateCommandBytesAsync(connection, cancellationToken);
    }

    /// <summary>旧库兼容迁移：job_items 表缺少 command_bytes 列时补列（决策 #195 二进制指令载荷；已存在则跳过；失败静默忽略，不影响启动）。</summary>
    private static async Task MigrateCommandBytesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var hasCommandBytes = false;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(job_items);";
            await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "command_bytes", StringComparison.OrdinalIgnoreCase))
                {
                    hasCommandBytes = true;
                    break;
                }
            }
        }

        if (hasCommandBytes)
        {
            return;
        }

        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE job_items ADD COLUMN command_bytes BLOB NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // 已存在列等竞态：静默忽略，保持旧库可启动
        }
    }

    /// <inheritdoc />
    public async Task<LabelJob> CreateJobAsync(LabelJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO jobs (id, request_id, status, created_at, updated_at)
                VALUES ($id, $requestId, $status, $createdAt, $updatedAt);
                """;
            command.Parameters.AddWithValue("$id", job.Id);
            command.Parameters.AddWithValue("$requestId", job.RequestId);
            command.Parameters.AddWithValue("$status", job.Status.ToString());
            command.Parameters.AddWithValue("$createdAt", SqliteSupport.Format(job.CreatedAt));
            command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(job.UpdatedAt));
            var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
            if (inserted == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                var requestIdParams = new Dictionary<string, string> { ["requestId"] = job.RequestId };
                return await GetJobByRequestIdCoreAsync(connection, job.RequestId, cancellationToken)
                    ?? throw new LabelJobException(
                        JobErrorCodes.InvalidRequest,
                        ErrorMessageTemplates.Format("请求重复且作业不存在：{requestId}。", requestIdParams),
                        requestIdParams);
            }
        }

        foreach (var item in job.Items)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO job_items (id, job_id, item_index, status, zpl, command_bytes, error_code, error_message, updated_at)
                VALUES ($id, $jobId, $index, $status, $zpl, $commandBytes, NULL, NULL, $updatedAt);
                """;
            command.Parameters.AddWithValue("$id", item.Id);
            command.Parameters.AddWithValue("$jobId", item.JobId);
            command.Parameters.AddWithValue("$index", item.Index);
            command.Parameters.AddWithValue("$status", item.Status.ToString());
            command.Parameters.AddWithValue("$zpl", item.Zpl);
            command.Parameters.AddWithValue("$commandBytes", (object?)item.CommandBytes ?? DBNull.Value);
            command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(job.UpdatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    /// <inheritdoc />
    public Task<LabelJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        => GetJobCoreAsync(jobId, byRequestId: false, cancellationToken);

    /// <inheritdoc />
    public Task<LabelJob?> GetJobByRequestIdAsync(string requestId, CancellationToken cancellationToken = default)
        => GetJobCoreAsync(requestId, byRequestId: true, cancellationToken);

    /// <inheritdoc />
    public async Task<LabelJob?> SetJobStatusAsync(string jobId, LabelJobStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs SET status = $status, updated_at = $updatedAt WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", jobId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 0 ? null : await LoadJobCoreAsync(connection, jobId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LabelJob?> SetItemStatusAsync(
        string jobId,
        string itemId,
        LabelJobItemStatus status,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE job_items
            SET status = $status, error_code = $errorCode, error_message = $errorMessage, updated_at = $updatedAt
            WHERE job_id = $jobId AND id = $itemId;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$errorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$jobId", jobId);
        command.Parameters.AddWithValue("$itemId", itemId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 0 ? null : await LoadJobCoreAsync(connection, jobId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LabelJob>> ListJobsByStatusAsync(LabelJobStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var jobs = new List<LabelJob>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM jobs WHERE status = $status ORDER BY created_at, id;";
            command.Parameters.AddWithValue("$status", status.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var job = await LoadJobCoreAsync(connection, reader.GetString(0), cancellationToken);
                if (job is not null)
                {
                    jobs.Add(job);
                }
            }
        }

        return jobs;
    }

    /// <inheritdoc />
    public async Task<bool> HasPendingItemsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM job_items WHERE status = $pending LIMIT 1);";
        command.Parameters.AddWithValue("$pending", LabelJobItemStatus.Pending.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is 1L or 1;
    }

    public async Task<IReadOnlyList<LabelJob>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var jobs = new List<LabelJob>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM jobs ORDER BY created_at DESC, id LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var job = await LoadJobCoreAsync(connection, reader.GetString(0), cancellationToken);
                if (job is not null)
                {
                    jobs.Add(job);
                }
            }
        }

        return jobs;
    }

    private async Task<LabelJob?> GetJobCoreAsync(string key, bool byRequestId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return byRequestId
            ? await GetJobByRequestIdCoreAsync(connection, key, cancellationToken)
            : await LoadJobCoreAsync(connection, key, cancellationToken);
    }

    private static async Task<LabelJob?> GetJobByRequestIdCoreAsync(SqliteConnection connection, string requestId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM jobs WHERE request_id = $requestId LIMIT 1;";
        command.Parameters.AddWithValue("$requestId", requestId);
        var id = await command.ExecuteScalarAsync(cancellationToken) as string;
        return id is null ? null : await LoadJobCoreAsync(connection, id, cancellationToken);
    }

    private static async Task<LabelJob?> LoadJobCoreAsync(SqliteConnection connection, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, request_id, status, created_at, updated_at
            FROM jobs WHERE id = $id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var job = new LabelJob
        {
            Id = reader.GetString(0),
            RequestId = reader.GetString(1),
            Status = Enum.Parse<LabelJobStatus>(reader.GetString(2)),
            CreatedAt = SqliteSupport.Parse(reader.GetString(3)),
            UpdatedAt = SqliteSupport.Parse(reader.GetString(4)),
            Items = Array.Empty<LabelJobItem>(),
        };

        var items = new List<LabelJobItem>();
        await using (var itemCommand = connection.CreateCommand())
        {
            itemCommand.CommandText = """
                SELECT id, job_id, item_index, status, zpl, command_bytes, error_code, error_message
                FROM job_items WHERE job_id = $jobId ORDER BY item_index;
                """;
            itemCommand.Parameters.AddWithValue("$jobId", jobId);
            await using var itemReader = await itemCommand.ExecuteReaderAsync(cancellationToken);
            while (await itemReader.ReadAsync(cancellationToken))
            {
                items.Add(new LabelJobItem
                {
                    Id = itemReader.GetString(0),
                    JobId = itemReader.GetString(1),
                    Index = itemReader.GetInt32(2),
                    Status = Enum.Parse<LabelJobItemStatus>(itemReader.GetString(3)),
                    Zpl = itemReader.GetString(4),
                    CommandBytes = itemReader.IsDBNull(5) ? null : (byte[])itemReader[5],
                    ErrorCode = itemReader.IsDBNull(6) ? null : itemReader.GetString(6),
                    ErrorMessage = itemReader.IsDBNull(7) ? null : itemReader.GetString(7),
                });
            }
        }

        return new LabelJob
        {
            Id = job.Id,
            RequestId = job.RequestId,
            Status = job.Status,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            Items = items,
        };
    }

    private Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        => SqliteSupport.OpenAsync(_connectionString, cancellationToken);
}