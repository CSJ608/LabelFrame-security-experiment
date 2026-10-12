using System.Net;
using LabelFrame.Api;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 设备 last_ip 记录 / 迁移、by-ip 查找、targetIp 提交、IP 规范化。
/// 含「同 IP 双设备号」场景：设备号变更后新设备号从同 IP 注册，旧行 last_ip 无人清除——
/// 解析必须命中最近活跃的新行，而非先注册的旧行（FakeTimeProvider 驱动保证时间确定）。
/// </summary>
public class DeviceIpTests
{
    private static SubmitJobRequest CreateRequest(string requestId, string? targetDeviceId = null, string? targetIp = null) => new(
        requestId,
        new TemplateDto(SampleContract, SampleLayout),
        [new LabelDto(new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-00" })],
        TargetDeviceId: targetDeviceId,
        TemplateName: null,
        TargetIp: targetIp);

    private static LabelContract SampleContract { get; } = new()
    {
        Name = "location-label",
        Version = "1.0",
        Fields =
        [
            new LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true },
            new LabelField { Key = "zone", DisplayName = "区域", IsRequired = true },
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

    [Fact]
    public async Task Register_should_record_normalized_last_ip()
    {
        using var db = new TempServer();

        var device = await db.Service.RegisterDeviceAsync("device-1", "一号机", "::ffff:192.168.1.5");

        Assert.Equal("192.168.1.5", device.LastIp);
        var found = await db.Service.FindDeviceByIpAsync("192.168.1.5");
        Assert.NotNull(found);
        Assert.Equal("device-1", found!.DeviceId);
    }

    [Fact]
    public async Task Touch_should_refresh_last_ip()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机", "10.0.0.1");

        await db.Service.TouchDeviceAsync("device-1", DateTimeOffset.UtcNow, "::ffff:10.0.0.2");

        var byOld = await db.Service.FindDeviceByIpAsync("10.0.0.1");
        Assert.Null(byOld);
        var byNew = await db.Service.FindDeviceByIpAsync("10.0.0.2");
        Assert.NotNull(byNew);
        Assert.Equal("10.0.0.2", byNew!.LastIp);
    }

    [Fact]
    public async Task Find_by_ip_should_return_null_when_missing()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机", "192.168.1.5");

        Assert.Null(await db.Service.FindDeviceByIpAsync("192.168.1.99"));
        Assert.Null(await db.Service.FindDeviceByIpAsync("   "));
    }

    [Fact]
    public async Task Find_by_ip_should_prefer_recently_active_device_when_same_ip_reused()
    {
        // 复现路径：设备号 A 在某 IP 注册 → 设备号变更后新设备号 B 从同 IP 注册（旧行 stale、新行活跃）
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 11, 8, 0, 0, TimeSpan.Zero));
        using var db = new TempServer(time);
        await db.Service.RegisterDeviceAsync("device-a", "旧设备号", "192.168.7.77");

        time.Advance(TimeSpan.FromHours(1)); // 旧设备号此后不再心跳（last_seen_at 停滞）
        await db.Service.RegisterDeviceAsync("device-b", "新设备号", "192.168.7.77");
        time.Advance(TimeSpan.FromMinutes(1));

        var found = await db.Service.FindDeviceByIpAsync("192.168.7.77");
        Assert.NotNull(found);
        Assert.Equal("device-b", found!.DeviceId);
    }

    [Fact]
    public async Task Submit_job_target_ip_should_resolve_to_recently_active_device()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 11, 8, 0, 0, TimeSpan.Zero));
        using var db = new TempServer(time);
        await db.Service.RegisterDeviceAsync("device-a", "旧设备号", "192.168.7.77");

        time.Advance(TimeSpan.FromHours(1));
        await db.Service.RegisterDeviceAsync("device-b", "新设备号", "192.168.7.77");
        time.Advance(TimeSpan.FromMinutes(1));

        var job = await db.Service.SubmitJobAsync(CreateRequest("req-ip-reuse", targetIp: "192.168.7.77"));

        Assert.Equal("device-b", job.TargetDeviceId);
    }

    [Fact]
    public async Task Find_by_ip_should_follow_most_recent_heartbeat_when_same_ip()
    {
        // 同 IP 两行并存且都活跃（如 NAT 后两台真机）：解析跟随最近一次心跳的设备
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 11, 8, 0, 0, TimeSpan.Zero));
        using var db = new TempServer(time);
        await db.Service.RegisterDeviceAsync("device-a", "甲机", "192.168.7.77");
        time.Advance(TimeSpan.FromMinutes(1));
        await db.Service.RegisterDeviceAsync("device-b", "乙机", "192.168.7.77");

        Assert.Equal("device-b", (await db.Service.FindDeviceByIpAsync("192.168.7.77"))!.DeviceId);

        // 甲机随后心跳（同 IP）：最近活跃换回甲机
        time.Advance(TimeSpan.FromMinutes(1));
        await db.Service.TouchDeviceAsync("device-a", time.GetUtcNow(), "192.168.7.77");
        Assert.Equal("device-a", (await db.Service.FindDeviceByIpAsync("192.168.7.77"))!.DeviceId);
    }

    [Fact]
    public async Task Submit_job_with_target_ip_should_resolve_device()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机", "192.168.1.10");

        var job = await db.Service.SubmitJobAsync(CreateRequest("req-ip", targetIp: "192.168.1.10"));

        Assert.Equal("device-1", job.TargetDeviceId);
        Assert.Equal("Pending", job.Status);
    }

    [Fact]
    public async Task Submit_job_with_unknown_target_ip_should_fail()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机", "192.168.1.10");

        var exception = await Assert.ThrowsAsync<ServerException>(() => db.Service.SubmitJobAsync(CreateRequest("req-ip-x", targetIp: "192.168.1.99")));

        // 按 IP 未找到拆码 LF_SRV_012（与 LF_SRV_001 设备未注册 {deviceId} 各自单参数键集）
        Assert.Equal(ServerErrorCodes.DeviceNotFoundByIp, exception.Code);
        Assert.Equal(["ip"], exception.Parameters!.Keys.Order().ToList());
    }

    [Fact]
    public async Task Submit_job_with_both_targets_should_prefer_device_id()
    {
        using var db = new TempServer();
        await db.Service.RegisterDeviceAsync("device-1", "一号机", "192.168.1.10");
        await db.Service.RegisterDeviceAsync("device-2", "二号机", "192.168.1.20");

        var job = await db.Service.SubmitJobAsync(CreateRequest("req-both", targetDeviceId: "device-2", targetIp: "192.168.1.10"));

        Assert.Equal("device-2", job.TargetDeviceId);
    }

    [Fact]
    public async Task Submit_job_without_target_should_fail()
    {
        using var db = new TempServer();

        var exception = await Assert.ThrowsAsync<ServerException>(() => db.Service.SubmitJobAsync(CreateRequest("req-none")));

        Assert.Equal(ServerErrorCodes.InvalidRequest, exception.Code);
    }

    [Fact]
    public void Normalize_remote_ip_should_map_ipv4_mapped_ipv6()
    {
        Assert.Equal("192.168.1.5", ServerService.NormalizeRemoteIp(IPAddress.Parse("::ffff:192.168.1.5")));
        Assert.Equal("127.0.0.1", ServerService.NormalizeRemoteIp(IPAddress.Parse("127.0.0.1")));
        Assert.Equal("::1", ServerService.NormalizeRemoteIp(IPAddress.Parse("::1")));
        Assert.Null(ServerService.NormalizeRemoteIp(null));
        Assert.Equal("192.168.1.5", ServerService.NormalizeIpText("::ffff:192.168.1.5"));
        Assert.Null(ServerService.NormalizeIpText("   "));
    }

    [Fact]
    public async Task Initialize_should_migrate_legacy_devices_table_with_last_ip()
    {
        // 先构造旧库（devices 无 last_ip 列），再初始化验证迁移补列且旧数据可读
        var path = Path.Combine(Path.GetTempPath(), $"lfsrv-mig-{Guid.NewGuid():N}.db");
        try
        {
            var legacy = new ServerDb(path);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE devices (
                        id            TEXT PRIMARY KEY,
                        name          TEXT NOT NULL,
                        registered_at TEXT NOT NULL,
                        last_seen_at  TEXT NOT NULL
                    );
                    INSERT INTO devices (id, name, registered_at, last_seen_at)
                    VALUES ('legacy-1', '旧设备', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await legacy.InitializeAsync();

            var legacyDevice = await legacy.GetDeviceAsync("legacy-1");
            Assert.NotNull(legacyDevice);
            Assert.Equal("legacy-1", legacyDevice!.Id);
            Assert.Null(legacyDevice.LastIp);

            await legacy.UpsertDeviceAsync(new Device
            {
                Id = "new-1",
                Name = "新设备",
                RegisteredAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
                LastIp = "192.168.1.9",
            });

            var newDevice = await legacy.GetDeviceAsync("new-1");
            Assert.Equal("192.168.1.9", newDevice!.LastIp);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}