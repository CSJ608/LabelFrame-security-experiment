using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;

namespace LabelFrame.Core.Tests.Transport;

/// <summary>传输插件 id 策略（DESIGN）：官方前缀 / 保留 id / 别名归一。</summary>
public class TransportPluginIdPolicyTests
{
    [Theory]
    [InlineData("labelframe-transport-zebra", true)]
    [InlineData("labelframe-transport-tscl", true)]
    [InlineData("Labelframe-Other", true)] // 前缀忽略大小写
    [InlineData("zebra", false)]
    [InlineData("sample", false)]
    [InlineData("thirdparty-plugin", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOfficial_should_match_labelframe_prefix(string? pluginId, bool expected)
        => Assert.Equal(expected, TransportPluginIdPolicy.IsOfficial(pluginId!));

    [Theory]
    [InlineData("log", true)]
    [InlineData("tcp9100", true)]
    [InlineData("winspool", true)]
    [InlineData("LOG", true)] // 忽略大小写（注册表语义一致）
    [InlineData("zebra", false)]  // 起不再是内置 id（已外置为官方插件）
    [InlineData("labelframe-transport-zebra", false)] // 官方插件 id 不属保留名单（放行）
    [InlineData("sample", false)]
    public void IsReservedBuiltin_should_cover_builtin_transport_ids(string pluginId, bool expected)
        => Assert.Equal(expected, TransportPluginIdPolicy.IsReservedBuiltin(pluginId));

    [Fact]
    public void NormalizeAlias_should_map_legacy_zebra_to_official_id()
    {
        Assert.Equal(TransportPluginIdPolicy.ZebraPluginId, TransportPluginIdPolicy.NormalizeAlias("zebra"));
        Assert.Equal(TransportPluginIdPolicy.ZebraPluginId, TransportPluginIdPolicy.NormalizeAlias(" Zebra "));
        Assert.Equal(TransportPluginIdPolicy.ZebraPluginId, TransportPluginIdPolicy.NormalizeAlias(TransportPluginIdPolicy.ZebraPluginId));
        Assert.Equal("sample", TransportPluginIdPolicy.NormalizeAlias("sample"));
    }

    [Fact]
    public void BrandPluginIds_should_map_zebra_brand()
        => Assert.Equal(TransportPluginIdPolicy.ZebraPluginId, TransportPluginIdPolicy.BrandPluginIds["zebra"]);
}

/// <summary>插件版本比较（官方插件覆盖安装版本比较语义）。</summary>
public class PluginVersionComparerTests
{
    [Theory]
    [InlineData("0.27.0", "0.26.0", 1)]   // 新版本
    [InlineData("0.26.0", "0.27.0", -1)]  // 旧版本（降级）
    [InlineData("0.27.0", "0.27.0", 0)]   // 同版本
    [InlineData("1.0", "1.0.0", 0)]       // System.Version 语义：1.0 == 1.0.0
    [InlineData("0.27.10", "0.27.9", 1)]  // 数值段比较（非字符串序）
    [InlineData("10.0.0", "9.0.0", 1)]
    public void Compare_should_use_version_semantics(string left, string right, int expectedSign)
    {
        var result = PluginVersionComparer.Compare(left, right);
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Theory]
    [InlineData("beta", "alpha", 1)]      // 非版本号回退字符串 Ordinal
    [InlineData("v1.0.x", "v1.0.a", 1)]
    [InlineData("same", "same", 0)]
    public void Compare_non_version_strings_should_fall_back_to_ordinal(string left, string right, int expectedSign)
        => Assert.Equal(expectedSign, Math.Sign(PluginVersionComparer.Compare(left, right)));

    [Fact]
    public void Compare_null_or_empty_should_throw()
    {
        Assert.Throws<ArgumentException>(() => PluginVersionComparer.Compare("", "1.0.0"));
        Assert.Throws<ArgumentException>(() => PluginVersionComparer.Compare("1.0.0", " "));
    }
}
