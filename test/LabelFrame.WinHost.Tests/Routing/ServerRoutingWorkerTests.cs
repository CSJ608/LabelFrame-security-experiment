using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using LabelFrame.WinHost.Tests.Transport;
using LabelFrame.WinHost.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFrame.WinHost.Tests.Routing;

public class ServerRoutingWorkerTests
{
    private sealed class FakePoller : IServerJobPoller
    {
        private readonly Queue<ServerJobPayload> _pending = new();
        public List<(string JobId, ServerJobResult Result)> Reported { get; } = [];
        public List<(string JobId, ServerJobProgress Progress)> ProgressReports { get; } = [];
        public int RegisterCount { get; private set; }

        /// <summary>长轮询等待时长（默认 20ms；可调大模拟服务端长时间挂起，验证回报不依赖它）。</summary>
        public TimeSpan WaitDelay { get; set; } = TimeSpan.FromMilliseconds(20);

        /// <summary>模拟 Server 不可达：进度上报抛异常（静默降级用）。</summary>
        public bool FailProgress { get; set; }
        public bool FailResults { get; set; }
        public int ResultAttempts;

        public void Enqueue(ServerJobPayload payload) => _pending.Enqueue(payload);

        public Task RegisterAsync(CancellationToken cancellationToken = default)
        {
            RegisterCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ServerJobPayload>> FetchPendingAsync(CancellationToken cancellationToken = default)
        {
            var batch = new List<ServerJobPayload>();
            while (_pending.Count > 0)
            {
                batch.Add(_pending.Dequeue());
            }

            return Task.FromResult<IReadOnlyList<ServerJobPayload>>(batch);
        }

        public async Task<bool> WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (_pending.Count > 0)
            {
                return true;
            }

            await Task.Delay(WaitDelay, cancellationToken);
            return false;
        }

        public Task ReportResultAsync(string jobId, ServerJobResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ResultAttempts);
            if (FailResults) throw new HttpRequestException("结果回报暂时不可达");
            Reported.Add((jobId, result));
            return Task.CompletedTask;
        }

        public Task ReportProgressAsync(string jobId, ServerJobProgress progress, CancellationToken cancellationToken = default)
        {
            if (FailProgress)
            {
                throw new HttpRequestException("模拟 Server 不可达");
            }

            ProgressReports.Add((jobId, progress));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Worker_should_print_routed_job_and_report_result()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroute-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            var queue = new LabelJobQueue(store);
            var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroutetpl-{Guid.NewGuid():N}.db");
            var templates = new TemplateStore(templatesDb);
            await templates.InitializeAsync();
            var submission = CreateSubmission(queue, templates);

            var poller = new FakePoller();
            var payload = new ServerJobPayload(
                "server-job-1",
                "req-route",
                1,
                new TemplateDto(SampleContract, SampleLayout),
                [new LabelDto(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" })]);
            poller.Enqueue(payload);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = new ServerRoutingWorker(poller, submission, queue, TimeSpan.FromMilliseconds(100), NullLogger<ServerRoutingWorker>.Instance);
            var workerTask = worker.StartAsync(cts.Token);

            // 等待本地作业创建
            LabelJob? localJob = null;
            for (var i = 0; i < 100 && localJob is null; i++)
            {
                await Task.Delay(50, cts.Token);
                localJob = await store.GetJobByRequestIdAsync("req-route", cts.Token);
            }

            Assert.NotNull(localJob);

            // 模拟打印 Worker：领取并完成
            var claimed = await queue.ClaimNextItemAsync(cts.Token);
            Assert.NotNull(claimed);
            await queue.CompleteItemAsync(claimed!.Value.JobId, claimed.Value.Item.Id, cts.Token);

            // 等待回报
            for (var i = 0; i < 100 && poller.Reported.Count == 0; i++)
            {
                await Task.Delay(50, cts.Token);
            }

            var report = Assert.Single(poller.Reported);
            Assert.Equal("server-job-1", report.JobId);
            Assert.Equal("Completed", report.Result.Status);
            Assert.Equal(1, report.Result.CompletedItems);

            await worker.StopAsync(cts.Token);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task Worker_should_report_finished_job_without_waiting_for_long_poll()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroute-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            var queue = new LabelJobQueue(store);
            var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroutetpl-{Guid.NewGuid():N}.db");
            var templates = new TemplateStore(templatesDb);
            await templates.InitializeAsync();
            var submission = CreateSubmission(queue, templates);

            var poller = new FakePoller
            {
                // 模拟长轮询长时间挂起：完成回报必须由独立循环送达，不能等长轮询返回
                WaitDelay = TimeSpan.FromSeconds(10),
            };
            var payload = new ServerJobPayload(
                "server-job-2",
                "req-route-fast",
                1,
                new TemplateDto(SampleContract, SampleLayout),
                [new LabelDto(new Dictionary<string, string> { ["zone"] = "A-02", ["locationCode"] = "A-02-01-01" })]);
            poller.Enqueue(payload);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = new ServerRoutingWorker(poller, submission, queue, TimeSpan.FromMilliseconds(100), NullLogger<ServerRoutingWorker>.Instance);
            var workerTask = worker.StartAsync(cts.Token);

            LabelJob? localJob = null;
            for (var i = 0; i < 100 && localJob is null; i++)
            {
                await Task.Delay(50, cts.Token);
                localJob = await store.GetJobByRequestIdAsync("req-route-fast", cts.Token);
            }

            Assert.NotNull(localJob);

            var claimed = await queue.ClaimNextItemAsync(cts.Token);
            Assert.NotNull(claimed);
            await queue.CompleteItemAsync(claimed!.Value.JobId, claimed.Value.Item.Id, cts.Token);

            // 回报应在独立循环（1s 周期）内及时送达，而不是等 10s 长轮询超时
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 60 && poller.Reported.Count == 0; i++)
            {
                await Task.Delay(50, cts.Token);
            }

            stopwatch.Stop();
            Assert.Single(poller.Reported);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(3),
                $"完成回报被长轮询阻塞，延迟 {stopwatch.Elapsed.TotalSeconds:F1}s。");

            await worker.StopAsync(cts.Token);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
    [Fact]
    public async Task Worker_should_report_throttled_progress_with_fake_time_provider()
    {
        // FakeTimeProvider 驱动回报循环时钟——打印中间隔性上报、节流生效、终态后停发
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroute-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            var queue = new LabelJobQueue(store);
            var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroutetpl-{Guid.NewGuid():N}.db");
            var templates = new TemplateStore(templatesDb);
            await templates.InitializeAsync();
            var submission = CreateSubmission(queue, templates);

            var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
            var poller = new FakePoller();
            poller.Enqueue(new ServerJobPayload(
                "server-job-prog",
                "req-route-prog",
                3,
                new TemplateDto(SampleContract, SampleLayout),
                [
                    new LabelDto(new Dictionary<string, string> { ["zone"] = "A", ["locationCode"] = "A-1" }),
                    new LabelDto(new Dictionary<string, string> { ["zone"] = "B", ["locationCode"] = "B-1" }),
                    new LabelDto(new Dictionary<string, string> { ["zone"] = "C", ["locationCode"] = "C-1" }),
                ]));

            // CI 慢机上 SQLite 初始化 + 队列操作可超 20s（本地 ~2s），放宽避免环境性超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var worker = new ServerRoutingWorker(
                poller, submission, queue, TimeSpan.FromMilliseconds(100),
                NullLogger<ServerRoutingWorker>.Instance, time,
                progressInterval: TimeSpan.FromSeconds(1),
                reportInterval: TimeSpan.FromSeconds(1));
            await worker.StartAsync(cts.Token);

            LabelJob? localJob = null;
            for (var i = 0; i < 100 && localJob is null; i++)
            {
                await Task.Delay(50, cts.Token);
                localJob = await store.GetJobByRequestIdAsync("req-route-prog", cts.Token);
            }

            Assert.NotNull(localJob);

            // 第 1 张完成 → 虚拟时钟推进过一个间隔 → 收到首条进度 (1,0)
            await CompleteNextItemAsync(queue, cts.Token);
            await UntilAdvancedAsync(() => poller.ProgressReports.Count == 1, time, cts.Token);
            Assert.Equal(("server-job-prog", new ServerJobProgress(1, 0)), poller.ProgressReports[0]);

            // 第 2 张完成、时钟冻结 0.5s（间隔内）→ 抑制，不新增上报
            //（清扫若恰在此间运行也由节流器按「距上次上报不足间隔」抑制，语义不依赖清扫时机）
            await CompleteNextItemAsync(queue, cts.Token);
            await Task.Delay(500, cts.Token);
            Assert.Single(poller.ProgressReports);

            // 推进满间隔 → 上报 (2,0)
            await UntilAdvancedAsync(() => poller.ProgressReports.Count == 2, time, cts.Token);
            Assert.Equal(("server-job-prog", new ServerJobProgress(2, 0)), poller.ProgressReports[1]);

            // 终态：第 3 张完成后只走 result，不再有进度上报
            await CompleteNextItemAsync(queue, cts.Token);
            await UntilAdvancedAsync(() => poller.Reported.Count == 1, time, cts.Token);
            Assert.Equal(("server-job-prog", "Completed", 3), (poller.Reported[0].JobId, poller.Reported[0].Result.Status, poller.Reported[0].Result.CompletedItems));
            Assert.Equal(2, poller.ProgressReports.Count);

            await worker.StopAsync(cts.Token);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task Progress_failure_should_degrade_silently_and_not_block_result()
    {
        // Server 不可达（进度上报持续抛异常）不影响打印主链路，终态回报仍送达
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroute-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            var queue = new LabelJobQueue(store);
            var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroutetpl-{Guid.NewGuid():N}.db");
            var templates = new TemplateStore(templatesDb);
            await templates.InitializeAsync();
            var submission = CreateSubmission(queue, templates);

            var poller = new FakePoller { FailProgress = true };
            poller.Enqueue(new ServerJobPayload(
                "server-job-down",
                "req-route-down",
                1,
                new TemplateDto(SampleContract, SampleLayout),
                [new LabelDto(new Dictionary<string, string> { ["zone"] = "A", ["locationCode"] = "A-9" })]));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var worker = new ServerRoutingWorker(
                poller, submission, queue, TimeSpan.FromMilliseconds(100),
                NullLogger<ServerRoutingWorker>.Instance,
                TimeProvider.System,
                progressInterval: TimeSpan.FromMilliseconds(100),
                reportInterval: TimeSpan.FromMilliseconds(100));
            await worker.StartAsync(cts.Token);

            LabelJob? localJob = null;
            for (var i = 0; i < 100 && localJob is null; i++)
            {
                await Task.Delay(50, cts.Token);
                localJob = await store.GetJobByRequestIdAsync("req-route-down", cts.Token);
            }

            Assert.NotNull(localJob);

            // 打印（模拟）完成 → 终态回报在独立循环内送达，进度持续失败不拖垮循环
            var claimed = await queue.ClaimNextItemAsync(cts.Token);
            Assert.NotNull(claimed);
            await queue.CompleteItemAsync(claimed!.Value.JobId, claimed.Value.Item.Id, cts.Token);

            for (var i = 0; i < 100 && poller.Reported.Count == 0; i++)
            {
                await Task.Delay(50, cts.Token);
            }

            var report = Assert.Single(poller.Reported);
            Assert.Equal("server-job-down", report.JobId);
            Assert.Equal("Completed", report.Result.Status);
            Assert.Empty(poller.ProgressReports);

            await worker.StopAsync(cts.Token);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    /// <summary>领取并完成下一张（模拟打印 Worker 的串行领取-完成路径）。</summary>
    private static async Task CompleteNextItemAsync(LabelJobQueue queue, CancellationToken cancellationToken)
    {
        var claimed = await queue.ClaimNextItemAsync(cancellationToken);
        Assert.NotNull(claimed);
        await queue.CompleteItemAsync(claimed!.Value.JobId, claimed!.Value.Item.Id, cancellationToken);
    }

    /// <summary>构造提交服务（可注入 fake 编译器插件与预写 connection.json——原生指令模式路由场景用）。</summary>
    private static JobSubmissionService CreateSubmission(
        LabelJobQueue queue,
        TemplateStore templates,
        FakeCompilerTransportPlugin? plugin = null,
        string? connectionJson = null)
    {
        var (manager, registry) = TestTransportRegistry.CreateManagerWithRegistry(
            configure: plugin is null ? null : r => r.Register(plugin),
            connectionJson: connectionJson);
        return new JobSubmissionService(
            queue,
            new ZplImageEncoder(),
            dpiProvider: () => 203,
            new SkiaLabelRenderer(),
            templates,
            manager,
            registry,
            TestTransportRegistry.CreateContext(),
            TextWriter.Null);
    }

    /// <summary>
    /// 原生指令模式下编译失败 → 路由回报 Failed：任一标签编译失败本地不建作业
    /// 宿主按既有失败回报路径回报 Server Failed，错误消息含问题码与中文原因。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compile_failure_in_native_mode_should_report_server_job_failed(bool disconnected)
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroute-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            var queue = new LabelJobQueue(store);
            var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfroutetpl-{Guid.NewGuid():N}.db");
            var templates = new TemplateStore(templatesDb);
            await templates.InitializeAsync();

            // fake 编译器：locationCode=BAD 的标签编译失败（LF_ENC_002 + fieldKey），其余成功
            var plugin = new FakeCompilerTransportPlugin((document, options) =>
                document.Data.GetValueOrDefault("locationCode") == "BAD"
                    ? new LabelCommandCompileResult(null, JobErrorCodes.CommandCompileFailed, "内置字体无法表示该字符", "locationCode")
                    : new LabelCommandCompileResult("^XAFKE:OK^FS^XZ", null, null, null));
            var submission = CreateSubmission(
                queue,
                templates,
                plugin,
                """{ "pluginId": "fakecompile", "params": { "printMode": "native" } }""");

            var poller = new FakePoller { FailResults = disconnected };
            poller.Enqueue(new ServerJobPayload(
                "server-job-native-fail",
                "req-route-native-fail",
                1,
                new TemplateDto(SampleContract, SampleLayout),
                [new LabelDto(new Dictionary<string, string> { ["zone"] = "A", ["locationCode"] = "BAD" })]));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var activity = new HostActivityState();
            var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
            var worker = new ServerRoutingWorker(poller, submission, queue, TimeSpan.FromMilliseconds(100),
                NullLogger<ServerRoutingWorker>.Instance, time, activity: activity);
            async Task AdvanceUntilAsync(Func<bool> condition)
            {
                while (!condition())
                {
                    time.Advance(TimeSpan.FromSeconds(1));
                    await Task.Delay(10, cts.Token);
                }
            }
            await worker.StartAsync(cts.Token);
            if (disconnected)
            {
                await AdvanceUntilAsync(() => Volatile.Read(ref poller.ResultAttempts) > 0);
                Assert.True(Volatile.Read(ref poller.ResultAttempts) > 0);
                Assert.False(activity.TryPrepare(out _));
                poller.FailResults = false;
            }

            await AdvanceUntilAsync(() => poller.Reported.Count > 0);

            var report = Assert.Single(poller.Reported);
            Assert.Equal("server-job-native-fail", report.JobId);
            Assert.Equal("Failed", report.Result.Status);
            Assert.Contains(JobErrorCodes.CommandCompileFailed, report.Result.ErrorMessage);
            Assert.Contains("插件 fakecompile", report.Result.ErrorMessage);
            Assert.Contains("编译失败", report.Result.ErrorMessage);

            // 直连不建作业语义在路由侧 = 本地队列无该 requestId 作业
            Assert.Null(await store.GetJobByRequestIdAsync("req-route-native-fail", cts.Token));

            await worker.StopAsync(cts.Token);
            Assert.True(activity.TryPrepare(out _));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }


    /// <summary>
    /// 真实时钟等待条件满足，期间小步推进虚拟时钟——回报循环停泊在 Task.Delay(fakeTime) 时，
    /// 若一次大步 Advance 恰落在循环未停泊的空档会被错过，导致虚拟时钟冻结、条件永不成真；
    /// 小步推进保证已停泊的延迟最终到期（条件语义仍由节流器保证，不受清扫时机影响）。
    /// </summary>
    private static async Task UntilAdvancedAsync(Func<bool> condition, Microsoft.Extensions.Time.Testing.FakeTimeProvider time, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(50, cancellationToken);
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.True(condition(), "等待条件超时未满足。");
    }

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

    private static LabelLayout SampleLayout { get; } = new()
    {
        Name = "location-label-100x60",
        ContractName = "location-label",
        ContractVersion = "1.0",
        WidthMm = 100,
        HeightMm = 60,
        Elements =
        [
            new LabelTextElement { SourceKey = "zone", XMm = 5, YMm = 4, FontHeightMm = 5, FontWidthMm = 5 },
            new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
        ],
    };
}
