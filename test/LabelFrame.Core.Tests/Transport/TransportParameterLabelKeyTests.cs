using System.Reflection;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.Core.Tests.Transport;

/// <summary>
/// 插件自描述文案键化防线（决策 #192，#366）：LabelKey / HintKey 为 init-only 属性而非构造参数——
/// 构造器签名零变化，旧插件二进制兼容；内置插件（log / tcp9100）spec 键完备且键名遵循
/// plugin.&lt;pluginId&gt;.&lt;paramKey&gt; 约定，展示端语言包按约定对齐。
/// </summary>
public class TransportParameterLabelKeyTests
{
    [Fact]
    public void Label_keys_should_default_null_for_legacy_construction()
    {
        var spec = new TransportParameterSpec("host", "打印机地址 / IP", TransportParameterType.String, true);

        Assert.Null(spec.LabelKey);
        Assert.Null(spec.HintKey);
        Assert.Null(new TransportParameterOption("Tcp", "TCP").LabelKey);
    }

    [Fact]
    public void Positional_constructors_should_keep_signature_count_unchanged()
    {
        // 键字段以 init 属性承接（二进制兼容），公开构造器必须仍只有主构造器 + 历史 7 参两个签名
        var ctors = typeof(TransportParameterSpec).GetConstructors();
        Assert.Equal(2, ctors.Length);
    }

    [Fact]
    public void Tcp9100_parameters_should_carry_label_keys_following_naming_convention()
    {
        var plugin = new Tcp9100TransportPlugin();

        foreach (var spec in plugin.Parameters)
        {
            Assert.Equal($"plugin.tcp9100.params.{spec.Key}.label", spec.LabelKey);
            Assert.Equal(spec.Hint is null ? null : $"plugin.tcp9100.params.{spec.Key}.hint", spec.HintKey);
        }
    }

    [Fact]
    public void Log_plugin_should_have_no_parameters()
    {
        Assert.Empty(new LogTransportPlugin().Parameters);
    }
}
