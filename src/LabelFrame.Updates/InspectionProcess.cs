using System.Diagnostics;
using System.Text;

namespace LabelFrame.Updates;

/// <summary>有时间和输出上限的只读安装包校验工具调用。</summary>
internal static class InspectionProcess
{
    internal static async Task<byte[]> RunAsync(string executable, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["LC_ALL"] = "C";
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidDataException("无法启动安装包校验工具。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        async Task<byte[]> ReadAsync(Stream stream, int maximum)
        {
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (read == 0) return output.ToArray();
                if (output.Length + read > maximum)
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    throw new InvalidDataException("安装包校验输出超出限制。");
                }
                output.Write(buffer, 0, read);
            }
        }
        try
        {
            var output = ReadAsync(process.StandardOutput.BaseStream, 1024 * 1024);
            var errors = ReadAsync(process.StandardError.BaseStream, 65536);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0) throw new InvalidDataException("安装包签名或元数据校验未通过，请检查发布者证书与文件完整性。");
            return await output;
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        }
    }
}
