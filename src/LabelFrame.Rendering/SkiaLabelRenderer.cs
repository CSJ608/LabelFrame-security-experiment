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

/// <summary>Skia 整版渲染器：与前端 canvas 渲染同源，输出 1bpp 位图与 PNG；中间态按尺寸池化复用。</summary>
public sealed class SkiaLabelRenderer : ILabelBitmapRenderer
{
    private const int MaxPooledSurfaces = 4;

    private readonly object _surfacePoolLock = new();
    private readonly Stack<SKBitmap> _surfacePool = new();

    /// <summary>租用渲染中间态：尺寸全匹配的池化项复用，否则新分配；租用方须先整面 Clear 再绘制（池化复用前提）。</summary>
    private SKBitmap RentSurface(int width, int height)
    {
        lock (_surfacePoolLock)
        {
            while (_surfacePool.Count > 0)
            {
                var surface = _surfacePool.Pop();
                if (surface.Width == width && surface.Height == height)
                {
                    return surface;
                }

                surface.Dispose();
            }
        }

        return new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
    }

    private void ReturnSurface(SKBitmap bitmap)
    {
        lock (_surfacePoolLock)
        {
            if (_surfacePool.Count < MaxPooledSurfaces)
            {
                _surfacePool.Push(bitmap);
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
        var bitmap = RentSurface(width, height);
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
            return ToLabelBitmap(width, height, rowBytes, bitmap.GetPixelSpan());
        }
        finally
        {
            ReturnSurface(bitmap);
        }
    }

    /// <inheritdoc />
    public byte[] RenderLabelBitmapPng(LabelDocument document, int dpi = 203, IReadOnlyDictionary<string, byte[]>? templateImages = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var width = Math.Max(1, ToDots(document.Layout.WidthMm, dpi));
        var height = Math.Max(1, ToDots(document.Layout.HeightMm, dpi));
        var bitmap = RentSurface(width, height);
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
            ReturnSurface(bitmap);
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
        // 区域水平锚定按实测文本宽度计算：度量与绘制同源（字型查找 / 加粗一致）
        var bounds = LabelLayoutResolver.ResolveBounds(text, regions, SkiaTextWidthMeasurer.Instance, value);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var boxWidth = ToDots(bounds.WidthMm, dpi);
        var padH = ToDots(text.EffectivePaddingHMm, dpi);
        var padV = ToDots(text.EffectivePaddingVMm, dpi);
        // 框高取值：显式 heightMm > 锚定框高（bounds.HeightMm）> 非锚定兜底 max(字高 + 2×最大边距, 10mm)（与前端一致）；
        // 锚定路径禁改用兜底框再垂直居中——字形会较锚定盒中心下沉、小字号穿出盒底（曾有真机缺陷）。
        var anchored = text.RegionId is not null && regions.ContainsKey(text.RegionId);
        var boxHeightMm = text.HeightMm > 0
            ? text.HeightMm
            : anchored ? Math.Max(bounds.HeightMm, 0)
            : Math.Max(text.FontHeightMm + 2 * Math.Max(text.EffectivePaddingHMm, text.EffectivePaddingVMm), 10);
        var boxHeight = ToDots(boxHeightMm, dpi);
        using var typeface = SkiaTypefaceLookup.CreateTypeface(text.FontFamily, value);

        // 边框 = 元素框、内边距在框内，与前端 ElementNode 一致（跨端渲染同源契约）
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
        var innerW = boxWidth > 0 ? Math.Max(1, boxWidth - 2 * padH) : 0;
        var innerH = Math.Max(1, boxHeight - 2 * padV);
        var baseFontSize = Math.Max(1f, ToDots(text.FontHeightMm, dpi));
        var lineHeightFactor = (float)(text.LineHeight > 0 ? text.LineHeight : 1.2);
        var minFontSize = Math.Max(1f, ToDots(1.5, dpi));

        var fontSize = baseFontSize;
        List<string> lines;
        if (text.Wrap)
        {
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
            lines = [value];
        }
        else
        {
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
        // 垂直对齐（Top / Middle / Bottom）语义与前端一致
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

        // displayValue 的数值文字由渲染器自绘；ZXing 侧 PureBarcode=true 屏蔽其内建文字（字体一致性）
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
        using var barcodeBitmap = ToSkBitmap(writer.Encode(value));
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
        // 二维码须保持正方形（可扫前提）并居中
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
        using var qrBitmap = ToSkBitmap(writer.Encode(value));
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
        // SourceKey 空＝未指定：整元素跳过（含边框，「未指定即不存在于打印面」）；
        // key 非空缺资源才显式报错——两条路径语义不同，不可合并
        if (string.IsNullOrEmpty(image.SourceKey))
        {
            return;
        }

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
                // 线性采样：大幅缩小时防混叠失真；SkiaSharp 3.x 采样走
                // DrawImage(SKImage, rect, SKSamplingOptions)——SKPaint.FilterQuality 已废弃
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

        // 缺资源＝渲染侧显式报错（预览 / 出图 / 打印链一致失败并透出键名消息），不画灰占位照打
        throw new MissingImageResourceException(image.SourceKey);
    }

    private static readonly SKSamplingOptions MonochromeSampling = new(SKFilterMode.Linear);

    /// <summary>解码图片并按 luma 阈值 128 单色化；阈值须与 ToLabelBitmap 同源——预览与打印输出一致依赖它。</summary>
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
    private static SKBitmap ToSkBitmap(BitMatrix matrix)
    {
        var bitmap = new SKBitmap(new SKImageInfo(matrix.Width, matrix.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        try
        {
            var pixels = bitmap.GetPixelSpan();
            var black = BitConverter.IsLittleEndian ? 0xff000000u : 0x000000ffu;
            for (var y = 0; y < matrix.Height; y++)
            {
                var row = MemoryMarshal.Cast<byte, uint>(pixels.Slice(y * bitmap.RowBytes, matrix.Width * 4));
                for (var x = 0; x < matrix.Width; x++)
                    row[x] = matrix[x, y] ? black : uint.MaxValue;
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
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
                bytes[offset] = bit ? (byte)0 : byte.MaxValue;
                bytes[offset + 1] = bit ? (byte)0 : byte.MaxValue;
                bytes[offset + 2] = bit ? (byte)0 : byte.MaxValue;
                bytes[offset + 3] = byte.MaxValue;
            }
        }

        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }

    /// <summary>BGRA 像素 → 1bpp LabelBitmap（白底黑字，阈值 128）。</summary>
    private static LabelBitmap ToLabelBitmap(int width, int height, int rowBytes, ReadOnlySpan<byte> bytes)
    {
        var result = new LabelBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * rowBytes;
            var targetRow = y * result.RowBytes;
            for (var x = 0; x < width; x++)
            {
                var offset = sourceRow + x * 4;
                var b = bytes[offset];
                var g = bytes[offset + 1];
                var r = bytes[offset + 2];
                var weightedLuma = r * 299 + g * 587 + b * 114;
                if (weightedLuma < 128000)
                {
                    result.Pixels[targetRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return result;
    }

    /// <summary>毫米 → 点（DPI），四舍五入远离零。</summary>
    private static int ToDots(double mm, int dpi)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero));
}
