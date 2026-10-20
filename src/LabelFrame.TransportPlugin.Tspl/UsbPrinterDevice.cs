using System.Runtime.InteropServices;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// USB 免驱动直连设备发现：CM_Get_Device_Interface_List 枚举现役 usbprint 设备接口（Windows USB
/// Printing Support 类驱动，TSPL 兼容机普遍免厂商驱动）。必须带 PRESENT 标志——注册表 DeviceClasses
/// 键含已拔出设备的陈旧接口，裸枚举会拿到 CreateFile 打不开的路径。
/// </summary>
public static class UsbPrinterDeviceDiscovery
{
    /// <summary>USB 打印机设备接口 GUID（GUID_DEVINTERFACE_USBPRINT）。</summary>
    private static readonly Guid UsbPrintInterfaceGuid = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");

    /// <summary>CM_GET_DEVICE_INTERFACE_LIST_PRESENT：只列当前在位设备的接口。</summary>
    private const uint CmListPresent = 0x00000001;

    private const uint CrSuccess = 0;

    /// <summary>发现的 USB 打印机设备。</summary>
    /// <param name="DisplayName">展示名（如 USB#VID_1203&amp;PID_0273#serial，供用户指定）。</param>
    /// <param name="DevicePath">CreateFile 设备路径（\\?\USB#VID_…&amp;PID_…#serial#{guid}）。</param>
    public sealed record UsbPrinterDevice(string DisplayName, string DevicePath);

    /// <summary>
    /// 枚举全部在位 usbprint 设备接口（名称序稳定排序——探测顺序确定）。CM 调用失败时抛异常，
    /// 调用方按连接失败语义转中文提示。
    /// </summary>
    public static IReadOnlyList<UsbPrinterDevice> ListDevices()
    {
        var cr = Cm_Get_Device_Interface_List_Size(out var bufferLength, in UsbPrintInterfaceGuid, null, CmListPresent);
        if (cr != CrSuccess)
        {
            throw new InvalidOperationException($"CM_Get_Device_Interface_List_Size 失败（错误码 {cr}）。");
        }

        var buffer = new char[bufferLength];
        cr = Cm_Get_Device_Interface_List(in UsbPrintInterfaceGuid, null, buffer, bufferLength, CmListPresent);
        if (cr != CrSuccess)
        {
            throw new InvalidOperationException($"CM_Get_Device_Interface_List 失败（错误码 {cr}）。");
        }

        var devices = new List<UsbPrinterDevice>();
        var index = 0;
        while (index < buffer.Length && buffer[index] != '\0')
        {
            var length = Array.IndexOf(buffer, '\0', index) - index;
            if (ParseDevicePath(new string(buffer, index, length)) is { } device)
            {
                devices.Add(device);
            }

            index += length + 1;
        }

        return devices.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 接口符号链接 → 设备（纯函数，单测锚定）：路径 <c>\\?\USB#VID_xxxx&amp;PID_xxxx#serial#{guid}</c>，
    /// **接口 GUID 尾巴必须保留**（剥掉就不是接口路径，CreateFile 打不开）；展示名取去前缀与 GUID
    /// 尾巴的实例段。非 USB 接口路径返回 null。
    /// </summary>
    internal static UsbPrinterDevice? ParseDevicePath(string devicePath)
    {
        const string Prefix = @"\\?\USB#";
        if (!devicePath.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffixIndex = devicePath.LastIndexOf("#{", StringComparison.Ordinal);
        if (suffixIndex < 0)
        {
            return null;
        }

        var displayName = devicePath[4..suffixIndex]; // USB#VID_x&PID_y#serial
        return new UsbPrinterDevice(displayName, devicePath); // CM 返回的即 CreateFile 路径，原样保留
    }

    /// <summary>设备清单的中文可读列表（错误提示用，如「USB#VID_1203&amp;PID_0273#serial」）。</summary>
    public static string DescribeDevices(IReadOnlyList<UsbPrinterDevice> devices)
        => devices.Count == 0
            ? "未发现任何 USB 打印机"
            : string.Join("、", devices.Select(d => d.DisplayName));

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW", CharSet = CharSet.Unicode)]
    private static extern uint Cm_Get_Device_Interface_List_Size(out uint bufferLength, in Guid interfaceClassGuid, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW", CharSet = CharSet.Unicode)]
    private static extern uint Cm_Get_Device_Interface_List(in Guid interfaceClassGuid, string? deviceId, char[] buffer, uint bufferLength, uint flags);
}
