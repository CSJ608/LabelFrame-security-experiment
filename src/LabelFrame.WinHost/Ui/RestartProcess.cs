using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace LabelFrame.WinHost.Ui;

/// <summary>同身份启动并等待旧进程退出的重启辅助进程。</summary>
internal static class RestartProcess
{
    public static bool IsWaiter(string[] args) => args.Length > 0 && args[0] == "--restart-wait";

    public static async Task RunWaiterAsync(string[] args)
    {
        try
        {
            if (args.Length != 5 || !int.TryParse(args[1], out var pid) || !long.TryParse(args[2], out var ticks))
                throw new ArgumentException("重启等待参数无效。");
            using var parent = Process.GetProcessById(pid);
            if (parent.StartTime.ToUniversalTime().Ticks != ticks) throw new InvalidOperationException("旧进程身份不符。");
            var nextArgs = JsonSerializer.Deserialize<string[]>(Convert.FromBase64String(args[4]))
                ?? throw new ArgumentException("启动参数为空。");
            using var ready = EventWaitHandle.OpenExisting(args[3]);
            ready.Set();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await parent.WaitForExitAsync(timeout.Token);
            using var process = Process.Start(CreateStartInfo(nextArgs))
                ?? throw new InvalidOperationException("新客户端进程未能启动。");
        }
        catch (Exception ex)
        {
            var message = $"LabelFrame 重启失败：{ex.Message}。请从桌面快捷方式打开客户端。";
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelFrame");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "restart.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch { /* 日志失败时仍向用户展示恢复方法。 */ }
            MessageBox.Show(message, "LabelFrame", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static Task PrepareAsync(string[] originalArgs, bool showWindow, bool showPlugins)
    {
        using var parent = Process.GetCurrentProcess();
        var eventName = $"Local\\LabelFrame.Restart.{Guid.NewGuid():N}";
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var next = originalArgs.Where(a => a is not "--autostart" and not "--restart-plugins").ToList();
        if (!showWindow && !showPlugins) next.Add("--autostart");
        if (showPlugins) next.Add("--restart-plugins");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(next)));
        using var waiter = Process.Start(CreateStartInfo([
            "--restart-wait", parent.Id.ToString(CultureInfo.InvariantCulture), parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), eventName, encoded]))
            ?? throw new InvalidOperationException("无法启动重启等待进程。");
        if (!ready.WaitOne(TimeSpan.FromSeconds(5)))
        {
            try { waiter.Kill(); } catch (InvalidOperationException) { /* 等待器已自行退出。 */ }
            throw new TimeoutException("重启等待进程未就绪，已保留当前客户端。");
        }
        return Task.CompletedTask;
    }

    private static ProcessStartInfo CreateStartInfo(IEnumerable<string> args)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定客户端程序路径。");
        var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }
}
