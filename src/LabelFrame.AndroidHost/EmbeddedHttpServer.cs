using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelFrame.AndroidHost.Api;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.AndroidHost.Updates;

namespace LabelFrame.AndroidHost;

/// <summary>本地 HTTP 服务（PDA 网页直连不经 Server）：TcpListener 极简实现（仅监听 127.0.0.1），
/// 端点与 WinHost 同构（含插件安装 / 卸载，重启生效）。</summary>
public sealed class EmbeddedHttpServer : IDisposable
{
    /// <summary>插件端点错误码（与 LabelFrame.Api 的 ApiErrorCodes 同值——AndroidHost 不引用 Api 工程，字面量对齐）。</summary>
    private static class PluginErrorCodes
    {
        public const string Invalid = "LF_PLUGIN_INVALID";
        public const string Busy = "LF_PLUGIN_BUSY";
        public const string InstallFailed = "LF_PLUGIN_INSTALL_FAILED";
    }

    private readonly int _port;
    private readonly SubmissionService _submission;
    private readonly LabelJobQueue _queue;
    private readonly ILabelJobStore _store;
    private readonly IPrintTransport _transport;
    private readonly IPrinterStatusProvider? _status;
    private readonly PluginInstaller? _pluginInstaller;
    private readonly Android.Content.Context _context;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly HashSet<Task> _requests = [];

    /// <summary>创建本地 HTTP 服务。</summary>
    /// <param name="port">监听端口（127.0.0.1）。</param>
    /// <param name="submission">本地提交服务。</param>
    /// <param name="queue">本地作业队列。</param>
    /// <param name="store">作业存储。</param>
    /// <param name="transport">当前打印传输（作业发送与打印机测试）。</param>
    /// <param name="context">Android 上下文（版本读取）。</param>
    /// <param name="pluginInstaller">插件安装服务（null = 插件端点不可用，防御式）。</param>
    public EmbeddedHttpServer(
        int port,
        SubmissionService submission,
        LabelJobQueue queue,
        ILabelJobStore store,
        IPrintTransport transport,
        Android.Content.Context context,
        PluginInstaller? pluginInstaller = null)
    {
        _port = port;
        _submission = submission;
        _queue = queue;
        _store = store;
        _transport = transport;
        _status = transport as IPrinterStatusProvider;
        _pluginInstaller = pluginInstaller;
        _context = context;
    }

    /// <summary>启动监听。</summary>
    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => LoopAsync(token));
        HostLog.Info(HostLog.Tags.Http, $"本地 HTTP 服务启动：127.0.0.1:{_port}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        HostLog.Info(HostLog.Tags.Http, "本地 HTTP 服务停止");
    }

    /// <summary>停止监听并等待在途请求释放后回收取消源。</summary>
    public async Task StopAsync()
    {
        Dispose();
        try
        {
            try
            {
                if (_loop is not null) await _loop.ConfigureAwait(false);
            }
            finally
            {
                await Task.WhenAll(_requests).ConfigureAwait(false);
            }
        }
        finally
        {
            _cts?.Dispose();
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 接受连接失败（监听已停等罕见场景）：记录后继续
                HostLog.Warn(HostLog.Tags.Http, $"接受新连接失败：{ex.Message}");
                continue;
            }

            _requests.RemoveWhere(static request => request.IsCompleted);
            // 已接受连接即由处理器负责释放，不能因调度取消而跳过 finally。
            _requests.Add(Task.Run(() => HandleAsync(client, cancellationToken), CancellationToken.None));
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        // 请求行解析成功前 method/path 未知，先占位；解析成功后更新供失败日志定位
        var method = "-";
        var path = "-";
        try
        {
            using var stream = client.GetStream();
            var (m, p, headers, bodyBytes) = await ReadRequestAsync(stream, cancellationToken);
            method = m;
            path = p;
            using var write = method is not ("GET" or "OPTIONS") ? MaintenanceGate.Shared.TryEnterWrite() : null;
            var response = method is not ("GET" or "OPTIONS") && write is null
                ? Json(409, new ErrorView("LF_UPDATE_BUSY", "应用升级维护中，暂不接受作业、配置或插件变更。"))
                : Route(method, path, headers, bodyBytes, cancellationToken);
            await WriteResponseAsync(stream, response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 服务停止时的在途请求：正常关闭路径
        }
        catch (Exception ex) when (method == "-")
        {
            // 请求头尚未读完就失败（对端断开 / 畸形请求）：单请求失败不影响服务
            HostLog.Warn(HostLog.Tags.Http, $"读取请求失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            // 单请求失败不影响服务
            HostLog.Error(HostLog.Tags.Http, $"请求处理失败 {method} {path}：{ex.Message}", ex.ToString());
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>统一缓冲读取请求（头与体从同一份累积字节解析，请求体以原始字节返回）。
    /// 不能先 StreamReader 读头再读体——预读缓冲会把请求体一并吞掉，后续按 Content-Length
    /// 直读网络流将永久等待（POST 带体请求挂死）。</summary>
    private static async Task<(string Method, string Path, Dictionary<string, string> Headers, byte[] BodyBytes)> ReadRequestAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        const int MaxHeaderBytes = 64 * 1024;
        const int MaxBodyBytes = 64 * 1024 * 1024;

        var received = new List<byte>(2048);
        var buffer = new byte[4096];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (n == 0)
            {
                throw new InvalidDataException("连接在请求头结束前关闭。");
            }

            received.AddRange(buffer.AsSpan(0, n));
            if (received.Count > MaxHeaderBytes)
            {
                throw new InvalidDataException("请求头超出大小上限。");
            }

            headerEnd = IndexOfHeaderTerminator(received);
        }

        var headerText = Encoding.UTF8.GetString([.. received.Take(headerEnd)]);
        var lines = headerText.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 3)
        {
            throw new InvalidDataException("请求行不合法。");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var idx = line.IndexOf(':');
            if (idx > 0)
            {
                headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
        }

        var bodyBytes = Array.Empty<byte>();
        if (headers.TryGetValue("Content-Length", out var lengthText)
            && int.TryParse(lengthText, out var length) && length > 0)
        {
            if (length > MaxBodyBytes)
            {
                throw new InvalidDataException("请求体超出大小上限。");
            }

            bodyBytes = new byte[length];
            var buffered = Math.Min(received.Count - headerEnd - 4, length);
            for (var i = 0; i < buffered; i++)
            {
                bodyBytes[i] = received[headerEnd + 4 + i];
            }

            var read = buffered;
            while (read < length)
            {
                var n = await stream.ReadAsync(bodyBytes.AsMemory(read, length - read), cancellationToken);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            if (read < length)
            {
                bodyBytes = bodyBytes[..read];
            }
        }

        return (parts[0].ToUpperInvariant(), parts[1], headers, bodyBytes);
    }

    /// <summary>在已接收字节中查找请求头结束符 \r\n\r\n 的起始下标。</summary>
    private static int IndexOfHeaderTerminator(List<byte> received)
    {
        for (var i = 0; i <= received.Count - 4; i++)
        {
            if (received[i] == 13 && received[i + 1] == 10 && received[i + 2] == 13 && received[i + 3] == 10)
            {
                return i;
            }
        }

        return -1;
    }

    private (int Status, string ContentType, byte[] Body) Route(
        string method, string path, Dictionary<string, string> headers, byte[] bodyBytes, CancellationToken cancellationToken)
    {
        var basePath = path.Split('?')[0];
        var body = Encoding.UTF8.GetString(bodyBytes);

        // JS 桥（第三方 WebView / 浏览器页面跨源直连）：宽松 CORS 预检直接放行
        if (method == "OPTIONS")
        {
            return (204, "text/plain; charset=utf-8", []);
        }

        if (method == "GET" && basePath == "/healthz")
        {
            return Json(200, new { service = "LabelFrame.AndroidHost", status = "ok", version = HostInfo.GetVersion(_context) });
        }

        if (method == "GET" && basePath == "/")
        {
            return Html(200, StatusPageHtml);
        }

        if (method == "POST" && basePath == "/api/jobs")
        {
            return SubmitJob(body);
        }

        if (method == "GET" && basePath == "/api/jobs")
        {
            return ListJobs(path);
        }

        if (method == "GET" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal))
        {
            var jobId = basePath["/api/jobs/".Length..];
            return GetJob(jobId);
        }

        if (method == "POST" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal) && basePath.EndsWith("/suspend", StringComparison.Ordinal))
        {
            return Transition(basePath, "suspend");
        }

        if (method == "POST" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal) && basePath.EndsWith("/resume", StringComparison.Ordinal))
        {
            return Transition(basePath, "resume");
        }

        if (method == "POST" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal) && basePath.EndsWith("/cancel", StringComparison.Ordinal))
        {
            return Transition(basePath, "cancel");
        }

        if (method == "POST" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal) && basePath.EndsWith("/retry", StringComparison.Ordinal))
        {
            return RetryItem(basePath);
        }

        if (method == "GET" && basePath == "/api/printer/status")
        {
            return GetPrinterStatus();
        }

        if (method == "POST" && basePath == "/api/printer/test")
        {
            return TestPrinter(cancellationToken);
        }

        // ---- 宿主配置（与 WinHost 同构；保存后重启宿主生效）----
        if (method == "GET" && basePath == "/api/host/config")
        {
            return Json(200, HostConfigView.From(LabelHostConfig.Load(_context), _context));
        }

        if (method == "POST" && basePath == "/api/host/config")
        {
            return SaveHostConfig(body);
        }

        // ---- 测试打印（内置测试标签走完整链路）----
        if (method == "POST" && basePath == "/api/host/test-print")
        {
            return TestPrint();
        }

        // ---- 插件安装 / 卸载（与 WinHost 端点同构；安装 / 卸载重启生效）----
        if (method == "GET" && basePath == "/api/plugins/installed")
        {
            return ListInstalledPlugins();
        }

        if (method == "POST" && basePath == "/api/plugins/install")
        {
            return InstallPlugin(bodyBytes);
        }

        if (method == "POST" && basePath == "/api/plugins/uninstall")
        {
            return UninstallPlugin(body);
        }

        return Json(404, new ErrorView(JobErrorCodes.JobNotFound, "接口不存在。"));
    }

    private (int, string, byte[]) SubmitJob(string body)
    {
        try
        {
            var request = System.Text.Json.JsonSerializer.Deserialize<SubmitJobRequest>(body, HostJson.Options);
            if (request is null)
            {
                return Json(400, new ErrorView(JobErrorCodes.InvalidRequest, "请求体不能为空。"));
            }

            var result = _submission.SubmitAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Job is null)
            {
                return Json(400, new ErrorView(result.ErrorCode!, result.ErrorMessage!, result.FieldKey));
            }

            return Json(result.Created ? 202 : 200, JobViews.From(result.Job));
        }
        catch (Exception ex)
        {
            return Json(400, new ErrorView(JobErrorCodes.InvalidRequest, $"请求解析失败：{ex.Message}"));
        }
    }

    private (int, string, byte[]) GetJob(string jobId)
    {
        var job = _queue.GetAsync(jobId, CancellationToken.None).GetAwaiter().GetResult();
        return job is null
            ? Json(404, new ErrorView(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。", null, new Dictionary<string, string> { ["jobId"] = jobId }))
            : Json(200, JobViews.From(job));
    }

    /// <summary>作业列表（与 WinHost GET /api/jobs?limit= 同构）。</summary>
    private (int, string, byte[]) ListJobs(string path)
    {
        var query = path.Contains('?', StringComparison.Ordinal) ? path.Split('?', 2)[1] : string.Empty;
        var limit = 100;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "limit" && int.TryParse(kv[1], out var parsed))
            {
                limit = Math.Clamp(parsed, 1, 500);
            }
        }

        var jobs = _store.ListRecentAsync(limit, CancellationToken.None).GetAwaiter().GetResult();
        return Json(200, jobs.Select(JobViews.From).ToList());
    }

    private (int, string, byte[]) Transition(string basePath, string action)
    {
        var middle = basePath["/api/jobs/".Length..];
        var jobId = middle[..middle.LastIndexOf('/')];
        try
        {
            var job = action switch
            {
                "suspend" => _queue.SuspendAsync(jobId, CancellationToken.None).GetAwaiter().GetResult(),
                "resume" => _queue.ResumeAsync(jobId, CancellationToken.None).GetAwaiter().GetResult(),
                _ => _queue.CancelAsync(jobId, CancellationToken.None).GetAwaiter().GetResult(),
            };
            return Json(200, JobViews.From(job));
        }
        catch (LabelJobException ex)
        {
            return ex.Code == JobErrorCodes.JobNotFound
                ? Json(404, new ErrorView(ex.Code, ex.Message))
                : Json(409, new ErrorView(ex.Code, ex.Message));
        }
    }

    /// <summary>失败项单独重打：把指定序号的 Failed Item 重置为 Pending（与 WinHost 端点同构）。
    /// 路径形如 /api/jobs/{jobId}/items/{itemIndex}/retry。</summary>
    private (int, string, byte[]) RetryItem(string basePath)
    {
        var middle = basePath["/api/jobs/".Length..];
        var parts = middle.Split('/');
        if (parts.Length != 4 || parts[1] != "items" || !int.TryParse(parts[2], out var itemIndex))
        {
            return Json(404, new ErrorView(JobErrorCodes.JobNotFound, "接口不存在。"));
        }

        try
        {
            var job = _queue.RetryItemAsync(parts[0], itemIndex, CancellationToken.None).GetAwaiter().GetResult();
            return Json(200, JobViews.From(job));
        }
        catch (LabelJobException ex)
        {
            return ex.Code == JobErrorCodes.JobNotFound
                ? Json(404, new ErrorView(ex.Code, ex.Message))
                : Json(409, new ErrorView(ex.Code, ex.Message));
        }
    }

    /// <summary>宿主配置视图（GET /api/host/config 响应形状）；Version 只读（POST 不可改），
    /// PrinterAddress 为按连接类型生成的用户可读连接摘要。</summary>
    private sealed record HostConfigView(
        string TcpHost, int TcpPort, string PrinterBrand, string ConnectionType,
        string BluetoothMac, string PrinterAddress,
        string ServerUrl, string DeviceId, string DeviceName, string LocalPort, string Version)
    {
        public static HostConfigView From(LabelHostConfig config, Android.Content.Context context)
            => new(
                config.TcpHost, config.TcpPort, config.PrinterBrand, config.ConnectionType,
                config.BluetoothMac, config.PrinterDisplay(context),
                config.ServerUrl, config.DeviceId, config.DeviceName,
                $"127.0.0.1:{LabelHostConfig.LocalPort}", HostInfo.GetVersion(context));
    }

    /// <summary>保存宿主配置到 SharedPreferences（null / 空白 / 越界字段保持原值）；传输与路由在下次宿主启动时按新配置创建。</summary>
    private (int, string, byte[]) SaveHostConfig(string body)
    {
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<HostConfigUpdateDto>(body, HostJson.Options);
            var config = LabelHostConfig.Load(_context);
            config.Persist(
                _context,
                dto?.ServerUrl,
                dto?.PrinterBrand,
                dto?.ConnectionType,
                dto?.TcpHost,
                dto?.TcpPort,
                dto?.BluetoothMac,
                dto?.DeviceName);
            return Json(200, HostConfigView.From(config, _context));
        }
        catch (Exception ex)
        {
            return Json(400, new ErrorView(JobErrorCodes.InvalidRequest, $"配置解析失败：{ex.Message}"));
        }
    }

    private sealed record HostConfigUpdateDto(
        string? ServerUrl, string? PrinterBrand, string? ConnectionType,
        string? TcpHost, int? TcpPort, string? BluetoothMac, string? DeviceName);

    /// <summary>已装插件列表（与 WinHost 同构；加载状态 / 失败原因透出）。</summary>
    private (int, string, byte[]) ListInstalledPlugins()
    {
        if (_pluginInstaller is null)
        {
            return Json(503, new ErrorView(PluginErrorCodes.Invalid, "插件通道不可用。"));
        }

        return Json(200, _pluginInstaller.ListInstalled());
    }

    /// <summary>安装插件包：请求体 = .lfplugin 包原始字节（octet-stream / zip 均可）；
    /// 三层校验（含平台门）在 Core PluginInstaller 内执行。</summary>
    private (int, string, byte[]) InstallPlugin(byte[] bodyBytes)
    {
        if (_pluginInstaller is null)
        {
            return Json(503, new ErrorView(PluginErrorCodes.Invalid, "插件通道不可用。"));
        }

        if (bodyBytes.Length == 0)
        {
            return Json(400, new ErrorView(PluginErrorCodes.Invalid, "请选择要安装的插件包。"));
        }

        try
        {
            var view = _pluginInstaller.InstallAsync(new MemoryStream(bodyBytes), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            return Json(200, new { ok = true, message = $"插件「{view.Name} {view.Version}」已安装，重启打印服务后生效。", plugin = view });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Json(400, new ErrorView(PluginErrorCodes.Busy, ex.Message));
        }
        catch (Core.Transport.Plugins.Package.PluginPackageException ex)
        {
            // 业务性失败（包校验等）：消息已是中文可行动提示；detail 随 ErrorView 透出供按码翻译插值
            return Json(400, new ErrorView(PluginErrorCodes.Invalid, ex.Message, null, new Dictionary<string, string> { ["detail"] = ex.Message }));
        }
        catch (Exception ex)
        {
            HostLog.Error(HostLog.Tags.Http, $"插件安装失败（POST /api/plugins/install）：{ex.Message}", ex.ToString());
            return Json(500, new ErrorView(PluginErrorCodes.InstallFailed, "插件安装失败，请重试；问题持续请联系管理员。"));
        }
    }

    /// <summary>卸载插件（JSON { pluginId }；删目录 + 重启生效）。</summary>
    private (int, string, byte[]) UninstallPlugin(string body)
    {
        if (_pluginInstaller is null)
        {
            return Json(503, new ErrorView(PluginErrorCodes.Invalid, "插件通道不可用。"));
        }

        string? pluginId = null;
        try
        {
            pluginId = System.Text.Json.JsonSerializer.Deserialize<UninstallPluginDto>(body, HostJson.Options)?.PluginId;
        }
        catch (Exception ex)
        {
            return Json(400, new ErrorView(PluginErrorCodes.Invalid, $"请求解析失败：{ex.Message}"));
        }

        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return Json(400, new ErrorView(PluginErrorCodes.Invalid, "缺少插件 ID。"));
        }

        try
        {
            _pluginInstaller.Uninstall(pluginId);
            return Json(200, new { ok = true, message = $"插件「{pluginId}」已卸载，重启打印服务后生效。" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Json(400, new ErrorView(PluginErrorCodes.Busy, ex.Message));
        }
        catch (Core.Transport.Plugins.Package.PluginPackageException ex)
        {
            return Json(400, new ErrorView(PluginErrorCodes.Invalid, ex.Message, null, new Dictionary<string, string> { ["detail"] = ex.Message }));
        }
    }

    private sealed record UninstallPluginDto(string? PluginId);

    private (int, string, byte[]) GetPrinterStatus()
    {
        if (_status is null)
        {
            return Json(200, new PrinterStatusInfo(false, false, false, "当前传输不支持状态查询。"));
        }

        var status = _status.GetStatusAsync(CancellationToken.None).GetAwaiter().GetResult();
        return Json(200, status);
    }

    private (int, string, byte[]) TestPrinter(CancellationToken cancellationToken)
    {
        const string testZpl =
            "^XA^FO40,40^A0N,64,64^FDLabelFrame Test^FS" +
            "^FO40,120^BY2,3^BCN,80,Y,N,N^FDLABELFRAME-TEST^FS^XZ";
        try
        {
            _transport.SendAsync(testZpl, cancellationToken).GetAwaiter().GetResult();
            return Json(200, new { sent = true, bytes = Encoding.UTF8.GetByteCount(testZpl) });
        }
        catch (Exception ex)
        {
            // 5xx 的具体原因（连接重置等）只有此处可见，必须留痕
            HostLog.Error(HostLog.Tags.Http, $"打印机测试发送失败（POST /api/printer/test）：{ex.Message}", ex.ToString());
            return Json(500, new ErrorView(JobErrorCodes.TransportSendFailed, $"发送失败：{ex.Message}", null, new Dictionary<string, string> { ["reason"] = ex.Message }));
        }
    }

    /// <summary>测试打印：提交内置测试标签作业（校验 → 渲染 → ^GF → TCP 发送），返回作业供调用方轮询终态。</summary>
    private (int, string, byte[]) TestPrint()
    {
        var request = new SubmitJobRequest(
            $"self-test-{Guid.NewGuid():N}",
            new TemplateDto(TestLabelTemplate.Contract, TestLabelTemplate.Layout),
            new List<LabelDto> { new(TestLabelTemplate.SampleData()) });
        var result = _submission.SubmitAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        if (result.Job is null)
        {
            return Json(400, new ErrorView(result.ErrorCode!, result.ErrorMessage!, result.FieldKey));
        }

        return Json(202, JobViews.From(result.Job));
    }

    private static (int, string, byte[]) Html(int status, string html)
        => (status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

    /// <summary>宿主状态页：本机 / 服务器 / 打印机状态 + 测试打印（轻量单页；第三方集成走同端口 HTTP API）。</summary>
    private const string StatusPageHtml = """
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>LabelFrame 打印状态</title>
        <style>
          body { font-family: system-ui, sans-serif; margin: 0; background: #f2f4f7; color: #1f2329; }
          .wrap { padding: 16px; }
          h1 { font-size: 20px; margin: 0 0 2px; }
          .sub { color: #6b7075; font-size: 13px; margin: 0 0 14px; }
          .card { background: #fff; border-radius: 12px; padding: 14px 16px; margin-bottom: 12px; }
          .card h2 { font-size: 15px; margin: 0 0 6px; }
          .row { display: flex; justify-content: space-between; gap: 12px; font-size: 14px; margin: 6px 0; }
          .row b { color: #6b7075; font-weight: 600; flex-shrink: 0; }
          .row span { text-align: right; word-break: break-all; }
          .ok { color: #1b7e3a; } .err { color: #c5221f; } .warn { color: #b25e00; }
          .muted { color: #6b7075; }
          button { display: block; width: 100%; padding: 14px; font-size: 16px; color: #fff;
                   background: #1d6fe0; border: 0; border-radius: 10px; min-height: 48px; }
          #result { margin-top: 10px; font-size: 14px; white-space: pre-wrap; }
          .foot { color: #9aa0a6; font-size: 12px; margin-top: 16px; }
        </style>
        </head>
        <body>
        <div class="wrap">
        <h1>LabelFrame 打印状态</h1>
        <p class="sub">这台 PDA 打印服务的运行情况</p>
        <div class="card" id="device"><h2>本机</h2><div class="row">正在读取…</div></div>
        <div class="card"><h2>服务器</h2><div class="row"><b>地址</b><span id="server">—</span></div></div>
        <div class="card">
          <h2>打印机</h2>
          <div class="row"><b>地址</b><span id="printerAddr">—</span></div>
          <div class="row"><b>状态</b><span id="printer">正在检查…</span></div>
        </div>
        <button id="print">打印一张测试标签</button>
        <div id="result"></div>
        <p class="foot">本页由 PDA 上的打印服务提供；开发者集成请用同端口的 HTTP 接口（见项目文档）。</p>
        </div>
        <script>
        const resultEl = document.getElementById('result');
        function setResult(text, cls) {
          resultEl.textContent = text;
          resultEl.className = cls || '';
        }
        async function loadConfig() {
          try {
            const res = await fetch('/api/host/config');
            const c = await res.json();
            document.getElementById('device').innerHTML =
              '<h2>本机</h2>' +
              '<div class="row"><b>设备号</b><span>' + c.DeviceId + '</span></div>' +
              '<div class="row"><b>设备名称</b><span>' + c.DeviceName + '</span></div>';
            document.getElementById('server').textContent = c.ServerUrl || '未设置';
            document.getElementById('printerAddr').textContent = c.PrinterAddress || (c.TcpHost + ':' + c.TcpPort);
          } catch (ex) {
            document.getElementById('device').innerHTML =
              '<h2>本机</h2><div class="row err">读取失败，刷新页面试试</div>';
          }
        }
        async function loadPrinter() {
          const el = document.getElementById('printer');
          try {
            const res = await fetch('/api/printer/status');
            const s = await res.json();
            if (s.IsOnline) {
              if (s.IsPaperOut) { el.textContent = '缺纸，请装纸'; el.className = 'err'; }
              else if (s.IsPaused) { el.textContent = '已暂停'; el.className = 'warn'; }
              else { el.textContent = '在线，可以打印'; el.className = 'ok'; }
            } else {
              el.textContent = '连不上——请检查打印机电源和连接方式';
              el.className = 'err';
            }
          } catch (ex) {
            el.textContent = '检查失败，稍后自动重试';
            el.className = 'muted';
          }
        }
        document.getElementById('print').onclick = async () => {
          setResult('正在发送到打印机…', 'muted');
          try {
            const res = await fetch('/api/host/test-print', { method: 'POST' });
            const job = await res.json();
            if (!res.ok) { setResult('✗ 没打出来——请再试一次', 'err'); return; }
            const timer = setInterval(async () => {
              const r = await fetch('/api/jobs/' + job.JobId);
              const j = await r.json();
              if (j.Status === 'Completed' || j.Status === 'Failed' || j.Status === 'Cancelled') {
                clearInterval(timer);
                if (j.Status === 'Completed') {
                  setResult('✓ 打印成功，已出纸（' + j.CompletedItems + '/' + j.TotalItems + '）', 'ok');
                } else {
                  const err = (j.Items || []).find(x => x.ErrorMessage)?.ErrorMessage || '';
                  setResult('✗ 没打出来——请检查打印机是否开机、连接方式与地址是否正确' + (err ? '（原因：' + err + '）' : ''), 'err');
                }
              } else {
                setResult('正在打印…（' + j.CompletedItems + '/' + j.TotalItems + '）', 'muted');
              }
            }, 1000);
          } catch (ex) { setResult('出错了，请再试一次', 'err'); }
        };
        loadConfig();
        loadPrinter();
        setInterval(loadPrinter, 10000);
        </script>
        </body>
        </html>
        """;

    private static (int, string, byte[]) Json(int status, object value)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, HostJson.Options);
        return (status, "application/json; charset=utf-8", bytes);
    }

    private static async Task WriteResponseAsync(Stream stream, (int Status, string ContentType, byte[] Body) response, CancellationToken cancellationToken)
    {
        var reason = response.Status switch
        {
            200 => "OK",
            202 => "Accepted",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            409 => "Conflict",
            500 => "Internal Server Error",
            _ => "OK",
        };
        var head = $"HTTP/1.1 {response.Status} {reason}\r\n" +
                   $"Content-Type: {response.ContentType}\r\n" +
                   $"Content-Length: {response.Body.Length}\r\n" +
                   "Access-Control-Allow-Origin: *\r\n" +
                   "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                   "Access-Control-Allow-Headers: Content-Type\r\n" +
                   "Connection: close\r\n\r\n";
        var headBytes = Encoding.UTF8.GetBytes(head);
        await stream.WriteAsync(headBytes, cancellationToken);
        await stream.WriteAsync(response.Body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
