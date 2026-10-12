using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using LabelFrame.Api;
using LabelFrame.Core.Excel;

namespace LabelFrame.Api.Endpoints;

/// <summary>导入端点选项。</summary>
public sealed record ImportApiOptions(string InvalidRequestCode);

/// <summary>数据导入端点（Server 与 WinHost 共用）：Excel 模板生成 + xlsx 解析（前端做列映射）。</summary>
public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportApi(this IEndpointRouteBuilder app, ImportApiOptions options)
    {
        // 契约字段 + testData 生成 xlsx，直接套用 Excel 导入做打印测试
        app.MapPost("/api/import/excel-template", (ExcelTemplateRequest? request) =>
        {
            if (request is null || request.Columns is null || request.Columns.Count == 0)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少模板列（columns）。"));
            }

            var columns = request.Columns
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .Select(c => new ExcelTemplateColumn(c.Key!, string.IsNullOrWhiteSpace(c.DisplayName) ? c.Key! : c.DisplayName!))
                .ToList();
            if (columns.Count == 0)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少有效的模板列（key）。"));
            }

            var bytes = ExcelTemplateWriter.CreateTemplate(columns, request.SampleRow);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "excel-template.xlsx");
        });

        app.MapPost("/api/import/excel", async (IFormFile file, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "请上传 .xlsx 文件。"));
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var table = ExcelTableReader.ReadTextTable(stream);
                return Results.Ok(new { table.Headers, table.Rows });
            }
            catch (Exception ex)
            {
                // 模板化消息（③ /）：detail 为解析异常原因（动态数据，展示端插值原样呈现）
                var parameters = new Dictionary<string, string> { ["detail"] = ex.Message };
                return Results.BadRequest(new ErrorView(
                    options.InvalidRequestCode,
                    LabelFrame.Core.Errors.ErrorMessageTemplates.Format("Excel 解析失败：{detail}", parameters),
                    Params: parameters));
            }
        }).DisableAntiforgery();

        return app;
    }
}
