using LabelFrame.Core.Layout;
using SkiaSharp;

namespace LabelFrame.Rendering;

/// <summary>
/// Skia 字型查找：文本渲染与字宽度量共用同一字型（保证锚定偏移的度量口径与实际绘制一致）。
/// </summary>
internal static class SkiaTypefaceLookup
{
    private const string FontFamily = "Microsoft YaHei";

    /// <summary>
    /// 创建文本字型：优先指定字体族（fontFamily，缺省回退微软雅黑）；文本含非 ASCII 字符时按
    /// 实际字符匹配系统回退字体，避免指定字体缺字型导致整段文本不绘制。
    /// 回退策略：逐字符取**首个非 ASCII 字符**（代理对取完整码点）交系统回退链匹配。
    /// 修复前硬编码以『中』匹配——任意非 ASCII 文本（假名 / 韩文 / 泰文等）都被套上中文字体，
    /// 韩文 / 泰文等在雅黑中无字形直接豆腐块（CJK 中心缺陷）。按实际字符匹配后各脚本由系统
    /// 回退链解析到含该脚本字形的字体（Windows 上韩文 → Malgun Gothic、泰文 → Leelawadee UI 等）。
    /// 两个已知取舍（单字型渲染管线固有限制，与修复前一致）：
    /// ① 中文样本不受影响——任意常用汉字经系统回退链均解析到与『中』相同的字体（按脚本路由），
    /// 中文渲染输出与修复前逐字节一致（位图哈希锚定）
    /// ② 首个非 ASCII 字符若属生僻范围（如扩展 B 区），可能匹配到仅覆盖该范围的字体，
    ///   其余常用字符缺字形——修复前以『中』硬编码规避此景，现按文本实际脚本优先保障（生僻首字符场景更罕见）。
    /// </summary>
    public static SKTypeface CreateTypeface(string fontFamily, string value)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? LabelTextElement.DefaultFontFamily : fontFamily;
        var preferred = SKTypeface.FromFamilyName(family) ?? SKTypeface.FromFamilyName(FontFamily) ?? SKTypeface.Default;
        if (string.IsNullOrEmpty(value))
        {
            return preferred;
        }

        // 逐字符找首个非 ASCII：ASCII 由 preferred 兜底覆盖（回退链也优先解析到拉丁字体，无需介入）；
        // 代理对按完整码点匹配（MatchCharacter 支持码点入参），孤立代理项属非法文本直接跳过。
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c <= 0x7F)
            {
                continue;
            }

            int codePoint;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    continue;
                }

                codePoint = char.ConvertToUtf32(value, i);
            }
            else if (char.IsLowSurrogate(c))
            {
                continue;
            }
            else
            {
                codePoint = c;
            }

            // 按实际字符匹配系统回退字体（含该字符字形的字体，脚本级路由）；无匹配时维持 preferred。
            return SKFontManager.Default.MatchCharacter(codePoint) ?? preferred;
        }

        return preferred;
    }
}
