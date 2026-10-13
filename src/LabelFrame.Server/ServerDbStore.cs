using LabelFrame.Core.Data;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Server;

/// <summary>Server 各聚合存储的基类：共享同一数据库连接串与开连接方式。</summary>
internal abstract class ServerDbStore
{
    private readonly string _connectionString;

    protected ServerDbStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        => SqliteSupport.OpenAsync(_connectionString, cancellationToken);
}
