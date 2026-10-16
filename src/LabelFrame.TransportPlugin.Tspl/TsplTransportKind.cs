namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>TSPL 连接类型枚举（字符串参数跨插件边界后于此解析）。</summary>
public enum TsplTransportKind
{
    /// <summary>TCP 9100（网络直连）。</summary>
    Tcp,

    /// <summary>USB 免驱动直连（usbprint 类驱动，CreateFile 直开设备）。</summary>
    Usb,
}
