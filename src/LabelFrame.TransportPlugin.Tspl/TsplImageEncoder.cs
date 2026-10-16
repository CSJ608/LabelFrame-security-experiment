using System.Globalization;
using System.Text;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// TSPL 图片模式编码器（ILabelImageEncoder）：整张标签的 1bpp 位图经 BITMAP 原始二进制输出
/// （mode 0 = OVERWRITE；LabelBitmap 的行优先 / MSB 在前 / 1=黑点线格式与 TSPL BITMAP 数据一致）。
/// 输出为二进制指令流（头尾 ASCII 指令 + 位图原始字节），经 LabelJobItem.CommandBytes 走二进制回放。
/// </summary>
public sealed class TsplImageEncoder : ILabelImageEncoder
{
    /// <inheritdoc />
    public byte[] EncodeImage(LabelBitmap bitmap, double widthMm, double heightMm, int dpi)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI 必须为正整数。");
        }

        var header = new StringBuilder();
        header.Append("SIZE ").Append(Mm(widthMm)).Append(" mm,").Append(Mm(heightMm)).Append(" mm\r\n");
        header.Append("DIRECTION 1\r\n");
        header.Append("CLS\r\n");
        header.Append(CultureInfo.InvariantCulture, $"BITMAP 0,0,{bitmap.RowBytes},{bitmap.Height},0,");

        var trailer = "\r\nPRINT 1\r\n"u8;

        var head = Encoding.ASCII.GetBytes(header.ToString());
        var command = new byte[head.Length + bitmap.Pixels.Length + trailer.Length];
        head.CopyTo(command, 0);
        bitmap.Pixels.CopyTo(command, head.Length);
        trailer.CopyTo(command.AsSpan(head.Length + bitmap.Pixels.Length));
        return command;
    }

    /// <summary>SIZE 指令的毫米数（最多三位小数、不变文化）。</summary>
    private static string Mm(double mm)
        => mm.ToString("0.###", CultureInfo.InvariantCulture);
}
