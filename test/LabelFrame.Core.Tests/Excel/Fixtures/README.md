# 旧版 Excel 兼容样本

`templateframe-2.4.1.xlsx` 由独立 .NET 10 控制台引用 NuGet `TemplateFrame.Excel.Simple 2.4.1` 生成；作为固定输入随测试保留，避免用待测新版本生成数据掩盖跨版本差异。

SHA256：`E982DCA54361690FF6BADAA8D3C4E01E24B5A245E4BEE825B9E1D8235E39F312`。

生成代码（输出流由调用方创建与释放）：

```csharp
SimpleExcel.Write(output, new SimpleExcelTable {
    Headers = new[] { "编码", "数量", "日期", "备注" },
    Rows = new IReadOnlyList<object?>[] {
        new object?[] { "007", 3.5, new DateTime(2024, 2, 29, 12, 34, 56), "中文 & <标签>" },
        new object?[] { "A-02", -42, new DateTime(2026, 10, 24), null }
    }
}, new SimpleExcelOptions { SheetName = "打印数据" });
```
