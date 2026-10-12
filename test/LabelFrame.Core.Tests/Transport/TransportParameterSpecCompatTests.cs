using System.Reflection;
using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.Core.Tests.Transport;

/// <summary>
/// 参数规格二进制兼容防线：外部插件 DLL 按历史构造器签名编译（可选参数在调用方编译期展开），
/// 宿主升级而插件未随发（服务端分发窗口期）时仍须可加载——历史签名构造器被移除会在装配期
/// MissingMethodException 崩溃宿主。此断言锚定旧签名必须持续存在，防止重构时误删。
/// </summary>
public class TransportParameterSpecCompatTests
{
    [Fact]
    public void Legacy_seven_parameter_constructor_should_remain_for_old_plugin_binaries()
    {
        var ctor = typeof(TransportParameterSpec).GetConstructor(new[]
        {
            typeof(string),
            typeof(string),
            typeof(TransportParameterType),
            typeof(bool),
            typeof(string),
            typeof(IReadOnlyList<TransportParameterOption>),
            typeof(string),
        });

        Assert.NotNull(ctor);

        // 经旧签名构造的规格行为等价：无 VisibleWhen 声明 = 恒可见
        var spec = (TransportParameterSpec)ctor!.Invoke([
            "host", "主机地址", TransportParameterType.String, true, null, null, null]);
        Assert.Null(spec.VisibleWhenKey);
        Assert.True(spec.IsVisible(new Dictionary<string, string>()));
    }
}
