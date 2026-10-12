using System.Text.Json;
using LabelFrame.Api;
using LabelFrame.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 终态回调单元测试（FakeTimeProvider + 假发送器驱动，时间与投递确定）：
/// 投递成功（载荷 schema）、Failed / Expired / 失联回收三终态无漏报、指数退避重试、死信、
/// 重启恢复（持久化）、非法 scheme 提交拒绝、无回调作业零开销。
/// </summary>
public sealed class JobCallbackTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero));
    private readonly TempServer _temp;
    private readonly FakeSender _sender = new();
    private readonly JobCallbackDeliveryService _delivery;

    public JobCallbackTests()
    {
        _temp = new TempServer(_time);
        _delivery = new JobCallbackDeliveryService(_temp.Db, _time, _sender, NullLogger<JobCallbackDeliveryService>.Instance);
    }

    // ---- ：投递成功 + 载荷 schema ----

    [Fact]
    public async Task Completed_job_should_be_delivered_with_payload_schema()
    {
        var job = await SubmitClaimAndReportAsync("req-cb-1", callbackUrl: "http://127.0.0.1:9/cb", reportStatus: "Completed", completedItems: 2, totalLabels: 2);

        Assert.Equal(1, await _delivery.ScanOnceAsync());
        Assert.Single(_sender.Sent);

        var sent = _sender.Sent[0];
        Assert.Equal("http://127.0.0.1:9/cb", sent.Url);
        using var payload = JsonDocument.Parse(sent.Payload);
        var root = payload.RootElement;
        Assert.Equal(job.JobId, root.GetProperty("jobId").GetString());
        Assert.Equal("req-cb-1", root.GetProperty("requestId").GetString());
        Assert.Equal("Completed", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("totalItems").GetInt32());
        Assert.Equal(2, root.GetProperty("completedItems").GetInt32());
        Assert.Equal(0, root.GetProperty("failedItems").GetInt32());

        // completedAt = 作业终态时间（回报落库的 finished_at）
        var stored = (await _temp.Db.GetJobAsync(job.JobId))!;
        Assert.Equal(
            stored.FinishedAt,
            DateTimeOffset.Parse(root.GetProperty("completedAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture));

        // 视图透出投递状态（前半）
        var view = await _temp.Service.GetJobAsync(job.JobId);
        Assert.Equal("Delivered", view.CallbackStatus);
        Assert.Equal(1, view.CallbackAttempts);
        Assert.Null(view.CallbackLastError);
    }

    // ---- ：三种终态无漏报 ----

    [Fact]
    public async Task Failed_job_should_deliver_with_status_and_error()
    {
        var job = await SubmitClaimAndReportAsync("req-cb-fail", "http://127.0.0.1:9/cb", reportStatus: "Failed", completedItems: 1, failedItems: 1, error: "打印机离线", totalLabels: 2);

        await _delivery.ScanOnceAsync();

        var root = JsonDocument.Parse(_sender.Sent.Single().Payload).RootElement;
        Assert.Equal("Failed", root.GetProperty("status").GetString());
        Assert.Equal("打印机离线", root.GetProperty("errorMessage").GetString());
        Assert.Equal(1, root.GetProperty("completedItems").GetInt32());
        Assert.Equal(1, root.GetProperty("failedItems").GetInt32());
        Assert.Equal(job.JobId, root.GetProperty("jobId").GetString());
    }

    [Fact]
    public async Task Expired_job_should_be_registered_and_delivered_by_expiration_scan()
    {
        var options = new ServerOptions { PendingJobTtlHours = 1 };
        var service = new ServerService(_temp.Db, options: options, timeProvider: _time);
        await service.RegisterDeviceAsync("dev", "一号机");
        await service.SubmitJobAsync(CreateRequest("req-exp", "dev", "http://127.0.0.1:9/cb"));

        // 超 TTL 未投递 → 过期扫描置 Expired（三处终态转移点之二）
        _time.Advance(TimeSpan.FromHours(2));
        var scanner = new PendingJobExpirationService(_temp.Db, options, _time, NullLogger<PendingJobExpirationService>.Instance);
        Assert.Equal(1, await scanner.ScanOnceAsync());

        Assert.Equal(1, await _delivery.ScanOnceAsync());
        var root = JsonDocument.Parse(_sender.Sent.Single().Payload).RootElement;
        Assert.Equal("Expired", root.GetProperty("status").GetString());
        Assert.Contains("已放弃", root.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task Host_lost_claimed_job_should_be_registered_and_delivered_by_timeout_scan()
    {
        var options = new ServerOptions();
        var service = new ServerService(_temp.Db, options: options, timeProvider: _time);
        await service.RegisterDeviceAsync("dev", "一号机");
        await service.SubmitJobAsync(CreateRequest("req-lost", "dev", "http://127.0.0.1:9/cb"));
        var claimed = Assert.Single(await service.ClaimPendingJobsAsync("dev"));

        // 领取后宿主失联超时（默认 30 分钟）→ 回收扫描置 Failed（三处终态转移点之三）
        await BackdateClaimedAtAsync("req-lost", minutes: 31);
        var scanner = new ClaimedJobTimeoutService(_temp.Db, options, _time, NullLogger<ClaimedJobTimeoutService>.Instance);
        Assert.Equal(1, await scanner.ScanOnceAsync());

        Assert.Equal(1, await _delivery.ScanOnceAsync());
        var root = JsonDocument.Parse(_sender.Sent.Single().Payload).RootElement;
        Assert.Equal("Failed", root.GetProperty("status").GetString());
        Assert.Contains(ServerErrorCodes.HostLostTimeout, root.GetProperty("errorMessage").GetString());
        Assert.Equal(claimed.JobId, root.GetProperty("jobId").GetString());
    }

    // ---- ：失败退避重试与死信 ----

    [Fact]
    public async Task Failed_delivery_should_backoff_exponentially()
    {
        var job = await SubmitClaimAndReportAsync("req-retry", "http://127.0.0.1:9/cb");
        _sender.Respond = _ => "回调端点返回 HTTP 500。";

        // 第 1 次失败：退避 30 秒后重试；未到期不再尝试
        Assert.Equal(1, await _delivery.ScanOnceAsync());
        var row = await _temp.Db.GetJobCallbackAsync(job.JobId);
        Assert.Equal(JobCallbackStatus.Pending, row!.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromSeconds(30), row.NextRetryAt);
        Assert.Equal(0, await _delivery.ScanOnceAsync());

        // 第 2 次失败：退避翻倍至 1 分钟
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await _delivery.ScanOnceAsync());
        row = await _temp.Db.GetJobCallbackAsync(job.JobId);
        Assert.Equal(2, row!.Attempts);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromMinutes(1), row.NextRetryAt);

        // 第 3 次失败：退避 2 分钟；视图可见末次错误
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await _delivery.ScanOnceAsync());
        row = await _temp.Db.GetJobCallbackAsync(job.JobId);
        Assert.Equal(3, row!.Attempts);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromMinutes(2), row.NextRetryAt);
        Assert.Contains("HTTP 500", row.LastError);

        var view = await _temp.Service.GetJobAsync(job.JobId);
        Assert.Equal("Pending", view.CallbackStatus);
        Assert.Equal(3, view.CallbackAttempts);
        Assert.Contains("HTTP 500", view.CallbackLastError);
    }

    [Fact]
    public async Task Delivery_should_dead_letter_after_max_attempts()
    {
        var job = await SubmitClaimAndReportAsync("req-dead", "http://127.0.0.1:9/cb");
        _sender.Respond = _ => "回调端点不可达：连接被拒绝。";

        // 至多 5 次尝试，之后死信不再重试
        for (var attempt = 1; attempt <= JobCallbackDeliveryPolicy.MaxAttempts; attempt++)
        {
            Assert.Equal(1, await _delivery.ScanOnceAsync());
            if (attempt < JobCallbackDeliveryPolicy.MaxAttempts)
            {
                var next = (await _temp.Db.GetJobCallbackAsync(job.JobId))!.NextRetryAt;
                _time.Advance(next - _time.GetUtcNow());
            }
        }

        var row = await _temp.Db.GetJobCallbackAsync(job.JobId);
        Assert.Equal(JobCallbackStatus.DeadLetter, row!.Status);
        Assert.Equal(JobCallbackDeliveryPolicy.MaxAttempts, row.Attempts);
        Assert.Contains("连接被拒绝", row.LastError);
        Assert.Equal(JobCallbackDeliveryPolicy.MaxAttempts, _sender.Sent.Count);

        // 死信后推进任意时长都不再投递
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, await _delivery.ScanOnceAsync());
        Assert.Equal(JobCallbackDeliveryPolicy.MaxAttempts, _sender.Sent.Count);

        var view = await _temp.Service.GetJobAsync(job.JobId);
        Assert.Equal("DeadLetter", view.CallbackStatus);
    }

    // ---- ：重启恢复（投递状态持久化，重启后继续投递） ----

    [Fact]
    public async Task Pending_delivery_should_survive_service_restart()
    {
        var job = await SubmitClaimAndReportAsync("req-restart", "http://127.0.0.1:9/cb");
        _sender.Respond = _ => "回调端点不可达：连接被拒绝。";
        Assert.Equal(1, await _delivery.ScanOnceAsync());

        // 模拟进程重启：同一数据库上重建投递服务与发送器（内存态全部丢失）
        var sender2 = new FakeSender();
        var delivery2 = new JobCallbackDeliveryService(_temp.Db, _time, sender2, NullLogger<JobCallbackDeliveryService>.Instance);

        // 未到退避时间不投递；到期后由新实例继续投递成功
        Assert.Equal(0, await delivery2.ScanOnceAsync());
        var next = (await _temp.Db.GetJobCallbackAsync(job.JobId))!.NextRetryAt;
        _time.Advance(next - _time.GetUtcNow());
        Assert.Equal(1, await delivery2.ScanOnceAsync());

        var row = await _temp.Db.GetJobCallbackAsync(job.JobId);
        Assert.Equal(JobCallbackStatus.Delivered, row!.Status);
        Assert.Equal(2, row.Attempts);
        Assert.Single(sender2.Sent);
        Assert.Contains("req-restart", sender2.Sent[0].Payload);
    }

    // ---- ：非法 scheme 提交即拒（作业不入队） ----

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://127.0.0.1/cb")]
    [InlineData("example.com/cb")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Invalid_callback_url_should_be_rejected_without_creating_job(string callbackUrl)
    {
        await _temp.Service.RegisterDeviceAsync("dev", "一号机");

        var ex = await Assert.ThrowsAsync<ServerException>(() =>
            _temp.Service.SubmitJobAsync(CreateRequest("req-bad", "dev", callbackUrl)));

        // callbackUrl 校验拆出专属码 LF_SRV_013（原 LF_SRV_002，单码单参数键集）
        Assert.Equal(ServerErrorCodes.InvalidCallbackUrl, ex.Code);
        Assert.Contains("callbackUrl", ex.Message);
        Assert.Null(await _temp.Db.GetJobByRequestIdAsync("req-bad"));
        Assert.Empty(await _temp.Db.ListJobsAsync());
    }

    [Fact]
    public async Task Https_and_uppercase_scheme_should_be_accepted()
    {
        await _temp.Service.RegisterDeviceAsync("dev", "一号机");
        await _temp.Service.SubmitJobAsync(CreateRequest("req-ok-1", "dev", "https://cb.example.com/hook"));
        await _temp.Service.SubmitJobAsync(CreateRequest("req-ok-2", "dev", "HTTP://127.0.0.1:9/CB"));

        Assert.NotNull(await _temp.Db.GetJobByRequestIdAsync("req-ok-1"));
        Assert.NotNull(await _temp.Db.GetJobByRequestIdAsync("req-ok-2"));
    }

    // ---- 无回调作业零开销 + 登记幂等 ----

    [Fact]
    public async Task Job_without_callback_should_have_no_delivery_row()
    {
        var job = await SubmitClaimAndReportAsync("req-plain", callbackUrl: null);

        Assert.Null(await _temp.Db.GetJobCallbackAsync(job.JobId));
        Assert.Equal(0, await _delivery.ScanOnceAsync());
        Assert.Empty(_sender.Sent);

        var view = await _temp.Service.GetJobAsync(job.JobId);
        Assert.Null(view.CallbackStatus);
        Assert.Null(view.CallbackAttempts);
        Assert.Null(view.CallbackLastError);
    }

    [Fact]
    public async Task Replayed_report_and_heal_scan_should_not_duplicate_delivery()
    {
        var job = await SubmitClaimAndReportAsync("req-idem", "http://127.0.0.1:9/cb");

        // 幂等重放回报（终态作业直接返回）+ 多轮扫描自愈登记：投递仍只成功一次
        await _temp.Service.ReportResultAsync("dev", job.JobId, new ReportResultRequest("Completed", 2, 0, null));
        await _delivery.ScanOnceAsync();
        await _delivery.ScanOnceAsync();

        Assert.Single(_sender.Sent);
        var row = await _temp.Db.GetJobCallbackAsync(job.JobId);
        Assert.Equal(JobCallbackStatus.Delivered, row!.Status);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _temp.Dispose();
    }

    private async Task<ServerJobView> SubmitClaimAndReportAsync(
        string requestId,
        string? callbackUrl,
        string reportStatus = "Completed",
        int completedItems = 1,
        int failedItems = 0,
        string? error = null,
        int totalLabels = 1)
    {
        await _temp.Service.RegisterDeviceAsync("dev", "一号机");
        var request = CreateRequest(requestId, "dev", callbackUrl, totalLabels);
        var submitted = await _temp.Service.SubmitJobAsync(request);
        var claimed = Assert.Single(await _temp.Service.ClaimPendingJobsAsync("dev"));
        return await _temp.Service.ReportResultAsync("dev", claimed.JobId, new ReportResultRequest(reportStatus, completedItems, failedItems, error));
    }

    private static SubmitJobRequest CreateRequest(string requestId, string deviceId, string? callbackUrl, int totalLabels = 1) => new(
        requestId,
        new TemplateDto(SampleContract, SampleLayout),
        Enumerable.Range(0, totalLabels).Select(_ => new LabelDto(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" })).ToList(),
        TargetDeviceId: deviceId,
        CallbackUrl: callbackUrl);

    private async Task BackdateClaimedAtAsync(string requestId, int minutes)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_temp.Path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE server_jobs SET claimed_at = $time WHERE request_id = $requestId;";
        command.Parameters.AddWithValue("$time", LabelFrame.Core.Data.SqliteSupport.Format(_time.GetUtcNow().AddMinutes(-minutes)));
        command.Parameters.AddWithValue("$requestId", requestId);
        await command.ExecuteNonQueryAsync();
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

    /// <summary>假发送器：按尝试序号返回成败；记录 URL 与载荷 JSON。</summary>
    private sealed class FakeSender : IJobCallbackSender
    {
        public List<(string Url, string Payload)> Sent { get; } = [];

        public Func<int, string?>? Respond { get; set; }

        public Task<JobCallbackSendResult> SendAsync(string url, string payloadJson, CancellationToken cancellationToken)
        {
            Sent.Add((url, payloadJson));
            var error = Respond?.Invoke(Sent.Count);
            return Task.FromResult(new JobCallbackSendResult(error is null, error));
        }
    }
}
