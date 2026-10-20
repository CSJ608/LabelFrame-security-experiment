namespace LabelFrame.WinHost;

/// <summary>打印设置 DTO：API 请求 / 响应与 print-settings.json 持久化共用形状。<c>dpi</c> 可空——null = 未设置（继承 appsettings / 环境变量配置）。</summary>
public sealed record PrintSettingsDto(bool BatchEnabled, int BatchSize, int BatchIntervalMs, int? Dpi = null);

/// <summary>
/// 打印设置（选项模型）：全局单例，API 线程写、打印 Worker 线程读，
/// 读写统一走 lock 保证跨线程可见性。保存即生效，无需重启。打印 DPI 为 203/300/600 白名单，
/// 未设置时由宿主配置（appsettings WinHost:Dpi / 环境变量 LABELFRAME_DPI）兜底。
/// </summary>
public sealed class PrintSettings
{
    /// <summary>默认：批次作业关闭。</summary>
    public const bool DefaultEnabled = false;

    /// <summary>默认：每批次打印数量。</summary>
    public const int DefaultBatchSize = 10;

    /// <summary>默认：批次打印间隔（毫秒）。</summary>
    public const int DefaultBatchIntervalMs = 500;

    /// <summary>支持的打印 DPI 白名单（标签打印机主流三档分辨率）。</summary>
    public static readonly int[] SupportedDpis = [203, 300, 600];

    private readonly object _gate = new();
    private bool _batchEnabled = DefaultEnabled;
    private int _batchSize = DefaultBatchSize;
    private int _batchIntervalMs = DefaultBatchIntervalMs;
    private int? _dpi;

    /// <summary>默认设置（DPI 未设置 = 继承宿主配置）。</summary>
    public static PrintSettingsDto Defaults => new(DefaultEnabled, DefaultBatchSize, DefaultBatchIntervalMs);

    /// <summary>
    /// 读取 Normalize：缺失 / 损坏 / 越界统一回默认值——
    /// batchSize &lt; 1 → 10、batchIntervalMs &lt; 0 → 500、batchEnabled 非 bool → false、
    /// dpi 不在白名单 → null（继承宿主配置）。
    /// </summary>
    public static PrintSettingsDto Normalize(bool? batchEnabled, int? batchSize, int? batchIntervalMs, int? dpi = null)
        => new(
            batchEnabled ?? DefaultEnabled,
            batchSize is >= 1 ? batchSize.Value : DefaultBatchSize,
            batchIntervalMs is >= 0 ? batchIntervalMs.Value : DefaultBatchIntervalMs,
            dpi is not null && SupportedDpis.Contains(dpi.Value) ? dpi : null);

    /// <summary>保存校验：返回中文原因；合法返回 null。</summary>
    public static string? Validate(int batchSize, int batchIntervalMs, int? dpi = null)
    {
        if (batchSize < 1)
        {
            return "每批次打印数量需 ≥ 1。";
        }

        if (batchIntervalMs < 0)
        {
            return "批次打印间隔需 ≥ 0 毫秒。";
        }

        if (dpi is not null && !SupportedDpis.Contains(dpi.Value))
        {
            return "打印 DPI 仅支持 203、300 或 600。";
        }

        return null;
    }

    /// <summary>读取当前设置快照（线程安全）。</summary>
    public PrintSettingsDto Snapshot()
    {
        lock (_gate)
        {
            return new PrintSettingsDto(_batchEnabled, _batchSize, _batchIntervalMs, _dpi);
        }
    }

    /// <summary>读取快照并把 DPI 解析为生效值（未设置时取宿主配置兜底；GET 响应与打印链路共用口径）。</summary>
    public PrintSettingsDto EffectiveSnapshot(int fallbackDpi)
    {
        lock (_gate)
        {
            return new PrintSettingsDto(_batchEnabled, _batchSize, _batchIntervalMs, _dpi ?? fallbackDpi);
        }
    }

    /// <summary>当前生效打印 DPI（未设置时取宿主配置兜底；打印链路逐次读取，保存即生效）。</summary>
    public int EffectiveDpi(int fallbackDpi)
    {
        lock (_gate)
        {
            return _dpi ?? fallbackDpi;
        }
    }

    /// <summary>保存即生效：整体更新内存中的设置（线程安全）。</summary>
    public void Update(PrintSettingsDto value)
    {
        lock (_gate)
        {
            _batchEnabled = value.BatchEnabled;
            _batchSize = value.BatchSize;
            _batchIntervalMs = value.BatchIntervalMs;
            _dpi = value.Dpi is not null && SupportedDpis.Contains(value.Dpi.Value) ? value.Dpi : null;
        }
    }
}
