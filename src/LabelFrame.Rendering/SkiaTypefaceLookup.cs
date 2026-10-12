using LabelFrame.Core.Layout;
using SkiaSharp;

namespace LabelFrame.Rendering;

/// <summary>
/// Skia 字型查找：文本渲染与字宽度量共用同一字型（保证锚定偏移的度量口径与实际绘制一致）。
/// </summary>
internal static class SkiaTypefaceLookup
{
    private const string FontFamily = "Microsoft YaHei";

    /// <summary>创建文本字型：优先指定字体族（缺省回退微软雅黑）；文本含非 ASCII 字符时按实际字符匹配系统回退字体，避免指定字体缺字形导致整段不绘制。</summary>
    public static SKTypeface CreateTypeface(string fontFamily, string value)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? LabelTextElement.DefaultFontFamily : fontFamily;
        var preferred = SKTypeface.FromFamilyName(family) ?? SKTypeface.FromFamilyName(FontFamily) ?? SKTypeface.Default;
        if (string.IsNullOrEmpty(value))
        {
            return preferred;
        }

        // 逐字符找首个非 ASCII（代理对取完整码点交 MatchCharacter，孤立代理项跳过）
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

            // 回退必须按文本实际字符匹配——退回硬编码样本字（如「中」）会给韩文 / 泰文等套中文字体致豆腐块
            return SKFontManager.Default.MatchCharacter(codePoint) ?? preferred;
        }

        return preferred;
    }
}
