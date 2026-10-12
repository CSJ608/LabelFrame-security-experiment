using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Tests.Layout;

/// <summary>
/// 版式解析区域锚定测试（方案 A； 补垂直锚定框语义断言）：
/// 自动宽度文本水平锚定按实测文本宽度计算（块宽仍为区域全宽、块内 TextAlign 语义不变）；
/// 显式宽度路径 / 垂直锚定 / 无度量器回退行为防回归。
/// </summary>
public class LabelLayoutResolverTests
{
    private const string Value = "示例文本";

    /// <summary>确定性度量器：固定返回宽度（毫米），不依赖字体环境。</summary>
    private sealed class FixedWidthMeasurer(double widthMm) : ITextWidthMeasurer
    {
        public double MeasureWidthMm(LabelTextElement element, string value) => widthMm;
    }

    private static Dictionary<string, LabelRegionElement> SingleRegion(double x, double y, double w, double h)
        => new()
        {
            ["r1"] = new LabelRegionElement { Id = "r1", XMm = x, YMm = y, WidthMm = w, HeightMm = h },
        };

    private static LabelTextElement Text(
        LabelRegionAlign? h = null,
        LabelRegionAlign? v = null,
        double widthMm = 0,
        double paddingHMm = 0,
        double paddingMm = 0,
        double fontHeightMm = 4,
        double heightMm = 0)
        => new()
        {
            Literal = Value,
            XMm = 0,
            YMm = 0,
            FontHeightMm = fontHeightMm,
            WidthMm = widthMm,
            HeightMm = heightMm,
            PaddingHMm = paddingHMm,
            PaddingMm = paddingMm,
            RegionId = "r1",
            RegionHAlign = h,
            RegionVAlign = v,
        };

    [Fact]
    public void Auto_width_text_horizontal_anchor_uses_measured_width()
    {
        // 区域 (10, 5, 40, 20)，实测宽度 8mm、无内边距 → 锚定宽 8，偏移 =（40 − 8）× 因子
        var regions = SingleRegion(10, 5, 40, 20);
        var measurer = new FixedWidthMeasurer(8);

        var start = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.Start), regions, measurer, Value);
        var center = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.Center), regions, measurer, Value);
        var end = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.End), regions, measurer, Value);

        Assert.Equal(10, start.XMm, 5);
        Assert.Equal(26, center.XMm, 5); // 10 + 32 × 0.5
        Assert.Equal(42, end.XMm, 5);    // 10 + 32 × 1

        // 块宽仍为区域全宽（供块内 TextAlign 使用，语义不变）
        Assert.Equal(40, start.WidthMm, 5);
        Assert.Equal(40, center.WidthMm, 5);
        Assert.Equal(40, end.WidthMm, 5);

        // 垂直锚定按块高（无 heightMm 取字高 4）不受影响
        Assert.Equal(13, start.YMm, 5); // 5 + (20 − 4) × 0.5（VAlign 缺省 Center）
        Assert.Equal(4, start.HeightMm, 5);
    }

    [Fact]
    public void Auto_width_text_anchor_includes_horizontal_padding()
    {
        // 实测 8 + 双边水平内边距 1×2 → 锚定宽 10：Start 时文本距区域左缘一个内边距、End 时距右缘一个内边距
        var regions = SingleRegion(10, 5, 40, 20);
        var measurer = new FixedWidthMeasurer(8);

        var start = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.Start, paddingHMm: 1), regions, measurer, Value);
        var end = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.End, paddingHMm: 1), regions, measurer, Value);

        Assert.Equal(10, start.XMm, 5); // 区域左缘
        Assert.Equal(40, end.XMm, 5);   // 10 + (40 − 10)
    }

    [Fact]
    public void Auto_width_text_anchor_clamps_to_region_width()
    {
        // 实测宽度超过区域宽：夹取到区域宽 → 偏移 0（块从区域左缘起，与溢出缩小 / 换行逻辑衔接）
        var regions = SingleRegion(10, 5, 40, 20);
        var measurer = new FixedWidthMeasurer(100);

        var end = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.End), regions, measurer, Value);
        Assert.Equal(10, end.XMm, 5);
        Assert.Equal(40, end.WidthMm, 5);
    }

    [Fact]
    public void Auto_width_text_anchor_matrix_combines_with_vertical_align()
    {
        // 3×3 组合：水平位置只由 RegionHAlign 决定，垂直位置只由 RegionVAlign 决定
        var regions = SingleRegion(10, 5, 40, 20);
        var measurer = new FixedWidthMeasurer(8);

        foreach (var (h, expectedX) in new[] { (LabelRegionAlign.Start, 10d), (LabelRegionAlign.Center, 26d), (LabelRegionAlign.End, 42d) })
        {
            foreach (var (v, expectedY) in new[] { (LabelRegionAlign.Start, 5d), (LabelRegionAlign.Center, 13d), (LabelRegionAlign.End, 21d) })
            {
                var bounds = LabelLayoutResolver.ResolveBounds(Text(h, v), regions, measurer, Value);
                Assert.Equal(expectedX, bounds.XMm, 5);
                Assert.Equal(expectedY, bounds.YMm, 5);
                Assert.Equal(40, bounds.WidthMm, 5);
                Assert.Equal(4, bounds.HeightMm, 5);
            }
        }
    }

    [Fact]
    public void Without_measurer_auto_width_falls_back_to_full_width_block()
    {
        // 未注入度量器（纯几何调用方）：既有行为——块宽 = 区域宽，偏移 ≈ 0（垂直锚定仍正常）
        var regions = SingleRegion(10, 5, 40, 20);

        var start = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.Start, LabelRegionAlign.Start), regions);
        var end = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.End, LabelRegionAlign.End), regions);

        Assert.Equal(10, start.XMm, 5);
        Assert.Equal(10, end.XMm, 5);
        Assert.Equal(5, start.YMm, 5);
        Assert.Equal(21, end.YMm, 5);
        Assert.Equal(40, start.WidthMm, 5);
        Assert.Equal(40, end.WidthMm, 5);
    }

    [Fact]
    public void Explicit_width_text_keeps_block_formula_unchanged()
    {
        // 显式宽度路径零变化：块宽 = WidthMm，偏移 =（区域宽 − 块宽）× 因子（度量器在场也不参与）
        var regions = SingleRegion(10, 5, 40, 20);
        var measurer = new FixedWidthMeasurer(8);

        var start = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.Start, widthMm: 12), regions, measurer, Value);
        var center = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.Center, widthMm: 12), regions, measurer, Value);
        var end = LabelLayoutResolver.ResolveBounds(Text(LabelRegionAlign.End, widthMm: 12), regions, measurer, Value);

        Assert.Equal(10, start.XMm, 5);
        Assert.Equal(24, center.XMm, 5); // 10 + (40 − 12) × 0.5
        Assert.Equal(38, end.XMm, 5);    // 10 + (40 − 12) × 1
        Assert.Equal(12, start.WidthMm, 5);
        Assert.Equal(12, center.WidthMm, 5);
        Assert.Equal(12, end.WidthMm, 5);
    }

    [Fact]
    public void Unanchored_text_returns_absolute_position_regardless_of_measurer()
    {
        // 未锚定区域：绝对坐标 + 自然尺寸，度量器不参与
        var measurer = new FixedWidthMeasurer(8);
        var text = new LabelTextElement { Literal = Value, XMm = 3, YMm = 7, FontHeightMm = 4, WidthMm = 0 };

        var bounds = LabelLayoutResolver.ResolveBounds(text, new Dictionary<string, LabelRegionElement>(), measurer, Value);

        Assert.Equal(3, bounds.XMm, 5);
        Assert.Equal(7, bounds.YMm, 5);
        Assert.Equal(0, bounds.WidthMm, 5);
        Assert.Equal(4, bounds.HeightMm, 5);
    }

    [Fact]
    public void Non_text_elements_keep_natural_size_anchoring()
    {
        // 条码 / 二维码按自然尺寸锚定（既有公式），不受度量器影响
        var regions = SingleRegion(10, 5, 40, 20);
        var measurer = new FixedWidthMeasurer(8);

        var barcode = new LabelBarcodeElement { Literal = "123456", HeightMm = 10, RegionId = "r1", RegionHAlign = LabelRegionAlign.End };
        var barcodeBounds = LabelLayoutResolver.ResolveBounds(barcode, regions, measurer, "123456");
        Assert.Equal(25, barcodeBounds.XMm, 5); // 10 + (40 − 25) × 1，条码自然宽 = 高 × 2.5
        Assert.Equal(25, barcodeBounds.WidthMm, 5);

        var qr = new LabelQrCodeElement { Literal = "abc", SizeMm = 12, RegionId = "r1", RegionHAlign = LabelRegionAlign.Center };
        var qrBounds = LabelLayoutResolver.ResolveBounds(qr, regions, measurer, "abc");
        Assert.Equal(24, qrBounds.XMm, 5); // 10 + (40 − 12) × 0.5
        Assert.Equal(12, qrBounds.WidthMm, 5);
    }

    [Fact]
    public void Anchored_auto_width_auto_height_text_bounds_center_inside_region_vertically()
    {
        // 区域居中锚定的自动宽度 + 自动高度文本——
        // 解析 bounds（锚定框 = 字高框，格顶语义）完整位于锚定盒内、垂直居中。
        // 渲染方必须在该框内绘制（此前误用 ≥10mm 兜底框再块内居中导致穿底，见渲染层回归测试）。
        var regions = SingleRegion(10, 5, 40, 20);
        var measurer = new FixedWidthMeasurer(8);

        var bounds = LabelLayoutResolver.ResolveBounds(
            Text(LabelRegionAlign.Center, LabelRegionAlign.Center, fontHeightMm: 2), regions, measurer, Value);

        // 锚定框 = 字高 2mm：y = 5 + (20 − 2) × 0.5 = 14，bounds = [14, 16]
        Assert.Equal(14, bounds.YMm, 5);
        Assert.Equal(2, bounds.HeightMm, 5);
        // 完整位于盒内（垂直，字高 ≤ 区域高时）
        Assert.True(bounds.YMm >= 5 - 0.01, $"bounds 顶 {bounds.YMm} 不得越出盒顶");
        Assert.True(bounds.YMm + bounds.HeightMm <= 25 + 0.01, $"bounds 底 {bounds.YMm + bounds.HeightMm} 不得越出盒底");
        // 垂直居中（区域中心 15，容差 ±0.01）
        Assert.InRange(bounds.YMm + bounds.HeightMm / 2, 15 - 0.01, 15 + 0.01);
    }

    [Fact]
    public void Anchored_auto_height_text_bounds_follow_region_valign_direction()
    {
        // 自动高度锚定框（字高框）随 RegionVAlign 方向贴边——Start 贴盒顶 / End 贴盒底。
        // 渲染方若在该框内绘制，Start/End 语义即「文本贴边」，与 Center 居中同一套框语义。
        var regions = SingleRegion(10, 5, 40, 20);
        var measurer = new FixedWidthMeasurer(8);

        var start = LabelLayoutResolver.ResolveBounds(
            Text(LabelRegionAlign.Center, LabelRegionAlign.Start, fontHeightMm: 2), regions, measurer, Value);
        var end = LabelLayoutResolver.ResolveBounds(
            Text(LabelRegionAlign.Center, LabelRegionAlign.End, fontHeightMm: 2), regions, measurer, Value);

        Assert.Equal(5, start.YMm, 5);                       // Start：框顶 = 盒顶
        Assert.Equal(23, end.YMm, 5);                        // End：框顶 = 5 + (20 − 2) × 1
        Assert.Equal(2, start.HeightMm, 5);
        Assert.True(end.YMm + end.HeightMm <= 25 + 0.01, "End 框底不得越出盒底");
    }
}
