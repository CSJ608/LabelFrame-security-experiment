namespace LabelFrame.WinHost.Tests;

/// <summary>启动失败的用户可读提示：端口占用转中文可行动建议，其余透出原始信息。</summary>
public class StartupFailureMessageTests
{
    [Fact]
    public void 端口占用_转中文提示与配置指引()
    {
        var message = Program.DescribeStartupFailure(
            new Microsoft.AspNetCore.Connections.AddressInUseException("address already in use"),
            "http://127.0.0.1:53960");

        Assert.Contains("端口被占用", message);
        Assert.Contains("http://127.0.0.1:53960", message);
        Assert.Contains("ListenUrl", message);
    }

    [Fact]
    public void 内层端口占用_同样识别()
    {
        var wrapped = new InvalidOperationException("bind failed",
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.AddressAlreadyInUse));

        var message = Program.DescribeStartupFailure(wrapped, "http://127.0.0.1:53960");

        Assert.Contains("端口被占用", message);
    }

    [Fact]
    public void 其他异常_透出原始信息与日志指引()
    {
        var message = Program.DescribeStartupFailure(new InvalidOperationException("boom"), "http://127.0.0.1:53960");

        Assert.Contains("boom", message);
        Assert.Contains("host.log", message);
    }
}
