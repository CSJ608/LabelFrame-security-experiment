namespace LabelFrame.WinHost.Transport;

/// <summary>Zebra SDK 连接类型；保留在公共配置模型中用于旧 connection.json 兼容。</summary>
/// <remarks>
/// 起 zebra 传输外置为官方插件（labelframe-transport-zebra），本枚举仅为
/// 旧配置面（connection.json 旧字段 / appsettings / 环境变量）的兼容反序列化服务——
/// 取值以字符串（"Tcp" / "Usb" / "Driver"）写入插件参数 kind，不与插件侧类型跨越 ALC 边界共享。
/// </remarks>
public enum ZebraTransportKind
{
    /// <summary>TCP/IP 网络打印机（默认 9100）。</summary>
    Tcp,

    /// <summary>USB 直连（ZebraUsbName 为空时自动发现第一台）。</summary>
    Usb,

    /// <summary>Windows 驱动（按打印机名）。</summary>
    Driver,
}
