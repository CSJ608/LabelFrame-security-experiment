using System.Diagnostics;
using System.Text.RegularExpressions;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Logging;

namespace LabelFrame.WinHost.Tests.Jobs;

/// <summary>
/// JobPrintWorker 批次节流集成测试：FakeTransport 记录发送时间戳，
/// 断言「发送前暂停」的批间间隔——25 张/批 5 → 第 6/11/16/21 张前各停一次（共 4 次）；
/// 批间计数按作业重置（两个作业各 5 张 → 双方首张均不等待；B 作业内仍按批 5 节流）；不足一批不等待；禁用无间隔。
/// 判定双通道： 批次节流日志的「已发送张数」序列（确定性主通道——代码中日志与 Task.Delay 同分支
/// 无日志即无延迟，CI 高负载下依然精确）； 发送时间序列大间隔落在预期张序（辅助，证明延迟真实发生）。
/// </summary>
public class JobPrintWorkerThrottleTests
{
    private const int IntervalMs = 400;
    private const double PauseThresholdMs = 200;

    [Fact]
    public async Task Disabled_should_send_without_pauses()
    {
        var result = await RunWorkerAsync(new PrintSettingsDto(false, 5, IntervalMs), 25);

        Assert.Equal(25, result.Offsets.Count);
        // 确定性主通道：无任何「批次节流」决策 → 无暂停延迟
        Assert.Equal(0, result.ThrottleLogCount);
        Assert.Empty(result.SentCounts);
    }

    [Fact]
    public async Task Enabled_25_labels_batch_5_should_pause_before_6_11_16_21()
    {
        var result = await RunWorkerAsync(new PrintSettingsDto(true, 5, IntervalMs), 25);

        // 每批 5 张连续；第 6/11/16/21 张发送前各停一次（共 4 次）
        Assert.Equal(25, result.Offsets.Count);
        Assert.Equal(4, result.ThrottleLogCount);
        Assert.Equal(new[] { 5, 10, 15, 20 }, result.SentCounts);
        var pauses = PauseIndices(result.Offsets);
        Assert.Contains(5, pauses);
        Assert.Contains(10, pauses);
        Assert.Contains(15, pauses);
        Assert.Contains(20, pauses);
    }

    [Fact]
    public async Task Enabled_cross_job_should_not_pause_before_second_job_first_label()
    {
        // 作业 A 5 张 + 作业 B 5 张：批间计数按作业重置（决议），B 首张发送前不等待
        var result = await RunWorkerAsync(new PrintSettingsDto(true, 5, IntervalMs), 5, 5);

        Assert.Equal(10, result.Offsets.Count);
        // 确定性主通道：全程无批次节流决策（旧「跨作业累计」语义此处会记录 1 次、已发送 5 张）
        Assert.Equal(0, result.ThrottleLogCount);
        Assert.Empty(result.SentCounts);
    }

    [Fact]
    public async Task Enabled_new_job_should_restart_batch_counting_from_zero()
    {
        // 作业 A 5 张 + 作业 B 7 张：B 首张前不等待（计数归零）；B 作业内第 6 张前仍按批 5 等待
        var result = await RunWorkerAsync(new PrintSettingsDto(true, 5, IntervalMs), 5, 7);

        Assert.Equal(12, result.Offsets.Count);
        // 确定性主通道：全程仅 1 次节流决策，且发生在「B 作业内已发送 5 张」时——证明 B 从 0 重新计数
        Assert.Equal(1, result.ThrottleLogCount);
        Assert.Equal(new[] { 5 }, result.SentCounts);
        // 辅助通道仅断言正向 Contains：真实节流延迟（400ms 假时间）必在 B 第 6 张（0 基序号 10）前产生大间隔；
        // 不做 DoesNotContain 反向断言——驱动循环按 500ms 步进推进假时间，调度噪声可能在任意张序产生假间隔
        Assert.Contains(10, PauseIndices(result.Offsets));
    }

    [Fact]
    public async Task Enabled_less_than_one_batch_should_not_pause()
    {
        var result = await RunWorkerAsync(new PrintSettingsDto(true, 5, IntervalMs), 3);

        Assert.Equal(3, result.Offsets.Count);
        Assert.Equal(0, result.ThrottleLogCount);
        Assert.Empty(result.SentCounts);
    }

    [Fact]
    public async Task Enabled_exact_one_batch_should_not_pause()
    {
        // 恰满一批（5 张）：第 5 张是最后一张，之后无下一张 → 不等待
        var result = await RunWorkerAsync(new PrintSettingsDto(true, 5, IntervalMs), 5);

        Assert.Equal(5, result.Offsets.Count);
        Assert.Equal(0, result.ThrottleLogCount);
        Assert.Empty(result.SentCounts);
    }

    private static async Task<WorkerRunResult> RunWorkerAsync(PrintSettingsDto settings, params int[] jobSizes)
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfworker-{Guid.NewGuid():N}.db");
        var store = new SqliteLabelJobStore(dbPath);
        await store.InitializeAsync();
        var queue = new LabelJobQueue(store);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var transport = new FakePrintTransport(TimeSpan.Zero, time);
        var printSettings = new PrintSettings();
        printSettings.Update(settings);
        var logProvider = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory();
        loggerFactory.AddProvider(logProvider);
        var worker = new JobPrintWorker(
            queue,
            new FakeTransportManager(transport),
            loggerFactory.CreateLogger<JobPrintWorker>(),
            printSettings,
            time);
        try
        {
            await worker.StartAsync(CancellationToken.None);

            var jobIds = new List<string>();
            for (var i = 0; i < jobSizes.Length; i++)
            {
                var zpl = Enumerable.Range(0, jobSizes[i]).Select(n => $"^XA^FO0,0^FDjob-{i}-{n}^FS^XZ").ToList();
                var (job, _) = await queue.SubmitAsync($"req-{Guid.NewGuid():N}", zpl);
                jobIds.Add(job.Id);
                if (i < jobSizes.Length - 1)
                {
                    // 保证 CreatedAt 严格递增（假时间推进 1ms 即可），队列按「最旧作业优先」领取
                    time.Advance(TimeSpan.FromMilliseconds(1));
                }
            }

            Assert.True(await WaitForCompletionAsync(queue, jobIds, time), "作业未在真实限时内完成（驱动循环异常）。");
            var throttleMessages = logProvider.Messages.Where(m => m.Contains("批次节流")).ToList();
            var sentCounts = throttleMessages
                .Select(m => Regex.Match(m, "已发送 (\\d+) 张"))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
            return new WorkerRunResult(
                transport.SendOffsetsMs.ToList(),
                throttleMessages.Count,
                sentCounts);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(dbPath);
        }
    }

    /// <summary>驱动循环：推进假时间（节流 / 空转间隔瞬时跨过）+ 真实让步（Worker 线程得以执行 IO）。</summary>
    private static async Task<bool> WaitForCompletionAsync(LabelJobQueue queue, IReadOnlyList<string> jobIds, FakeTimeProvider time)
    {
        // 真实限时仅为安全网：假时间驱动下正常路径毫秒级完成
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromMilliseconds(500));
            var allDone = true;
            foreach (var id in jobIds)
            {
                var job = await queue.GetAsync(id);
                if (job is null || job.Status != LabelJobStatus.Completed)
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone)
            {
                return true;
            }

            await Task.Delay(5);
        }

        return false;
    }

    /// <summary>返回发送间隔 ≥ 阈值的（0 基）发送序号——即「暂停」发生的位置（辅助断言用）。</summary>
    private static int[] PauseIndices(List<double> offsets)
    {
        var indices = new List<int>();
        for (var i = 1; i < offsets.Count; i++)
        {
            if (offsets[i] - offsets[i - 1] >= PauseThresholdMs)
            {
                indices.Add(i);
            }
        }

        return indices.ToArray();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
        catch
        {
            // 清理失败不影响断言
        }
    }

    private sealed record WorkerRunResult(List<double> Offsets, int ThrottleLogCount, List<int> SentCounts);

    /// <summary>记录每次 SendAsync 开始时刻（相对 TimeProvider 起始，假时间轴）的假传输。</summary>
    private sealed class FakePrintTransport(TimeSpan sendDelay, TimeProvider time) : IPrintTransport
    {
        private readonly DateTimeOffset _start = time.GetUtcNow();
        public List<double> SendOffsetsMs { get; } = [];

        public Task SendAsync(string zpl, CancellationToken cancellationToken = default)
        {
            SendOffsetsMs.Add((time.GetUtcNow() - _start).TotalMilliseconds);
            return Task.Delay(sendDelay, time, cancellationToken);
        }
    }

    private sealed class FakeTransportManager : ITransportManager
    {
        private readonly FakePrintTransport _transport;

        public FakeTransportManager(FakePrintTransport transport)
        {
            _transport = transport;
        }

        public TransportConfig CurrentConfig { get; } = new();

        public IPrintTransport CurrentTransport => _transport;

        public string ConfigFilePath => string.Empty;

        public Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default)
            => Task.FromResult(new TransportChangeResult(true, "ok", config));
    }

    /// <summary>内存日志收集器（断言「批次节流」日志条数与已发送张数序列）。</summary>
    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new ListLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class ListLogger : ILogger
    {
        private readonly List<string> _messages;

        public ListLogger(List<string> messages)
        {
            _messages = messages;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }
}
