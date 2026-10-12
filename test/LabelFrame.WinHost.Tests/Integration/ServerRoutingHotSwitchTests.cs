using System.Net;
using System.Net.Http.Json;
using System.Text;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LabelFrame.WinHost.Integration;

/// <summary>
/// 服务端地址切换「保存即生效」HTTP 集成测试（/）：
/// 用与生产一致的 WinHostApp 装配 + 两个真实 Kestrel stub 服务端模拟运行中切换——
/// 断言设备在新服务端注册（POST /api/devices 可见）、与旧服务端的长轮询连接被断开（notify 请求中止）。
/// </summary>
public sealed class ServerRoutingHotSwitchTests : IDisposable
{
    private const string RemoteIpHeader = "X-Test-Remote-IP";

    private readonly string _directory;
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly StubServer _oldServer = new();
    private readonly StubServer _newServer = new();

    public ServerRoutingHotSwitchTests()
    {
        _oldServer.StartAsync().GetAwaiter().GetResult();
        _newServer.StartAsync().GetAwaiter().GetResult();

        _directory = Path.Combine(Path.GetTempPath(), $"lfhotswitch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
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
            ServerUrl = _oldServer.Url,
            PollIntervalSeconds = 1,
        };

        _app = WinHostApp.BuildAsync(options, TextWriter.Null, _ => { }, builder =>
        {
            builder.WebHost.UseTestServer();
            // 与 WinHostEndpointsTests 同款：模拟回环来源（POST /api/host/config 仅回环可写）
            builder.Services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(RemoteIpHeader));
        }, services =>
        {
            // 移除后台打印 Worker（本测试不打印，作业状态确定性）；路由协调器非 IHostedService，热切换保留
            services.RemoveAll<IHostedService>();
        }).GetAwaiter().GetResult();

        _app.StartAsync().GetAwaiter().GetResult();
        var server = (TestServer)_app.Services.GetRequiredService<IServer>();
        _client = server.CreateClient();
    }

    [Fact]
    public async Task Saving_new_server_url_should_register_on_new_and_disconnect_old_polling()
    {
        // 前置：启动时路由到旧服务端——设备已注册且长轮询挂起（ESTABLISHED 模拟）
        await UntilAsync(() => _oldServer.RegisterCount >= 1);
        await UntilAsync(() => _oldServer.ActiveNotifyCount >= 1);
        Assert.Equal(0, _newServer.RegisterCount);

        // 运行中保存新服务端地址（不重启进程）
        var save = await _client.PostAsync("/api/host/config", Json($"{{ \"serverUrl\": \"{_newServer.Url}\" }}"));
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());

        // 设备在新服务端注册（POST /api/devices 可见）
        await UntilAsync(() => _newServer.RegisterCount >= 1);

        // 与旧服务端的轮询连接断开（旧 notify 长轮询请求被中止，无新轮询建立）
        var disconnected = await Task.WhenAny(_oldServer.NotifyAborted.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(_oldServer.NotifyAborted.Task, disconnected);
        await UntilAsync(() => _oldServer.ActiveNotifyCount == 0);

        // 保存回读一致（生效配置已更新）
        using var config = await _client.GetAsync("/api/host/config");
        var body = await config.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(_newServer.Url, body.GetProperty("serverUrl").GetString());
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition())
        {
            await Task.Delay(100, cts.Token);
        }
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _oldServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _newServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

    /// <summary>模拟 LabelFrame Server 的最小 stub：记录设备注册；notify 长轮询挂起并观察连接中止。</summary>
    private sealed class StubServer : IAsyncDisposable
    {
        private WebApplication? _app;
        private int _activeNotify;
        private int _registerCount;

        public string Url { get; private set; } = string.Empty;

        public int RegisterCount => Volatile.Read(ref _registerCount);

        /// <summary>在途 notify 长轮询数（>0 = 与该服务端保持轮询连接）。</summary>
        public int ActiveNotifyCount => Volatile.Read(ref _activeNotify);

        /// <summary>任一 notify 请求被客户端中止（连接断开）时完成。</summary>
        public TaskCompletionSource NotifyAborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            // 动态端口绑定回环地址（ListenLocalhost 不支持动态端口）
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(System.Net.IPAddress.Loopback, 0));
            var app = builder.Build();

            app.MapPost("/api/devices", () =>
            {
                Interlocked.Increment(ref _registerCount);
                return Results.Ok(new { status = "Online" });
            });
            app.MapGet("/api/devices/{deviceId}/jobs/pending", () => Results.Ok(Array.Empty<object>()));
            app.MapPost("/api/devices/{deviceId}/jobs/{jobId}/result", () => Results.Ok(new { }));
            app.MapPost("/api/devices/{deviceId}/jobs/{jobId}/progress", () => Results.Ok(new { }));
            app.MapGet("/api/devices/{deviceId}/jobs/notify", async (HttpContext context) =>
            {
                Interlocked.Increment(ref _activeNotify);
                try
                {
                    // 挂起等待（模拟服务端长轮询）：客户端断开（旧连接清理）或 25s 兜底超时后返回
                    await Task.Delay(TimeSpan.FromSeconds(25), context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    NotifyAborted.TrySetResult();
                }
                finally
                {
                    Interlocked.Decrement(ref _activeNotify);
                }

                return Results.Ok(new { hasPending = false });
            });

            _app = app;
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
            Assert.NotNull(addresses);
            Url = addresses.Single();
        }

        public async ValueTask DisposeAsync()
        {
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }

    /// <summary>与 WinHostEndpointsTests 同款：按请求头改写 RemoteIpAddress（默认回环）。</summary>
    private sealed class RemoteIpStartupFilter(string headerName) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, forward) =>
                {
                    context.Connection.RemoteIpAddress = context.Request.Headers.TryGetValue(headerName, out var ip)
                        ? System.Net.IPAddress.Parse(ip.ToString())
                        : System.Net.IPAddress.Loopback;
                    return forward();
                });
                next(app);
            };
    }
}
