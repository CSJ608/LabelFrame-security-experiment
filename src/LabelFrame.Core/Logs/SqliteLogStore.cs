using LabelFrame.Core.Data;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Logs;

/// <summary>日志条目（PDA / 设备回传）。</summary>
public sealed record LogEntry(string DeviceId, DateTimeOffset Time, string Line);

/// <summary>SQLite 日志存储：设备日志回传与查询（PDA 调试用）。</summary>
public sealed class SqliteLogStore
{
    /// <summary>量闸循环轮数上限：防止 VACUUM 后仍不收敛（如阈值小于空库体积）时无限删库。</summary>
    private const int SizeLimitMaxRounds = 10;

    /// <summary>VACUUM 命令超时（秒）：默认 5s 与 /api/logs 并发写撞锁即失败，放大到 60s。</summary>
    private const int VacuumTimeoutSeconds = 60;

    private readonly string _connectionString;

    /// <summary>创建日志存储（默认 %LOCALAPPDATA%\LabelFrame\logs.db）。</summary>
    public SqliteLogStore(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LabelFrame",
            "logs.db");
        DatabasePath = Path.GetFullPath(path);
        _connectionString = SqliteSupport.BuildConnectionString(path);
    }

    /// <summary>日志库文件路径（绝对路径；量闸度量口径 = 本文件 + 同名 -wal 合计，见 <see cref="EnforceSizeLimitAsync"/>）。</summary>
    public string DatabasePath { get; }

    /// <summary>建表。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS logs (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id TEXT NOT NULL,
                time      TEXT NOT NULL,
                line      TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_logs_device_time ON logs(device_id, time);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>追加设备日志（按行拆分入库：lines 各元素及元素内换行符均按物理行拆为独立记录）。</summary>
    public async Task AppendAsync(string deviceId, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || lines.Count == 0)
        {
            return;
        }

        // 按物理行拆分（\r\n / \n / \r 统一处理），空白行不落库；同一提交共用同一时间戳、单事务原子写入
        var rows = lines
            .SelectMany(line => line.Split(["\u000D\u000A", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO logs (device_id, time, line)
            VALUES ($device, $time, $line);
            """;
        var deviceParameter = command.Parameters.Add("$device", SqliteType.Text);
        var timeParameter = command.Parameters.Add("$time", SqliteType.Text);
        var lineParameter = command.Parameters.Add("$line", SqliteType.Text);
        deviceParameter.Value = deviceId;
        timeParameter.Value = SqliteSupport.Format(now);
        foreach (var row in rows)
        {
            lineParameter.Value = row;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>查询日志（可按设备 / 时间过滤，最多返回 500 条）。</summary>
    public async Task<IReadOnlyList<LogEntry>> QueryAsync(
        string? deviceId = null,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var sql = "SELECT device_id, time, line FROM logs";
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            conditions.Add("device_id = $device");
            command.Parameters.AddWithValue("$device", deviceId);
        }

        if (since is not null)
        {
            conditions.Add("time > $since");
            command.Parameters.AddWithValue("$since", SqliteSupport.Format(since.Value));
        }

        if (conditions.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", conditions);
        }

        sql += " ORDER BY id DESC LIMIT 500;";
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<LogEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new LogEntry(
                reader.GetString(0),
                SqliteSupport.Parse(reader.GetString(1)),
                reader.GetString(2)));
        }

        return entries;
    }

    /// <summary>删除早于截止时间的日志（历史清理用）。</summary>
    public async Task<int> DeleteBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM logs WHERE time < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", SqliteSupport.Format(cutoff));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>量闸执行结果：累计删除行数 + 异常态警告（null = 无异常；VACUUM 失败 / 表空仍超阈等，
    /// 由调用方经 ILogger 记录——Core 库无日志抽象，服务宿主可能无控制台， 留观项②）。</summary>
    public sealed record SizeLimitResult(int DeletedRows, string? VacuumWarning);

    /// <summary>
    /// logs.db 按量闸：库文件（logs.db + logs.db-wal 合计）超过 <paramref name="maxBytes"/> 时
    /// 循环「删一批最旧 → VACUUM → wal_checkpoint(TRUNCATE)」直至合计回到阈值内 / 表已空 / 轮数上限。
    /// 按大小删最旧不受保留期下限约束（有意行为）；返回累计删除行数与异常态警告。
    /// 收敛判据只取 VACUUM + checkpoint 之后的合计大小——DELETE 之后主文件不会缩小（空闲页仅库内复用），
    /// 以删除间歇的文件大小判断收敛会在首次超阈即删空全表。
    /// </summary>
    /// <param name="maxBytes">阈值（字节；0 或负值 = 不执行）。</param>
    public async Task<SizeLimitResult> EnforceSizeLimitAsync(long maxBytes, CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
        {
            return new SizeLimitResult(0, null);
        }

        var totalDeleted = 0;
        for (var round = 0; round < SizeLimitMaxRounds; round++)
        {
            if (MeasureTotalSize() <= maxBytes)
            {
                return new SizeLimitResult(totalDeleted, null);
            }

            var deleted = await DeleteOldestBatchAsync(cancellationToken);
            totalDeleted += deleted;
            if (deleted == 0)
            {
                // 表已空仍超阈（阈值小于空库体积 / 空间回收失败）——本轮放弃，不无限重试
                return new SizeLimitResult(totalDeleted,
                    $"日志库已清空仍超阈值（{DatabasePath}，阈值 {maxBytes} 字节）——阈值可能小于空库体积，请复核 LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB。");
            }

            var vacuumWarning = await TryVacuumAsync(cancellationToken);
            if (vacuumWarning is not null)
            {
                // VACUUM 失败后度量不再可信（主文件未收缩），继续删只会按过期量纲误删——本轮终止、下个清理周期重试
                return new SizeLimitResult(totalDeleted, vacuumWarning);
            }
        }

        return new SizeLimitResult(totalDeleted, null);
    }

    /// <summary>量纲 = 主库文件 + 同名 -wal 合计大小（字节）。被动 checkpoint 不截断 -wal，
    /// VACUUM 大事务可把 -wal 涨至库大小——合计口径才反映真实磁盘占用。</summary>
    private long MeasureTotalSize()
    {
        long GetLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
        return GetLength(DatabasePath) + GetLength(DatabasePath + "-wal");
    }

    /// <summary>删一批最旧：批大小 max(1000, 行数/10)——既保证小库快速收敛，也避免大库一次 10% 太慢。</summary>
    private async Task<int> DeleteOldestBatchAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM logs WHERE id IN (
                SELECT id FROM logs ORDER BY id LIMIT max(1000, (SELECT COUNT(*) FROM logs) / 10));
            """;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>独立连接执行 VACUUM + 尽力截断 -wal。失败不抛出（量闸属后台自愈，失败留给下个清理周期），
    /// 返回警告消息（null = 成功）交调用方记录——服务宿主可能无控制台，Console.Error 不可依赖。</summary>
    private async Task<string?> TryVacuumAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = VacuumTimeoutSeconds;
            command.CommandText = "VACUUM; PRAGMA wal_checkpoint(TRUNCATE);";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 停机取消不是 VACUUM 失败——上抛给清理服务按停机路径退出
            throw;
        }
        catch (Exception ex)
        {
            return $"日志库空间回收（VACUUM）失败（{DatabasePath}）：{ex.Message}。本轮量闸终止，下个清理周期重试。";
        }
    }

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        => SqliteSupport.OpenAsync(_connectionString, cancellationToken);
}
