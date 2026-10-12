using LabelFrame.Api;
using LabelFrame.Core.Logs;
using LabelFrame.Server;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFrame.Server.Tests;

public class DataCleanupServiceTests
{
    private const string OldTime = "2020-01-01T00:00:00.0000000+00:00";

    [Fact]
    public async Task Delete_terminal_jobs_before_cutoff_should_remove_only_old_terminal_jobs()
    {
        using var temp = new TempServer();
        await temp.Service.RegisterDeviceAsync("device-1", "一号机");
        await temp.Service.SubmitJobAsync(CreateRequest("req-old", "device-1"));
        await temp.Service.SubmitJobAsync(CreateRequest("req-new", "device-1"));

        // 两个终态作业（Claimed -> Completed）；req-pending 之后提交保持 Pending
        var claimed = await temp.Service.ClaimPendingJobsAsync("device-1");
        Assert.Equal(2, claimed.Count);
        await temp.Service.ReportResultAsync("device-1", claimed[0].JobId, new ReportResultRequest("Completed", 1, 0, null));
        await temp.Service.ReportResultAsync("device-1", claimed[1].JobId, new ReportResultRequest("Completed", 1, 0, null));
        await temp.Service.SubmitJobAsync(CreateRequest("req-pending", "device-1"));

        // 把 req-old 的结束时间改到很早（模拟超期）
        await BackdateFinishedAtAsync(temp.Path, "req-old");

        // 截止时间取 1 小时前：req-new（刚完成）保留，req-old（2020）被清理
        var deleted = await temp.Db.DeleteTerminalJobsBeforeAsync(DateTimeOffset.UtcNow.AddHours(-1));

        Assert.Equal(1, deleted);
        var jobs = await temp.Service.ListJobsAsync(100);
        var remaining = jobs.Select(j => j.RequestId).ToList();
        Assert.Contains("req-new", remaining);
        Assert.Contains("req-pending", remaining);
        Assert.DoesNotContain("req-old", remaining);
    }

    [Fact]
    public async Task Cleanup_service_should_delete_old_logs_and_keep_recent()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-clean-{Guid.NewGuid():N}.db");
        try
        {
            var logStore = new SqliteLogStore(dbPath);
            await logStore.InitializeAsync();
            await logStore.AppendAsync("pda-1", ["旧日志"], CancellationToken.None);
            await logStore.AppendAsync("pda-1", ["新日志"], CancellationToken.None);
            await BackdateLogAsync(dbPath, "旧日志");

            using var temp = new TempServer();
            var options = new ServerOptions { LogRetentionDays = 30, JobRetentionDays = 30, CleanupIntervalHours = 24 };
            var service = new DataCleanupService(temp.Db, logStore, options, NullLogger<DataCleanupService>.Instance);
            await service.CleanupAsync();

            var entries = await logStore.QueryAsync("pda-1", null, CancellationToken.None);
            var entry = Assert.Single(entries);
            Assert.Contains("新日志", entry.Line);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public void Server_options_defaults_and_environment_overrides()
    {
        var options = new ServerOptions();
        Assert.Equal(30, options.JobRetentionDays);
        Assert.Equal(90, options.LogRetentionDays);
        Assert.Equal(24, options.CleanupIntervalHours);
        Assert.Contains("ProgramData", ServerOptions.DefaultDataDirectory);

        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_JOB_RETENTION_DAYS", "7");
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_RETENTION_DAYS", "15");
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_CLEANUP_INTERVAL_HOURS", "6");
        try
        {
            options.ApplyEnvironmentOverrides();
            Assert.Equal(7, options.JobRetentionDays);
            Assert.Equal(15, options.LogRetentionDays);
            Assert.Equal(6, options.CleanupIntervalHours);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_JOB_RETENTION_DAYS", null);
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_RETENTION_DAYS", null);
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_CLEANUP_INTERVAL_HOURS", null);
        }
    }

    [Fact]
    public async Task Enforce_size_limit_should_delete_oldest_rows_and_reclaim_disk_space()
    {
        // / （KB 级阈值直测 store 量闸）：库文件超阈值按大小删最旧直至回到阈值内——
        // 全部行均为刚写入的新行（远新于 90 天保留期）仍被删除，证明量闸不受保留期下限约束（有意行为）；
        // 度量口径 = logs.db + logs.db-wal 合计（VACUUM + wal_checkpoint(TRUNCATE) 之后取值——DELETE 后主文件不收缩）
        using var logDb = new TempLogDb();
        // 峰值约为阈值的 2.5 倍：既有收敛余量（每轮删 max(1000, 行数/10)，轮数上限 10），
        // 又保证「回收后 < 峰值一半」与「收敛后仍留存大量新行」两个断言都有富余
        const int rowCount = 9000;
        var lines = Enumerable.Range(1, rowCount).Select(i => $"size-gate-{i:000000}-{new string('s', 160)}").ToList();
        await logDb.Store.AppendAsync("pda-1", lines, CancellationToken.None);

        var peak = logDb.TotalSize;
        const long threshold = 1024L * 1024; // 1MB
        Assert.True(peak > threshold * 2, $"测试前提不成立：峰值 {peak} 字节应显著大于阈值 {threshold}");

        var deleted = await logDb.Store.EnforceSizeLimitAsync(threshold, CancellationToken.None);

        Assert.True(deleted.DeletedRows > 0, "超阈值未触发量闸删除");
        Assert.Null(deleted.VacuumWarning); // 正常路径无异常态警告
        Assert.True(logDb.TotalSize <= threshold, $"合计未回到阈值内：{logDb.TotalSize} > {threshold}");
        Assert.True(logDb.TotalSize < peak / 2, $"空间未真实回收：{logDb.TotalSize}（回收后）应 < {peak / 2}（峰值一半）");

        var (count, minId, maxId) = await RowStatsAsync(logDb.Store.DatabasePath);
        Assert.Equal(rowCount - deleted.DeletedRows, count);
        // 删除取走的是最旧连续前缀（id 升序 = 时间最旧，AUTOINCREMENT 严格递增不回用）：留存 id 恰为 [minId, maxId] 连续段且含最新行
        Assert.Equal(rowCount, maxId);
        Assert.Equal(rowCount - count + 1, minId);

        var entries = await logDb.Store.QueryAsync("pda-1", null, CancellationToken.None);
        Assert.Contains(entries, entry => entry.Line.Contains($"size-gate-{rowCount:000000}")); // 最新行留存
    }

    [Fact]
    public async Task Enforce_size_limit_on_empty_db_smaller_than_threshold_should_return_warning()
    {
        // 留观项锚定：表已空 / 无行可删而库文件仍超阈值（阈值小于空库体积）——返回异常态警告交调用方记录
        // 不抛出、不无限重试；正常收敛路径警告为 null（见上测断言）
        using var logDb = new TempLogDb();

        var result = await logDb.Store.EnforceSizeLimitAsync(100, CancellationToken.None); // 阈值 100 字节 < 空库体积

        Assert.Equal(0, result.DeletedRows);
        Assert.NotNull(result.VacuumWarning);
        Assert.Contains("仍超阈值", result.VacuumWarning);
    }

    [Fact]
    public async Task Cleanup_service_should_enforce_logs_db_size_gate_after_retention_cleanup()
    {
        // 编排（MB 级阈值走 ServerOptions）：按期删（时间闸）不变 → 量闸接管大小维度
        // 全部行均为新行（按期删除零命中），CleanupAsync 后合计仍回到阈值内——两道闸先后生效
        using var logDb = new TempLogDb();
        var lines = Enumerable.Range(1, 8000).Select(i => new string('b', 300)).ToList();
        await logDb.Store.AppendAsync("pda-1", lines, CancellationToken.None);
        var peak = logDb.TotalSize;
        Assert.True(peak > 1024L * 1024, $"测试前提不成立：峰值 {peak} 字节应大于 1MB 阈值");

        using var temp = new TempServer();
        var options = new ServerOptions { LogRetentionDays = 90, JobRetentionDays = 30, LogsDbMaxSizeMB = 1 };
        var service = new DataCleanupService(temp.Db, logDb.Store, options, NullLogger<DataCleanupService>.Instance);
        await service.CleanupAsync();

        Assert.True(logDb.TotalSize <= 1024L * 1024, $"量闸未回到阈值内：{logDb.TotalSize}");
        Assert.True(logDb.TotalSize < peak / 2, $"空间未真实回收：{logDb.TotalSize}（回收后）应 < {peak / 2}（峰值一半）");
    }

    [Fact]
    public async Task Cleanup_service_without_size_gate_should_keep_all_recent_logs()
    {
        // 回归：LogsDbMaxSizeMB = 0 / 负 = 关闭量闸——按期行为与现状一致，新行全保留
        using var logDb = new TempLogDb();
        var lines = Enumerable.Range(1, 300).Select(i => $"regression-{i}").ToList();
        await logDb.Store.AppendAsync("pda-1", lines, CancellationToken.None);

        using var temp = new TempServer();
        var options = new ServerOptions { LogRetentionDays = 90, JobRetentionDays = 30, LogsDbMaxSizeMB = 0 };
        var service = new DataCleanupService(temp.Db, logDb.Store, options, NullLogger<DataCleanupService>.Instance);
        await service.CleanupAsync();

        var entries = await logDb.Store.QueryAsync("pda-1", null, CancellationToken.None);
        Assert.Equal(300, entries.Count);
    }

    [Fact]
    public void Server_options_log_size_limits_defaults_and_environment_overrides()
    {
        // 两个新配置项默认值与环境变量覆盖（有效 / 无效 / 缺省三态，对齐既有模式）
        var options = new ServerOptions();
        Assert.Equal(50, options.LogFileMaxSizeMB);
        Assert.Equal(256, options.LogsDbMaxSizeMB);

        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB", "100");
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB", "512");
        try
        {
            options.ApplyEnvironmentOverrides();
            Assert.Equal(100, options.LogFileMaxSizeMB);
            Assert.Equal(512, options.LogsDbMaxSizeMB);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB", null);
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB", null);
        }

        // 无效值（非整数）不覆盖——保持默认；缺省（变量未设）亦保持默认
        var fresh = new ServerOptions();
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB", "not-a-number");
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB", "not-a-number");
        try
        {
            fresh.ApplyEnvironmentOverrides();
            Assert.Equal(50, fresh.LogFileMaxSizeMB);
            Assert.Equal(256, fresh.LogsDbMaxSizeMB);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB", null);
            Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB", null);
        }
    }

    private static SubmitJobRequest CreateRequest(string requestId, string deviceId) => new(
        requestId,
        new TemplateDto(SampleContract, SampleLayout),
        [new LabelDto(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" })],
        TargetDeviceId: deviceId);

    private static LabelFrame.Core.Contracts.LabelContract SampleContract { get; } = new()
    {
        Name = "location-label",
        Version = "1.0",
        Fields =
        [
            new LabelFrame.Core.Contracts.LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true },
            new LabelFrame.Core.Contracts.LabelField { Key = "zone", DisplayName = "区域", IsRequired = true },
        ],
    };

    private static LabelFrame.Core.Layout.LabelLayout SampleLayout { get; } = new()
    {
        Name = "location-label-100x60",
        ContractName = "location-label",
        ContractVersion = "1.0",
        WidthMm = 100,
        HeightMm = 60,
        Elements =
        [
            new LabelFrame.Core.Layout.LabelTextElement { SourceKey = "zone", XMm = 5, YMm = 4, FontHeightMm = 5, FontWidthMm = 5 },
            new LabelFrame.Core.Layout.LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
        ],
    };

    private static async Task BackdateFinishedAtAsync(string dbPath, string requestId)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE server_jobs SET finished_at = $time WHERE request_id = $requestId;";
        command.Parameters.AddWithValue("$time", OldTime);
        command.Parameters.AddWithValue("$requestId", requestId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task BackdateLogAsync(string dbPath, string line)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE logs SET time = $time WHERE line LIKE '%' || $line || '%';";
        command.Parameters.AddWithValue("$time", OldTime);
        command.Parameters.AddWithValue("$line", line);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>行统计（总数 / 最小 id / 最大 id）——断言量闸删的是最旧连续前缀（id 严格递增不回用，按 id 升序 = 按时间最旧）。</summary>
    private static async Task<(long Count, long MinId, long MaxId)> RowStatsAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(id), MAX(id) FROM logs;";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    /// <summary>
    /// 临时日志库（量闸测试用）：独立临时目录承载 logs.db（含 -wal / -shm 伴生文件），
    /// TotalSize 为量闸同口径度量（logs.db + logs.db-wal 合计）；收尾先 ClearAllPools 释放池化连接句柄再删目录。
    /// </summary>
    private sealed class TempLogDb : IDisposable
    {
        public TempLogDb()
        {
            Folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lflogdb-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Folder);
            Store = new SqliteLogStore(System.IO.Path.Combine(Folder, "logs.db"));
            Store.InitializeAsync().GetAwaiter().GetResult();
        }

        public string Folder { get; }

        public SqliteLogStore Store { get; }

        public long TotalSize
        {
            get
            {
                static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
                var database = System.IO.Path.Combine(Folder, "logs.db");
                return Length(database) + Length(database + "-wal");
            }
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                System.IO.Directory.Delete(Folder, recursive: true);
            }
            catch (IOException)
            {
                // 句柄延迟释放时忽略清理失败
            }
        }
    }
}
