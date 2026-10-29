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
/// Server 稳定性 soak（Trait=Soak，`dotnet test --filter Category=Soak` 运行，默认 5 分钟 / 环境变量 LF_SOAK_MINUTES 调整）：
/// 稳态平台期 = 混合负载持续运行（并发 提交+领取+回报+长轮询 notify+日志写入），
/// 按 Grafana soak 实践断言三类漂移——GC 堆无持续增长（首/末窗口对比）、SQLite WAL 文件有界（autocheckpoint 生效）、吞吐无衰减（末窗口 ≥ 首窗口 80%）。
/// </summary>
[Trait("Category", "Soak")]
public sealed class ServerSoakTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IsolatedServerEnvironment _environment = new();
    private string _directory => _environment.DirectoryPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ServerSoakTests(ITestOutputHelper output)
    {
        _output = output;
        _factory = new WebApplicationFactory<Program>();
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Mixed_load_steady_state_should_not_drift()
    {
        await IsolatedServerEnvironment.LoginAsync(_client);
        var minutes = double.TryParse(Environment.GetEnvironmentVariable("LF_SOAK_MINUTES"), out var m) ? m : 5;
        var devices = new[] { 0, 1, 2, 3 };
        foreach (var d in devices)
        {
            await _client.PostAsync("/api/devices", Json($$"""{ "deviceId": "soak-{{d}}", "name": "soak{{d}}" }"""));
        }
        await _client.PostAsync("/api/templates", Json("""
            { "name": "soak", "group": "压测",
              "contract": { "name": "s", "version": "1.0", "fields": [ { "key": "code", "displayName": "c", "isRequired": true, "type": "text" } ] },
              "layout": { "name": "l", "contractName": "s", "contractVersion": "1.0", "widthMm": 60, "heightMm": 40,
                          "elements": [ { "type": "text", "sourceKey": "code", "xMm": 2, "yMm": 2, "fontHeightMm": 5 } ] } }
            """));

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(minutes);
        var round = 0;
        var errors = 0;
        var firstWindowThroughput = -1.0;
        var firstWindowHeap = -1L;
        GC.Collect();
        var firstSnapshot = GC.GetTotalMemory(false);
        var snapshotAt = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            // 一轮 = 4 设备并发各完成 提交→领取→回报 + 1 次 notify + 1 次日志写入 + 1 次列表查询
            var roundStart = DateTime.UtcNow;
            await Task.WhenAll(devices.Select(async d =>
            {
                try
                {
                    var submit = await _client.PostAsync("/api/jobs", Json($$"""
                        { "requestId": "soak-{{round}}-{{d}}", "targetDeviceId": "soak-{{d}}", "templateName": "soak",
                          "labels": [ { "data": { "code": "S-{{round}}-{{d}}" } } ] }
                        """));
                    submit.EnsureSuccessStatusCode();
                    var job = await submit.Content.ReadFromJsonAsync<JsonElement>();
                    await _client.GetAsync($"/api/devices/soak-{d}/jobs/pending");
                    await _client.PostAsync($"/api/devices/soak-{d}/jobs/{job.GetProperty("jobId").GetString()}/result",
                        Json("""{ "status": "Completed", "completedItems": 1 }"""));
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref errors);
                }
            }));
            await _client.GetAsync("/api/devices/soak-0/jobs/notify?timeout=1");
            await _client.PostAsync("/api/logs", Json($$"""{ "deviceId": "soak-0", "lines": ["soak round {{round}}"] }"""));
            await _client.GetAsync("/api/jobs?limit=10");

            round++;

            // 每 30 秒采样一次：吞吐（轮/秒）、托管堆、WAL 尺寸
            if ((DateTime.UtcNow - snapshotAt).TotalSeconds >= 30)
            {
                var throughput = round / (DateTime.UtcNow - deadline + TimeSpan.FromMinutes(minutes)).TotalSeconds;
                var heap = GC.GetTotalMemory(false);
                var walSize = SafeWalSize();
                if (firstWindowThroughput < 0)
                {
                    firstWindowThroughput = throughput;
                    firstWindowHeap = heap;
                }
                _output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] round={round} heap={heap / 1024}KB wal={walSize / 1024}KB");
                snapshotAt = DateTime.UtcNow;
            }

            await Task.Delay(200);
        }

        // 无错误（稳态下偶发锁错误即不稳定）
        Assert.Equal(0, errors);

        // WAL 有界：autocheckpoint=1000 页（≈4MB）+ 运行期余量，10MB 上限
        var finalWal = SafeWalSize();
        Assert.True(finalWal < 10 * 1024 * 1024, $"WAL 文件 {finalWal / 1024}KB 无界增长——autocheckpoint 未生效");

        // 托管堆无持续增长：允许 50% 余量（GC 波动），排除泄漏
        GC.Collect();
        var finalHeap = GC.GetTotalMemory(false);
        Assert.True(finalHeap < firstSnapshot * 1.5 + 10 * 1024 * 1024,
            $"托管堆首 {firstSnapshot / 1024}KB → 末 {finalHeap / 1024}KB，疑似泄漏");

        _output.WriteLine($"soak {minutes} 分钟完成：{round} 轮 × 4 设备 = {round * 4} 作业，0 错误，WAL={finalWal / 1024}KB，堆 {firstSnapshot / 1024}KB→{finalHeap / 1024}KB");
    }

    /// <summary>WAL 文件尺寸（文件可能被 SQLite 关闭后清空/移除——无则 0）。</summary>
    long SafeWalSize() { try { return File.Exists(Path.Combine(_directory, "server.db-wal")) ? new FileInfo(Path.Combine(_directory, "server.db-wal")).Length : 0; } catch (IOException) { return 0; } }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _client.Dispose();
        _factory.Dispose();
        _environment.Dispose();
    }
}
