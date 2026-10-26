using LabelFrame.Core.Data;
using Microsoft.Data.Sqlite;

using LabelFrame.Core.Jobs;

namespace LabelFrame.Core.Tests.Jobs;

internal sealed partial class LegacySqliteLabelJobStore
{
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
            SELECT DISTINCT j.status, i.status FROM jobs j
            LEFT JOIN job_items i ON i.job_id = j.id WHERE j.id = $jobId;
            """;
        command.Parameters.AddWithValue("$jobId", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (false, null);
        var status = Enum.Parse<LabelJobStatus>(reader.GetString(0));
        var items = new List<LabelJobItemStatus>();
        do
        {
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
                i.id, i.item_index, i.status, i.error_code, i.error_message
            FROM jobs j LEFT JOIN job_items i ON i.job_id = j.id
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
                items.Add(new LabelJobItemState(reader.GetString(5), reader.GetInt32(6),
                    Enum.Parse<LabelJobItemStatus>(reader.GetString(7)),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            }
        } while (await reader.ReadAsync(cancellationToken));
        return state;
    }
}
