using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Fake;

/// <summary>fake 传输插件（测试 / PDA 通道验证）：发送内容记录到宿主日志与数据目录落盘，连接测试恒成功、状态恒在线。</summary>
/// <remarks>
/// 保守 API 面约束：Release AOT 宿主内动态加载程序集走 interpreter 解释执行，其可解析面不含宿主
/// AOT 图未引用的 .NET 6+ 新增 BCL API——命中即方法体解析期 MissingMethodException（真机实证）。
/// 故只使用近似 netstandard2.0 级老牌 API；本插件即该约束面的门槛用例（其链路跑通 = 约束面下限成立）。
/// </remarks>
public sealed class FakeTransportPlugin : ITransportPlugin
{
    /// <summary>发送内容落盘文件名（插件数据目录内）。</summary>
    public const string SinkFileName = "fake-transport-sent.txt";

    /// <inheritdoc />
    public string Id => "labelframe-transport-fake";

    /// <inheritdoc />
    public string DisplayName => "假想品牌（通道验证）";

    /// <inheritdoc />
    public string Description => "外置插件通道验证用假想品牌：发送内容记录到日志与数据目录，不连接真实打印机。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("host", "打印机 IP 地址", TransportParameterType.String, Required: true, DefaultValue: "127.0.0.1", Hint: "fake 传输不实际连接，仅参与配置与路由"),
        new TransportParameterSpec("port", "端口", TransportParameterType.Int, DefaultValue: "9100"),
    };

    /// <inheritdoc />
    public string Describe(TransportPluginParameters parameters)
        => $"假想品牌 {parameters.GetString("host", "127.0.0.1")}:{parameters.GetInt("port", 9100)}";

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
        => new FakePrintTransport(
            context,
            parameters.GetString("host", "127.0.0.1"),
            parameters.GetInt("port", 9100));
}

/// <summary>fake 传输：发送写入宿主日志 + 数据目录落盘；连接测试恒成功；状态恒在线。</summary>
public sealed class FakePrintTransport : IPrintTransport, IPrinterStatusProvider, ITestableTransport
{
    private readonly ITransportPluginContext _context;
    private readonly string _host;
    private readonly int _port;

    /// <summary>创建 fake 传输。</summary>
    public FakePrintTransport(ITransportPluginContext context, string host, int port)
    {
        _context = context;
        _host = host;
        _port = port;
    }

    /// <inheritdoc />
    public Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        // 保守 API 面：ArgumentNullException.ThrowIfNull 为 .NET 6+（约束面外）——空判用构造器抛出，CA1510 本地抑制
#pragma warning disable CA1510 // 使用 ArgumentNullException.ThrowIfNull
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }
#pragma warning restore CA1510

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // 落盘留痕：一条发送一行「时间 + 字节数 + 内容」，写失败不影响链路（日志仍有记录）。
            // 换行折叠用链式 Replace 实现——ReplaceLineEndings 为 .NET 6+ API（保守 API 面禁用，勿改写）
            Directory.CreateDirectory(_context.DataDirectory);
            File.AppendAllText(
                Path.Combine(_context.DataDirectory, FakeTransportPlugin.SinkFileName),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{System.Text.Encoding.UTF8.GetByteCount(command)}B\t{command.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ")}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            _context.HostLog.WriteLine($"[FAKE] 发送内容落盘失败（不影响模拟结果）：{ex.Message}");
        }

        _context.HostLog.WriteLine($"[FAKE] 模拟发送到 {_host}:{_port}：{command.Length} 字符。");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> TestAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PrinterStatusInfo(true, false, false, "假想品牌通道正常（未连接真实打印机）。"));
}
