using System.Runtime.InteropServices;
using LabelFrame.Core.Documents;
using LabelFrame.Rendering;
using SkiaSharp;
using Xunit.Abstractions;

namespace LabelFrame.WinHost.Tests.Rendering;

public class LabelBitmapPngTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 3)]
    [InlineData(8, 5)]
    [InlineData(9, 7)]
    [InlineData(31, 17)]
    [InlineData(799, 480)]
    [InlineData(1181, 709)]
    [InlineData(2362, 1417)]
    public void Png_should_match_original_bytes_and_preserve_input(int width, int height)
    {
        foreach (var fill in new byte[] { 0, 255, 0xa5 })
        {
            var bitmap = new LabelBitmap(width, height);
            Array.Fill(bitmap.Pixels, fill);
            var before = bitmap.Pixels.ToArray();
            Assert.Equal(Original(bitmap), LabelBitmapPng.ToPng(bitmap));
            Assert.Equal(before, bitmap.Pixels);
        }
        var random = new LabelBitmap(width, height);
        new Random(42).NextBytes(random.Pixels);
        Assert.Equal(Original(random), LabelBitmapPng.ToPng(random));
    }

    [Fact]
    public void Png_should_not_allocate_full_bgra_staging()
    {
        var bitmap = new LabelBitmap(1181, 709);
        Array.Fill(bitmap.Pixels, (byte)0xa5);
        for (var i = 0; i < 3; i++) LabelBitmapPng.ToPng(bitmap);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 3; i++) LabelBitmapPng.ToPng(bitmap);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"threePngAllocated={allocated}");
        Assert.InRange(allocated, 1, 800_000);
    }

    [Fact]
    public async Task Concurrent_calls_should_have_independent_output()
    {
        var inputs = Enumerable.Range(1, 16).Select(i => new LabelBitmap(i * 7, i * 3)).ToArray();
        foreach (var bitmap in inputs) new Random(bitmap.Width).NextBytes(bitmap.Pixels);
        var expected = inputs.Select(Original).ToArray();
        await Task.WhenAll(inputs.Select((bitmap, index) => Task.Run(() =>
        {
            for (var i = 0; i < 5; i++) Assert.Equal(expected[index], LabelBitmapPng.ToPng(bitmap));
        })));
        Assert.Throws<ArgumentNullException>(() => LabelBitmapPng.ToPng(null!));
    }

    private static byte[] Original(LabelBitmap bitmap)
    {
        using var skBitmap = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var bytes = new byte[bitmap.Width * bitmap.Height * 4];
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var bit = (bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0;
            var offset = (y * bitmap.Width + x) * 4;
            bytes[offset] = bit ? (byte)0 : byte.MaxValue;
            bytes[offset + 1] = bit ? (byte)0 : byte.MaxValue;
            bytes[offset + 2] = bit ? (byte)0 : byte.MaxValue;
            bytes[offset + 3] = byte.MaxValue;
        }
        Marshal.Copy(bytes, 0, skBitmap.GetPixels(), bytes.Length);
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
