namespace LabelFrame.Core.Jobs;

/// <summary>作业中的单张标签：批内顺序 + 逐张状态 + 不可变打印机指令。</summary>
public sealed class LabelJobItem
{
    /// <summary>Item 标识。</summary>
    public required string Id { get; init; }

    /// <summary>所属作业标识。</summary>
    public required string JobId { get; init; }

    /// <summary>批内序号（0 起，按序打印）。</summary>
    public int Index { get; init; }

    /// <summary>逐张状态。</summary>
    public LabelJobItemStatus Status { get; set; } = LabelJobItemStatus.Pending;

    /// <summary>持久化的打印机指令（字段与 DB 列名沿用 zpl 不改名——存量库兼容；图片模式 = ^GF 位图 ZPL，原生指令模式 = 插件编译的品牌原生指令；不可变，重启后可续打且不重打）。</summary>
    public required string Zpl { get; init; }

    /// <summary>失败问题码。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>失败原因（中文）。</summary>
    public string? ErrorMessage { get; set; }
}