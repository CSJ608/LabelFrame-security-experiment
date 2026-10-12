using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;
using Zebra.Sdk.Printer.Discovery;

namespace LabelFrame.TransportPlugin.Zebra;

/// <summary>Zebra Link-OS SDK 传输：TCP / USB / Windows 驱动统一连接发送 ZPL；异常统一转中文 InvalidOperationException。</summary>
public sealed class ZebraPrinterTransport : IPrintTransport, IPrinterStatusProvider, ITestableTransport
{
    private readonly ZebraTransportKind _kind;
    private readonly string _address;
    private readonly int _port;
    private readonly string _printerName;
    private readonly string _usbName;

    /// <summary>创建 Zebra SDK 传输。</summary>
    public ZebraPrinterTransport(
        ZebraTransportKind kind,
        string? address = null,
        int port = 9100,
        string? printerName = null,
        string? usbName = null)
    {
        _kind = kind;
        _address = address ?? string.Empty;
        _port = port;
        _printerName = printerName ?? string.Empty;
        _usbName = usbName ?? string.Empty;

        if (kind == ZebraTransportKind.Tcp && string.IsNullOrWhiteSpace(_address))
        {
            throw new ArgumentException("TCP 模式必须指定打印机地址（LABELFRAME_TCP_HOST）。", nameof(address));
        }

        if (kind == ZebraTransportKind.Driver && string.IsNullOrWhiteSpace(_printerName))
        {
            throw new ArgumentException("驱动模式必须指定打印机名（LABELFRAME_PRINTER）。", nameof(printerName));
        }
    }

    /// <inheritdoc />
    public Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => SendCore(command), cancellationToken);
    }

    private void SendCore(string command)
    {
        Connection? connection = null;
        try
        {
            connection = CreateConnection();
            connection.Open();
            connection.Write(System.Text.Encoding.UTF8.GetBytes(command));
        }
        catch (ConnectionException ex)
        {
            throw new InvalidOperationException($"Zebra 打印机连接失败（{DescribeTarget()}）：{ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Zebra 打印机发送失败（{DescribeTarget()}）：{ex.Message}", ex);
        }
        finally
        {
            if (connection is not null)
            {
                try
                {
                    connection.Close();
                }
                catch
                {
                    // 关闭失败不影响发送结果
                }
            }
        }
    }

    private Connection CreateConnection() => _kind switch
    {
        ZebraTransportKind.Tcp => new TcpConnection(_address, _port),
        ZebraTransportKind.Usb => string.IsNullOrWhiteSpace(_usbName)
            ? DiscoverUsbConnection()
            : new UsbConnection(_usbName),
        ZebraTransportKind.Driver => new DriverPrinterConnection(_printerName),
        _ => throw new InvalidOperationException($"不支持的 Zebra 连接类型：{_kind}。"),
    };

    private static Connection DiscoverUsbConnection()
    {
        var printers = UsbDiscoverer.GetZebraUsbPrinters();
        if (printers is null || printers.Count == 0)
        {
            throw new InvalidOperationException("未发现 Zebra USB 打印机，请检查连接或配置 ZebraUsbName。");
        }

        return printers[0].GetConnection();
    }

    /// <summary>ITestableTransport：连接测试，失败消息含打印目标与排查提示。</summary>
    public Task<string?> TestAsync(CancellationToken cancellationToken = default)
        => TestConnectionAsync(cancellationToken).ContinueWith(
            t => t.Result ? null : $"连接测试失败：Zebra 打印机不可达（{DescribeTarget()}，请检查连接方式与地址）。",
            cancellationToken);

    /// <summary>连接测试：~HS 主机状态探测——收到打印机响应才算就绪（能连端口 ≠ 打印机）。</summary>
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            Connection? connection = null;
            try
            {
                connection = CreateConnection();
                connection.Open();
                var response = connection.SendAndWaitForResponse(
                    System.Text.Encoding.UTF8.GetBytes("~HS"),
                    2000,
                    256,
                    "~HS 无响应");
                return response is { Length: > 0 };
            }
            catch
            {
                return false;
            }
            finally
            {
                if (connection is not null)
                {
                    try
                    {
                        connection.Close();
                    }
                    catch
                    {
                        // 忽略关闭异常
                    }
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => GetStatusCore(), cancellationToken);
    }

    private PrinterStatusInfo GetStatusCore()
    {
        Connection? connection = null;
        try
        {
            connection = CreateConnection();
            connection.Open();
            if (_kind == ZebraTransportKind.Driver)
            {
                // 驱动（winspool）路径设计上无法读回打印机状态，维持「默认在线」（设计裁剪非缺陷）
                return new PrinterStatusInfo(true, IsPaperOut: false, IsPaused: false, "驱动模式无法读回打印机状态（默认在线）。");
            }

            // Message 最小口径只报缺纸 / 暂停，不扩充
            var printer = ZebraPrinterFactory.GetInstance(connection);
            var status = printer.GetCurrentStatus();
            var paperOut = status.isPaperOut;
            var paused = status.isPaused;
            string? message = paperOut ? "缺纸，请装纸。" : paused ? "已暂停，按打印机的暂停键恢复。" : null;
            return new PrinterStatusInfo(true, paperOut, paused, message);
        }
        catch (ConnectionException ex)
        {
            return new PrinterStatusInfo(false, IsPaperOut: false, IsPaused: false, $"读取 Zebra 打印机状态失败（{DescribeTarget()}）：{ex.Message}");
        }
        finally
        {
            if (connection is not null)
            {
                try
                {
                    connection.Close();
                }
                catch
                {
                    // 忽略关闭异常
                }
            }
        }
    }

    private string DescribeTarget() => _kind switch
    {
        ZebraTransportKind.Tcp => $"{_address}:{_port}",
        ZebraTransportKind.Usb => string.IsNullOrWhiteSpace(_usbName) ? "USB（自动发现）" : $"USB:{_usbName}",
        ZebraTransportKind.Driver => $"驱动:{_printerName}",
        _ => _kind.ToString(),
    };
}
