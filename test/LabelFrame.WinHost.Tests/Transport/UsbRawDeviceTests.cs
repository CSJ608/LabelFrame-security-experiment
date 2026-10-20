using LabelFrame.TransportPlugin.Tspl;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>UsbRawDevice 打开失败文案映射（Win32 错误号 → 中文病因）单测锚定。</summary>
public class UsbRawDeviceTests
{
    [Theory]
    [InlineData(2, "设备不存在")]
    [InlineData(5, "无访问权限")]
    [InlineData(32, "被其他程序占用")]
    public void DescribeOpenError_should_map_common_codes(int error, string part)
        => Assert.Contains(part, UsbRawDevice.DescribeOpenError(error));

    [Fact]
    public void DescribeOpenError_unknown_code_should_say_unknown()
        => Assert.Contains("未知", UsbRawDevice.DescribeOpenError(31));
}
