using LabelFrame.WinHost;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// Serilog 文件日志装配回归测试：同一装配路径（WinHostApp.BuildAsync 的
/// configureBuilder → SerilogSetup.Apply）构建应用后写事件，断言 app-*.log 创建且含事件——
/// 结构性锚定「配置必须挂在被 Build 的真实 builder 上」，防止装配抽取类重构再次把配置挂死。
/// </summary>
public class SerilogSetupTests
{
    [Fact]
    public async Task Built_app_should_write_startup_event_to_daily_log_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lfserilog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var app = await BuildAppAsync(directory, logLevel: "Information");
            try
            {
                app.Logger.LogInformation("Serilog 回归自检事件：app 文件通道写入验证。");
            }
            finally
            {
                await app.DisposeAsync();
            }

            var today = $"{DateTime.Now:yyyyMMdd}";
            var logFile = Path.Combine(directory, $"app-{today}.log");
            Assert.True(File.Exists(logFile), $"日志文件未创建：{logFile}（目录内容：{string.Join(", ", Directory.GetFiles(directory).Select(Path.GetFileName))}）");
            var content = File.ReadAllText(logFile);
            Assert.Contains("Serilog 回归自检事件", content);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TryCleanup(directory);
        }
    }

    [Fact]
    public async Task Level_warning_should_filter_information_events()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lfserilog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var app = await BuildAppAsync(directory, logLevel: "Warning");
            try
            {
                app.Logger.LogInformation("信息级事件：不应出现在 Warning 级别文件。");
                app.Logger.LogWarning("警告级事件：应出现在文件。");
            }
            finally
            {
                await app.DisposeAsync();
            }

            var logFile = Path.Combine(directory, $"app-{DateTime.Now:yyyyMMdd}.log");
            Assert.True(File.Exists(logFile));
            var content = File.ReadAllText(logFile);
            Assert.DoesNotContain("信息级事件", content);
            Assert.Contains("警告级事件", content);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TryCleanup(directory);
        }
    }

    [Theory]
    [InlineData("Debug", Serilog.Events.LogEventLevel.Debug)]
    [InlineData("debug", Serilog.Events.LogEventLevel.Debug)]
    [InlineData("Warning", Serilog.Events.LogEventLevel.Warning)]
    [InlineData("不合法值", Serilog.Events.LogEventLevel.Information)]
    [InlineData("", Serilog.Events.LogEventLevel.Information)]
    public void ParseLevel_should_accept_case_insensitive_and_fallback(string text, Serilog.Events.LogEventLevel expected)
    {
        Assert.Equal(expected, SerilogSetup.ParseLevel(text));
    }

    /// <summary>与生产同一条装配路径：BuildAsync 的 configureBuilder 挂 SerilogSetup（不经 TestServer，仅验证日志装配）。</summary>
    private static async Task<WebApplication> BuildAppAsync(string logDirectory, string logLevel)
    {
        var options = new HostOptions
        {
            ListenUrl = "http://127.0.0.1:0",
            DatabasePath = Path.Combine(logDirectory, "jobs.db"),
            TemplatesDbPath = Path.Combine(logDirectory, "templates.db"),
            LogsDbPath = Path.Combine(logDirectory, "logs.db"),
            PluginsPath = Path.Combine(logDirectory, "plugins"),
            ConfigPath = Path.Combine(logDirectory, "settings.json"),
            PrintSettingsPath = Path.Combine(logDirectory, "print-settings.json"),
            ConnectionPath = Path.Combine(logDirectory, "connection.json"),
            HostLogPath = Path.Combine(logDirectory, "host.log"),
            PrintOutputPath = Path.Combine(logDirectory, "print"),
            AppLogDirectory = logDirectory,
            LogLevel = logLevel,
            Transport = TransportMode.Log,
            OpenBrowser = false,
            EnableTray = false,
        };
        return await WinHostApp.BuildAsync(options, TextWriter.Null, _ => { }, configureBuilder: b => SerilogSetup.Apply(b, options));
    }

    private static void TryCleanup(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException)
        {
            // 日志文件句柄延迟释放时忽略清理失败
        }
    }
}
