using LabelFrame.Api;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Rendering;
using LabelFrame.TransportPlugin.Tspl;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Tests.Samples;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Jobs;

public class LogSimulationIsolationTests
{
    [Theory]
    [InlineData("log", true)]
    [InlineData("LOG", true)]
    [InlineData("log", true, "image", false)]
    [InlineData("tcp9100", false)]
    [InlineData("labelframe-transport-tspl", false)]
    [InlineData("custom-printer", false)]
    [InlineData("labelframe-transport-tspl", false, "native")]
    public async Task Only_log_plugin_should_save_images_log_data_and_clean_history(string pluginId, bool simulated, string printMode = "image", bool saveImages = true)
    {
        var root = Path.Combine(Path.GetTempPath(), $"lf-log-isolation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var print = Path.Combine(root, "print");
            var expiredId = Guid.NewGuid().ToString("N");
            var expired = Path.Combine(print, expiredId);
            Directory.CreateDirectory(expired);
            File.WriteAllText(Path.Combine(expired, PrintImageRetentionCleaner.MarkerName), expiredId);
            Directory.SetLastWriteTime(expired, DateTime.Now.AddDays(-10));
            using var log = new StringWriter();
            var registry = new TransportPluginRegistry();
            registry.Register(new TsplTransportPlugin());
            var config = new TransportConfig { PluginId = pluginId };
            config.Params[TransportPrintMode.ParameterKey] = printMode;
            config.SyncLegacyFields();
            var manager = new NoSendManager(config);
            var store = new SqliteLabelJobStore(Path.Combine(root, "jobs.db"));
            await store.InitializeAsync();
            using var queue = new LabelJobQueue(store);
            var templates = new TemplateStore(Path.Combine(root, "templates.db"));
            await templates.InitializeAsync();
            var service = new JobSubmissionService(queue, new ZplImageEncoder(), () => 203,
                new SkiaLabelRenderer(), templates, manager, registry,
                new TransportPluginContext(log, root), log, print,
                new PrintImageRetentionCleaner(print, 1, log), saveImages);
            var request = new SubmitJobRequest("isolation", new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout),
                [new LabelDto(new Dictionary<string, string> { ["zone"] = "A", ["locationCode"] = "A-01" })]);
            var result = await service.SubmitAsync(request);
            Assert.Null(result.ErrorCode);
            Assert.True(result.Created);
            Assert.NotNull(result.Job);
            var job = result.Job;
            Assert.Equal(LabelJobStatus.Pending, job.Status);
            if (printMode == "native")
                Assert.NotEmpty(job.Items[0].Zpl);
            else if (pluginId == TransportPluginIdPolicy.TsplPluginId)
                Assert.NotEmpty(job.Items[0].CommandBytes!);
            else
                Assert.Contains("^GF", job.Items[0].Zpl);
            Assert.Equal(simulated && saveImages, Directory.Exists(Path.Combine(print, job.Id)));
            Assert.Equal(!simulated, Directory.Exists(expired));
            Assert.Equal(simulated, log.ToString().Contains(job.Id, StringComparison.Ordinal));
            var view = JobsApi.EnrichPrintInfo(JobViews.From(job), job.Id, manager, print);
            Assert.Equal(simulated ? Path.Combine(print, job.Id) : null, view.PrintImageDir);
            if (simulated) Assert.Equal(saveImages ? 1 : 0, view.PrintImageCount);
            else Assert.Null(view.PrintImageCount);
            var beforeReplay = log.ToString();
            var replay = await service.SubmitAsync(request);
            Assert.False(replay.Created);
            Assert.Equal(job.Id, replay.Job!.Id);
            Assert.Equal(job.Items[0].Zpl, replay.Job.Items[0].Zpl);
            Assert.Equal(job.Items[0].CommandBytes, replay.Job.Items[0].CommandBytes);
            Assert.Equal(beforeReplay, log.ToString());
            if (simulated && !saveImages)
            {
                var enabled = new JobSubmissionService(queue, new ZplImageEncoder(), () => 203,
                    new SkiaLabelRenderer(), templates, manager, registry,
                    new TransportPluginContext(log, root), log, print,
                    new PrintImageRetentionCleaner(print, 1, log), saveLogPrintImages: true);
                var comparison = await enabled.SubmitAsync(request with { RequestId = "save-enabled" });
                Assert.True(comparison.Created);
                Assert.Equal(job.Items[0].Zpl, comparison.Job!.Items[0].Zpl);
                Assert.True(File.Exists(Path.Combine(print, comparison.Job.Id, "label-1.png")));
            }
        }
        finally
        {
            foreach (var name in new[] { "jobs.db", "templates.db" })
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                    LabelFrame.Core.Data.SqliteSupport.BuildConnectionString(Path.Combine(root, name)));
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            }
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(root, true);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    // 测试目录的原生句柄可能延迟释放；重试耗尽仍使测试失败。
                    await Task.Delay(100);
                }
            }
        }
    }

    [Theory]
    [InlineData("labelframe-transport-tspl", TransportMode.Log, false)]
    [InlineData("custom-printer", TransportMode.Log, false)]
    [InlineData("LOG", TransportMode.Tcp, true)]
    public void View_should_use_plugin_identity_even_with_stale_legacy_mode(string pluginId, TransportMode mode, bool simulated)
    {
        var manager = new NoSendManager(new TransportConfig { PluginId = pluginId, Mode = mode });
        var original = JobViews.From(new LabelJob { Id = "view-only", RequestId = "view-only", Items = [] });
        var root = Path.Combine(Path.GetTempPath(), $"lf-view-{Guid.NewGuid():N}");
        var view = JobsApi.EnrichPrintInfo(original, "view-only", manager, root);
        if (simulated)
        {
            Assert.Equal(Path.Combine(root, "view-only"), view.PrintImageDir);
            Assert.Equal(0, view.PrintImageCount);
        }
        else Assert.Equal(original, view);
    }

    private sealed class NoSendManager(TransportConfig config) : ITransportManager
    {
        public TransportConfig CurrentConfig => config;
        public IPrintTransport CurrentTransport => throw new InvalidOperationException("测试禁止访问物理传输");
        public string ConfigFilePath => string.Empty;
        public Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
