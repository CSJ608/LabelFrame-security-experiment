using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;
using SkiaSharp;

namespace LabelFrame.WinHost.Tests.Rendering;

/// <summary>
/// SkiaTypefaceLookup 回退策略单测：
/// 非中文字符按实际字符匹配（假名 / 韩文样本，存在性探测 + 缺失跳过，不假绿）
/// 中文样本与修复前基线（硬编码 MatchCharacter（'中'））渲染逐字节一致（位图哈希对比防回归）。
/// </summary>
public class SkiaTypefaceLookupTests
{
    private const string ChineseSample = "5m门架O20起升拉线固定支架";

    // —— ：中文零回归 ——

    [Fact]
    public void Chinese_sample_typeface_should_match_pre_fix_fallback()
    {
        var sample = ChineseSample;
        // 修复前基线逻辑（前）：任一非 ASCII → 固定以『中』匹配系统回退字体
        using var legacy = LegacyChineseTypeface();
        using var current = SkiaTypefaceLookup.CreateTypeface(null!, sample);

        // 同族同款 ⇒ 同一物理字体 ⇒ 渲染管线收到完全一致的字型（中文渲染逐字节一致的充分条件）：
        // 常用汉字经系统回退链按脚本路由，与『中』解析到同一字体（探针实证：中 / 门 / 机 → 同族）。
        // SKFontStyle 未重写 Equals（按本机句柄比较），故比较样式三分量。
        Assert.Equal(legacy.FamilyName, current.FamilyName);
        Assert.Equal(legacy.FontStyle.Weight, current.FontStyle.Weight);
        Assert.Equal(legacy.FontStyle.Width, current.FontStyle.Width);
        Assert.Equal(legacy.FontStyle.Slant, current.FontStyle.Slant);
    }

    [Fact]
    public void Chinese_sample_bitmap_hash_should_be_identical_to_pre_fix_fallback()
    {
        var sample = ChineseSample;
        // 基线字型（修复前路径）与现行为字型分别以同一参数栅格化，1 字节灰度位图逐字节一致（SHA-256 相等）——
        // 同进程内自洽对比（基线逻辑在测试内复刻），不受环境字体差异影响，可在任意 Windows 环境复跑。
        using var legacy = LegacyChineseTypeface();
        using var current = SkiaTypefaceLookup.CreateTypeface(null!, sample);
        Assert.Equal(Sha256(Rasterize(legacy, sample)), Sha256(Rasterize(current, sample)));
    }

    [Fact]
    public void Chinese_label_bitmap_should_be_deterministic_across_renders()
    {
        // 端到端：整版中文标签经公开渲染路径两次渲染（含中间态表面池复用路径），像素逐字节一致
        var first = new SkiaLabelRenderer().RenderLabelBitmap(ChineseLabelDocument(), dpi: 203);
        var second = new SkiaLabelRenderer().RenderLabelBitmap(ChineseLabelDocument(), dpi: 203);

        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.Pixels, second.Pixels);
        Assert.Equal(Sha256(first.Pixels), Sha256(second.Pixels));
        // 渲染有效性锚定：整图必须有足量墨迹（防「空图也逐字节一致」的假通过）
        var ink = 0;
        foreach (var b in first.Pixels)
        {
            ink += System.Numerics.BitOperations.PopCount(b);
        }

        Assert.True(ink > 500, $"中文标签应有足量墨迹：{ink}");
    }

    // —— ：非中文字符按实际字符匹配（存在性探测 + 缺失跳过，不假绿） ——

    [SkippableFact]
    public void Kana_sample_should_fall_back_by_actual_kana_character()
    {
        // 存在性探测：MatchCharacter 本身即「环境存在含该字符字形的字体」的探测
        using var expected = SKFontManager.Default.MatchCharacter('あ');
        Skip.If(expected is null, "环境无含假名字形的系统字体——AC-01 环境豁免（跳过，非假绿）");

        var sample = "あいうえおカキクケコ";
        // 场景 1：无显式字体（缺省微软雅黑——其覆盖假名字形但非假名正字场景，回退仍按实际字符匹配）
        using var noExplicit = SkiaTypefaceLookup.CreateTypeface(null!, sample);
        // 场景 2：preferred 未含假名字形（拉丁字体不含假名）
        using var preferredMiss = SkiaTypefaceLookup.CreateTypeface("Segoe UI", sample);

        var family = expected!.FamilyName;
        Assert.Equal(family, noExplicit.FamilyName);
        Assert.Equal(family, preferredMiss.FamilyName);
        // 所选字体必须覆盖全部假名字形（无豆腐块）——修复前硬编码『中』时韩文 / 泰文曾因此豆腐
        Assert.All(noExplicit.GetGlyphs(sample), g => Assert.NotEqual((ushort)0, g));
    }

    [SkippableFact]
    public void Hangul_sample_should_fall_back_by_actual_hangul_character()
    {
        using var expected = SKFontManager.Default.MatchCharacter('안');
        Skip.If(expected is null, "环境无含韩文字形的系统字体——AC-01 环境豁免（跳过，非假绿）");

        var sample = "안녕하세요";
        using var actual = SkiaTypefaceLookup.CreateTypeface(null!, sample);

        // 韩文在雅黑中无字形（修复前以『中』匹配 → 豆腐块）；现按实际字符命中韩文字体（如 Malgun Gothic）
        Assert.Equal(expected!.FamilyName, actual.FamilyName);
        Assert.All(actual.GetGlyphs(sample), g => Assert.NotEqual((ushort)0, g));
    }

    // —— 码点处理：代理对按完整码点匹配、孤立代理不抛错 ——

    [SkippableFact]
    public void Supplementary_plane_character_should_match_by_full_code_point()
    {
        using var expected = SKFontManager.Default.MatchCharacter(0x20000); // 𠀀（CJK 扩展 B）
        Skip.If(expected is null, "环境无覆盖 CJK 扩展 B 区的字体——跳过");

        using var actual = SkiaTypefaceLookup.CreateTypeface(null!, "\U00020000");
        Assert.Equal(expected!.FamilyName, actual.FamilyName);
    }

    [Fact]
    public void Lone_surrogate_text_should_not_throw_and_keep_preferred()
    {
        // 孤立高代理（非法 UTF-16 文本）+ ASCII：跳过非法单元，不抛异常，维持 preferred 字体
        using var actual = SkiaTypefaceLookup.CreateTypeface(null!, "\uD840abc");
        using var preferred = SKTypeface.FromFamilyName(LabelTextElement.DefaultFontFamily);
        Assert.Equal(preferred.FamilyName, actual.FamilyName);
    }

    /// <summary>复刻修复前（前）的中文回退路径：任一非 ASCII 固定以『中』匹配。</summary>
    private static SKTypeface LegacyChineseTypeface()
        => SKFontManager.Default.MatchCharacter('中')
            ?? SKTypeface.FromFamilyName(LabelTextElement.DefaultFontFamily)
            ?? SKTypeface.Default;

    private static LabelDocument ChineseLabelDocument() => new()
    {
        Layout = new LabelLayout
        {
            Name = "cjk",
            ContractName = "cjk",
            ContractVersion = "1.0",
            WidthMm = 70,
            HeightMm = 50,
            Elements =
            [
                new LabelTextElement { SourceKey = "t", XMm = 5, YMm = 5, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 60 },
                new LabelTextElement { SourceKey = "c", XMm = 5, YMm = 30, FontHeightMm = 2, FontWidthMm = 2, WidthMm = 60 },
            ],
        },
        Data = new Dictionary<string, string>
        {
            ["t"] = ChineseSample,
            ["c"] = "劢微机器人科技（深圳）有限公司",
        },
    };

    /// <summary>以指定字型栅格化文本为 1 字节灰度位图（与整版渲染一致的 BGRA→灰度口径）。</summary>
    private static byte[] Rasterize(SKTypeface typeface, string text)
    {
        const int width = 256;
        const int height = 64;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var font = new SKFont(typeface, 36);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        canvas.DrawText(text, 4, 48, SKTextAlign.Left, font, paint);

        var rowBytes = bitmap.RowBytes;
        var staging = new byte[rowBytes * height];
        Marshal.Copy(bitmap.GetPixels(), staging, 0, staging.Length);
        var luma = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * rowBytes + x * 4;
                luma[y * width + x] = (byte)((staging[offset + 2] * 299 + staging[offset + 1] * 587 + staging[offset] * 114) / 1000);
            }
        }

        return luma;
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
