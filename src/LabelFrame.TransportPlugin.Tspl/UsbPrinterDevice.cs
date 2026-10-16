using Microsoft.Win32;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// USB 免驱动直连设备发现：枚举注册表 usbprint 设备接口（Windows USB Printing Support 类驱动——
/// TSPL 兼容机普遍免厂商驱动，2026-10-16 TSC TT065-60 实测依据）。CreateFile 设备路径取自
/// DeviceClasses 接口键名（键名即符号链接：前缀 ##?# 对应 \\?\）。
/// </summary>
public static class UsbPrinterDeviceDiscovery
{
    /// <summary>USB 打印机设备接口 GUID（GUID_DEVINTERFACE_USBPRINT）。</summary>
    private const string DeviceClassesRoot = @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";

    /// <summary>发现的 USB 打印机设备。</summary>
    /// <param name="DisplayName">展示名（如 USB VID_1203&amp;PID_0273 (000001)，供用户指定）。</param>
    /// <param name="DevicePath">CreateFile 设备路径（\\?\usb#vid=…&amp;pid=…#serial#{guid}）。</param>
    public sealed record UsbPrinterDevice(string DisplayName, string DevicePath);

    /// <summary>
    /// 枚举全部 usbprint 设备接口（名称序稳定排序）。读注册表失败（权限 / 键不存在）时抛异常——
    /// 调用方按连接失败语义转中文提示。
    /// </summary>
    public static IReadOnlyList<UsbPrinterDevice> ListDevices()
    {
        using var root = Registry.LocalMachine.OpenSubKey(DeviceClassesRoot)
            ?? throw new InvalidOperationException("注册表无 usbprint 设备接口键（本机未连接任何 USB 打印机或系统异常）。");

        var devices = new List<UsbPrinterDevice>();
        foreach (var keyName in root.GetSubKeyNames()
                     .Where(name => name.StartsWith("##?#USB#", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            // 键名 = ##?#USB#VID_xxxx&PID_xxxx#serial#{guid}；CreateFile 路径 = 同名小写、前缀换 \\?\
            var instance = keyName[..keyName.LastIndexOf("#{", StringComparison.Ordinal)];
            var devicePath = @"\\" + instance[3..].ToLowerInvariant();
            devices.Add(new UsbPrinterDevice(instance, devicePath));
        }

        return devices;
    }

    /// <summary>
    /// 按用户指定名解析设备路径：精确 / 大小写不敏感包含匹配接口实例名（如 vid_1203 或完整键名）。
    /// 无匹配返回 null（调用方给可行动中文错误并列出可用设备）。
    /// </summary>
    public static string? ResolveDevicePath(string usbName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usbName);
        var devices = ListDevices();
        var match = devices.FirstOrDefault(d => d.DisplayName.Equals(usbName, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.DisplayName.Contains(usbName, StringComparison.OrdinalIgnoreCase)
                || d.DevicePath.Contains(usbName, StringComparison.OrdinalIgnoreCase));
        return match?.DevicePath;
    }

    /// <summary>自动发现第一台 usbprint 设备；无设备返回 null。</summary>
    public static string? DiscoverFirstDevicePath()
    {
        var devices = ListDevices();
        return devices.Count == 0 ? null : devices[0].DevicePath;
    }

    /// <summary>设备清单的中文可读列表（错误提示用，如「USB VID_1203&amp;PID_0273 (000001)」）。</summary>
    public static string DescribeDevices(IReadOnlyList<UsbPrinterDevice> devices)
        => devices.Count == 0
            ? "未发现任何 USB 打印机"
            : string.Join("、", devices.Select(d => d.DisplayName));
}
