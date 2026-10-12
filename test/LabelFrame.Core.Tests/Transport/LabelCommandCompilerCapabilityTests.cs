using LabelFrame.Core.Documents;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.Core.Tests.Transport;

/// <summary>
/// 文档编译能力契约锚定：
/// 装配期能力位判定（plugin is ILabelCommandCompiler）+ 注册表编译器取用 + printMode 读取口径。
/// </summary>
public class LabelCommandCompilerCapabilityTests
{
    /// <summary>测试用编译插件：同一对象实现 ITransportPlugin + ILabelCommandCompiler（能力模式先例）。</summary>
    private sealed class TestCompilerPlugin : ITransportPlugin, ILabelCommandCompiler
    {
        public string Id => "testcompiler";
        public string DisplayName => "测试编译插件";
        public string Description => "测试用。";
        public IReadOnlyList<TransportParameterSpec> Parameters => [];
        public string Describe(TransportPluginParameters parameters) => "TEST-COMPILER";
        public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
            => new LogPrintTransport(context.HostLog);

        public Task<LabelCommandCompileResult> CompileAsync(
            LabelDocument document,
            LabelCommandCompileOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LabelCommandCompileResult("^XATEST^XZ", null, null, null));
    }

    private static TransportPluginRegistry CreateRegistry()
    {
        var registry = new TransportPluginRegistry();
        foreach (var plugin in BuiltinTransportPlugins.CreateCorePlugins())
        {
            registry.Register(plugin);
        }

        return registry;
    }

    [Fact]
    public void Compiler_plugin_should_set_capability_bit_and_be_resolvable()
    {
        var registry = CreateRegistry();
        var plugin = new TestCompilerPlugin();
        registry.Register(plugin);

        var descriptor = registry.GetPlugin("testcompiler");
        Assert.NotNull(descriptor);
        Assert.True(descriptor!.SupportsDocumentCompile); // 装配期 plugin is ILabelCommandCompiler 判定
        Assert.Same(plugin, registry.GetCommandCompiler("testcompiler"));
        Assert.Contains(registry.ListPlugins(), p => p.Id == "testcompiler" && p.SupportsDocumentCompile);
    }

    [Fact]
    public void Builtin_plugins_should_not_support_document_compile()
    {
        var registry = CreateRegistry();

        foreach (var descriptor in registry.ListPlugins())
        {
            Assert.False(descriptor.SupportsDocumentCompile, $"内置插件 {descriptor.Id} 不应具备文档编译能力。");
        }

        Assert.Null(registry.GetCommandCompiler("log"));
        Assert.Null(registry.GetCommandCompiler("tcp9100"));
        Assert.Null(registry.GetCommandCompiler("missing"));
    }

    [Fact]
    public void Compile_options_should_carry_dpi_and_context()
    {
        // 输入选项：首版 = DPI + 插件上下文（与 Create 收到的 ITransportPluginContext 同构）
        var context = new TransportPluginContext(TextWriter.Null, "dir");
        var options = new LabelCommandCompileOptions(300, context);

        Assert.Equal(300, options.Dpi);
        Assert.Same(context, options.Context);
    }

    [Theory]
    [InlineData(null, TransportPrintMode.Image)]      // 缺失回默认
    [InlineData("", TransportPrintMode.Image)]        // 空白回默认
    [InlineData("image", TransportPrintMode.Image)]
    [InlineData("IMAGE", TransportPrintMode.Image)]   // 忽略大小写
    [InlineData("native", TransportPrintMode.Native)]
    [InlineData("Native", TransportPrintMode.Native)] // 忽略大小写
    [InlineData(" native ", TransportPrintMode.Native)] // 容忍首尾空白
    [InlineData("garbage", TransportPrintMode.Image)] // 非法值回默认
    public void Resolve_should_fall_back_to_image_except_native(string? raw, string expected)
    {
        Assert.Equal(expected, TransportPrintMode.Resolve(raw));
    }
}
