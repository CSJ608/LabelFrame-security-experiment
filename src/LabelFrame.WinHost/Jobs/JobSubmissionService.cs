using LabelFrame.Api;
using LabelFrame.Api.Endpoints;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.Core.Validation;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Jobs;

/// <summary>作业提交服务：契约校验 → 按当前连接的打印方式分派（image 渲染 ^GF / native 插件编译），任一标签失败整体拒绝；Log 连接＝模拟打印（PNG 出图＋作业数据留痕）。</summary>
public sealed class JobSubmissionService
{
    private readonly LabelJobQueue _queue;
    private readonly ZplImageEncoder _encoder;
    private readonly ILabelBitmapRenderer _renderer;
    private readonly TemplateStore _templateStore;
    private readonly ITransportManager _transportManager;
    private readonly ITransportPluginRegistry _pluginRegistry;
    private readonly ITransportPluginContext _pluginContext;
    private readonly TextWriter _hostLogWriter;
    private readonly string _printOutputPath;
    private readonly PrintImageRetentionCleaner? _printRetentionCleaner;
    private readonly int _dpi;

    /// <summary>创建提交服务。</summary>
    public JobSubmissionService(
        LabelJobQueue queue,
        ZplImageEncoder encoder,
        int dpi,
        ILabelBitmapRenderer renderer,
        TemplateStore templateStore,
        ITransportManager transportManager,
        ITransportPluginRegistry pluginRegistry,
        ITransportPluginContext pluginContext,
        TextWriter hostLogWriter,
        string? printOutputPath = null,
        PrintImageRetentionCleaner? printRetentionCleaner = null)
    {
        _queue = queue;
        _encoder = encoder;
        _renderer = renderer;
        _templateStore = templateStore;
        _transportManager = transportManager;
        _pluginRegistry = pluginRegistry;
        _pluginContext = pluginContext;
        _hostLogWriter = hostLogWriter;
        _printOutputPath = printOutputPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LabelFrame",
            "print");
        _printRetentionCleaner = printRetentionCleaner;
        _dpi = dpi;
    }

    /// <summary>提交作业；失败返回问题码（不建作业）。</summary>
    public async Task<SubmitJobResult> SubmitAsync(SubmitJobRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return SubmitJobResult.Failure(JobErrorCodes.InvalidRequest, "缺少 requestId（幂等键）。");
        }

        var config = _transportManager.CurrentConfig;
        var rawPrintMode = config.Params.TryGetValue(TransportPrintMode.ParameterKey, out var raw) ? raw : null;
        var printMode = TransportPrintMode.Resolve(rawPrintMode);

        IReadOnlyList<LabelJobPayload> payloads;
        IReadOnlyList<RenderItem>? renderedItems = null;
        if (printMode == TransportPrintMode.Native)
        {
            // 幂等短路（重放不重新编译）：同 requestId 已有作业直接返回，编译调用零增加
            var existing = await _queue.GetByRequestIdAsync(request.RequestId!, cancellationToken);
            if (existing is not null)
            {
                return SubmitJobResult.Success(existing, created: false);
            }

            // 存量异常配置兜底（手改 connection.json / 插件降级后能力消失）：
            // native 但插件无编译能力 → 显式失败，不静默按图片打印
            var compiler = _pluginRegistry.GetCommandCompiler(config.PluginId);
            if (compiler is null)
            {
                return SubmitJobResult.Failure(
                    JobErrorCodes.PrintModeNotSupported,
                    $"{JobErrorCodes.PrintModeNotSupported} 当前打印方式为原生指令，但连接的插件 {config.PluginId} 不支持指令编译，请切回图片或更换插件。");
            }

            var compiled = await CompileAllAsync(request, config.PluginId, compiler, cancellationToken);
            if (compiled.ErrorCode is not null)
            {
                return SubmitJobResult.Failure(compiled.ErrorCode, compiled.ErrorMessage!, compiled.FieldKey);
            }

            payloads = compiled.Commands!.Select(LabelJobPayload.OfText).ToList();
        }
        else
        {
            var rendered = await RenderAllAsync(request, cancellationToken);
            if (rendered.ErrorCode is not null)
            {
                return SubmitJobResult.Failure(rendered.ErrorCode, rendered.ErrorMessage!, rendered.FieldKey);
            }

            renderedItems = rendered.Items!;
            try
            {
                // 图片指令编码按连接插件分派：未实现 ILabelImageEncoder 的连接（Zebra / 内置通道）维持 ^GF 文本，输出逐字节不变
                var imageEncoder = _pluginRegistry.GetImageEncoder(config.PluginId);
                var encodeOptions = new LabelImageEncodeOptions(
                    InvertBits: config.Params.TryGetValue(LabelImageEncodeOptions.InvertBitsParameterKey, out var invertRaw)
                        && bool.TryParse(invertRaw, out var invertValue)
                        && invertValue);
                payloads = imageEncoder is null
                    ? renderedItems.Select(i => LabelJobPayload.OfText(_encoder.EncodeImage(i.Bitmap, i.WidthMm, i.HeightMm, _dpi))).ToList()
                    : renderedItems.Select(i => LabelJobPayload.OfBytes(imageEncoder.EncodeImage(i.Bitmap, i.WidthMm, i.HeightMm, _dpi, encodeOptions))).ToList();
            }
            catch (NotSupportedException ex)
            {
                return SubmitJobResult.Failure(JobErrorCodes.EncodeFailed, ex.Message, null);
            }
            catch (ArgumentException ex)
            {
                return SubmitJobResult.Failure(JobErrorCodes.EncodeFailed, ex.Message, null);
            }
        }

        var (job, created) = await _queue.SubmitAsync(request.RequestId!, payloads, cancellationToken);

        // Log 连接 = 模拟打印：保存渲染 PNG 到 print\{jobId}\（不发真机）；原生指令路径无渲染位图
        if (created && _transportManager.CurrentConfig.Mode == TransportMode.Log)
        {
            if (renderedItems is not null)
            {
                SaveLogPrintImages(job.Id, renderedItems);
            }

            LogJobData(job.Id, request);

            // 落盘后顺带执行出图目录保留清理（清理器永不抛出，不打断打印链路）
            _printRetentionCleaner?.CleanupExpired();
        }

        return SubmitJobResult.Success(job, created);
    }

    /// <summary>渲染结果：单张标签的整版位图与尺寸。</summary>
    public sealed record RenderedImage(int Index, byte[] Png);

    private sealed record RenderItem(int Index, LabelBitmap Bitmap, double WidthMm, double HeightMm);

    private sealed record RenderResult(IReadOnlyList<RenderItem>? Items, string? ErrorCode, string? ErrorMessage, string? FieldKey);

    private sealed record CompileResult(IReadOnlyList<string>? Commands, string? ErrorCode, string? ErrorMessage, string? FieldKey);

    private async Task<RenderResult> RenderAllAsync(SubmitJobRequest request, CancellationToken cancellationToken)
    {
        if (request.Template?.Contract is null || request.Template.Layout is null)
        {
            return new RenderResult(null, JobErrorCodes.InvalidRequest, "缺少 template（contract + layout）。", null);
        }

        if (request.Labels is null || request.Labels.Count == 0)
        {
            return new RenderResult(null, JobErrorCodes.InvalidRequest, "缺少 labels（至少一张）。", null);
        }

        var images = await LoadTemplateImagesAsync(request.Template, cancellationToken);
        var items = new List<RenderItem>(request.Labels.Count);
        for (var i = 0; i < request.Labels.Count; i++)
        {
            var label = request.Labels[i];
            var data = label.Data ?? new Dictionary<string, string>();
            var validation = LabelValidator.Validate(request.Template.Contract, data);
            if (!validation.IsValid)
            {
                var problem = validation.Problems[0];
                return new RenderResult(null, problem.Code, problem.Message, problem.FieldKey);
            }

            try
            {
                var document = new LabelDocument
                {
                    Layout = request.Template.Layout,
                    Data = data,
                };
                var bitmap = _renderer.RenderLabelBitmap(document, _dpi, images);
                items.Add(new RenderItem(i, bitmap, document.Layout.WidthMm, document.Layout.HeightMm));
            }
            catch (NotSupportedException ex)
            {
                return new RenderResult(null, JobErrorCodes.EncodeFailed, ex.Message, null);
            }
            catch (ArgumentException ex)
            {
                return new RenderResult(null, JobErrorCodes.EncodeFailed, ex.Message, null);
            }
            catch (MissingImageResourceException ex)
            {
                // 缺资源＝打印链显式失败（消息含键名），不静默灰占位照打
                return new RenderResult(null, JobErrorCodes.InvalidRequest, ex.Message, null);
            }
        }

        return new RenderResult(items, null, null, null);
    }

    /// <summary>
    /// 原生指令路径：契约校验后逐张调用插件编译器，产物即该 Item 的最终指令（整页自包含，宿主不包装追加）；
    /// 任一标签编译失败整体拒绝，显式失败不回退图片。
    /// </summary>
    private async Task<CompileResult> CompileAllAsync(
        SubmitJobRequest request,
        string pluginId,
        ILabelCommandCompiler compiler,
        CancellationToken cancellationToken)
    {
        if (request.Template?.Contract is null || request.Template.Layout is null)
        {
            return new CompileResult(null, JobErrorCodes.InvalidRequest, "缺少 template（contract + layout）。", null);
        }

        if (request.Labels is null || request.Labels.Count == 0)
        {
            return new CompileResult(null, JobErrorCodes.InvalidRequest, "缺少 labels（至少一张）。", null);
        }

        var commands = new List<string>(request.Labels.Count);
        var options = new LabelCommandCompileOptions(_dpi, _pluginContext);
        for (var i = 0; i < request.Labels.Count; i++)
        {
            var label = request.Labels[i];
            var data = label.Data ?? new Dictionary<string, string>();
            var validation = LabelValidator.Validate(request.Template.Contract, data);
            if (!validation.IsValid)
            {
                var problem = validation.Problems[0];
                return new CompileResult(null, problem.Code, problem.Message, problem.FieldKey);
            }

            var document = new LabelDocument
            {
                Layout = request.Template.Layout,
                Data = data,
            };

            LabelCommandCompileResult result;
            try
            {
                result = await compiler.CompileAsync(document, options, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 意外异常映射为同一失败语义（不透出堆栈）
                return new CompileResult(
                    null,
                    JobErrorCodes.CommandCompileFailed,
                    $"{JobErrorCodes.CommandCompileFailed} 插件 {pluginId} 编译器异常：{ex.Message}。可将打印方式切回图片后重试。",
                    null);
            }

            if (string.IsNullOrWhiteSpace(result.Command))
            {
                // 预期内失败走结果；消息含问题码与中文原因（Server 路由回报 Failed 只带文本）
                var code = string.IsNullOrWhiteSpace(result.ErrorCode) ? JobErrorCodes.CommandCompileFailed : result.ErrorCode;
                var reason = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "编译器未产出指令" : result.ErrorMessage;
                return new CompileResult(
                    null,
                    code,
                    $"{code} 插件 {pluginId} 指令编译失败：{reason}。可将打印方式切回图片后重试。",
                    result.FieldKey);
            }

            commands.Add(result.Command);
        }

        return new CompileResult(commands, null, null, null);
    }

    /// <summary>Log 模拟打印：保存 PNG 并记录摘要。</summary>
    private void SaveLogPrintImages(string jobId, IReadOnlyList<RenderItem> items)
    {
        try
        {
            var dir = Path.Combine(_printOutputPath, jobId);
            Directory.CreateDirectory(dir);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                File.WriteAllBytes(Path.Combine(dir, $"label-{item.Index + 1}.png"), LabelBitmapPng.ToPng(item.Bitmap));
            }

            _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 模拟打印（Log）：作业 {jobId} 已保存 {items.Count} 张 PNG 到 {dir}");
            _hostLogWriter.Flush();
        }
        catch (Exception ex)
        {
            _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 模拟打印（Log）：作业 {jobId} 保存 PNG 失败：{ex.Message}");
            _hostLogWriter.Flush();
        }
    }

    /// <summary>Log 模拟打印：留痕作业数据（格式化见 JobDataLogFormatter）；失败只记一行原因不影响打印链路。</summary>
    private void LogJobData(string jobId, SubmitJobRequest request)
    {
        try
        {
            var labels = request.Labels?.Select(label => label.Data).ToList();
            var lines = JobDataLogFormatter.Format(jobId, request.TemplateName ?? request.Template?.Name, labels);
            foreach (var line in lines)
            {
                _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}");
            }

            _hostLogWriter.Flush();
        }
        catch (Exception ex)
        {
            _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 模拟打印（Log）：作业 {jobId} 数据留痕失败：{ex.Message}");
            _hostLogWriter.Flush();
        }
    }

    private async Task<IReadOnlyDictionary<string, byte[]>> LoadTemplateImagesAsync(TemplateDto template, CancellationToken cancellationToken)
        // 图片资源解析与共享出图端点同源：base64 附带优先、按名回退本地模板库
        => await RenderEndpoints.ResolveImagesAsync(template, _templateStore, cancellationToken);
}
