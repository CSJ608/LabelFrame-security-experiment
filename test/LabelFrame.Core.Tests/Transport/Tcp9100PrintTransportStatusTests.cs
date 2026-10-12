using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Tests.Transport;

/// <summary>
/// ~HS 状态解析模拟报文矩阵（按官方 ZPL 指南 ~HS 字段表）：
/// 官方响应为三行逗号分隔字段（每行以 STX 开头、ETX+CR+LF 结尾），
/// 首行第 2 字段 = 缺纸、第 3 字段 = 暂停；矩阵覆盖
/// 正常 / 缺纸 / 暂停 / 截断 / 空响应 / 无响应超时六类场景。
/// </summary>
public class Tcp9100PrintTransportStatusTests
{
    /// <summary>官方报文行包装：STX + 内容 + ETX + CR + LF。</summary>
    private static string Wrap(string content) => $"\x02{content}\x03\r\n";

    /// <summary>构造官方三行 ~HS 报文；首行第 2 字段 = 缺纸位、第 3 字段 = 暂停位。</summary>
    private static string HostStatus(int paperOut, int paused)
        => Wrap($"030,{paperOut},{paused},0810,0,0,0,0,000,0,0,0")
         + Wrap("030,0,0,0810,1,0810,0,0,0,000,0")
         + Wrap("030,0");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void ParseHostStatus_should_map_official_first_line_flags(int paperOutFlag, int pausedFlag)
    {
        var status = Tcp9100PrintTransport.ParseHostStatus(HostStatus(paperOutFlag, pausedFlag));

        Assert.True(status.IsOnline);
        Assert.Equal(paperOutFlag == 1, status.IsPaperOut);
        Assert.Equal(pausedFlag == 1, status.IsPaused);
        if (paperOutFlag == 1)
        {
            Assert.Contains("缺纸", status.Message);
        }
        else if (pausedFlag == 1)
        {
            Assert.Contains("暂停", status.Message);
        }
        else
        {
            Assert.Null(status.Message);
        }
    }

    [Fact]
    public void ParseHostStatus_should_parse_single_line_without_control_chars()
    {
        // 防御性：部分环境抓到的报文可能缺控制字符 / 只有首行，首行字段足够解析
        var status = Tcp9100PrintTransport.ParseHostStatus("030,1,0,0810,0,0,0,0,000,0,0,0\r\n");

        Assert.True(status.IsOnline);
        Assert.True(status.IsPaperOut);
        Assert.False(status.IsPaused);
    }

    [Fact]
    public void ParseHostStatus_should_reject_truncated_first_line()
    {
        // 截断报文：首行字段不足（缺暂停位），不得映射为正常
        var status = Tcp9100PrintTransport.ParseHostStatus(Wrap("030,1"));

        Assert.False(status.IsOnline);
        Assert.False(status.IsPaperOut);
        Assert.False(status.IsPaused);
        Assert.Contains("无法解析", status.Message);
    }

    [Fact]
    public void ParseHostStatus_should_reject_non_boolean_flag_values()
    {
        var status = Tcp9100PrintTransport.ParseHostStatus(Wrap("030,x,y,0810,0,0,0,0,000,0,0,0"));

        Assert.False(status.IsOnline);
        Assert.Contains("无法解析", status.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void ParseHostStatus_should_treat_empty_response_as_abnormal(string rawResponse)
    {
        // 官方声明缺纸 / 耗材用尽 / 打印头打开等状态下打印机可能完全不响应 ~HS——空响应是异常态
        var status = Tcp9100PrintTransport.ParseHostStatus(rawResponse);

        Assert.False(status.IsOnline);
        Assert.False(status.IsPaperOut);
        Assert.False(status.IsPaused);
        Assert.Contains("未返回状态响应", status.Message);
    }

    [Fact]
    public void ParseHostStatus_null_should_throw()
    {
        Assert.Throws<ArgumentNullException>(() => Tcp9100PrintTransport.ParseHostStatus(null!));
    }

    [Fact]
    public async Task GetStatusAsync_should_parse_normal_response_over_socket()
    {
        var status = await GetStatusOverSocketAsync(HostStatus(0, 0));

        Assert.True(status.IsOnline);
        Assert.False(status.IsPaperOut);
        Assert.False(status.IsPaused);
        Assert.Null(status.Message);
    }

    [Fact]
    public async Task GetStatusAsync_should_parse_paper_out_response_over_socket()
    {
        var status = await GetStatusOverSocketAsync(HostStatus(1, 0));

        Assert.True(status.IsOnline);
        Assert.True(status.IsPaperOut);
        Assert.False(status.IsPaused);
        Assert.Contains("缺纸", status.Message);
    }

    [Fact]
    public async Task GetStatusAsync_should_parse_paused_response_over_socket()
    {
        var status = await GetStatusOverSocketAsync(HostStatus(0, 1));

        Assert.True(status.IsOnline);
        Assert.False(status.IsPaperOut);
        Assert.True(status.IsPaused);
        Assert.Contains("暂停", status.Message);
    }

    [Fact]
    public async Task GetStatusAsync_should_treat_closed_connection_without_response_as_abnormal()
    {
        // 服务端接受连接、读走 ~HS 后直接关闭（零字节响应）——异常态，不得映射为正常
        var status = await GetStatusOverSocketAsync(closeWithoutResponse: true);

        Assert.False(status.IsOnline);
        Assert.Contains("未返回状态响应", status.Message);
    }

    [Fact]
    public async Task GetStatusAsync_should_treat_silent_connection_as_timeout_abnormal()
    {
        // 无响应超时：服务端接受连接后保持静默；官方声明缺纸等状态下打印机可能完全不响应——超时绝不映射为正常
        var status = await GetStatusOverSocketAsync(holdSilence: true, timeout: TimeSpan.FromMilliseconds(400));

        Assert.False(status.IsOnline);
        Assert.False(status.IsPaperOut);
        Assert.False(status.IsPaused);
        Assert.Contains("超时", status.Message);
    }

    /// <summary>
    /// 起本地 TCP 模拟打印机：接受连接 → 读 ~HS → 按场景回写 / 关闭 / 保持静默。
    /// </summary>
    private static async Task<PrinterStatusInfo> GetStatusOverSocketAsync(
        string? response = null,
        bool closeWithoutResponse = false,
        bool holdSilence = false,
        TimeSpan? timeout = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        // 先让服务端任务就绪（已进入 Accept）再发起连接，避免 CI 高负载下服务端任务调度延迟导致读取超时
        var serverReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseServer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = Task.Run(async () =>
        {
            serverReady.SetResult(true);
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var buffer = new byte[256];
            _ = await stream.ReadAsync(buffer);
            if (closeWithoutResponse)
            {
                client.Close();
                return;
            }

            if (holdSilence)
            {
                await releaseServer.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return;
            }

            var responseBytes = System.Text.Encoding.ASCII.GetBytes(response ?? string.Empty);
            await stream.WriteAsync(responseBytes);
            await stream.FlushAsync();
        });

        try
        {
            await serverReady.Task;
            var transport = new Tcp9100PrintTransport("127.0.0.1", port, timeout ?? TimeSpan.FromSeconds(15));
            return await transport.GetStatusAsync();
        }
        finally
        {
            releaseServer.TrySetResult(true);
            await serverTask;
            listener.Stop();
        }
    }
}
