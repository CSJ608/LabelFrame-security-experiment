using LabelFrame.Core.Transport.Plugins;

namespace LabelFrame.Core.Tests.Transport;

/// <summary>
/// 连接级标签纸尺寸键约定：键缺失 / 非整数 / 越界回退默认 40×20；宽高两维独立解析、边界值（10 / 500）接受。
/// </summary>
public class LabelMediaSizeTests
{
    [Fact]
    public void Resolve_without_params_should_fallback_to_defaults()
    {
        var size = LabelMediaSize.Resolve(new Dictionary<string, string>());

        Assert.Equal(LabelMediaSize.DefaultWidthMm, size.WidthMm);
        Assert.Equal(LabelMediaSize.DefaultHeightMm, size.HeightMm);
    }

    [Fact]
    public void Resolve_with_valid_params_should_parse_both_dimensions()
    {
        var size = LabelMediaSize.Resolve(new Dictionary<string, string>
        {
            [LabelMediaSize.WidthParameterKey] = "100",
            [LabelMediaSize.HeightParameterKey] = "80",
        });

        Assert.Equal(100, size.WidthMm);
        Assert.Equal(80, size.HeightMm);
    }

    [Fact]
    public void Resolve_should_parse_dimensions_independently()
    {
        // 宽有效 / 高缺失 → 宽取值、高回退默认
        var size = LabelMediaSize.Resolve(new Dictionary<string, string> { [LabelMediaSize.WidthParameterKey] = "70" });

        Assert.Equal(70, size.WidthMm);
        Assert.Equal(LabelMediaSize.DefaultHeightMm, size.HeightMm);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("-30")]
    [InlineData("501")]
    public void Resolve_with_invalid_value_should_fallback(string raw)
    {
        var size = LabelMediaSize.Resolve(new Dictionary<string, string> { [LabelMediaSize.WidthParameterKey] = raw });

        Assert.Equal(LabelMediaSize.DefaultWidthMm, size.WidthMm);
    }

    [Fact]
    public void Resolve_with_boundary_values_should_be_accepted()
    {
        var size = LabelMediaSize.Resolve(new Dictionary<string, string>
        {
            [LabelMediaSize.WidthParameterKey] = "10",
            [LabelMediaSize.HeightParameterKey] = "500",
        });

        Assert.Equal(10, size.WidthMm);
        Assert.Equal(500, size.HeightMm);
    }
}
