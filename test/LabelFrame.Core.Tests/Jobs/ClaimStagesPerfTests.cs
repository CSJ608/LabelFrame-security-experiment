using System.Diagnostics;
using System.Text.Json;
using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace LabelFrame.Core.Tests.Jobs;

public class LegacyClaimStagesPerfTests(ITestOutputHelper output)
{
    private const string CandidatesSql = """
        SELECT id, created_at FROM jobs
        WHERE status IN ('Pending', 'Printing')
          AND EXISTS (SELECT 1 FROM job_items WHERE job_id = jobs.id AND status = 'Pending');
        """;
    private const string PayloadSql = """
        SELECT id, item_index, zpl, command_bytes FROM job_items
        WHERE job_id = $jobId AND status = 'Pending' ORDER BY item_index LIMIT 1;
        """;
    private const string SplitPayloadSql = """
        SELECT i.id, i.item_index, p.zpl, p.command_bytes FROM job_items i
        JOIN job_payloads p ON p.id = i.id
        WHERE i.job_id = $jobId AND i.status = 'Pending' ORDER BY i.item_index LIMIT 1;
        """;

    [Fact]
    public async Task Measure_claim_transaction_stages_and_query_plans()
    {
        for (var round = 1; round <= 4; round++)
        {
            foreach (var payloadLength in round % 2 == 0 ? new[] { 210000, 64 } : new[] { 64, 210000 })
            {
                await Measure(round, payloadLength);
            }
        }

    }

    [Fact]
    public async Task Compare_split_payload_storage_with_original()
    {
        foreach (var binary in new[] { false, true })
            for (var round = 1; round <= 4; round++)
            {
                foreach (var split in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
                {
                    await Measure(round, binary ? 105000 : 210000, split, binary);
                }
            }
    }

    [Fact]
    public async Task Split_prototype_roundtrip_preserves_data_and_failed_migration_rolls_back()
    {
        using var db = new LegacyJobDb();
        var (job, _) = await db.Queue.SubmitAsync("roundtrip", new[]
        {
            LabelJobPayload.OfText("^XA中文^XZ"), LabelJobPayload.OfBytes([0, 1, 128, 255]),
            LabelJobPayload.OfText("pending"), LabelJobPayload.OfText("cancelled"), LabelJobPayload.OfText("printing"),
        });
        await using (var connection = await SqliteSupport.OpenAsync(SqliteSupport.BuildConnectionString(db.Path)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE job_items SET status = CASE item_index WHEN 0 THEN 'Completed' WHEN 1 THEN 'Failed'
                    WHEN 2 THEN 'Pending' WHEN 3 THEN 'Cancelled' ELSE 'Printing' END,
                    error_code = 'LF_TEST', error_message = '保留错误', updated_at = '2026-10-20T01:02:03.0000000+00:00';
                UPDATE jobs SET status = 'Suspended';
                """;
            await command.ExecuteNonQueryAsync();
        }
        var before = JsonSerializer.Serialize(await db.Queue.GetAsync(job.Id));
        var beforeRows = await ReadStoredRows(db.Path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SplitPayloads(db.Path, failBeforeCommit: true));
        Assert.Equal(before, JsonSerializer.Serialize(await db.Queue.GetAsync(job.Id)));
        Assert.Equal(beforeRows, await ReadStoredRows(db.Path));
        await SplitPayloads(db.Path);
        await Assert.ThrowsAsync<SqliteException>(() => db.Queue.GetAsync(job.Id));
        await RestorePayloads(db.Path);
        Assert.Equal(before, JsonSerializer.Serialize(await db.Queue.GetAsync(job.Id)));
        Assert.Equal(beforeRows, await ReadStoredRows(db.Path));

        static async Task<string> ReadStoredRows(string path)
        {
            await using var connection = await SqliteSupport.OpenAsync(SqliteSupport.BuildConnectionString(path));
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, job_id, item_index, status, zpl, hex(command_bytes), command_bytes IS NULL, error_code, error_message, updated_at FROM job_items ORDER BY item_index;";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<object[]>();
            while (await reader.ReadAsync())
            {
                var row = new object[reader.FieldCount];
                reader.GetValues(row);
                rows.Add(row);
            }
            return JsonSerializer.Serialize(rows);
        }
    }

    [Fact]
    public async Task Compatibility_view_does_not_preserve_existing_update_result()
    {
        using var db = new LegacyJobDb();
        var (job, _) = await db.Queue.SubmitAsync("view-compatibility", new[] { "payload" });
        await SplitPayloads(db.Path);
        await using (var connection = await SqliteSupport.OpenAsync(SqliteSupport.BuildConnectionString(db.Path)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE job_items RENAME TO job_item_states;
                CREATE VIEW job_items AS SELECT i.id, i.job_id, i.item_index, i.status,
                    p.zpl, p.command_bytes, i.error_code, i.error_message, i.updated_at
                    FROM job_item_states i JOIN job_payloads p ON p.id = i.id;
                CREATE TRIGGER compatibility_item_update INSTEAD OF UPDATE ON job_items BEGIN
                    UPDATE job_item_states SET status = NEW.status, error_code = NEW.error_code,
                        error_message = NEW.error_message, updated_at = NEW.updated_at WHERE id = OLD.id;
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var initialization = await Assert.ThrowsAsync<SqliteException>(() => db.Store.InitializeAsync());
        output.WriteLine($"兼容视图初始化失败：{initialization.Message}");
        var result = await db.Store.SetItemStatusAsync(job.Id, job.Items[0].Id, LabelJobItemStatus.Completed, null, null);
        Assert.Null(result);
        var stored = (await db.Queue.GetAsync(job.Id))!;
        Assert.Equal(LabelJobItemStatus.Completed, stored.Items[0].Status);
        Assert.Equal("payload", stored.Items[0].Zpl);
        output.WriteLine("兼容视图可读且更新已落库，但既有 SetItemStatusAsync 返回 null，不能当作透明兼容层。");
    }

    private async Task Measure(int round, int payloadLength, bool split = false, bool binary = false)
    {
        using var db = new LegacyJobDb();
        var payload = binary ? string.Empty : new string('A', payloadLength);
        var bytes = binary ? Enumerable.Range(0, payloadLength).Select(i => (byte)i).ToArray() : null;
        var (job, _) = await db.Queue.SubmitAsync("claim-stages", Enumerable.Repeat(new LabelJobPayload(payload, bytes), 200).ToArray());
        if (split) await SplitPayloads(db.Path);
        var payloadSql = split ? SplitPayloadSql : PayloadSql;
        var stages = new Dictionary<string, long>();
        for (var index = 0; index < 200; index++)
        {
            var start = Stopwatch.GetTimestamp();
            await using (var connection = await SqliteSupport.OpenAsync(SqliteSupport.BuildConnectionString(db.Path)))
            {
                Add("open", start);
                if (round == 1 && index == 0)
                {
                    foreach (var sql in new[] { CandidatesSql, payloadSql })
                    {
                        await using var explain = connection.CreateCommand();
                        explain.CommandText = "EXPLAIN QUERY PLAN " + sql;
                        if (sql == payloadSql) explain.Parameters.AddWithValue("$jobId", job.Id);
                        await using var plan = await explain.ExecuteReaderAsync();
                        while (await plan.ReadAsync()) output.WriteLine($"plan: {plan.GetString(3)}");
                    }
                }
                start = Stopwatch.GetTimestamp();
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
                Add("begin", start);
                start = Stopwatch.GetTimestamp();
                var candidates = new List<(string Id, DateTimeOffset CreatedAt)>();
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = CandidatesSql;
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) candidates.Add((reader.GetString(0), SqliteSupport.Parse(reader.GetString(1))));
                }
                var jobId = candidates.OrderBy(j => j.CreatedAt).ThenBy(j => j.Id).Select(j => j.Id).FirstOrDefault();
                Add("candidates", start);
                Assert.Equal(job.Id, jobId);
                start = Stopwatch.GetTimestamp();
                LabelJobItem item;
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = payloadSql;
                    command.Parameters.AddWithValue("$jobId", jobId);
                    await using var reader = await command.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());
                    item = new LabelJobItem
                    {
                        Id = reader.GetString(0),
                        JobId = jobId!,
                        Index = reader.GetInt32(1),
                        Zpl = reader.GetString(2),
                        CommandBytes = reader.IsDBNull(3) ? null : (byte[])reader[3],
                        Status = LabelJobItemStatus.Printing,
                    };
                }
                Add("payload", start);
                Assert.Equal(index, item.Index);
                Assert.Equal(payload, item.Zpl);
                Assert.Equal(bytes, item.CommandBytes);
                start = Stopwatch.GetTimestamp();
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                            UPDATE job_items SET status = 'Printing', error_code = NULL, error_message = NULL,
                                updated_at = $updatedAt WHERE id = $itemId AND job_id = $jobId;
                            UPDATE jobs SET status = 'Printing', updated_at = $updatedAt WHERE id = $jobId;
                            """;
                    command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(DateTimeOffset.UtcNow));
                    command.Parameters.AddWithValue("$itemId", item.Id);
                    command.Parameters.AddWithValue("$jobId", jobId);
                    Assert.Equal(2, await command.ExecuteNonQueryAsync());
                }
                Add("updates", start);
                start = Stopwatch.GetTimestamp();
                await transaction.CommitAsync();
                Add("commit", start);
            }
            start = Stopwatch.GetTimestamp();
            await db.Store.CompleteItemWithoutResultAsync(job.Id, job.Items[index].Id, default);
            Add("complete", start);
        }
        Assert.Null(await db.Queue.ClaimNextItemAsync());
        var final = (await db.Store.GetStateAsync(job.Id, default))!;
        Assert.Equal(LabelJobStatus.Completed, final.Status);
        Assert.All(final.Items, item =>
        {
            Assert.Equal(LabelJobItemStatus.Completed, item.Status);
        });
        await using (var verify = await SqliteSupport.OpenAsync(SqliteSupport.BuildConnectionString(db.Path)))
        {
            await using var command = verify.CreateCommand();
            command.CommandText = split
                ? "SELECT i.id, p.zpl, p.command_bytes FROM job_items i JOIN job_payloads p ON p.id = i.id ORDER BY i.item_index;"
                : "SELECT id, zpl, command_bytes FROM job_items ORDER BY item_index;";
            await using var reader = await command.ExecuteReaderAsync();
            var count = 0;
            while (await reader.ReadAsync())
            {
                Assert.Equal(job.Items[count++].Id, reader.GetString(0));
                Assert.Equal(payload, reader.GetString(1));
                Assert.Equal(bytes, reader.IsDBNull(2) ? null : (byte[])reader[2]);
            }
            Assert.Equal(200, count);
        }
        output.WriteLine($"schema=legacy-or-historical-prototype; round={round}; count=200; split={split}; binary={binary}; payloadLength={payloadLength}; " + string.Join("; ", stages.Select(s => $"{s.Key}Ms={s.Value * 1000d / Stopwatch.Frequency:F2}")));
        if (split)
        {
            await RestorePayloads(db.Path);
            var restored = (await db.Queue.GetAsync(job.Id))!;
            Assert.Equal(LabelJobStatus.Completed, restored.Status);
            Assert.All(restored.Items, item =>
            {
                Assert.Equal(LabelJobItemStatus.Completed, item.Status);
                Assert.Equal(payload, item.Zpl);
                Assert.Equal(bytes, item.CommandBytes);
            });
        }

        void Add(string name, long start)
            => stages[name] = stages.GetValueOrDefault(name) + Stopwatch.GetTimestamp() - start;
    }

    private async Task SplitPayloads(string path, bool failBeforeCommit = false)
    {
        var start = Stopwatch.GetTimestamp();
        await using var connection = await SqliteSupport.OpenAsync(SqliteSupport.BuildConnectionString(path));
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE job_payloads (id TEXT PRIMARY KEY, zpl TEXT NOT NULL, command_bytes BLOB NULL);
            INSERT INTO job_payloads SELECT id, zpl, command_bytes FROM job_items;
            CREATE TABLE slim_items (
                id TEXT PRIMARY KEY, job_id TEXT NOT NULL REFERENCES jobs(id), item_index INTEGER NOT NULL,
                status TEXT NOT NULL, error_code TEXT NULL, error_message TEXT NULL, updated_at TEXT NOT NULL);
            INSERT INTO slim_items SELECT id, job_id, item_index, status, error_code, error_message, updated_at FROM job_items;
            DROP TABLE job_items;
            ALTER TABLE slim_items RENAME TO job_items;
            CREATE UNIQUE INDEX ux_job_items_job_index ON job_items(job_id, item_index);
            CREATE INDEX ix_job_items_job_id ON job_items(job_id);
            """;
        await command.ExecuteNonQueryAsync();
        if (failBeforeCommit) throw new InvalidOperationException("模拟迁移提交前失败");
        await transaction.CommitAsync();
        output.WriteLine($"prototype-migrationMs={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F2}");
    }

    private static async Task RestorePayloads(string path)
    {
        await using var connection = await SqliteSupport.OpenAsync(SqliteSupport.BuildConnectionString(path));
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        await using (var validate = connection.CreateCommand())
        {
            validate.Transaction = transaction;
            validate.CommandText = """
                SELECT (SELECT COUNT(*) FROM job_items i LEFT JOIN job_payloads p ON p.id = i.id WHERE p.id IS NULL)
                     + (SELECT COUNT(*) FROM job_payloads p LEFT JOIN job_items i ON i.id = p.id WHERE i.id IS NULL);
                """;
            if ((long)(await validate.ExecuteScalarAsync())! != 0)
                throw new InvalidOperationException("标签状态与载荷不一一对应，拒绝反向迁移");
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE restored_items (
                id TEXT PRIMARY KEY, job_id TEXT NOT NULL REFERENCES jobs(id), item_index INTEGER NOT NULL,
                status TEXT NOT NULL, zpl TEXT NOT NULL, command_bytes BLOB NULL,
                error_code TEXT NULL, error_message TEXT NULL, updated_at TEXT NOT NULL);
            INSERT INTO restored_items
                SELECT i.id, i.job_id, i.item_index, i.status, p.zpl, p.command_bytes,
                    i.error_code, i.error_message, i.updated_at FROM job_items i JOIN job_payloads p ON p.id = i.id;
            DROP TABLE job_items;
            ALTER TABLE restored_items RENAME TO job_items;
            CREATE UNIQUE INDEX ux_job_items_job_index ON job_items(job_id, item_index);
            CREATE INDEX ix_job_items_job_id ON job_items(job_id);
            DROP TABLE job_payloads;
            """;
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
