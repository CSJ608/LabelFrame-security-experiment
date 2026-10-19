using LabelFrame.TransportPlugin.Tspl;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// usbprint 注册表接口键名解析回归（真机发现的路径缺陷：旧实现剥掉「#\」前缀段与接口 GUID 尾巴，
/// 生成不存在的设备路径导致 CreateFile 打不开）。键名形状取自 TT065-60 真机注册表。
/// </summary>
public class UsbPrinterDeviceDiscoveryTests
{
    private const string GuidSuffix = "#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";

    [Fact]
    public void ParseDeviceKey_should_keep_guid_suffix_and_backslash_prefix()
    {
        // 真机键名形状：##?#USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000#{guid}
        var device = UsbPrinterDeviceDiscovery.ParseDeviceKey($"##?#USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000{GuidSuffix}");

        Assert.NotNull(device);
        Assert.Equal(@"\\?\USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}", device.DevicePath);
        Assert.Equal("USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000", device.DisplayName);
    }

    [Fact]
    public void ParseDeviceKey_should_accept_mixed_case_key()
    {
        // 注册表键名大小写不定；前缀匹配大小写不敏感，其余保留原样（符号链接大小写不敏感）
        var device = UsbPrinterDeviceDiscovery.ParseDeviceKey($"##?#usb#vid_1203&pid_0273#serial{GuidSuffix}");

        Assert.NotNull(device);
        Assert.Equal(@"\\?\usb#vid_1203&pid_0273#serial#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}", device.DevicePath);
    }

    [Fact]
    public void ParseDeviceKey_with_non_usb_interface_should_return_null()
    {
        Assert.Null(UsbPrinterDeviceDiscovery.ParseDeviceKey($"##?#SWD#PRINTENUM{GuidSuffix}"));
        Assert.Null(UsbPrinterDeviceDiscovery.ParseDeviceKey($"##?#PCI#VEN_8086&DEV_1234#3&11583659&0&C8{GuidSuffix}"));
    }

    [Fact]
    public void ParseDeviceKey_without_guid_suffix_should_return_null()
    {
        // 无接口 GUID 尾巴的不是接口符号链接（实例键形态），拒绝解析
        Assert.Null(UsbPrinterDeviceDiscovery.ParseDeviceKey("##?#USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000"));
    }
}
