using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Templates;
using LabelFrame.Rendering;

namespace LabelFrame.Api.Endpoints;

/// <summary>模板端点选项（宿主传入各自的问题码前缀，保持两宿主对外错误码不变）。</summary>
public sealed record TemplateApiOptions(
    TemplateStore Store,
    ILabelBitmapRenderer Renderer,
    int Dpi,
    string InvalidRequestCode,
    string TemplateNotFoundCode,
    string? ImageInvalidCode = null)
{
    /// <summary>模板图片校验失败码（缺省取注册表 LF_TPL_002；宿主可覆盖前缀）。</summary>
    public string ImageInvalid => ImageInvalidCode ?? ApiErrorCodes.TemplateImageInvalid;
}

/// <summary>模板库端点（Server 与 WinHost 共用）：CRUD + 导入导出 + 预览。</summary>
public static class TemplateEndpoints
{
    /// <summary>单图体积上限（解码后原始字节）：2MB。</summary>
    private const int MaxImageBytes = 2 * 1024 * 1024;

    /// <summary>单模板图片张数上限：10 张。</summary>
    private const int MaxImageCount = 10;

    /// <summary>单模板图片总量上限（解码后原始字节）：10MB。</summary>
    private const long MaxTotalImageBytes = 10L * 1024 * 1024;

    public static IEndpointRouteBuilder MapTemplateApi(this IEndpointRouteBuilder app, TemplateApiOptions options)
    {
        app.MapPost("/api/templates", async (TemplatePackageDto? dto, CancellationToken ct) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name) || dto.Contract is null || dto.Layout is null)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少模板 name / contract / layout。"));
            }

            IReadOnlyDictionary<string, byte[]> images;
            try
            {
                images = await ResolvePostedImagesAsync(options, dto, ct);
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new ErrorView(options.ImageInvalid, $"模板图片 base64 无效：{ex.Message}。"));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorView(options.ImageInvalid, ex.Message));
            }

            await options.Store.SaveAsync(new TemplatePackage
            {
                Name = dto.Name,
                Group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group,
                Contract = dto.Contract,
                Layout = dto.Layout,
                TestData = dto.TestData ?? new Dictionary<string, string>(),
                Images = images,
            }, ct);
            return Results.Ok(new { name = dto.Name, group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group });
        });

        app.MapGet("/api/templates", async (string? group, CancellationToken ct) =>
            Results.Ok(await options.Store.ListAsync(group, ct)));

        app.MapGet("/api/templates/{name}", async (string name, CancellationToken ct) =>
        {
            var package = await options.Store.GetAsync(name, ct);
            if (package is not null)
            {
                return Results.Ok(package);
            }

            // 模板化消息须与旧内插文案逐字一致（冒号后无空格为既有形态）；
            // 参数键固定 templateName——同码参数键集不一致会使 en 码表词条插值失配回退中文
            var parameters = new Dictionary<string, string> { ["templateName"] = name };
            return Results.NotFound(new ErrorView(
                options.TemplateNotFoundCode,
                LabelFrame.Core.Errors.ErrorMessageTemplates.Format("模板不存在:{templateName}。", parameters),
                Params: parameters));
        });

        app.MapDelete("/api/templates/{name}", async (string name, CancellationToken ct) =>
        {
            await options.Store.DeleteAsync(name, ct);
            return Results.NoContent();
        });

        app.MapGet("/api/templates/{name}/export", async (string name, CancellationToken ct) =>
        {
            var package = await options.Store.GetAsync(name, ct);
            if (package is not null)
            {
                return Results.File(TemplatePackageSerializer.Export(package), "application/zip", $"{name}.lfpkg");
            }

            var exportParameters = new Dictionary<string, string> { ["templateName"] = name };
            return Results.NotFound(new ErrorView(
                options.TemplateNotFoundCode,
                LabelFrame.Core.Errors.ErrorMessageTemplates.Format("模板不存在:{templateName}。", exportParameters),
                Params: exportParameters));
        });

        app.MapPost("/api/templates/import", async (IFormFile file, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少模板包文件。"));
            }

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            try
            {
                var package = TemplatePackageSerializer.Import(memory.ToArray());
                await options.Store.SaveAsync(package, ct);
                return Results.Ok(package.Name);
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, ex.Message));
            }
        }).DisableAntiforgery();

        app.MapPost("/api/templates/{name}/preview", async (string name, PreviewRequest? request, CancellationToken ct) =>
        {
            var package = await options.Store.GetAsync(name, ct);
            if (package is null)
            {
                var previewParameters = new Dictionary<string, string> { ["templateName"] = name };
                return Results.NotFound(new ErrorView(
                    options.TemplateNotFoundCode,
                    LabelFrame.Core.Errors.ErrorMessageTemplates.Format("模板不存在:{templateName}。", previewParameters),
                    Params: previewParameters));
            }

            var document = new LabelDocument
            {
                Layout = package.Layout,
                Data = request?.Data ?? package.TestData ?? new Dictionary<string, string>(),
            };
            byte[] png;
            try
            {
                png = options.Renderer.RenderLabelBitmapPng(document, options.Dpi, package.Images);
            }
            catch (MissingImageResourceException ex)
            {
                // 缺资源＝预览侧显式报错（不静默灰占位照打）
                return Results.BadRequest(new ErrorView(options.ImageInvalid, ex.Message));
            }

            return Results.File(png, "image/png");
        });

        return app;
    }

    /// <summary>
    /// 保存请求图片三态解析与硬校验：null＝读存量并入＝保留；显式字典＝解码校验（空字典自然产出空集＝清空）。
    /// 失败抛 <see cref="FormatException"/>（坏 base64，消息为键名）或 <see cref="InvalidOperationException"/>（上限 / 格式，中文消息含键名）。
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, byte[]>> ResolvePostedImagesAsync(
        TemplateApiOptions options,
        TemplatePackageDto dto,
        CancellationToken ct)
    {
        if (dto.Images is null)
        {
            var existing = await options.Store.GetAsync(dto.Name!, ct);
            return existing?.Images ?? new Dictionary<string, byte[]>();
        }

        if (dto.Images.Count > MaxImageCount)
        {
            throw new InvalidOperationException($"模板图片数量超过上限（{MaxImageCount} 张）。");
        }

        var decoded = new Dictionary<string, byte[]>(dto.Images.Count, StringComparer.Ordinal);
        long total = 0;
        foreach (var (key, base64) in dto.Images)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new FormatException(key);
            }

            if (bytes.LongLength > MaxImageBytes)
            {
                throw new InvalidOperationException($"图片超过单张上限（{MaxImageBytes / (1024 * 1024)}MB）：{key}。");
            }

            if (!IsSupportedImage(bytes))
            {
                throw new InvalidOperationException($"图片格式不支持（仅 PNG / JPG / BMP）：{key}。");
            }

            total += bytes.LongLength;
            if (total > MaxTotalImageBytes)
            {
                throw new InvalidOperationException($"模板图片总量超过上限（{MaxTotalImageBytes / (1024 * 1024)}MB）。");
            }

            decoded[key] = bytes;
        }

        return decoded;
    }

    /// <summary>格式探嗅（魔数）：PNG（89 50 4E 47）/ JPEG（FF D8 FF）/ BMP（42 4D），与双端解码器能力对齐。</summary>
    private static bool IsSupportedImage(byte[] bytes) => bytes.Length >= 4
        && (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            || bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
            || bytes[0] == 0x42 && bytes[1] == 0x4D);
}
