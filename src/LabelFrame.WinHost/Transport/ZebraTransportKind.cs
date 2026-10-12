namespace LabelFrame.WinHost.Transport;

/// <summary>Zebra SDK 连接类型；仅存于公共配置模型用于旧 connection.json / appsettings / 环境变量兼容。</summary>
/// <remarks>
/// zebra 传输外置为官方插件后，本枚举只为旧配置面的兼容反序列化服务——
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
