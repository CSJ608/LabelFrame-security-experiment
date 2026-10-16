namespace LabelFrame.Bootstrapper.Printing;

/// <summary>打印机品牌预选：读 Windows 已装打印机驱动名，ZDesigner → zebra、TSC → tspl 预勾选；其余品牌从零勾选（扩表零契约变更；tspl 行 = 指令集维度，决策 #195）。</summary>
public static class PrinterBrandDetector
{
    private static readonly (string Keyword, string Brand)[] Rules =
    [
        ("zdesigner", "zebra"),
        ("tsc", "tspl"),
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
