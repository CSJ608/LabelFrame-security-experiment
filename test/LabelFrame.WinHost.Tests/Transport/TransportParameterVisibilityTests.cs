using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Zebra;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// 参数条件可见性（spec.VisibleWhen）与保存校验语义：必填只对当前可见字段生效、
/// 类型 / Select 校验对已提供字段照常；Zebra 插件字段集声明（按连接类型互斥）。
/// </summary>
public class TransportParameterVisibilityTests
{
    // ── spec.IsVisible 判定 ──

    [Fact]
    public void IsVisible_without_declaration_should_be_always_visible()
    {
        var spec = new TransportParameterSpec("host", "主机", TransportParameterType.String);
        Assert.True(spec.IsVisible(new Dictionary<string, string>()));
        Assert.True(spec.IsVisible(new Dictionary<string, string> { ["kind"] = "Driver" }));
    }

    [Fact]
    public void IsVisible_should_match_condition_value_case_insensitive()
    {
        var spec = new TransportParameterSpec(
            "host", "主机", TransportParameterType.String,
            VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"]);

        Assert.True(spec.IsVisible(new Dictionary<string, string> { ["kind"] = "Tcp" }));
        Assert.True(spec.IsVisible(new Dictionary<string, string> { ["kind"] = "tcp" }));
        Assert.False(spec.IsVisible(new Dictionary<string, string> { ["kind"] = "Driver" }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsVisible_should_be_false_when_condition_key_missing_or_empty(string? kindValue)
    {
        var spec = new TransportParameterSpec(
            "host", "主机", TransportParameterType.String,
            VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"]);

        var parameters = kindValue is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["kind"] = kindValue };
        Assert.False(spec.IsVisible(parameters));
    }

    // ── 保存校验语义（经 fake 插件走 TransportManager 完整链路；传输用 Log 不依赖真连接）──

    [Fact]
    public async Task ApplyAsync_required_param_visible_but_missing_should_reject()
    {
        var (manager, _) = CreateWithKindPlugin();
        var result = await manager.ApplyAsync(new TransportConfig
        {
            PluginId = "kindfake",
            Params = new Dictionary<string, string> { ["kind"] = "Tcp" },
        }, testOnly: false);

        Assert.False(result.Ok);
        Assert.Contains("主机地址", result.Message);
    }

    [Fact]
    public async Task ApplyAsync_required_param_hidden_should_skip_required_check()
    {
        // kind=Driver 时 host 不可见：即使缺失也不因 host 必填而拒绝（保存可成功）
        var (manager, _) = CreateWithKindPlugin();
        var result = await manager.ApplyAsync(new TransportConfig
        {
            PluginId = "kindfake",
            Params = new Dictionary<string, string> { ["kind"] = "Driver" },
        }, testOnly: false);

        Assert.True(result.Ok, result.Message);
    }

    [Fact]
    public async Task ApplyAsync_hidden_param_present_with_invalid_type_should_still_reject()
    {
        // 隐藏字段的类型校验对已提供值照常：存量配置携带隐藏字段非法值不放过
        var (manager, _) = CreateWithKindPlugin();
        var result = await manager.ApplyAsync(new TransportConfig
        {
            PluginId = "kindfake",
            Params = new Dictionary<string, string>
            {
                ["kind"] = "Driver",
                ["port"] = "abc",
            },
        }, testOnly: false);

        Assert.False(result.Ok);
        Assert.Contains("整数", result.Message);
    }

    // ── Zebra 插件字段集声明（按连接类型互斥；kind / printMode 恒显）──

    [Fact]
    public void Zebra_spec_should_declare_field_visibility_per_kind()
    {
        var plugin = new ZebraTransportPlugin();
        var kind = Find(plugin, "kind");
        var host = Find(plugin, "host");
        var port = Find(plugin, "port");
        var printerName = Find(plugin, "printerName");
        var usbName = Find(plugin, "usbName");
        var printMode = Find(plugin, TransportPrintMode.ParameterKey);

        Assert.Null(kind.VisibleWhenKey);
        Assert.Null(printMode.VisibleWhenKey);

        var tcp = new Dictionary<string, string> { ["kind"] = "Tcp" };
        var usb = new Dictionary<string, string> { ["kind"] = "Usb" };
        var driver = new Dictionary<string, string> { ["kind"] = "Driver" };

        // TCP：host / port 可见，打印机名与 USB 名不可见
        Assert.True(host.IsVisible(tcp));
        Assert.True(port.IsVisible(tcp));
        Assert.False(printerName.IsVisible(tcp));
        Assert.False(usbName.IsVisible(tcp));

        // USB：仅 usbName 可见（可空 = 自动发现）
        Assert.True(usbName.IsVisible(usb));
        Assert.False(host.IsVisible(usb));
        Assert.False(printerName.IsVisible(usb));

        // Windows 驱动：仅 printerName 可见
        Assert.True(printerName.IsVisible(driver));
        Assert.False(host.IsVisible(driver));
        Assert.False(usbName.IsVisible(driver));
    }

    private static TransportParameterSpec Find(ZebraTransportPlugin plugin, string key)
        => plugin.Parameters.Single(p => p.Key == key);

    /// <summary>
    /// 测试专用插件：host（必填 + kind=Tcp 可见）+ port（Int，kind=Tcp 可见）+ kind（Select 必填）；
    /// 传输复用 Log 模拟发送，使 ApplyAsync 全链路（校验 → 测试连接）不依赖真打印机。
    /// </summary>
    private sealed class KindFakeTransportPlugin : ITransportPlugin
    {
        public string Id => "kindfake";

        public string DisplayName => "字段可见性测试插件";

        public string Description => "测试专用：声明条件可见参数，不进产品装配。";

        public IReadOnlyList<TransportParameterSpec> Parameters { get; } =
        [
            new TransportParameterSpec(
                "kind", "连接类型", TransportParameterType.Select,
                Required: true, DefaultValue: "Tcp",
                Options: [new TransportParameterOption("Tcp", "TCP"), new TransportParameterOption("Driver", "Windows 驱动")]),
            new TransportParameterSpec(
                "host", "主机地址", TransportParameterType.String,
                Required: true, VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"]),
            new TransportParameterSpec(
                "port", "端口", TransportParameterType.Int,
                VisibleWhenKey: "kind", VisibleWhenValues: ["Tcp"]),
        ];

        public string Describe(TransportPluginParameters parameters) => "KINDFAKE";

        public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
            => new LogPrintTransport(context.HostLog);
    }

    private static (TransportManager Manager, string ConfigPath) CreateWithKindPlugin()
    {
        var registry = new TransportPluginRegistry();
        foreach (var plugin in BuiltinTransportPlugins.CreateCorePlugins())
        {
            registry.Register(plugin);
        }

        registry.Register(new KindFakeTransportPlugin());
        var path = Path.Combine(Path.GetTempPath(), $"lfconn-vis-{Guid.NewGuid():N}.json");
        var manager = new TransportManager(
            registry,
            new TransportPluginContext(TextWriter.Null, Path.GetTempPath()),
            new HostOptions { Transport = TransportMode.Log, TcpHost = "127.0.0.1", TcpPort = 9100, PrinterName = "Test Printer" },
            TextWriter.Null,
            path);
        return (manager, path);
    }
}
