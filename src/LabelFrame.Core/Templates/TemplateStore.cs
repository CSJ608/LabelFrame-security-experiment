using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Data;
using LabelFrame.Core.Layout;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Templates;

/// <summary>SQLite 模板存储：模板 CRUD + 图片资源；按分组列表。</summary>
public sealed class TemplateStore
{
    private const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS templates (
            id            TEXT PRIMARY KEY,
            name          TEXT NOT NULL DEFAULT '',
            group_name    TEXT NOT NULL,
            contract_json TEXT NOT NULL,
            layout_json   TEXT NOT NULL,
            test_data_json TEXT NOT NULL DEFAULT '{}',
            created_at    TEXT NOT NULL,
            updated_at    TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS template_images (
            template_id TEXT NOT NULL REFERENCES templates(id),
            image_key   TEXT NOT NULL,
            bytes       BLOB NOT NULL,
            PRIMARY KEY (template_id, image_key)
        );

        CREATE INDEX IF NOT EXISTS ix_templates_group ON templates(group_name);
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new LabelElementJsonConverter(),
        },
    };

    private readonly string _connectionString;

    /// <summary>创建模板存储。</summary>
    public TemplateStore(string databasePath)
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

        // 旧库迁移：补充 test_data_json 列（已存在时忽略）
        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE templates ADD COLUMN test_data_json TEXT NOT NULL DEFAULT '{}';";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            // 列已存在，忽略
        }

        // 旧库迁移：补充 name 列（已存在时忽略）；存量行 id 即旧模板名，回填 name 后 id 从此只充当不透明 uid
        try
        {
            await using var alterName = connection.CreateCommand();
            alterName.CommandText = "ALTER TABLE templates ADD COLUMN name TEXT NOT NULL DEFAULT '';";
            await alterName.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            // 列已存在，忽略
        }

        await using (var backfillName = connection.CreateCommand())
        {
            backfillName.CommandText = "UPDATE templates SET name = id WHERE name = '';";
            await backfillName.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createNameIndex = connection.CreateCommand())
        {
            createNameIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_templates_name ON templates(name);";
            await createNameIndex.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>保存（upsert）模板。</summary>
    public async Task SaveAsync(TemplatePackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(package.Name))
        {
            throw new ArgumentException("模板名不能为空。", nameof(package));
        }

        var now = DateTimeOffset.UtcNow;
        var targetId = string.IsNullOrWhiteSpace(package.Uid) ? Guid.NewGuid().ToString("N") : package.Uid;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // testData 为读-改-写（不是整体替换）：现有值为基底，元素预览值派生覆盖优先——不传不覆盖既有值。
        // 基底定位：带 uid＝本行（改名随行携带，被撞名行不并入）；无 uid＝同名现存行（即正被替换的行，等价旧按名覆盖）
        var testData = await ReadTestDataAsync(
            connection,
            (Microsoft.Data.Sqlite.SqliteTransaction)transaction,
            byName: string.IsNullOrWhiteSpace(package.Uid),
            string.IsNullOrWhiteSpace(package.Uid) ? package.Name : targetId,
            cancellationToken);

        // 撞名行（name 同名但非本 uid）连带图片删除；无 uid 保存时 targetId 必为新值，正被替换的旧行即在此删除
        await using (var deleteColliding = connection.CreateCommand())
        {
            deleteColliding.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            deleteColliding.CommandText = """
                DELETE FROM template_images WHERE template_id IN (SELECT id FROM templates WHERE name = $name AND id <> $id);
                DELETE FROM templates WHERE name = $name AND id <> $id;
                """;
            deleteColliding.Parameters.AddWithValue("$name", package.Name);
            deleteColliding.Parameters.AddWithValue("$id", targetId);
            await deleteColliding.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (key, value) in package.TestData)
        {
            testData[key] = value;
        }

        foreach (var element in package.Layout.Elements)
        {
            var key = GetSourceKey(element);
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(element.PreviewValue))
            {
                testData[key] = element.PreviewValue!;
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO templates (id, name, group_name, contract_json, layout_json, test_data_json, created_at, updated_at)
                VALUES ($id, $name, $group, $contractJson, $layoutJson, $testDataJson, $now, $now)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    group_name = excluded.group_name,
                    contract_json = excluded.contract_json,
                    layout_json = excluded.layout_json,
                    test_data_json = excluded.test_data_json,
                    updated_at = excluded.updated_at;
                DELETE FROM template_images WHERE template_id = $id;
                """;
            command.Parameters.AddWithValue("$id", targetId);
            command.Parameters.AddWithValue("$name", package.Name);
            command.Parameters.AddWithValue("$group", package.Group);
            command.Parameters.AddWithValue("$contractJson", JsonSerializer.Serialize(package.Contract, JsonOptions));
            command.Parameters.AddWithValue("$layoutJson", JsonSerializer.Serialize(package.Layout, JsonOptions));
            command.Parameters.AddWithValue("$testDataJson", JsonSerializer.Serialize(testData, JsonOptions));
            command.Parameters.AddWithValue("$now", SqliteSupport.Format(now));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (key, bytes) in package.Images)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO template_images (template_id, image_key, bytes)
                VALUES ($id, $key, $bytes);
                """;
            command.Parameters.AddWithValue("$id", targetId);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$bytes", bytes);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>读取库中现有 testData（读-改-写合并的基底；byName 按名定位无 uid 路径的被替换行，否则按行 uid 定位）。</summary>
    private static async Task<Dictionary<string, string>> ReadTestDataAsync(
        SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        bool byName,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = byName
            ? "SELECT test_data_json FROM templates WHERE name = $key LIMIT 1;"
            : "SELECT test_data_json FROM templates WHERE id = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not string json || string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static string? GetSourceKey(LabelElement element) => element switch
    {
        LabelTextElement t => t.SourceKey,
        LabelBarcodeElement b => b.SourceKey,
        LabelQrCodeElement q => q.SourceKey,
        _ => null,
    };
    /// <summary>按名称查询模板（含图片资源与 testData；返回包携带行 uid）。</summary>
    public async Task<TemplatePackage?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, group_name, contract_json, layout_json, test_data_json
            FROM templates WHERE name = $name LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var uid = reader.GetString(0);
        var templateName = reader.GetString(1);
        var group = reader.GetString(2);
        var contract = JsonSerializer.Deserialize<LabelContract>(reader.GetString(3), JsonOptions)!;
        var layout = JsonSerializer.Deserialize<LabelLayout>(reader.GetString(4), JsonOptions)!;
        var testData = reader.IsDBNull(5)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5), JsonOptions)
              ?? new Dictionary<string, string>();

        var images = new Dictionary<string, byte[]>();
        await using (var imageCommand = connection.CreateCommand())
        {
            imageCommand.CommandText = "SELECT image_key, bytes FROM template_images WHERE template_id = $id;";
            imageCommand.Parameters.AddWithValue("$id", uid);
            await using var imageReader = await imageCommand.ExecuteReaderAsync(cancellationToken);
            while (await imageReader.ReadAsync(cancellationToken))
            {
                images[imageReader.GetString(0)] = (byte[])imageReader.GetValue(1);
            }
        }

        return new TemplatePackage
        {
            Name = templateName,
            Uid = uid,
            Group = group,
            Contract = contract,
            Layout = layout,
            Images = images,
            TestData = testData,
        };
    }

    /// <summary>删除模板（按名解析行 uid 后连带图片删除）。</summary>
    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string? uid = null;
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            query.CommandText = "SELECT id FROM templates WHERE name = $name LIMIT 1;";
            query.Parameters.AddWithValue("$name", name);
            uid = await query.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (uid is not null)
        {
            // 先删图片（外键约束），再删模板
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
                command.CommandText = "DELETE FROM template_images WHERE template_id = $id;";
                command.Parameters.AddWithValue("$id", uid);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
                command.CommandText = "DELETE FROM templates WHERE id = $id;";
                command.Parameters.AddWithValue("$id", uid);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>模板列表（可选按分组过滤）。</summary>
    public async Task<IReadOnlyList<TemplateSummary>> ListAsync(string? group = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var summaries = new List<TemplateSummary>();
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(group)
            ? "SELECT id, name, group_name, updated_at FROM templates ORDER BY group_name, name;"
            : "SELECT id, name, group_name, updated_at FROM templates WHERE group_name = $group ORDER BY name;";
        if (!string.IsNullOrWhiteSpace(group))
        {
            command.Parameters.AddWithValue("$group", group);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new TemplateSummary(reader.GetString(1), reader.GetString(2), SqliteSupport.Parse(reader.GetString(3)), reader.GetString(0)));
        }

        return summaries;
    }

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        => SqliteSupport.OpenAsync(_connectionString, cancellationToken);
}

/// <summary>模板列表摘要。</summary>
public sealed record TemplateSummary(string Name, string Group, DateTimeOffset UpdatedAt, string? Uid = null);