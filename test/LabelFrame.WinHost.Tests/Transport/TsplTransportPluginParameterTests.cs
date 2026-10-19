using LabelFrame.Core.Transport.Plugins;
using LabelFrame.TransportPlugin.Tspl;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>TSPL 插件「反色处理」参数声明测试。</summary>
public class TsplTransportPluginParameterTests
{
    [Fact]
    public void Invert_image_spec_should_declare_bool_default_off_and_image_only_visibility()
    {
        var spec = Assert.Single(new TsplTransportPlugin().Parameters, p => p.Key == LabelImageEncodeOptions.InvertBitsParameterKey);

        Assert.Equal(TransportParameterType.Bool, spec.Type);
        Assert.False(spec.Required);
        Assert.Equal("false", spec.DefaultValue);
        Assert.Equal(TransportPrintMode.ParameterKey, spec.VisibleWhenKey);
        Assert.Equal(new[] { TransportPrintMode.Image }, spec.VisibleWhenValues);
        Assert.True(spec.IsVisible(new Dictionary<string, string> { [TransportPrintMode.ParameterKey] = TransportPrintMode.Image }));
        Assert.False(spec.IsVisible(new Dictionary<string, string> { [TransportPrintMode.ParameterKey] = TransportPrintMode.Native }));
        Assert.Equal("plugin.labelframe-transport-tspl.params.invertImage.label", spec.LabelKey);
        Assert.Equal("plugin.labelframe-transport-tspl.params.invertImage.hint", spec.HintKey);
    }
}
