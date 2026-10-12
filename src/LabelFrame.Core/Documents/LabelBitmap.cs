namespace LabelFrame.Core.Documents;

/// <summary>单色（1bpp）位图：每行按字节对齐，高位在前（MSB），1 表示黑点——^GF 编码的线格式。</summary>
public sealed class LabelBitmap
{
    /// <summary>创建位图。</summary>
    public LabelBitmap(int width, int height, byte[]? pixels = null)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "宽度必须为正整数。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "高度必须为正整数。");
        }

        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[RowBytes * height];
        if (Pixels.Length != RowBytes * height)
        {
            throw new ArgumentException($"像素数据长度必须为 {RowBytes * height} 字节。", nameof(pixels));
        }
    }

    /// <summary>位图宽度（像素）。</summary>
    public int Width { get; }

    /// <summary>位图高度（像素）。</summary>
    public int Height { get; }

    /// <summary>每行字节数（按字节对齐）。</summary>
    public int RowBytes => (Width + 7) / 8;

    /// <summary>像素数据：RowBytes * Height，MSB 在前，1 表示黑点。</summary>
    public byte[] Pixels { get; }
}