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
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // testData 为读-改-写（不是整体替换）：现有值为基底，元素预览值派生覆盖优先——不传不覆盖既有值
        var testData = await ReadTestDataAsync(connection, (Microsoft.Data.Sqlite.SqliteTransaction)transaction, package.Name, cancellationToken);
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
                INSERT INTO templates (id, group_name, contract_json, layout_json, test_data_json, created_at, updated_at)
                VALUES ($id, $group, $contractJson, $layoutJson, $testDataJson, $now, $now)
                ON CONFLICT(id) DO UPDATE SET
                    group_name = excluded.group_name,
                    contract_json = excluded.contract_json,
                    layout_json = excluded.layout_json,
                    test_data_json = excluded.test_data_json,
                    updated_at = excluded.updated_at;
                DELETE FROM template_images WHERE template_id = $id;
                """;
            command.Parameters.AddWithValue("$id", package.Name);
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
            command.Parameters.AddWithValue("$id", package.Name);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$bytes", bytes);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>读取库中现有 testData（读-改-写合并的基底）。</summary>
    private static async Task<Dictionary<string, string>> ReadTestDataAsync(
        SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT test_data_json FROM templates WHERE id = $id;";
        command.Parameters.AddWithValue("$id", name);
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
    /// <summary>按名称查询模板（含图片资源与 testData）。</summary>
    public async Task<TemplatePackage?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, group_name, contract_json, layout_json, test_data_json
            FROM templates WHERE id = $id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var templateName = reader.GetString(0);
        var group = reader.GetString(1);
        var contract = JsonSerializer.Deserialize<LabelContract>(reader.GetString(2), JsonOptions)!;
        var layout = JsonSerializer.Deserialize<LabelLayout>(reader.GetString(3), JsonOptions)!;
        var testData = reader.IsDBNull(4)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(4), JsonOptions)
              ?? new Dictionary<string, string>();

        var images = new Dictionary<string, byte[]>();
        await using (var imageCommand = connection.CreateCommand())
        {
            imageCommand.CommandText = "SELECT image_key, bytes FROM template_images WHERE template_id = $id;";
            imageCommand.Parameters.AddWithValue("$id", templateName);
            await using var imageReader = await imageCommand.ExecuteReaderAsync(cancellationToken);
            while (await imageReader.ReadAsync(cancellationToken))
            {
                images[imageReader.GetString(0)] = (byte[])imageReader.GetValue(1);
            }
        }

        return new TemplatePackage
        {
            Name = templateName,
            Group = group,
            Contract = contract,
            Layout = layout,
            Images = images,
            TestData = testData,
        };
    }

    /// <summary>删除模板。</summary>
    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // 先删图片（外键约束），再删模板
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM template_images WHERE template_id = $id;";
            command.Parameters.AddWithValue("$id", name);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM templates WHERE id = $id;";
            command.Parameters.AddWithValue("$id", name);
            await command.ExecuteNonQueryAsync(cancellationToken);
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
            ? "SELECT id, group_name, updated_at FROM templates ORDER BY group_name, id;"
            : "SELECT id, group_name, updated_at FROM templates WHERE group_name = $group ORDER BY id;";
        if (!string.IsNullOrWhiteSpace(group))
        {
            command.Parameters.AddWithValue("$group", group);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new TemplateSummary(reader.GetString(0), reader.GetString(1), SqliteSupport.Parse(reader.GetString(2))));
        }

        return summaries;
    }

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        => SqliteSupport.OpenAsync(_connectionString, cancellationToken);
}

/// <summary>模板列表摘要。</summary>
public sealed record TemplateSummary(string Name, string Group, DateTimeOffset UpdatedAt);