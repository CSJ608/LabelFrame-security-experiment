using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Tspl;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// TSPL 图片模式编码器单测：输出 = ASCII 头（SIZE / DIRECTION / CLS / BITMAP 参数行）+ 位图原始字节 +
/// ASCII 尾（PRINT）——BITMAP 数据非十六进制转码；行优先 / MSB 在前与 LabelBitmap 一致，
/// 位极性相反（TSPL 0 = 印点），逐字节取反后落流（TT065-60 真机反色实证 + TSPL2 手册）。
/// </summary>
public class TsplImageEncoderTests
{
    [Fact]
    public void EncodeImage_should_lay_out_header_raw_bitmap_and_trailer()
    {
        // 8×2 位图（1 字节行 × 2 行）：0xFF = 全黑行，0x00 = 空白行——取反后 0x00 = 全印行、0xFF = 空白行
        var bitmap = new LabelBitmap(8, 2, [0xFF, 0x00]);

        var command = new TsplImageEncoder().EncodeImage(bitmap, 40, 20, 203);

        var header = Encoding.ASCII.GetBytes("SIZE 40 mm,20 mm\r\nDIRECTION 1\r\nCLS\r\nBITMAP 0,0,1,2,0,");
        var trailer = Encoding.ASCII.GetBytes("\r\nPRINT 1\r\n");
        Assert.Equal(header.Length + 2 + trailer.Length, command.Length);
        Assert.Equal(header, command[..header.Length]);
        Assert.Equal(trailer, command[^trailer.Length..]);
        Assert.Equal(0x00, command[header.Length]);
        Assert.Equal(0xFF, command[header.Length + 1]);
    }

    [Fact]
    public void EncodeImage_should_carry_exact_bitmap_bytes_in_row_major()
    {
        // 16×3 位图（2 字节行 × 3 行）：逐字节锚定（BITMAP 数据必须精确 width×height 字节，且为源字节取反）
        var pixels = new byte[] { 0x80, 0x01, 0x40, 0x02, 0x20, 0x04 };
        var inverted = new byte[] { 0x7F, 0xFE, 0xBF, 0xFD, 0xDF, 0xFB };
        var bitmap = new LabelBitmap(16, 3, pixels);

        var command = new TsplImageEncoder().EncodeImage(bitmap, 60, 30, 203);

        var offset = Encoding.ASCII.GetByteCount("SIZE 60 mm,30 mm\r\nDIRECTION 1\r\nCLS\r\nBITMAP 0,0,2,3,0,");
        Assert.Equal(inverted, command[offset..(offset + inverted.Length)]);
        Assert.Contains("BITMAP 0,0,2,3,0,", Encoding.ASCII.GetString(command));
    }

    [Fact]
    public void EncodeImage_with_invert_enabled_should_pass_pixels_through()
    {
        // 决策 #196 InvertBits 开启 = 跳过极性取反原样透传（位极性相反机型的用户侧自救）
        var bitmap = new LabelBitmap(8, 2, [0xFF, 0x00]);

        var command = new TsplImageEncoder().EncodeImage(bitmap, 40, 20, 203, new LabelImageEncodeOptions(InvertBits: true));

        var offset = Encoding.ASCII.GetByteCount("SIZE 40 mm,20 mm\r\nDIRECTION 1\r\nCLS\r\nBITMAP 0,0,1,2,0,");
        Assert.Equal(0xFF, command[offset]);
        Assert.Equal(0x00, command[offset + 1]);
    }

    [Fact]
    public void EncodeImage_with_null_options_should_match_default_output()
    {
        // 选项缺省与 null 等价（DIM 委托口径）；默认输出 = 取反
        var bitmap = new LabelBitmap(16, 3, [0x80, 0x01, 0x40, 0x02, 0x20, 0x04]);
        var encoder = new TsplImageEncoder();

        Assert.Equal(encoder.EncodeImage(bitmap, 60, 30, 203), encoder.EncodeImage(bitmap, 60, 30, 203, options: null));
    }

    [Fact]
    public void EncodeImage_with_non_positive_dpi_should_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TsplImageEncoder().EncodeImage(new LabelBitmap(8, 1), 40, 20, 0));
    }
}
