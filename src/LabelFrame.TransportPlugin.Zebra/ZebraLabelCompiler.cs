using System.Globalization;
using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Zebra;

/// <summary>Zebra 原生指令编译器：LabelDocument → 整页自包含 ZPL 文本；纯函数性质（同输入同输出、不访问打印机）。</summary>
public sealed class ZebraLabelCompiler : ILabelCommandCompiler
{
    /// <summary>加粗近似：粗体字体变体映射（不同机型可调整映射表，效果以真机为准）。</summary>
    internal static readonly IReadOnlyDictionary<char, char> BoldFontVariantMap = new Dictionary<char, char>
    {
        ['0'] = '1',
    };

    /// <inheritdoc />
    public Task<LabelCommandCompileResult> CompileAsync(
        LabelDocument document,
        LabelCommandCompileOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.Dpi <= 0)
        {
            return Task.FromResult(Fail($"DPI 必须为正整数（当前 {options.Dpi}）。", null));
        }

        var layout = document.Layout;
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var measurer = new ZebraTextWidthMeasurer(options.Dpi);

        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine(CultureInfo.InvariantCulture, $"^PW{ToDots(layout.WidthMm, options.Dpi)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"^LL{ToDots(layout.HeightMm, options.Dpi)}");
        sb.AppendLine("^CI28");

        foreach (var element in layout.Elements)
        {
            switch (element)
            {
                case LabelRegionElement:
                    continue;
                case LabelTextElement text:
                    var compiled = CompileText(text, document, regions, measurer, options.Dpi);
                    if (compiled.ErrorCode is not null)
                    {
                        return Task.FromResult(compiled);
                    }

                    sb.Append(compiled.Command);
                    break;
                case LabelBarcodeElement barcode:
                    var compiledBarcode = CompileBarcode(barcode, document, regions, options.Dpi);
                    if (compiledBarcode.ErrorCode is not null)
                    {
                        return Task.FromResult(compiledBarcode);
                    }

                    sb.Append(compiledBarcode.Command);
                    break;
                case LabelQrCodeElement qrCode:
                    var compiledQr = CompileQrCode(qrCode, document, regions, options.Dpi);
                    if (compiledQr.ErrorCode is not null)
                    {
                        return Task.FromResult(compiledQr);
                    }

                    sb.Append(compiledQr.Command);
                    break;
                case LabelImageElement image when string.IsNullOrEmpty(image.SourceKey):
                    // SourceKey 空＝未指定：静默跳过不出指令（「未指定＝不存在于打印面」，与两渲染器同口径）；
                    // key 非空的有图元素仍按下方口径显式失败——两条路径语义不同，不可合并
                    continue;
                case LabelImageElement:
                    return Task.FromResult(Fail("含图片元素：打印机内置指令无法原生表示位图元素，可将打印方式切回图片。", null));
                case LabelLineElement:
                    return Task.FromResult(Fail("含线元素：原生指令编译暂不支持，可将打印方式切回图片。", null));
                default:
                    return Task.FromResult(Fail($"含不支持的元素类型：{element.GetType().Name}。", null));
            }
        }

        sb.Append("^XZ");
        return Task.FromResult(new LabelCommandCompileResult(sb.ToString(), null, null, null));
    }

    /// <summary>编译单个文本元素：定位（锚定 / 对齐尽力近似）+ ^A 字体 + ^FD 内容。</summary>
    private static LabelCommandCompileResult CompileText(
        LabelTextElement element,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        ZebraTextWidthMeasurer measurer,
        int dpi)
    {
        string value;
        try
        {
            value = LabelElementContent.Get(element, document.Data);
        }
        catch (ArgumentException ex)
        {
            // 缺字段数据＝防御口径（宿主提交链已先行校验；预期内失败走结果不走异常，勿清理此 catch）
            return Fail(ex.Message, element.SourceKey);
        }

        if (value.Length == 0)
        {
            return new LabelCommandCompileResult(string.Empty, null, null, null);
        }

        foreach (var c in value)
        {
            if (!IsSupportedChar(c))
            {
                return Fail(
                    $"文本值含内置字体无法表示的字符「{c}」（U+{(int)c:X4}）：原生指令模式不支持打印机内置字体未覆盖的字符（如未安装中文字库时的中文）。",
                    element.SourceKey);
            }
        }

        var fontChar = ResolveFontChar(element.FontName);
        if (element.Bold && BoldFontVariantMap.TryGetValue(fontChar, out var boldChar))
        {
            fontChar = boldChar;
        }

        var heightDots = Math.Max(1, ToDots(element.FontHeightMm, dpi)); // ≤0 兜底 1 点（与渲染口径一致）
        var widthFragment = element.FontWidthMm > 0
            ? $",{Math.Max(1, ToDots(element.FontWidthMm, dpi))}"
            : string.Empty;

        var bounds = LabelLayoutResolver.ResolveBounds(element, regions, measurer, value);
        var xMm = bounds.XMm + element.EffectivePaddingHMm;
        var yMm = bounds.YMm + element.EffectivePaddingVMm;

        if (element.WidthMm > 0)
        {
            var measuredMm = measurer.MeasureWidthMm(element, value);
            xMm += Math.Max(0, element.WidthMm - measuredMm) * AlignFactor(element.TextAlign);
        }

        var blockHeightMm = element.HeightMm > 0 ? element.HeightMm : element.FontHeightMm;
        yMm += Math.Max(0, blockHeightMm - element.FontHeightMm) * VerticalFactor(element.VerticalAlign);

        // 超长文本不换行不缩放直接输出（原生模式排版降级为预期常态）——勿加换行 / 截断 / 溢出校验
        var command =
            $"^FO{ToDots(xMm, dpi)},{ToDots(yMm, dpi)}" +
            $"^A{fontChar}N,{heightDots}{widthFragment}" +
            $"^FH^FD{EscapeFieldData(value)}^FS" + Environment.NewLine;
        return new LabelCommandCompileResult(command, null, null, null);
    }

    /// <summary>编译单个 Code 128 条码元素（^BY 模块宽 + ^BC，含字符集校验）。</summary>
    private static LabelCommandCompileResult CompileBarcode(
        LabelBarcodeElement element,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        string value;
        try
        {
            value = LabelElementContent.Get(element, document.Data);
        }
        catch (ArgumentException ex)
        {
            // 缺字段数据＝防御口径（宿主提交链已先行校验；预期内失败走结果不走异常，勿清理此 catch）
            return Fail(ex.Message, element.SourceKey);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            // 空白值跳过（与图片模式渲染口径一致：不绘制空条码）
            return new LabelCommandCompileResult(string.Empty, null, null, null);
        }

        // Code 128 子集 B 仅可打印 ASCII：超出字符集按 LF_ENC_001（编码类）拒绝——与文本元素的 LF_ENC_002（编译类）区分
        foreach (var c in value)
        {
            if (c is < (char)0x20 or > (char)0x7E)
            {
                return Fail(
                    JobErrorCodes.EncodeFailed,
                    $"条码值含 Code 128 字符集无法表示的字符「{c}」（U+{(int)c:X4}）：Code 128 仅支持可打印 ASCII 字符，中文等值请改用二维码元素。",
                    element.SourceKey);
            }
        }

        var bounds = LabelLayoutResolver.ResolveBounds(element, regions);
        var xMm = bounds.XMm + element.EffectivePaddingHMm;
        var yMm = bounds.YMm + element.EffectivePaddingVMm;
        var heightDots = Math.Max(1, ToDots(element.HeightMm, dpi)); // ≤0 兜底 1 点（与文本口径一致）
        var module = Math.Clamp(element.ModuleWidth, 1, 10);
        var interpretation = element.DisplayValue ? 'Y' : 'N';

        var command =
            $"^FO{ToDots(xMm, dpi)},{ToDots(yMm, dpi)}" +
            $"^BY{module},3^BCN,{heightDots},{interpretation},N,N" +
            $"^FH^FD{EscapeBarcodeFieldData(value)}^FS" + Environment.NewLine;
        return new LabelCommandCompileResult(command, null, null, null);
    }

    /// <summary>编译单个二维码元素（^BQ，版本估算 → 放大倍数换算，矩阵按静区居中内缩）。</summary>
    /// <remarks>放大倍数钳位上限 10：官方现行上限 100、旧固件上限 10——取保守值兼容旧机型，超限按 10 输出（符号略小于模板框属预期）。</remarks>
    private static LabelCommandCompileResult CompileQrCode(
        LabelQrCodeElement element,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        string value;
        try
        {
            value = LabelElementContent.Get(element, document.Data);
        }
        catch (ArgumentException ex)
        {
            // 缺字段数据＝防御口径（宿主提交链已先行校验；预期内失败走结果不走异常，勿清理此 catch）
            return Fail(ex.Message, element.SourceKey);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            // 空白值跳过（与图片模式渲染口径一致：不绘制空二维码）
            return new LabelCommandCompileResult(string.Empty, null, null, null);
        }

        var bounds = LabelLayoutResolver.ResolveBounds(element, regions);
        var xDots = ToDots(bounds.XMm + element.EffectivePaddingHMm, dpi);
        var yDots = ToDots(bounds.YMm + element.EffectivePaddingVMm, dpi);
        var sizeDots = Math.Max(1, ToDots(element.SizeMm, dpi));

        var version = ZebraQrCodeMetrics.EstimateVersion(Encoding.UTF8.GetByteCount(value), element.QrEcc);
        var matrixModules = ZebraQrCodeMetrics.MatrixModules(version);
        var totalModules = matrixModules + 2 * Math.Max(0, element.QrMargin);
        var magnification = Math.Clamp(
            (int)Math.Round(sizeDots / (double)totalModules, MidpointRounding.AwayFromZero),
            1,
            10);

        var insetDots = Math.Max(0, (int)Math.Round((sizeDots - matrixModules * (double)magnification) / 2, MidpointRounding.AwayFromZero));
        var eccChar = element.QrEcc.ToString();

        var command =
            $"^FO{xDots + insetDots},{yDots + insetDots}" +
            $"^BQN,2,{magnification},{eccChar}" +
            $"^FH^FD{eccChar}A,{EscapeFieldData(value)}^FS" + Environment.NewLine;
        return new LabelCommandCompileResult(command, null, null, null);
    }

    /// <summary>
    /// Code 128 字段数据转义：在通用 ^FH 转义之上，「>」额外映射为 &gt;0 调用码——
    /// 子集 B 中 > 为子集切换调用前缀，十六进制转义（_3E）还原后的 > 仍会被数据层当作调用前缀，故不可用。
    /// </summary>
    internal static string EscapeBarcodeFieldData(string value)
        => EscapeFieldData(value).Replace(">", ">0", StringComparison.Ordinal);

    /// <summary>内置字体可表示的字符：可打印 ASCII（U+0020..U+007E）+ Latin-1 补充（U+00A0..U+00FF）。</summary>
    internal static bool IsSupportedChar(char c)
        => c is (>= (char)0x20 and <= (char)0x7E) or (>= (char)0xA0 and <= (char)0xFF);

    /// <summary>字体字符解析：合法内置字体字符（A-Z / 0-9）原样，其余回退默认缩放字体 0。</summary>
    internal static char ResolveFontChar(string? fontName)
        => fontName is { Length: 1 } && ZebraBuiltInFontMetrics.IsBuiltInFontChar(fontName[0])
            ? fontName[0]
            : '0';

    /// <summary>
    /// 字段数据转义（^FH 十六进制）：^ / ~ / _ 三类指令控制字符转 _HH；必须先转义 _——
    /// 后转会二次替换破坏已引入的 _5E / _7E 前缀。
    /// </summary>
    internal static string EscapeFieldData(string value)
        => value
            .Replace("_", "_5F", StringComparison.Ordinal)
            .Replace("^", "_5E", StringComparison.Ordinal)
            .Replace("~", "_7E", StringComparison.Ordinal);

    private static LabelCommandCompileResult Fail(string reason, string? fieldKey)
        => new(null, JobErrorCodes.CommandCompileFailed, reason, fieldKey);

    /// <summary>带错误码的失败结果（编译器无法原生表示元素 / 字符等预期内失败）。</summary>
    private static LabelCommandCompileResult Fail(string errorCode, string reason, string? fieldKey)
        => new(null, errorCode, reason, fieldKey);

    private static double AlignFactor(LabelTextAlign align) => align switch
    {
        LabelTextAlign.Left => 0,
        LabelTextAlign.Center => 0.5,
        LabelTextAlign.Right => 1,
        _ => 0,
    };

    private static double VerticalFactor(LabelVerticalAlign align) => align switch
    {
        LabelVerticalAlign.Top => 0,
        LabelVerticalAlign.Middle => 0.5,
        LabelVerticalAlign.Bottom => 1,
        _ => 0.5,
    };

    /// <summary>毫米 → 点，四舍五入（远离零）；与图片模式 ZplImageEncoder 同口径。</summary>
    private static int ToDots(double mm, int dpi)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero));
}
