using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.Core.Tests.Transport;

public class TransportPluginRegistryTests
{
    private static (TransportPluginRegistry Registry, TransportPluginContext Context) Create()
    {
        var registry = new TransportPluginRegistry();
        foreach (var plugin in BuiltinTransportPlugins.CreateCorePlugins())
        {
            registry.Register(plugin);
        }

        return (registry, new TransportPluginContext(TextWriter.Null, Path.GetTempPath()));
    }

    [Fact]
    public void ListPlugins_should_include_builtin_log_and_tcp9100()
    {
        var (registry, _) = Create();

        var ids = registry.ListPlugins().Select(p => p.Id).ToList();

        Assert.Contains("log", ids);
        Assert.Contains("tcp9100", ids);
        var tcp = Assert.Single(registry.ListPlugins().Where(p => p.Id == "tcp9100"));
        Assert.Equal("网口打印机（TCP 9100）", tcp.DisplayName);
        Assert.Contains(tcp.Parameters, p => p.Key == "host" && p.Required);
        Assert.Contains(tcp.Parameters, p => p.Key == "port" && p.Type == TransportParameterType.Int);
    }

    [Fact]
    public void GetPlugin_should_be_case_insensitive()
    {
        var (registry, _) = Create();
        Assert.NotNull(registry.GetPlugin("TCP9100"));
        Assert.Null(registry.GetPlugin("missing"));
    }

    [Fact]
    public void CreateTransport_log_should_use_host_log_writer()
    {
        var (registry, context) = Create();
        var transport = registry.CreateTransport("log", new TransportPluginParameters(), context);
        Assert.IsType<LogPrintTransport>(transport);
    }

    [Fact]
    public void CreateTransport_tcp9100_should_build_with_params()
    {
        var (registry, context) = Create();
        var transport = registry.CreateTransport(
            "tcp9100",
            new TransportPluginParameters(new Dictionary<string, string> { ["host"] = "192.168.1.50", ["port"] = "9100" }),
            context);
        Assert.IsType<Tcp9100PrintTransport>(transport);
    }

    [Fact]
    public void CreateTransport_missing_plugin_should_throw()
    {
        var (registry, context) = Create();
        Assert.Throws<InvalidOperationException>(() => registry.CreateTransport("nope", new TransportPluginParameters(), context));
    }

    [Fact]
    public void Describe_should_use_plugin_describe()
    {
        var (registry, _) = Create();
        Assert.Equal("模拟打印", registry.Describe("log", new TransportPluginParameters()));
        Assert.Equal(
            "TCP 192.168.1.50:9100",
            registry.Describe("tcp9100", new TransportPluginParameters(new Dictionary<string, string> { ["host"] = "192.168.1.50", ["port"] = "9100" })));
        Assert.Equal("nope", registry.Describe("nope", new TransportPluginParameters()));
    }

    [Fact]
    public void Log_plugin_copy_should_be_user_friendly()
    {
        // （评审 / /）：徽标 / 摘要取值（Describe）用户化「模拟打印」，不再直出「LOG」
        // 说明文（Description）句读完整且无开发者用语（「联调」）。
        // 选项列表显示名（DisplayName）同步去技术前缀「Log」；插件 Id 保持 log（存量连接兼容）。
        var (registry, _) = Create();
        var log = registry.GetPlugin("log")!;
        Assert.Equal("log", log.Id);
        Assert.Equal("模拟打印", registry.Describe("log", new TransportPluginParameters()));
        Assert.Equal("模拟打印", log.DisplayName);
        Assert.DoesNotContain("Log", log.DisplayName);
        Assert.DoesNotContain("联调", log.Description);
        Assert.DoesNotContain("LOG", log.Description);
        Assert.EndsWith("。", log.Description);
    }

    [Fact]
    public void Duplicate_register_should_override()
    {
        var (registry, context) = Create();
        registry.Register(new SampleOverridePlugin());
            var plugin = registry.GetPlugin("log");
        Assert.Equal("覆盖", registry.GetPlugin("log")!.DisplayName);
    }

    private sealed class SampleOverridePlugin : ITransportPlugin
    {
        public string Id => "log";
        public string DisplayName => "覆盖";
        public string Description => string.Empty;
        public IReadOnlyList<TransportParameterSpec> Parameters => [];
        public string Describe(TransportPluginParameters parameters) => "OVERRIDE";
        public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
            => new LogPrintTransport(context.HostLog);
    }
}
