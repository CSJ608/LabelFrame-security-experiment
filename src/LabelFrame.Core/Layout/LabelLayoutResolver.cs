namespace LabelFrame.Core.Layout;

/// <summary>元素解析后的边界（毫米）。</summary>
public readonly record struct ElementBounds(double XMm, double YMm, double WidthMm, double HeightMm);

/// <summary>版式解析：索引区域（格子），并按区域 + 对齐计算元素实际位置与尺寸——各渲染方与原生指令编译方共用。</summary>
public static class LabelLayoutResolver
{
    /// <summary>索引区域元素（按 Id）。</summary>
    public static IReadOnlyDictionary<string, LabelRegionElement> IndexRegions(LabelLayout layout)
        => layout.Elements.OfType<LabelRegionElement>().ToDictionary(r => r.Id, StringComparer.Ordinal);

    /// <summary>解析元素边界：未锚定区域时返回绝对坐标 + 自然尺寸。</summary>
    public static ElementBounds ResolveBounds(LabelElement element, IReadOnlyDictionary<string, LabelRegionElement> regions)
        => ResolveBounds(element, regions, textMeasurer: null, textValue: null);

    /// <summary>
    /// 解析元素边界（含区域锚定）。自动宽度文本锚定时：块宽仍为区域全宽（块内 TextAlign / 边框 / 裁剪语义不变），
    /// 水平锚定偏移按「实测文本宽度 + 2×有效水平内边距（夹取到区域宽）」计算；未提供度量器或文本值时回退既有几何。
    /// 垂直锚定：Y / Height 即锚定框（自动高度时 = 字高框）——渲染方必须在该框内绘制，不得换用更高的兜底框后再块内居中。
    /// </summary>
    public static ElementBounds ResolveBounds(
        LabelElement element,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        ITextWidthMeasurer? textMeasurer,
        string? textValue)
    {
        double width;
        double height;
        switch (element)
        {
            case LabelTextElement text:
                width = text.WidthMm;
                height = text.HeightMm > 0 ? text.HeightMm : text.FontHeightMm;
                break;
            case LabelBarcodeElement barcode:
                width = barcode.HeightMm * 2.5;
                height = barcode.HeightMm;
                break;
            case LabelQrCodeElement qrCode:
                width = qrCode.SizeMm;
                height = qrCode.SizeMm;
                break;
            case LabelImageElement image:
                width = image.WidthMm;
                height = image.HeightMm;
                break;
            case LabelRegionElement regionElement:
                width = regionElement.WidthMm;
                height = regionElement.HeightMm;
                break;
            default:
                return new ElementBounds(element.XMm, element.YMm, 0, 0);
        }

        if (string.IsNullOrEmpty(element.RegionId) || !regions.TryGetValue(element.RegionId, out var region))
        {
            return new ElementBounds(element.XMm, element.YMm, width, height);
        }

        var isAutoWidthText = element is LabelTextElement && width <= 0;
        if (isAutoWidthText)
        {
            width = region.WidthMm - element.PaddingMm * 2;
        }

        var factorX = AlignFactor(element.RegionHAlign ?? LabelRegionAlign.Center);
        var factorY = AlignFactor(element.RegionVAlign ?? LabelRegionAlign.Center);
        double x;
        if (isAutoWidthText && textMeasurer is not null && textValue is not null)
        {
            var measuredWidthMm = textMeasurer.MeasureWidthMm((LabelTextElement)element, textValue);
            var anchorWidthMm = Math.Clamp(measuredWidthMm + element.EffectivePaddingHMm * 2, 0, Math.Max(0, region.WidthMm));
            x = region.XMm + (region.WidthMm - anchorWidthMm) * factorX;
        }
        else
        {
            x = region.XMm + (region.WidthMm - width) * factorX;
        }

        var y = region.YMm + (region.HeightMm - height) * factorY;
        return new ElementBounds(x, y, width, height);
    }

    private static double AlignFactor(LabelRegionAlign align) => align switch
    {
        LabelRegionAlign.Start => 0,
        LabelRegionAlign.Center => 0.5,
        LabelRegionAlign.End => 1,
        _ => 0.5,
    };
}
