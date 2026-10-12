using Android.Content;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.AndroidHost.Transport.Plugins;

/// <summary>打印传输品牌路由：brand = zebra（或空）→ 内置 SDK 档 <see cref="ZebraSdkTransport"/>；
/// brand = 外部插件 id → 注册表装配插件传输；配置引用未安装插件 → 回退内置 Zebra + 中文留痕（不崩溃）。</summary>
public static class AndroidTransportFactory
{
    /// <summary>按配置品牌创建打印传输。</summary>
    public static IPrintTransport Create(
        LabelHostConfig config,
        ITransportPluginRegistry registry,
        ITransportPluginContext pluginContext,
        Context androidContext)
    {
        var brand = config.PrinterBrand.Trim();
        if (brand.Length == 0 || string.Equals(brand, ZebraBuiltinTransportPlugin.PluginId, StringComparison.OrdinalIgnoreCase))
        {
            return ZebraSdkTransport.Create(config, androidContext);
        }

        var descriptor = registry.GetPlugin(brand);
        if (descriptor is { IsExternal: true })
        {
            var parameters = new TransportPluginParameters(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // 配置结构 { brand, connectionType, host, port } 零变更：插件品牌连接类型固定 tcp 起步
                ["connectionType"] = ZebraSdkTransport.ConnectionTypeTcp,
                ["host"] = config.TcpHost,
                ["port"] = config.TcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            HostLog.Info(HostLog.Tags.Plugin, $"品牌 {descriptor.DisplayName}（{descriptor.Id}）走外置插件传输：{config.TcpHost}:{config.TcpPort}");
            return registry.CreateTransport(descriptor.Id, parameters, pluginContext);
        }

        HostLog.Warn(
            HostLog.Tags.Plugin,
            $"配置的打印机品牌「{brand}」传输插件未安装，已回退内置 Zebra 传输——请在「插件管理」安装该品牌插件，或改选 Zebra。");
        return ZebraSdkTransport.Create(config, androidContext);
    }
}
