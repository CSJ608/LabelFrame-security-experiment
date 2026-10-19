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

    /// <summary>
    /// 带连接级选项的编码（决策 #196 加法）：DIM 委托旧签名——未实现选项的插件行为逐字节不变，
    /// 支持可调项的指令集按需覆写。
    /// </summary>
    byte[] EncodeImage(LabelBitmap bitmap, double widthMm, double heightMm, int dpi, LabelImageEncodeOptions? options)
        => EncodeImage(bitmap, widthMm, heightMm, dpi);
}

/// <summary>
/// 图片编码连接级选项（决策 #196）。InvertBits = 位极性反转：位极性分歧机型（打印出黑底白字）的
/// 用户侧自救开关，默认 false = 插件指令集默认极性行为。
/// </summary>
public sealed record LabelImageEncodeOptions(bool InvertBits = false)
{
    /// <summary>「反色处理」参数键约定（插件参数表声明 → 宿主分派读取，对齐 TransportPrintMode.ParameterKey 先例）。</summary>
    public const string InvertBitsParameterKey = "invertImage";
}
