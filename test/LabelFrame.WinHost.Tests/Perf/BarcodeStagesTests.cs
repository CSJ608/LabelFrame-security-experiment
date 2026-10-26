using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit.Abstractions;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace LabelFrame.WinHost.Tests.Perf;

public class BarcodeStagesTests(ITestOutputHelper output)
{
    [Fact]
    public void Measure_matrix_pixels_copy_and_draw()
    {
        using var surface = new SKBitmap(new SKImageInfo(1181, 709, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(surface);
        for (var round = 0; round < 4; round++)
        {
            long matrixTicks = 0, pixelsTicks = 0, copyTicks = 0, drawTicks = 0, allocated = 0;
            for (var i = 0; i < 200; i++)
            {
                var value = $"LF-{i + 1:0000}";
                var options = new EncodingOptions { Height = 260, Width = 650, Margin = 2, PureBarcode = true };
                var writer = new BarcodeWriter<PixelData>
                {
                    Format = BarcodeFormat.CODE_128, Options = options, Renderer = new PixelDataRenderer(),
                };
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                var matrix = writer.Encode(value);
                matrixTicks += Stopwatch.GetTimestamp() - start;
                start = Stopwatch.GetTimestamp();
                var pixels = writer.Renderer.Render(matrix, writer.Format, value, options);
                pixelsTicks += Stopwatch.GetTimestamp() - start;
                start = Stopwatch.GetTimestamp();
                using var bitmap = new SKBitmap(new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                Marshal.Copy(pixels.Pixels, 0, bitmap.GetPixels(), pixels.Pixels.Length);
                copyTicks += Stopwatch.GetTimestamp() - start;
                start = Stopwatch.GetTimestamp();
                canvas.DrawBitmap(bitmap, 35, 213);
                drawTicks += Stopwatch.GetTimestamp() - start;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                if (i == 0) Assert.Equal(writer.Write(value).Pixels, pixels.Pixels);
            }
            output.WriteLine($"round={round}; matrixMs={Ms(matrixTicks):F2}; pixelsMs={Ms(pixelsTicks):F2}; copyMs={Ms(copyTicks):F2}; drawMs={Ms(drawTicks):F2}; bytes={allocated}");
        }
    }

    private static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
