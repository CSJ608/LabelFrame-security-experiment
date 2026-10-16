using System.Globalization;
using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// TSPL 原生指令编译器（TSPL2 基线）：LabelDocument → 整页自包含 TSPL 文本指令；纯函数性质
/// （同输入同输出、不访问打印机）。直接打印形态（SIZE→CLS→元素→PRINT，不下载表单存储）。
/// </summary>
/// <remarks>
/// 元素口径（§5.4.1 裁剪原则——实现不了的不强求）：文本 = TEXT（缩放字体 0，字号按磅值近似）；
/// Code 128 = BARCODE "128"（自动子集）；二维码 = QRCODE（mode A 自动，cell 1..10）；
/// 线 = BAR（水平 / 垂直）；边框（含区域）= BOX——Zebra 原生模式不支持的差异能力。
/// 中文文本显式失败（LF_ENC_002：内置字体无汉字字形，装中文字库前不可表示）；
/// QR 中文经页级 CODEPAGE UTF-8 透传（行为以真机定案，AC-07）。
/// GAP / BLINE 不发送——媒介间隙 / 黑标属耗材属性，交由打印机既有标定；DIRECTION 显式 1 保证确定性。
/// </remarks>
public sealed class TsplLabelCompiler : ILabelCommandCompiler
{
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
            return Task.FromResult(ElementResult.Fail($"DPI 必须为正整数（当前 {options.Dpi}）。", null).ToResult());
        }

        var layout = document.Layout;
        var regions = LabelLayoutResolver.IndexRegions(layout);
        var measurer = new TsplTextWidthMeasurer(options.Dpi);

        var sb = new StringBuilder();
        sb.Append("SIZE ").Append(Mm(layout.WidthMm)).Append(" mm,").Append(Mm(layout.HeightMm)).Append(" mm\r\n");
        sb.Append("DIRECTION 1\r\n");
        sb.Append("CODEPAGE UTF-8\r\n");
        sb.Append("CLS\r\n");

        foreach (var element in layout.Elements)
        {
            var compiled = element switch
            {
                LabelRegionElement region => CompileRegionBorder(region, regions, options.Dpi),
                LabelTextElement text => CompileText(text, document, regions, measurer, options.Dpi),
                LabelBarcodeElement barcode => CompileBarcode(barcode, document, regions, options.Dpi),
                LabelQrCodeElement qrCode => CompileQrCode(qrCode, document, regions, options.Dpi),
                LabelLineElement line => CompileLine(line, options.Dpi),
                LabelImageElement image when string.IsNullOrEmpty(image.SourceKey) => ElementResult.Ok(null), // 未指定＝不存在于打印面（与两渲染器同口径）
                LabelImageElement => ElementResult.Fail("含图片元素：打印机内置指令无法原生表示位图元素，可将打印方式切回图片。", null),
                _ => ElementResult.Fail($"含不支持的元素类型：{element.GetType().Name}。", null),
            };

            if (compiled.ErrorCode is not null)
            {
                return Task.FromResult(compiled.ToResult());
            }

            if (compiled.Command is not null)
            {
                sb.Append(compiled.Command);
            }
        }

        sb.Append("PRINT 1\r\n");
        return Task.FromResult(new LabelCommandCompileResult(sb.ToString(), null, null, null));
    }

    /// <summary>区域元素：仅边框参与原生输出（BOX），无边框即跳过——版式容器自身不出其他指令。</summary>
    private static ElementResult CompileRegionBorder(
        LabelRegionElement element,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
        => ElementResult.Ok(CompileBorder(LabelLayoutResolver.ResolveBounds(element, regions), element.BorderMm, dpi));

    /// <summary>编译单个文本元素：边框（BOX）+ 定位（锚定 / 对齐尽力近似）+ TEXT（缩放字体 0，磅值字号）。</summary>
    private static ElementResult CompileText(
        LabelTextElement element,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        TsplTextWidthMeasurer measurer,
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
            return ElementResult.Fail(ex.Message, element.SourceKey);
        }

        var border = CompileBorder(LabelLayoutResolver.ResolveBounds(element, regions), element.BorderMm, dpi);

        if (value.Length == 0)
        {
            return ElementResult.Ok(border);
        }

        foreach (var c in value)
        {
            if (!IsSupportedChar(c))
            {
                return ElementResult.Fail(
                    $"文本值含内置字体无法表示的字符「{c}」（U+{(int)c:X4}）：原生指令模式的 TSPL 内置字体不含汉字字形，中文文本请将打印方式切回图片（或在打印机安装中文字库后再议）。",
                    element.SourceKey);
            }
        }

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

        // 字体 0（可缩放 TTF）的 x/y 倍率单位为磅（1 pt = 1/72 inch）；宽度未指定随高度保持纵横比
        var yPt = ToPoints(element.FontHeightMm);
        var xPt = element.FontWidthMm > 0 ? ToPoints(element.FontWidthMm) : yPt;
        var xDots = ToDots(xMm, dpi);
        var yDots = ToDots(yMm, dpi);
        var command =
            $"TEXT {xDots},{yDots},\"0\",0,{xPt},{yPt},\"{EscapeContent(value)}\"\r\n";
        if (element.Bold)
        {
            // 加粗近似：同内容偏移 1 点重绘（TSPL 内置字体无粗体变体，裁剪原则下的尽力近似）
            command += $"TEXT {xDots + 1},{yDots},\"0\",0,{xPt},{yPt},\"{EscapeContent(value)}\"\r\n";
        }

        return ElementResult.Ok(border + command);
    }

    /// <summary>编译单个 Code 128 条码元素（BARCODE "128" 自动子集，narrow = 模块宽，含字符集校验）。</summary>
    private static ElementResult CompileBarcode(
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
            return ElementResult.Fail(ex.Message, element.SourceKey);
        }

        var border = CompileBorder(LabelLayoutResolver.ResolveBounds(element, regions), element.BorderMm, dpi);
        if (string.IsNullOrWhiteSpace(value))
        {
            // 空白值跳过（与图片模式渲染口径一致：不绘制空条码）
            return ElementResult.Ok(border);
        }

        // Code 128 子集 B 仅可打印 ASCII：超出字符集按 LF_ENC_001（编码类）拒绝——与文本元素的 LF_ENC_002（编译类）区分
        foreach (var c in value)
        {
            if (c is < (char)0x20 or > (char)0x7E)
            {
                return ElementResult.Fail(
                    JobErrorCodes.EncodeFailed,
                    $"条码值含 Code 128 字符集无法表示的字符「{c}」（U+{(int)c:X4}）：Code 128 仅支持可打印 ASCII 字符，中文等值请改用二维码元素。",
                    element.SourceKey);
            }
        }

        var bounds = LabelLayoutResolver.ResolveBounds(element, regions);
        var xDots = ToDots(bounds.XMm + element.EffectivePaddingHMm, dpi);
        var yDots = ToDots(bounds.YMm + element.EffectivePaddingVMm, dpi);
        var heightDots = Math.Max(1, ToDots(element.HeightMm, dpi)); // ≤0 兜底 1 点（与渲染口径一致）
        var narrow = Math.Clamp(element.ModuleWidth, 1, 10); // Code 128 为连续型码制：narrow 即模块宽（dot），wide 无意义（恒 1）
        var human = element.DisplayValue ? 2 : 0; // 2 = 居中人读行

        var command =
            $"BARCODE {xDots},{yDots},\"128\",{heightDots},{human},0,{narrow},1,\"{EscapeContent(value)}\"\r\n";
        return ElementResult.Ok(border + command);
    }

    /// <summary>编译单个二维码元素（QRCODE，版本估算 → cell width 换算，矩阵按静区居中内缩）。</summary>
    /// <remarks>cell width 钳位 1..10（TSPL2 规格）；中文值经页级 CODEPAGE UTF-8 透传，编码行为以真机定案（AC-07）。</remarks>
    private static ElementResult CompileQrCode(
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
            return ElementResult.Fail(ex.Message, element.SourceKey);
        }

        var border = CompileBorder(LabelLayoutResolver.ResolveBounds(element, regions), element.BorderMm, dpi);
        if (string.IsNullOrWhiteSpace(value))
        {
            // 空白值跳过（与图片模式渲染口径一致：不绘制空二维码）
            return ElementResult.Ok(border);
        }

        var bounds = LabelLayoutResolver.ResolveBounds(element, regions);
        var xDots = ToDots(bounds.XMm + element.EffectivePaddingHMm, dpi);
        var yDots = ToDots(bounds.YMm + element.EffectivePaddingVMm, dpi);
        var sizeDots = Math.Max(1, ToDots(element.SizeMm, dpi));

        var version = TsplQrCodeMetrics.EstimateVersion(Encoding.UTF8.GetByteCount(value), element.QrEcc);
        var matrixModules = TsplQrCodeMetrics.MatrixModules(version);
        var totalModules = matrixModules + 2 * Math.Max(0, element.QrMargin);
        var cellWidth = Math.Clamp(
            (int)Math.Round(sizeDots / (double)totalModules, MidpointRounding.AwayFromZero),
            1,
            10);

        var insetDots = Math.Max(0, (int)Math.Round((sizeDots - matrixModules * (double)cellWidth) / 2, MidpointRounding.AwayFromZero));
        var command =
            $"QRCODE {xDots + insetDots},{yDots + insetDots},{element.QrEcc},{cellWidth},A,0,\"{EscapeContent(value)}\"\r\n";
        return ElementResult.Ok(border + command);
    }

    /// <summary>编译线元素：水平 / 垂直线 = BAR；斜线显式失败（BAR 只能画正交矩形）。</summary>
    private static ElementResult CompileLine(LabelLineElement element, int dpi)
    {
        if (Math.Abs(element.X2Mm - element.XMm) < 0.01 && Math.Abs(element.Y2Mm - element.YMm) < 0.01)
        {
            return ElementResult.Fail("线元素起止点重合，无法绘制。", null);
        }

        var thickness = Math.Max(1, ToDots(element.ThicknessMm, dpi));
        var x1 = ToDots(element.XMm, dpi);
        var y1 = ToDots(element.YMm, dpi);
        var x2 = ToDots(element.X2Mm, dpi);
        var y2 = ToDots(element.Y2Mm, dpi);

        if (Math.Abs(element.Y2Mm - element.YMm) < 0.01)
        {
            // 水平线：宽 = 跨度、高 = 线宽
            return ElementResult.Ok($"BAR {Math.Min(x1, x2)},{y1},{Math.Max(1, Math.Abs(x2 - x1))},{thickness}\r\n");
        }

        if (Math.Abs(element.X2Mm - element.XMm) < 0.01)
        {
            // 垂直线：宽 = 线宽、高 = 跨度
            return ElementResult.Ok($"BAR {x1},{Math.Min(y1, y2)},{thickness},{Math.Max(1, Math.Abs(y2 - y1))}\r\n");
        }

        return ElementResult.Fail("含斜线元素：TSPL BAR 仅支持水平 / 垂直线，可将打印方式切回图片。", null);
    }

    /// <summary>边框编译（BOX 右下角坐标形态）：无边框或宽 / 高非正即跳过（自动宽度文本无边框，与渲染口径一致）。</summary>
    private static string? CompileBorder(ElementBounds bounds, double borderMm, int dpi)
    {
        if (borderMm <= 0 || bounds.WidthMm <= 0 || bounds.HeightMm <= 0)
        {
            return null;
        }

        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var xEnd = ToDots(bounds.XMm + bounds.WidthMm, dpi);
        var yEnd = ToDots(bounds.YMm + bounds.HeightMm, dpi);
        return $"BOX {x},{y},{Math.Max(x + 1, xEnd)},{Math.Max(y + 1, yEnd)},{Math.Max(1, ToDots(borderMm, dpi))}\r\n";
    }

    /// <summary>内容转义：双引号按官方形态改写为 ["]（TSPL 引号内内容的唯一官方转义）。</summary>
    internal static string EscapeContent(string value)
        => value.Replace("\"", "[\"]", StringComparison.Ordinal);

    /// <summary>内置字体可表示的字符：可打印 ASCII（U+0020..U+007E）+ Latin-1 补充（U+00A0..U+00FF，经 UTF-8 编码）。</summary>
    internal static bool IsSupportedChar(char c)
        => c is (>= (char)0x20 and <= (char)0x7E) or (>= (char)0xA0 and <= (char)0xFF);

    /// <summary>单元素编译结果（内部流转形态；空 Command = 无指令但非失败）。</summary>
    private sealed record ElementResult(string? Command, string? ErrorCode, string? ErrorMessage, string? FieldKey)
    {
        public static ElementResult Ok(string? command) => new(command, null, null, null);

        public static ElementResult Fail(string reason, string? fieldKey)
            => new(null, JobErrorCodes.CommandCompileFailed, reason, fieldKey);

        public static ElementResult Fail(string errorCode, string reason, string? fieldKey)
            => new(null, errorCode, reason, fieldKey);

        public LabelCommandCompileResult ToResult() => new(Command, ErrorCode, ErrorMessage, FieldKey);
    }

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

    /// <summary>毫米 → 点，四舍五入（远离零）；与图片模式编码器同口径。</summary>
    private static int ToDots(double mm, int dpi)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero));

    /// <summary>毫米 → 磅（缩放字体 0 的倍率单位，1 pt = 1/72 inch），四舍五入（远离零）、下限 1。</summary>
    private static int ToPoints(double mm)
        => Math.Max(1, (int)Math.Round(mm * 72 / 25.4, MidpointRounding.AwayFromZero));

    /// <summary>SIZE 指令的毫米数（最多三位小数、不变文化）。</summary>
    private static string Mm(double mm)
        => mm.ToString("0.###", CultureInfo.InvariantCulture);
}
