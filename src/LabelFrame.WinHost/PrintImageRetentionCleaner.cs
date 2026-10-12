namespace LabelFrame.WinHost;

/// <summary>
/// 出图目录保留清理（口径对齐 按日保留）：Log 模拟打印每个作业把
/// 渲染 PNG 落盘到 print\&lt;jobId&gt;\，此前无任何清理机制、长期联调机器无限累积——本清理器删除
/// 超期作业子目录（含内部 PNG）。判龄 = 作业子目录 LastWriteTime（与 jobs.db 解耦）；
/// 保留天数 ≤0 = 不清理（关闭语义）。触发时机 = 客户端启动 + 每次模拟打印落盘后顺带执行，
/// 不新增常驻后台任务。整体永不抛出：遇目录被占用 / 权限失败降级留痕（宿主日志一行），
/// 不影响打印与出图主链路（下次触发再试）。
/// </summary>
public sealed class PrintImageRetentionCleaner
{
    private readonly string _printOutputPath;
    private readonly int _retentionDays;
    private readonly TextWriter _hostLogWriter;

    /// <summary>创建出图目录保留清理器。</summary>
    /// <param name="printOutputPath">出图根目录（print\，其下每个子目录 = 一个作业）。</param>
    /// <param name="retentionDays">按天保留（默认 31；0 或负值 = 不清理）。</param>
    /// <param name="hostLogWriter">宿主日志写入器（清理摘要与降级留痕）。</param>
    public PrintImageRetentionCleaner(string printOutputPath, int retentionDays, TextWriter hostLogWriter)
    {
        _printOutputPath = printOutputPath;
        _retentionDays = retentionDays;
        _hostLogWriter = hostLogWriter;
    }

    /// <summary>
    /// 执行一次清理：删除 LastWriteTime 早于保留期的作业子目录（递归含内部 PNG）。
    /// 永不抛出；单个目录删除失败只留痕，不影响其余目录与调用方。
    /// </summary>
    public void CleanupExpired()
    {
        if (_retentionDays <= 0)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(_printOutputPath))
            {
                return;
            }

            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            var deleted = 0;
            foreach (var directory in Directory.EnumerateDirectories(_printOutputPath))
            {
                try
                {
                    if (Directory.GetLastWriteTime(directory) >= cutoff)
                    {
                        continue;
                    }

                    Directory.Delete(directory, recursive: true);
                    deleted++;
                }
                catch (Exception ex)
                {
                    // 目录被占用 / 权限失败：降级留痕，不抛出、不中断其余目录的清理
                    Log($"出图目录清理：删除超期作业目录失败：{directory}（{ex.Message}）");
                }
            }

            if (deleted > 0)
            {
                Log($"出图目录清理：删除超期作业目录 {deleted} 个（保留 {_retentionDays} 天，根目录 {_printOutputPath}）");
            }
        }
        catch (Exception ex)
        {
            // 枚举失败（根目录不可读等）：留痕后静默返回，不影响启动与打印链路
            Log($"出图目录清理失败（不影响打印与出图）：{ex.Message}");
        }
    }

    private void Log(string message)
    {
        try
        {
            _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            _hostLogWriter.Flush();
        }
        catch
        {
            // 日志写入失败不抛出（与宿主日志口径一致）
        }
    }
}
