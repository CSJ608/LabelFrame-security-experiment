using TemplateFrame.Excel.Simple;

namespace LabelFrame.Core.Excel;

/// <summary>Excel 模板列：key（契约字段键）+ displayName（表头显示名）。</summary>
public sealed record ExcelTemplateColumn(string Key, string DisplayName);

/// <summary>Excel 模板生成：按契约字段生成「表头 + 示例行」的 .xlsx，供用户套用 Excel 导入做打印测试。</summary>
public static class ExcelTemplateWriter
{
    /// <summary>生成 Excel 模板字节。</summary>
    /// <param name="columns">列（字段键 + 显示名），顺序即表头顺序。</param>
    /// <param name="sampleRow">示例行（键 → 值，来自模板 testData；可为空 = 仅表头）。</param>
    /// <returns>.xlsx 字节数组。</returns>
    public static byte[] CreateTemplate(IReadOnlyList<ExcelTemplateColumn> columns, IReadOnlyDictionary<string, string>? sampleRow)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var table = new SimpleExcelTable
        {
            Headers = columns.Select(c => c.DisplayName).ToList(),
            Rows = BuildRows(columns, sampleRow),
        };

        using var stream = new MemoryStream();
        SimpleExcel.Write(stream, table, new SimpleExcelOptions { SheetName = "打印数据" });
        return stream.ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<object?>> BuildRows(
        IReadOnlyList<ExcelTemplateColumn> columns,
        IReadOnlyDictionary<string, string>? sampleRow)
    {
        if (sampleRow is null || sampleRow.Count == 0)
        {
            return [];
        }

        return
        [
            columns.Select(c => sampleRow.TryGetValue(c.Key, out var v) ? (object?)v : null).ToList(),
        ];
    }
}
