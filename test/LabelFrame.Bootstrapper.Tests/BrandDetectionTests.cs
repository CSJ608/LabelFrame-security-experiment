using LabelFrame.Bootstrapper.Printing;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>品牌预选（Issue 决议 2：仅 Zebra——ZDesigner 驱动名 → zebra 预勾选；其余品牌从零勾选）。</summary>
public sealed class BrandDetectionTests
{
    [Fact]
    public void Detect_preselected_brands_zdesigner_driver_should_preselect_zebra()
    {
        var brands = PrinterBrandDetector.DetectPreselectedBrands(
            ["ZDesigner ZD421-203dpi ZPL", "Microsoft Print to PDF"],
            ["zebra"]);

        Assert.True(brands.SetEquals(["zebra"]));
    }

    [Fact]
    public void Detect_preselected_brands_without_zebra_driver_should_select_nothing()
    {
        var brands = PrinterBrandDetector.DetectPreselectedBrands(
            ["HP LaserJet M404", "Microsoft Print to PDF"],
            ["zebra"]);

        Assert.Empty(brands);
    }

    [Fact]
    public void Detect_preselected_brands_without_manifest_entry_should_select_nothing()
    {
        // 决议 2 边界：预选只对「清单已有条目」的品牌生效（映射语义 7/8 完整化）
        var brands = PrinterBrandDetector.DetectPreselectedBrands(
            ["ZDesigner ZD421-203dpi ZPL"],
            []);

        Assert.Empty(brands);
    }

    [Fact]
    public void Detect_preselected_brands_other_brands_should_start_unchecked()
    {
        // 本轮唯一规则是 ZDesigner → zebra：其他品牌即使本机有同名驱动也不预选
        var brands = PrinterBrandDetector.DetectPreselectedBrands(
            ["ZDesigner ZD421-203dpi ZPL", "Generic / Text Only"],
            ["zebra", "honeywell", "tsc"]);

        Assert.True(brands.SetEquals(["zebra"]));
    }

    [Fact]
    public void Detect_preselected_brands_matching_should_be_case_insensitive()
    {
        var brands = PrinterBrandDetector.DetectPreselectedBrands(
            ["zdesigner zd421"],
            ["zebra"]);

        Assert.True(brands.SetEquals(["zebra"]));
    }
}
