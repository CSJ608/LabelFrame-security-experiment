using System.Text;
using LabelFrame.Api;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Rendering;
using LabelFrame.TransportPlugin.Tspl;
using LabelFrame.TransportPlugin.Zebra;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Tests.Transport;
using LabelFrame.WinHost.Transport;
using Microsoft.Extensions.Logging;

namespace LabelFrame.WinHost.Tests.Jobs;

/// <summary>图片模式编码器按连接插件分派的行为矩阵（未实现 / 实现 / 连接级选项 / 双路径回放）。</summary>
public class TsplImageDispatchTests
{
    /// <summary>fake 图片编码插件：图片模式返回固定位图指令流（标记字节），native 不实现由基类承载。</summary>
    private sealed class FakeImageEncoderPlugin : ITransportPlugin, ILabelImageEncoder
    {
        public static readonly byte[] MarkerCommand = [0xAA, 0x55, 0x03, 0x00];

        public string Id => "fake-image-encoder";

        public string DisplayName => "Fake 图片编码插件";

        public string Description => "测试用：仅实现图片编码能力。";

        public IReadOnlyList<TransportParameterSpec> Parameters => Array.Empty<TransportParameterSpec>();

        public string Describe(TransportPluginParameters parameters) => "Fake 图片编码";

        public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context) => new LogPrintTransport(TextWriter.Null);

        public byte[] EncodeImage(LabelBitmap bitmap, double widthMm, double heightMm, int dpi) => MarkerCommand;
    }

    private static (JobSubmissionService Service, LabelJobQueue Queue) CreateService(string connectionJson, Action<TransportPluginRegistry>? configure = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"lfdispatch-{Guid.NewGuid():N}.db");
        var store = new SqliteLabelJobStore(dbPath);
        store.InitializeAsync().GetAwaiter().GetResult();
        var queue = new LabelJobQueue(store);

        var templatesDb = Path.Combine(Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        var templates = new TemplateStore(templatesDb);
        templates.InitializeAsync().GetAwaiter().GetResult();

        var (manager, registry) = TestTransportRegistry.CreateManagerWithRegistry(
            configure: r =>
            {
                r.Register(new TsplTransportPlugin());
                r.Register(new ZebraTransportPlugin());
                r.Register(new FakeImageEncoderPlugin());
                configure?.Invoke(r);
            },
            connectionJson: connectionJson);
        var service = new JobSubmissionService(
            queue,
            new ZplImageEncoder(),
            dpi: 203,
            new SkiaLabelRenderer(),
            templates,
            manager,
            registry,
            TestTransportRegistry.CreateContext(),
            TextWriter.Null);
        return (service, queue);
    }

    private static TemplateDto CreateTemplate() => new(
        new LabelContract
        {
            Name = "text-label",
            Version = "1.0",
            Fields = [new LabelField { Key = "code", DisplayName = "编码", IsRequired = true }],
        },
        new LabelLayout
        {
            Name = "text-label-40x20",
            ContractName = "text-label",
            ContractVersion = "1.0",
            WidthMm = 40,
            HeightMm = 20,
            Elements = [new LabelTextElement { SourceKey = "code", XMm = 2, YMm = 2, FontHeightMm = 4 }],
        });

    private static SubmitJobRequest CreateRequest(string requestId)
        => new(requestId, CreateTemplate(), [new LabelDto(new Dictionary<string, string> { ["code"] = "A-1" })]);

    /// <summary>提交 image 作业并取首张 Item。</summary>
    private static async Task<LabelJobItem> SubmitSingleAsync(JobSubmissionService service, string requestId)
    {
        var result = await service.SubmitAsync(CreateRequest(requestId));
        Assert.Null(result.ErrorCode);
        return result.Job!.Items[0];
    }

    // ──字节不变断言（未实现 ILabelImageEncoder → 宿主 ^GF）──

    [Fact]
    public async Task Image_mode_with_zebra_plugin_should_stay_byte_identical_to_zpl_encoder()
    {
        var (service, _) = CreateService("""{ "pluginId": "labelframe-transport-zebra", "params": { "kind": "Tcp", "host": "127.0.0.1" } }""");

        var item = await SubmitSingleAsync(service, "req-zebra-image");

        Assert.Null(item.CommandBytes);
        var expected = new ZplImageEncoder().EncodeImage(
            RenderSingle(), 40, 20, 203);
        Assert.Equal(expected, item.Zpl);
        Assert.StartsWith("^XA", item.Zpl); // ^GF 位图 ZPL 语义未变
    }

    [Fact]
    public async Task Image_mode_with_builtin_log_should_stay_byte_identical_to_zpl_encoder()
    {
        var (service, _) = CreateService("""{ "pluginId": "log", "params": {} }""");

        var item = await SubmitSingleAsync(service, "req-log-image");

        Assert.Null(item.CommandBytes);
        var expected = new ZplImageEncoder().EncodeImage(RenderSingle(), 40, 20, 203);
        Assert.Equal(expected, item.Zpl);
    }

    // ──插件图片编码分派（实现 ILabelImageEncoder → 二进制 CommandBytes）──

    [Fact]
    public async Task Image_mode_with_fake_image_encoder_should_persist_binary_command()
    {
        var (service, _) = CreateService("""{ "pluginId": "fake-image-encoder", "params": {} }""");

        var item = await SubmitSingleAsync(service, "req-fake-image");

        Assert.Equal(FakeImageEncoderPlugin.MarkerCommand, item.CommandBytes);
        Assert.Equal(string.Empty, item.Zpl); // 文本 / 二进制载荷互斥
    }

    [Fact]
    public async Task Image_mode_with_tspl_plugin_should_persist_tspl_bitmap_stream()
    {
        var (service, _) = CreateService("""{ "pluginId": "labelframe-transport-tspl", "params": { "kind": "Tcp", "host": "127.0.0.1" } }""");

        var item = await SubmitSingleAsync(service, "req-tspl-image");

        Assert.Equal(string.Empty, item.Zpl); // 文本 / 二进制载荷互斥
        var expected = new TsplImageEncoder().EncodeImage(RenderSingle(), 40, 20, 203);
        Assert.Equal(expected, item.CommandBytes);
        Assert.StartsWith("SIZE 40 mm,20 mm\r\n", Encoding.ASCII.GetString(item.CommandBytes![..20]));
        Assert.EndsWith("PRINT 1\r\n", Encoding.ASCII.GetString(item.CommandBytes![^9..]));
    }

    [Fact]
    public async Task Image_mode_with_invert_image_param_should_pass_option_to_tspl_encoder()
    {
        // 连接参数 invertImage=true → 编码选项 InvertBits=true（跳过取反透传）
        var (service, _) = CreateService("""{ "pluginId": "labelframe-transport-tspl", "params": { "kind": "Tcp", "host": "127.0.0.1", "invertImage": "true" } }""");

        var item = await SubmitSingleAsync(service, "req-tspl-invert-on");

        Assert.Equal(string.Empty, item.Zpl);
        var expected = new TsplImageEncoder().EncodeImage(RenderSingle(), 40, 20, 203, new LabelImageEncodeOptions(InvertBits: true));
        Assert.Equal(expected, item.CommandBytes);
    }

    [Fact]
    public async Task Image_mode_with_invalid_invert_value_should_fall_back_to_default()
    {
        // 非法值（非 bool.TryParse 可解析）回退 false = 默认取反输出，与缺省逐字节一致
        var (service, _) = CreateService("""{ "pluginId": "labelframe-transport-tspl", "params": { "kind": "Tcp", "host": "127.0.0.1", "invertImage": "yes" } }""");

        var item = await SubmitSingleAsync(service, "req-tspl-invert-invalid");

        var expected = new TsplImageEncoder().EncodeImage(RenderSingle(), 40, 20, 203);
        Assert.Equal(expected, item.CommandBytes);
    }

    [Fact]
    public async Task Native_mode_with_tspl_plugin_should_persist_text_commands()
    {
        var (service, _) = CreateService("""{ "pluginId": "labelframe-transport-tspl", "params": { "kind": "Tcp", "host": "127.0.0.1", "printMode": "native" } }""");

        var item = await SubmitSingleAsync(service, "req-tspl-native");

        Assert.Null(item.CommandBytes);
        Assert.StartsWith("SIZE 40 mm,20 mm\r\nDIRECTION 1\r\nCODEPAGE UTF-8\r\nCLS\r\nTEXT ", item.Zpl);
        Assert.EndsWith("PRINT 1\r\n", item.Zpl);
    }

    // ──IPrintTransport 二进制重载缺省（DIM）──

    [Fact]
    public async Task Plain_transport_receive_binary_should_throw_not_supported()
    {
        IPrintTransport transport = new LogPrintTransport(TextWriter.Null);
        await Assert.ThrowsAsync<NotSupportedException>(() => transport.SendAsync(new byte[] { 0x00 }));
    }

    // ──Worker 双路径回放──

    private sealed class CapturingTransportManager : ITransportManager
    {
        public CapturingTransport Transport { get; } = new();

        public TransportConfig CurrentConfig { get; } = new()
        {
            PluginId = "labelframe-transport-tspl",
            Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        public IPrintTransport CurrentTransport => Transport;

        public string ConfigFilePath => "unused";

        public Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("测试桩不支持切换连接");

        public sealed class CapturingTransport : IPrintTransport
        {
            public List<string> TextSends { get; } = [];

            public List<byte[]> BinarySends { get; } = [];

            public Task SendAsync(string command, CancellationToken cancellationToken = default)
            {
                TextSends.Add(command);
                return Task.CompletedTask;
            }

            public Task SendAsync(byte[] command, CancellationToken cancellationToken = default)
            {
                BinarySends.Add(command);
                return Task.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task Worker_should_replay_binary_payload_via_byte_send()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"lfworkerbin-{Guid.NewGuid():N}.db");
        var store = new SqliteLabelJobStore(dbPath);
        await store.InitializeAsync();
        var queue = new LabelJobQueue(store);

        var binary = new byte[] { 0x01, 0x02, 0xFF };
        await queue.SubmitAsync($"req-bin-{Guid.NewGuid():N}", new List<LabelJobPayload> { LabelJobPayload.OfBytes(binary) });

        var manager = new CapturingTransportManager();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var worker = new JobPrintWorker(queue, manager, LoggerFactory.Create(_ => { }).CreateLogger<JobPrintWorker>(), new PrintSettings());
        await worker.StartAsync(cts.Token);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (manager.Transport.BinarySends.Count == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        await worker.StopAsync(cts.Token);
        Assert.Equal(binary, Assert.Single(manager.Transport.BinarySends));
        Assert.Empty(manager.Transport.TextSends); // 二进制载荷不走文本路径
    }

    /// <summary>渲染单张 40×20 文本标签位图（与提交链同源渲染口径）。</summary>
    private static LabelBitmap RenderSingle()
    {
        var document = new LabelDocument
        {
            Layout = CreateTemplate().Layout!,
            Data = new Dictionary<string, string> { ["code"] = "A-1" },
        };
        return new SkiaLabelRenderer().RenderLabelBitmap(document, 203);
    }
}
