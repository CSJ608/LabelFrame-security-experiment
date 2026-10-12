using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LabelFrame.Core.Excel;

namespace LabelFrame.Core.Tests.Excel;

/// <summary>
/// Excel 读取回归测试（防上游 TemplateFrame.Excel.Simple 回退）：
/// 续填行识别——模板生成方在数据区写命名区域、用户在命名区域下方续填的行不得丢失；
/// 非 A 列起始表头——第三方表格表头不在 A 列起始时前几列恒空、末列静默丢弃的缺陷不得复现。
/// 两场景均以 inline string 构造单元格（Excel 手工输入形态，亦为 SimpleExcel 写出形态）。
/// </summary>
public class ExcelTableReaderRegressionTests
{
    [Fact]
    public void ReadTextTable_should_recognize_user_continuation_rows_below_named_region()
    {
        var columns = new[]
        {
            new ExcelTemplateColumn("locationCode", "库位码"),
            new ExcelTemplateColumn("zone", "区域"),
        };
        var sampleRow = new Dictionary<string, string> { ["locationCode"] = "A-01-02-03", ["zone"] = "A-01" };
        var bytes = ExcelTemplateWriter.CreateTemplate(columns, sampleRow);

        // 可扩容 MemoryStream：续填后包体积增长，非扩容流（new MemoryStream(byte[])）会在保存时抛异常
        using var stream = new MemoryStream();
        stream.Write(bytes, 0, bytes.Length);
        using (var doc = SpreadsheetDocument.Open(stream, true))
        {
            // 场景前提：模板生成方写入的命名区域只覆盖表头 + 示例行（$A$1:$B$2）
            var definedNames = doc.WorkbookPart!.Workbook.GetFirstChild<DefinedNames>();
            Assert.Contains(definedNames!.Elements<DefinedName>(), d => d.InnerText == "'打印数据'!$A$1:$B$2");

            // 模拟用户在命名区域下方续填 2 行（inline string，Excel 手工输入形态）——不更新命名区域
            var sheetData = doc.WorkbookPart.WorksheetParts.First().Worksheet.GetFirstChild<SheetData>()!;
            AppendRow(sheetData, 3, "A-02-01-01", "A-02");
            AppendRow(sheetData, 4, "A-03-01-01", "A-03");
        }

        stream.Position = 0;
        var table = ExcelTableReader.ReadTextTable(stream);

        Assert.Equal(new[] { "库位码", "区域" }, table.Headers);

        // 全部数据行（示例行 + 2 续填行）识别、值正确
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(new[] { "A-01-02-03", "A-01" }, table.Rows[0]);
        Assert.Equal(new[] { "A-02-01-01", "A-02" }, table.Rows[1]);
        Assert.Equal(new[] { "A-03-01-01", "A-03" }, table.Rows[2]);
    }

    [Fact]
    public void ReadTextTable_should_read_third_party_table_with_header_not_starting_at_column_a()
    {
        // 第三方表格：表头在 C/D 列（A / B 列空），2 行数据
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            sheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(sheetPart), SheetId = 1, Name = "Sheet1" });

            AppendCell(sheetData, 1, "C", "库位码");
            AppendCell(sheetData, 1, "D", "区域");
            AppendCell(sheetData, 2, "C", "B-05-02-01");
            AppendCell(sheetData, 2, "D", "B-05");
            AppendCell(sheetData, 3, "C", "B-06-01-02");
            AppendCell(sheetData, 3, "D", "B-06");

            sheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }

        stream.Position = 0;

        // 原始层（SimpleExcel.Read）：表头与列完整，无前导空列、无末列丢失
        var raw = TemplateFrame.Excel.Simple.SimpleExcel.Read(stream);
        Assert.Equal(new[] { "库位码", "区域" }, raw.Headers!);
        Assert.Equal(2, raw.Rows.Count);
        Assert.Equal(new[] { "B-05-02-01", "B-05" }, raw.Rows[0].Select(c => c?.ToString()).ToArray());
        Assert.Equal(new[] { "B-06-01-02", "B-06" }, raw.Rows[1].Select(c => c?.ToString()).ToArray());

        // 产品层（ExcelTableReader，导入端点消费的入口）
        stream.Position = 0;
        var table = ExcelTableReader.ReadTextTable(stream);
        Assert.Equal(new[] { "库位码", "区域" }, table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new[] { "B-05-02-01", "B-05" }, table.Rows[0]);
        Assert.Equal(new[] { "B-06-01-02", "B-06" }, table.Rows[1]);
    }

    /// <summary>在指定行号追加一行 inline string 单元格（从 A 列起连续填充）。</summary>
    private static void AppendRow(SheetData sheetData, uint rowIndex, params string[] values)
    {
        var row = new Row { RowIndex = rowIndex };
        for (var i = 0; i < values.Length; i++)
        {
            AppendCell(row, $"{(char)('A' + i)}{rowIndex}", values[i]);
        }
        sheetData.Append(row);
    }

    /// <summary>在指定行号的行内（不存在则追加）写入指定列的 inline string 单元格。</summary>
    private static void AppendCell(SheetData sheetData, uint rowIndex, string columnLetters, string text)
    {
        var row = sheetData.Elements<Row>().LastOrDefault(r => r.RowIndex?.Value == rowIndex);
        if (row is null)
        {
            row = new Row { RowIndex = rowIndex };
            sheetData.Append(row);
        }
        AppendCell(row, $"{columnLetters}{rowIndex}", text);
    }

    private static void AppendCell(Row row, string cellReference, string text) => row.Append(new Cell
    {
        CellReference = cellReference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(text)),
    });
}
