using LabelFrame.Core.Documents;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 图片模式编码能力（可选实现于 ITransportPlugin 同一插件对象，决策 #195）：
/// 把整张标签的 1bpp 位图编码为该品牌指令集的图片打印指令（如 TSPL BITMAP 原始二进制位图）。
/// 未实现的插件（Zebra 与全部内置通道）由宿主默认 ZplImageEncoder 输出 ^GF，行为不变。
/// </summary>
public interface ILabelImageEncoder
{
    /// <summary>整版位图编码：输出含指令头尾与位图数据的完整自包含指令流（二进制）。</summary>
    byte[] EncodeImage(LabelBitmap bitmap, double widthMm, double heightMm, int dpi);
}
