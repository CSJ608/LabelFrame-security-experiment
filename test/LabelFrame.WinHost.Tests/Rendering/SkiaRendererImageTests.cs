using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;
using SkiaSharp;

namespace LabelFrame.WinHost.Tests.Rendering;

/// <summary>
/// 图片元素渲染：预览 PNG 与打印 1bpp 输出统一单色
/// （DrawImage 解码后 luma 阈值 128 单色化——彩色像素存在即红：红 luma≈76→黑、蓝 luma≈29→黑、白→白，
/// 未单色化的旧口径会在 PNG 中留下 R≠G≠B 的彩色像素）；缺资源显式报错（不再灰占位照打）。
/// </summary>
public class SkiaRendererImageTests
{
    /// <summary>左半红右半蓝的 16×16 彩色 PNG（两侧 luma 均 &lt;128 → 单色化后全黑；未单色化则两半均为彩色像素）。</summary>
    private static byte[] ColorPng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawRect(0, 0, 8, 16, new SKPaint { Color = SKColors.Red });
            canvas.DrawRect(8, 0, 16, 16, new SKPaint { Color = SKColors.Blue });
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static LabelDocument Doc() => new()
    {
        Layout = new LabelLayout
        {
            Name = "img",
            ContractName = "img",
            ContractVersion = "1.0",
            WidthMm = 40,
            HeightMm = 20,
            Elements =
            [
                new LabelImageElement { SourceKey = "logo.png", XMm = 2, YMm = 5, WidthMm = 20, HeightMm = 10 },
            ],
        },
        Data = new Dictionary<string, string>(),
    };

    private static int CountColorPixels(SKBitmap bitmap)
    {
        var colored = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red != c.Green || c.Green != c.Blue) colored++;
            }
        }

        return colored;
    }

    [Fact]
    public void Preview_png_should_contain_no_color_pixels()
    {
        var png = new SkiaLabelRenderer().RenderLabelBitmapPng(Doc(), dpi: 203, new Dictionary<string, byte[]> { ["logo.png"] = ColorPng() });

        using var decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        Assert.True(CountColorPixels(decoded) == 0, $"预览 PNG 应无彩色像素（单色化统一——彩色像素存在即旧口径证伪）：{CountColorPixels(decoded)}");
    }

    [Fact]
    public void Print_bitmap_should_have_ink_inside_element_box()
    {
        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(Doc(), dpi: 203, new Dictionary<string, byte[]> { ["logo.png"] = ColorPng() });

        // 红 / 蓝 luma 均 <128 → 元素盒（2~22mm × 5~15mm @203dpi）内应大面积有墨
        var black = 0;
        for (var y = 45; y < 115; y++)
        {
            for (var x = 20; x < 170; x++)
            {
                if ((bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0) black++;
            }
        }

        Assert.True(black > 3000, $"元素盒内应有墨迹（红蓝均转黑）：black={black}");
    }

    [Fact]
    public void Missing_resource_should_throw_with_key_name()
    {
        var ex = Assert.Throws<MissingImageResourceException>(() => new SkiaLabelRenderer().RenderLabelBitmap(Doc(), dpi: 203));
        Assert.Contains("logo.png", ex.Message);
    }

    [Fact]
    public void Empty_source_key_should_skip_element_entirely_without_ink_or_border()
    {
        // key 空＝未指定图片——整元素跳过（含边框）：渲染成功、元素盒内无墨、边框不画
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "img",
                ContractName = "img",
                ContractVersion = "1.0",
                WidthMm = 40,
                HeightMm = 20,
                Elements =
                [
                    // 边框 >0 也一并跳过（「未指定＝不存在于打印面」，含边框）
                    new LabelImageElement { SourceKey = "", XMm = 2, YMm = 5, WidthMm = 20, HeightMm = 10, BorderMm = 0.3 },
                ],
            },
            Data = new Dictionary<string, string>(),
        };

        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        // 全页无墨（唯一元素被跳过；边框若误画会在元素盒边缘出墨）
        var black = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if ((bitmap.Pixels[y * bitmap.RowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0) black++;
            }
        }

        Assert.True(black == 0, $"空 key 图片元素应整元素跳过（无墨、边框不画）：black={black}");
    }

    [Fact]
    public void Document_images_channel_should_still_work()
    {
        // 既有行为回归：请求附带（document.Images 打印时数据图通道）命中时不报缺资源、正常出墨
        var document = new LabelDocument
        {
            Layout = Doc().Layout,
            Data = new Dictionary<string, string>(),
            Images = new Dictionary<string, LabelBitmap> { ["logo.png"] = SolidBlackLabelBitmap() },
        };
        var bitmap = new SkiaLabelRenderer().RenderLabelBitmap(document, dpi: 203);

        Assert.True((bitmap.Pixels[80 * bitmap.RowBytes + (80 >> 3)] & (0x80 >> (80 & 7))) != 0, "元素盒中心应有墨（document.Images 通道）");
    }

    private static LabelBitmap SolidBlackLabelBitmap()
    {
        var result = new LabelBitmap(16, 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                result.Pixels[y * result.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }

        return result;
    }
}
