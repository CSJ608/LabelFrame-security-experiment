using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;
using LabelFrame.TransportPlugin.Tspl;
using LabelFrame.WinHost.Tests.Rendering;
using SkiaSharp;
using Xunit.Abstractions;

namespace LabelFrame.WinHost.Tests.Perf;

public class BitmapRenderStagesTests(ITestOutputHelper output)
{
    [Fact]
    public void Compare_conversion_with_reference_in_alternating_order()
    {
        const int width = 1181, height = 709, count = 200;
        var input = new byte[width * height * 4];
        new Random(42).NextBytes(input);
        var methods = new (string Name, Func<int, int, int, byte[], LabelBitmap> Convert)[]
        {
            ("reference", SkiaMonochromeConversionTests.Reference),
            ("current", SkiaMonochromeConversionTests.ConvertPixels),
        };
        foreach (var (_, convert) in methods) _ = convert(width, height, width * 4, input);
        var expected = methods[0].Convert(width, height, width * 4, input);
        for (var round = 1; round <= 4; round++)
        foreach (var (name, convert) in round % 2 == 1 ? methods : methods.Reverse())
        {
            LabelBitmap result = null!;
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < count; i++) result = convert(width, height, width * 4, input);
            watch.Stop();
            Assert.Equal(expected.Pixels, result.Pixels);
            output.WriteLine($"round={round}; method={name}; count={count}; ms={watch.Elapsed.TotalMilliseconds:F2}");
        }
    }

    [Fact]
    public async Task Measure_drawing_copy_conversion_encoding_and_persistence()
    {
        const int count = 200;
        var layout = new LabelLayout
        {
            Name = "perf", ContractName = "perf", ContractVersion = "1.0", WidthMm = 100, HeightMm = 60,
            Elements =
            [
                new LabelTextElement { SourceKey = "text", XMm = 3, YMm = 3, WidthMm = 80, HeightMm = 10, FontHeightMm = 5 },
                new LabelBarcodeElement { SourceKey = "code", XMm = 3, YMm = 18, HeightMm = 22, ModuleWidth = 2 },
                new LabelQrCodeElement { SourceKey = "code", XMm = 75, YMm = 40, SizeMm = 15 },
            ],
        };
        var documents = Enumerable.Range(1, count).Select(i => new LabelDocument
        {
            Layout = layout, Data = new Dictionary<string, string> { ["code"] = $"LF-{i:0000}", ["text"] = $"模拟打印-{i:0000}" },
        }).ToArray();
        var renderer = new SkiaLabelRenderer();
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var draw = typeof(SkiaLabelRenderer).GetMethod("DrawElement", flags)!.CreateDelegate<Draw>();
        var convert = typeof(SkiaLabelRenderer).GetMethod("ToLabelBitmap", flags)!
            .CreateDelegate<SkiaMonochromeConversionTests.PixelConverter>();
        using var bitmap = new SKBitmap(new SKImageInfo(1181, 709, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        var staging = new byte[bitmap.RowBytes * bitmap.Height];
        var expected = documents.Select(d => renderer.RenderLabelBitmap(d, 300)).ToArray();
        var root = Path.Combine(Path.GetTempPath(), $"lf-render-stages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            for (var round = 1; round <= 3; round++)
            {
                var ticks = new Dictionary<string, long>();
                var bitmaps = new List<LabelBitmap>();
                foreach (var document in documents)
                {
                    Measure("clear", () => canvas.Clear(SKColors.White));
                    IReadOnlyDictionary<string, LabelRegionElement> regions = null!;
                    Measure("regions", () => regions = LabelLayoutResolver.IndexRegions(layout));
                    foreach (var element in layout.Elements)
                        Measure(element.GetType().Name, () => draw(canvas, element, document, null, regions, 300));
                    Measure("copy-diagnostic", () => Marshal.Copy(bitmap.GetPixels(), staging, 0, staging.Length));
                    LabelBitmap copied = null!;
                    Measure("convert-copied-diagnostic", () => copied = convert(bitmap.Width, bitmap.Height, bitmap.RowBytes, staging));
                    Measure("convert-native", () => bitmaps.Add(convert(bitmap.Width, bitmap.Height, bitmap.RowBytes, bitmap.GetPixelSpan())));
                    Assert.Equal(copied.Pixels, bitmaps[^1].Pixels);
                }
                for (var i = 0; i < count; i++) Assert.Equal(expected[i].Pixels, bitmaps[i].Pixels);
                var zpl = new ZplImageEncoder();
                var tspl = new TsplTransportPlugin();
                var text = new List<LabelJobPayload>();
                var binary = new List<LabelJobPayload>();
                Measure("encode-zpl", () => text.AddRange(bitmaps.Select(b => LabelJobPayload.OfText(zpl.EncodeImage(b, 100, 60, 300)))));
                Measure("encode-tspl", () => binary.AddRange(bitmaps.Select(b => LabelJobPayload.OfBytes(tspl.EncodeImage(b, 100, 60, 300)))));
                foreach (var (name, payloads) in round % 2 == 1
                             ? new[] { ("zpl", text), ("tspl", binary) }
                             : new[] { ("tspl", binary), ("zpl", text) })
                {
                    var store = new SqliteLabelJobStore(Path.Combine(root, $"{round}-{name}.db"));
                    await store.InitializeAsync();
                    using var queue = new LabelJobQueue(store);
                    var watch = Stopwatch.StartNew();
                    var result = await queue.SubmitAsync("stages", payloads);
                    ticks[$"persist-{name}"] = watch.ElapsedTicks;
                    Assert.True(result.Created);
                    var stored = await store.GetJobAsync(result.Job.Id);
                    Assert.Equal(count, stored!.Items.Count);
                    for (var i = 0; i < count; i++)
                    {
                        Assert.Equal(payloads[i].Bytes, stored.Items[i].CommandBytes);
                        Assert.Equal(payloads[i].Bytes is null ? payloads[i].Text : string.Empty, stored.Items[i].Zpl);
                    }
                }
                foreach (var (phase, elapsed) in ticks)
                    output.WriteLine($"round={round}; count={count}; phase={phase}; ms={elapsed * 1000d / Stopwatch.Frequency:F2}");
                void Measure(string name, Action action)
                {
                    var start = Stopwatch.GetTimestamp();
                    action();
                    ticks[name] = ticks.GetValueOrDefault(name) + Stopwatch.GetTimestamp() - start;
                }
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private delegate void Draw(SKCanvas canvas, LabelElement element, LabelDocument document,
        IReadOnlyDictionary<string, byte[]>? images, IReadOnlyDictionary<string, LabelRegionElement> regions, int dpi);
}
