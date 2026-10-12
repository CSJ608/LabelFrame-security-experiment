using Android.App;
using Android.Util;

namespace LabelFrame.AndroidHost;

/// <summary>轻量静态日志门面：logcat（tag 前缀 LabelFrame.）+ 应用私有目录本地滚动文件
/// （{FilesDir}/logs/host-yyyyMMdd-NNN.log，按日期 + 序号滚动限量保留）；
/// 写失败一律静默——日志设施自身绝不影响业务。</summary>
public static class HostLog
{
    /// <summary>埋点区域短名（完整 tag = <c>LabelFrame.</c> 前缀 + 区域名）。</summary>
    public static class Tags
    {
        /// <summary>前台服务 / 应用生命周期。</summary>
        public const string Host = "Host";

        /// <summary>本地 HTTP 请求处理。</summary>
        public const string Http = "Http";

        /// <summary>打印循环。</summary>
        public const string Print = "Print";

        /// <summary>Server 轮询 / 回报循环。</summary>
        public const string Server = "Server";

        /// <summary>配置页（Activity）交互。</summary>
        public const string Ui = "Ui";

        /// <summary>外置插件通道（加载 / 安装 / 品牌路由）。</summary>
        public const string Plugin = "Plugin";

        /// <summary>全局崩溃捕获。</summary>
        public const string Crash = "Crash";
    }

    private const string TagPrefix = "LabelFrame.";

    /// <summary>单个滚动文件大小上限（超出换下一序号文件）。</summary>
    private const int MaxFileBytes = 512 * 1024;

    /// <summary>目录内最多保留的滚动文件数。</summary>
    private const int MaxRetainedFiles = 6;

    /// <summary>logcat 单行安全上限（官方约 4KB，留余量后分片）。</summary>
    private const int MaxLogcatChunk = 3500;

    private static readonly object FileLock = new();

    /// <summary>常规信息（生命周期、启动参数等）。</summary>
    public static void Info(string tag, string message) => Write(LogPriority.Info, tag, message, null);

    /// <summary>可恢复异常（周期性重试的失败：打印发送、服务器通讯等）。</summary>
    public static void Warn(string tag, string message) => Write(LogPriority.Warn, tag, message, null);

    /// <summary>错误（请求处理失败、崩溃等）；<paramref name="stack"/> 为完整堆栈文本（<c>ex.ToString()</c>）。</summary>
    public static void Error(string tag, string message, string? stack = null) =>
        Write(LogPriority.Error, tag, message, stack);

    private static void Write(LogPriority priority, string tag, string message, string? stack)
    {
        try
        {
            WriteLogcat(priority, tag, message, stack);
        }
        catch
        {
            // 写失败一律静默：日志设施绝不影响业务
        }

        try
        {
            AppendFile(priority, tag, message, stack);
        }
        catch
        {
            // 同上：静默
        }
    }

    private static void WriteLogcat(LogPriority priority, string tag, string message, string? stack)
    {
        var fullTag = TagPrefix + tag;
        var text = stack is null ? message : message + "\n" + stack;

        if (text.Length <= MaxLogcatChunk)
        {
            Log.WriteLine(priority, fullTag, text);
            return;
        }

        // logcat 单行上限内分片，序号 [i/N] 供拼接还原长堆栈
        var chunks = (text.Length + MaxLogcatChunk - 1) / MaxLogcatChunk;
        for (var i = 0; i < chunks; i++)
        {
            var part = text.Substring(i * MaxLogcatChunk, Math.Min(MaxLogcatChunk, text.Length - i * MaxLogcatChunk));
            Log.WriteLine(priority, fullTag, $"[{i + 1}/{chunks}] {part}");
        }
    }

    private static void AppendFile(LogPriority priority, string tag, string message, string? stack)
    {
        lock (FileLock)
        {
            var directory = ResolveLogDirectory();
            if (directory is null)
            {
                return;
            }

            var file = ResolveWritableFile(directory);
            var level = priority switch
            {
                LogPriority.Info => "I",
                LogPriority.Warn => "W",
                LogPriority.Error => "E",
                _ => "?",
            };
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {tag,-6} {message}\n";
            if (stack is not null)
            {
                line += stack + "\n";
            }

            File.AppendAllText(file, line);
            RetainLimit(directory);
        }
    }

    /// <summary>日志目录（应用私有 <c>{FilesDir}/logs</c>）；创建失败返回 null（仅剩 logcat）。</summary>
    private static string? ResolveLogDirectory()
    {
        var filesDir = Application.Context.FilesDir?.AbsolutePath;
        if (string.IsNullOrEmpty(filesDir))
        {
            return null;
        }

        var directory = Path.Combine(filesDir, "logs");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>当天序号文件：首个未超上限的（不足则新建）；序号耗尽时覆写最后一个（理论到不了）。</summary>
    private static string ResolveWritableFile(string directory)
    {
        string last = Path.Combine(directory, $"host-{DateTime.Now:yyyyMMdd}-001.log");
        for (var i = 1; i <= 99; i++)
        {
            last = Path.Combine(directory, $"host-{DateTime.Now:yyyyMMdd}-{i:D3}.log");
            if (!File.Exists(last) || new FileInfo(last).Length < MaxFileBytes)
            {
                return last;
            }
        }

        return last;
    }

    /// <summary>按名字序删除最旧文件，保留最近 <see cref="MaxRetainedFiles"/> 个（日期 + 序号命名即时间序）。</summary>
    private static void RetainLimit(string directory)
    {
        var files = Directory.GetFiles(directory, "host-*.log");
        if (files.Length <= MaxRetainedFiles)
        {
            return;
        }

        foreach (var stale in files.OrderBy(f => f, StringComparer.Ordinal).Take(files.Length - MaxRetainedFiles))
        {
            try
            {
                File.Delete(stale);
            }
            catch
            {
                // 删除失败不影响写日志
            }
        }
    }
}
