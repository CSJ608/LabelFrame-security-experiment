using System.Text.Json;

namespace LabelFrame.AndroidHost.Updates;

/// <summary>在人工恢复前持久保存原始异常与管理员接受的风险原因。</summary>
public static class RecoveryAudit
{
    public sealed record Receipt(string Id, DateTimeOffset TimeUtc, string Reason);

    public static Receipt Save<T>(string journalPath, string reason, T original)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 512)
            throw new ArgumentException("请填写 1～512 字的人工恢复原因。");
        var receipt = new Receipt(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, reason.Trim());
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(journalPath))!, "recovery-audit");
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { receipt, original });
        using var file = new FileStream(Path.Combine(directory, receipt.Id + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(true);
        return receipt;
    }
}
