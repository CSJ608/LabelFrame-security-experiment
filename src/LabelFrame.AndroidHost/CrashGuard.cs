using Android.App;
using Android.OS;
using Android.Util;

namespace LabelFrame.AndroidHost;

/// <summary>全局崩溃捕获：.NET AppDomain.UnhandledException + Java 层默认未捕获异常处理器，
/// 堆栈入 HostLog；崩溃摘要落应用私有目录（{FilesDir}/crash/，保留最近 3 份）供下次启动取证。</summary>
public static class CrashGuard
{
    /// <summary>崩溃摘要目录内保留的文件数上限。</summary>
    private const int MaxRetainedSummaries = 3;

    private static int _registered;
    private static int _summaryWritten;

    /// <summary>注册崩溃处理器（幂等，进程内只生效一次）。注册时机越早越好：
    /// 应用进程创建首行 + 服务 OnCreate 首行兜底——晚于此的早期失败不在捕获窗口内。</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var detail = e.ExceptionObject is Exception ex ? ex.ToString() : e.ExceptionObject?.ToString() ?? "（无异常对象）";
            HostLog.Error(HostLog.Tags.Crash, "未捕获托管异常，进程即将终止。", detail);
            WriteSummary("dotnet", detail);
        };

        Java.Lang.Thread.DefaultUncaughtExceptionHandler =
            new UncaughtHandler(Java.Lang.Thread.DefaultUncaughtExceptionHandler);
    }

    /// <summary>服务启动时调用：上次运行留有崩溃摘要则提示（证明「下次启动可读」并引导取证）。</summary>
    public static void NotePreviousRunCrash()
    {
        try
        {
            var newest = ListSummaries().OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();
            if (newest is not null)
            {
                HostLog.Warn(HostLog.Tags.Crash, $"检测到上次运行异常退出的崩溃摘要：{Path.GetFileName(newest)}（应用私有目录 crash/ 下，可 adb pull 取证）。");
            }
        }
        catch
        {
            // 检测失败不影响启动
        }
    }

    /// <summary>写崩溃摘要文件（单份：托管 / Java 双通道只落第一次——托管异常最终也会以 Java 形态到达；
    /// 失败静默，只剩 logcat + 滚动日志）。</summary>
    private static void WriteSummary(string source, string stack)
    {
        if (Interlocked.Exchange(ref _summaryWritten, 1) == 1)
        {
            return;
        }

        try
        {
            var filesDir = Application.Context.FilesDir?.AbsolutePath;
            if (string.IsNullOrEmpty(filesDir))
            {
                return;
            }

            var directory = Path.Combine(filesDir, "crash");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var content =
                "LabelFrame AndroidHost 崩溃摘要\n" +
                $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}\n" +
                $"来源：{source}（dotnet = 托管未捕获异常；java = Java 层未捕获异常）\n" +
                $"版本：{HostInfo.GetVersion(Application.Context)}\n" +
                $"系统：Android {Build.VERSION.Release}（SDK {Build.VERSION.SdkInt}）\n" +
                $"设备：{Build.Manufacturer} {Build.Model}\n" +
                "---- 堆栈 ----\n" +
                stack + "\n";
            File.WriteAllText(path, content);
            RetainLimit(directory);
        }
        catch
        {
            // 摘要写失败时仅剩 logcat + 滚动日志通道
        }
    }

    private static void RetainLimit(string directory)
    {
        var files = ListSummaries(directory);
        foreach (var stale in files.OrderBy(f => f, StringComparer.Ordinal).Take(Math.Max(0, files.Length - MaxRetainedSummaries)))
        {
            try
            {
                File.Delete(stale);
            }
            catch
            {
                // 删除失败不影响本次崩溃记录
            }
        }
    }

    private static string[] ListSummaries(string? directory = null)
    {
        directory ??= Application.Context.FilesDir?.AbsolutePath is { Length: > 0 } filesDir
            ? Path.Combine(filesDir, "crash")
            : null;
        return directory is not null && Directory.Exists(directory)
            ? Directory.GetFiles(directory, "crash-*.txt")
            : [];
    }

    /// <summary>Java 层未捕获异常处理器：记录后交回原处理器（保持系统默认崩溃流程）。</summary>
    private sealed class UncaughtHandler : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
    {
        private readonly Java.Lang.Thread.IUncaughtExceptionHandler? _previous;

        public UncaughtHandler(Java.Lang.Thread.IUncaughtExceptionHandler? previous) => _previous = previous;

        public void UncaughtException(Java.Lang.Thread? thread, Java.Lang.Throwable? throwable)
        {
            try
            {
                var stack = throwable is null ? "（无异常对象）" : Log.GetStackTraceString(throwable);
                // 线程名缺省时退回 Thread.toString()：Java.Lang.Thread.Id 属性低版本不可用
                var where = thread is null ? "未知线程" : thread.Name ?? thread.ToString();
                HostLog.Error(HostLog.Tags.Crash, $"Java 未捕获异常（{where}），进程即将终止。", stack);
                WriteSummary("java", stack);
            }
            catch
            {
                // 记录失败也要走系统崩溃流程
            }
            finally
            {
                // 绑定接口形参按非空声明，此处系统回调可能为 null，以 null 容忍传递
                _previous?.UncaughtException(thread!, throwable!);
            }
        }
    }
}
