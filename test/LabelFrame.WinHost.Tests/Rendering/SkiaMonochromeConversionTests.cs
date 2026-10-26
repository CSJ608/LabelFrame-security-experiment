using System.Reflection;
using LabelFrame.Core.Documents;
using LabelFrame.Rendering;

namespace LabelFrame.WinHost.Tests.Rendering;

public class SkiaMonochromeConversionTests
{
    internal delegate LabelBitmap PixelConverter(int width, int height, int rowBytes, ReadOnlySpan<byte> bytes);

    internal static readonly PixelConverter ConvertSpan = typeof(SkiaLabelRenderer)
        .GetMethod("ToLabelBitmap", BindingFlags.NonPublic | BindingFlags.Static)!
        .CreateDelegate<PixelConverter>();

    internal static LabelBitmap ConvertPixels(int width, int height, int rowBytes, byte[] bytes)
        => ConvertSpan(width, height, rowBytes, bytes);

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(7, 3, 12)]
    [InlineData(8, 2, 4)]
    [InlineData(9, 5, 8)]
    [InlineData(1181, 709, 0)]
    public void Conversion_should_preserve_reference_bytes_and_input(int width, int height, int padding)
    {
        var rowBytes = width * 4 + padding;
        var input = new byte[rowBytes * height];
        new Random(42).NextBytes(input);
        var copy = input.ToArray();
        var expected = Reference(width, height, rowBytes, input);
        var actual = ConvertPixels(width, height, rowBytes, input);
        Assert.Equal(expected.Pixels, actual.Pixels);
        Assert.Equal(copy, input);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
    }

    [Fact]
    public void Every_rgb_combination_should_preserve_threshold_and_bit_order()
    {
        const int width = 65536, height = 256;
        var input = new byte[width * height * 4];
        for (var rgb = 0; rgb < 1 << 24; rgb++)
        {
            input[rgb * 4] = (byte)rgb;
            input[rgb * 4 + 1] = (byte)(rgb >> 8);
            input[rgb * 4 + 2] = (byte)(rgb >> 16);
            input[rgb * 4 + 3] = (byte)(rgb % 256);
        }
        Assert.Equal(Reference(width, height, width * 4, input).Pixels,
            ConvertPixels(width, height, width * 4, input).Pixels);
    }

    internal static LabelBitmap Reference(int width, int height, int rowBytes, byte[] bytes)
    {
        var result = new LabelBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * rowBytes + x * 4;
            var luma = (bytes[offset + 2] * 299 + bytes[offset + 1] * 587 + bytes[offset] * 114) / 1000;
            if (luma < 128) result.Pixels[y * result.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }
        return result;
    }
}
