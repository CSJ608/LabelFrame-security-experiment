using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LabelFrame.Api.Tests;

/// <summary>模板端点 HTTP 集成：CRUD / 导入导出 / 预览（错误码透传宿主前缀、DPI 取宿主配置）。</summary>
public class TemplateEndpointTests : SharedEndpointsTestBase
{
    [Fact]
    public async Task Save_with_missing_fields_should_return_400_with_host_code()
    {
        var client = await CreateClientAsync();
        var response = await client.PostAsync("/api/templates", new StringContent("""{ "name": "" }""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadErrorAsync(response);
        Assert.Equal("LF_API_001", error.GetProperty("code").GetString());
        Assert.Contains("缺少模板", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Template_lifecycle_should_save_list_get_delete()
    {
        var client = await CreateClientAsync();
        var save = await client.PostAsync("/api/templates", new StringContent(TemplateJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/templates");
        Assert.Equal(1, list.GetArrayLength());

        var detail = await client.GetFromJsonAsync<JsonElement>("/api/templates/集成测试模板");
        Assert.Equal("it-label", detail.GetProperty("contract").GetProperty("name").GetString());
        Assert.Equal("IT-001", detail.GetProperty("testData").GetProperty("code").GetString());

        // 模板不存在：404 + 宿主自定义码
        var missing = await client.GetAsync("/api/templates/不存在");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("LF_TPL_001", (await ReadErrorAsync(missing)).GetProperty("code").GetString());

        // 导出：zip（PK 头）
        var export = await client.GetAsync("/api/templates/集成测试模板/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);

        // 删除后 404
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/templates/集成测试模板")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/templates/集成测试模板")).StatusCode);
    }

    [Fact]
    public async Task Preview_should_render_at_host_dpi_and_fall_back_to_testData()
    {
        // 回归锚点：预览 DPI 必须取宿主配置（203 与 300 两档像素宽应不同）
        var dpi203 = await CreateClientAsync(dpi: 203);
        var dpi300 = await CreateClientAsync(dpi: 300);
        await dpi203.PostAsync("/api/templates", new StringContent(TemplateJson, Encoding.UTF8, "application/json"));
        await dpi300.PostAsync("/api/templates", new StringContent(TemplateJson, Encoding.UTF8, "application/json"));

        var png203 = await (await dpi203.PostAsync("/api/templates/集成测试模板/preview", new StringContent("{}", Encoding.UTF8, "application/json"))).Content.ReadAsByteArrayAsync();
        var png300 = await (await dpi300.PostAsync("/api/templates/集成测试模板/preview", new StringContent("{}", Encoding.UTF8, "application/json"))).Content.ReadAsByteArrayAsync();

        // 100mm 宽 @203dpi ≈ 799px、@300dpi ≈ 1181px（testData 回退使文本元素有内容可渲染）
        Assert.InRange(PngWidth(png203), 795, 805);
        Assert.InRange(PngWidth(png300), 1175, 1187);
    }

    [Fact]
    public async Task Preview_missing_template_should_return_404()
    {
        var client = await CreateClientAsync();
        var response = await client.PostAsync("/api/templates/不存在/preview", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>调试出图端点 HTTP 集成：自包含模板渲染、校验、base64 图片非法 400。</summary>
public class RenderEndpointTests : SharedEndpointsTestBase
{
    private static readonly string RenderJson = $$"""
        {
          "template": {
            "contract": { "name": "it", "version": "1.0", "fields": [] },
            "layout": {
              "name": "l", "contractName": "it", "contractVersion": "1.0",
              "widthMm": 40, "heightMm": 20,
              "elements": [ { "type": "text", "literal": "X", "xMm": 1, "yMm": 1, "fontHeightMm": 3 } ]
            }
          },
          "labels": [ { "data": {} } ]
        }
        """;

    [Fact]
    public async Task Render_image_should_return_png()
    {
        var client = await CreateClientAsync();
        var response = await client.PostAsync("/api/print/render-image", new StringContent(RenderJson, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 8);
        Assert.Equal((byte)0x89, bytes[0]); // PNG 魔数
    }

    [Fact]
    public async Task Render_image_without_labels_should_return_400()
    {
        var client = await CreateClientAsync();
        var response = await client.PostAsync("/api/print/render-image", new StringContent("""{ "template": null }""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Render_image_with_invalid_base64_should_return_400()
    {
        // 回归锚点：base64 非法应 400 + 中文原因（而非 500）
        var withImages = $$"""
        {
          "template": {
            "contract": { "name": "it", "version": "1.0", "fields": [] },
            "layout": {
              "name": "l", "contractName": "it", "contractVersion": "1.0",
              "widthMm": 40, "heightMm": 20,
              "elements": [ { "type": "text", "literal": "X", "xMm": 1, "yMm": 1, "fontHeightMm": 3 } ]
            },
            "images": { "logo": "!!!not-base64!!!" }
          },
          "labels": [ { "data": {} } ]
        }
        """;
        var client = await CreateClientAsync();
        var response = await client.PostAsync("/api/print/render-image", new StringContent(withImages, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadErrorAsync(response);
        Assert.Contains("base64", error.GetProperty("message").GetString());
    }
}

/// <summary>Excel 端点 HTTP 集成：模板生成 → 上传解析回环（真实 xlsx 读写）。</summary>
public class ImportEndpointTests : SharedEndpointsTestBase
{
    [Theory]
    [InlineData(100_000, HttpStatusCode.OK)]
    [InlineData(100_001, HttpStatusCode.BadRequest)]
    public async Task Excel_row_budget_should_enforce_boundary_and_allow_next_valid_import(int dataRows, HttpStatusCode expectedStatus)
    {
        var client = await CreateClientAsync();
        using var oversized = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(oversized, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            WriteEntry(zip, "[Content_Types].xml", """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            WriteEntry(zip, "_rels/.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            WriteEntry(zip, "xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="数据" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            using var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());
            writer.Write("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>编码</t></is></c></row>""");
            for (var row = 2; row <= dataRows + 1; row++)
                writer.Write($"<row r=\"{row}\"><c r=\"A{row}\"><v>7</v></c></row>");
            writer.Write("</sheetData></worksheet>");
        }

        using var invalidForm = new MultipartFormDataContent();
        invalidForm.Add(new ByteArrayContent(oversized.ToArray()), "file", "oversized.xlsx");
        using var rejected = await client.PostAsync("/api/import/excel", invalidForm);
        Assert.Equal(expectedStatus, rejected.StatusCode);
        var result = await ReadErrorAsync(rejected);
        if (expectedStatus == HttpStatusCode.BadRequest)
        {
            Assert.Equal("LF_API_001", result.GetProperty("code").GetString());
            Assert.Contains("MaxTableRows", result.GetProperty("params").GetProperty("detail").GetString());
            Assert.False(result.TryGetProperty("rows", out _));
        }
        else
        {
            var rows = result.GetProperty("rows");
            Assert.Equal(dataRows, rows.GetArrayLength());
            Assert.Equal("7", rows[0][0].GetString());
            Assert.Equal("7", rows[dataRows - 1][0].GetString());
        }

        var valid = LabelFrame.Core.Excel.ExcelTemplateWriter.CreateTemplate(
            [new("code", "编码")], new Dictionary<string, string> { ["code"] = "007" });
        using var validForm = new MultipartFormDataContent();
        validForm.Add(new ByteArrayContent(valid), "file", "valid.xlsx");
        using var accepted = await client.PostAsync("/api/import/excel", validForm);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var table = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("007", table.GetProperty("rows")[0][0].GetString());
        Assert.Equal(1, table.GetProperty("rows").GetArrayLength());
    }

    private static void WriteEntry(System.IO.Compression.ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    [Fact]
    public async Task Excel_template_without_columns_should_return_400()
    {
        var client = await CreateClientAsync();
        var response = await client.PostAsync("/api/import/excel-template", new StringContent("""{ "columns": [] }""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Excel_template_then_import_should_roundtrip()
    {
        var client = await CreateClientAsync();

        // 生成模板（契约列 + 示例行）
        var template = await client.PostAsync("/api/import/excel-template", new StringContent(
            """{ "columns": [ { "key": "code", "displayName": "编码" }, { "key": "zone", "displayName": "区域" } ], "sampleRow": { "code": "A-01", "zone": "A" } }""",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        var xlsx = await template.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'P', xlsx[0]);

        // 上传解析：表头 + 示例行回读
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(xlsx);
        form.Add(file, "file", "template.xlsx");
        var imported = await client.PostAsync("/api/import/excel", form);

        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var table = await imported.Content.ReadFromJsonAsync<JsonElement>();
        var headers = table.GetProperty("headers");
        Assert.Equal(2, headers.GetArrayLength());
        Assert.Equal("编码", headers[0].GetString());
        var rows = table.GetProperty("rows");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal("A-01", rows[0][0].GetString());
    }
}

/// <summary>设备日志端点 HTTP 集成：回传 / 查询回环。</summary>
public class LogEndpointTests : SharedEndpointsTestBase
{
    [Fact]
    public async Task Push_then_query_should_roundtrip()
    {
        var client = await CreateClientAsync();

        var invalid = await client.PostAsync("/api/logs", new StringContent("""{ "deviceId": "" }""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var push = await client.PostAsync("/api/logs", new StringContent("""{ "deviceId": "dev-1", "lines": ["line-a", "line-b"] }""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);

        // 存储语义：一次回传的多行按物理行拆为独立记录（查询 id 倒序，最新在前）
        var entries = await client.GetFromJsonAsync<JsonElement>("/api/logs?deviceId=dev-1");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("line-b", entries[0].GetProperty("line").GetString());
        Assert.Equal("line-a", entries[1].GetProperty("line").GetString());
    }
}
