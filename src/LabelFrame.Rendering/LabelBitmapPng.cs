using System.Runtime.InteropServices;
using LabelFrame.Core.Documents;
using SkiaSharp;

namespace LabelFrame.Rendering;

/// <summary>1bpp 位图 → PNG 工具（Log 模拟打印、调试出图、render-images 共用）。</summary>
public static class LabelBitmapPng
{
    /// <summary>把 1bpp LabelBitmap（白底黑字）编码为 PNG 字节。</summary>
    public static byte[] ToPng(LabelBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var skBitmap = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var bytes = new byte[bitmap.Width * bitmap.Height * 4];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var bit = (bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                var offset = (y * bitmap.Width + x) * 4;
                bytes[offset] = bit ? (byte)0 : byte.MaxValue;
                bytes[offset + 1] = bit ? (byte)0 : byte.MaxValue;
                bytes[offset + 2] = bit ? (byte)0 : byte.MaxValue;
                bytes[offset + 3] = byte.MaxValue;
            }
        }

        Marshal.Copy(bytes, 0, skBitmap.GetPixels(), bytes.Length);
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}