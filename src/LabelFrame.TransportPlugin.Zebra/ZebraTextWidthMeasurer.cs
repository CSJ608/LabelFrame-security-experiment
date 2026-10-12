using LabelFrame.Core.Layout;

namespace LabelFrame.TransportPlugin.Zebra;

/// <summary>
/// Zebra 内置字体的字宽度量（ITextWidthMeasurer）：供 LabelLayoutResolver 计算区域锚定偏移——
/// 锚定宽度按实际打印字体（打印机内置字体）的估算宽度计算，尽力近似、不强求与图片模式等价。
/// </summary>
public sealed class ZebraTextWidthMeasurer : ITextWidthMeasurer
{
    private readonly int _dpi;

    /// <summary>创建度量器（dpi = 宿主打印配置，默认 203）。</summary>
    public ZebraTextWidthMeasurer(int dpi = ZebraBuiltInFontMetrics.DefaultDpi)
    {
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI 必须为正整数。");
        }

        _dpi = dpi;
    }

    /// <inheritdoc />
    public double MeasureWidthMm(LabelTextElement element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var fontHeightMm = element.FontHeightMm > 0 ? element.FontHeightMm : 1.0 / _dpi * 25.4; // ≤0 兜底 1 点（与渲染口径一致）
        double widthMm;
        if (ZebraBuiltInFontMetrics.BitmapFonts.TryGetValue(ResolveMetricFontChar(element.FontName), out var design))
        {
            // 位图字体：单元格宽（点）× 缩放倍率（请求字高点 ÷ 设计高点）→ 毫米
            var scale = ToDots(fontHeightMm) / (double)design.HeightDots;
            widthMm = value.Length * design.WidthDots * scale / _dpi * 25.4;
        }
        else
        {
            // 缩放字体 0：比例字宽累加 × 字高
            var ratio = 0.0;
            foreach (var c in value)
            {
                ratio += ZebraBuiltInFontMetrics.ScalableCharWidthRatio(c);
            }

            widthMm = ratio * fontHeightMm;
        }

        if (element.FontWidthMm > 0 && element.FontHeightMm > 0 && element.FontWidthMm != element.FontHeightMm)
        {
            // ^A0N,h,w 的 w 相对设计纵横比横向独立缩放：估算按宽高比线性换算
            widthMm *= element.FontWidthMm / element.FontHeightMm;
        }

        return widthMm;
    }

    /// <summary>度量用字体字符：非法 / 未建度量表的字体名回退缩放字体 0（默认字体口径）。</summary>
    private static char ResolveMetricFontChar(string? fontName)
        => fontName is { Length: 1 } && ZebraBuiltInFontMetrics.BitmapFonts.ContainsKey(fontName[0])
            ? fontName[0]
            : '0';

    private int ToDots(double mm)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * _dpi, MidpointRounding.AwayFromZero));
}
