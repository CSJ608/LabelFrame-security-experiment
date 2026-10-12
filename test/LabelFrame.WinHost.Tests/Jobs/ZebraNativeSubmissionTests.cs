using LabelFrame.Api;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Rendering;
using LabelFrame.TransportPlugin.Zebra;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Tests.Transport;

namespace LabelFrame.WinHost.Tests.Jobs;

/// <summary>
/// 宿主集成（+ ，；DESIGN）：native 分派走真实 Zebra 编译器——
/// 本地作业指令字段（LabelJobItem.Zpl）为品牌原生指令（文本 / 条码 / 二维码元素，^A0 / ^BC / ^BQ / ^FD，非 ^GF 位图）；
/// image / 缺省路径经真实 Zebra 插件连接零回归（仍整版渲染 ^GF）。
/// 与的 JobSubmissionCompileTests（fake 编译器）互补：此处为真实品牌编译器锚定。
/// </summary>
public class ZebraNativeSubmissionTests
{
    /// <summary>构造提交服务：注册表含内置插件 + 真实 Zebra 官方插件，connection.json 预写指定内容。</summary>
    private static (JobSubmissionService Service, SqliteLabelJobStore Store) CreateService(string connectionJson)
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfhost-{Guid.NewGuid():N}.db");
        var store = new SqliteLabelJobStore(dbPath);
        store.InitializeAsync().GetAwaiter().GetResult();
        var queue = new LabelJobQueue(store);

        var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        var templates = new TemplateStore(templatesDb);
        templates.InitializeAsync().GetAwaiter().GetResult();

        var (manager, registry) = TestTransportRegistry.CreateManagerWithRegistry(
            configure: r => r.Register(new ZebraTransportPlugin()),
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
        return (service, store);
    }

    /// <summary>纯文本模板（锚定）。</summary>
    private static TemplateDto CreateTemplate() => new(
        new Core.Contracts.LabelContract
        {
            Name = "text-label",
            Version = "1.0",
            Fields = [new Core.Contracts.LabelField { Key = "code", DisplayName = "编码", IsRequired = true }],
        },
        new Core.Layout.LabelLayout
        {
            Name = "text-label-40x20",
            ContractName = "text-label",
            ContractVersion = "1.0",
            WidthMm = 40,
            HeightMm = 20,
            Elements =
            [
                new Core.Layout.LabelTextElement { SourceKey = "code", XMm = 2, YMm = 2, FontHeightMm = 4 },
            ],
        });

    /// <summary>混合模板（文本 + Code 128 条码 + 二维码）。</summary>
    private static TemplateDto CreateMixedTemplate() => new(
        new Core.Contracts.LabelContract
        {
            Name = "mixed-label",
            Version = "1.0",
            Fields =
            [
                new Core.Contracts.LabelField { Key = "code", DisplayName = "编码", IsRequired = true },
                new Core.Contracts.LabelField { Key = "barcode", DisplayName = "条码", IsRequired = true },
                new Core.Contracts.LabelField { Key = "qr", DisplayName = "二维码", IsRequired = true },
            ],
        },
        new Core.Layout.LabelLayout
        {
            Name = "mixed-label-40x20",
            ContractName = "mixed-label",
            ContractVersion = "1.0",
            WidthMm = 40,
            HeightMm = 20,
            Elements =
            [
                new Core.Layout.LabelTextElement { SourceKey = "code", XMm = 2, YMm = 1, FontHeightMm = 3 },
                new Core.Layout.LabelBarcodeElement { SourceKey = "barcode", XMm = 2, YMm = 6, HeightMm = 8, ModuleWidth = 2 },
                new Core.Layout.LabelQrCodeElement { SourceKey = "qr", XMm = 27, YMm = 4, SizeMm = 12, QrEcc = Core.Layout.LabelQrEcc.M, QrMargin = 2 },
            ],
        });

    private static SubmitJobRequest CreateRequest(string requestId, params IReadOnlyDictionary<string, string>[] labels)
        => new(requestId, CreateTemplate(), labels.Select(d => new LabelDto(d)).ToList());

    private static SubmitJobRequest CreateMixedRequest(string requestId, IReadOnlyDictionary<string, string> label)
        => new(requestId, CreateMixedTemplate(), [new LabelDto(label)]);

    private const string NativeConnection =
        """{ "pluginId": "labelframe-transport-zebra", "params": { "kind": "Tcp", "host": "127.0.0.1", "printMode": "native" } }""";

    private const string ImageConnection =
        """{ "pluginId": "labelframe-transport-zebra", "params": { "kind": "Tcp", "host": "127.0.0.1" } }""";

    // ── ：native 分派走真实 Zebra 编译器（作业指令字段为原生指令）──

    [Fact]
    public async Task Native_mode_should_dispatch_to_real_zebra_compiler()
    {
        var (service, store) = CreateService(NativeConnection);

        var result = await service.SubmitAsync(CreateRequest(
            "req-zebra-native",
            new Dictionary<string, string> { ["code"] = "A-1" },
            new Dictionary<string, string> { ["code"] = "B-2" }));

        Assert.NotNull(result.Job);
        Assert.True(result.Created);
        var stored = await store.GetJobAsync(result.Job!.Id);
        Assert.Equal(2, stored!.Items.Count);

        // 逐张编译为品牌原生指令：整页自包含结构 + ^A0 文本指令，非 ^GF 位图
        var first = stored.Items[0].Zpl;
        Assert.StartsWith("^XA", first);
        Assert.EndsWith("^XZ", first);
        Assert.Contains("^PW320", first);
        Assert.Contains("^FO16,16^A0N,32^FH^FDA-1^FS", first);
        Assert.Contains("^FH^FDB-2^FS", stored.Items[1].Zpl); // 逐张内容区分
        Assert.DoesNotContain("^GF", first);
    }

    // ── ：image / 缺省路径零回归（经真实 Zebra 插件连接，无 printMode → 既有整版渲染）──

    [Fact]
    public async Task Image_mode_with_real_plugin_should_render_bitmap_as_before()
    {
        var (service, store) = CreateService(ImageConnection);

        var result = await service.SubmitAsync(CreateRequest("req-zebra-image", new Dictionary<string, string> { ["code"] = "A-1" }));

        Assert.NotNull(result.Job);
        var stored = await store.GetJobAsync(result.Job!.Id);
        // 既有图片路径：Skia 整版渲染 ^GF 位图（打印方式缺省回退 image）
        Assert.Contains("^GF", stored!.Items[0].Zpl);
        Assert.DoesNotContain("^FD", stored.Items[0].Zpl);
    }

    // ── 端到端：中文经宿主提交链路显式失败（LF_ENC_002 + 插件 id + 中文原因 + fieldKey）──

    [Fact]
    public async Task Native_mode_chinese_text_should_fail_with_plugin_reason()
    {
        var (service, store) = CreateService(NativeConnection);

        var result = await service.SubmitAsync(CreateRequest("req-zebra-chinese", new Dictionary<string, string> { ["code"] = "库位A" }));

        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("labelframe-transport-zebra", result.ErrorMessage);
        Assert.Contains("内置字体无法表示的字符", result.ErrorMessage);
        Assert.Contains("切回图片", result.ErrorMessage);
        Assert.Equal("code", result.FieldKey);
        Assert.Null(await store.GetJobByRequestIdAsync("req-zebra-chinese"));
    }

    // ── 端到端：混合模板 native 逐张编译（文本 + 条码 + 二维码整页自包含）──

    [Fact]
    public async Task Native_mode_mixed_template_should_compile_all_element_types()
    {
        var (service, store) = CreateService(NativeConnection);

        var result = await service.SubmitAsync(CreateMixedRequest(
            "req-zebra-mixed",
            new Dictionary<string, string>
            {
                ["code"] = "A-1",
                ["barcode"] = "WH-A-01",
                ["qr"] = "库位A-01", // 二维码经 ^CI28 支持 UTF-8 中文
            }));

        Assert.NotNull(result.Job);
        var stored = await store.GetJobAsync(result.Job!.Id);
        var zpl = stored!.Items[0].Zpl;

        // 整页自包含：格式级指令齐全 + 三类元素各产出原生指令，非 ^GF 位图
        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl);
        Assert.Contains("^PW320", zpl);
        Assert.Contains("^CI28", zpl);
        Assert.Contains("^A0N,", zpl); // 文本
        Assert.Contains("^BY2,3^BCN,64,Y,N,N^FH^FDWH-A-01^FS", zpl); // 条码（8mm 高 @203dpi = 64 点）
        Assert.Contains("^BQN,2,", zpl); // 二维码
        Assert.Contains("^FDMA,库位A-01^FS", zpl); // 二维码 UTF-8 中文数据
        Assert.DoesNotContain("^GF", zpl);
    }

    // ── 端到端：Code 128 中文值经提交链路按 LF_ENC_001 拒绝（既有语义零回归）──

    [Fact]
    public async Task Native_mode_chinese_barcode_should_fail_with_lf_enc_001()
    {
        var (service, store) = CreateService(NativeConnection);

        var result = await service.SubmitAsync(CreateMixedRequest(
            "req-zebra-barcode-cn",
            new Dictionary<string, string>
            {
                ["code"] = "A-1",
                ["barcode"] = "库位A-01", // Code 128 仅可打印 ASCII
                ["qr"] = "库位A-01",
            }));

        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.EncodeFailed, result.ErrorCode); // LF_ENC_001（与图片模式渲染层拒绝同码，非 LF_ENC_002）
        Assert.Contains("labelframe-transport-zebra", result.ErrorMessage);
        Assert.Contains("Code 128", result.ErrorMessage);
        Assert.Equal("barcode", result.FieldKey);
        Assert.Null(await store.GetJobByRequestIdAsync("req-zebra-barcode-cn"));
    }
}
