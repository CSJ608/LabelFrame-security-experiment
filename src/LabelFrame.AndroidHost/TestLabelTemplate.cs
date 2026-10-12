using System.Globalization;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.AndroidHost;

/// <summary>内置测试标签（60×40mm）：标题 + 时间 + 编码文本 + Code128 条码，
/// 「测试打印」走完整真实链路（校验 → 渲染 → ^GF → 发送 → 终态）验证打印功能。</summary>
public static class TestLabelTemplate
{
    /// <summary>测试契约。</summary>
    public static LabelContract Contract { get; } = new()
    {
        Name = "host-self-test",
        Version = "1",
        Fields =
        [
            new LabelField { Key = "time", DisplayName = "时间", IsRequired = true },
            new LabelField { Key = "code", DisplayName = "编码", IsRequired = true },
        ],
    };

    /// <summary>测试版式。</summary>
    public static LabelLayout Layout { get; } = new()
    {
        Name = "host-self-test",
        ContractName = "host-self-test",
        ContractVersion = "1",
        WidthMm = 60,
        HeightMm = 40,
        Elements =
        [
            new LabelTextElement { XMm = 3, YMm = 2, Literal = "LabelFrame 测试", FontHeightMm = 3, Bold = true },
            new LabelTextElement { XMm = 3, YMm = 8, SourceKey = "time", FontHeightMm = 2.5 },
            new LabelTextElement { XMm = 3, YMm = 13, SourceKey = "code", FontHeightMm = 3, Bold = true },
            new LabelBarcodeElement { XMm = 3, YMm = 20, SourceKey = "code", HeightMm = 12, ModuleWidth = 2, DisplayValue = true },
        ],
    };

    /// <summary>生成一份测试数据（时间与编码取当前时刻）。</summary>
    public static IReadOnlyDictionary<string, string> SampleData() => new Dictionary<string, string>
    {
        ["time"] = DateTime.Now.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        ["code"] = $"LF-TEST-{DateTime.Now:HHmmss}",
    };
}
