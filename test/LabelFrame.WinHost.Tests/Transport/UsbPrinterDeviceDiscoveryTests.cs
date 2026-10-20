using LabelFrame.TransportPlugin.Tspl;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// usbprint 接口符号链接解析回归（真机发现的路径缺陷：旧实现剥掉「#\」前缀段与接口 GUID 尾巴，
/// 生成不存在的设备路径导致 CreateFile 打不开）。路径形状取自 TT065-60 真机（CM 接口枚举返回形态）。
/// </summary>
public class UsbPrinterDeviceDiscoveryTests
{
    private const string GuidSuffix = "#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";

    [Fact]
    public void ParseDevicePath_should_keep_guid_suffix_and_prefix()
    {
        // 真机形态：\\?\USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000#{guid}
        var device = UsbPrinterDeviceDiscovery.ParseDevicePath($@"\\?\USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000{GuidSuffix}");

        Assert.NotNull(device);
        Assert.Equal(@"\\?\USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}", device.DevicePath);
        Assert.Equal("USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000", device.DisplayName);
    }

    [Fact]
    public void ParseDevicePath_should_accept_mixed_case_path()
    {
        // 符号链接大小写不敏感；前缀匹配大小写不敏感，其余保留原样
        var device = UsbPrinterDeviceDiscovery.ParseDevicePath($@"\\?\usb#vid_1203&pid_0273#serial{GuidSuffix}");

        Assert.NotNull(device);
        Assert.Equal($@"\\?\usb#vid_1203&pid_0273#serial{GuidSuffix}", device.DevicePath);
    }

    [Fact]
    public void ParseDevicePath_with_non_usb_interface_should_return_null()
    {
        Assert.Null(UsbPrinterDeviceDiscovery.ParseDevicePath($@"\\?\SWD#PRINTENUM{GuidSuffix}"));
        Assert.Null(UsbPrinterDeviceDiscovery.ParseDevicePath($@"\\?\PCI#VEN_8086&DEV_1234#3&11583659&0&C8{GuidSuffix}"));
    }

    [Fact]
    public void ParseDevicePath_without_guid_suffix_should_return_null()
    {
        // 无接口 GUID 尾巴的不是接口符号链接（实例路径形态），拒绝解析
        Assert.Null(UsbPrinterDeviceDiscovery.ParseDevicePath(@"\\?\USB#VID_1203&PID_0273#6&2b3c4d5e&0&0000"));
    }

    [Fact]
    public void DescribeDevices_should_list_display_names()
    {
        var devices = new[]
        {
            UsbPrinterDeviceDiscovery.ParseDevicePath($@"\\?\USB#VID_0A5F&PID_0168#DMJ1{GuidSuffix}")!,
            UsbPrinterDeviceDiscovery.ParseDevicePath($@"\\?\USB#VID_1203&PID_0273#0001{GuidSuffix}")!,
        };

        var description = UsbPrinterDeviceDiscovery.DescribeDevices(devices);

        Assert.Contains("VID_0A5F", description);
        Assert.Contains("VID_1203", description);
        Assert.Equal("未发现任何 USB 打印机", UsbPrinterDeviceDiscovery.DescribeDevices([]));
    }
}
