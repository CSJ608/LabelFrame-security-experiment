namespace LabelFrame.Bootstrapper.Printing;

/// <summary>打印机品牌预选（Issue ：本轮仅 Zebra——读 Windows 已装打印机驱动名，ZDesigner → zebra 预勾选；其余品牌从零勾选）。</summary>
/// <remarks>完整「驱动名关键词 → 品牌」映射表归专项 7/8；此处只含唯一规则，扩表零契约变更。</remarks>
public static class PrinterBrandDetector
{
    private static readonly (string Keyword, string Brand)[] Rules =
    [
        ("zdesigner", "zebra"),
    ];

    /// <summary>从已装打印机驱动名推断应预选的品牌（纯函数，测试矩阵锚点）；只预选 <paramref name="availableBrands"/> 中已有 manifest 条目的品牌。</summary>
    public static ISet<string> DetectPreselectedBrands(IEnumerable<string> installedPrinterNames, IEnumerable<string> availableBrands)
    {
        var names = installedPrinterNames.ToList();
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var brand in availableBrands)
        {
            foreach (var (keyword, brandId) in Rules)
            {
                if (string.Equals(brandId, brand, StringComparison.Ordinal)
                    && names.Any(name => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(brand);
                }
            }
        }

        return result;
    }
}
