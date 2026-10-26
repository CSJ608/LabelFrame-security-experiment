using System.Globalization;
using LabelFrame.Core.Data;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Jobs;

/// <summary>在独占维护连接中备份并原子转换作业库格式。</summary>
internal static class JobStorageMigration
{
    internal const int SplitVersion = 1;
    private const string ItemColumns = "id, job_id, item_index, status, error_code, error_message, updated_at";
    private const string JoinedColumns = "i.id, i.job_id, i.item_index, i.status, i.error_code, i.error_message, i.updated_at, p.zpl, p.command_bytes";
    private const string IndexSql = """
        CREATE UNIQUE INDEX ux_job_items_job_index ON job_items(job_id, item_index);
        CREATE INDEX ix_job_items_job_id ON job_items(job_id);
        """;

    internal static async Task InitializeAsync(string databasePath, string createSql, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SqliteSupport.EnsureInitialized();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
            DefaultTimeout = 1,
        };
        await using (var connection = new SqliteConnection(builder.ToString()))
        {
            await connection.OpenAsync(token);
            await using var transaction = connection.BeginTransaction(deferred: true);
            var version = await ScalarAsync(connection, transaction, "PRAGMA user_version;", token);
            if (version is not (0 or SplitVersion))
                throw new InvalidDataException($"不支持的作业库版本：{version}。");
            var objects = await ScalarAsync(connection, transaction,
                "SELECT count(*) FROM sqlite_schema WHERE name NOT GLOB 'sqlite_*';", token);
            if (version == 0 && objects == 0)
            {
                await transaction.RollbackAsync(token);
                await transaction.DisposeAsync();
                await InitializeEmptyAsync(connection, createSql, token);
                return;
            }
            if (version == SplitVersion)
            {
                await ValidateSchemaAsync(connection, true, token, transaction);
                await ValidateRelationsAsync(connection, transaction, true, token);
                await transaction.CommitAsync(token);
                return;
            }
        }
        await ConvertAsync(databasePath, true, cancellationToken: token);
    }

    private static async Task InitializeEmptyAsync(SqliteConnection connection, string createSql, CancellationToken token)
    {
        // 空库并发首启须先获取写锁再识别，避免多个读事务相互阻塞锁升级。
        await using var transaction = connection.BeginTransaction(deferred: false);
        var version = await ScalarAsync(connection, transaction, "PRAGMA user_version;", token);
        var objects = await ScalarAsync(connection, transaction,
            "SELECT count(*) FROM sqlite_schema WHERE name NOT GLOB 'sqlite_*';", token);
        if (version == 0 && objects == 0)
            await ExecuteAsync(connection, transaction, createSql + "PRAGMA user_version=1;", token);
        else if (version == SplitVersion)
        {
            await ValidateSchemaAsync(connection, true, token, transaction);
            await ValidateRelationsAsync(connection, transaction, true, token);
        }
        else
            throw new InvalidDataException("作业库结构在初始化期间改变，请停止所有宿主后重试。");
        await transaction.CommitAsync(token);
    }

    internal static async Task<string?> ConvertAsync(
        string databasePath,
        bool toSplit,
        Action<string, SqliteConnection>? checkpoint = null,
        Func<string, long>? availableSpace = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SqliteSupport.EnsureInitialized();
        var fullPath = Path.GetFullPath(databasePath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = false,
            DefaultTimeout = 1,
        };
        await using var connection = await OpenExclusiveAsync(builder.ToString(), checkpoint, cancellationToken);

        checkpoint?.Invoke("locked", connection);
        var version = await ScalarAsync(connection, null, "PRAGMA user_version;", cancellationToken);
        if (version is not (0 or SplitVersion))
            throw new InvalidDataException($"不支持的作业库版本：{version}。");
        var split = version == SplitVersion;
        var hasBytes = await ValidateSchemaAsync(connection, split, cancellationToken);
        await ValidateIntegrityAsync(connection, null, split, cancellationToken);
        if (split == toSplit) return null;

        var pageCount = await ScalarAsync(connection, null, "PRAGMA page_count;", cancellationToken);
        var pageSize = await ScalarAsync(connection, null, "PRAGMA page_size;", cancellationToken);
        var required = checked(pageCount * pageSize * 4 + 64L * 1024 * 1024);
        var free = availableSpace?.Invoke(fullPath)
            ?? JobStorageSpace.AvailableForDatabase(fullPath);
        if (free < required)
            throw new IOException($"作业库迁移空间不足：至少需要 {required} 字节可用空间。");

        var backupPath = fullPath + ".before-" + (toSplit ? "split-" : "legacy-") + Guid.NewGuid().ToString("N") + ".db";
        var backupVerified = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // CreateNew 防止覆盖已存在的恢复证据；失败备份保留，不当作可恢复成功结果。
            using (new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            checkpoint?.Invoke("backup-created", connection);
            cancellationToken.ThrowIfCancellationRequested();
            var backupBuilder = new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false };
            await using (var backup = new SqliteConnection(backupBuilder.ToString()))
            {
                await backup.OpenAsync(cancellationToken);
                connection.BackupDatabase(backup);
                await ValidateIntegrityAsync(backup, null, split, cancellationToken);
            }
            backupVerified = true;

            checkpoint?.Invoke("backed-up", connection);
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = connection.BeginTransaction(deferred: false);
            if (toSplit)
            {
                await ExecuteAsync(connection, transaction, """
                CREATE TABLE job_payloads (
                    item_id TEXT PRIMARY KEY NOT NULL REFERENCES job_items(id),
                    zpl TEXT NOT NULL,
                    command_bytes BLOB NULL
                );
                CREATE TABLE job_items_next (
                    id TEXT PRIMARY KEY,
                    job_id TEXT NOT NULL REFERENCES jobs(id),
                    item_index INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    error_code TEXT NULL,
                    error_message TEXT NULL,
                    updated_at TEXT NOT NULL
                );
                """, cancellationToken);
                var bytes = hasBytes ? "command_bytes" : "NULL";
                await ExecuteAsync(connection, transaction,
                    $"INSERT INTO job_payloads SELECT id, zpl, {bytes} FROM job_items; INSERT INTO job_items_next SELECT {ItemColumns} FROM job_items;", cancellationToken);
                checkpoint?.Invoke("copied", connection);
                await AssertSameAsync(connection, transaction,
                    $"SELECT {ItemColumns}, zpl, {bytes} FROM job_items",
                    $"SELECT {JoinedColumns} FROM job_items_next i LEFT JOIN job_payloads p ON p.item_id=i.id", cancellationToken);
            }
            else
            {
                await ExecuteAsync(connection, transaction, """
                CREATE TABLE job_items_next (
                    id TEXT PRIMARY KEY,
                    job_id TEXT NOT NULL REFERENCES jobs(id),
                    item_index INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    zpl TEXT NOT NULL,
                    command_bytes BLOB NULL,
                    error_code TEXT NULL,
                    error_message TEXT NULL,
                    updated_at TEXT NOT NULL
                );
                """, cancellationToken);
                await ExecuteAsync(connection, transaction,
                    $"INSERT INTO job_items_next ({ItemColumns}, zpl, command_bytes) SELECT {JoinedColumns} FROM job_items i LEFT JOIN job_payloads p ON p.item_id=i.id;", cancellationToken);
                checkpoint?.Invoke("copied", connection);
                await AssertSameAsync(connection, transaction,
                    $"SELECT {JoinedColumns} FROM job_items i LEFT JOIN job_payloads p ON p.item_id=i.id",
                    $"SELECT {ItemColumns}, zpl, command_bytes FROM job_items_next", cancellationToken);
                await ExecuteAsync(connection, transaction, "DROP TABLE job_payloads;", cancellationToken);
            }

            await ExecuteAsync(connection, transaction,
                "DROP TABLE job_items; ALTER TABLE job_items_next RENAME TO job_items;" + IndexSql
                + (toSplit ? "PRAGMA user_version=1;" : "PRAGMA user_version=0;"), cancellationToken);
            await ValidateIntegrityAsync(connection, transaction, toSplit, cancellationToken);
            checkpoint?.Invoke("before-commit", connection);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);
            return backupPath;
        }
        catch (Exception exception)
        {
            exception.Data["JobStorageBackupPath"] = backupPath;
            exception.Data["JobStorageBackupVerified"] = backupVerified;
            throw;
        }
    }

    /// <summary>有界申请独占维护连接并释放失败连接。</summary>
    private static async Task<SqliteConnection> OpenExclusiveAsync(string connectionString,
        Action<string, SqliteConnection>? checkpoint, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var connection = new SqliteConnection(connectionString);
            try
            {
                await connection.OpenAsync(token);
                await ExecuteAsync(connection, null, "PRAGMA locking_mode=EXCLUSIVE;", token);
                checkpoint?.Invoke("locking", connection);
                // WAL 的独占锁在事务提交后仍由本连接持有，直至关闭。
                await using var gate = connection.BeginTransaction(deferred: false);
                await ExecuteAsync(connection, gate, "SELECT count(*) FROM sqlite_schema;", token);
                await gate.CommitAsync(token);
                return connection;
            }
            catch (SqliteException error) when (error.SqliteErrorCode == 5 && attempt < 2)
            {
                // 失败连接可能仍持有底层读锁，必须关闭后才能退避重开。
                await connection.DisposeAsync();
                token.ThrowIfCancellationRequested();
                checkpoint?.Invoke("lock-retry", connection);
                await Task.Delay(Random.Shared.Next(50, 151), token);
            }
            catch
            {
                await connection.DisposeAsync();
                token.ThrowIfCancellationRequested();
                throw;
            }
        }
    }

    private static async Task<bool> ValidateSchemaAsync(SqliteConnection connection, bool split, CancellationToken token,
        SqliteTransaction? transaction = null)
    {
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["jobs"] = ["id", "request_id", "status", "created_at", "updated_at"],
            ["job_items"] = split
                ? ["id", "job_id", "item_index", "status", "error_code", "error_message", "updated_at"]
                : ["id", "job_id", "item_index", "status", "zpl", "error_code", "error_message", "updated_at"],
        };
        if (split) expected.Add("job_payloads", ["item_id", "zpl", "command_bytes"]);
        var requiredObjects = expected.Keys.Concat(["ux_job_items_job_index", "ix_job_items_job_id"]).ToHashSet(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT type, name, tbl_name, sql FROM sqlite_schema WHERE name NOT GLOB 'sqlite_*';";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var type = reader.GetString(0);
                var name = reader.GetString(1);
                if ((type == "table" && expected.ContainsKey(name))
                    || (type == "index" && reader.GetString(2) == "job_items"
                        && name is "ux_job_items_job_index" or "ix_job_items_job_id"))
                {
                    ValidateDefinition(name, reader.GetString(3), split);
                    requiredObjects.Remove(name);
                    continue;
                }
                throw new InvalidDataException($"作业库包含未识别结构：{type} {name}，拒绝迁移。");
            }
        }
        if (requiredObjects.Count != 0)
            throw new InvalidDataException("作业库缺少必需的表或索引，拒绝迁移。");

        var hasBytes = false;
        foreach (var (table, columns) in expected)
        {
            var actual = new HashSet<string>(StringComparer.Ordinal);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) actual.Add(reader.GetString(1));
            if (!split && table == "job_items") hasBytes = actual.Remove("command_bytes");
            if (!actual.SetEquals(columns)) throw new InvalidDataException($"作业库表结构不匹配：{table}。");
        }
        return hasBytes;
    }

    private static void ValidateDefinition(string name, string sql, bool split)
    {
        var definitions = name switch
        {
            "jobs" => new[] { "CREATE TABLE jobs (id TEXT PRIMARY KEY, request_id TEXT NOT NULL UNIQUE, status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL)" },
            "job_items" when split => ["CREATE TABLE job_items (id TEXT PRIMARY KEY, job_id TEXT NOT NULL REFERENCES jobs(id), item_index INTEGER NOT NULL, status TEXT NOT NULL, error_code TEXT NULL, error_message TEXT NULL, updated_at TEXT NOT NULL)"],
            "job_items" => [
                "CREATE TABLE job_items (id TEXT PRIMARY KEY, job_id TEXT NOT NULL REFERENCES jobs(id), item_index INTEGER NOT NULL, status TEXT NOT NULL, zpl TEXT NOT NULL, error_code TEXT NULL, error_message TEXT NULL, updated_at TEXT NOT NULL)",
                "CREATE TABLE job_items (id TEXT PRIMARY KEY, job_id TEXT NOT NULL REFERENCES jobs(id), item_index INTEGER NOT NULL, status TEXT NOT NULL, zpl TEXT NOT NULL, command_bytes BLOB NULL, error_code TEXT NULL, error_message TEXT NULL, updated_at TEXT NOT NULL)",
                "CREATE TABLE job_items (id TEXT PRIMARY KEY, job_id TEXT NOT NULL REFERENCES jobs(id), item_index INTEGER NOT NULL, status TEXT NOT NULL, zpl TEXT NOT NULL, error_code TEXT NULL, error_message TEXT NULL, updated_at TEXT NOT NULL, command_bytes BLOB NULL)"],
            "job_payloads" => ["CREATE TABLE job_payloads (item_id TEXT PRIMARY KEY NOT NULL REFERENCES job_items(id), zpl TEXT NOT NULL, command_bytes BLOB NULL)"],
            "ux_job_items_job_index" => ["CREATE UNIQUE INDEX ux_job_items_job_index ON job_items(job_id, item_index)"],
            "ix_job_items_job_id" => ["CREATE INDEX ix_job_items_job_id ON job_items(job_id)"],
            _ => [],
        };
        var actual = NormalizeDefinition(sql);
        if (!definitions.Any(definition => NormalizeDefinition(definition) == actual))
            throw new InvalidDataException($"作业库定义不匹配：{name}，拒绝迁移。");
    }

    private static string NormalizeDefinition(string sql)
    {
        // SQLite 重命名表后会给表名加双引号；不泛化剥离表达式或列名引号。
        return string.Concat(sql.Replace("\"job_items\"", "job_items", StringComparison.Ordinal)
            .Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
    }

    private static async Task ValidateIntegrityAsync(SqliteConnection connection, SqliteTransaction? transaction, bool split, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(await command.ExecuteScalarAsync(token) as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("作业库完整性检查失败。");
        command.CommandText = "PRAGMA foreign_key_check;";
        if (await command.ExecuteScalarAsync(token) is not null)
            throw new InvalidDataException("作业库外键检查失败。");
        await ValidateRelationsAsync(connection, transaction, split, token);
    }

    private static async Task ValidateRelationsAsync(SqliteConnection connection, SqliteTransaction? transaction, bool split, CancellationToken token)
    {
        var invalid = """
            SELECT EXISTS(SELECT 1 FROM job_items i LEFT JOIN jobs j ON i.job_id=j.id
                WHERE j.id IS NULL OR i.id IS NULL)
            OR EXISTS(SELECT 1 FROM jobs WHERE id IS NULL)
            OR EXISTS(SELECT 1 FROM job_items GROUP BY job_id, item_index HAVING count(*) <> 1)
            """;
        if (split) invalid += """

            OR EXISTS(SELECT 1 FROM job_items i LEFT JOIN job_payloads p ON p.item_id=i.id WHERE p.item_id IS NULL)
            OR EXISTS(SELECT 1 FROM job_payloads p LEFT JOIN job_items i ON p.item_id=i.id WHERE i.id IS NULL)
            """;
        if (await ScalarAsync(connection, transaction, invalid, token) != 0)
            throw new InvalidDataException("作业库状态、作业或载荷对应关系不完整。");
    }

    private static async Task AssertSameAsync(SqliteConnection connection, SqliteTransaction transaction, string before, string after, CancellationToken token)
    {
        var sql = $"SELECT EXISTS(SELECT * FROM ({before} EXCEPT {after})) OR EXISTS(SELECT * FROM ({after} EXCEPT {before}));";
        if (await ScalarAsync(connection, transaction, sql, token) != 0)
            throw new InvalidDataException("迁移前后作业字段不一致，拒绝提交。");
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }
}
