using LabelFrame.Core.Data;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Server;

/// <summary>devices 聚合的数据访问：设备目录的注册、心跳与查询。</summary>
internal sealed class ServerDeviceStore(string connectionString) : ServerDbStore(connectionString)
{
    /// <summary>建表并执行旧库兼容迁移。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS devices (
                id            TEXT PRIMARY KEY,
                name          TEXT NOT NULL,
                registered_at TEXT NOT NULL,
                last_seen_at  TEXT NOT NULL,
                last_ip       TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await MigrateLastIpAsync(connection, cancellationToken);
    }

    /// <summary>旧库兼容迁移：devices 表缺少 last_ip 列时补列（已存在则跳过；失败静默忽略，不影响启动）。</summary>
    private static async Task MigrateLastIpAsync(SqliteConnection connection, CancellationToken cancellationToken)
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

    /// <summary>注册 / 更新设备并刷新心跳。</summary>
    public async Task<Device> UpsertAsync(Device device, CancellationToken cancellationToken = default)
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
    public async Task<int> TouchAsync(string deviceId, DateTimeOffset now, string? lastIp, CancellationToken cancellationToken = default)
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
    public async Task<Device?> GetAsync(string deviceId, CancellationToken cancellationToken = default)
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
    public async Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
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

    /// <summary>按 last_ip 精确查找设备（忽略大小写；未找到 null）。同 IP 多行取最近活跃（last_seen_at DESC）——设备号变更后旧行 stale 必命中新行；同 IP 多设备都活跃（NAT 共网出口）时跟随最近心跳。</summary>
    public async Task<Device?> FindByIpAsync(string ip, CancellationToken cancellationToken = default)
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
}
