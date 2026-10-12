namespace LabelFrame.TransportPlugin.Zebra;

/// <summary>
/// Zebra 打印机内置字体度量（DESIGN 「SDK 内置字体度量接 ITextWidthMeasurer」）。
/// 用途仅限区域锚定 / 对齐的起始坐标编译期换算（功能裁剪原则——对齐为尽力近似）
/// 不追求与打印机实际字形位级一致：
/// 位图字体按 ZPL II 编程指南 203 dpi 基准设计点阵（字符高 × 宽，点）取单元格宽度；
/// 缩放字体 0（Swiss 721 家族）按 Helvetica 同度量的比例字宽表估算（1/1000 字高）。
/// </summary>
internal static class ZebraBuiltInFontMetrics
{
    /// <summary>默认打印机分辨率（203 dpi，Zebra 常见；与图片模式 ZplImageEncoder.DefaultDpi 同源）。</summary>
    public const int DefaultDpi = 203;

    /// <summary>常用位图字体的官方设计尺寸（高 × 宽，点；缩放倍率 = 请求字高点 ÷ 设计高点）。</summary>
    internal static readonly IReadOnlyDictionary<char, (int HeightDots, int WidthDots)> BitmapFonts =
        new Dictionary<char, (int, int)>
        {
            ['A'] = (9, 5),    // 大写字母与数字
            ['B'] = (11, 7),   // 大小写（含下伸部）
            ['C'] = (18, 10),  // 大小写（含下伸部）
            ['D'] = (18, 10),  // 大小写（压缩）
        };

    /// <summary>
    /// 缩放字体 0 的比例字宽（1/1000 字高，Helvetica 同度量；索引 0 = U+0020 起的可打印 ASCII，共 95 项）。
    /// Swiss 721 与 Helvetica 度量兼容；此表仅服务锚定近似的确定性换算，非打印机官方数据文件。
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

    /// <summary>是否为编译器支持的内置字体字符（A-Z / 0-9 单字符；其余回退默认字体 0）。</summary>
    public static bool IsBuiltInFontChar(char c)
        => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9');

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
