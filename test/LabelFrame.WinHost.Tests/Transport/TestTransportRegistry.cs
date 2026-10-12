using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>测试用传输插件注册表 / 连接管理器工厂（注册表 = Core 内置 + WinHost 内置，同生产装配）。</summary>
public static class TestTransportRegistry
{
    /// <summary>生产同款注册表（log / tcp9100 / winspool；zebra 已外置为官方插件——）。</summary>
    public static TransportPluginRegistry Create()
    {
        var registry = new TransportPluginRegistry();
        foreach (var plugin in BuiltinTransportPlugins.CreateCorePlugins())
        {
            registry.Register(plugin);
        }

        registry.Register(new WinspoolTransportPlugin());
        return registry;
    }

    /// <summary>创建连接管理器（connection.json 可注入临时路径；hostLogWriter 可注入捕获回退留痕）。</summary>
    public static TransportManager CreateManager(HostOptions? options = null, string? configFilePath = null, TextWriter? hostLogWriter = null)
    {
        options ??= new HostOptions { Transport = TransportMode.Log, TcpHost = "127.0.0.1", TcpPort = 9100, PrinterName = "Test Printer" };
        var path = configFilePath ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        return new TransportManager(
            Create(),
            new TransportPluginContext(TextWriter.Null, System.IO.Path.GetTempPath()),
            options,
            hostLogWriter ?? TextWriter.Null,
            path);
    }

    /// <summary>测试用插件上下文（日志可注入捕获）。</summary>
    public static TransportPluginContext CreateContext(TextWriter? hostLog = null)
        => new(hostLog ?? TextWriter.Null, System.IO.Path.GetTempPath());

    /// <summary>
    /// 创建连接管理器与所用注册表（JobSubmissionService 需同时持有两者的测试用）：
    /// 可注入自定义插件（fake 编译器）、可预写 connection.json（原生指令模式 / 存量异常配置场景）。
    /// </summary>
    public static (TransportManager Manager, TransportPluginRegistry Registry) CreateManagerWithRegistry(
        Action<TransportPluginRegistry>? configure = null,
        HostOptions? options = null,
        string? connectionJson = null,
        TextWriter? hostLogWriter = null)
    {
        options ??= new HostOptions { Transport = TransportMode.Log, TcpHost = "127.0.0.1", TcpPort = 9100, PrinterName = "Test Printer" };
        var registry = Create();
        configure?.Invoke(registry);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfconn-{Guid.NewGuid():N}.json");
        if (connectionJson is not null)
        {
            System.IO.File.WriteAllText(path, connectionJson);
        }

        var manager = new TransportManager(registry, CreateContext(), options, hostLogWriter ?? TextWriter.Null, path);
        return (manager, registry);
    }
}
