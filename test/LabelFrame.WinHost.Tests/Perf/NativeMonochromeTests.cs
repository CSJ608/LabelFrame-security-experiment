using System.Diagnostics;
using System.Runtime.InteropServices;
using LabelFrame.Core.Documents;
using LabelFrame.WinHost.Tests.Rendering;
using SkiaSharp;
using Xunit.Abstractions;

namespace LabelFrame.WinHost.Tests.Perf;

public class NativeMonochromeTests(ITestOutputHelper output)
{
    [Fact]
    public void Compare_native_span_with_copy_then_convert()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1181, 709, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var staging = new byte[bitmap.RowBytes * bitmap.Height];
        foreach (var randomPixels in new[] { false, true })
        {
            if (randomPixels)
            {
                new Random(42).NextBytes(staging);
                Marshal.Copy(staging, 0, bitmap.GetPixels(), staging.Length);
            }
            else
            {
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.White);
                using var paint = new SKPaint { Color = SKColors.Black };
                for (var x = 20; x < 1000; x += 7)
                    canvas.DrawRect(x, 200, 3, 200, paint);
            }
            Marshal.Copy(bitmap.GetPixels(), staging, 0, staging.Length);
            var expected = Reference(bitmap.Width, bitmap.Height, bitmap.RowBytes, staging);
            Assert.Equal(expected.Pixels, Direct(bitmap.Width, bitmap.Height, bitmap.RowBytes, bitmap.GetPixelSpan()).Pixels);
            for (var round = 0; round < 4; round++)
            foreach (var direct in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                LabelBitmap? last = null;
                for (var i = 0; i < 200; i++)
                {
                    if (direct)
                        last = Direct(bitmap.Width, bitmap.Height, bitmap.RowBytes, bitmap.GetPixelSpan());
                    else
                    {
                        Marshal.Copy(bitmap.GetPixels(), staging, 0, staging.Length);
                        last = Reference(bitmap.Width, bitmap.Height, bitmap.RowBytes, staging);
                    }
                }
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(expected.Pixels, last!.Pixels);
                output.WriteLine($"random={randomPixels}; round={round}; direct={direct}; ms={elapsed:F2}; bytes={allocated}");
            }
        }
    }

    private static LabelBitmap Direct(int width, int height, int rowBytes, ReadOnlySpan<byte> bytes)
        => SkiaMonochromeConversionTests.ConvertSpan(width, height, rowBytes, bytes);

    private static LabelBitmap Reference(int width, int height, int rowBytes, byte[] bytes)
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
                    result.Pixels[targetRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
        return result;
    }
}
