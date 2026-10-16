namespace LabelFrame.Core.Transport;

/// <summary>打印传输：把打印机指令送到打印机。</summary>
public interface IPrintTransport
{
    /// <summary>发送打印机指令（文本形态）。</summary>
    /// <param name="command">打印机指令（如 ZPL / TSPL 文本指令）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送打印机指令（二进制形态，如 TSPL BITMAP 原始位图指令流）。
    /// 默认实现抛 NotSupportedException——不支持二进制指令的通道收到二进制载荷时显式失败
    /// （决策 #195：DIM 缺省实现保证既有插件与内置通道二进制兼容，不受本重载影响）。
    /// </summary>
    /// <param name="command">二进制指令流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendAsync(byte[] command, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("该传输通道不支持二进制打印机指令。");
}