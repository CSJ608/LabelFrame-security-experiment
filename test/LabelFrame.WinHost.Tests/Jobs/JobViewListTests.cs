using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using LabelFrame.WinHost.Api;
using Microsoft.Data.Sqlite;

namespace LabelFrame.WinHost.Tests.Jobs;

public class JobViewListTests
{
    [Fact]
    public void State_snapshot_should_serialize_to_identical_job_view()
    {
        foreach (var status in Enum.GetValues<LabelJobStatus>())
        {
            var job = new LabelJob
            {
                Id = "job", RequestId = "request", Status = status, CreatedAt = DateTimeOffset.UtcNow,
                Items = Enum.GetValues<LabelJobItemStatus>().Select((s, index) => new LabelJobItem
                {
                    Id = $"i{index}", JobId = "job", Index = index, Status = s, Zpl = "payload",
                    ErrorCode = s == LabelJobItemStatus.Failed ? "error" : null,
                    ErrorMessage = s == LabelJobItemStatus.Failed ? "错误" : null,
                }).ToArray(),
            };
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(JobViews.From(job)),
                System.Text.Json.JsonSerializer.Serialize(JobViews.From(LabelJobState.From(job))));
        }
    }

    [Fact]
    public void JobViews_From_should_include_history_columns()
    {
        var created = new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);
        var job = new LabelJob
        {
            Id = "job-1",
            RequestId = "req-1",
            CreatedAt = created,
            Items =
            [
                new LabelJobItem { Id = "i1", JobId = "job-1", Index = 0, Status = LabelJobItemStatus.Failed, Zpl = "x", ErrorCode = "LF_IO_001", ErrorMessage = "发送失败" },
                new LabelJobItem { Id = "i2", JobId = "job-1", Index = 1, Status = LabelJobItemStatus.Completed, Zpl = "y" },
            ],
        };

        var view = JobViews.From(job);

        Assert.Equal(created, view.CreatedAt);
        Assert.Equal(1, view.FailedItems);
        Assert.Equal("发送失败", view.ErrorMessage);
        Assert.Null(view.TargetDeviceId);
        Assert.Equal(2, view.TotalItems);
        Assert.Equal(1, view.CompletedItems);
    }

    [Fact]
    public async Task Store_ListRecentAsync_should_return_newest_first_with_limit()
    {
        await using var database = new TemporaryJobDatabase();
        var store = new SqliteLabelJobStore(database.Path);
        await store.InitializeAsync();
        await store.CreateJobAsync(new LabelJob
        {
            Id = "j1",
            RequestId = "r1",
            CreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            Items = [],
        });
        await store.CreateJobAsync(new LabelJob
        {
            Id = "j2",
            RequestId = "r2",
            CreatedAt = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero),
            Items = [],
        });
        await store.CreateJobAsync(new LabelJob
        {
            Id = "j3",
            RequestId = "r3",
            CreatedAt = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
            Items = [],
        });

        var recent = await store.ListRecentAsync(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("j3", recent[0].Id);
        Assert.Equal("j2", recent[1].Id);
    }

    [Fact]
    public async Task Temporary_database_cleanup_waits_for_transient_sharing_handle()
    {
        await using var database = new TemporaryJobDatabase();
        var store = new SqliteLabelJobStore(database.Path);
        await store.InitializeAsync();
        await store.ListRecentAsync(2);
        using var blocker = new FileStream(database.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var cleanup = database.DisposeAsync().AsTask();
        Assert.False(cleanup.IsCompleted);
        blocker.Dispose();
        await cleanup;
        Assert.False(File.Exists(database.Path));
    }

    [Fact]
    public async Task Temporary_database_cleanup_fails_for_persistent_handle_then_recovers()
    {
        await using var database = new TemporaryJobDatabase();
        await new SqliteLabelJobStore(database.Path).InitializeAsync();
        using (var blocker = new FileStream(database.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var error = await Assert.ThrowsAsync<IOException>(() => database.DisposeAsync().AsTask());
            Assert.Equal(32, error.HResult & 0xffff);
            Assert.True(File.Exists(database.Path));
        }
        await database.DisposeAsync();
        Assert.False(File.Exists(database.Path));
    }

    [Fact]
    public async Task Failed_list_releases_readers_and_connection_before_cleanup()
    {
        await using var database = new TemporaryJobDatabase();
        var store = new SqliteLabelJobStore(database.Path);
        await store.InitializeAsync();
        await store.CreateJobAsync(new LabelJob
        {
            Id = "job", RequestId = "request",
            Items = [new LabelJobItem { Id = "item", JobId = "job", Zpl = "payload" }],
        });
        using (var connection = new SqliteConnection(SqliteSupport.BuildConnectionString(database.Path)))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM job_payloads;";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ListRecentAsync(2));
        await database.DisposeAsync();
        Assert.False(File.Exists(database.Path));
    }

    /// <summary>管理历史列表测试自建的临时库及定向清理。</summary>
    private sealed class TemporaryJobDatabase : IAsyncDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-job-view-{Guid.NewGuid():N}");

        public TemporaryJobDatabase()
        {
            Directory.CreateDirectory(_root);
            Path = System.IO.Path.Combine(_root, "jobs.db");
        }

        public string Path { get; }

        public async ValueTask DisposeAsync()
        {
            if (!Directory.Exists(_root)) return;
            using var connection = new SqliteConnection(SqliteSupport.BuildConnectionString(Path));
            SqliteConnection.ClearPool(connection);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                    return;
                }
                catch (IOException error) when (attempt < 4 && (error.HResult & 0xffff) is 32 or 33)
                {
                    // 仅容忍临时共享占用，持续泄漏与其他 IO 错误必须使测试失败。
                    await Task.Delay(100);
                }
            }
        }
    }
}
