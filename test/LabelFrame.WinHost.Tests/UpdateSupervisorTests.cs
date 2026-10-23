using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LabelFrame.Updates;
using LabelFrame.WinHost.Updates;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>独立监督进程的提交门与原用户恢复验证。</summary>
[Collection(nameof(NativeUpdateTestGroup))]
public sealed class UpdateSupervisorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task Installer_without_commit_cancels_without_starting_msiexec_or_stopping_parent()
    {
        var root = TemporaryDirectory();
        try
        {
            using var parent = Process.GetCurrentProcess();
            var ready = Path.Combine(root, "ready");
            var resultPath = Path.Combine(root, "result.json");
            await UpdateSupervisor.LaunchAsync(UpdateSupervisor.InstallerScript, new {
                pid = parent.Id, start = parent.StartTime.ToUniversalTime().Ticks, version = "1.5.0", ready,
                arm = Path.Combine(root, "never-committed"), result = resultPath,
                package = Path.Combine(root, "missing.msi"), log = Path.Combine(root, "install.log"),
            }, ready, CancellationToken.None);
            await WaitForFileAsync(resultPath, TimeSpan.FromSeconds(40));
            var result = JsonSerializer.Deserialize<UpdateInstallResult>(await File.ReadAllTextAsync(resultPath), Json);
            Assert.Equal(1602, result?.ExitCode);
            Assert.False(parent.HasExited);
            Assert.False(File.Exists(Path.Combine(root, "install.log")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Relauncher_waits_exact_parent_exit_and_keeps_original_user()
    {
        var root = TemporaryDirectory();
        try
        {
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var release = Path.Combine(root, "parent-release");
            var escapedRelease = release.Replace("'", "''", StringComparison.Ordinal);
            using var parent = StartPowerShell(powershell, $"$end = [DateTime]::UtcNow.AddSeconds(120); while (!(Test-Path -LiteralPath '{escapedRelease}')) {{ if ([DateTime]::UtcNow -gt $end) {{ exit }}; Start-Sleep -Milliseconds 50 }}");
            var result = Path.Combine(root, "result.json");
            await File.WriteAllTextAsync(result, "{}");
            var output = Path.Combine(root, "child.json");
            var escapedOutput = output.Replace("'", "''", StringComparison.Ordinal);
            var script = $"$j = @{{ parentAlive = [bool](Get-Process -Id {parent.Id} -ErrorAction SilentlyContinue); user = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value; childProcessId = $PID }} | ConvertTo-Json; [IO.File]::WriteAllText('{escapedOutput}.tmp', $j); [IO.File]::Move('{escapedOutput}.tmp', '{escapedOutput}')";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var ready = Path.Combine(root, "ready");
            await UpdateSupervisor.LaunchAsync(UpdateSupervisor.RelaunchScript, new { pid = parent.Id,
                start = parent.StartTime.ToUniversalTime().Ticks, ready, result, executable = powershell,
                arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                workingDirectory = root, failure = Path.Combine(root, "failure.txt") }, ready, CancellationToken.None);
            Assert.False(parent.HasExited);
            Assert.False(File.Exists(output));
            await File.WriteAllTextAsync(release, "exit");
            await WaitForFileAsync(output, TimeSpan.FromSeconds(40));
            using var child = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            Assert.False(child.RootElement.GetProperty("parentAlive").GetBoolean());
            using var identity = WindowsIdentity.GetCurrent();
            Assert.Equal(identity.User?.Value, child.RootElement.GetProperty("user").GetString());
            Assert.False(File.Exists(Path.Combine(root, "failure.txt")));
            try
            {
                using var launched = Process.GetProcessById(child.RootElement.GetProperty("childProcessId").GetInt32());
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await launched.WaitForExitAsync(deadline.Token);
            }
            catch (ArgumentException) { }
        }
        finally { Directory.Delete(root, true); }
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "lf-update-supervisor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static Process StartPowerShell(string executable, string script)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }
    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!File.Exists(path))
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("监督进程没有输出验证结果。");
            await Task.Delay(100);
        }
    }
}

/// <summary>串行运行原生更新验证，避免进程冷启动干扰打印时序回归。</summary>
[CollectionDefinition(nameof(NativeUpdateTestGroup), DisableParallelization = true)]
public sealed class NativeUpdateTestGroup { }
