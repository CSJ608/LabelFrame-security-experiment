using LabelFrame.Core.Documents;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Zebra;

/// <summary>Zebra 官方 Link-OS SDK 传输插件（TCP / USB / 驱动统一连接）——外置官方插件。</summary>
/// <remarks>原生指令模式下二维码（QR）数据经打印机实测中文被静默过滤（仅 ASCII 可用）——
/// Description 与 printMode Hint 的限制文案即为此设；移除提示前须先让编译器对 QR 中文显式失败。</remarks>
public sealed class ZebraTransportPlugin : ITransportPlugin, ILabelCommandCompiler
{
    private readonly ZebraLabelCompiler _compiler = new();

    /// <inheritdoc />
    public string Id => TransportPluginIdPolicy.ZebraPluginId;

    /// <inheritdoc />
    public string DisplayName => "Zebra";

    /// <inheritdoc />
    public string Description => "Zebra 官方 Link-OS SDK：TCP / USB（自动发现）/ Windows 驱动统一连接与打印机状态（Win10+，官方插件）。原生指令模式下二维码（QR）数据不支持中文（仅 ASCII），中文请用文本框（图片模式）。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("kind", "连接类型", TransportParameterType.Select, Required: true, DefaultValue: "Tcp", Options: new[]
        {
            new TransportParameterOption("Tcp", "TCP"),
            new TransportParameterOption("Usb", "USB（自动发现）") { LabelKey = "plugin.labelframe-transport-zebra.params.kind.opt.Usb" },
            new TransportParameterOption("Driver", "Windows 驱动") { LabelKey = "plugin.labelframe-transport-zebra.params.kind.opt.Driver" },
        })
        {
            LabelKey = "plugin.labelframe-transport-zebra.params.kind.label",
        },
        new TransportParameterSpec("host", "打印机地址 / IP", TransportParameterType.String, Required: true, Hint: "如 192.168.1.50", VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"])
        {
            LabelKey = "plugin.labelframe-transport-zebra.params.host.label",
            HintKey = "plugin.labelframe-transport-zebra.params.host.hint",
        },
        new TransportParameterSpec("port", "端口", TransportParameterType.Int, DefaultValue: "9100", VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"])
        {
            LabelKey = "plugin.labelframe-transport-zebra.params.port.label",
        },
        new TransportParameterSpec("printerName", "Windows 打印机名", TransportParameterType.String, Required: true, Hint: "如 ZDesigner ZD421-203dpi ZPL", VisibleWhenKey: "kind", VisibleWhenValues: ["Driver"])
        {
            LabelKey = "plugin.labelframe-transport-zebra.params.printerName.label",
            HintKey = "plugin.labelframe-transport-zebra.params.printerName.hint",
        },
        new TransportParameterSpec("usbName", "Zebra USB 打印机名", TransportParameterType.String, Required: false, Hint: "留空 = 自动发现第一台", VisibleWhenKey: "kind", VisibleWhenValues: ["Usb"])
        {
            LabelKey = "plugin.labelframe-transport-zebra.params.usbName.label",
            HintKey = "plugin.labelframe-transport-zebra.params.usbName.hint",
        },
        new TransportParameterSpec(
            TransportPrintMode.ParameterKey,
            "打印方式",
            TransportParameterType.Select,
            Required: true,
            DefaultValue: TransportPrintMode.Image,
            Options: new[]
            {
                new TransportParameterOption(TransportPrintMode.Image, "图片（默认）") { LabelKey = "plugin.labelframe-transport-zebra.params.printMode.opt.image" },
                new TransportParameterOption(TransportPrintMode.Native, "原生指令") { LabelKey = "plugin.labelframe-transport-zebra.params.printMode.opt.native" },
            },
            Hint: "原生指令模式无预览，效果以真机为准；该模式下二维码（QR）数据不支持中文（仅 ASCII），中文请用文本框（图片模式）")
        {
            LabelKey = "plugin.labelframe-transport-zebra.params.printMode.label",
            HintKey = "plugin.labelframe-transport-zebra.params.printMode.hint",
        },
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
    public Task<LabelCommandCompileResult> CompileAsync(
        LabelDocument document,
        LabelCommandCompileOptions options,
        CancellationToken cancellationToken = default)
        => _compiler.CompileAsync(document, options, cancellationToken);
}
