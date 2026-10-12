using LabelFrame.Core.Documents;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Zebra;

/// <summary>Zebra 官方 Link-OS SDK 传输插件（TCP / USB / 驱动统一连接）——外置官方插件（迭代 63，决策 #123）。</summary>
/// <remarks>
/// 插件 Id = <see cref="TransportPluginIdPolicy.ZebraPluginId"/>（labelframe-transport-zebra）；
/// 旧内置时代 id "zebra" 由 WinHost 读取配置时按别名映射（DESIGN §6.8），插件侧不再使用。
/// 迭代 78（#120）：同一插件对象实现 <see cref="ILabelCommandCompiler"/>（品牌级能力，§5.4.1——
/// 与连接实例 / 传输形态无关），并声明连接级打印方式参数 printMode（§5.4.2 决议 2）。
/// 迭代 90（#148）：Description / printMode Hint 补「原生指令模式下二维码（QR）数据不支持中文（仅 ASCII）」
/// 说明（真机实证见 #121：原生 ^CI28 下中文被打印机静默过滤）；编译行为不动（保持现状，未升级 fail-closed）。
/// </remarks>
public sealed class ZebraTransportPlugin : ITransportPlugin, ILabelCommandCompiler
{
    private readonly ZebraLabelCompiler _compiler = new();

    /// <inheritdoc />
    public string Id => TransportPluginIdPolicy.ZebraPluginId;

    /// <inheritdoc />
    public string DisplayName => "Zebra";

    /// <inheritdoc />
    /// <remarks>迭代 90（#148）：说明文限定「原生指令模式」——中文走文本框（图片模式 / 原生 LF_ENC_002 既有拒绝口径）。</remarks>
    public string Description => "Zebra 官方 Link-OS SDK：TCP / USB（自动发现）/ Windows 驱动统一连接与打印机状态（Win10+，官方插件）。原生指令模式下二维码（QR）数据不支持中文（仅 ASCII），中文请用文本框（图片模式）。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("kind", "连接类型", TransportParameterType.Select, Required: true, DefaultValue: "Tcp", Options: new[]
        {
            new TransportParameterOption("Tcp", "TCP"),
            new TransportParameterOption("Usb", "USB（自动发现）"),
            new TransportParameterOption("Driver", "Windows 驱动"),
        }),
        new TransportParameterSpec("host", "打印机地址 / IP", TransportParameterType.String, Required: true, Hint: "如 192.168.1.50", VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"]),
        new TransportParameterSpec("port", "端口", TransportParameterType.Int, DefaultValue: "9100", VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"]),
        new TransportParameterSpec("printerName", "Windows 打印机名", TransportParameterType.String, Required: true, Hint: "如 ZDesigner ZD421-203dpi ZPL", VisibleWhenKey: "kind", VisibleWhenValues: ["Driver"]),
        new TransportParameterSpec("usbName", "Zebra USB 打印机名", TransportParameterType.String, Required: false, Hint: "留空 = 自动发现第一台", VisibleWhenKey: "kind", VisibleWhenValues: ["Usb"]),
        // 打印方式（§5.4.2 决议 2：仅连接级 Select 参数；默认 image，native = 插件编译器产出品牌原生指令）
        new TransportParameterSpec(
            TransportPrintMode.ParameterKey,
            "打印方式",
            TransportParameterType.Select,
            Required: true,
            DefaultValue: TransportPrintMode.Image,
            Options: new[]
            {
                new TransportParameterOption(TransportPrintMode.Image, "图片（默认）"),
                new TransportParameterOption(TransportPrintMode.Native, "原生指令"),
            },
            Hint: "原生指令模式无预览，效果以真机为准；该模式下二维码（QR）数据不支持中文（仅 ASCII），中文请用文本框（图片模式）"),
    };

    /// <inheritdoc />
    public string Describe(TransportPluginParameters parameters)
    {
        var kind = parameters.GetString("kind") ?? "Tcp";
        var mode = TransportPrintMode.Resolve(parameters.GetString(TransportPrintMode.ParameterKey)) == TransportPrintMode.Native
            ? "（原生指令）"
            : string.Empty;
        return kind switch
        {
            "Usb" => string.IsNullOrWhiteSpace(parameters.GetString("usbName")) ? $"Zebra USB（自动发现）{mode}" : $"Zebra USB {parameters.GetString("usbName")}{mode}",
            "Driver" => $"Zebra 驱动 {parameters.GetString("printerName") ?? "?"}{mode}",
            _ => $"Zebra TCP {parameters.GetString("host", "?")}:{parameters.GetInt("port", 9100)}{mode}",
        };
    }

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
    {
        var kind = Enum.TryParse<ZebraTransportKind>(parameters.GetString("kind"), ignoreCase: true, out var parsed) ? parsed : ZebraTransportKind.Tcp;
        return new ZebraPrinterTransport(
            kind,
            parameters.GetString("host") ?? string.Empty,
            parameters.GetInt("port", 9100),
            parameters.GetString("printerName") ?? string.Empty,
            parameters.GetString("usbName") ?? string.Empty);
    }

    /// <inheritdoc />
    /// <remarks>文档编译能力（§5.4.1）：品牌级纯函数变换，不访问打印机；委托无状态 <see cref="ZebraLabelCompiler"/>。</remarks>
    public Task<LabelCommandCompileResult> CompileAsync(
        LabelDocument document,
        LabelCommandCompileOptions options,
        CancellationToken cancellationToken = default)
        => _compiler.CompileAsync(document, options, cancellationToken);
}
