using System.Diagnostics;
using System.Text;
using LabelFrame.Api;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Transport;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace LabelFrame.WinHost.Tests.Perf;

/// <summary>
/// 单张全链路延迟（Trait=Perf，`dotnet test --filter Category=Perf` 运行）：
/// POST /api/jobs → 契约校验 → Skia 渲染 → ^GF 编码 → 入队 → Worker 发送（FakeTransport）→ 终态。
/// 这是「提交到出纸 &lt; 1 秒」指标的系统侧全自动版本（物理打印除外）。
/// </summary>
[Trait("Category", "Perf")]
public sealed class SingleLabelPipelineTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _directory;

    public SingleLabelPipelineTests(ITestOutputHelper output)
    {
        _output = output;
        _directory = Path.Combine(Path.GetTempPath(), $"lfpipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task Single_label_submit_to_completed_should_be_well_under_1s()
    {
        var dbPath = Path.Combine(_directory, "jobs.db");
        var templatesPath = Path.Combine(_directory, "templates.db");
        var store = new SqliteLabelJobStore(dbPath);
        await store.InitializeAsync();
        var queue = new LabelJobQueue(store);
        var templates = new TemplateStore(templatesPath);
        await templates.InitializeAsync();
        var transport = new InstantTransport();
        var settings = new PrintSettings();
        var worker = new JobPrintWorker(queue, new ConstTransportManager(transport), NullLogger<JobPrintWorker>.Instance, settings);
        var submission = new JobSubmissionService(
            queue, new ZplImageEncoder(), 203, new SkiaLabelRenderer(), templates, new ConstTransportManager(transport),
            new LabelFrame.Core.Transport.Plugins.TransportPluginRegistry(),
            new LabelFrame.Core.Transport.Plugins.TransportPluginContext(TextWriter.Null, _directory),
            TextWriter.Null);
        try
        {
            await worker.StartAsync(CancellationToken.None);

            // 30 张逐张测量（每张独立作业：渲染 → 编码 → 入队 → Worker 发送 → 终态可查）
            var latencies = new List<long>();
            for (var i = 0; i < 30; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await submission.SubmitAsync(new SubmitJobRequest(
                    $"perf-{i}",
                    new TemplateDto(SampleContract, SampleLayout),
                    [new LabelDto(new Dictionary<string, string> { ["code"] = $"LF-{i:D4}" })]));
                Assert.Null(result.ErrorCode);
                Assert.NotNull(result.Job);
                // 轮询到终态（Log 传输即时完成；轮询间隔 5ms 仅是观测开销）
                LabelJob? job;
                while (true)
                {
                    job = await queue.GetAsync(result.Job.Id);
                    if (job is { Status: LabelJobStatus.Completed or LabelJobStatus.Failed })
                    {
                        break;
                    }

                    await Task.Delay(5);
                }
                stopwatch.Stop();
                Assert.Equal(LabelJobStatus.Completed, job.Status);
                latencies.Add(stopwatch.ElapsedMilliseconds);
            }

            latencies.Sort();
            var p50 = latencies[latencies.Count / 2];
            var p99 = latencies[(int)(latencies.Count * 0.99)];
            _output.WriteLine($"单张提交→终态（60x40mm @203dpi）：p50={p50}ms p99={p99}ms max={latencies[^1]}ms");

            // 需求指标：提交到出纸 < 1 秒（物理打印除外）。
            // 起 Worker 由入队信号即时唤醒（原 200ms 空转轮询），延迟主体收敛为渲染+编码 ~1ms
            // 与入队 / 领取开销。阈值分环境口径：本地 p50 < 20ms / p99 < 500ms
            // （首张含 JIT / 字体加载预热）；GitHub 共享宿主实测 p50=31ms（镜像 20260907 起）——放宽至 100ms / 1s。
            var onSharedCi = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
            var (p50Limit, p99Limit) = onSharedCi ? (100, 1000) : (20, 500);
            Assert.True(p50 < p50Limit, $"p50={p50}ms 超过 {p50Limit}ms，信号唤醒失效或系统侧占用过高");
            Assert.True(p99 < p99Limit, $"p99={p99}ms 超过 {p99Limit}ms，系统侧占用过高");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }
    }

    private static Core.Contracts.LabelContract SampleContract { get; } = new()
    {
        Name = "perf", Version = "1.0",
        Fields = [new Core.Contracts.LabelField { Key = "code", DisplayName = "编码", IsRequired = true, Type = Core.Contracts.LabelFieldType.Text }],
    };

    private static Core.Layout.LabelLayout SampleLayout { get; } = new()
    {
        Name = "l", ContractName = "perf", ContractVersion = "1.0", WidthMm = 60, HeightMm = 40,
        Elements = [new Core.Layout.LabelBarcodeElement { SourceKey = "code", XMm = 2, YMm = 4, HeightMm = 12, DisplayValue = true }],
    };

    /// <summary>零耗时传输：只测系统路径，不含任何传输延迟。</summary>
    private sealed class InstantTransport : IPrintTransport
    {
        public Task SendAsync(string zpl, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ConstTransportManager(IPrintTransport transport) : ITransportManager
    {
        public TransportConfig CurrentConfig => new() { PluginId = "log" };
        public string ConfigFilePath => string.Empty;
        public IPrintTransport CurrentTransport => transport;
        public Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default)
            => Task.FromResult(new TransportChangeResult(true, "noop", config));
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
