using System.Runtime.InteropServices;
using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace LabelFrame.Rendering;

/// <summary>整版位图渲染器（Image 打印 / 调试出图用）。</summary>
public interface ILabelBitmapRenderer
{
    /// <summary>渲染整张标签为 1bpp 位图（白底黑字）。</summary>
    LabelBitmap RenderLabelBitmap(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null);

    /// <summary>渲染整张标签为 1bpp 位图并编码为 PNG 字节。</summary>
    byte[] RenderLabelBitmapPng(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null);
}

/// <summary>
/// SkiaSharp 后端渲染器：与前端 canvas 渲染同源（自动换行 / 行距 / 溢出处理 / 字体族 /
/// 左中右对齐 / 双边内边距 / 边框、线条、区域、ZXing 条码二维码参数、模板图片），
/// 输出 1bpp 位图，用于图片打印与调试。
/// 整版渲染中间态（SKBitmap 像素内存 + 托管像素暂存数组）按尺寸池化复用，
/// 降低大批量打印时每张 1-5MB 的分配与 GC 压力；输出 LabelBitmap / PNG 始终为新分配，行为不变。
/// </summary>
public sealed class SkiaLabelRenderer : ILabelBitmapRenderer
{
    /// <summary>池上限：留出并发渲染（多请求提交 + 出图 / 预览）的余量，超出即释放不再复用。</summary>
    private const int MaxPooledSurfaces = 4;

    private readonly object _surfacePoolLock = new();
    private readonly Stack<(SKBitmap Bitmap, byte[] Pixels)> _surfacePool = new();

    /// <summary>租用整版渲染中间态：优先取「尺寸与暂存长度都匹配」的池化项（像素内存复用，内容由 Clear 全量重置）；无则新分配。</summary>
    private (SKBitmap Bitmap, byte[] Pixels) RentSurface(int width, int height)
    {
        lock (_surfacePoolLock)
        {
            while (_surfacePool.Count > 0)
            {
                var surface = _surfacePool.Pop();
                if (surface.Bitmap.Width == width
                    && surface.Bitmap.Height == height
                    && surface.Pixels.Length == surface.Bitmap.RowBytes * height)
                {
                    return surface;
                }

                surface.Bitmap.Dispose();
            }
        }

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        return (bitmap, new byte[bitmap.RowBytes * height]);
    }

    /// <summary>归还中间态：池未满则复用，尺寸 / 容量溢出时释放。</summary>
    private void ReturnSurface(SKBitmap bitmap, byte[] pixels)
    {
        lock (_surfacePoolLock)
        {
            if (_surfacePool.Count < MaxPooledSurfaces)
            {
                _surfacePool.Push((bitmap, pixels));
                return;
            }
        }

        bitmap.Dispose();
    }

    /// <inheritdoc />
    public LabelBitmap RenderLabelBitmap(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var width = Math.Max(1, ToDots(document.Layout.WidthMm, dpi));
        var height = Math.Max(1, ToDots(document.Layout.HeightMm, dpi));
        var (bitmap, staging) = RentSurface(width, height);
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            var regions = LabelLayoutResolver.IndexRegions(document.Layout);
            foreach (var element in document.Layout.Elements)
            {
                DrawElement(canvas, element, document, templateImages, regions, dpi);
            }

            var rowBytes = bitmap.RowBytes;
            Marshal.Copy(bitmap.GetPixels(), staging, 0, staging.Length);
            return ToLabelBitmap(width, height, rowBytes, staging);
        }
        finally
        {
            ReturnSurface(bitmap, staging);
        }
    }

    /// <inheritdoc />
    public byte[] RenderLabelBitmapPng(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var width = Math.Max(1, ToDots(document.Layout.WidthMm, dpi));
        var height = Math.Max(1, ToDots(document.Layout.HeightMm, dpi));
        var (bitmap, _) = RentSurface(width, height);
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            var regions = LabelLayoutResolver.IndexRegions(document.Layout);
            foreach (var element in document.Layout.Elements)
            {
                DrawElement(canvas, element, document, templateImages, regions, dpi);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally
        {
            ReturnSurface(bitmap, []);
        }
    }

    private static void DrawElement(
        SKCanvas canvas,
        LabelElement element,
        LabelDocument document,
        IReadOnlyDictionary<string, byte[]>? templateImages,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        switch (element)
        {
            case LabelTextElement text:
                DrawText(canvas, text, document, regions, dpi);
                break;
            case LabelBarcodeElement barcode:
                DrawBarcode(canvas, barcode, document, regions, dpi);
                break;
            case LabelQrCodeElement qrCode:
                DrawQrCode(canvas, qrCode, document, regions, dpi);
                break;
            case LabelImageElement image:
                DrawImage(canvas, image, document, templateImages, regions, dpi);
                break;
            case LabelLineElement line:
                DrawLine(canvas, line, dpi);
                break;
            case LabelRegionElement region:
                DrawRegion(canvas, region, dpi);
                break;
        }
    }

    private static void DrawText(
        SKCanvas canvas,
        LabelTextElement text,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(text, document.Data, out var value);
        // 区域水平锚定按实测文本宽度计算（决策 #110）：度量与绘制同源（字型查找 / 加粗一致）
        var bounds = LabelLayoutResolver.ResolveBounds(text, regions, SkiaTextWidthMeasurer.Instance, value);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var boxWidth = ToDots(bounds.WidthMm, dpi);
        var padH = ToDots(text.EffectivePaddingHMm, dpi);
        var padV = ToDots(text.EffectivePaddingVMm, dpi);
        // 框高：显式块高用块高；无 heightMm 时——锚定文本的绘制框 = 解析锚定框（bounds.HeightMm，
        // 自动高度即字高，格顶语义与锚定偏移一致，迭代 89 #147），非锚定文本维持决策 A 兜底
        // max(字高 + 2×最大双边内边距, 10mm)（与前端读回兜底一致）。
        // 锚定路径此前误用兜底框再按 VerticalAlign 居中，字形比锚定盒中心下沉 (兜底框高 − 字高)/2，
        // 小字号时约 1.5–2 字高，穿出锚定盒底边（真机陪验 #120 取证）。
        var anchored = text.RegionId is not null && regions.ContainsKey(text.RegionId);
        var boxHeightMm = text.HeightMm > 0
            ? text.HeightMm
            : anchored ? Math.Max(bounds.HeightMm, 0)
            : Math.Max(text.FontHeightMm + 2 * Math.Max(text.EffectivePaddingHMm, text.EffectivePaddingVMm), 10);
        var boxHeight = ToDots(boxHeightMm, dpi);
        using var typeface = SkiaTypefaceLookup.CreateTypeface(text.FontFamily, value);

        // 边框 = 元素框；内边距在框内（与前端 ElementNode 一致）
        if (text.BorderMm > 0 && boxWidth > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(text.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + boxWidth, y + boxHeight), borderPaint);
        }

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var innerX = (float)(x + padH);
        var innerY = (float)(y + padV);
        // 内容区 = 元素框减去 paddingH / paddingV（0 = 宽度不限）
        var innerW = boxWidth > 0 ? Math.Max(1, boxWidth - 2 * padH) : 0;
        var innerH = Math.Max(1, boxHeight - 2 * padV);
        var baseFontSize = Math.Max(1f, ToDots(text.FontHeightMm, dpi));
        var lineHeightFactor = (float)(text.LineHeight > 0 ? text.LineHeight : 1.2);
        var minFontSize = Math.Max(1f, ToDots(1.5, dpi));

        var fontSize = baseFontSize;
        List<string> lines;
        if (text.Wrap)
        {
            // wrap=true：按框宽自动换行；若整体超高则整体缩小至能放下（最小 1.5mm），避免打印丢字
            lines = WrapLines(value, typeface, fontSize, innerW, text.Bold);
            var totalHeight = lines.Count * fontSize * lineHeightFactor;
            var guard = 0;
            while (totalHeight > innerH && fontSize > minFontSize && guard++ < 10)
            {
                fontSize = Math.Max(minFontSize, fontSize * innerH / totalHeight);
                lines = WrapLines(value, typeface, fontSize, innerW, text.Bold);
                totalHeight = lines.Count * fontSize * lineHeightFactor;
            }
        }
        else if (text.FitMode == LabelFitMode.Overflow)
        {
            // wrap=false + overflow：单行、不缩小、按框裁剪（隐藏溢出）
            lines = [value];
        }
        else
        {
            // wrap=false + shrink（默认）：单行，超出框宽 / 框高按比例缩小至最小 1.5mm
            lines = [value];
            using var measureFont = new SKFont(typeface, fontSize) { Embolden = text.Bold };
            var measuredWidth = measureFont.MeasureText(value);
            var widthFactor = innerW > 0 && measuredWidth > innerW ? innerW / measuredWidth : 1f;
            var fitLineHeightPx = fontSize * lineHeightFactor;
            var heightFactor = fitLineHeightPx > innerH ? innerH / fitLineHeightPx : 1f;
            var factor = Math.Min(widthFactor, heightFactor);
            if (factor < 1f)
            {
                fontSize = Math.Max(minFontSize, fontSize * factor);
            }
        }

        using var font = new SKFont(typeface, fontSize) { Embolden = text.Bold };
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        var fm = font.Metrics;
        var lineHeightPx = fontSize * lineHeightFactor;
        var totalHeightPx = lines.Count * lineHeightPx;
        // 与前端一致：文本在元素框内按 verticalAlign 垂直对齐（Top / Middle / Bottom）
        var startY = innerY;
        switch (text.VerticalAlign)
        {
            case LabelVerticalAlign.Middle:
                startY += (innerH - totalHeightPx) / 2;
                break;
            case LabelVerticalAlign.Bottom:
                startY += innerH - totalHeightPx;
                break;
        }

        startY = Math.Max(innerY, startY);
        canvas.Save();
        var clipRight = innerW > 0 ? innerX + innerW : canvas.DeviceClipBounds.Right;
        canvas.ClipRect(new SKRect(innerX, innerY, clipRight, innerY + innerH));
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            var lineWidth = font.MeasureText(line);
            var lineX = innerX;
            if (innerW > 0)
            {
                lineX = text.TextAlign switch
                {
                    LabelTextAlign.Center => innerX + (innerW - lineWidth) / 2,
                    LabelTextAlign.Right => innerX + innerW - lineWidth,
                    _ => innerX,
                };
            }

            var lineTop = startY + i * lineHeightPx;
            var baseline = lineTop - fm.Ascent;
            canvas.DrawText(line, lineX, baseline, SKTextAlign.Left, font, paint);
        }

        canvas.Restore();
    }

    /// <summary>按框宽换行：英文按空格断词，超宽单词 / 中文按字拆行。</summary>
    private static List<string> WrapLines(string value, SKTypeface typeface, float fontSize, float maxWidth, bool embolden)
    {
        using var font = new SKFont(typeface, fontSize) { Embolden = embolden };
        var lines = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            return lines;
        }

        if (maxWidth <= 0)
        {
            lines.Add(value);
            return lines;
        }

        foreach (var rawLine in value.Split('\n'))
        {
            var current = new StringBuilder();
            var words = rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rawLine.Length == 0 || words.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            foreach (var word in words)
            {
                if (font.MeasureText(word) > maxWidth)
                {
                    // 超宽单词 / 无空格中文：按字拆行
                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }

                    foreach (var ch in word)
                    {
                        var trial = current.Length == 0 ? ch.ToString() : current.ToString() + ch;
                        if (current.Length > 0 && font.MeasureText(trial) > maxWidth)
                        {
                            lines.Add(current.ToString());
                            current.Clear();
                        }

                        current.Append(ch);
                    }

                    continue;
                }

                var candidate = current.Length == 0 ? word : current.ToString() + " " + word;
                if (current.Length > 0 && font.MeasureText(candidate) > maxWidth)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                {
                    current.Append(' ');
                }

                current.Append(word);
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
            }
        }

        return lines;
    }

    private static void DrawBarcode(
        SKCanvas canvas,
        LabelBarcodeElement barcode,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(barcode, document.Data, out var value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var bounds = LabelLayoutResolver.ResolveBounds(barcode, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var boxWidth = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        var boxHeight = Math.Max(1, ToDots(bounds.HeightMm, dpi));
        var padH = ToDots(barcode.EffectivePaddingHMm, dpi);
        var padV = ToDots(barcode.EffectivePaddingVMm, dpi);
        // 内容区 = 元素框减去 paddingH / paddingV
        var contentX = x + padH;
        var contentY = y + padV;
        var contentW = Math.Max(1, boxWidth - 2 * padH);
        var contentH = Math.Max(1, boxHeight - 2 * padV);

        if (barcode.BorderMm > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(barcode.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + boxWidth, y + boxHeight), borderPaint);
        }

        // displayValue=true：底部绘制数值文字（字号取框高比例，最小 1.5mm），条码占剩余高度；
        // displayValue=false：仅条码（PureBarcode，不绘制文字）
        var textSize = 0f;
        var textBand = 0f;
        if (barcode.DisplayValue)
        {
            textSize = Math.Max(ToDots(1.5, dpi), contentH * 0.15f);
            using var measureFont = new SKFont(SkiaTypefaceLookup.CreateTypeface(LabelTextElement.DefaultFontFamily, value), textSize);
            var measured = measureFont.MeasureText(value);
            if (measured > contentW)
            {
                textSize = Math.Max(ToDots(1.5, dpi), textSize * contentW / measured);
            }

            textBand = textSize * 1.2f;
            if (textBand >= contentH)
            {
                textBand = Math.Max(0f, contentH * 0.15f);
            }
        }

        var barcodeHeight = Math.Max(1, (int)(contentH - textBand));
        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Height = barcodeHeight, Width = contentW, Margin = 2, PureBarcode = true },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var barcodeBitmap = ToSkBitmap(writer.Write(value));
        canvas.DrawBitmap(barcodeBitmap, contentX, contentY);

        if (barcode.DisplayValue)
        {
            using var typeface = SkiaTypefaceLookup.CreateTypeface(LabelTextElement.DefaultFontFamily, value);
            using var textFont = new SKFont(typeface, textSize);
            using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
            var textWidth = textFont.MeasureText(value);
            var textX = contentX + Math.Max(0, (contentW - textWidth) / 2);
            var textY = contentY + contentH - textFont.Metrics.Descent;
            canvas.Save();
            canvas.ClipRect(new SKRect(contentX, contentY, contentX + contentW, contentY + contentH));
            canvas.DrawText(value, textX, textY, SKTextAlign.Left, textFont, textPaint);
            canvas.Restore();
        }
    }

    private static void DrawQrCode(
        SKCanvas canvas,
        LabelQrCodeElement qrCode,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(qrCode, document.Data, out var value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var bounds = LabelLayoutResolver.ResolveBounds(qrCode, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var boxSize = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        var padH = ToDots(qrCode.EffectivePaddingHMm, dpi);
        var padV = ToDots(qrCode.EffectivePaddingVMm, dpi);
        // 内容区 = 元素框减去 paddingH / paddingV；二维码保持正方形并居中
        var contentW = Math.Max(1, boxSize - 2 * padH);
        var contentH = Math.Max(1, boxSize - 2 * padV);
        var contentSize = Math.Min(contentW, contentH);
        var contentX = x + padH + (contentW - contentSize) / 2;
        var contentY = y + padV + (contentH - contentSize) / 2;

        if (qrCode.BorderMm > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(qrCode.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + boxSize, y + boxSize), borderPaint);
        }

        var writer = new BarcodeWriter<ZXing.Rendering.PixelData>
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = contentSize,
                Height = contentSize,
                Margin = qrCode.QrMargin,
                ErrorCorrection = qrCode.QrEcc switch
                {
                    LabelQrEcc.L => ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
                    LabelQrEcc.Q => ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
                    LabelQrEcc.H => ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
                    _ => ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
                },
            },
            Renderer = new ZXing.Rendering.PixelDataRenderer(),
        };
        using var qrBitmap = ToSkBitmap(writer.Write(value));
        canvas.DrawBitmap(qrBitmap, contentX, contentY);
    }

    private static void DrawImage(
        SKCanvas canvas,
        LabelImageElement image,
        LabelDocument document,
        IReadOnlyDictionary<string, byte[]>? templateImages,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        var bounds = LabelLayoutResolver.ResolveBounds(image, regions);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var width = Math.Max(1, ToDots(bounds.WidthMm, dpi));
        var height = Math.Max(1, ToDots(bounds.HeightMm, dpi));
        if (image.BorderMm > 0)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, ToDots(image.BorderMm, dpi)),
                IsAntialias = false,
                Color = SKColors.Black,
            };
            canvas.DrawRect(new SKRect(x, y, x + width, y + height), borderPaint);
        }

        if (templateImages is not null && templateImages.TryGetValue(image.SourceKey, out var bytes))
        {
            using var source = DecodeMonochrome(bytes);
            if (source is not null)
            {
                // 线性采样：大幅缩小（原图 >> 元素盒）时防混叠失真（决策 #184）；
                // SkiaSharp 3.x 采样选项走 DrawImage(SKImage, rect, SKSamplingOptions)——SKPaint.FilterQuality 已废弃
                using var monochrome = SKImage.FromBitmap(source);
                canvas.DrawImage(monochrome, new SKRect(x, y, x + width, y + height), MonochromeSampling);
                return;
            }
        }

        if (document.Images.TryGetValue(image.SourceKey, out var labelBitmap))
        {
            using var source = ToSkBitmap(labelBitmap);
            canvas.DrawBitmap(source, new SKRect(x, y, x + width, y + height));
            return;
        }

        // 决策 #184：缺资源＝渲染侧显式报错（预览 / 出图 / 打印链失败并透出键名中文消息），
        // 不再画灰占位框照打；设计器画布的占位框由前端自行呈现（属性面板提示可重新上传修复）
        throw new MissingImageResourceException(image.SourceKey);
    }

    /// <summary>单色图片缩放采样（Linear）：黑 / 白硬边缩小仍保边缘锐度。</summary>
    private static readonly SKSamplingOptions MonochromeSampling = new(SKFilterMode.Linear);

    /// <summary>
    /// 解码图片并按 luma 阈值 128 单色化（黑白 BGRA，不透明）：与整版 1bpp 转换（ToLabelBitmap）同阈值同源，
    /// 使预览 PNG 与打印输出一致（决策 #184——此前预览编码彩色画布、打印 1bpp 阈值，本就不一致）；
    /// 透明区按白处理（出纸无墨）。解码失败返回 null（由调用方按缺资源口径处理）。
    /// </summary>
    private static SKBitmap? DecodeMonochrome(byte[] bytes)
    {
        using var source = SKBitmap.Decode(bytes);
        if (source is null)
        {
            return null;
        }

        var dest = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(dest))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, 0, 0);
        }

        var staging = new byte[dest.RowBytes * dest.Height];
        Marshal.Copy(dest.GetPixels(), staging, 0, staging.Length);
        for (var offset = 0; offset < staging.Length; offset += 4)
        {
            // BGRA 布局：B=0 G=1 R=2（与 ToLabelBitmap 同读取序）
            var luma = (staging[offset + 2] * 299 + staging[offset + 1] * 587 + staging[offset] * 114) / 1000;
            var value = luma < 128 ? (byte)0 : byte.MaxValue;
            staging[offset] = value;
            staging[offset + 1] = value;
            staging[offset + 2] = value;
            staging[offset + 3] = byte.MaxValue;
        }

        Marshal.Copy(staging, 0, dest.GetPixels(), staging.Length);
        return dest;
    }

    private static void DrawLine(SKCanvas canvas, LabelLineElement line, int dpi)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, ToDots(line.ThicknessMm, dpi)),
            StrokeCap = SKStrokeCap.Butt,
            IsAntialias = false,
            Color = SKColors.Black,
        };
        canvas.DrawLine(
            ToDots(line.XMm, dpi),
            ToDots(line.YMm, dpi),
            ToDots(line.X2Mm, dpi),
            ToDots(line.Y2Mm, dpi),
            paint);
    }

    private static void DrawRegion(SKCanvas canvas, LabelRegionElement region, int dpi)
    {
        if (region.BorderMm <= 0)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, ToDots(region.BorderMm, dpi)),
            IsAntialias = false,
            Color = SKColors.Black,
        };
        canvas.DrawRect(
            new SKRect(
                ToDots(region.XMm, dpi),
                ToDots(region.YMm, dpi),
                ToDots(region.XMm + region.WidthMm, dpi),
                ToDots(region.YMm + region.HeightMm, dpi)),
            paint);
    }

    /// <summary>ZXing PixelData（BGRA 字节）→ SKBitmap。</summary>
    private static SKBitmap ToSkBitmap(ZXing.Rendering.PixelData pixelData)
    {
        var bitmap = new SKBitmap(new SKImageInfo(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);
        return bitmap;
    }

    /// <summary>LabelBitmap（1bpp）→ SKBitmap（白底黑字）。</summary>
    private static SKBitmap ToSkBitmap(LabelBitmap labelBitmap)
    {
        var bitmap = new SKBitmap(new SKImageInfo(labelBitmap.Width, labelBitmap.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var bytes = new byte[labelBitmap.Width * labelBitmap.Height * 4];
        for (var y = 0; y < labelBitmap.Height; y++)
        {
            for (var x = 0; x < labelBitmap.Width; x++)
            {
                var bit = (labelBitmap.Pixels[y * labelBitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                var offset = (y * labelBitmap.Width + x) * 4;
                bytes[offset] = bit ? (byte)0 : byte.MaxValue;     // B
                bytes[offset + 1] = bit ? (byte)0 : byte.MaxValue; // G
                bytes[offset + 2] = bit ? (byte)0 : byte.MaxValue; // R
                bytes[offset + 3] = byte.MaxValue;                 // A
            }
        }

        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }

    /// <summary>BGRA 像素 → 1bpp LabelBitmap（白底黑字，阈值 128）。</summary>
    private static LabelBitmap ToLabelBitmap(int width, int height, int rowBytes, byte[] bytes)
    {
        var result = new LabelBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * rowBytes + x * 4;
                var b = bytes[offset];
                var g = bytes[offset + 1];
                var r = bytes[offset + 2];
                var luma = (r * 299 + g * 587 + b * 114) / 1000;
                if (luma < 128)
                {
                    result.Pixels[y * result.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return result;
    }

    /// <summary>毫米 → 点（DPI），四舍五入远离零。</summary>
    private static int ToDots(double mm, int dpi)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero));
}
