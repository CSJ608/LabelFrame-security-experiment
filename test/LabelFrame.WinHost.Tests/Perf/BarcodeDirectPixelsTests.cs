using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit.Abstractions;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.Rendering;

namespace LabelFrame.WinHost.Tests.Perf;

public class BarcodeDirectPixelsTests(ITestOutputHelper output)
{
    [Fact]
    public void Direct_pixels_match_reference_and_measure_alternating_paths()
    {
        foreach (var format in new[] { BarcodeFormat.CODE_128, BarcodeFormat.QR_CODE })
        foreach (var size in new[] { 1, 17, 201, 650 })
        foreach (var margin in new[] { 0, 2, 7 })
        foreach (var ecc in new[] { ErrorCorrectionLevel.L, ErrorCorrectionLevel.M, ErrorCorrectionLevel.Q, ErrorCorrectionLevel.H })
        {
            var writer = CreateWriter(format, size, size == 650 ? 260 : size, margin, ecc);
            foreach (var value in format == BarcodeFormat.CODE_128
                         ? new[] { "LF-0001", "12345678901234567890", "aZ-!_09" }
                         : new[] { "LF-0001", "标签测试-0123456789", new string('X', 200) })
            {
                var reference = writer.Write(value);
                using var direct = Direct(writer.Encode(value));
                Assert.Equal(reference.Width, direct.Width);
                Assert.Equal(reference.Height, direct.Height);
                for (var y = 0; y < direct.Height; y++)
                    Assert.True(reference.Pixels.AsSpan(y * direct.Width * 4, direct.Width * 4)
                        .SequenceEqual(direct.GetPixelSpan().Slice(y * direct.RowBytes, direct.Width * 4)));
            }
        }

        for (var round = 0; round < 4; round++)
        foreach (var directPath in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
        {
            var writer = CreateWriter(BarcodeFormat.CODE_128, 650, 260, 2, ErrorCorrectionLevel.M);
            long ticks = 0, allocated = 0;
            for (var i = 0; i < 200; i++)
            {
                var matrix = writer.Encode($"LF-{i + 1:0000}");
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                using var bitmap = directPath ? Direct(matrix) : Reference(writer, matrix);
                ticks += Stopwatch.GetTimestamp() - start;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            output.WriteLine($"round={round}; direct={directPath}; pixelAndBitmapMs={ticks * 1000d / Stopwatch.Frequency:F2}; bytes={allocated}");
        }
    }

    private static BarcodeWriter<PixelData> CreateWriter(BarcodeFormat format, int width, int height, int margin, ErrorCorrectionLevel ecc) => new()
    {
        Format = format,
        Options = format == BarcodeFormat.QR_CODE
            ? new QrCodeEncodingOptions { Width = width, Height = height, Margin = margin, ErrorCorrection = ecc }
            : new EncodingOptions { Width = width, Height = height, Margin = margin, PureBarcode = true },
        Renderer = new PixelDataRenderer(),
    };

    private static SKBitmap Reference(BarcodeWriter<PixelData> writer, BitMatrix matrix)
    {
        var pixels = writer.Renderer.Render(matrix, writer.Format, "LF-0001", writer.Options);
        var bitmap = new SKBitmap(new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(pixels.Pixels, 0, bitmap.GetPixels(), pixels.Pixels.Length);
        return bitmap;
    }

    private static SKBitmap Direct(BitMatrix matrix)
        => Rendering.BarcodeBitmapTests.Convert(matrix);
}
