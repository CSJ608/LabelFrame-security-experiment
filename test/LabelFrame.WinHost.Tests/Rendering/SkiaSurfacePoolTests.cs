using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;
using Xunit.Abstractions;

namespace LabelFrame.WinHost.Tests.Rendering;

/// <summary>验证预览与打印共用渲染缓存时的分配和内容隔离。</summary>
public class SkiaSurfacePoolTests(ITestOutputHelper output)
{
    private static LabelDocument Document(double width = 100, string text = "缓存测试") => new()
    {
        Layout = new LabelLayout
        {
            Name = "pool", ContractName = "pool", ContractVersion = "1.0",
            WidthMm = width, HeightMm = 60,
            Elements = [new LabelTextElement { Literal = text, XMm = 2, YMm = 2, WidthMm = 35, HeightMm = 10, FontHeightMm = 4 }],
        },
        Data = new Dictionary<string, string>(),
    };

    [Fact]
    public void Cold_print_should_not_allocate_full_managed_bgra_buffer()
    {
        var document = Document();
        new SkiaLabelRenderer().RenderLabelBitmap(document, 300);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = new SkiaLabelRenderer().RenderLabelBitmap(document, 300);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"首次打印托管分配：{allocated} bytes");
        Assert.NotEmpty(result.Pixels);
        Assert.True(allocated < 1_000_000, $"首次打印托管分配：{allocated} bytes");
    }

    [Fact]
    public void Warm_png_should_not_allocate_another_full_bgra_buffer()
    {
        var renderer = new SkiaLabelRenderer();
        var document = Document();
        for (var i = 0; i < 3; i++) renderer.RenderLabelBitmapPng(document, 300);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 3; i++) renderer.RenderLabelBitmapPng(document, 300);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // 单张 BGRA 中间态超过 3 MB，暖缓存三次总分配不得达到一次中间态大小。
        Assert.True(allocated < 3_000_000, $"暖缓存三次 PNG 分配：{allocated} bytes");
    }

    [Fact]
    public void Mixed_sizes_and_prints_should_match_fresh_renderers()
    {
        var renderer = new SkiaLabelRenderer();
        foreach (var width in new[] { 60, 60, 100, 100, 60 })
        {
            var document = Document(width);
            var png = new SkiaLabelRenderer().RenderLabelBitmapPng(document);
            var print = new SkiaLabelRenderer().RenderLabelBitmap(document);
            Assert.Equal(png, renderer.RenderLabelBitmapPng(document));
            Assert.Equal(print.Pixels, renderer.RenderLabelBitmap(document).Pixels);
            renderer.RenderLabelBitmapPng(Document(width, ""));
            Assert.Equal(png, renderer.RenderLabelBitmapPng(document));
        }
    }

    [Fact]
    public async Task Concurrent_previews_should_not_mix_contents()
    {
        var renderer = new SkiaLabelRenderer();
        var documents = Enumerable.Range(0, 12).Select(i => Document(i % 2 == 0 ? 60 : 100, $"标签-{i}")).ToArray();
        var expected = documents.Select(d => new SkiaLabelRenderer().RenderLabelBitmapPng(d)).ToArray();
        await Task.WhenAll(documents.Select((document, i) => Task.Run(() =>
        {
            for (var repeat = 0; repeat < 3; repeat++)
                Assert.Equal(expected[i], renderer.RenderLabelBitmapPng(document));
        })));
    }

    [Fact]
    public async Task Concurrent_mixed_prints_and_previews_should_match_independent_renderers()
    {
        var renderer = new SkiaLabelRenderer();
        var documents = Enumerable.Range(0, 16).Select(i => Document(i % 2 == 0 ? 60 : 100, $"标签-{i}")).ToArray();
        var expected = documents.Select(d => new SkiaLabelRenderer().RenderLabelBitmap(d, 300)).ToArray();
        var png = documents.Select(d => new SkiaLabelRenderer().RenderLabelBitmapPng(d, 300)).ToArray();
        await Task.WhenAll(documents.Select((document, i) => Task.Run(() =>
        {
            for (var repeat = 0; repeat < 3; repeat++)
            {
                Assert.Equal(expected[i].Pixels, renderer.RenderLabelBitmap(document, 300).Pixels);
                Assert.Equal(png[i], renderer.RenderLabelBitmapPng(document, 300));
            }
        })));
    }
}
