using System.Text.Json;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Api;

/// <summary>
/// 插件自描述文案键化 API 契约防线（决策 #192，#366）：labelKey / hintKey 随 DTO 透传；
/// 无键插件（第三方 / 旧版官方）序列化后不出现新字段——响应与键化前逐字节等价。
/// </summary>
public class TransportPluginLabelKeyDtoTests
{
    [Fact]
    public void Descriptor_mapping_should_pass_through_label_keys()
    {
        var spec = new TransportParameterSpec("kind", "连接类型", TransportParameterType.Select, true,
            Options: [new TransportParameterOption("Usb", "USB（自动发现）") { LabelKey = "p.params.kind.opt.Usb" }])
        {
            LabelKey = "p.params.kind.label",
            HintKey = "p.params.kind.hint",
        };
        var descriptor = new TransportPluginDescriptor("demo", "Demo", "演示", [spec], IsExternal: true);

        var dto = TransportApi.ToTransportPluginDescriptorDto(descriptor);

        var param = Assert.Single(dto.Parameters);
        Assert.Equal("p.params.kind.label", param.LabelKey);
        Assert.Equal("p.params.kind.hint", param.HintKey);
        var option = Assert.Single(param.Options!);
        Assert.Equal("p.params.kind.opt.Usb", option.LabelKey);
    }

    [Fact]
    public void Keyless_parameters_should_serialize_without_key_fields()
    {
        var spec = new TransportParameterSpec("host", "打印机地址 / IP", TransportParameterType.String, true, Hint: "如 192.168.1.50");

        var json = JsonSerializer.Serialize(new TransportPluginParameterDto(
            spec.Key, spec.Label, spec.Type.ToString(), spec.Required, spec.DefaultValue,
            spec.Options?.Select(o => new TransportParameterOptionDto(o.Value, o.Label)).ToList(),
            spec.Hint, spec.VisibleWhenKey, spec.VisibleWhenValues, spec.LabelKey, spec.HintKey));

        Assert.DoesNotContain("labelKey", json);
        Assert.DoesNotContain("hintKey", json);
    }

    [Fact]
    public void Winspool_parameters_should_carry_label_keys()
    {
        var descriptor = new TransportPluginDescriptor(
            "winspool", "Windows 驱动", "desc", new WinspoolTransportPlugin().Parameters);

        var dto = TransportApi.ToTransportPluginDescriptorDto(descriptor);

        var param = Assert.Single(dto.Parameters);
        Assert.Equal("plugin.winspool.params.printerName.label", param.LabelKey);
        Assert.Equal("plugin.winspool.params.printerName.hint", param.HintKey);
    }
}
