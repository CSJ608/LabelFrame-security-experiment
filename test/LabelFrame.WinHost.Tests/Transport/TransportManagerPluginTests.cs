using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>TransportManager 插件化路径（pluginId + params 装配 / 校验 / 测试 / 持久化）。</summary>
public class TransportManagerPluginTests
{
    [Fact]
    public async Task ApplyAsync_by_plugin_id_log_should_succeed_and_persist_plugin_format()
    {
        var (manager, path) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { PluginId = "log" }, testOnly: false);

        Assert.True(result.Ok);
        Assert.Equal("log", manager.CurrentConfig.PluginId);
        Assert.True(File.Exists(path));
        Assert.Contains("\"PluginId\": \"log\"", File.ReadAllText(path));
    }

    [Fact]
    public async Task ApplyAsync_by_plugin_id_tcp_unreachable_should_fail_and_keep_log()
    {
        var (manager, path) = Create();
        var result = await manager.ApplyAsync(new TransportConfig
        {
            PluginId = "tcp9100",
            Params = new Dictionary<string, string> { ["host"] = "127.0.0.1", ["port"] = "1" },
        }, testOnly: false);

        Assert.False(result.Ok);
        Assert.Equal("log", manager.CurrentConfig.PluginId);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ApplyAsync_unknown_plugin_should_fail()
    {
        var (manager, _) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { PluginId = "no-such-plugin" }, testOnly: false);

        Assert.False(result.Ok);
        Assert.Contains("no-such-plugin", result.Message);
    }

    [Fact]
    public async Task ApplyAsync_missing_required_param_should_fail_with_label()
    {
        var (manager, _) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { PluginId = "winspool" }, testOnly: false);

        Assert.False(result.Ok);
        Assert.Contains("printerName", result.Message);
        Assert.Contains("Windows 打印机名", result.Message);
    }

    [Fact]
    public async Task ApplyAsync_int_param_invalid_should_fail()
    {
        var (manager, _) = Create();
        var result = await manager.ApplyAsync(new TransportConfig
        {
            PluginId = "tcp9100",
            Params = new Dictionary<string, string> { ["host"] = "127.0.0.1", ["port"] = "abc" },
        }, testOnly: false);

        Assert.False(result.Ok);
        Assert.Contains("整数", result.Message);
    }

    [Fact]
    public async Task TestOnly_plugin_should_not_switch_or_persist()
    {
        var (manager, path) = Create();
        var result = await manager.ApplyAsync(new TransportConfig { PluginId = "log" }, testOnly: true);

        Assert.True(result.Ok);
        Assert.False(File.Exists(path));
    }

    // ── 打印方式合法性（DESIGN 保存层校验）──

    [Fact]
    public async Task ApplyAsync_native_print_mode_without_capability_should_reject()
    {
        var (manager, path) = Create();
        var result = await manager.ApplyAsync(new TransportConfig
        {
            PluginId = "log",
            Params = new Dictionary<string, string> { ["printMode"] = "native" },
        }, testOnly: false);

        // native 仅对能力位为真的插件合法：保存校验失败（中文提示），连接不切换不持久化
        Assert.False(result.Ok);
        Assert.Contains("原生指令", result.Message);
        Assert.Contains("log", result.Message);
        Assert.Contains("切回图片或更换插件", result.Message);
        Assert.Equal("log", manager.CurrentConfig.PluginId);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ApplyAsync_native_print_mode_with_capability_should_succeed_and_persist()
    {
        // fake 编译器插件（能力位真）：native 保存合法（连接测试按传输实例能力走，fake 非 ITestableTransport 即通过）
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        try
        {
            var registry = TestTransportRegistry.Create();
            registry.Register(new FakeCompilerTransportPlugin());
            var manager = new TransportManager(
                registry,
                TestTransportRegistry.CreateContext(),
                new HostOptions { Transport = TransportMode.Log, TcpHost = "127.0.0.1", TcpPort = 9100, PrinterName = "Test Printer" },
                TextWriter.Null,
                path);

            var result = await manager.ApplyAsync(new TransportConfig
            {
                PluginId = "fakecompile",
                Params = new Dictionary<string, string> { ["printMode"] = "native" },
            }, testOnly: false);

            Assert.True(result.Ok, result.Message);
            Assert.Equal("fakecompile", manager.CurrentConfig.PluginId);
            Assert.Equal("native", manager.CurrentConfig.Params["printMode"]);
            Assert.Contains("\"printMode\": \"native\"", File.ReadAllText(path));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public async Task ApplyAsync_image_print_mode_without_capability_should_stay_valid()
    {
        // 非法 / 缺省取值回退 image 语义：image 值对任何插件合法（含无能力插件）
        var (manager, _) = Create();
        var result = await manager.ApplyAsync(new TransportConfig
        {
            PluginId = "log",
            Params = new Dictionary<string, string> { ["printMode"] = "image" },
        }, testOnly: false);

        Assert.True(result.Ok);
    }


    [Fact]
    public void Startup_should_fall_back_to_log_when_persisted_plugin_missing()
    {
        // 卸载外部插件 = 删除 DLL + 重启生效：connection.json 仍引用已删除插件时宿主必须正常启动（回退默认连接 + 日志警告）
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, """{"PluginId":"ghost-plugin","Params":{"x":"1"}}""");
        try
        {
            var options = new HostOptions { Transport = TransportMode.Log, TcpHost = "127.0.0.1", TcpPort = 9100, PrinterName = "Test Printer" };
            var registry = TestTransportRegistry.Create();
            var log = new StringWriter();
            var manager = new TransportManager(
                registry,
                new TransportPluginContext(log, System.IO.Path.GetTempPath()),
                options,
                log,
                path);

            Assert.Equal("log", manager.CurrentConfig.PluginId);
            Assert.Equal(TransportMode.Log, manager.CurrentConfig.Mode);
            Assert.Contains("ghost-plugin", log.ToString());
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
    private static (TransportManager Manager, string ConfigPath) Create()
    {
        var options = new HostOptions { Transport = TransportMode.Log, TcpHost = "127.0.0.1", TcpPort = 9100, PrinterName = "Test Printer" };
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        var manager = TestTransportRegistry.CreateManager(options, path);
        return (manager, path);
    }
}
