using Android.Content;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.AndroidHost.Transport.Plugins;

/// <summary>
/// 内置 Zebra 传输插件（SDK 档，/ 双档模型的内置侧）：Link-OS SDK 编译进 APK 不外置——
/// 注册进插件注册表作 brand = zebra 的路由锚点与「外部包占用内置 id」三层校验拒绝锚点；
/// 参数模型即 PDA 配置的 { brand, connectionType, host, port } 结构（零变更）。
/// </summary>
public sealed class ZebraBuiltinTransportPlugin : ITransportPlugin
{
    /// <summary>内置插件 id（= 配置 printer_brand 的 zebra 值；外部包占用此 id 会被三层校验拒绝）。</summary>
    public const string PluginId = "zebra";

    private readonly Context _context;

    /// <summary>创建内置插件（持有 Android Context 供 SDK 建连）。</summary>
    public ZebraBuiltinTransportPlugin(Context context) => _context = context;

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string DisplayName => "Zebra（内置）";

    /// <inheritdoc />
    public string Description => "Zebra 官方 SDK 内置传输：网线 / 蓝牙 / USB 三种连接方式。";

    /// <inheritdoc />
    public IReadOnlyList<TransportParameterSpec> Parameters => new[]
    {
        new TransportParameterSpec(
            "connectionType", "连接方式", TransportParameterType.Select, Required: true, DefaultValue: ZebraSdkTransport.ConnectionTypeTcp, Options: new[]
            {
                new TransportParameterOption(ZebraSdkTransport.ConnectionTypeTcp, "网线"),
                new TransportParameterOption(ZebraSdkTransport.ConnectionTypeBluetooth, "蓝牙"),
                new TransportParameterOption(ZebraSdkTransport.ConnectionTypeUsb, "USB 数据线"),
            }),
        new TransportParameterSpec("host", "打印机 IP 地址", TransportParameterType.String, DefaultValue: "192.168.1.50", VisibleWhenKey: "connectionType", VisibleWhenValues: [ZebraSdkTransport.ConnectionTypeTcp]),
        new TransportParameterSpec("port", "端口", TransportParameterType.Int, DefaultValue: "9100", VisibleWhenKey: "connectionType", VisibleWhenValues: [ZebraSdkTransport.ConnectionTypeTcp]),
        new TransportParameterSpec("bluetoothMac", "蓝牙地址", TransportParameterType.String, VisibleWhenKey: "connectionType", VisibleWhenValues: [ZebraSdkTransport.ConnectionTypeBluetooth]),
    };

    /// <inheritdoc />
    public string Describe(TransportPluginParameters parameters) => ZebraSdkTransport.DescribeTarget(
        parameters.GetSelect("connectionType") ?? ZebraSdkTransport.ConnectionTypeTcp,
        parameters.GetString("host", string.Empty),
        parameters.GetInt("port", 9100),
        parameters.GetString("bluetoothMac", string.Empty));

    /// <inheritdoc />
    public IPrintTransport Create(TransportPluginParameters parameters, ITransportPluginContext context)
        => ZebraSdkTransport.Create(
            parameters.GetSelect("connectionType") ?? ZebraSdkTransport.ConnectionTypeTcp,
            parameters.GetString("host", string.Empty),
            parameters.GetInt("port", 9100),
            parameters.GetString("bluetoothMac", string.Empty),
            _context);
}
