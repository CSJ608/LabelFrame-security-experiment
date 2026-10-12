using LabelFrame.Api;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Transport.Plugins.Package;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.WinHost.Api;

/// <summary>插件安装 / 卸载端点：插件包上传服务端 → 客户端下载安装 / 卸载（重启生效）。</summary>
internal static class PluginApi
{
    public static IEndpointRouteBuilder MapPluginApi(this IEndpointRouteBuilder app)
    {
    // 插件安装 / 卸载（安装 / 卸载 = 写文件 + 重启生效）
    app.MapGet("/api/plugins/installed", (PluginInstaller installer) => Results.Ok(installer.ListInstalled()));

    app.MapPost("/api/plugins/install", async (IFormFile file, PluginInstaller installer, CancellationToken ct) =>
    {
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, "请选择要安装的插件包。"));
        }

        try
        {
            var view = await installer.InstallAsync(file.OpenReadStream(), file.FileName, ct);
            return Results.Ok(new { ok = true, message = $"插件「{view.Name} {view.Version}」已安装，重启客户端后生效。", plugin = view });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginBusy, ex.Message));
        }
        catch (PluginPackageException ex)
        {
            // 业务性失败（非 zip / zip 损坏 / manifest 非法 / DLL 无效等）：消息已是中文可行动提示
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, ex.Message));
        }
        catch (InvalidDataException)
        {
            // 其余框架抛出的 InvalidDataException：不透出英文原话，给通用中文可行动提示
            return Results.BadRequest(new ErrorView(
                ApiErrorCodes.PluginInvalid,
                "插件包无效，无法完成安装。请使用「导出插件包」生成的 .zip 文件重试；问题持续请联系插件提供方。"));
        }
        // 其余意外异常交给全局异常处理器 → 500，不误报 400 或透出内部信息
    }).DisableAntiforgery();

    app.MapPost("/api/plugins/uninstall", (Api.UninstallPluginRequest? request, PluginInstaller installer) =>
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PluginId))
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, "缺少插件 ID。"));
        }

        try
        {
            installer.Uninstall(request.PluginId);
            return Results.Ok(new { ok = true, message = $"插件「{request.PluginId}」已卸载，重启客户端后生效。" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginBusy, ex.Message));
        }
        catch (PluginPackageException ex)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, ex.Message));
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new ErrorView(
                ApiErrorCodes.PluginInvalid,
                "无法卸载该插件：插件包信息无效。请确认其为「导出插件包」安装的插件后重试。"));
        }
    }).DisableAntiforgery();

        return app;
    }
}
