using System.Diagnostics;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Rendering;
using Xunit.Abstractions;

namespace LabelFrame.WinHost.Tests.Perf;

public class BatchSubmissionStagesTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(50)]
    [InlineData(200)]
    public async Task Measure_submission_stages(int count)
    {
        var root = Path.Combine(Path.GetTempPath(), $"lf-submit-stages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteLabelJobStore(Path.Combine(root, "jobs.db"));
            await store.InitializeAsync();
            using var queue = new LabelJobQueue(store);
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
            var renderer = new SkiaLabelRenderer();
            var encoder = new ZplImageEncoder();
            LabelDocument Document(int index) => new()
            {
                Layout = layout,
                Data = new Dictionary<string, string> { ["code"] = $"LF-{index:0000}", ["text"] = $"模拟打印-{index:0000}" },
            };
            var warm = renderer.RenderLabelBitmap(Document(1), 300);
            _ = encoder.EncodeImage(warm, 100, 60, 300);
            _ = LabelBitmapPng.ToPng(warm);
            var bitmaps = new List<LabelBitmap>();
            var payloads = new List<string>();
            var watch = Stopwatch.StartNew();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 1; i <= count; i++) bitmaps.Add(renderer.RenderLabelBitmap(Document(i), 300));
            Report("render", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            foreach (var bitmap in bitmaps) payloads.Add(encoder.EncodeImage(bitmap, 100, 60, 300));
            Report("encode", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            var submit = queue.SubmitAsync("stages", payloads);
            Assert.True(submit.IsCompletedSuccessfully);
            var (job, _) = await submit;
            Report("persist", before);
            long pngTicks = 0, writeTicks = 0;
            before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var (bitmap, index) in bitmaps.Select((bitmap, index) => (bitmap, index)))
            {
                var start = Stopwatch.GetTimestamp();
                var png = LabelBitmapPng.ToPng(bitmap);
                pngTicks += Stopwatch.GetTimestamp() - start;
                start = Stopwatch.GetTimestamp();
                File.WriteAllBytes(Path.Combine(root, $"{index}.png"), png);
                writeTicks += Stopwatch.GetTimestamp() - start;
            }
            output.WriteLine($"count={count}; pngMs={pngTicks * 1000d / Stopwatch.Frequency:F2}; fileMs={writeTicks * 1000d / Stopwatch.Frequency:F2}; pngAndFileBytes={GC.GetAllocatedBytesForCurrentThread() - before}");
            Assert.Equal(count, job.Items.Count);
            Assert.Equal(count, Directory.GetFiles(root, "*.png").Length);
            void Report(string phase, long allocatedBefore) => output.WriteLine(
                $"count={count}; phase={phase}; ms={watch.Elapsed.TotalMilliseconds:F2}; allocated={GC.GetAllocatedBytesForCurrentThread() - allocatedBefore}");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
