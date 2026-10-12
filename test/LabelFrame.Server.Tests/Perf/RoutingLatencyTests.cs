using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace LabelFrame.Server.Tests.Perf;

/// <summary>
/// Server 路由全链路延迟（Trait=Perf，日常 CI 不跑——`dotnet test --filter Category=Perf` 显式运行）：
/// N 个虚拟设备并发「提交 → 领取 → 回报」，输出 p50/p95/p99。
/// 验证目标：并发修复（原子领取 / WAL / 门收窄）在负载下的端到端表现；无绝对阈值（CI 机器差异大），
/// 以「无错误 + 相对分布合理」为准，基准数据落 docs/PERF-BASELINE.md。
/// </summary>
[Trait("Category", "Perf")]
public sealed class RoutingLatencyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _directory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RoutingLatencyTests(ITestOutputHelper output)
    {
        _output = output;
        _directory = Path.Combine(Path.GetTempPath(), $"lfperf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", Path.Combine(_directory, "server.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", Path.Combine(_directory, "templates.db"));
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", Path.Combine(_directory, "logs.db"));
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    public static TheoryData<int> DeviceCounts => new() { 1, 5, 20 };

    [Theory]
    [MemberData(nameof(DeviceCounts))]
    public async Task Submit_claim_report_pipeline_under_concurrent_devices(int deviceCount)
    {
        // 注册设备
        for (var d = 0; d < deviceCount; d++)
        {
            var response = await _client.PostAsync("/api/devices", Json($$"""{ "deviceId": "perf-{{d}}", "name": "压测设备{{d}}" }"""));
            Assert.True(response.IsSuccessStatusCode);
        }

        var template = await _client.PostAsync("/api/templates", Json(TemplateJson));
        Assert.True(template.IsSuccessStatusCode);

        // 每设备 20 个作业（提交→领取→回报），并发跑
        const int jobsPerDevice = 20;
        var latencies = new List<long>();
        var errors = 0;
        await Task.WhenAll(Enumerable.Range(0, deviceCount).Select(async d =>
        {
            for (var i = 0; i < jobsPerDevice; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var submit = await _client.PostAsync("/api/jobs", Json($$"""
                        { "requestId": "perf-{{d}}-{{i}}", "targetDeviceId": "perf-{{d}}", "templateName": "性能模板",
                          "labels": [ { "data": { "code": "P-{{d}}-{{i}}" } }, { "data": { "code": "P-{{d}}-{{i}}-b" } } ] }
                        """));
                    submit.EnsureSuccessStatusCode();
                    var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
                    var jobId = job.GetProperty("jobId").GetString();

                    var claimed = await _client.GetFromJsonAsync<JsonElement>($"/api/devices/perf-{d}/jobs/pending");
                    Assert.True(claimed.GetArrayLength() >= 0, "领取不应报错");

                    var report = await _client.PostAsync($"/api/devices/perf-{d}/jobs/{jobId}/result",
                        Json("""{ "status": "Completed", "completedItems": 2 }"""));
                    Assert.True(report.IsSuccessStatusCode);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref errors);
                }
                finally
                {
                    lock (latencies)
                    {
                        latencies.Add(stopwatch.ElapsedMilliseconds);
                    }
                }
            }
        }));

        Assert.Equal(0, errors);
        latencies.Sort();
        var p50 = latencies[latencies.Count / 2];
        var p95 = latencies[(int)(latencies.Count * 0.95)];
        var p99 = latencies[(int)(latencies.Count * 0.99)];
        _output.WriteLine($"设备 {deviceCount} 并发（每设备 {jobsPerDevice} 作业，每作业 2 张）：p50={p50}ms p95={p95}ms p99={p99}ms max={latencies[^1]}ms");

        // 实测特征（PERF-BASELINE.md）：本机 p50 恒 3-9ms；SQLite 单写者使高并发下写事务排队，
        // 20 设备时 p95 尾部 2-3s（busy_timeout 排队，不丢不错）。阈值分环境口径：
        // 本地严格——p50 < 50ms（主体不受影响）+ p95 分层（≤5 设备 < 2s、20 设备 < 5s）；
        // GitHub 共享宿主 p50 轮间抖动 10-300 倍（4 轮实测 51-1169ms，同代码本机 6-9ms）无判别力——
        // 只判「无错误 + p95 分层」（放大至 5s / 10s），p50 转观测不判定。
        var onSharedCi = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        var p95Limit = onSharedCi
            ? (deviceCount <= 5 ? 5000 : 10000)
            : (deviceCount <= 5 ? 2000 : 5000);
        if (!onSharedCi)
        {
            Assert.True(p50 < 50, $"p50={p50}ms 主体延迟回归");
        }
        Assert.True(p95 < p95Limit, $"p95={p95}ms 超过 {p95Limit}ms（{deviceCount} 设备），锁竞争异常");
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private const string TemplateJson = """
        {
          "name": "性能模板", "group": "压测",
          "contract": { "name": "perf", "version": "1.0", "fields": [ { "key": "code", "displayName": "编码", "isRequired": true, "type": "text" } ] },
          "layout": { "name": "l", "contractName": "perf", "contractVersion": "1.0", "widthMm": 60, "heightMm": 40,
                      "elements": [ { "type": "text", "sourceKey": "code", "xMm": 2, "yMm": 2, "fontHeightMm": 5 } ] }
        }
        """;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _client.Dispose();
        _factory.Dispose();
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_DB", null);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_TEMPLATES_DB", null);
        Environment.SetEnvironmentVariable("LABELFRAME_SERVER_LOGS_DB", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
        catch (IOException) { }
    }
}
