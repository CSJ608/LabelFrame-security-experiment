using System.Reflection;
using LabelFrame.Rendering;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.Rendering;

namespace LabelFrame.WinHost.Tests.Rendering;

public class BarcodeBitmapTests
{
    internal static readonly Func<BitMatrix, SKBitmap> Convert = typeof(SkiaLabelRenderer)
        .GetMethod("ToSkBitmap", BindingFlags.NonPublic | BindingFlags.Static, [typeof(BitMatrix)])!
        .CreateDelegate<Func<BitMatrix, SKBitmap>>();

    [Theory]
    [InlineData(BarcodeFormat.CODE_128)]
    [InlineData(BarcodeFormat.QR_CODE)]
    public void Encoded_pixels_match_original_renderer(BarcodeFormat format)
    {
        foreach (var size in new[] { 1, 17, 201, 650 })
        foreach (var margin in new[] { 0, 2, 7 })
        foreach (var ecc in new[] { ErrorCorrectionLevel.L, ErrorCorrectionLevel.M, ErrorCorrectionLevel.Q, ErrorCorrectionLevel.H })
        {
            var options = format == BarcodeFormat.QR_CODE
                ? new QrCodeEncodingOptions { Width = size, Height = size, Margin = margin, ErrorCorrection = ecc }
                : new EncodingOptions { Width = size, Height = size == 650 ? 260 : size, Margin = margin, PureBarcode = true };
            var writer = new BarcodeWriter<PixelData> { Format = format, Options = options, Renderer = new PixelDataRenderer() };
            foreach (var value in format == BarcodeFormat.CODE_128
                         ? new[] { "LF-0001", "12345678901234567890", "aZ-!_09" }
                         : new[] { "LF-0001", "标签测试-0123456789", new string('X', 200) })
            {
                var matrix = writer.Encode(value);
                var before = matrix.ToString();
                var reference = writer.Write(value);
                using var bitmap = Convert(matrix);
                Assert.Equal(SKColorType.Bgra8888, bitmap.ColorType);
                Assert.Equal(SKAlphaType.Opaque, bitmap.AlphaType);
                Assert.Equal(reference.Width, bitmap.Width);
                Assert.Equal(reference.Height, bitmap.Height);
                for (var y = 0; y < bitmap.Height; y++)
                    Assert.True(reference.Pixels.AsSpan(y * bitmap.Width * 4, bitmap.Width * 4)
                        .SequenceEqual(bitmap.GetPixelSpan().Slice(y * bitmap.RowBytes, bitmap.Width * 4)));
                Assert.Equal(before, matrix.ToString());
            }
        }
    }

    [Fact]
    public void Arbitrary_rows_are_independent_and_concurrent_results_do_not_alias()
    {
        Parallel.For(0, 32, seed =>
        {
            var random = new Random(seed);
            var matrix = new BitMatrix(seed + 1, 37);
            for (var y = 0; y < matrix.Height; y++)
            for (var x = 0; x < matrix.Width; x++)
                matrix[x, y] = random.Next(2) == 1;
            using var first = Convert(matrix);
            using var second = Convert(matrix);
            Assert.NotEqual(first.GetPixels(), second.GetPixels());
            for (var y = 0; y < matrix.Height; y++)
            for (var x = 0; x < matrix.Width; x++)
                Assert.Equal(matrix[x, y] ? SKColors.Black : SKColors.White, first.GetPixel(x, y));
            first.Erase(SKColors.Red);
            Assert.NotEqual(SKColors.Red, second.GetPixel(0, 0));
        });
    }

    [Fact]
    public void Conversion_does_not_allocate_a_managed_full_image()
    {
        var matrix = new BitMatrix(650, 260);
        using (Convert(matrix)) { }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++)
        {
            using var bitmap = Convert(matrix);
        }
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 100_000);
    }
}
