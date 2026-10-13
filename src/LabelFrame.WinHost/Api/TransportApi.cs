using LabelFrame.Api;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.WinHost.Api;

/// <summary>连接管理端点：查询 / 切换 / 测试；单一连接生效，先测试后生效。</summary>
internal static class TransportApi
{
    // 旧字段 availableModes 的固定取值（兼容旧前端；新代码用 availablePlugins）
    private static readonly string[] LegacyModes = ["Log", "Tcp", "WindowsDriver", "Zebra"];

    public static IEndpointRouteBuilder MapTransportApi(this IEndpointRouteBuilder app)
    {
    // ---- 连接管理：查询 / 切换 / 测试；单一连接生效，先测试后生效 ----
        app.MapGet("/api/transport", (ITransportManager transportManager, ITransportPluginRegistry registry) =>
        Results.Ok(ToTransportConfigDto(transportManager.CurrentConfig, registry)));

    // 已装配传输插件列表（含来源：内置 / 外部 DLL）
    app.MapGet("/api/transport/plugins", (ITransportPluginRegistry registry) =>
        Results.Ok(registry.ListPlugins().Select(ToTransportPluginDescriptorDto)));

    app.MapPost("/api/transport", async (Api.TransportApplyRequest? request, ITransportManager transportManager, ITransportPluginRegistry registry, CancellationToken ct) =>
    {
        if (request is null)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.TransportInvalid, "请求体不能为空。"));
        }

        TransportConfig config;
        if (!string.IsNullOrWhiteSpace(request.PluginId))
        {
            // 新格式：pluginId + params 字典
            config = new TransportConfig
            {
                PluginId = request.PluginId,
                Params = request.Params is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(request.Params, StringComparer.OrdinalIgnoreCase),
            };
        }
        else if (!string.IsNullOrWhiteSpace(request.Mode))
        {
            // 旧格式兼容：mode + 平铺参数 → 迁移为 pluginId + params
            if (!Enum.TryParse<TransportMode>(request.Mode, ignoreCase: true, out var mode))
            {
                var modeParameters = new Dictionary<string, string> { ["mode"] = request.Mode };
                return Results.BadRequest(new ErrorView(
                    ApiErrorCodes.TransportInvalid,
                    LabelFrame.Core.Errors.ErrorMessageTemplates.Format("不支持的连接方式：{mode}。", modeParameters),
                    Params: modeParameters));
            }

            config = new TransportConfig
            {
                Mode = mode,
                TcpHost = request.TcpHost ?? transportManager.CurrentConfig.TcpHost,
                TcpPort = request.TcpPort ?? transportManager.CurrentConfig.TcpPort,
                PrinterName = request.PrinterName ?? transportManager.CurrentConfig.PrinterName,
                ZebraKind = request.ZebraKind is not null && Enum.TryParse<ZebraTransportKind>(request.ZebraKind, ignoreCase: true, out var zebraKind)
                    ? zebraKind
                    : transportManager.CurrentConfig.ZebraKind,
                ZebraUsbName = request.ZebraUsbName ?? transportManager.CurrentConfig.ZebraUsbName,
            };
            config.MigrateFromLegacy();
        }
        else
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.TransportInvalid, "缺少 pluginId 或 mode。"));
        }

        var result = await transportManager.ApplyAsync(config, request.TestOnly ?? false, ct);
        return Results.Ok(new TransportApplyResponse(result.Ok, result.Message, ToTransportConfigDto(result.Config, registry)));
    });

        return app;
    }

    internal static TransportConfigDto ToTransportConfigDto(TransportConfig config, ITransportPluginRegistry registry)
    {
        var plugin = registry.GetPlugin(config.PluginId);
        return new TransportConfigDto(
            config.PluginId,
            plugin?.DisplayName ?? config.PluginId,
            registry.Describe(config.PluginId, new TransportPluginParameters(config.Params)),
            new Dictionary<string, string>(config.Params, StringComparer.OrdinalIgnoreCase),
            registry.ListPlugins().Select(ToTransportPluginDescriptorDto).ToList(),
            config.Mode.ToString(),
            LegacyModes);
    }

    internal static TransportPluginDescriptorDto ToTransportPluginDescriptorDto(TransportPluginDescriptor plugin) => new(
        plugin.Id,
        plugin.DisplayName,
        plugin.Description,
        plugin.Parameters.Select(p => new TransportPluginParameterDto(
            p.Key,
            p.Label,
            p.Type.ToString(),
            p.Required,
            p.DefaultValue,
            p.Options?.Select(o => new TransportParameterOptionDto(o.Value, o.Label, o.LabelKey)).ToList(),
            p.Hint,
            p.VisibleWhenKey,
            p.VisibleWhenValues,
            p.LabelKey,
            p.HintKey)).ToList(),
        plugin.IsExternal,
        plugin.AssemblyPath,
        plugin.SupportsDocumentCompile,
        plugin.DisplayNameKey,
        plugin.DescriptionKey);
}
