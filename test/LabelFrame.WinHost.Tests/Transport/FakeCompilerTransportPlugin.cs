using LabelFrame.Core.Documents;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// 测试专用 fake 编译器插件：同一对象实现 ITransportPlugin + ILabelCommandCompiler（可选能力模式），
/// 用于锚定宿主提交链路的编译分派。仅测试工程使用，不进产品装配；
/// 声明 printMode Select 参数，传输实例复用 Log 模拟发送。
/// </summary>
public sealed class FakeCompilerTransportPlugin : ITransportPlugin, ILabelCommandCompiler
{
    private readonly Func<LabelDocument, LabelCommandCompileOptions, LabelCommandCompileResult> _compile;

    /// <summary>编译调用计数（幂等不重编译 / 逐张编译断言用）。</summary>
    public int CompileCallCount { get; private set; }

    public FakeCompilerTransportPlugin(
        Func<LabelDocument, LabelCommandCompileOptions, LabelCommandCompileResult>? compile = null)
    {
        _compile = compile ?? DefaultCompile;
    }

    /// <summary>默认行为：按标签数据产出确定性指令文本（区分逐张内容）。</summary>
    private static LabelCommandCompileResult DefaultCompile(LabelDocument document, LabelCommandCompileOptions options)
        => new($"^XAFKE:{document.Data.GetValueOrDefault("locationCode")}@{options.Dpi}^FS^XZ", null, null, null);

    /// <inheritdoc />
    public string Id => "fakecompile";

    /// <inheritdoc />
    public string DisplayName => "Fake 编译器（测试专用）";

    /// <inheritdoc />
    public string Description => "测试专用：带文档编译能力的假传输插件，不进产品装配。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters { get; } =
    [
        new TransportParameterSpec(
            TransportPrintMode.ParameterKey,
            "打印方式",
            TransportParameterType.Select,
            Required: true,
            DefaultValue: TransportPrintMode.Image,
            Options:
            [
                new TransportParameterOption(TransportPrintMode.Image, "图片（默认）"),
                new TransportParameterOption(TransportPrintMode.Native, "原生指令"),
            ]),
    ];

    /// <inheritdoc />
    public string Describe(TransportPluginParameters parameters) => "FAKE-COMPILE";

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
        => new LogPrintTransport(context.HostLog);

    /// <inheritdoc />
    public Task<LabelCommandCompileResult> CompileAsync(
        LabelDocument document,
        LabelCommandCompileOptions options,
        CancellationToken cancellationToken = default)
    {
        CompileCallCount++;
        return Task.FromResult(_compile(document, options));
    }
}
