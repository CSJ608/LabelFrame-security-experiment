using LabelFrame.Core.Layout;
using SkiaSharp;

namespace LabelFrame.Rendering;

/// <summary>字宽度量默认实现（Skia）：与渲染器共用同一字型查找与加粗口径（SkiaTypefaceLookup），保证度量结果与实际绘制一致。</summary>
public sealed class SkiaTextWidthMeasurer : ITextWidthMeasurer
{
    /// <summary>度量参考比例（像素 / 毫米）：矢量度量线性换算，取较大比例以降低舍入误差。</summary>
    private const double ReferencePxPerMm = 16;

    public static SkiaTextWidthMeasurer Instance { get; } = new();

    /// <inheritdoc />
    public double MeasureWidthMm(LabelTextElement element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        using var typeface = SkiaTypefaceLookup.CreateTypeface(element.FontFamily, value);
        using var font = new SKFont(typeface, (float)Math.Max(1, element.FontHeightMm * ReferencePxPerMm))
        {
            Embolden = element.Bold,
        };
        return font.MeasureText(value) / ReferencePxPerMm;
    }
}
