using LabelFrame.Server.Security;
using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace LabelFrame.Server.Tests;

public sealed class AdminPasswordCommandTests
{
    [Theory]
    [InlineData("synthetic-long-password")]
    [InlineData(" 中文 密码 ")]
    public void Generated_hash_matches_framework_without_echo(string password)
    {
        var (code, output, error) = Run(password + "\r" + password + "\r");
        Assert.Equal(0, code);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<string>().VerifyHashedPassword("admin", output.Trim(), password));
        Assert.DoesNotContain(password, error);
        Assert.DoesNotContain(output.Trim(), error);
        Assert.Single(output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Salt_changes_and_backspace_preserves_exact_password()
    {
        var first = Run("abx\bcd\rabcd\r");
        var second = Run("abcd\rabcd\r");
        Assert.Equal(0, first.Code);
        Assert.Equal(0, second.Code);
        Assert.NotEqual(first.Output, second.Output);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<string>().VerifyHashedPassword("admin", first.Output.Trim(), "abcd"));
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("   \r")]
    [InlineData("abc\rdef\r")]
    [InlineData("\u001b")]
    [InlineData("abc\r\u001b")]
    public void Invalid_or_cancelled_input_produces_no_hash(string keys)
    {
        var result = Run(keys);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
    }

    [Fact]
    public void Maximum_length_is_accepted_but_overflow_is_rejected()
    {
        var password = new string('x', 1024);
        Assert.Equal(0, Run(password + "\r" + password + "\r").Code);
        var result = Run(password + "x");
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Unsafe_console_conditions_do_not_read(bool inputRedirected, bool outputRedirected)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, AdminPasswordCommand.Run([AdminPasswordCommand.Name], inputRedirected, outputRedirected,
            () => throw new Xunit.Sdk.XunitException("不得读取"), output, error));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Extra_arguments_are_rejected_without_echo()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, AdminPasswordCommand.Run([AdminPasswordCommand.Name, "synthetic-secret-argument"], false, true,
            () => throw new Xunit.Sdk.XunitException("不得读取"), output, error));
        Assert.Empty(output.ToString());
        Assert.DoesNotContain("synthetic-secret-argument", error.ToString());
    }

    [Fact]
    public void Console_failure_has_fixed_error_and_no_hash()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(3, AdminPasswordCommand.Run([AdminPasswordCommand.Name], false, true,
            () => throw new IOException("synthetic-sensitive-detail"), output, error));
        Assert.Empty(output.ToString());
        Assert.DoesNotContain("synthetic-sensitive-detail", error.ToString());
    }

    [Fact]
    public void Output_failure_is_not_reported_as_success()
    {
        using var error = new StringWriter();
        using var output = new FailingWriter();
        var keys = new Queue<char>("abc\rabc\r");
        Assert.Equal(3, AdminPasswordCommand.Run([AdminPasswordCommand.Name], false, true, () =>
        {
            var value = keys.Dequeue();
            return new ConsoleKeyInfo(value, value == '\r' ? ConsoleKey.Enter : ConsoleKey.A, false, false, false);
        }, output, error));
        Assert.DoesNotContain("synthetic-output-detail", error.ToString());
        Assert.DoesNotContain("哈希已写入", error.ToString());
    }

    [Theory]
    [InlineData("--hash-admin-password")]
    [InlineData("--hash-admin-password=synthetic-secret")]
    public async Task Redirected_child_rejects_before_host_initialization(string argument)
    {
        using var isolation = new IsolatedServerEnvironment();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = isolation.DirectoryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(AdminSecurity).Assembly.Location);
        start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(2, process.ExitCode);
            Assert.Empty(await output);
            Assert.DoesNotContain("synthetic-secret", await error);
            Assert.Empty(Directory.EnumerateFileSystemEntries(isolation.DirectoryPath));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private sealed class FailingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("synthetic-output-detail");
    }

    private static (int Code, string Output, string Error) Run(string input)
    {
        var keys = new Queue<char>(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = AdminPasswordCommand.Run([AdminPasswordCommand.Name], false, true, () =>
        {
            var value = keys.Dequeue();
            var key = value switch
            {
                '\r' => ConsoleKey.Enter,
                '\b' => ConsoleKey.Backspace,
                '\u001b' => ConsoleKey.Escape,
                _ => ConsoleKey.A,
            };
            return new ConsoleKeyInfo(value, key, false, false, false);
        }, output, error);
        return (code, output.ToString(), error.ToString());
    }
}
