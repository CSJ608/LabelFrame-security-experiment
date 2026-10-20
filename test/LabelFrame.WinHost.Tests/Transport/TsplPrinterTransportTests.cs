using System.Text;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Tspl;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// TSPL 传输单测（mock 链路，不碰真机）：发送（文本 UTF-8 / 二进制透传）、ESC ! ? 状态语义、
/// 重试、USB 设备发现、连接测试与参数构造。
/// </summary>
public class TsplPrinterTransportTests
{
    /// <summary>假链路：捕获写入字节序列，按写入次数逐条回放读回应答（null = 该次无应答）。</summary>
    private sealed class FakeLink : ITsplLink
    {
        private readonly Queue<byte[]?> _reads;
        public List<byte[]> Writes { get; } = [];

        public FakeLink(params byte[]?[] reads) => _reads = new Queue<byte[]?>(reads);

        public void Write(byte[] buffer, int timeoutMs) => Writes.Add(buffer);

        public byte[]? Read(int maxBytes, int timeoutMs) => _reads.Count > 0 ? _reads.Dequeue() : null;

        public void Dispose()
        {
        }
    }

    private static TsplPrinterTransport CreateTcp(FakeLink link)
        => new(TsplTransportKind.Tcp, "192.168.1.50", 9100, usbName: null, openTcpLink: () => link, openUsbLink: null, listUsbDevices: () => []);

    private static TsplPrinterTransport CreateUsb(
        FakeLink link,
        IReadOnlyList<UsbPrinterDeviceDiscovery.UsbPrinterDevice> devices,
        string? usbName = null)
        => new(TsplTransportKind.Usb, null, 9100, usbName, openTcpLink: null, openUsbLink: _ => link, listUsbDevices: () => devices);

    private static TsplPrinterTransport CreateUsbLinks(
        IReadOnlyList<UsbPrinterDeviceDiscovery.UsbPrinterDevice> devices,
        Func<string, ITsplLink> openUsbLink,
        string? usbName = null)
        => new(TsplTransportKind.Usb, null, 9100, usbName, openTcpLink: null, openUsbLink, listUsbDevices: () => devices);

    private static UsbPrinterDeviceDiscovery.UsbPrinterDevice Device(string displayName, string path)
        => new(displayName, path);

    // ──发送──

    [Fact]
    public async Task SendAsync_text_should_write_utf8_bytes()
    {
        var link = new FakeLink();
        var transport = CreateTcp(link);

        await transport.SendAsync("SIZE 40 mm,20 mm\r\nCLS\r\nPRINT 1\r\n");

        var write = Assert.Single(link.Writes);
        Assert.Equal("SIZE 40 mm,20 mm\r\nCLS\r\nPRINT 1\r\n", Encoding.UTF8.GetString(write));
    }

    [Fact]
    public async Task SendAsync_binary_should_pass_through_bytes()
    {
        var link = new FakeLink();
        var transport = CreateTcp(link);
        var payload = new byte[] { 0x53, 0x49, 0x5A, 0x45, 0x00, 0xFF, 0x0D, 0x0A, 0x80 };

        await transport.SendAsync(payload);

        Assert.Equal(payload, Assert.Single(link.Writes));
    }

    [Fact]
    public void Constructor_tcp_without_host_should_throw()
    {
        var ex = Assert.Throws<ArgumentException>(() => new TsplPrinterTransport(TsplTransportKind.Tcp));
        Assert.Contains("TCP", ex.Message);
    }

    // ──ESC ! ? 状态语义──

    [Theory]
    [InlineData(0x00, true, false, null)]
    [InlineData(0x04, true, true, "缺纸")]
    [InlineData(0x10, true, false, "暂停")]
    [InlineData(0x01, true, false, "打印头")]
    [InlineData(0x02, true, false, "卡纸")]
    [InlineData(0x08, true, false, "色带")]
    public void ParseStatusByte_should_map_tspl2_bits(byte status, bool online, bool paperOut, string? messagePart)
    {
        var info = TsplPrinterTransport.ParseStatusByte(status);
        Assert.Equal(online, info.IsOnline);
        Assert.Equal(paperOut, info.IsPaperOut);
        if (messagePart is null)
        {
            Assert.Null(info.Message);
        }
        else
        {
            Assert.Contains(messagePart, info.Message);
        }
    }

    [Fact]
    public async Task GetStatus_should_query_with_esc_and_retry_on_empty_responses()
    {
        // 前两次无应答（任务后短暂无应答语义）、第三次 0x00 → 在线；共三轮查询
        var link = new FakeLink(null, null, new byte[] { 0x00 });
        var transport = CreateTcp(link);

        var status = await transport.GetStatusAsync();

        Assert.True(status.IsOnline);
        Assert.False(status.IsPaperOut);
        Assert.Equal(3, link.Writes.Count);
        Assert.All(link.Writes, write => Assert.Equal(new byte[] { 0x1B, 0x21, 0x3F }, write));
    }

    [Fact]
    public async Task GetStatus_all_retries_empty_should_report_offline()
    {
        var link = new FakeLink();
        var transport = CreateTcp(link);

        var status = await transport.GetStatusAsync();

        Assert.False(status.IsOnline);
        Assert.Contains("无应答", status.Message);
        Assert.Equal(3 /* StatusRetryCount（internal 常量） */, link.Writes.Count);
    }

    [Fact]
    public async Task GetStatus_paper_out_bit_should_report_paper_out()
    {
        var link = new FakeLink(new byte[] { 0x04 });
        var transport = CreateTcp(link);

        var status = await transport.GetStatusAsync();

        Assert.True(status.IsOnline);
        Assert.True(status.IsPaperOut);
        Assert.Contains("缺纸", status.Message);
    }

    // ──连接测试──

    [Fact]
    public async Task TestAsync_with_response_should_return_null()
    {
        var link = new FakeLink(new byte[] { 0x00 });
        var transport = CreateTcp(link);

        Assert.Null(await transport.TestAsync());
    }

    [Fact]
    public async Task TestAsync_without_response_should_return_failure_message()
    {
        var link = new FakeLink();
        var transport = CreateTcp(link);

        var message = await transport.TestAsync();
        Assert.NotNull(message);
        Assert.Contains("无应答", message);
    }

    // ──USB 设备发现──

    [Fact]
    public async Task Usb_send_without_name_should_pick_first_responding_device()
    {
        // 多 usbprint 设备并存（真机形态：挂驱动的 Zebra 排在 TSC 前）：首台开得了但不应答，
        // 自动发现逐台探测命中 TSC，载荷发往 TSC；Zebra 只收到一次探测查询
        const string zebraPath = @"\\?\usb#vid_0a5f&pid_0168#dmj1#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";
        const string tsplPath = @"\\?\usb#vid_1203&pid_0273#000001#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";
        var zebra = new FakeLink();
        var tspl = new FakeLink(new byte[] { 0x00 });
        var transport = CreateUsbLinks(
            new[] { Device("USB\\VID_0A5F&PID_0168\\DMJ1", zebraPath), Device("USB\\VID_1203&PID_0273\\000001", tsplPath) },
            path => path == tsplPath ? tspl : zebra);

        await transport.SendAsync("PRINT 1\r\n");

        Assert.Equal(2, tspl.Writes.Count); // 探测 ESC ! ? + 载荷
        Assert.Equal(TsplPrinterTransport.StatusQueryCommand, tspl.Writes[0]);
        Assert.Equal(Encoding.UTF8.GetBytes("PRINT 1\r\n"), tspl.Writes[1]);
        Assert.Equal(new[] { TsplPrinterTransport.StatusQueryCommand }, zebra.Writes);
    }

    [Fact]
    public async Task Usb_send_without_name_should_skip_unopenable_device()
    {
        // 首台被驱动占用（开链即抛错，真机 Win32 32/5 形态）：跳过继续探测下一台
        const string zebraPath = @"\\?\usb#vid_0a5f&pid_0168#dmj1#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";
        const string tsplPath = @"\\?\usb#vid_1203&pid_0273#000001#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";
        var tspl = new FakeLink(new byte[] { 0x00 });
        var transport = CreateUsbLinks(
            new[] { Device("USB\\VID_0A5F&PID_0168\\DMJ1", zebraPath), Device("USB\\VID_1203&PID_0273\\000001", tsplPath) },
            path => path == zebraPath
                ? throw new InvalidOperationException("打开 USB 打印机失败：Win32 错误 32，被其他程序占用。")
                : tspl);

        await transport.SendAsync("PRINT 1\r\n");

        Assert.Equal(2, tspl.Writes.Count);
    }

    [Fact]
    public async Task Usb_send_without_name_when_all_devices_fail_should_throw_actionable_error()
    {
        var link = new FakeLink(); // 开得了但全部无应答
        const string zebraPath = @"\\?\usb#vid_0a5f&pid_0168#dmj1#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";
        const string tsplPath = @"\\?\usb#vid_1203&pid_0273#000001#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";
        var transport = CreateUsbLinks(
            new[] { Device("USB\\VID_0A5F&PID_0168\\DMJ1", zebraPath), Device("USB\\VID_1203&PID_0273\\000001", tsplPath) },
            _ => link);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("PRINT 1\r\n"));

        Assert.Contains("自动发现失败", ex.Message);
        Assert.Contains("VID_0A5F", ex.Message); // 探测清单列出各台结果
        Assert.Contains("VID_1203", ex.Message);
        Assert.Contains("显式指定", ex.Message);
    }

    [Fact]
    public async Task Usb_test_without_name_should_pass_when_device_responds()
    {
        // 探测应答 + 状态查询应答各一次（探测消耗一次读回，QueryStatusByte 再查一次）
        var link = new FakeLink(new byte[] { 0x00 }, new byte[] { 0x00 });
        var transport = CreateUsb(
            link,
            new[] { Device("USB\\VID_1203&PID_0273\\000001", @"\\?\usb#vid_1203&pid_0273#000001#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}") });

        Assert.Null(await transport.TestAsync());
    }

    [Fact]
    public async Task Usb_send_without_devices_should_throw_actionable_error()
    {
        var link = new FakeLink();
        var transport = CreateUsb(link, []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("PRINT 1\r\n"));
        Assert.Contains("未发现 USB 打印机", ex.Message);
    }

    [Fact]
    public async Task Usb_send_with_exact_name_should_match_device()
    {
        var link = new FakeLink();
        var devices = new[]
        {
            Device("USB\\VID_0A5F&PID_0168\\DMJ1", "\\\\?\\usb#vid_0a5f#path1"),
            Device("USB\\VID_1203&PID_0273\\000001", "\\\\?\\usb#vid_1203#path2"),
        };
        var transport = CreateUsb(link, devices, usbName: "USB\\VID_1203&PID_0273\\000001");

        await transport.SendAsync("PRINT 1\r\n");

        Assert.Single(link.Writes);
    }

    [Fact]
    public async Task Usb_send_with_partial_vid_name_should_match_by_contains()
    {
        var link = new FakeLink();
        var devices = new[]
        {
            Device("USB\\VID_0A5F&PID_0168\\DMJ1", "\\\\?\\usb#vid_0a5f#path1"),
            Device("USB\\VID_1203&PID_0273\\000001", "\\\\?\\usb#vid_1203#path2"),
        };
        var transport = CreateUsb(link, devices, usbName: "VID_1203");

        await transport.SendAsync("PRINT 1\r\n");

        Assert.Single(link.Writes);
    }

    [Fact]
    public async Task Usb_send_with_unknown_name_should_list_available_devices()
    {
        var link = new FakeLink();
        var devices = new[]
        {
            Device("USB\\VID_0A5F&PID_0168\\DMJ1", "\\\\?\\usb#vid_0a5f#path1"),
        };
        var transport = CreateUsb(link, devices, usbName: "VID_9999");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("PRINT 1\r\n"));
        Assert.Contains("VID_9999", ex.Message);
        Assert.Contains("VID_0A5F", ex.Message); // 可用设备清单列出
    }

    // ──插件构造与描述──

    [Fact]
    public void Create_from_parameters_should_parse_kind()
    {
        var plugin = new TsplTransportPlugin();
        var transport = plugin.Create(
            new TransportPluginParameters(new Dictionary<string, string> { ["kind"] = "Usb", ["usbName"] = "VID_1203" }),
            new TransportPluginContext(TextWriter.Null, Path.GetTempPath()));

        Assert.IsType<TsplPrinterTransport>(transport);
    }

    [Fact]
    public void Describe_should_reflect_kind_and_mode()
    {
        var plugin = new TsplTransportPlugin();
        Assert.Equal("TSPL TCP 10.0.0.9:9100", plugin.Describe(new TransportPluginParameters(new Dictionary<string, string> { ["kind"] = "Tcp", ["host"] = "10.0.0.9" })));
        Assert.Equal("TSPL USB（自动发现）（原生指令）", plugin.Describe(new TransportPluginParameters(new Dictionary<string, string>
        {
            ["kind"] = "Usb",
            ["printMode"] = "native",
        })));
    }
}
