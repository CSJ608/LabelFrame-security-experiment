namespace LabelFrame.AndroidHost;

/// <summary>宿主运行状态快照（配置页 / 状态页 / 通知共用）：不可变快照整体原子替换，读侧永远看到一致的一帧。</summary>
public static class HostStatus
{
    /// <summary>某一时刻的宿主状态。</summary>
    public sealed record Snapshot(
        bool ServiceRunning,
        string ActiveServerUrl,
        string ActivePrinterEndpoint,
        DateTime? LastServerContactUtc,
        string? LastServerError,
        DateTime? LastPrintUtc,
        string? LastPrintError);

    private static Snapshot _current = new(false, string.Empty, string.Empty, null, null, null, null);

    /// <summary>当前状态快照。</summary>
    public static Snapshot Current => _current;

    /// <summary>服务启动：记录生效配置。</summary>
    public static void NoteServiceStarted(string serverUrl, string printerEndpoint) =>
        Replace(s => s with
        {
            ServiceRunning = true,
            ActiveServerUrl = serverUrl,
            ActivePrinterEndpoint = printerEndpoint,
        });

    /// <summary>服务停止。</summary>
    public static void NoteServiceStopped() =>
        Replace(s => s with { ServiceRunning = false });

    /// <summary>服务端通讯成功（注册 / 长轮询 / 领取任一成功即视为可达）。</summary>
    public static void NoteServerContact() =>
        Replace(s => s with { LastServerContactUtc = DateTime.UtcNow, LastServerError = null });

    /// <summary>服务端通讯异常。</summary>
    public static void NoteServerError(string message) =>
        Replace(s => s with { LastServerError = message });

    /// <summary>打印机发送成功。</summary>
    public static void NotePrintSuccess() =>
        Replace(s => s with { LastPrintUtc = DateTime.UtcNow, LastPrintError = null });

    /// <summary>打印机发送失败。</summary>
    public static void NotePrintError(string message) =>
        Replace(s => s with { LastPrintError = message });

    private static void Replace(Func<Snapshot, Snapshot> update) => _current = update(_current);
}
