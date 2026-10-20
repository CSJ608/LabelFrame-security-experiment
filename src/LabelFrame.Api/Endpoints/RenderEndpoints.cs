using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Templates;
using LabelFrame.Rendering;

namespace LabelFrame.Api.Endpoints;

/// <summary>调试出图端点选项。</summary>
public sealed record RenderApiOptions(
    TemplateStore? Store,
    ILabelBitmapRenderer Renderer,
    Func<int> Dpi,
    string InvalidRequestCode);

/// <summary>调试出图端点（Server 与 WinHost 共用）：后端渲染 PNG / zip 下载，不建作业、不发驱动。</summary>
public static class RenderEndpoints
{
    public static IEndpointRouteBuilder MapRenderApi(this IEndpointRouteBuilder app, RenderApiOptions options)
    {
        app.MapPost("/api/print/render-image", async (SubmitJobRequest? request, CancellationToken ct) =>
        {
            if (request?.Template?.Contract is null || request.Template.Layout is null)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少 template（contract + layout）。"));
            }

            if (request.Labels is null || request.Labels.Count == 0)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少 labels（至少一张）。"));
            }

            var document = new LabelDocument
            {
                Layout = request.Template.Layout,
                Data = request.Labels[0].Data ?? new Dictionary<string, string>(),
            };
            IReadOnlyDictionary<string, byte[]> images;
            try
            {
                images = await ResolveImagesAsync(request.Template, options.Store, ct);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "模板图片 base64 无效。"));
            }

            byte[] png;
            try
            {
                png = options.Renderer.RenderLabelBitmapPng(document, options.Dpi(), images);
            }
            catch (MissingImageResourceException ex)
            {
                // 缺资源＝出图侧显式报错（不静默灰占位照打）
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, ex.Message));
            }

            var fileName = $"{(string.IsNullOrWhiteSpace(request.Template.Name) ? "label" : request.Template.Name)}-print.png";
            return Results.File(png, "image/png", fileName);
        });

        app.MapPost("/api/print/render-images", async (SubmitJobRequest? request, CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "请求体不能为空。"));
            }

            if (request.Template?.Contract is null || request.Template.Layout is null || request.Labels is null || request.Labels.Count == 0)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少 template 或 labels。"));
            }

            IReadOnlyDictionary<string, byte[]> images;
            try
            {
                images = await ResolveImagesAsync(request.Template, options.Store, ct);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "模板图片 base64 无效。"));
            }

            using var stream = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                for (var i = 0; i < request.Labels.Count; i++)
                {
                    var document = new LabelDocument
                    {
                        Layout = request.Template.Layout,
                        Data = request.Labels[i].Data ?? new Dictionary<string, string>(),
                    };
                    byte[] bitmapBytes;
                    try
                    {
                        // 缺资源＝批量出图同样显式报错（与 render-image / preview 同口径）
                        var bitmap = options.Renderer.RenderLabelBitmap(document, options.Dpi(), images);
                        bitmapBytes = LabelBitmapPng.ToPng(bitmap);
                    }
                    catch (MissingImageResourceException ex)
                    {
                        return Results.BadRequest(new ErrorView(options.InvalidRequestCode, ex.Message));
                    }

                    var entry = archive.CreateEntry($"label-{i + 1}.png");
                    using var entryStream = entry.Open();
                    entryStream.Write(bitmapBytes);
                }
            }

            var name = string.IsNullOrWhiteSpace(request.Template.Name) ? "label" : request.Template.Name;
            var zipName = $"{name}-debug-{DateTime.Now:yyyyMMddHHmmss}.zip";
            return Results.File(stream.ToArray(), "application/zip", zipName);
        });

        return app;
    }

    /// <summary>
    /// 模板图片资源解析（统一两宿主语义）：请求附带 base64 Images 优先；否则按 Name 从本地模板库回退加载。
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, byte[]>> ResolveImagesAsync(
        TemplateDto template,
        TemplateStore? store,
        CancellationToken cancellationToken = default)
    {
        if (template.Images is not null && template.Images.Count > 0)
        {
            return template.Images.ToDictionary(kv => kv.Key, kv => Convert.FromBase64String(kv.Value));
        }

        if (string.IsNullOrWhiteSpace(template.Name) || store is null)
        {
            return new Dictionary<string, byte[]>();
        }

        var package = await store.GetAsync(template.Name, cancellationToken);
        return package?.Images ?? new Dictionary<string, byte[]>();
    }
}
