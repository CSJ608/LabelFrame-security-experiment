using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;

namespace LabelFrame.WinHost.Tests.Rendering;

/// <summary>
/// 渲染器元素覆盖补全：图片 / 线 / 区域三种元素此前零测试（文本 / 条码 / 二维码已有）；
/// 补锚定自动高度文本垂直坐标语义回归（绘制框 = 解析锚定框，不穿锚定盒底边）。
/// 断言策略与 SkiaLabelRendererTests 一致：按毫米区域数墨点（不依赖具体字体渲染）。
/// </summary>
public class SkiaRendererElementTests
{
    private const int Dpi = 203;

    private static int CountBlack(LabelBitmap bitmap, double xMm, double yMm, double wMm, double hMm)
    {
        var black = 0;
        var x0 = (int)Math.Round(xMm / 25.4 * Dpi);
        var x1 = (int)Math.Round((xMm + wMm) / 25.4 * Dpi);
        var y0 = (int)Math.Round(yMm / 25.4 * Dpi);
        var y1 = (int)Math.Round((yMm + hMm) / 25.4 * Dpi);
        for (var y = y0; y < y1 && y < bitmap.Height; y++)
        {
            for (var x = x0; x < x1 && x < bitmap.Width; x++)
            {
                if ((bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0)
                {
                    black++;
                }
            }
        }

        return black;
    }

    private static LabelBitmap Render(LabelLayout layout, IReadOnlyDictionary<string, byte[]>? images = null)
        => new SkiaLabelRenderer().RenderLabelBitmap(new LabelDocument { Layout = layout, Data = new Dictionary<string, string>() }, Dpi, images);

    /// <summary>生成纯黑 8x8 PNG（图片元素的模板资源）。</summary>
    private static byte[] BlackSquarePng()
    {
        using var bmp = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(8, 8, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque));
        bmp.Erase(SkiaSharp.SKColors.Black);
        using var image = SkiaSharp.SKImage.FromBitmap(bmp);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    [Fact]
    public void Image_element_should_render_template_image_pixels()
    {
        var bitmap = Render(new LabelLayout
        {
            Name = "img", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements = [new LabelImageElement { SourceKey = "logo", XMm = 2, YMm = 2, WidthMm = 10, HeightMm = 10 }],
        }, new Dictionary<string, byte[]> { ["logo"] = BlackSquarePng() });

        // 图片区域应有大量墨点（8x8 黑块放大到 10mm×10mm ≈ 80×80 点）
        Assert.True(CountBlack(bitmap, 2.5, 2.5, 9, 9) > 3000, "图片区域应有大面积墨迹");
        // 图片外区域应空白
        Assert.Equal(0, CountBlack(bitmap, 20, 2, 15, 15));
    }

    [Fact]
    public void Image_element_without_resource_should_throw_with_key_name()
    {
        // 改写：缺资源由「灰占位照打（无墨仅边框）」变为
        // 显式报错（预览 / 出图 / 打印链失败并透出键名）——灰占位呈现归设计器画布与属性面板。
        var ex = Assert.Throws<MissingImageResourceException>(() => Render(new LabelLayout
        {
            Name = "img-missing", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements = [new LabelImageElement { SourceKey = "missing", XMm = 2, YMm = 2, WidthMm = 10, HeightMm = 10 }],
        }));
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void Line_element_should_render_horizontal_and_vertical_strokes()
    {
        var bitmap = Render(new LabelLayout
        {
            Name = "line", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements =
            [
                new LabelLineElement { XMm = 2, YMm = 10, X2Mm = 38, Y2Mm = 10 },          // 横线
                new LabelLineElement { XMm = 20, YMm = 2, X2Mm = 20, Y2Mm = 18 },          // 竖线
            ],
        });

        // 横线带（y=10mm ±1mm）与竖线带（x=20mm ±1mm）均有墨迹，交叉区外空白
        Assert.True(CountBlack(bitmap, 5, 9.5, 10, 1) > 20, "横线应有墨迹");
        Assert.True(CountBlack(bitmap, 19.5, 4, 1, 10) > 20, "竖线应有墨迹");
        Assert.Equal(0, CountBlack(bitmap, 25, 3, 10, 5));
    }

    [Fact]
    public void Region_element_should_render_border_rectangle_only()
    {
        var bitmap = Render(new LabelLayout
        {
            Name = "region", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements =
            [
                new LabelRegionElement { Id = "r1", XMm = 2, YMm = 2, WidthMm = 30, HeightMm = 15, BorderMm = 0.4 },
                // 元素锚定区域（Start/Center）应正常渲染在区域内
                new LabelTextElement { Literal = "区域文本", XMm = 0, YMm = 0, FontHeightMm = 4, RegionId = "r1", RegionHAlign = LabelRegionAlign.Start, RegionVAlign = LabelRegionAlign.Start },
            ],
        });

        // 区域边框：四条边有墨迹
        Assert.True(CountBlack(bitmap, 2, 2, 30, 0.6) > 10, "区域上边框");
        Assert.True(CountBlack(bitmap, 2, 16.4, 30, 0.6) > 10, "区域下边框");
        Assert.True(CountBlack(bitmap, 2, 2, 0.6, 15) > 10, "区域左边框");
        Assert.True(CountBlack(bitmap, 31.4, 2, 0.6, 15) > 10, "区域右边框");
        // 区域中心空白（仅有边框与锚定文本）
        Assert.Equal(0, CountBlack(bitmap, 12, 10, 8, 5));
        // 锚定文本渲染在区域左上角
        Assert.True(CountBlack(bitmap, 2.5, 2.5, 15, 6) > 30, "区域锚定文本应有墨迹");
    }

    [Fact]
    public void Region_anchored_element_should_align_when_configured()
    {
        // 对比式断言：同一文本以不同锚定渲染，墨迹重心应随锚定方向移动。
        // 显式宽度（块位置由对齐系数计算）与自动宽度（按实测文本宽度计算偏移）都应生效。
        LabelBitmap RenderWith(LabelRegionAlign h, LabelRegionAlign v, double widthMm) => Render(new LabelLayout
        {
            Name = "anchor-" + h + "-" + v, ContractName = "c", ContractVersion = "1.0", WidthMm = 60, HeightMm = 30,
            Elements =
            [
                new LabelRegionElement { Id = "r1", XMm = 2, YMm = 2, WidthMm = 56, HeightMm = 26 },
                new LabelTextElement { Literal = "锚定对比文本", XMm = 0, YMm = 0, FontHeightMm = 4, WidthMm = widthMm, RegionId = "r1", RegionHAlign = h, RegionVAlign = v },
            ],
        });

        // 垂直锚定（无显式宽度，高度取字高）：Center 的墨迹应明显靠下
        var (_, startY) = InkCenter(RenderWith(LabelRegionAlign.Start, LabelRegionAlign.Start, 0));
        var (_, centerY) = InkCenter(RenderWith(LabelRegionAlign.Start, LabelRegionAlign.Center, 0));
        Assert.True(centerY > startY + 2, $"垂直锚定未生效：start y={startY:F1}, center y={centerY:F1}");

        // 水平锚定——显式宽度：Center 的墨迹应明显靠右
        var (startX, _) = InkCenter(RenderWith(LabelRegionAlign.Start, LabelRegionAlign.Start, 12));
        var (centerX, _) = InkCenter(RenderWith(LabelRegionAlign.Center, LabelRegionAlign.Center, 12));
        Assert.True(centerX > startX + 5, $"水平锚定未生效：start x={startX:F1}, center x={centerX:F1}");

        // 水平锚定——自动宽度（方案 A，实测文本宽度参与计算）：Start / Center / End 墨迹重心递增
        var (autoStartX, _) = InkCenter(RenderWith(LabelRegionAlign.Start, LabelRegionAlign.Start, 0));
        var (autoCenterX, _) = InkCenter(RenderWith(LabelRegionAlign.Center, LabelRegionAlign.Center, 0));
        var (autoEndX, _) = InkCenter(RenderWith(LabelRegionAlign.End, LabelRegionAlign.End, 0));
        Assert.True(autoCenterX > autoStartX + 5, $"自动宽度水平锚定未生效：start x={autoStartX:F1}, center x={autoCenterX:F1}");
        Assert.True(autoEndX > autoCenterX + 5, $"自动宽度水平锚定未生效：center x={autoCenterX:F1}, end x={autoEndX:F1}");
    }

    [Fact]
    public void Region_anchored_auto_width_text_should_stay_inside_region_for_all_aligns()
    {
        // 方案 A 语义：自动宽度文本按实测宽度锚定，Start 贴左 / End 贴右，文本不越出区域
        LabelBitmap RenderWith(LabelRegionAlign h) => Render(new LabelLayout
        {
            Name = "auto-" + h, ContractName = "c", ContractVersion = "1.0", WidthMm = 60, HeightMm = 30,
            Elements =
            [
                new LabelRegionElement { Id = "r1", XMm = 2, YMm = 2, WidthMm = 56, HeightMm = 26 },
                new LabelTextElement { Literal = "文本", XMm = 0, YMm = 0, FontHeightMm = 4, RegionId = "r1", RegionHAlign = h, RegionVAlign = LabelRegionAlign.Center },
            ],
        });

        // Start：区域左缘外（0~2mm）空白、左缘内侧有墨迹；End：区域右缘外（58~60mm）空白、右缘内侧有墨迹
        var start = RenderWith(LabelRegionAlign.Start);
        var end = RenderWith(LabelRegionAlign.End);
        Assert.Equal(0, CountBlack(start, 0, 2, 1.8, 26));
        Assert.True(CountBlack(start, 2.2, 2, 8, 26) > 20, "Start 锚定文本应在区域左缘内侧");
        Assert.Equal(0, CountBlack(end, 58.2, 2, 1.8, 26));
        Assert.True(CountBlack(end, 49, 2, 8, 26) > 20, "End 锚定文本应在区域右缘内侧");
    }

    [Fact]
    public void Region_anchored_auto_height_text_should_center_inside_region_without_piercing_bottom()
    {
        // 区域居中锚定自动宽度 + 自动高度文本——绘制框 = 解析锚定框（字高框）
        // 墨迹完整位于锚定盒内且垂直居中。修复前误用决策 A 兜底框（max(字高 + 2×内边距, 10mm) = 10mm）
        // 再按缺省 Middle 块内居中，字形中心比区域中心下沉 （10 − 2）/2 = 4mm = 2 字高，穿出盒底边（真机陪验）。
        // 区域无边框（区域自身不绘制），全版墨迹即文本墨迹，InkCenter 可直接度量垂直重心。
        LabelBitmap RenderWith(LabelRegionAlign v) => Render(new LabelLayout
        {
            Name = "anchor-y-" + v, ContractName = "c", ContractVersion = "1.0", WidthMm = 100, HeightMm = 40,
            Elements =
            [
                new LabelRegionElement { Id = "r1", XMm = 10, YMm = 20, WidthMm = 60, HeightMm = 8 },
                new LabelTextElement { Literal = "ANCHOR", XMm = 0, YMm = 0, FontHeightMm = 2, RegionId = "r1", RegionHAlign = LabelRegionAlign.Center, RegionVAlign = v },
            ],
        });

        // Center：区域 y∈[20, 28]、中心 24——盒内上部有墨迹、盒外上 / 下均无墨迹（不穿顶也不穿底），
        // 墨迹重心垂直居中（±1mm 容差）
        var center = RenderWith(LabelRegionAlign.Center);
        Assert.True(CountBlack(center, 12, 20.5, 55, 7) > 20, "锚定文本应在区域内有墨迹");
        Assert.True(CountBlack(center, 12, 0, 55, 19.7) == 0, "锚定文本不得越出盒顶");
        Assert.True(CountBlack(center, 12, 28.3, 55, 11.7) == 0, "锚定文本不得穿出盒底边");
        var (_, centerY) = InkCenter(center);
        Assert.True(centerY is >= 23 and <= 25, $"垂直居中失效：墨迹重心 {centerY:F2}mm（区域中心 24mm）");

        // Start / End：同一字高框随锚定方向贴边（Start 贴盒顶、End 贴盒底），且同样不越出盒
        var start = RenderWith(LabelRegionAlign.Start);
        var (_, startY) = InkCenter(start);
        Assert.True(startY is >= 20 and <= 22.2, $"Start 贴盒顶失效：墨迹重心 {startY:F2}mm（盒顶 20mm）");
        Assert.True(CountBlack(start, 12, 22.5, 55, 17.5) == 0, "Start 锚定文本不得越过盒中部");

        var end = RenderWith(LabelRegionAlign.End);
        var (_, endY) = InkCenter(end);
        Assert.True(endY is >= 25.8 and <= 28, $"End 贴盒底失效：墨迹重心 {endY:F2}mm（盒底 28mm）");
        Assert.True(CountBlack(end, 12, 0, 55, 25.5) == 0, "End 锚定文本不得穿出盒底边");
    }

    [Fact]
    public void Non_anchored_auto_height_text_keeps_fallback_box_vertical_center()
    {
        // #147非锚定路径零变化 pin：无 heightMm 的非锚定文本仍按决策 A 兜底框
        // max(字高 + 2×内边距, 10mm) 块内 Middle 居中——元素 y=5 → 墨迹重心 ≈ 5 + 10/2 = 10mm。
        var bitmap = Render(new LabelLayout
        {
            Name = "fallback-box", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements = [new LabelTextElement { Literal = "TEXT", XMm = 5, YMm = 5, FontHeightMm = 2 }],
        });

        var (_, centerY) = InkCenter(bitmap);
        Assert.True(centerY is >= 9 and <= 11.2, $"非锚定兜底框语义变化：墨迹重心 {centerY:F2}mm（兜底框中心 10mm）");
    }

    [Fact]
    public void Explicit_width_text_block_text_align_should_keep_working()
    {
        // 防回归：显式宽度块内 TextAlign 既有行为——Left 与 Right 墨迹重心应明显不同
        LabelBitmap RenderWith(LabelTextAlign align) => Render(new LabelLayout
        {
            Name = "talign-" + align, ContractName = "c", ContractVersion = "1.0", WidthMm = 60, HeightMm = 30,
            Elements =
            [
                new LabelTextElement { Literal = "块内对齐文本", XMm = 5, YMm = 5, FontHeightMm = 4, WidthMm = 50, HeightMm = 8, TextAlign = align, VerticalAlign = LabelVerticalAlign.Middle },
            ],
        });

        var (leftX, _) = InkCenter(RenderWith(LabelTextAlign.Left));
        var (rightX, _) = InkCenter(RenderWith(LabelTextAlign.Right));
        Assert.True(rightX > leftX + 10, $"块内 TextAlign 未生效：left x={leftX:F1}, right x={rightX:F1}");
    }

    /// <summary>墨迹重心（毫米）。</summary>
    private static (double X, double Y) InkCenter(LabelBitmap bitmap)
    {
        double sumX = 0, sumY = 0; long count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if ((bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0)
                {
                    sumX += x; sumY += y; count++;
                }
            }
        }

        return (sumX / count / Dpi * 25.4, sumY / count / Dpi * 25.4);
    }

    [Fact]
    public void Image_element_should_encode_to_zpl_gf_via_full_pipeline()
    {
        // 全链路：图片元素 → Skia 整版位图 → ^GF 编码（验证元素进入打印编码路径而非仅预览）
        var bitmap = Render(new LabelLayout
        {
            Name = "img-zpl", ContractName = "c", ContractVersion = "1.0", WidthMm = 40, HeightMm = 20,
            Elements = [new LabelImageElement { SourceKey = "logo", XMm = 2, YMm = 2, WidthMm = 10, HeightMm = 10 }],
        }, new Dictionary<string, byte[]> { ["logo"] = BlackSquarePng() });

        var zpl = new ZplImageEncoder().EncodeImage(bitmap, 40, 20, Dpi);
        Assert.StartsWith("^XA", zpl);
        Assert.Contains("^GFA,", zpl);
        Assert.EndsWith("^XZ", zpl);
        // 40mm@203dpi = 320 点宽
        Assert.Contains("^PW320", zpl);
        Assert.Contains("^LL160", zpl);
    }
}
