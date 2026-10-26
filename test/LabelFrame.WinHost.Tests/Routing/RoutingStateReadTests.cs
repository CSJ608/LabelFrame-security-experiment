using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using LabelFrame.WinHost.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace LabelFrame.WinHost.Tests.Routing;

public class RoutingStateReadTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Report_Perf_should_measure_real_report_reads(bool binary)
    {
        using var fixture = await Fixture.CreateAsync(200, binary);
        await fixture.Report(CancellationToken.None);
        for (var round = 0; round < 4; round++)
        {
            var started = Stopwatch.GetTimestamp();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var task = fixture.Report(CancellationToken.None);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var elapsed = Stopwatch.GetElapsedTime(started);
            Assert.True(task.IsCompletedSuccessfully);
            await task;
            output.WriteLine($"binary={binary} round={round} ms={elapsed.TotalMilliseconds:F3} bytes={bytes}");
        }
        Assert.Empty(fixture.Poller.Progress);
        Assert.Empty(fixture.Poller.Results);
        await fixture.AssertPayloadsAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Report_should_not_allocate_print_payloads(bool binary)
    {
        using var fixture = await Fixture.CreateAsync(64, binary);
        await fixture.Report(CancellationToken.None);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var task = fixture.Report(CancellationToken.None);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(task.IsCompletedSuccessfully);
        await task;
        output.WriteLine($"binary={binary} bytes={bytes}");
        Assert.True(bytes < 2_000_000, $"路由状态回报不应分配完整打印载荷，实际 {bytes} bytes");
        Assert.Empty(fixture.Poller.Progress);
        await fixture.AssertPayloadsAsync();
    }

    [Theory]
    [InlineData(LabelJobStatus.Completed)]
    [InlineData(LabelJobStatus.Failed)]
    [InlineData(LabelJobStatus.Cancelled)]
    public async Task Terminal_report_should_preserve_counts_first_error_and_retry(LabelJobStatus status)
    {
        using var fixture = await Fixture.CreateAsync(4, true);
        await fixture.ExecuteAsync("""
            UPDATE job_items SET status = CASE item_index WHEN 0 THEN 'Completed' WHEN 1 THEN 'Failed' WHEN 2 THEN 'Cancelled' ELSE 'Pending' END,
                error_code = CASE WHEN item_index > 0 THEN 'code-' || item_index ELSE NULL END,
                error_message = CASE WHEN item_index > 0 THEN '错误-' || item_index ELSE NULL END;
            """);
        await fixture.Store.SetJobStatusAsync(fixture.Job.Id, status);
        fixture.Poller.FailResult = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Report(CancellationToken.None));
        Assert.Single(fixture.Mappings);
        fixture.Poller.FailResult = false;
        await fixture.Report(CancellationToken.None);
        var actual = Assert.Single(fixture.Poller.Results);
        Assert.Equal(new ServerJobResult(status == LabelJobStatus.Completed ? "Completed" : "Failed", 1, 2, "错误-1", "code-1"), actual);
        Assert.Empty(fixture.Mappings);
        await fixture.Report(CancellationToken.None);
        Assert.Single(fixture.Poller.Results);
        await fixture.AssertPayloadsAsync();
    }

    [Fact]
    public async Task Missing_payload_should_reject_report_without_removing_mapping()
    {
        using var fixture = await Fixture.CreateAsync(2, false);
        await fixture.ExecuteAsync("DELETE FROM job_payloads WHERE item_id = (SELECT id FROM job_items WHERE item_index = 1);");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Report(CancellationToken.None));
        Assert.Single(fixture.Mappings);
        Assert.Empty(fixture.Poller.Results);
        Assert.Empty(fixture.Poller.Progress);
    }

    private sealed class Poller : IServerJobPoller
    {
        internal List<ServerJobResult> Results { get; } = [];
        internal List<ServerJobProgress> Progress { get; } = [];
        internal bool FailResult { get; set; }
        public Task RegisterAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServerJobPayload>> FetchPendingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReportResultAsync(string jobId, ServerJobResult result, CancellationToken cancellationToken = default)
        {
            Assert.Equal("server", jobId);
            if (FailResult) throw new HttpRequestException("模拟回报失败");
            Results.Add(result);
            return Task.CompletedTask;
        }
        public Task ReportProgressAsync(string jobId, ServerJobProgress progress, CancellationToken cancellationToken = default)
        {
            Assert.Equal("server", jobId);
            Progress.Add(progress);
            return Task.CompletedTask;
        }
    }

    /// <summary>仅驱动路由回报方法的独立临时数据库夹具。</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lf-route-state-{Guid.NewGuid():N}");
        private readonly LabelJobQueue _queue;
        private readonly ServerRoutingWorker _worker;
        internal SqliteLabelJobStore Store { get; }
        internal LabelJob Job { get; private set; } = null!;
        internal Poller Poller { get; } = new();
        internal Func<CancellationToken, Task> Report { get; }
        internal ConcurrentDictionary<string, string> Mappings { get; }
        private string DatabasePath => Path.Combine(_directory, "jobs.db");

        private Fixture()
        {
            Store = new SqliteLabelJobStore(DatabasePath);
            _queue = new LabelJobQueue(Store);
            _worker = new ServerRoutingWorker(Poller, null!, _queue, TimeSpan.FromSeconds(1), NullLogger<ServerRoutingWorker>.Instance);
            Report = typeof(ServerRoutingWorker).GetMethod("ReportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Func<CancellationToken, Task>>(_worker);
            Mappings = (ConcurrentDictionary<string, string>)typeof(ServerRoutingWorker)
                .GetField("_localToServer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_worker)!;
        }

        internal static async Task<Fixture> CreateAsync(int count, bool binary)
        {
            var fixture = new Fixture();
            try
            {
                await fixture.Store.InitializeAsync();
                var payload = binary ? LabelJobPayload.OfBytes(Enumerable.Repeat((byte)0x9a, 105000).ToArray())
                    : LabelJobPayload.OfText(new string('A', 210000));
                (fixture.Job, _) = await fixture._queue.SubmitAsync("route", Enumerable.Repeat(payload, count).ToArray());
                fixture.Mappings[fixture.Job.Id] = "server";
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        internal async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection(SqliteSupport.BuildConnectionString(DatabasePath));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        internal async Task AssertPayloadsAsync()
        {
            var actual = (await _queue.GetAsync(Job.Id))!;
            Assert.Equal(Job.Items.Count, actual.Items.Count);
            for (var i = 0; i < actual.Items.Count; i++)
            {
                Assert.Equal(Job.Items[i].Id, actual.Items[i].Id);
                Assert.Equal(Job.Items[i].Zpl, actual.Items[i].Zpl);
                Assert.Equal(Job.Items[i].CommandBytes, actual.Items[i].CommandBytes);
            }
        }

        public void Dispose()
        {
            _worker.Dispose();
            _queue.Dispose();
            using var connection = new SqliteConnection(SqliteSupport.BuildConnectionString(DatabasePath));
            SqliteConnection.ClearPool(connection);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
