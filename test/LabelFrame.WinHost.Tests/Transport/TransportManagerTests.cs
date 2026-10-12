using System.Text;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

public class TransportManagerTests
{
    private static (TransportManager Manager, string ConfigPath) Create(string? configFilePath = null, TransportMode defaultMode = TransportMode.Log)
    {
        var options = new HostOptions { Transport = defaultMode, TcpHost = "127.0.0.1", TcpPort = 9100, PrinterName = "Test Printer" };
        var path = configFilePath ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        var manager = TestTransportRegistry.CreateManager(options, path);
        return (manager, path);
    }

    [Fact]
    public async Task ApplyAsync_tcp_without_host_should_fail()
    {
        var (manager, _) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.Tcp, TcpHost = "", TcpPort = 9100 }, testOnly: false);
        Assert.False(result.Ok);
        Assert.Contains("host", result.Message);
        Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
    }

    [Fact]
    public async Task ApplyAsync_windows_driver_without_printer_should_fail()
    {
        var (manager, _) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.WindowsDriver, PrinterName = "" }, testOnly: false);
        Assert.False(result.Ok);
        Assert.Contains("printerName", result.Message);
        Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
    }

    [Fact]
    public async Task ApplyAsync_log_should_succeed_and_persist()
    {
        var (manager, path) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.Log }, testOnly: false);

        Assert.True(result.Ok);
        Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
        Assert.True(File.Exists(path));
        Assert.Contains("Log", File.ReadAllText(path));
    }

    [Fact]
    public async Task ApplyAsync_tcp_unreachable_should_fail_and_keep_log()
    {
        var (manager, path) = Create();
        // 127.0.0.1:1 通常立即拒绝连接；失败应保持原连接（Log）且不持久化
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.Tcp, TcpHost = "127.0.0.1", TcpPort = 1 }, testOnly: false);

        Assert.False(result.Ok);
        Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task TestOnly_should_not_switch_or_persist()
    {
        var (manager, path) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { Mode = TransportMode.Log }, testOnly: true);

        Assert.True(result.Ok);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Startup_should_prefer_persisted_connection()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"Mode":"Tcp","TcpHost":"192.168.1.50","TcpPort":9100}""");
        try
        {
            var (manager, _) = Create(path, defaultMode: TransportMode.Log);
            Assert.Equal(TransportMode.Tcp, manager.CurrentConfig.Mode);
            Assert.Equal("192.168.1.50", manager.CurrentConfig.TcpHost);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Tcp_connection_test_should_require_printer_response()
    {
        // 能连端口 ≠ 打印机：测试必须收到 ~HS 响应才算成功。
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var buffer = new byte[16];
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 3)); // 读 ~HS（3 字节）
                await stream.WriteAsync(Encoding.UTF8.GetBytes("1,0,0,0,0,0,0,0,\n")); // 模拟打印机状态响应
            }
            catch
            {
                // 客户端提前关闭时忽略
            }
        });

        var open = await new Tcp9100PrintTransport("127.0.0.1", port).TestConnectionAsync();
        Assert.True(open, "收到 ~HS 响应的监听应判定连接成功");
        listener.Stop();
        await acceptTask;
    }

    [Fact]
    public async Task Tcp_connection_test_should_fail_when_no_printer_response()
    {
        // 能 accept 但不响应 ~HS 的设备（如误配 IP 上的其它服务）→ 判定失败
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var buffer = new byte[16];
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 3)); // 收 ~HS（3 字节）但不响应
                await Task.Delay(500);
            }
            catch
            {
                // 忽略
            }
        });

        var ok = await new Tcp9100PrintTransport("127.0.0.1", port).TestConnectionAsync();
        Assert.False(ok, "无 ~HS 响应的监听不应判定为打印机");
        listener.Stop();
        await acceptTask;
    }

    [Fact]
    public async Task Tcp_test_message_should_carry_specific_failure_reason()
    {
        // 连接测试失败原因透传——能 accept 但不响应 ~HS 的目标给出具体「无响应」原因，而非泛化失败
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var buffer = new byte[16];
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 3)); // 收 ~HS 但不响应
                await Task.Delay(500);
            }
            catch
            {
                // 忽略
            }
        });

        var message = await new Tcp9100PrintTransport("127.0.0.1", port).TestAsync();
        Assert.NotNull(message);
        Assert.Contains("~HS", message);
        Assert.Contains("无响应", message);
        listener.Stop();
        await acceptTask;
    }

    [Fact]
    public async Task Tcp_test_message_should_carry_unreachable_reason()
    {
        // 不可达地址（已释放端口，连接被拒）给出含目标地址的具体原因
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // 立即释放端口 → 连接被拒

        var message = await new Tcp9100PrintTransport("127.0.0.1", port).TestAsync();
        Assert.NotNull(message);
        Assert.Contains($"127.0.0.1:{port}", message);
    }

    [Fact]
    public void Corrupted_connection_json_should_fallback_to_default_and_log_reason()
    {
        // connection.json 损坏 → 回退默认连接，host.log 留痕原因与回退目标（此前静默回退无痕迹）
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ 不是合法 JSON");
        var hostLog = new System.IO.StringWriter();
        try
        {
            var manager = TestTransportRegistry.CreateManager(configFilePath: path, hostLogWriter: hostLog);
            Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
            var log = hostLog.ToString();
            Assert.Contains("connection.json 解析失败", log);
            Assert.Contains(path, log);
            Assert.Contains("已回退默认连接", log);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unreadable_connection_json_should_fallback_to_default_and_log_reason()
    {
        // 变体：文件被独占锁定（读取 IO 失败）同样回退默认连接并留痕
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"pluginId":"tcp9100","params":{"host":"192.168.1.50","port":"9100"}}""");
        using (var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // 独占打开（FileShare.None）→ TransportManager 的 File.ReadAllText 抛 IO 异常
            var hostLog = new System.IO.StringWriter();
            var manager = TestTransportRegistry.CreateManager(configFilePath: path, hostLogWriter: hostLog);
            Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
            var log = hostLog.ToString();
            Assert.Contains("connection.json 读取失败", log);
            Assert.Contains("已回退默认连接", log);
        }

        File.Delete(path);
    }
}