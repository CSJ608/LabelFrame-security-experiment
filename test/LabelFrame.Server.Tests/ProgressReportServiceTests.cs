namespace LabelFrame.Server.Tests;

/// <summary>
/// 进度增量上报服务语义：仅 Claimed 接受、计数 max 单调不回退
/// 不改终态字段、不刷新 claimed_at（不干预 Claimed 超时回收计龄）。
/// </summary>
public class ProgressReportServiceTests
{
    private static LabelFrame.Api.TemplateDto SampleTemplate { get; } = new(
        new LabelFrame.Core.Contracts.LabelContract
        {
            Name = "it",
            Version = "1.0",
            Fields = [new LabelFrame.Core.Contracts.LabelField { Key = "code", DisplayName = "编码", IsRequired = true }],
        },
        new LabelFrame.Core.Layout.LabelLayout
        {
            Name = "l",
            ContractName = "it",
            ContractVersion = "1.0",
            WidthMm = 40,
            HeightMm = 20,
            Elements = [new LabelFrame.Core.Layout.LabelTextElement { Literal = "固定", XMm = 1, YMm = 1, FontHeightMm = 3 }],
        });

    private static async Task<(ServerService Service, string JobId)> CreateClaimedJobAsync(
        TempServer server, string deviceId = "device-1", int labels = 3)
    {
        await server.Service.RegisterDeviceAsync(deviceId, "一号机");
        var job = await server.Service.SubmitJobAsync(new LabelFrame.Api.SubmitJobRequest(
            $"req-{Guid.NewGuid():N}",
            SampleTemplate,
            Enumerable.Range(0, labels).Select(i => new LabelFrame.Api.LabelDto(
                new Dictionary<string, string> { ["code"] = $"A-0{i}" })).ToList(),
            TargetDeviceId: deviceId));
        var claimed = await server.Service.ClaimPendingJobsAsync(deviceId);
        return (server.Service, claimed.Single().JobId);
    }

    [Fact]
    public async Task Progress_should_grow_counts_monotonically_without_touching_claimed_at()
    {
        using var server = new TempServer();
        var (service, jobId) = await CreateClaimedJobAsync(server);

        var first = await server.Db.GetJobAsync(jobId);
        Assert.NotNull(first);

        var view = await service.ReportProgressAsync("device-1", jobId, new ReportProgressRequest(1, 0));
        Assert.Equal("Claimed", view.Status);
        Assert.Equal(1, view.CompletedItems);
        Assert.Equal(0, view.FailedItems);

        // 乱序 / 迟到 / 重复上报：取 max，不回退
        await service.ReportProgressAsync("device-1", jobId, new ReportProgressRequest(0, 1));
        view = await service.ReportProgressAsync("device-1", jobId, new ReportProgressRequest(1, 0));
        Assert.Equal(1, view.CompletedItems);
        Assert.Equal(1, view.FailedItems);
        view = await service.ReportProgressAsync("device-1", jobId, new ReportProgressRequest(2, 0));
        Assert.Equal(2, view.CompletedItems);

        // 只描述过程：status / finished_at / claimed_at 均不变
        var after = await server.Db.GetJobAsync(jobId);
        Assert.NotNull(after);
        Assert.Equal(ServerJobStatus.Claimed, after.Status);
        Assert.Null(after.FinishedAt);
        Assert.Equal(first!.ClaimedAt, after!.ClaimedAt);
    }

    [Fact]
    public async Task Progress_on_terminal_should_be_idempotent_noop_and_result_overwrites()
    {
        using var server = new TempServer();
        var (service, jobId) = await CreateClaimedJobAsync(server);

        await service.ReportProgressAsync("device-1", jobId, new ReportProgressRequest(1, 0));
        // result 是唯一终态写入者：绝对值覆盖（小于过程峰值也直接覆盖）
        var final = await service.ReportResultAsync(
            "device-1", jobId, new ReportResultRequest("Completed", 3, 0, null));
        Assert.Equal("Completed", final.Status);
        Assert.Equal(3, final.CompletedItems);

        // 终态后的 progress = 幂等 no-op，返回既有视图且计数不变
        var noop = await service.ReportProgressAsync("device-1", jobId, new ReportProgressRequest(0, 2));
        Assert.Equal("Completed", noop.Status);
        Assert.Equal(3, noop.CompletedItems);
        Assert.Equal(0, noop.FailedItems);
    }

    [Fact]
    public async Task Progress_on_pending_or_wrong_owner_or_missing_job_should_fail()
    {
        using var server = new TempServer();
        await server.Service.RegisterDeviceAsync("device-1", "一号机");
        var pending = await server.Service.SubmitJobAsync(new LabelFrame.Api.SubmitJobRequest(
            $"req-{Guid.NewGuid():N}",
            SampleTemplate,
            [new LabelFrame.Api.LabelDto(new Dictionary<string, string> { ["code"] = "A-01" })],
            TargetDeviceId: "device-1"));

        var ex = await Assert.ThrowsAsync<ServerException>(() =>
            server.Service.ReportProgressAsync("device-1", pending.JobId, new ReportProgressRequest(1, 0)));
        Assert.Equal(ServerErrorCodes.InvalidTransition, ex.Code);

        var (_, claimedId) = await CreateClaimedJobAsync(server, deviceId: "device-2");
        ex = await Assert.ThrowsAsync<ServerException>(() =>
            server.Service.ReportProgressAsync("device-other", claimedId, new ReportProgressRequest(1, 0)));
        Assert.Equal(ServerErrorCodes.NotJobOwner, ex.Code);

        ex = await Assert.ThrowsAsync<ServerException>(() =>
            server.Service.ReportProgressAsync("device-1", "no-such-job", new ReportProgressRequest(1, 0)));
        Assert.Equal(ServerErrorCodes.JobNotFound, ex.Code);
    }
}
