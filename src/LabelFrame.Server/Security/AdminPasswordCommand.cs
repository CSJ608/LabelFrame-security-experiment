using Microsoft.AspNetCore.Identity;

namespace LabelFrame.Server.Security;

/// <summary>在启动服务前交互生成管理员密码哈希。</summary>
internal static class AdminPasswordCommand
{
    internal const string Name = "--hash-admin-password";

    internal static int Run(string[] args, bool inputRedirected, bool outputRedirected,
        Func<ConsoleKeyInfo> readKey, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length != 1 || args[0] != Name || inputRedirected || !outputRedirected)
            {
                error.WriteLine("请仅使用 --hash-admin-password，在交互终端输入并将标准输出重定向到受限文件；不接受密码参数或管道输入。");
                return 2;
            }
            error.WriteLine("离线生成：请勿录制终端或收集输出日志。输出文件须限制访问；本命令不会配置或启动服务。");
            error.Write("请输入管理员密码（不回显，Esc 取消）：");
            var password = ReadPassword(readKey);
            error.WriteLine();
            if (string.IsNullOrWhiteSpace(password))
            {
                error.WriteLine("已取消或密码无效（要求非空白，最多 1024 个字符）。");
                return 2;
            }
            error.Write("请再次输入密码：");
            var confirmation = ReadPassword(readKey);
            error.WriteLine();
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                error.WriteLine("确认不一致或已取消，未生成哈希。");
                return 2;
            }
            var hash = new PasswordHasher<string>().HashPassword("admin", password);
            output.WriteLine(hash);
            output.Flush();
            error.WriteLine("哈希已写入标准输出；请安全配置 Server:AdminPasswordHash 并安排重启，勿提交或上传该文件。");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // 异常细节可能包含输出目标；错误通道只提供固定文案。
            try { error.WriteLine("控制台输入或输出失败，请检查受限输出文件；未确认生成成功。"); }
            catch (IOException) { }
            return 3;
        }
    }

    private static string? ReadPassword(Func<ConsoleKeyInfo> readKey)
    {
        var buffer = new char[1024];
        var length = 0;
        try
        {
            while (true)
            {
                var key = readKey();
                if (key.Key == ConsoleKey.Escape) return null;
                if (key.Key == ConsoleKey.Enter) return new string(buffer, 0, length);
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (length > 0) buffer[--length] = '\0';
                    continue;
                }
                if (char.IsControl(key.KeyChar)) continue;
                if (length == buffer.Length) return null;
                buffer[length++] = key.KeyChar;
            }
        }
        finally { Array.Clear(buffer); }
    }
}
