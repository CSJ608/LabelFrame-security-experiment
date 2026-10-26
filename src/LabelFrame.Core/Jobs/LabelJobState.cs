namespace LabelFrame.Core.Jobs;

/// <summary>不含打印载荷的内部作业状态快照。</summary>
internal sealed record LabelJobState(
    string Id, string RequestId, LabelJobStatus Status, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, IReadOnlyList<LabelJobItemState> Items)
{
    internal static LabelJobState From(LabelJob job) => new(
        job.Id, job.RequestId, job.Status, job.CreatedAt, job.UpdatedAt,
        job.Items.Select(i => new LabelJobItemState(i.Id, i.Index, i.Status, i.ErrorCode, i.ErrorMessage)).ToArray());
}

/// <summary>不含打印载荷的内部逐张状态。</summary>
internal sealed record LabelJobItemState(
    string Id, int Index, LabelJobItemStatus Status, string? ErrorCode, string? ErrorMessage);
