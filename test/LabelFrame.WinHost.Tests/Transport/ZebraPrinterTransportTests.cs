using System.Net;
using System.Net.Sockets;
using LabelFrame.TransportPlugin.Zebra;

namespace LabelFrame.WinHost.Tests.Transport;

public class ZebraPrinterTransportTests
{
    [Fact]
    public void Constructor_should_require_tcp_address()
    {
        Assert.Throws<ArgumentException>(() => new ZebraPrinterTransport(ZebraTransportKind.Tcp));
    }

    [Fact]
    public void Constructor_should_require_driver_printer_name()
    {
        Assert.Throws<ArgumentException>(() => new ZebraPrinterTransport(ZebraTransportKind.Driver));
    }

    [Fact]
    public async Task SendAsync_to_unreachable_printer_should_throw_clear_error()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var transport = new ZebraPrinterTransport(ZebraTransportKind.Tcp, "127.0.0.1", port);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("^XA^XZ"));
        Assert.Contains("Zebra", exception.Message);
    }

    // 以下两项锚定 SDK 5.0.3685 升级后的行为口径：失败消息含目标与具体原因（/ 不泛化）

    [Fact]
    public async Task TestAsync_to_unreachable_printer_should_return_failure_with_target()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var transport = new ZebraPrinterTransport(ZebraTransportKind.Tcp, "127.0.0.1", port);

        var failure = await transport.TestAsync();

        Assert.NotNull(failure);
        Assert.Contains("127.0.0.1", failure);
    }

    [Fact]
    public async Task GetStatusAsync_to_unreachable_printer_should_be_offline_with_reason()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var transport = new ZebraPrinterTransport(ZebraTransportKind.Tcp, "127.0.0.1", port);

        var status = await transport.GetStatusAsync();

        Assert.False(status.IsOnline);
        Assert.False(status.IsPaperOut);
        Assert.False(status.IsPaused);
        Assert.Contains("127.0.0.1", status.Message);
    }
}