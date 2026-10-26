using LabelFrame.Core.Data;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Jobs;

public sealed partial class SqliteLabelJobStore
{
    internal async Task<IReadOnlyList<LabelJobState>> ListRecentStatesAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var jobs = new List<LabelJobState>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM jobs ORDER BY created_at DESC, id LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var job = await LoadStateAsync(connection, null, reader.GetString(0), cancellationToken);
            if (job is not null) jobs.Add(job);
        }
        return jobs;
    }

    internal async Task<LabelJobState?> GetStateAsync(string jobId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await LoadStateAsync(connection, null, jobId, cancellationToken);
    }

    internal async Task<bool> CompleteItemWithoutResultAsync(string jobId, string itemId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE job_items SET status = 'Completed', error_code = NULL, error_message = NULL,
                    updated_at = $updatedAt WHERE job_id = $jobId AND id = $itemId;
                """;
            command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(DateTimeOffset.UtcNow));
            command.Parameters.AddWithValue("$jobId", jobId);
            command.Parameters.AddWithValue("$itemId", itemId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) return false;
        }

        var (exists, nextStatus) = await ReadCompletionStatusAsync(connection, transaction, jobId, cancellationToken);
        if (!exists) return false;
        if (nextStatus is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE jobs SET status = $status, updated_at = $updatedAt WHERE id = $jobId;";
            command.Parameters.AddWithValue("$status", nextStatus.Value.ToString());
            command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(DateTimeOffset.UtcNow));
            command.Parameters.AddWithValue("$jobId", jobId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>读取作业与去重后的标签状态，计算单张完成后的作业状态。</summary>
    private static async Task<(bool Exists, LabelJobStatus? NextStatus)> ReadCompletionStatusAsync(
        SqliteConnection connection, SqliteTransaction transaction, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT DISTINCT j.status, i.status, i.id IS NOT NULL AND p.item_id IS NULL FROM jobs j
            LEFT JOIN job_items i ON i.job_id = j.id
            LEFT JOIN job_payloads p ON p.item_id = i.id WHERE j.id = $jobId;
            """;
        command.Parameters.AddWithValue("$jobId", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (false, null);
        var status = Enum.Parse<LabelJobStatus>(reader.GetString(0));
        var items = new List<LabelJobItemStatus>();
        do
        {
            if (reader.GetBoolean(2)) throw new InvalidDataException("作业项缺少指令载荷。");
            if (!reader.IsDBNull(1)) items.Add(Enum.Parse<LabelJobItemStatus>(reader.GetString(1)));
        } while (await reader.ReadAsync(cancellationToken));
        return (true, LabelJobQueue.GetCompletionStatus(status, items));
    }

    private static async Task<LabelJobState?> LoadStateAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT j.id, j.request_id, j.status, j.created_at, j.updated_at,
                i.id, i.item_index, i.status, i.error_code, i.error_message, p.item_id
            FROM jobs j LEFT JOIN job_items i ON i.job_id = j.id
            LEFT JOIN job_payloads p ON p.item_id = i.id
            WHERE j.id = $jobId ORDER BY i.item_index;
            """;
        command.Parameters.AddWithValue("$jobId", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var items = new List<LabelJobItemState>();
        var state = new LabelJobState(reader.GetString(0), reader.GetString(1),
            Enum.Parse<LabelJobStatus>(reader.GetString(2)), SqliteSupport.Parse(reader.GetString(3)),
            SqliteSupport.Parse(reader.GetString(4)), items);
        do
        {
            if (!reader.IsDBNull(5))
            {
                if (reader.IsDBNull(10)) throw new InvalidDataException("作业项缺少指令载荷。");
                items.Add(new LabelJobItemState(reader.GetString(5), reader.GetInt32(6),
                    Enum.Parse<LabelJobItemStatus>(reader.GetString(7)),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            }
        } while (await reader.ReadAsync(cancellationToken));
        return state;
    }
}
