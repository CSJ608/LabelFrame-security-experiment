using LabelFrame.Api;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Validation;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Tests.Samples;
using LabelFrame.WinHost.Tests.Transport;

namespace LabelFrame.WinHost.Tests.Jobs;

/// <summary>
/// 提交链路编译分派（DESIGN /）：fake 编译器插件全链路锚定——
/// native 逐张编译入 LabelJobItem.Zpl / 编译失败整体拒绝 / 无能力显式失败（LF_ENC_003）/
/// 缺失与非法回退 image / 幂等不重编译。fake 插件仅测试工程使用，不进产品装配。
/// </summary>
public class JobSubmissionCompileTests
{
    /// <summary>构造提交服务：注册表含内置插件 + fake 编译器插件，connection.json 预写指定内容。</summary>
    private static (JobSubmissionService Service, SqliteLabelJobStore Store, FakeCompilerTransportPlugin Plugin) CreateService(
        string connectionJson,
        FakeCompilerTransportPlugin? plugin = null,
        TextWriter? hostLogWriter = null)
    {
        plugin ??= new FakeCompilerTransportPlugin();

        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfhost-{Guid.NewGuid():N}.db");
        var store = new SqliteLabelJobStore(dbPath);
        store.InitializeAsync().GetAwaiter().GetResult();
        var queue = new LabelJobQueue(store);

        var templatesDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        var templates = new TemplateStore(templatesDb);
        templates.InitializeAsync().GetAwaiter().GetResult();

        var (manager, registry) = TestTransportRegistry.CreateManagerWithRegistry(
            configure: r => r.Register(plugin),
            connectionJson: connectionJson,
            hostLogWriter: hostLogWriter);
        var service = new JobSubmissionService(
            queue,
            new ZplImageEncoder(),
            dpi: 203,
            new SkiaLabelRenderer(),
            templates,
            manager,
            registry,
            TestTransportRegistry.CreateContext(),
            hostLogWriter ?? TextWriter.Null);
        return (service, store, plugin);
    }

    private static SubmitJobRequest CreateRequest(string requestId, params IReadOnlyDictionary<string, string>[] labels) => new(
        requestId,
        new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout),
        labels.Select(d => new LabelDto(d)).ToList());

    private static readonly Dictionary<string, string> LabelA = new() { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" };
    private static readonly Dictionary<string, string> LabelB = new() { ["zone"] = "B-02", ["locationCode"] = "B-02-01-01" };

    private const string NativeConnection = """{ "pluginId": "fakecompile", "params": { "printMode": "native" } }""";

    // ── ：native 逐张编译，产物入 LabelJobItem.Zpl（字段 / 列不改名）──

    [Fact]
    public async Task Native_mode_should_compile_each_label_into_job_item_zpl()
    {
        var (service, store, plugin) = CreateService(NativeConnection);
        var request = CreateRequest("req-native-ok", LabelA, LabelB);

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        Assert.True(result.Created);
        var stored = await store.GetJobAsync(result.Job!.Id);
        Assert.Equal(2, stored!.Items.Count);
        // 逐张编译：产物为 fake 编译器确定性指令（含数据与 DPI），不是 ^GF 位图
        Assert.Equal("^XAFKE:A-01-02-03@203^FS^XZ", stored.Items[0].Zpl);
        Assert.Equal("^XAFKE:B-02-01-01@203^FS^XZ", stored.Items[1].Zpl);
        Assert.DoesNotContain("^GF", stored.Items[0].Zpl);
        Assert.Equal(2, plugin.CompileCallCount);
    }

    // ── ：编译失败整体拒绝（直连不建作业 + 码 / 中文原因 / fieldKey）──

    [Fact]
    public async Task Native_compile_failure_should_reject_whole_job_with_code_and_fieldkey()
    {
        var plugin = new FakeCompilerTransportPlugin((document, options) =>
            document.Data.GetValueOrDefault("locationCode") == "BAD"
                ? new LabelCommandCompileResult(null, JobErrorCodes.CommandCompileFailed, "内置字体无法表示该字符", "locationCode")
                : new LabelCommandCompileResult("^XAFKE:OK^FS^XZ", null, null, null));
        var (service, store, _) = CreateService(NativeConnection, plugin);
        var request = CreateRequest("req-native-fail", LabelA, new Dictionary<string, string> { ["zone"] = "A", ["locationCode"] = "BAD" });

        var result = await service.SubmitAsync(request);

        // 任一标签失败 → 整体拒绝（缺数据不打半张）：不建作业
        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("插件 fakecompile", result.ErrorMessage);
        Assert.Contains("内置字体无法表示该字符", result.ErrorMessage);
        Assert.Contains("切回图片", result.ErrorMessage);
        Assert.Equal("locationCode", result.FieldKey);
        Assert.Null(await store.GetJobByRequestIdAsync("req-native-fail"));
    }

    [Fact]
    public async Task Native_compile_exception_should_map_to_same_failure_semantics()
    {
        // 意外异常由宿主捕获并映射为同一失败语义（不透出堆栈）
        var plugin = new FakeCompilerTransportPlugin((_, _) => throw new InvalidOperationException("boom: 内部细节"));
        var (service, store, _) = CreateService(NativeConnection, plugin);

        var result = await service.SubmitAsync(CreateRequest("req-native-exception", LabelA));

        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.CommandCompileFailed, result.ErrorCode);
        Assert.Contains("插件 fakecompile", result.ErrorMessage);
        Assert.DoesNotContain("   at", result.ErrorMessage); // 无堆栈
        Assert.Null(await store.GetJobByRequestIdAsync("req-native-exception"));
    }

    // ── ：native + 无能力插件 = 存量异常配置兜底，显式失败不静默按图片 ──

    [Fact]
    public async Task Native_mode_without_capability_should_fail_explicitly()
    {
        // 手改 connection.json 场景：printMode=native 挂在无编译能力的内置 log 插件上
        var (service, store, plugin) = CreateService("""{ "pluginId": "log", "params": { "printMode": "native" } }""");

        var result = await service.SubmitAsync(CreateRequest("req-native-nocap", LabelA));

        Assert.Null(result.Job);
        Assert.Equal(JobErrorCodes.PrintModeNotSupported, result.ErrorCode);
        Assert.Contains("原生指令", result.ErrorMessage);
        Assert.Contains("log", result.ErrorMessage);
        Assert.Contains("切回图片或更换插件", result.ErrorMessage);
        Assert.Null(await store.GetJobByRequestIdAsync("req-native-nocap"));
        Assert.Equal(0, plugin.CompileCallCount);
    }

    // ── ：printMode 缺失 / 非法值回退 image（默认值语义， 兼容演进风格）──

    [Fact]
    public async Task Missing_print_mode_should_fall_back_to_image_path()
    {
        var (service, store, plugin) = CreateService("""{ "pluginId": "fakecompile", "params": {} }""");

        var result = await service.SubmitAsync(CreateRequest("req-missing-mode", LabelA));

        Assert.NotNull(result.Job);
        var stored = await store.GetJobAsync(result.Job!.Id);
        Assert.Contains("^GF", stored!.Items[0].Zpl); // 既有渲染路径
        Assert.Equal(0, plugin.CompileCallCount); // 编译器零调用
    }

    [Fact]
    public async Task Invalid_print_mode_should_fall_back_to_image_path()
    {
        var (service, store, plugin) = CreateService("""{ "pluginId": "fakecompile", "params": { "printMode": "garbage" } }""");

        var result = await service.SubmitAsync(CreateRequest("req-invalid-mode", LabelA));

        Assert.NotNull(result.Job);
        var stored = await store.GetJobAsync(result.Job!.Id);
        Assert.Contains("^GF", stored!.Items[0].Zpl);
        Assert.Equal(0, plugin.CompileCallCount);
    }

    // ── ：幂等不重编译（同 requestId 重放返回既有作业，编译调用计数不增加）──

    [Fact]
    public async Task Duplicate_request_should_not_recompile()
    {
        var (service, _, plugin) = CreateService(NativeConnection);
        var request = CreateRequest("req-native-idempotent", LabelA, LabelB);

        var first = await service.SubmitAsync(request);
        var second = await service.SubmitAsync(request);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Job!.Id, second.Job!.Id);
        Assert.Equal(2, first.Job.Items.Count);
        Assert.Equal(2, plugin.CompileCallCount); // 重放零新增编译调用
    }

    // ── 补充锚定：native 路径契约校验先于编译（缺必填字段不打半张、不空跑编译器）──

    [Fact]
    public async Task Native_mode_should_validate_contract_before_compile()
    {
        var (service, store, plugin) = CreateService(NativeConnection);
        var request = CreateRequest("req-native-invalid", new Dictionary<string, string> { ["zone"] = "A-01" }); // 缺 locationCode

        var result = await service.SubmitAsync(request);

        Assert.Null(result.Job);
        Assert.Equal(LabelProblemCodes.RequiredFieldMissing, result.ErrorCode);
        Assert.Equal("locationCode", result.FieldKey);
        Assert.Equal(0, plugin.CompileCallCount);
        Assert.Null(await store.GetJobByRequestIdAsync("req-native-invalid"));
    }

    // ── 补充锚定：native + Log 兼容传输（fake 走 Log 发送）不产出渲染 PNG、作业数据留痕照常 ──

    [Fact]
    public async Task Native_mode_with_log_compatible_transport_should_log_job_data_without_png()
    {
        var hostLog = new StringWriter();
        var (service, _, _) = CreateService(NativeConnection, hostLogWriter: hostLog);
        var request = new SubmitJobRequest(
            "req-native-logdata",
            new TemplateDto(LocationLabelSamples.Contract, LocationLabelSamples.Layout, Name: "库位标签"),
            [new LabelDto(LabelA)]);

        var result = await service.SubmitAsync(request);

        Assert.NotNull(result.Job);
        // 原生指令路径无渲染位图：不出现 PNG 出图留痕，但作业数据留痕（语义）照常
        var logText = hostLog.ToString();
        Assert.DoesNotContain("已保存", logText);
        Assert.Contains("作业数据", logText);
        Assert.Contains("模板 库位标签", logText);
    }
}
