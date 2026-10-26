namespace LabelFrame.WinHost;

/// <summary>协调模拟 PNG 写入及受管作业目录的时间与容量清理。</summary>
public sealed class PrintImageRetentionCleaner
{
    internal const string MarkerName = ".labelframe-print-v1";
    private readonly string _printOutputPath;
    private readonly int _retentionDays;
    private readonly long _maxBytes;
    private readonly TextWriter _hostLogWriter;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private DateTimeOffset? _lastScan;

    /// <summary>创建模拟出图资源管理器。</summary>
    public PrintImageRetentionCleaner(string printOutputPath, int retentionDays, TextWriter hostLogWriter,
        int maxSizeMB = 512, TimeProvider? timeProvider = null)
    {
        _printOutputPath = Path.GetFullPath(printOutputPath);
        _retentionDays = retentionDays;
        _maxBytes = maxSizeMB > 0 ? (long)maxSizeMB * 1024 * 1024 : 0;
        _hostLogWriter = hostLogWriter;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>保存一个新模拟作业的 PNG；失败仅留痕。</summary>
    public void SaveImages(string jobId, IEnumerable<byte[]> images)
    {
        lock (_gate)
        {
            try
            {
                if (!Guid.TryParseExact(jobId, "N", out _) || !IsSafePath(_printOutputPath))
                    throw new IOException("出图路径或作业标识不安全。");
                Directory.CreateDirectory(_printOutputPath);
                var directory = Path.Combine(_printOutputPath, jobId);
                if (Directory.Exists(directory) || File.Exists(directory))
                    throw new IOException("出图目录已存在，不覆盖已有资源。");
                Directory.CreateDirectory(directory);
                if (!IsSafePath(directory)) throw new IOException("出图目录包含链接。");
                using (var marker = new StreamWriter(new FileStream(Path.Combine(directory, MarkerName), FileMode.CreateNew)))
                    marker.Write(jobId);
                var count = 0;
                foreach (var png in images)
                {
                    using var file = new FileStream(Path.Combine(directory, $"label-{++count}.png"), FileMode.CreateNew);
                    file.Write(png);
                }
                Log($"模拟打印（Log）：作业 {jobId} 已保存 {count} 张 PNG 到 {directory}");
            }
            catch (Exception ex)
            {
                Log($"模拟打印（Log）：作业 {jobId} 保存 PNG 失败：{ex.Message}");
            }
        }
    }

    /// <summary>清理受管图片目录，可选限制提交触发的扫描频率。</summary>
    public void CleanupExpired(bool throttle = false)
    {
        if ((_retentionDays <= 0 && _maxBytes <= 0) || !Monitor.TryEnter(_gate)) return;
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (throttle && _lastScan is { } last && now - last < TimeSpan.FromMinutes(5)) return;
            _lastScan = now;
            if (!Directory.Exists(_printOutputPath) || !IsSafePath(_printOutputPath)) return;
            var directories = new List<ManagedDirectory>();
            foreach (var path in Directory.EnumerateDirectories(_printOutputPath))
            {
                try
                {
                    if (Inspect(path) is { } entry) directories.Add(entry);
                }
                catch (Exception ex)
                {
                    Log($"出图目录清理：检查失败：{path}（{ex.Message}）");
                }
            }
            var total = directories.Sum(d => d.Size);
            var deleted = 0;
            foreach (var entry in directories.OrderBy(d => d.Modified).ThenBy(d => d.Path, StringComparer.Ordinal))
            {
                var expired = _retentionDays > 0 && now - entry.Modified > TimeSpan.FromDays(_retentionDays);
                if (!expired && (_maxBytes <= 0 || total <= _maxBytes)) continue;
                try
                {
                    // 不递归删除，复核归属及文件集合，防止混放内容被一并清理。
                    var current = Inspect(entry.Path);
                    if (current is null || current.Modified != entry.Modified || current.Size != entry.Size) continue;
                    foreach (var file in current.Files.Where(f => Path.GetFileName(f) != MarkerName).Order(StringComparer.Ordinal)) File.Delete(file);
                    File.Delete(Path.Combine(entry.Path, MarkerName));
                    Directory.Delete(entry.Path, recursive: false);
                    total -= entry.Size;
                    deleted++;
                }
                catch (Exception ex)
                {
                    Log($"出图目录清理：删除超期或超量作业目录失败：{entry.Path}（{ex.Message}）");
                    try
                    {
                        // 部分删除会刷新目录时间，必须保留原判龄，避免失败残留再等待一个保留周期。
                        if (Inspect(entry.Path) is not null)
                            Directory.SetLastWriteTimeUtc(entry.Path, entry.Modified.UtcDateTime);
                    }
                    catch (Exception retryEx)
                    {
                        Log($"出图目录清理：保留原判龄失败：{entry.Path}（{retryEx.Message}）");
                    }
                }
            }
            if (deleted > 0) Log($"出图目录清理：删除受管作业目录 {deleted} 个（根目录 {_printOutputPath}）");
            if (_maxBytes > 0 && total > _maxBytes) Log("出图目录清理：受管图片仍超过容量目标，下次扫描重试。");
        }
        catch (Exception ex)
        {
            Log($"出图目录清理失败（不影响打印）：{ex.Message}");
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private static ManagedDirectory? Inspect(string path)
    {
        var name = Path.GetFileName(path);
        if (!Guid.TryParseExact(name, "N", out _) || !IsSafePath(path)) return null;
        var files = Directory.GetFileSystemEntries(path);
        long size = 0;
        var hasMarker = false;
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return null;
            if (info.Name == MarkerName)
            {
                if (info.Length != 32 || File.ReadAllText(file) != name) return null;
                hasMarker = true;
            }
            else if (!IsImageName(info.Name)) return null;
            size = checked(size + info.Length);
        }
        return hasMarker ? new(path, Directory.GetLastWriteTimeUtc(path), size, files) : null;
    }

    private static bool IsImageName(string name)
        => name.StartsWith("label-", StringComparison.Ordinal) && name.EndsWith(".png", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(6, name.Length - 10), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var index) && index > 0;

    private static bool IsSafePath(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null || (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)) return false;
        return true;
    }

    private void Log(string message)
    {
        try
        {
            _hostLogWriter.WriteLine($"[{_timeProvider.GetLocalNow():yyyy-MM-dd HH:mm:ss}] {message}");
            _hostLogWriter.Flush();
        }
        catch
        {
            // 日志写入失败不阻断打印。
        }
    }

    private sealed record ManagedDirectory(string Path, DateTimeOffset Modified, long Size, string[] Files);
}
