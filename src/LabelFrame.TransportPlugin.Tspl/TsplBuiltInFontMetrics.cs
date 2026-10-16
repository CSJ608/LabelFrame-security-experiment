namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// TSPL 打印机内置字体度量（TSPL2 缩放字体 "0" = Monotype CG Triumvirate Bold Condensed，
/// Helvetica 同度量的比例字体）：仅供区域锚定 / 对齐起始坐标的编译期换算（尽力近似），
/// 不追求与打印机实际字形位级一致。点阵字体 1~5（8×12..32×48）不支持连续字高缩放，编译器不使用。
/// </summary>
internal static class TsplBuiltInFontMetrics
{
    /// <summary>
    /// 缩放字体 0 的比例字宽（1/1000 字高，Helvetica 同度量——Triumvirate 即 Helvetica 度量设计；
    /// 索引 0 = U+0020 起的可打印 ASCII，共 95 项）。此表仅服务锚定近似的确定性换算，非打印机官方数据文件。
    /// </summary>
    internal static readonly int[] ScalableCharWidths1000 =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, // U+0020 ' ' .. '/'
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, // '0' .. '?'
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, // '@' .. 'O'
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, // 'P' .. '_'
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, // '`' .. 'o'
        556, 556, 500, 556, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,      // 'p' .. '~'
    ];

    /// <summary>Latin-1 补充区（U+00A0..U+00FF）的兜底比例字宽（1/1000 字高；尽力近似，不逐字建模）。</summary>
    internal const int Latin1FallbackWidth1000 = 556;

    /// <summary>缩放字体单字符比例字宽（相对字高）；覆盖范围外返回 Latin-1 兜底值。</summary>
    internal static double ScalableCharWidthRatio(char c)
    {
        if (c is >= ' ' and <= (char)0x7E)
        {
            return ScalableCharWidths1000[c - ' '] / 1000.0;
        }

        return Latin1FallbackWidth1000 / 1000.0;
    }
}
