using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Tests.Transport;

public class LogPrintTransportTests
{
    [Fact]
    public async Task SendAsync_should_write_summary_not_payload()
    {
        var writer = new StringWriter();
        var transport = new LogPrintTransport(writer);

        await transport.SendAsync(new string('A', 4096));

        var output = writer.ToString();
        // Log 只记录摘要不写完整指令（^GF 数据量大）
        Assert.StartsWith("=== LabelFrame 模拟打印机（Log）", output);
        Assert.Contains("4096", output);
        Assert.DoesNotContain("AAAA", output);
    }

    [Fact]
    public async Task GetStatusAsync_should_report_online()
    {
        var transport = new LogPrintTransport(TextWriter.Null);
        var status = await transport.GetStatusAsync();
        Assert.True(status.IsOnline);
    }
}