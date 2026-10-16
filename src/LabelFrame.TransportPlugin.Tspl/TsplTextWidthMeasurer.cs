using LabelFrame.Core.Layout;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// TSPL 内置缩放字体的字宽度量（ITextWidthMeasurer）：供 LabelLayoutResolver 计算区域锚定偏移——
/// 锚定宽度按实际打印字体（TSPL2 缩放字体 0，Helvetica 同度量比例字）的估算宽度计算，尽力近似、
/// 不强求与图片模式等价。
/// </summary>
public sealed class TsplTextWidthMeasurer : ITextWidthMeasurer
{
    private readonly int _dpi;

    /// <summary>创建度量器（dpi = 宿主打印配置，默认 203）。</summary>
    public TsplTextWidthMeasurer(int dpi = 203)
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
        var ratio = 0.0;
        foreach (var c in value)
        {
            ratio += TsplBuiltInFontMetrics.ScalableCharWidthRatio(c);
        }

        var widthMm = ratio * fontHeightMm;
        if (element.FontWidthMm > 0 && element.FontHeightMm > 0 && element.FontWidthMm != element.FontHeightMm)
        {
            // TEXT 字体 0 的 x-mult 相对设计纵横比横向独立缩放：估算按宽高比线性换算
            widthMm *= element.FontWidthMm / element.FontHeightMm;
        }

        return widthMm;
    }
}
