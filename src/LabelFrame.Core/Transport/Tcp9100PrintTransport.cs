using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LabelFrame.Core.Transport;

/// <summary>
/// TCP 9100 打印传输：连接打印机 IP 的 9100 端口并发送指令（Zebra 等网络打印机）。
/// 状态查询：发送 ~HS 主机状态查询并按官方 ZPL 指南字段表解析（缺纸 / 暂停位在首行第 2 / 3 字段）；
/// 无响应 / 超时 / 响应不完整按异常态处理。
/// </summary>
public sealed class Tcp9100PrintTransport : IPrintTransport, IPrinterStatusProvider, LabelFrame.Core.Transport.Plugins.ITestableTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;

    /// <summary>创建 TCP 9100 传输。</summary>
    /// <param name="host">打印机主机 / IP。</param>
    /// <param name="port">端口（默认 9100）。</param>
    /// <param name="timeout">连接与发送超时（默认 10 秒）。</param>
    public Tcp9100PrintTransport(string host, int port = 9100, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1-65535 之间。");
        }

        _host = host;
        _port = port;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    /// <inheritdoc />
    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            await client.ConnectAsync(_host, _port, timeoutCts.Token);
            var payload = System.Text.Encoding.UTF8.GetBytes(command);
            await using var stream = client.GetStream();
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"连接或发送打印机超时（{_host}:{_port}）。");
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"连接打印机失败（{_host}:{_port}）：{ex.Message}", ex);
        }
    }

    /// <summary>ITestableTransport：连接测试（~HS 探测），成功返回 null，失败返回含具体原因的中文错误消息（不再泛化失败）。</summary>
    public async Task<string?> TestAsync(CancellationToken cancellationToken = default)
    {
        var (_, failureReason) = await TestConnectionCoreAsync(cancellationToken);
        return failureReason is null ? null : $"连接测试失败：{failureReason}";
    }

    /// <summary>连接测试：TCP 连接（3 秒超时）+ `~HS` 主机状态探测——收到打印机响应才算成功（能连端口≠打印机就绪）。</summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        => (await TestConnectionCoreAsync(cancellationToken)).Success;

    /// <summary>连接测试核心：成功返回 （true, null）；失败返回 （false, 具体原因）——连接被拒 / 超时 / ~HS 无响应等，供测试结果消息透传。</summary>
    private async Task<(bool Success, string? FailureReason)> TestConnectionCoreAsync(CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            // IP 字面量走 IPAddress 直连路径，避免字符串重载的 DNS 解析差异
            if (System.Net.IPAddress.TryParse(_host, out var address))
            {
                await client.ConnectAsync(address, _port, timeoutCts.Token);
            }
            else
            {
                await client.ConnectAsync(_host, _port, timeoutCts.Token);
            }

            // 打印机探测：发送 ~HS 主机状态查询，收到非空响应才算可达
            await using var stream = client.GetStream();
            var probe = System.Text.Encoding.UTF8.GetBytes("~HS");
            await stream.WriteAsync(probe, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);
            var buffer = new byte[64];
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
            if (read > 0)
            {
                return (true, null);
            }

            return (false, $"已连上 {_host}:{_port}，但打印机对 ~HS 状态探测无响应（目标可能不是打印机）。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, $"连接打印机超时（{_host}:{_port}，3 秒内无响应）。");
        }
        catch (SocketException ex)
        {
            return (false, $"无法连接打印机（{_host}:{_port}）：{ex.SocketErrorCode}——{ex.Message}。");
        }
        catch (IOException ex)
        {
            return (false, $"与打印机通讯失败（{_host}:{_port}）：{ex.Message}。");
        }
        catch (Exception ex)
        {
            return (false, $"连接打印机失败（{_host}:{_port}）：{ex.Message}。");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Zebra ~HS 主机状态响应为三行逗号分隔字段（每行以 STX 开头、ETX+CR+LF 结尾）；
    /// 缺纸 / 暂停位均在首行：第 2 字段 = 缺纸、第 3 字段 = 暂停（官方 ZPL 指南 ~HS 字段表，修正）。
    /// 官方声明缺纸 / 耗材用尽 / 打印头打开 / 回卷器满 / 打印头过热等状态下打印机可能完全不响应 ~HS——
    /// 「无响应 / 超时 / 响应不完整」按异常态处理（离线 + 原因），绝不映射为正常。
    /// </remarks>
    public async Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            await client.ConnectAsync(_host, _port, timeoutCts.Token);
            await using var stream = client.GetStream();
            var payload = System.Text.Encoding.UTF8.GetBytes("~HS");
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var response = new StringBuilder();
            var buffer = new byte[1024];
            while (response.Length < 1024)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
                if (read == 0)
                {
                    break;
                }

                // ~HS 为 ASCII 协议，按块解码追加；读到首行行尾（换行）即足够解析缺纸 / 暂停位
                response.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
                if (response.ToString().Contains('\n'))
                {
                    break;
                }
            }

            return ParseHostStatus(response.ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 一等异常态：官方声明缺纸 / 耗材用尽 / 打印头打开等状态下打印机可能不响应 ~HS，超时不得映射为正常
            return new PrinterStatusInfo(
                false,
                IsPaperOut: false,
                IsPaused: false,
                $"状态查询超时（{_timeout.TotalSeconds:0.#} 秒无响应，{_host}:{_port}）——打印机可能离线、缺纸或打印头打开。");
        }
        catch (SocketException ex)
        {
            return new PrinterStatusInfo(false, IsPaperOut: false, IsPaused: false, $"连接失败：{ex.Message}");
        }
        catch (IOException ex)
        {
            return new PrinterStatusInfo(false, IsPaperOut: false, IsPaused: false, $"与打印机通讯失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 解析 ~HS 主机状态响应（纯函数，供传输与单测共用）。
    /// 官方格式为三行逗号分隔字段（每行以 STX 开头、ETX+CR+LF 结尾），缺纸 = 首行第 2 字段、暂停 = 首行第 3 字段；
    /// 响应为空或字段不足（截断）时返回异常态（离线 + 中文原因），绝不映射为正常。
    /// </summary>
    /// <param name="rawResponse">~HS 原始响应文本（可含 STX / ETX / CR / LF 控制字符）。</param>
    public static PrinterStatusInfo ParseHostStatus(string rawResponse)
    {
        ArgumentNullException.ThrowIfNull(rawResponse);

        // 取第一个非空行（剥离行首 STX 与行尾 ETX / CR 等控制字符）；缺纸 / 暂停位均在首行
        var firstLine = rawResponse
            .Split('\n')
            .Select(line => line.Trim('\r', '\x02', '\x03', ' ', '\t'))
            .FirstOrDefault(line => line.Length > 0)
            ?? string.Empty;

        if (firstLine.Length == 0)
        {
            return new PrinterStatusInfo(
                false,
                IsPaperOut: false,
                IsPaused: false,
                "已连上打印机，但未返回状态响应（缺纸 / 耗材用尽 / 打印头打开等状态下打印机可能不响应状态查询）。");
        }

        var fields = firstLine.Split(',');
        if (fields.Length < 3
            || !TryParseStatusFlag(fields[1], out var paperOut)
            || !TryParseStatusFlag(fields[2], out var paused))
        {
            return new PrinterStatusInfo(
                false,
                IsPaperOut: false,
                IsPaused: false,
                $"状态响应不完整，无法解析（首行仅 {fields.Length} 个字段，期望至少 3 个）。");
        }

        // Message 最小口径：只报缺纸 / 暂停（其余异常位不纳入文案）
        string? message = paperOut ? "缺纸，请装纸。" : paused ? "已暂停，按打印机的暂停键恢复。" : null;
        return new PrinterStatusInfo(true, paperOut, paused, message);
    }

    /// <summary>解析 ~HS 标志位字段（0 / 1），非 0/1 取值视为不可解析。</summary>
    private static bool TryParseStatusFlag(string field, out bool value)
    {
        switch (field.Trim())
        {
            case "1":
                value = true;
                return true;
            case "0":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }
}
