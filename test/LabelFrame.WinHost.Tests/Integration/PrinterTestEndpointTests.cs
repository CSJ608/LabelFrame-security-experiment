using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LabelFrame.WinHost.Integration;

/// <summary>
/// 测试页传输错误分类 HTTP 集成测试：
/// connection.json 预置 tcp9100 指向已关闭的本机回环端口（启动加载不测试连接，与生产一致），
/// POST /api/printer/test 发送失败 → 非 500：400 + LF_TRANSPORT_TEST_FAILED +
/// 消息含目标地址（host:port）与失败原因；客户端 ILogger 留痕（经测试注入的日志收集器断言）。
/// </summary>
public sealed class PrinterTestEndpointTests : IDisposable
{
    private readonly string _directory;
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly CapturingLoggerProvider _loggerProvider = new();

    public PrinterTestEndpointTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lfprinter-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        // 选一个「已关闭」的本机端口：绑定后立即释放 → 连接必被拒绝（回环 RST，快于超时）
        var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var closedPort = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        // connection.json 预置 tcp9100 → 已关闭端口（启动加载不预检连接，与生产行为一致）
        File.WriteAllText(Path.Combine(_directory, "connection.json"),
            $$"""{ "pluginId": "tcp9100", "params": { "host": "127.0.0.1", "port": "{{closedPort}}" } }""");

        var options = new HostOptions
        {
            ListenUrl = "http://127.0.0.1:0",
            DatabasePath = Path.Combine(_directory, "jobs.db"),
            TemplatesDbPath = Path.Combine(_directory, "templates.db"),
            LogsDbPath = Path.Combine(_directory, "logs.db"),
            PluginsPath = Path.Combine(_directory, "plugins"),
            ConfigPath = Path.Combine(_directory, "settings.json"),
            PrintSettingsPath = Path.Combine(_directory, "print-settings.json"),
            ConnectionPath = Path.Combine(_directory, "connection.json"),
            HostLogPath = Path.Combine(_directory, "host.log"),
            Transport = TransportMode.Log,
            OpenBrowser = false,
            EnableTray = false,
        };

        _app = WinHostApp.BuildAsync(options, TextWriter.Null, _ => { }, builder =>
        {
            builder.WebHost.UseTestServer();
            // 客户端日志留痕断言用：捕获 Warning 及以上日志
            builder.Logging.AddProvider(_loggerProvider);
        }, services =>
        {
            services.RemoveAll<IHostedService>();
        }).GetAwaiter().GetResult();

        _app.StartAsync().GetAwaiter().GetResult();
        var server = (TestServer)_app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        _client = server.CreateClient();
    }

    [Fact]
    public async Task Printer_test_transport_failure_should_be_400_with_test_failed_code()
    {
        var response = await _client.PostAsync("/api/printer/test", null);

        // 非 500：可编程错误（此前为全局异常处理器 500 LF_INTERNAL_001）
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LF_TRANSPORT_TEST_FAILED", body.GetProperty("code").GetString());

        // 消息含目标地址与失败原因
        var message = body.GetProperty("message").GetString();
        Assert.NotNull(message);
        Assert.Contains("127.0.0.1", message);
        Assert.Contains("测试页发送失败", message);

        // 客户端日志留痕：目标 + 插件 + 失败原因
        var logged = string.Join("\n", _loggerProvider.Entries);
        Assert.Contains("测试页发送失败", logged);
        Assert.Contains("127.0.0.1", logged);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败忽略
        }
    }

    /// <summary>测试日志收集器（捕获 Warning 及以上事件，用于断言客户端日志留痕）。</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() => GC.SuppressFinalize(this);

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Entries)
                {
                    owner.Entries.Add($"{formatter(state, exception)} {exception?.Message}");
                }
            }
        }
    }
}
