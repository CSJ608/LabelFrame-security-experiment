namespace LabelFrame.Core.Jobs;

/// <summary>单张标签的回放载荷：文本指令（Text）或二进制指令（Bytes）二选一（决策 #195）。</summary>
/// <param name="Text">文本形态指令（ZPL / TSPL 文本指令）；二进制载荷时为空串。</param>
/// <param name="Bytes">二进制形态指令（如 TSPL BITMAP 位图流）；文本载荷时为 null。</param>
public sealed record LabelJobPayload(string Text, byte[]? Bytes = null)
{
    /// <summary>文本载荷。</summary>
    public static LabelJobPayload OfText(string text) => new(text, null);

    /// <summary>二进制载荷。</summary>
    public static LabelJobPayload OfBytes(byte[] bytes) => new(string.Empty, bytes);
}
