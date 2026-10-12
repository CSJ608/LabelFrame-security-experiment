using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Sample;

namespace LabelFrame.Core.Tests.Transport;

public class TransportPluginRegistryExternalTests
{
    [Fact]
    public void RegisterExternal_builtin_id_should_skip_and_log()
    {
        var registry = new TransportPluginRegistry();
        foreach (var plugin in BuiltinTransportPlugins.CreateCorePlugins())
        {
            registry.Register(plugin);
        }

        var writer = new StringWriter();
        var external = new SampleTransportPlugin();
        // 篡改 id 为内置 id 的"外部插件"
        var shadow = new ShadowPlugin("log", "Shadow Log");

        Assert.False(registry.RegisterExternal(shadow, "C:\\plugins\\shadow.dll", writer));
        Assert.Contains("内置插件 ID 冲突", writer.ToString());
        Assert.Equal("模拟打印", registry.GetPlugin("log")!.DisplayName);  // 内置未被覆盖（去技术前缀「Log」）
    }

    [Fact]
    public void RegisterExternal_new_id_should_register()
    {
        var registry = new TransportPluginRegistry();
        var writer = new StringWriter();
        var sample = new SampleTransportPlugin();

        Assert.True(registry.RegisterExternal(sample, "C:\\plugins\\sample.dll", writer));
        Assert.True(registry.GetPlugin("sample")!.IsExternal);
        Assert.Equal("C:\\plugins\\sample.dll", registry.GetPlugin("sample")!.AssemblyPath);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void RegisterExternal_existing_external_should_overwrite()
    {
        var registry = new TransportPluginRegistry();
        var writer = new StringWriter();
        Assert.True(registry.RegisterExternal(new SampleTransportPlugin(), "C:\\plugins\\a.dll", writer));
        Assert.True(registry.RegisterExternal(new SampleTransportPlugin(), "C:\\plugins\\b.dll", writer));

        Assert.Equal("C:\\plugins\\b.dll", registry.GetPlugin("sample")!.AssemblyPath);
    }

    private sealed class ShadowPlugin : ITransportPlugin
    {
        private readonly string _id;
        private readonly string _displayName;

        public ShadowPlugin(string id, string displayName)
        {
            _id = id;
            _displayName = displayName;
        }

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Description => "shadow";
        public IReadOnlyList<TransportParameterSpec> Parameters => [];
        public string Describe(TransportPluginParameters parameters) => _id;
        public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
            => throw new NotSupportedException();
    }
}