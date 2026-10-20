using System.Net.Sockets;
using System.Text;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// TSPL 传输实例：TCP 9100 / USB 免驱动直连双通道发送 + ESC ! ? 实时状态查询（带重试）。
/// 逐次建连（发送 / 测试 / 状态各自建连→操作→关闭）；USB 自动发现按「打开＋应答探测」取第一台
/// 可通讯设备；异常统一转中文 InvalidOperationException。
/// </summary>
public sealed class TsplPrinterTransport : IPrintTransport, IPrinterStatusProvider, ITestableTransport
{
    /// <summary>ESC ! ? 实时状态查询指令（TSPL2：回传单字节实时状态）。</summary>
    internal static readonly byte[] StatusQueryCommand = [0x1B, 0x21, 0x3F];

    /// <summary>状态查询重试次数（实测：任务刚结束时可能短暂无应答）。</summary>
    internal const int StatusRetryCount = 3;

    /// <summary>单次读应答超时（毫秒）。</summary>
    internal const int StatusReadTimeoutMs = 500;

    /// <summary>重试间隔（毫秒）。</summary>
    internal const int StatusRetryDelayMs = 200;

    private readonly TsplTransportKind _kind;
    private readonly string _host;
    private readonly int _port;
    private readonly string _usbName;
    private readonly Func<ITsplLink> _openTcpLink;
    private readonly Func<string, ITsplLink> _openUsbLink;
    private readonly Func<IReadOnlyList<UsbPrinterDeviceDiscovery.UsbPrinterDevice>> _listUsbDevices;

    /// <summary>创建 TSPL 传输（生产构造：链路与设备发现走真实实现）。</summary>
    public TsplPrinterTransport(TsplTransportKind kind, string? host = null, int port = 9100, string? usbName = null)
        : this(kind, host, port, usbName, openTcpLink: null, openUsbLink: null, listUsbDevices: null)
    {
    }

    /// <summary>创建 TSPL 传输（注入构造：TCP 开链 / USB 按设备路径开链与设备发现替身注入缝——单测 mock 用）。</summary>
    public TsplPrinterTransport(
        TsplTransportKind kind,
        string? host,
        int port,
        string? usbName,
        Func<ITsplLink>? openTcpLink,
        Func<string, ITsplLink>? openUsbLink,
        Func<IReadOnlyList<UsbPrinterDeviceDiscovery.UsbPrinterDevice>>? listUsbDevices)
    {
        _kind = kind;
        _host = host ?? string.Empty;
        _port = port;
        _usbName = usbName ?? string.Empty;
        _openTcpLink = openTcpLink ?? (() => new TcpTsplLink(_host, _port));
        _openUsbLink = openUsbLink ?? (path => new UsbTsplLink(path));
        _listUsbDevices = listUsbDevices ?? UsbPrinterDeviceDiscovery.ListDevices;

        if (kind == TsplTransportKind.Tcp && string.IsNullOrWhiteSpace(_host))
        {
            throw new ArgumentException("TCP 模式必须指定打印机地址 / IP。", nameof(host));
        }
    }

    /// <inheritdoc />
    public Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SendAsync(Encoding.UTF8.GetBytes(command), cancellationToken);
    }

    /// <inheritdoc />
    public Task SendAsync(byte[] command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            try
            {
                using var link = OpenLink();
                link.Write(command, 10_000);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
            {
                throw new InvalidOperationException($"TSPL 打印机连接失败（{DescribeTarget()}）：{ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"TSPL 打印机发送失败（{DescribeTarget()}）：{ex.Message}", ex);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> TestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            try
            {
                var status = QueryStatusByte();
                return status is null
                    ? $"连接测试失败：TSPL 打印机无应答（{DescribeTarget()}，请检查打印机电源与连接）。"
                    : null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or SocketException or TimeoutException)
            {
                return $"连接测试失败：TSPL 打印机不可达（{DescribeTarget()}，{ex.Message}）。";
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            try
            {
                var statusByte = QueryStatusByte();
                if (statusByte is null)
                {
                    return new PrinterStatusInfo(false, IsPaperOut: false, IsPaused: false, $"TSPL 打印机状态无应答（{DescribeTarget()}）。");
                }

                return ParseStatusByte(statusByte.Value);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or SocketException or TimeoutException)
            {
                return new PrinterStatusInfo(false, IsPaperOut: false, IsPaused: false, $"读取 TSPL 打印机状态失败（{DescribeTarget()}）：{ex.Message}");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 解析 ESC ! ? 状态字节（单字节实时状态，TSPL2 手册位语义：bit0 打印头开 / bit1 卡纸 /
    /// bit2 缺纸 / bit3 色带尽 / bit4 暂停 / bit5 打印中 / bit7 其他错误；错误位任务后锁存——实测 0x04 = 缺纸）。
    /// Message 最小口径只报可行动状态，不扩充。public = 单测位语义直接锚定。
    /// </summary>
    public static PrinterStatusInfo ParseStatusByte(byte status)
    {
        var headOpen = (status & 0x01) != 0;
        var paperJam = (status & 0x02) != 0;
        var paperOut = (status & 0x04) != 0;
        var ribbonOut = (status & 0x08) != 0;
        var paused = (status & 0x10) != 0;
        var otherError = (status & 0x80) != 0;
        var message = headOpen ? "打印头未闭合，请合上打印头。"
            : paperJam ? "卡纸，请开盖清理后重试。"
            : paperOut ? "缺纸，请装纸。"
            : ribbonOut ? "色带已用尽，请更换色带。"
            : otherError ? "打印机报其他错误（0x80），请检查打印机状态。"
            : paused ? "已暂停，按打印机的暂停键恢复。"
            : null;
        return new PrinterStatusInfo(true, paperOut, paused, message);
    }

    /// <summary>查询状态字节：发送 ESC ! ? 后读 1 字节，无应答时重试（合计 StatusRetryCount 次）。</summary>
    private byte? QueryStatusByte()
    {
        for (var attempt = 0; attempt < StatusRetryCount; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(StatusRetryDelayMs);
            }

            using var link = OpenLink();
            link.Write(StatusQueryCommand, StatusReadTimeoutMs);
            var response = link.Read(1, StatusReadTimeoutMs);
            if (response is { Length: > 0 })
            {
                return response[0];
            }
        }

        return null;
    }

    private ITsplLink OpenLink() => _kind switch
    {
        TsplTransportKind.Tcp => _openTcpLink(),
        TsplTransportKind.Usb => OpenUsbLink(),
        _ => throw new InvalidOperationException($"不支持的 TSPL 连接类型：{_kind}。"),
    };

    /// <summary>
    /// 打开 USB 链路：指定名直连匹配设备；留空自动发现——逐台「打开＋ESC ! ? 应答探测」，返回第一台
    /// 有 TSPL 应答的<b>已打开</b>链路（打不开 / 无应答跳过继续；全部失败抛可行动错误并列探测清单——
    /// 多 usbprint 设备并存时不能按枚举序取第一台，非 TSPL 设备须靠应答识别）。
    /// </summary>
    private ITsplLink OpenUsbLink()
    {
        if (!string.IsNullOrWhiteSpace(_usbName))
        {
            return _openUsbLink(ResolveUsbDevicePath());
        }

        var devices = ListUsbDevices();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("未发现 USB 打印机，请检查连接或指定设备名。");
        }

        var failures = new List<string>();
        foreach (var device in devices)
        {
            ITsplLink link;
            try
            {
                link = _openUsbLink(device.DevicePath);
            }
            catch (InvalidOperationException ex)
            {
                failures.Add($"{device.DisplayName} 打不开（{ex.Message}）");
                continue;
            }

            try
            {
                link.Write(StatusQueryCommand, StatusReadTimeoutMs);
                if (link.Read(1, StatusReadTimeoutMs) is { Length: > 0 })
                {
                    return link;
                }

                failures.Add($"{device.DisplayName} 无应答");
            }
            catch (InvalidOperationException)
            {
                failures.Add($"{device.DisplayName} 探测失败");
            }

            link.Dispose();
        }

        throw new InvalidOperationException(
            $"自动发现失败：{devices.Count} 台 USB 打印机均不可通讯（{string.Join("；", failures)}）。请在连接设置「USB 打印机名」显式指定（如 VID_1203）。");
    }

    private IReadOnlyList<UsbPrinterDeviceDiscovery.UsbPrinterDevice> ListUsbDevices()
    {
        try
        {
            return _listUsbDevices();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"枚举 USB 打印机失败：{ex.Message}");
        }
    }

    /// <summary>按指定名解析 USB 设备路径：精确匹配后大小写不敏感包含（实例名 / 路径）；无可行动错误抛 InvalidOperationException。</summary>
    private string ResolveUsbDevicePath()
    {
        var devices = ListUsbDevices();
        var match = devices.FirstOrDefault(d => d.DisplayName.Equals(_usbName, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.DisplayName.Contains(_usbName, StringComparison.OrdinalIgnoreCase)
                || d.DevicePath.Contains(_usbName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new InvalidOperationException(
                $"未找到名称匹配「{_usbName}」的 USB 打印机；可用设备：{UsbPrinterDeviceDiscovery.DescribeDevices(devices)}。");
        }

        return match.DevicePath;
    }

    private string DescribeTarget() => _kind switch
    {
        TsplTransportKind.Tcp => $"{_host}:{_port}",
        TsplTransportKind.Usb => string.IsNullOrWhiteSpace(_usbName) ? "USB（自动发现）" : $"USB:{_usbName}",
        _ => _kind.ToString(),
    };
}
