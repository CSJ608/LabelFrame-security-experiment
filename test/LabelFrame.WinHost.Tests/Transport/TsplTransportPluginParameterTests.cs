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

    [Fact]
    public void Media_size_specs_should_declare_optional_int_visible_always()
    {
        var parameters = new TsplTransportPlugin().Parameters;
        var width = Assert.Single(parameters, p => p.Key == LabelMediaSize.WidthParameterKey);
        var height = Assert.Single(parameters, p => p.Key == LabelMediaSize.HeightParameterKey);

        foreach (var spec in new[] { width, height })
        {
            Assert.Equal(TransportParameterType.Int, spec.Type);
            Assert.False(spec.Required);
            Assert.Null(spec.VisibleWhenKey); // 媒介尺寸与连接类型 / 打印方式无关，恒可见（测试打印始终是图片语义）
            Assert.Equal($"plugin.labelframe-transport-tspl.params.{spec.Key}.label", spec.LabelKey);
            Assert.Equal($"plugin.labelframe-transport-tspl.params.{spec.Key}.hint", spec.HintKey);
        }
    }

    [Fact]
    public void Row_groups_should_form_connection_mode_and_media_lines()
    {
        var rows = new TsplTransportPlugin().Parameters
            .GroupBy(p => p.Row)
            .Select(g => g.Select(p => p.Key).ToList())
            .ToList();

        // 行层级：连接类型｜连接参数（host+port / usbName 按 kind 二选一可见）｜打印方式＋反色｜媒介宽高
        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { "kind" }, rows[0]);
        Assert.Equal(new[] { "host", "port", "usbName" }, rows[1]);
        Assert.Equal(new[] { TransportPrintMode.ParameterKey, LabelImageEncodeOptions.InvertBitsParameterKey }, rows[2]);
        Assert.Equal(new[] { LabelMediaSize.WidthParameterKey, LabelMediaSize.HeightParameterKey }, rows[3]);
    }
}
