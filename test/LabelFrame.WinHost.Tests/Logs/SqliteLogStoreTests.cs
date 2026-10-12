using LabelFrame.Core.Logs;

namespace LabelFrame.WinHost.Tests.Logs;

public class SqliteLogStoreTests
{
    [Fact]
    public async Task Append_and_query_should_round_trip_per_line()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-log-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLogStore(dbPath);
            await store.InitializeAsync();
            // 多行提交按物理行拆为独立记录（此前合并为一条含换行符的 line）
            await store.AppendAsync("pda-1", ["打印完成", "第 1 张 OK"], CancellationToken.None);

            var entries = await store.QueryAsync("pda-1", null, CancellationToken.None);

            // 查询按 id 倒序返回（最新在前）：拆行后为两条独立记录
            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.Equal("pda-1", e.DeviceId));
            Assert.Equal("第 1 张 OK", entries[0].Line);
            Assert.Equal("打印完成", entries[1].Line);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Append_single_line_with_embedded_newlines_should_split_into_rows()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-log-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLogStore(dbPath);
            await store.InitializeAsync();
            // 单个列表元素内含换行（如整段堆栈一次回传）：同样按物理行拆分（\r\n / \n / \r 统一处理）
            await store.AppendAsync("pda-1", ["first\r\nsecond\nthird\rfourth"], CancellationToken.None);

            var entries = await store.QueryAsync("pda-1", null, CancellationToken.None);

            var lines = entries.Select(e => e.Line).Reverse().ToList();
            Assert.Equal(["first", "second", "third", "fourth"], lines);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Append_should_skip_blank_lines_and_ignore_all_blank_submission()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-log-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLogStore(dbPath);
            await store.InitializeAsync();
            // 空白行（含纯空白字符）不落库
            await store.AppendAsync("pda-1", ["A", "", "   ", "B"], CancellationToken.None);
            var entries = await store.QueryAsync("pda-1", null, CancellationToken.None);
            Assert.Equal(["B", "A"], entries.Select(e => e.Line).ToList());

            // 全空白提交：不产生任何记录
            await store.AppendAsync("pda-2", ["", "  "], CancellationToken.None);
            Assert.Empty(await store.QueryAsync("pda-2", null, CancellationToken.None));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Query_without_device_should_return_all()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-log-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLogStore(dbPath);
            await store.InitializeAsync();
            await store.AppendAsync("pda-1", ["A"], CancellationToken.None);
            await store.AppendAsync("pda-2", ["B"], CancellationToken.None);

            var entries = await store.QueryAsync(null, null, CancellationToken.None);

            Assert.Equal(2, entries.Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }
}
