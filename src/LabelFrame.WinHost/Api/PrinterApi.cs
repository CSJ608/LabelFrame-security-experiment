using System.Net.Sockets;
using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using LabelFrame.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace LabelFrame.WinHost.Api;

/// <summary>打印机状态 / 测试页端点（测试页与正式打印同源：Skia 整版位图按连接分派图片指令编码）。</summary>
internal static class PrinterApi
{
    public static IEndpointRouteBuilder MapPrinterApi(this IEndpointRouteBuilder app, int dpi)
    {
    // ---- 打印机测试页 / 在线状态 ----
    app.MapGet("/api/printer/status", async (ITransportManager transportManager, CancellationToken ct) =>
        Results.Ok(await (transportManager.CurrentTransport as IPrinterStatusProvider ?? new Program.UnsupportedStatusProvider()).GetStatusAsync(ct)));

    app.MapPost("/api/printer/test", async (ITransportManager transportManager, ITransportPluginRegistry pluginRegistry, ILabelBitmapRenderer renderer, ZplImageEncoder encoder, ILoggerFactory loggerFactory, CancellationToken ct) =>
    {
        // 测试页与正式打印同源：Skia 渲染整版位图，图片指令编码按连接插件分派；
        // 版面尺寸跟连接媒介参数（LabelMediaSize：TSPL 固件严格按 SIZE 走纸，与实体一致才出完整一张）
        var config = transportManager.CurrentConfig;
        var document = BuildTestDocument(LabelMediaSize.Resolve(config.Params));
        var bitmap = renderer.RenderLabelBitmap(document, dpi);
        var imageEncoder = pluginRegistry.GetImageEncoder(config.PluginId);
        var commandBytes = imageEncoder?.EncodeImage(bitmap, document.Layout.WidthMm, document.Layout.HeightMm, dpi);
        var commandText = commandBytes is null
            ? encoder.EncodeImage(bitmap, document.Layout.WidthMm, document.Layout.HeightMm, dpi)
            : null;
        var byteCount = commandBytes?.Length ?? System.Text.Encoding.UTF8.GetByteCount(commandText!);
        var target = DescribeTarget(config);
        try
        {
            if (commandBytes is not null)
            {
                await transportManager.CurrentTransport.SendAsync(commandBytes, ct);
            }
            else
            {
                await transportManager.CurrentTransport.SendAsync(commandText!, ct);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or InvalidOperationException)
        {
            // 打印机连接失败是环境问题而非服务故障：400 + 可编程错误码（含目标地址与原因）；
            // 客户端日志留痕（目标 / 插件 / 原因）
            var logger = loggerFactory.CreateLogger("LabelFrame.WinHost.Api.PrinterApi");
            logger.LogWarning(ex, "测试页发送失败：目标 {Target}（插件 {PluginId}）", target, config.PluginId);
            // 模板化消息：target / reason 为动态数据，展示端按码表插值呈现
            var parameters = new Dictionary<string, string> { ["target"] = target, ["reason"] = ex.Message };
            return Results.BadRequest(new ErrorView(
                ApiErrorCodes.TransportTestFailed,
                LabelFrame.Core.Errors.ErrorMessageTemplates.Format("测试页发送失败：无法连接打印机「{target}」——{reason}。请检查打印机地址 / 网络 / 驱动后重试。", parameters),
                Params: parameters));
        }
        return Results.Ok(new { sent = true, bytes = byteCount });
    });

        return app;
    }

    /// <summary>打印目标描述（错误消息用）：host:port / 打印机名 / USB 名，按当前连接配置取值，回退插件 ID。</summary>
    internal static string DescribeTarget(TransportConfig config)
    {
        if (config.Params.TryGetValue("host", out var host) && !string.IsNullOrWhiteSpace(host))
        {
            return config.Params.TryGetValue("port", out var port) && !string.IsNullOrWhiteSpace(port)
                ? $"{host}:{port}"
                : host;
        }

        if (config.Params.TryGetValue("printerName", out var printer) && !string.IsNullOrWhiteSpace(printer))
        {
            return printer;
        }

        if (config.Params.TryGetValue("usbName", out var usb) && !string.IsNullOrWhiteSpace(usb))
        {
            return usb;
        }

        return config.PluginId;
    }

    /// <summary>测试页文档（internal 供单测锚定）：单行居中文本；默认 40×20 与历史版面完全一致，仅字高随纸高在 3..8mm 缩放。</summary>
    internal static LabelDocument BuildTestDocument((double WidthMm, double HeightMm) media)
    {
        const double marginMm = 2;
        const double yMm = 4;
        var fontHeightMm = Math.Clamp(Math.Round(media.HeightMm / 10.0, 1), 3, 8);
        return new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "test",
                ContractName = "test",
                ContractVersion = "1.0",
                WidthMm = media.WidthMm,
                HeightMm = media.HeightMm,
                Elements =
                [
                    new LabelTextElement
                    {
                        Literal = "LabelFrame Test",
                        XMm = marginMm,
                        YMm = yMm,
                        FontHeightMm = fontHeightMm,
                        FontWidthMm = fontHeightMm,
                        WidthMm = media.WidthMm - marginMm * 2,
                        TextAlign = LabelTextAlign.Center,
                    },
                ],
            },
            Data = new Dictionary<string, string>(),
        };
    }
}
