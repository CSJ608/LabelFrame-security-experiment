using LabelFrame.Api;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.Server.Api;

/// <summary>分发端点：客户端安装包、PDA（Android 宿主）安装包与传输插件包的列表 / 上传 / 下载 / 删除。</summary>
internal static class PackagesApi
{
    public static IEndpointRouteBuilder MapPackagesApi(this IEndpointRouteBuilder app)
    {
// ---- 客户端下载分发（服务端统一分发客户端安装包）----
app.MapGet("/api/client-packages", (ClientPackagesService svc) => Results.Ok(svc.List()));

app.MapPost("/api/client-packages", async (IFormFile file, ClientPackagesService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请选择要上传的安装包文件。"));
    }

    try
    {
        var view = await svc.SaveAsync(file.FileName, file.OpenReadStream(), ct);
        return Results.Ok(view);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, ex.Message));
    }
    // 其余异常（磁盘满 / IO 故障等）交给全局异常处理器 → 500，不再误报 400 或透出内部信息
}).DisableAntiforgery();

app.MapGet("/api/client-packages/{fileName}", (string fileName, ClientPackagesService svc) =>
{
    var path = svc.GetDownloadPath(fileName);
    if (path is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.ClientPackageNotFound, "安装包不存在。"));
    }

    return Results.File(path, "application/octet-stream", Path.GetFileName(path));
});

app.MapDelete("/api/client-packages/{fileName}", (string fileName, ClientPackagesService svc) =>
{
    var view = svc.Get(fileName);
    if (view is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.ClientPackageNotFound, "安装包不存在。"));
    }

    svc.Delete(fileName);
    return Results.Ok(new { deleted = view.FileName });
});

// ---- PDA（Android 宿主）安装包分发（与 client-packages 对称；下载 MIME 为 APK 专用类型）----
app.MapGet("/api/pda-packages", (PdaPackagesService svc) => Results.Ok(svc.List()));

app.MapPost("/api/pda-packages", async (IFormFile file, PdaPackagesService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请选择要上传的 APK 文件。"));
    }

    try
    {
        var view = await svc.SaveAsync(file.FileName, file.OpenReadStream(), ct);
        return Results.Ok(view);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, ex.Message));
    }
    // 其余异常（磁盘满 / IO 故障等）交给全局异常处理器 → 500，不再误报 400 或透出内部信息
}).DisableAntiforgery();

app.MapGet("/api/pda-packages/{fileName}", (string fileName, PdaPackagesService svc) =>
{
    var path = svc.GetDownloadPath(fileName);
    if (path is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.PdaPackageNotFound, "安装包不存在。"));
    }

    // APK 专用 MIME：Android 浏览器据此识别为安装包并拉起安装
    return Results.File(path, PdaPackagesService.ApkContentType, Path.GetFileName(path));
});

app.MapDelete("/api/pda-packages/{fileName}", (string fileName, PdaPackagesService svc) =>
{
    var view = svc.Get(fileName);
    if (view is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.PdaPackageNotFound, "安装包不存在。"));
    }

    svc.Delete(fileName);
    return Results.Ok(new { deleted = view.FileName });
});

// ---- 传输插件包（插件包上传服务端，客户端安装用；列表含元数据与 valid 状态，路径穿越防护）----
app.MapGet("/api/plugin-packages", (PluginPackagesService svc) => Results.Ok(svc.List()));

app.MapPost("/api/plugin-packages", async (IFormFile file, PluginPackagesService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请选择要上传的插件包文件。"));
    }

    try
    {
        var view = await svc.SaveAsync(file.FileName, file.OpenReadStream(), ct);
        return Results.Ok(view);
    }
    catch (PluginPackageException ex)
    {
        // 业务性失败（非 zip / manifest 缺失或非法等）：消息已是中文可行动提示；detail 为异常原因（动态数据，展示端插值原样呈现）
        var parameters = new Dictionary<string, string> { ["detail"] = ex.Message };
        return Results.BadRequest(new ErrorView(
            ServerErrorCodes.InvalidRequest,
            LabelFrame.Core.Errors.ErrorMessageTemplates.Format("插件包无效：{detail}", parameters),
            Params: parameters));
    }
    catch (InvalidDataException)
    {
        // 其余框架抛出的 InvalidDataException：不透出英文原话，给通用中文可行动提示
        return Results.BadRequest(new ErrorView(
            ServerErrorCodes.InvalidRequest,
            "插件包无效，无法完成上传。请使用「导出插件包」生成的 .zip 文件重试；问题持续请联系插件提供方。"));
    }
    // 其余异常（磁盘满 / IO 故障等）交给全局异常处理器 → 500，不再误报 400 或透出内部信息
}).DisableAntiforgery();

app.MapGet("/api/plugin-packages/{fileName}", (string fileName, PluginPackagesService svc) =>
{
    var path = svc.GetDownloadPath(fileName);
    if (path is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.PluginPackageNotFound, "插件包不存在。"));
    }

    return Results.File(path, "application/octet-stream", Path.GetFileName(path));
});

app.MapDelete("/api/plugin-packages/{fileName}", (string fileName, PluginPackagesService svc) =>
{
    var view = svc.Get(fileName);
    if (view is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.PluginPackageNotFound, "插件包不存在。"));
    }

    svc.Delete(fileName);
    return Results.Ok(new { deleted = view.FileName });
});

        return app;
    }
}
