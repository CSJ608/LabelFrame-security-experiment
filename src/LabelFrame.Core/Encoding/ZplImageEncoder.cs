using System.Globalization;
using System.Text;
using LabelFrame.Core.Documents;

namespace LabelFrame.Core.Encoding;

/// <summary>整版位图 ZPL 编码器：把整张标签的 1bpp 位图经 ^GF 输出（图片打印模式的物理载体）。</summary>
public sealed class ZplImageEncoder
{
    /// <summary>默认打印机分辨率（203 dpi，Zebra 常见）。</summary>
    public const int DefaultDpi = 203;

    /// <summary>整版位图编码：把整张标签的 1bpp 位图经 ^GF 输出（图片打印模式，所见即所得）。</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:成员可标记为 static",
        Justification = "经 DI 单例注入消费（JobSubmissionService / 测试页端点），保持实例形态")]
    public string EncodeImage(LabelBitmap bitmap, double widthMm, double heightMm, int dpi = DefaultDpi)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI 必须为正整数。");
        }

        var totalBytes = bitmap.RowBytes * bitmap.Height;
        var hex = Convert.ToHexString(bitmap.Pixels);
        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine(CultureInfo.InvariantCulture, $"^PW{Math.Max(1, ToDots(widthMm, dpi))}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"^LL{Math.Max(1, ToDots(heightMm, dpi))}");
        sb.Append(CultureInfo.InvariantCulture, $"^FO0,0^GFA,{totalBytes},{totalBytes},{bitmap.RowBytes},{hex}^FS").AppendLine();
        sb.Append("^XZ");
        return sb.ToString();
    }

    /// <summary>毫米 → 点，四舍五入（远离零）。</summary>
    private static int ToDots(double mm, int dpi)
        => Math.Max(0, (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero));
}