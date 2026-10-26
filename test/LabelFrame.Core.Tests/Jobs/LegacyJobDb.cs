using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;

namespace LabelFrame.Core.Tests.Jobs;

/// <summary>提供旧版大行存储的隔离回归库。</summary>
internal sealed class LegacyJobDb : IDisposable
{
    public LegacyJobDb()
    {
        DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "labelframe-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        Path = System.IO.Path.Combine(DirectoryPath, "jobs.db");
        Store = new LegacySqliteLabelJobStore(Path);
        Store.InitializeAsync().GetAwaiter().GetResult();
        Queue = new LabelJobQueue(Store);
    }

    public string DirectoryPath { get; }
    public string Path { get; }
    public LegacySqliteLabelJobStore Store { get; }
    public LabelJobQueue Queue { get; }

    public void ReleasePool()
    {
        using var connection = new SqliteConnection(SqliteSupport.BuildConnectionString(Path));
        SqliteConnection.ClearPool(connection);
    }

    public void Dispose()
    {
        ReleasePool();
        Directory.Delete(DirectoryPath, recursive: true);
    }
}
