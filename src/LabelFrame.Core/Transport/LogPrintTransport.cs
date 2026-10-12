namespace LabelFrame.Core.Transport;

/// <summary>
/// 日志传输（模拟打印机）：只记录指令摘要，不把指令内容写入日志（数据量过大）；
/// 图片模式的实际图片由作业层保存 PNG。用于没有真实打印机时的验证。
/// </summary>
public sealed class LogPrintTransport : IPrintTransport, IPrinterStatusProvider, LabelFrame.Core.Transport.Plugins.ITestableTransport
{
    private readonly TextWriter _writer;

    /// <summary>创建日志传输。</summary>
    /// <param name="writer">摘要输出目标（host.log）。</param>
    public LogPrintTransport(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <inheritdoc />
    public Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        _writer.WriteLine($"=== LabelFrame 模拟打印机（Log）：收到 {command.Length} 字节（打印机指令，内容省略；图片模式 PNG 见 print\\{{jobId}} 目录） ===");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> TestAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PrinterStatusInfo(true, IsPaperOut: false, IsPaused: false, "日志模拟在线。"));
}
