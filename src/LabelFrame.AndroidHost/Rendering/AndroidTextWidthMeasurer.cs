using Android.Graphics;
using LabelFrame.Core.Layout;

namespace LabelFrame.AndroidHost.Rendering;

/// <summary>
/// 字宽度量 Android 实现：Android.Graphics Paint 与 AndroidLabelRenderer 同一文本渲染管线
/// （TextSize / FakeBoldText），在参考比例下度量后换算毫米——结果与渲染 DPI 无关（线性缩放）。
/// 无状态，进程内单例复用。
/// </summary>
public sealed class AndroidTextWidthMeasurer : ITextWidthMeasurer
{
    /// <summary>度量参考比例（像素 / 毫米）：取较大比例降低舍入误差。</summary>
    private const double ReferencePxPerMm = 16;

    /// <summary>无状态实现，进程内单例。</summary>
    public static AndroidTextWidthMeasurer Instance { get; } = new();

    /// <inheritdoc />
    public double MeasureWidthMm(LabelTextElement element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        using var paint = new Paint
        {
            TextSize = (float)Math.Max(1, element.FontHeightMm * ReferencePxPerMm),
            FakeBoldText = element.Bold,
        };
        return paint.MeasureText(value) / ReferencePxPerMm;
    }
}
