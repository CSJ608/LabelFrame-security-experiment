using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace LabelFrame.WinHost.Tests.Jobs;

[CollectionDefinition("历史列表分配", DisableParallelization = true)]
public sealed class HistoryListAllocationGroup;

[Collection("历史列表分配")]
public class HistoryListTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_Perf_should_measure_real_list_endpoint(bool binary)
    {
        await using var fixture = await Fixture.CreateAsync(120, 4, binary, true);
        await fixture.Client.GetStringAsync("/api/jobs");
        for (var round = 0; round < 4; round++)
        {
            var before = GC.GetTotalAllocatedBytes(true);
            var started = Stopwatch.GetTimestamp();
            var json = await fixture.Client.GetStringAsync("/api/jobs");
            var elapsed = Stopwatch.GetElapsedTime(started);
            var bytes = GC.GetTotalAllocatedBytes(true) - before;
            output.WriteLine($"binary={binary} round={round} ms={elapsed.TotalMilliseconds:F3} bytes={bytes} jsonChars={json.Length}");
            using var document = JsonDocument.Parse(json);
            Assert.Equal(100, document.RootElement.GetArrayLength());
        }
        await fixture.AssertResponseAsync(null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_should_not_allocate_print_payloads(bool binary)
    {
        await using var fixture = await Fixture.CreateAsync(20, 4, binary, true);
        await fixture.Client.GetStringAsync("/api/jobs");
        var before = GC.GetTotalAllocatedBytes(true);
        await fixture.Client.GetStringAsync("/api/jobs");
        var bytes = GC.GetTotalAllocatedBytes(true) - before;
        output.WriteLine($"binary={binary} bytes={bytes}");
        Assert.True(bytes < 3_000_000, $"历史列表不应加载完整打印载荷，实际 {bytes} bytes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_should_preserve_limits_order_all_fields_and_log_images(bool fallback)
    {
        await using var fixture = await Fixture.CreateAsync(503, 5, false, false, fallback);
        await fixture.ExecuteAsync("""
            UPDATE jobs SET status = CASE substr(id, -1) WHEN '0' THEN 'Pending' WHEN '1' THEN 'Printing'
                WHEN '2' THEN 'Suspended' WHEN '3' THEN 'Failed' WHEN '4' THEN 'Cancelled' ELSE 'Completed' END;
            UPDATE job_items SET status = CASE item_index WHEN 0 THEN 'Pending' WHEN 1 THEN 'Printing'
                WHEN 2 THEN 'Failed' WHEN 3 THEN 'Cancelled' ELSE 'Completed' END,
                error_code = CASE WHEN item_index = 2 THEN 'error' ELSE NULL END,
                error_message = CASE WHEN item_index = 2 THEN '中文错误' ELSE NULL END;
            DELETE FROM job_payloads WHERE item_id IN (SELECT id FROM job_items WHERE job_id = 'job-0502');
            DELETE FROM job_items WHERE job_id = 'job-0502';
            """);
        fixture.CreateImages("job-0501");
        foreach (int? limit in new int?[] { null, -1, 0, 1, 3, 500, 999 })
            await fixture.AssertResponseAsync(limit);
        var actual = await fixture.Client.GetFromJsonAsync<JobView[]>("/api/jobs?limit=3");
        Assert.NotNull(actual);
        Assert.Equal(new[] { "job-0502", "job-0500", "job-0501" }, actual.Select(j => j.JobId));
        Assert.Empty(actual[0].Items);
        Assert.Equal(5, actual[1].Items.Count);
        Assert.Equal(2, actual[2].PrintImageCount);
        Assert.Equal("中文错误", actual[2].ErrorMessage);
        fixture.SetPlugin("custom-printer");
        await fixture.AssertResponseAsync(3);
        actual = await fixture.Client.GetFromJsonAsync<JobView[]>("/api/jobs?limit=3");
        Assert.All(actual!, j => { Assert.Null(j.PrintImageDir); Assert.Null(j.PrintImageCount); });
    }

    [Fact]
    public async Task Empty_history_and_empty_job_should_remain_visible()
    {
        await using var fixture = await Fixture.CreateAsync(0, 0, false, false);
        await fixture.AssertResponseAsync(null);
        Assert.Empty((await fixture.Client.GetFromJsonAsync<JobView[]>("/api/jobs"))!);
        await fixture.Store.CreateJobAsync(new LabelJob { Id = "empty", RequestId = "empty", Items = [] });
        await fixture.AssertResponseAsync(null);
        Assert.Empty(Assert.Single((await fixture.Client.GetFromJsonAsync<JobView[]>("/api/jobs"))!).Items);
    }

    [Fact]
    public async Task Missing_payload_should_reject_selected_job_but_not_scan_outside_limit()
    {
        await using var fixture = await Fixture.CreateAsync(3, 2, true, false);
        await fixture.ExecuteAsync("DELETE FROM job_payloads WHERE item_id = 'job-0000-1';");
        await fixture.AssertResponseAsync(1);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.GetStringAsync("/api/jobs"));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ListRecentAsync(100));
    }

    [Fact]
    public async Task State_history_should_honor_cancellation_and_keep_public_payloads()
    {
        await using var fixture = await Fixture.CreateAsync(3, 2, true, true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.ListRecentStatesAsync(100, cancellation.Token));
        await fixture.AssertResponseAsync(null);
        var jobs = await fixture.Store.ListRecentAsync(100);
        Assert.All(jobs, j => Assert.All(j.Items, i =>
        {
            Assert.Empty(i.Zpl);
            Assert.Equal(105000, i.CommandBytes!.Length);
            Assert.All(i.CommandBytes, b => Assert.Equal((byte)0x9a, b));
        }));
    }

    private sealed class ReadOnlyStore(ILabelJobStore inner) : ILabelJobStore
    {
        public Task<IReadOnlyList<LabelJob>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
            => inner.ListRecentAsync(limit, cancellationToken);
        public Task InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LabelJob> CreateJobAsync(LabelJob job, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LabelJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LabelJob?> GetJobByRequestIdAsync(string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LabelJob?> SetJobStatusAsync(string jobId, LabelJobStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LabelJob?> SetItemStatusAsync(string jobId, string itemId, LabelJobItemStatus status, string? errorCode, string? errorMessage, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LabelJob>> ListJobsByStatusAsync(LabelJobStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasPendingItemsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoSendManager : ITransportManager
    {
        public TransportConfig CurrentConfig { get; } = new() { PluginId = "LOG" };
        public IPrintTransport CurrentTransport => throw new InvalidOperationException("测试禁止物理传输");
        public string ConfigFilePath => string.Empty;
        public Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"lf-history-{Guid.NewGuid():N}");
        private WebApplication? _app;
        internal HttpClient Client { get; private set; } = null!;
        internal SqliteLabelJobStore Store { get; private set; } = null!;
        private readonly NoSendManager _manager = new();
        private string _text = "";
        private byte[]? _data;
        private string DatabasePath => Path.Combine(_root, "jobs.db");
        private string PrintPath => Path.Combine(_root, "print");

        internal static async Task<Fixture> CreateAsync(int jobs, int items, bool binary, bool large, bool fallback = false)
        {
            var fixture = new Fixture();
            try
            {
                fixture.Store = new SqliteLabelJobStore(fixture.DatabasePath);
                await fixture.Store.InitializeAsync();
                var text = binary ? "" : new string('A', large ? 210000 : 8);
                var data = binary ? Enumerable.Repeat((byte)0x9a, large ? 105000 : 8).ToArray() : null;
                fixture._text = text;
                fixture._data = data;
                var date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                for (var n = 0; n < jobs; n++)
                {
                    var id = $"job-{n:D4}";
                    await fixture.Store.CreateJobAsync(new LabelJob
                    {
                        Id = id, RequestId = $"request-{n:D4}", Status = LabelJobStatus.Completed,
                        CreatedAt = date.AddMinutes(n / 2), UpdatedAt = date.AddMinutes(n),
                        Items = Enumerable.Range(0, items).Select(i => new LabelJobItem
                        {
                            Id = $"{id}-{i}", JobId = id, Index = i, Status = LabelJobItemStatus.Completed,
                            Zpl = text, CommandBytes = data
                        }).Reverse().ToArray()
                    });
                }
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton<ILabelJobStore>(fallback ? new ReadOnlyStore(fixture.Store) : fixture.Store);
                builder.Services.AddSingleton<ITransportManager>(fixture._manager);
                builder.Services.AddSingleton(new HostOptions { PrintOutputPath = fixture.PrintPath });
                builder.Services.AddSingleton<LabelJobQueue>();
                builder.Services.AddSingleton<JobSubmissionService>();
                fixture._app = builder.Build();
                fixture._app.MapJobsApi();
                await fixture._app.StartAsync();
                fixture.Client = fixture._app.GetTestClient();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal async Task AssertResponseAsync(int? limit)
        {
            var jobs = await Store.ListRecentAsync(Math.Clamp(limit ?? 100, 1, 500));
            var expected = jobs.Select(j => JobsApi.EnrichPrintInfo(JobViews.From(j), j.Id, _manager, PrintPath)).ToArray();
            var actual = await Client.GetFromJsonAsync<JsonElement>("/api/jobs" + (limit is null ? "" : $"?limit={limit}"));
            var expectedJson = JsonSerializer.SerializeToElement(expected, JsonSerializerOptions.Web);
            Assert.True(JsonElement.DeepEquals(expectedJson, actual), "历史响应应与公开完整查询投影逐字段相同");
            Assert.All(jobs, j => Assert.All(j.Items, i =>
            {
                Assert.Equal(_text, i.Zpl);
                if (_data is null) Assert.Null(i.CommandBytes);
                else Assert.True(_data.AsSpan().SequenceEqual(i.CommandBytes));
            }));
        }

        internal void SetPlugin(string pluginId) => _manager.CurrentConfig.PluginId = pluginId;

        internal void CreateImages(string jobId)
        {
            var path = Path.Combine(PrintPath, jobId);
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "1.png"), [1]);
            File.WriteAllBytes(Path.Combine(path, "2.png"), [2]);
            File.WriteAllText(Path.Combine(path, "ignore.txt"), "不计入图片");
        }

        internal async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection(SqliteSupport.BuildConnectionString(DatabasePath));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (_app is not null) await _app.DisposeAsync();
            using var connection = new SqliteConnection(SqliteSupport.BuildConnectionString(DatabasePath));
            SqliteConnection.ClearPool(connection);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
