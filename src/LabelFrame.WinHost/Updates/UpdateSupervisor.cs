using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LabelFrame.WinHost.Updates;

/// <summary>脱离宿主程序集的固定安装监督与原用户恢复进程。</summary>
internal static class UpdateSupervisor
{
    internal const string InstallerScript = """
        $ErrorActionPreference = 'Stop'
        $d = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__DATA__')) | ConvertFrom-Json
        function Save-Result($code, $reason) {
          if (Test-Path -LiteralPath $d.result) { return }
          $r = @{ exitCode = $code; targetVersion = $d.version; reason = $reason } | ConvertTo-Json -Compress
          [IO.File]::WriteAllText(($d.result + '.runner.tmp'), $r)
          try { [IO.File]::Move(($d.result + '.runner.tmp'), $d.result) }
          catch { if (!(Test-Path -LiteralPath $d.result)) { throw } }
        }
        try {
          [IO.File]::WriteAllText($d.ready, 'ready')
          $deadline = [DateTime]::UtcNow.AddSeconds(15)
          while (!(Test-Path -LiteralPath $d.arm)) {
            if (Test-Path -LiteralPath $d.result) { exit }
            if ([DateTime]::UtcNow -ge $deadline) { Save-Result 1602 '安装未提交，已取消。'; exit }
            Start-Sleep -Milliseconds 100
          }
          $p = Get-Process -Id $d.pid -ErrorAction SilentlyContinue
          if ($p -and $p.StartTime.ToUniversalTime().Ticks -eq $d.start) {
            if (!$p.WaitForExit(30000)) { Save-Result 1602 '客户端未按时退出，未执行安装。'; exit }
          }
          $arguments = '/i "' + $d.package + '" /qn /norestart REBOOT=ReallySuppress /L*v "' + $d.log + '"'
          $p = Start-Process -FilePath ($env:WINDIR + '\System32\msiexec.exe') -ArgumentList $arguments -PassThru -WindowStyle Hidden
          $handle = $p.Handle
          $p.WaitForExit()
          Save-Result $p.ExitCode $null
        } catch { Save-Result 1603 '安装监督失败，请管理员查看受保护的安装日志。' }
        """;

    internal const string RelaunchScript = """
        $ErrorActionPreference = 'Stop'
        $d = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__DATA__')) | ConvertFrom-Json
        function Show-Failure($message) {
          [IO.File]::WriteAllText($d.failure, $message)
          Add-Type -AssemblyName System.Windows.Forms
          [void][Windows.Forms.MessageBox]::Show($message, 'LabelFrame')
        }
        try {
          [IO.File]::WriteAllText($d.ready, 'ready')
          $p = Get-Process -Id $d.pid -ErrorAction SilentlyContinue
          if ($p -and $p.StartTime.ToUniversalTime().Ticks -eq $d.start) {
            if (!$p.WaitForExit(180000)) { exit }
          }
          $deadline = [DateTime]::UtcNow.AddMinutes(20)
          while (!(Test-Path -LiteralPath $d.result)) {
            if ([DateTime]::UtcNow -ge $deadline) {
              Show-Failure '更新安装尚未结束，请联系管理员查看安装日志后手动启动。'
              exit
            }
            Start-Sleep -Milliseconds 500
          }
          $info = New-Object Diagnostics.ProcessStartInfo
          $info.FileName = $d.executable
          $info.Arguments = $d.arguments
          $info.WorkingDirectory = $d.workingDirectory
          $info.UseShellExecute = $false
          [void][Diagnostics.Process]::Start($info)
        } catch { Show-Failure '更新后重新启动失败，请手动打开客户端。' }
        """;

    public static async Task PrepareInstallerAsync(string transaction, int pid, long start, string version, CancellationToken ct)
    {
        var directory = Path.Combine(ProtectedUpdatePaths.Root, transaction);
        var arm = Path.Combine(directory, "install.commit");
        await LaunchAsync(InstallerScript, new { pid, start, version, arm, package = Path.Combine(directory, "package.msi"),
            result = Path.Combine(directory, "result.json"), log = Path.Combine(directory, "install.log"),
            ready = Path.Combine(directory, "installer.ready") }, Path.Combine(directory, "installer.ready"), ct);
        await File.WriteAllTextAsync(arm + ".tmp", "committed", ct);
        File.Move(arm + ".tmp", arm);
    }

    public static Task PrepareRelaunchAsync(string transaction, string[] args, bool visible, string dataDirectory, CancellationToken ct)
    {
        using var process = Process.GetCurrentProcess();
        var ready = Path.Combine(dataDirectory, "relaunch-" + transaction + ".ready");
        var launchArgs = args.Where(a => !a.Equals("--autostart", StringComparison.OrdinalIgnoreCase)
            && !a.Equals("--restart-plugins", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!visible) launchArgs.Add("--autostart");
        return LaunchAsync(RelaunchScript, new { pid = process.Id, start = process.StartTime.ToUniversalTime().Ticks,
            result = Path.Combine(ProtectedUpdatePaths.Root, transaction, "result.json"), ready,
            executable = ProtectedUpdatePaths.Executable, arguments = string.Join(" ", launchArgs.Select(QuoteArgument)),
            workingDirectory = Environment.CurrentDirectory, failure = Path.Combine(dataDirectory, "update-relaunch-error.txt") }, ready, ct);
    }

    internal static async Task LaunchAsync(string template, object data, string ready, CancellationToken ct)
    {
        var script = template.Replace("__DATA__", Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(data)), StringComparison.Ordinal);
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(arg);
        using var child = Process.Start(info) ?? throw new IOException("无法启动更新监督进程。");
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(ready))
        {
            ct.ThrowIfCancellationRequested();
            if (child.HasExited) throw new IOException($"更新监督进程未就绪即退出（{child.ExitCode}），客户端继续运行。");
            if (DateTime.UtcNow >= deadline) throw new IOException("更新监督进程 60 秒内未就绪，客户端继续运行。");
            await Task.Delay(50, ct);
        }
        if (child.HasExited) throw new IOException("更新监督进程在就绪确认前已退出，客户端继续运行。");
    }

    internal static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }
}
