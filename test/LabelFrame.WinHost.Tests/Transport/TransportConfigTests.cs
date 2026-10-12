using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

public class TransportConfigTests
{
    [Fact]
    public void FromJson_legacy_tcp_should_migrate_to_plugin_params()
    {
        var config = TransportConfig.FromJson("""{"Mode":"Tcp","TcpHost":"192.168.1.50","TcpPort":9100}""");

        Assert.NotNull(config);
        Assert.Equal("tcp9100", config!.PluginId);
        Assert.Equal("192.168.1.50", config.Params["host"]);
        Assert.Equal("9100", config.Params["port"]);
        Assert.Equal(TransportMode.Tcp, config.Mode);
        Assert.Equal("192.168.1.50", config.TcpHost);
    }

    [Fact]
    public void FromJson_legacy_windows_driver_should_migrate()
    {
        var config = TransportConfig.FromJson("""{"Mode":"WindowsDriver","PrinterName":"ZDesigner ZD421"}""");

        Assert.NotNull(config);
        Assert.Equal("winspool", config!.PluginId);
        Assert.Equal("ZDesigner ZD421", config.Params["printerName"]);
    }

    [Fact]
    public void FromJson_legacy_zebra_should_migrate_kind_and_params()
    {
        // 旧 Mode=Zebra → 外置官方插件 id（连接配置 pluginId 用官方 id）
        var config = TransportConfig.FromJson("""{"Mode":"Zebra","ZebraKind":"Tcp","TcpHost":"10.0.0.9","TcpPort":9100}""");

        Assert.NotNull(config);
        Assert.Equal("labelframe-transport-zebra", config!.PluginId);
        Assert.Equal("Tcp", config.Params["kind"]);
        Assert.Equal("10.0.0.9", config.Params["host"]);
        Assert.Equal(TransportMode.Zebra, config.Mode); // 旧字段展示兼容
    }

    [Fact]
    public void FromJson_legacy_zebra_alias_plugin_id_should_normalize_to_official_id()
    {
        // 旧内置时代 pluginId "zebra"（≤0.26 存量 connection.json）读取别名 → 官方 id（内存态，不落盘迁移）
        var config = TransportConfig.FromJson("""{"PluginId":"zebra","Params":{"kind":"Tcp","host":"10.0.0.9","port":"9100"}}""");

        Assert.NotNull(config);
        Assert.Equal("labelframe-transport-zebra", config!.PluginId);
        Assert.Equal(TransportMode.Zebra, config.Mode);
        Assert.Equal("10.0.0.9", config.Params["host"]);
    }

    [Fact]
    public void ToJson_should_round_trip_plugin_format()
    {
        var config = new TransportConfig
        {
            PluginId = "tcp9100",
            Params = new Dictionary<string, string> { ["host"] = "192.168.1.50", ["port"] = "9100" },
        };

        var json = config.ToJson();
        var restored = TransportConfig.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal("tcp9100", restored!.PluginId);
        Assert.Equal("192.168.1.50", restored.Params["host"]);
        Assert.Equal(TransportMode.Tcp, restored.Mode); // 旧字段同步写出
    }

    [Fact]
    public void FromJson_invalid_should_return_null()
    {
        Assert.Null(TransportConfig.FromJson("not json"));
    }

    [Fact]
    public void MapModeToPluginId_should_cover_all_modes()
    {
        Assert.Equal("log", TransportConfig.MapModeToPluginId(TransportMode.Log));
        Assert.Equal("tcp9100", TransportConfig.MapModeToPluginId(TransportMode.Tcp));
        Assert.Equal("winspool", TransportConfig.MapModeToPluginId(TransportMode.WindowsDriver));
        Assert.Equal("labelframe-transport-zebra", TransportConfig.MapModeToPluginId(TransportMode.Zebra));
    }
}
