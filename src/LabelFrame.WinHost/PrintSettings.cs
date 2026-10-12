namespace LabelFrame.WinHost;

/// <summary>批次作业设置 DTO：API 请求 / 响应与 print-settings.json 持久化共用形状。</summary>
public sealed record PrintSettingsDto(bool BatchEnabled, int BatchSize, int BatchIntervalMs);

/// <summary>
/// 批次作业设置（选项模型）：全局单例，API 线程写、打印 Worker 线程读，
/// 读写统一走 lock 保证跨线程可见性（评审 结论）。保存即生效，无需重启。
/// </summary>
public sealed class PrintSettings
{
    /// <summary>默认：批次作业关闭。</summary>
    public const bool DefaultEnabled = false;

    /// <summary>默认：每批次打印数量。</summary>
    public const int DefaultBatchSize = 10;

    /// <summary>默认：批次打印间隔（毫秒）。</summary>
    public const int DefaultBatchIntervalMs = 500;

    private readonly object _gate = new();
    private bool _batchEnabled = DefaultEnabled;
    private int _batchSize = DefaultBatchSize;
    private int _batchIntervalMs = DefaultBatchIntervalMs;

    /// <summary>默认设置。</summary>
    public static PrintSettingsDto Defaults => new(DefaultEnabled, DefaultBatchSize, DefaultBatchIntervalMs);

    /// <summary>
    /// 读取 Normalize：缺失 / 损坏 / 越界统一回默认值——
    /// <c>batchSize &lt; 1 → 10</c>、<c>batchIntervalMs &lt; 0 → 500</c>、<c>batchEnabled</c> 非 bool → <c>false</c>。
    /// </summary>
    public static PrintSettingsDto Normalize(bool? batchEnabled, int? batchSize, int? batchIntervalMs)
        => new(
            batchEnabled ?? DefaultEnabled,
            batchSize is >= 1 ? batchSize.Value : DefaultBatchSize,
            batchIntervalMs is >= 0 ? batchIntervalMs.Value : DefaultBatchIntervalMs);

    /// <summary>保存校验：返回中文原因；合法返回 null。</summary>
    public static string? Validate(int batchSize, int batchIntervalMs)
    {
        if (batchSize < 1)
        {
            return "每批次打印数量需 ≥ 1。";
        }

        if (batchIntervalMs < 0)
        {
            return "批次打印间隔需 ≥ 0 毫秒。";
        }

        return null;
    }

    /// <summary>读取当前设置快照（线程安全）。</summary>
    public PrintSettingsDto Snapshot()
    {
        lock (_gate)
        {
            return new PrintSettingsDto(_batchEnabled, _batchSize, _batchIntervalMs);
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
        }
    }
}
