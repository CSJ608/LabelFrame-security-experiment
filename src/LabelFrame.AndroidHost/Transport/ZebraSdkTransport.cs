using Android.Content;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;
using Zebra.Sdk.Printer.Discovery;

namespace LabelFrame.AndroidHost.Transport;

/// <summary>Zebra 官方 Link-OS SDK 传输：tcp（默认）/ bluetooth（SPP 按 MAC）/ usb（OTG 自动发现第一台）。
/// 每次发送 / 测试 / 状态查询独立建连，不持有长连接；状态与连接测试走 SDK PrinterStatus 官方语义
/// （GetCurrentStatus），不保留 ~HS 手撕双轨。</summary>
public sealed class ZebraSdkTransport : IPrintTransport, IPrinterStatusProvider, ITestableTransport
{
    /// <summary>tcp 连接类型常量（配置存储值，默认）。</summary>
    public const string ConnectionTypeTcp = "tcp";

    /// <summary>bluetooth 连接类型常量（配置存储值）。</summary>
    public const string ConnectionTypeBluetooth = "bluetooth";

    /// <summary>usb 连接类型常量（配置存储值）。</summary>
    public const string ConnectionTypeUsb = "usb";

    /// <summary>USB 发现等待上限（发现器回调在 Java 线程触发，同步等待须有界）。</summary>
    private static readonly TimeSpan UsbDiscoveryTimeout = TimeSpan.FromSeconds(3);

    private readonly string _connectionType;
    private readonly string _host;
    private readonly int _port;
    private readonly string _bluetoothMac;
    private readonly Context _context;

    private ZebraSdkTransport(string connectionType, string host, int port, string bluetoothMac, Context context)
    {
        _connectionType = connectionType;
        _host = host;
        _port = port;
        _bluetoothMac = bluetoothMac;
        _context = context;
    }

    /// <summary>按配置创建 SDK 传输（存量 tcp_host / tcp_port 配置键沿用，已装设备升级零操作直入）。
    /// 配置缺参数不在此抛错——每次操作时校验并给可行动中文提示，允许先保存后补参数。</summary>
    public static ZebraSdkTransport Create(LabelHostConfig config, Context context) => new(
        NormalizeConnectionType(config.ConnectionType),
        config.TcpHost,
        config.TcpPort,
        config.BluetoothMac,
        context);

    /// <summary>按参数创建 SDK 传输（内置 zebra 插件路径）；语义与 <see cref="Create(LabelHostConfig, Context)"/> 一致。</summary>
    public static ZebraSdkTransport Create(string connectionType, string host, int port, string bluetoothMac, Context context) => new(
        NormalizeConnectionType(connectionType),
        host,
        port,
        bluetoothMac,
        context);

    /// <summary>连接目标的用户可读摘要（静态版：插件 Describe 与配置页共用同一文案）。</summary>
    public static string DescribeTarget(string connectionType, string host, int port, string bluetoothMac) =>
        NormalizeConnectionType(connectionType) switch
        {
            ConnectionTypeBluetooth => $"蓝牙 {bluetoothMac}",
            ConnectionTypeUsb => "USB（自动识别第一台 Zebra 打印机）",
            _ => $"{host}:{port}",
        };

    /// <summary>归一化连接类型：未识别值回退 tcp（存量配置只有 tcp，向前兼容新值）。</summary>
    public static string NormalizeConnectionType(string? value) => value switch
    {
        ConnectionTypeBluetooth => ConnectionTypeBluetooth,
        ConnectionTypeUsb => ConnectionTypeUsb,
        _ => ConnectionTypeTcp,
    };

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
            connection = OpenConnection();
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
            SafeClose(connection);
        }
    }

    /// <summary>连接测试：走 SDK PrinterStatus 语义。成功返回 null；失败返回含目标与具体原因的中文错误消息。</summary>
    public Task<string?> TestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            try
            {
                _ = ReadPrinterStatus();
                return (string?)null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return $"连接测试失败（{DescribeTarget()}）：{ex.Message}";
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>状态走 SDK GetCurrentStatus() 官方布尔属性；Message 最小口径只报缺纸 / 暂停，不扩展。</remarks>
    public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            try
            {
                var status = ReadPrinterStatus();
                string? message = status.isPaperOut
                    ? "缺纸，请装纸。"
                    : status.isPaused ? "已暂停，按打印机的暂停键恢复。" : null;
                return new PrinterStatusInfo(true, status.isPaperOut, status.isPaused, message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new PrinterStatusInfo(false, IsPaperOut: false, IsPaused: false, $"读取打印机状态失败（{DescribeTarget()}）：{ex.Message}");
            }
        }, cancellationToken);
    }

    /// <summary>建立连接并读取 SDK 打印机状态（连接随用随关，不复用）。</summary>
    private PrinterStatus ReadPrinterStatus()
    {
        var connection = OpenConnection();
        try
        {
            var printer = ZebraPrinterFactory.GetInstance(connection);
            return printer.GetCurrentStatus();
        }
        finally
        {
            SafeClose(connection);
        }
    }

    /// <summary>按连接类型建立并打开 SDK 连接（连接参数缺失给可行动中文错误）。</summary>
    private Connection OpenConnection() => _connectionType switch
    {
        ConnectionTypeBluetooth => OpenBluetooth(),
        ConnectionTypeUsb => OpenUsb(),
        _ => OpenTcp(),
    };

    private TcpConnection OpenTcp()
    {
        if (string.IsNullOrWhiteSpace(_host))
        {
            throw new InvalidOperationException("还没填打印机 IP 地址——在「连接打印机」里填写并保存后再试。");
        }

        var connection = new TcpConnection(_host, _port);
        connection.Open();
        return connection;
    }

    private BluetoothConnection OpenBluetooth()
    {
        if (string.IsNullOrWhiteSpace(_bluetoothMac))
        {
            throw new InvalidOperationException("还没填打印机蓝牙地址——在「连接打印机」里选蓝牙并填写地址后再试。");
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(31)
            && _context.CheckSelfPermission(Android.Manifest.Permission.BluetoothConnect)
            != Android.Content.PM.Permission.Granted)
        {
            throw new InvalidOperationException("还没给「附近的设备」权限——打开本应用设置开启权限后，再用蓝牙打印。");
        }

        var connection = new BluetoothConnection(_bluetoothMac);
        connection.Open();
        return connection;
    }

    private Connection OpenUsb()
    {
        // 自动发现锁定第一台 Zebra 设备：发现器为 Java 回调式，同步等待须有界超时
        var found = new DiscoveredPrinter?[1];
        var error = new string?[1];
        using var completed = new ManualResetEventSlim(false);
        UsbDiscoverer.FindPrinters(_context, new InlineDiscoveryHandler(
            printer => found[0] = printer,
            () => completed.Set(),
            message => { error[0] = message; completed.Set(); }));

        if (!completed.Wait(UsbDiscoveryTimeout))
        {
            throw new InvalidOperationException("查找 USB 打印机超时——请重新插一下打印机与 PDA 之间的数据线。");
        }

        if (found[0] is null)
        {
            throw new InvalidOperationException(
                error[0] is null
                    ? "没找到 Zebra USB 打印机——请检查打印机与 PDA 之间的数据线是否插好，换根线或换个 USB 口再试。"
                    : $"查找 USB 打印机失败：{error[0]}");
        }

        if (found[0] is not DiscoveredPrinterUsb usbPrinter)
        {
            throw new InvalidOperationException("查找 USB 打印机失败：发现的设备类型不受支持。");
        }

        var connection = usbPrinter.GetConnection();
        // 首次 USB 连接由 SDK 触发系统授权弹窗；重新插拔后需再次允许
        connection.Open();
        return connection;
    }

    private string DescribeTarget() => _connectionType switch
    {
        ConnectionTypeBluetooth => $"蓝牙 {_bluetoothMac}",
        ConnectionTypeUsb => "USB（自动识别第一台 Zebra 打印机）",
        _ => $"{_host}:{_port}",
    };

    private static void SafeClose(Connection? connection)
    {
        if (connection is null)
        {
            return;
        }

        try
        {
            connection.Close();
        }
        catch
        {
            // 关闭失败不影响发送 / 查询结果
        }
    }

    /// <summary>内联发现回调（Java 线程触发）：把异步发现收敛为同步等待。</summary>
    private sealed class InlineDiscoveryHandler : Java.Lang.Object, DiscoveryHandler
    {
        private readonly Action<DiscoveredPrinter> _found;
        private readonly Action _finished;
        private readonly Action<string> _error;

        public InlineDiscoveryHandler(Action<DiscoveredPrinter> found, Action finished, Action<string> error)
        {
            _found = found;
            _finished = finished;
            _error = error;
        }

        public void FoundPrinter(DiscoveredPrinter printer) => _found(printer);

        public void DiscoveryFinished() => _finished();

        public void DiscoveryError(string message) => _error(message);
    }
}
