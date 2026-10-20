using LabelFrame.Core.Documents;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// TSPL 指令集传输官方插件（指令集维度单插件——服务 TSC 及全部 TSPL 兼容品牌）：
/// TCP 9100 / USB 免驱动直连双通道 + ESC ! ? 状态查询；native 编译（TEXT / BARCODE / QRCODE / BAR / BOX）
/// 与图片模式 BITMAP 二进制编码（ILabelImageEncoder）双能力面。
/// </summary>
/// <remarks>原生指令模式的文本不支持中文（TSPL 内置字体无汉字字形，需打印机安装中文字库后再议）。</remarks>
public sealed class TsplTransportPlugin : ITransportPlugin, ILabelCommandCompiler, ILabelImageEncoder
{
    private readonly TsplLabelCompiler _compiler = new();
    private readonly TsplImageEncoder _imageEncoder = new();

    /// <inheritdoc />
    public string Id => TransportPluginIdPolicy.TsplPluginId;

    /// <inheritdoc />
    public string DisplayName => "TSPL（TSC 及兼容）";

    /// <inheritdoc />
    public string? DisplayNameKey => "plugin.labelframe-transport-tspl.displayName";

    /// <inheritdoc />
    public string Description => "TSPL 指令集直连（TSC 及 TSPL 兼容品牌）：TCP 9100 / USB 免驱动直连与打印机状态（官方插件）。原生指令模式下文本不支持中文（打印机内置字体无汉字字形），中文请用图片模式。";

    /// <inheritdoc />
    public string? DescriptionKey => "plugin.labelframe-transport-tspl.desc";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec("kind", "连接类型", TransportParameterType.Select, Required: true, DefaultValue: "Tcp", Options: new[]
        {
            new TransportParameterOption("Tcp", "TCP"),
            new TransportParameterOption("Usb", "USB（免驱动直连）") { LabelKey = "plugin.labelframe-transport-tspl.params.kind.opt.Usb" },
        })
        {
            LabelKey = "plugin.labelframe-transport-tspl.params.kind.label",
            Row = 1,
        },
        new TransportParameterSpec("host", "打印机地址 / IP", TransportParameterType.String, Required: true, Hint: "如 192.168.1.50", VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"])
        {
            LabelKey = "plugin.labelframe-transport-tspl.params.host.label",
            HintKey = "plugin.labelframe-transport-tspl.params.host.hint",
            Row = 2,
        },
        new TransportParameterSpec("port", "端口", TransportParameterType.Int, DefaultValue: "9100", VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"])
        {
            LabelKey = "plugin.labelframe-transport-tspl.params.port.label",
            Row = 2,
        },
        new TransportParameterSpec("usbName", "USB 打印机名", TransportParameterType.String, Required: false, Hint: "留空 = 自动发现（逐台探测取第一台可通讯打印机）；可填 VID（如 VID_1203）或设备名", VisibleWhenKey: "kind", VisibleWhenValues: ["Usb"])
        {
            LabelKey = "plugin.labelframe-transport-tspl.params.usbName.label",
            HintKey = "plugin.labelframe-transport-tspl.params.usbName.hint",
            Row = 2,
        },
        new TransportParameterSpec(
            TransportPrintMode.ParameterKey,
            "打印方式",
            TransportParameterType.Select,
            Required: true,
            DefaultValue: TransportPrintMode.Image,
            Options: new[]
            {
                new TransportParameterOption(TransportPrintMode.Image, "图片（默认）") { LabelKey = "plugin.labelframe-transport-tspl.params.printMode.opt.image" },
                new TransportParameterOption(TransportPrintMode.Native, "原生指令") { LabelKey = "plugin.labelframe-transport-tspl.params.printMode.opt.native" },
            },
            Hint: "原生指令模式无预览，效果以真机为准；该模式下文本不支持中文（打印机内置字体无汉字字形），中文请用图片模式")
        {
            LabelKey = "plugin.labelframe-transport-tspl.params.printMode.label",
            HintKey = "plugin.labelframe-transport-tspl.params.printMode.hint",
            Row = 3,
        },
        new TransportParameterSpec(
            LabelImageEncodeOptions.InvertBitsParameterKey,
            "反色处理",
            TransportParameterType.Bool,
            Required: false,
            DefaultValue: "false",
            Hint: "图片打印出现黑色背景时开启本开关恢复（适用于位极性与 TSC 相反的兼容机型）",
            VisibleWhenKey: TransportPrintMode.ParameterKey,
            VisibleWhenValues: [TransportPrintMode.Image])
        {
            LabelKey = "plugin.labelframe-transport-tspl.params.invertImage.label",
            HintKey = "plugin.labelframe-transport-tspl.params.invertImage.hint",
            Row = 3,
        },
        new TransportParameterSpec(LabelMediaSize.WidthParameterKey, "标签纸宽度（毫米）", TransportParameterType.Int, Required: false, Hint: "与实际纸宽一致（如 100），仅供「测试打印」走纸出完整一张；正式打印尺寸由模板决定。留空 = 40")
        {
            LabelKey = "plugin.labelframe-transport-tspl.params.labelWidthMm.label",
            HintKey = "plugin.labelframe-transport-tspl.params.labelWidthMm.hint",
            Row = 4,
        },
        new TransportParameterSpec(LabelMediaSize.HeightParameterKey, "标签纸高度（毫米）", TransportParameterType.Int, Required: false, Hint: "与实际纸高一致（如 80），仅供「测试打印」走纸出完整一张；正式打印尺寸由模板决定。留空 = 20")
        {
            LabelKey = "plugin.labelframe-transport-tspl.params.labelHeightMm.label",
            HintKey = "plugin.labelframe-transport-tspl.params.labelHeightMm.hint",
            Row = 4,
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
            "Usb" => string.IsNullOrWhiteSpace(parameters.GetString("usbName")) ? $"TSPL USB（自动发现）{mode}" : $"TSPL USB {parameters.GetString("usbName")}{mode}",
            _ => $"TSPL TCP {parameters.GetString("host", "?")}:{parameters.GetInt("port", 9100)}{mode}",
        };
    }

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
    {
        var kind = Enum.TryParse<TsplTransportKind>(parameters.GetString("kind"), ignoreCase: true, out var parsed) ? parsed : TsplTransportKind.Tcp;
        return new TsplPrinterTransport(
            kind,
            parameters.GetString("host"),
            parameters.GetInt("port", 9100),
            parameters.GetString("usbName"));
    }

    /// <inheritdoc />
    public Task<LabelCommandCompileResult> CompileAsync(
        LabelDocument document,
        LabelCommandCompileOptions options,
        CancellationToken cancellationToken = default)
        => _compiler.CompileAsync(document, options, cancellationToken);

    /// <inheritdoc />
    public byte[] EncodeImage(LabelBitmap bitmap, double widthMm, double heightMm, int dpi)
        => _imageEncoder.EncodeImage(bitmap, widthMm, heightMm, dpi);

    /// <inheritdoc />
    public byte[] EncodeImage(LabelBitmap bitmap, double widthMm, double heightMm, int dpi, LabelImageEncodeOptions? options)
        => _imageEncoder.EncodeImage(bitmap, widthMm, heightMm, dpi, options);
}
