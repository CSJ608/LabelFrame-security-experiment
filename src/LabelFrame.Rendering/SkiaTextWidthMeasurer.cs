using LabelFrame.Core.Layout;
using SkiaSharp;

namespace LabelFrame.Rendering;

/// <summary>
/// 字宽度量默认实现（Skia）：字型查找与渲染器同源（SkiaTypefaceLookup，含中文回退）
/// 加粗口径一致（Embolden），在参考比例下做矢量度量后换算毫米——结果与渲染 DPI 无关（线性缩放）。
/// 无状态，进程内单例复用。
/// </summary>
public sealed class SkiaTextWidthMeasurer : ITextWidthMeasurer
{
    /// <summary>度量参考比例（像素 / 毫米）：矢量度量线性，取较大比例降低舍入误差。</summary>
    private const double ReferencePxPerMm = 16;

    /// <summary>无状态实现，进程内单例。</summary>
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
