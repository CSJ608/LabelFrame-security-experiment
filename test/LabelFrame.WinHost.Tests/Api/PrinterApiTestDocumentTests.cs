using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Api;

namespace LabelFrame.WinHost.Tests.Api;

/// <summary>
/// 测试页文档随连接媒介尺寸渲染：默认 40×20 与历史版面完全一致（边距 2 / 纵坐标 4 / 字高 3）；
/// 大纸时版面随纸、字高按纸高 3..8mm 缩放（100×80 → 字高 8）。
/// </summary>
public class PrinterApiTestDocumentTests
{
    [Fact]
    public void BuildTestDocument_with_default_media_should_match_legacy_layout()
    {
        var document = PrinterApi.BuildTestDocument((LabelMediaSize.DefaultWidthMm, LabelMediaSize.DefaultHeightMm));

        Assert.Equal(40, document.Layout.WidthMm);
        Assert.Equal(20, document.Layout.HeightMm);
        var text = Assert.IsType<LabelTextElement>(Assert.Single(document.Layout.Elements));
        Assert.Equal("LabelFrame Test", text.Literal);
        Assert.Equal(2, text.XMm);
        Assert.Equal(4, text.YMm);
        Assert.Equal(3, text.FontHeightMm);
        Assert.Equal(3, text.FontWidthMm);
        Assert.Equal(36, text.WidthMm);
        Assert.Equal(LabelTextAlign.Center, text.TextAlign);
    }

    [Fact]
    public void BuildTestDocument_with_large_media_should_scale_page_and_font()
    {
        var document = PrinterApi.BuildTestDocument((100, 80));

        Assert.Equal(100, document.Layout.WidthMm);
        Assert.Equal(80, document.Layout.HeightMm);
        var text = Assert.IsType<LabelTextElement>(Assert.Single(document.Layout.Elements));
        Assert.Equal(8, text.FontHeightMm); // 80 ÷ 10，钳位上限
        Assert.Equal(8, text.FontWidthMm);
        Assert.Equal(96, text.WidthMm); // 100 − 2×2
    }
}
