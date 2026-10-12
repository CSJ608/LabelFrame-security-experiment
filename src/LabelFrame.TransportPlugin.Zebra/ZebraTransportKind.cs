namespace LabelFrame.TransportPlugin.Zebra;

/// <summary>Zebra 连接类型（插件参数 kind 的取值域）。</summary>
public enum ZebraTransportKind
{
    /// <summary>TCP（地址 + 端口）。</summary>
    Tcp,

    /// <summary>USB（SDK 自动发现第一台 Zebra 设备）。</summary>
    Usb,

    /// <summary>Windows 驱动（打印机名）。</summary>
    Driver,
}
