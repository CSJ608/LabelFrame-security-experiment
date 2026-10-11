using Android.Graphics;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace LabelFrame.AndroidHost.Rendering;

/// <summary>
/// Android 整版位图渲染器（AndroidHost 打印统一为图片）：
/// 文本用 Android.Graphics（加粗 / 对齐 / 垂直对齐 / 单行缩小适应），条码 / 二维码用 ZXing，
/// 线 / 区域 / 图片绘制，输出 1bpp LabelBitmap → ZplImageEncoder ^GF。
/// 与 PC 端 Skia 渲染语义尽量一致；换行等高级排版在真机联调阶段补齐。
/// </summary>
public static class AndroidLabelRenderer
{
    /// <summary>渲染整张标签为 1bpp 位图（白底黑字）。</summary>
    public static LabelBitmap RenderLabelBitmap(LabelDocument document, int dpi, IReadOnlyDictionary<string, byte[]>? templateImages = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var width = Math.Max(1, ToDots(document.Layout.WidthMm, dpi));
        var height = Math.Max(1, ToDots(document.Layout.HeightMm, dpi));
        using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(bitmap);
        canvas.DrawColor(Color.White);

        var regions = LabelLayoutResolver.IndexRegions(document.Layout);
        foreach (var element in document.Layout.Elements)
        {
            DrawElement(canvas, element, document, templateImages, regions, dpi);
        }

        var pixels = new int[width * height];
        bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);
        var result = new LabelBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var argb = pixels[y * width + x];
                var red = (argb >> 16) & 0xFF;
                var green = (argb >> 8) & 0xFF;
                var blue = argb & 0xFF;
                var luma = (red * 299 + green * 587 + blue * 114) / 1000;
                if (luma < 128)
                {
                    result.Pixels[y * result.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return result;
    }

    private static void DrawElement(
        Canvas canvas,
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
        Canvas canvas,
        LabelTextElement text,
        LabelDocument document,
        IReadOnlyDictionary<string, LabelRegionElement> regions,
        int dpi)
    {
        LabelElementContent.TryGet(text, document.Data, out var value);
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        // 区域水平锚定按实测文本宽度计算（决策 #110）：度量与绘制同一 Paint 管线（TextSize / FakeBoldText）
        var bounds = LabelLayoutResolver.ResolveBounds(text, regions, AndroidTextWidthMeasurer.Instance, value);
        var x = ToDots(bounds.XMm, dpi);
        var y = ToDots(bounds.YMm, dpi);
        var boxWidth = ToDots(bounds.WidthMm, dpi);
        var padH = ToDots(text.EffectivePaddingHMm, dpi);
        var padV = ToDots(text.EffectivePaddingVMm, dpi);
        // 框高：显式块高用块高；无 heightMm 时——锚定文本的绘制框 = 解析锚定框（bounds.HeightMm，
        // 自动高度即字高，格顶语义与锚定偏移一致，迭代 89 #147），非锚定文本维持决策 A 兜底
        // max(字高 + 2×最大双边内边距, 10mm)（与前端读回兜底一致；与 PC Skia 渲染同口径）。
        var anchored = text.RegionId is not null && regions.ContainsKey(text.RegionId);
        var boxHeightMm = text.HeightMm > 0
            ? text.HeightMm
            : anchored ? Math.Max(bounds.HeightMm, 0)
            : Math.Max(text.FontHeightMm + 2 * Math.Max(text.EffectivePaddingHMm, text.EffectivePaddingVMm), 10);
        var boxHeight = ToDots(boxHeightMm, dpi);

        if (text.BorderMm > 0 && boxWidth > 0)
        {
            DrawRect(canvas, x, y, boxWidth, boxHeight, ToDots(text.BorderMm, dpi));
        }

        var innerX = x + padH;
        var innerY = y + padV;
        var innerW = boxWidth > 0 ? Math.Max(1, boxWidth - 2 * padH) : 0;
        var innerH = Math.Max(1, boxHeight - 2 * padV);

        var fontSize = Math.Max(1f, ToDots(text.FontHeightMm, dpi));
        using var paint = new Paint
        {
            TextSize = fontSize,
            AntiAlias = true,
            Color = Color.Black,
            FakeBoldText = text.Bold,
        };

        // 单行超出框宽按比例缩小（最小 1.5mm），与 PC Skia 默认 shrink 一致
        var measured = paint.MeasureText(value);
        if (innerW > 0 && measured > innerW)
        {
            var minFont = Math.Max(1f, ToDots(1.5, dpi));
            fontSize = Math.Max(minFont, fontSize * innerW / measured);
            paint.TextSize = fontSize;
            measured = paint.MeasureText(value);
        }

        var lineHeight = fontSize * (float)(text.LineHeight > 0 ? text.LineHeight : 1.2);
        var top = (float)innerY;
        switch (text.VerticalAlign)
        {
            case LabelVerticalAlign.Middle:
                top += (innerH - lineHeight) / 2;
                break;
            case LabelVerticalAlign.Bottom:
                top += innerH - lineHeight;
                break;
        }

        top = Math.Max((float)innerY, top);
        var baseline = top - paint.Ascent();
        var textX = (float)innerX;
        switch (text.TextAlign)
        {
            case LabelTextAlign.Center:
                textX = innerW > 0 ? innerX + (innerW - measured) / 2 : innerX;
                break;
            case LabelTextAlign.Right:
                textX = innerW > 0 ? innerX + innerW - measured : innerX;
                break;
        }

        canvas.Save();
        canvas.ClipRect(new Rect(innerX, innerY, innerX + (innerW > 0 ? innerW : canvas.Width), innerY + innerH));
        canvas.DrawText(value, textX, baseline, paint);
        canvas.Restore();
    }

    private static void DrawBarcode(
        Canvas canvas,
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

        if (barcode.BorderMm > 0)
        {
            DrawRect(canvas, x, y, boxWidth, boxHeight, ToDots(barcode.BorderMm, dpi));
        }

        var contentX = x + padH;
        var contentY = y + padV;
        var contentW = Math.Max(1, boxWidth - 2 * padH);
        var contentH = Math.Max(1, boxHeight - 2 * padV);

        var textSize = 0f;
        var textBand = 0f;
        if (barcode.DisplayValue)
        {
            textSize = Math.Max(ToDots(1.5, dpi), contentH * 0.15f);
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
        using var barcodeBitmap = ToArgbBitmap(writer.Write(value));
        canvas.DrawBitmap(barcodeBitmap, contentX, contentY, null);

        if (barcode.DisplayValue)
        {
            using var textPaint = new Paint { TextSize = textSize, AntiAlias = true, Color = Color.Black };
            var textWidth = textPaint.MeasureText(value);
            var textX = contentX + Math.Max(0, (contentW - textWidth) / 2);
            var textY = contentY + contentH - textPaint.Descent();
            canvas.DrawText(value, textX, textY, textPaint);
        }
    }

    private static void DrawQrCode(
        Canvas canvas,
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

        if (qrCode.BorderMm > 0)
        {
            DrawRect(canvas, x, y, boxSize, boxSize, ToDots(qrCode.BorderMm, dpi));
        }

        var contentW = Math.Max(1, boxSize - 2 * padH);
        var contentH = Math.Max(1, boxSize - 2 * padV);
        var contentSize = Math.Min(contentW, contentH);
        var contentX = x + padH + (contentW - contentSize) / 2;
        var contentY = y + padV + (contentH - contentSize) / 2;

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
        using var qrBitmap = ToArgbBitmap(writer.Write(value));
        canvas.DrawBitmap(qrBitmap, contentX, contentY, null);
    }

    private static void DrawImage(
        Canvas canvas,
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

        if (templateImages is not null && templateImages.TryGetValue(image.SourceKey, out var bytes))
        {
            using var source = DecodeMonochrome(bytes);
            if (source is not null)
            {
                // 拉伸填满元素盒（与 PC Skia 同口径，决策 #184 填充拍板 A）；Paint 传 true 开双线性滤波防缩小混叠
                using var paint = new Paint { AntiAlias = true, FilterBitmap = true };
                canvas.DrawBitmap(source, null, new Rect(x, y, x + width, y + height), paint);
                return;
            }
        }

        if (document.Images.TryGetValue(image.SourceKey, out var labelBitmap))
        {
            using var source = ToArgbBitmap(labelBitmap);
            canvas.DrawBitmap(source, null, new Rect(x, y, x + width, y + height), null);
            return;
        }

        // 决策 #184：缺资源＝打印侧显式报错（与 PC Skia 同口径），不再画灰占位框照打
        throw new MissingImageResourceException(image.SourceKey);
    }

    /// <summary>
    /// 解码图片并按 luma 阈值 128 预单色化（黑 / 白 ARGB，不透明）：与 PC Skia DrawImage 同阈值同源
    /// （决策 #184——口径一致＝阈值同源，两端缩放插值实现不同、逐像素一致不作承诺）；
    /// 透明区按白处理。解码失败返回 null（由调用方按缺资源口径处理）。
    /// </summary>
    private static Bitmap? DecodeMonochrome(byte[] bytes)
    {
        using var source = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        if (source is null)
        {
            return null;
        }

        var width = source.Width;
        var height = source.Height;
        var pixels = new int[width * height];
        source.GetPixels(pixels, 0, width, 0, 0, width, height);
        for (var i = 0; i < pixels.Length; i++)
        {
            var argb = pixels[i];
            var red = (argb >> 16) & 0xFF;
            var green = (argb >> 8) & 0xFF;
            var blue = argb & 0xFF;
            var luma = (red * 299 + green * 587 + blue * 114) / 1000;
            pixels[i] = luma < 128 ? unchecked((int)0xFF000000) : unchecked((int)0xFFFFFFFF);
        }

        var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        bitmap.SetPixels(pixels, 0, width, 0, 0, width, height);
        return bitmap;
    }

    private static void DrawLine(Canvas canvas, LabelLineElement line, int dpi)
    {
        using var paint = new Paint
        {
            StrokeWidth = Math.Max(1, ToDots(line.ThicknessMm, dpi)),
            Color = Color.Black,
        };
        paint.SetStyle(Android.Graphics.Paint.Style.Stroke);
        canvas.DrawLine(ToDots(line.XMm, dpi), ToDots(line.YMm, dpi), ToDots(line.X2Mm, dpi), ToDots(line.Y2Mm, dpi), paint);
    }

    private static void DrawRegion(Canvas canvas, LabelRegionElement region, int dpi)
    {
        if (region.BorderMm <= 0)
        {
            return;
        }

        DrawRect(
            canvas,
            ToDots(region.XMm, dpi),
            ToDots(region.YMm, dpi),
            ToDots(region.WidthMm, dpi),
            ToDots(region.HeightMm, dpi),
            ToDots(region.BorderMm, dpi));
    }

    private static void DrawRect(Canvas canvas, int x, int y, int width, int height, int strokeWidth)
    {
        using var paint = new Paint
        {
            StrokeWidth = Math.Max(1, strokeWidth),
            Color = Color.Black,
        };
        paint.SetStyle(Android.Graphics.Paint.Style.Stroke);
        canvas.DrawRect(new Rect(x, y, x + width, y + height), paint);
    }

    /// <summary>ZXing PixelData（BGRA 字节）→ Android ARGB Bitmap。</summary>
    private static Bitmap ToArgbBitmap(ZXing.Rendering.PixelData pixelData)
    {
        var bitmap = Bitmap.CreateBitmap(pixelData.Width, pixelData.Height, Bitmap.Config.Argb8888!)!;
        var argb = new int[pixelData.Width * pixelData.Height];
        for (var i = 0; i < argb.Length; i++)
        {
            var b = pixelData.Pixels[i * 4];
            var g = pixelData.Pixels[i * 4 + 1];
            var r = pixelData.Pixels[i * 4 + 2];
            argb[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        bitmap.SetPixels(argb, 0, pixelData.Width, 0, 0, pixelData.Width, pixelData.Height);
        return bitmap;
    }

    /// <summary>LabelBitmap（1bpp）→ Android ARGB Bitmap（白底黑字）。</summary>
    private static Bitmap ToArgbBitmap(LabelBitmap labelBitmap)
    {
        var bitmap = Bitmap.CreateBitmap(labelBitmap.Width, labelBitmap.Height, Bitmap.Config.Argb8888!)!;
        var argb = new int[labelBitmap.Width * labelBitmap.Height];
        for (var y = 0; y < labelBitmap.Height; y++)
        {
            for (var x = 0; x < labelBitmap.Width; x++)
            {
                var bit = (labelBitmap.Pixels[y * labelBitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                argb[y * labelBitmap.Width + x] = bit ? unchecked((int)0xFF000000) : unchecked((int)0xFFFFFFFF);
            }
        }

        bitmap.SetPixels(argb, 0, labelBitmap.Width, 0, 0, labelBitmap.Width, labelBitmap.Height);
        return bitmap;
    }

    /// <summary>毫米 → 点（DPI），四舍五入远离零。</summary>
    private static int ToDots(double mm, int dpi)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero));
}