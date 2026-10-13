using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 内置传输插件：Core 提供 Log（模拟）与 TCP 9100（跨平台网络打印机）；
/// Windows 驱动（winspool）由 WinHost 以同一接口注册；
/// Zebra 品牌传输已外置为官方插件（labelframe-transport-zebra）。
/// </summary>
public static class BuiltinTransportPlugins
{
    /// <summary>Core 内置插件集合。</summary>
    public static IReadOnlyList<ITransportPlugin> CreateCorePlugins()
        => [new LogTransportPlugin(), new Tcp9100TransportPlugin()];
}

/// <summary>Log 模拟打印插件（默认连接方式：不连接真实打印机，作业按成功处理）。</summary>
public sealed class LogTransportPlugin : ITransportPlugin
{
    /// <inheritdoc />
    public string Id => "log";

    /// <summary>显示名「模拟打印」（选项列表去技术前缀「Log」）；插件 Id 保持 <c>log</c> 不变（存量连接配置兼容）。</summary>
    public string DisplayName => "模拟打印";

    /// <inheritdoc />
    public string Description => "模拟打印：不连接真实打印机，作业按打印成功处理；图片模式的标签图片会保存到本机，便于先确认打印效果。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => [];

    /// <inheritdoc />
    public string Describe(TransportPluginParameters parameters) => "模拟打印";

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
        => new LogPrintTransport(context.HostLog);
}

/// <summary>TCP 9100 网络打印机传输插件。</summary>
public sealed class Tcp9100TransportPlugin : ITransportPlugin
{
    /// <inheritdoc />
    public string Id => "tcp9100";

    /// <inheritdoc />
    public string DisplayName => "网口打印机（TCP 9100）";

    /// <inheritdoc />
    public string Description => "TCP 9100 网络打印机（Zebra 等）：连接打印机 IP 的 9100 端口发送指令，状态查询用 ~HS。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("host", "打印机地址 / IP", TransportParameterType.String, Required: true, Hint: "如 192.168.1.50")
        {
            LabelKey = "plugin.tcp9100.params.host.label",
            HintKey = "plugin.tcp9100.params.host.hint",
        },
        new TransportParameterSpec("port", "端口", TransportParameterType.Int, DefaultValue: "9100", Hint: "默认 9100")
        {
            LabelKey = "plugin.tcp9100.params.port.label",
            HintKey = "plugin.tcp9100.params.port.hint",
        },
        new TransportParameterSpec("timeoutSeconds", "超时（秒）", TransportParameterType.Int, DefaultValue: "10")
        {
            LabelKey = "plugin.tcp9100.params.timeoutSeconds.label",
        },
    };

    /// <inheritdoc />
    public string Describe(TransportPluginParameters parameters) => $"TCP {parameters.GetString("host", "?")}:{parameters.GetInt("port", 9100)}";

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
        => new Tcp9100PrintTransport(
            parameters.GetString("host") ?? throw new ArgumentException("缺少打印机地址（host）。"),
            parameters.GetInt("port", 9100),
            TimeSpan.FromSeconds(parameters.GetInt("timeoutSeconds", 10)));
}
