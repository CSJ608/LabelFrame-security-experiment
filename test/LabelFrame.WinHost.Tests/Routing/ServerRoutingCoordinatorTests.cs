using System.Collections.Concurrent;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Routing;
using LabelFrame.WinHost.Tests.Transport;
using LabelFrame.WinHost.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFrame.WinHost.Tests.Routing;

/// <summary>
/// 服务端路由协调器单测：保存服务端地址后的路由热切换与旧连接清理——
/// 重建 poller / worker 并向新服务端注册、旧 worker 停止、旧 HttpClient 连接池释放、地址未变化幂等。
/// </summary>
public class ServerRoutingCoordinatorTests
{
    /// <summary>fake poller：记录注册与新建次数；捕获协调器创建的 HttpClient 供断言释放；观察停止令牌取消。</summary>
    private sealed class FakePoller(HttpClient http) : IServerJobPoller
    {
        public HttpClient Http { get; } = http;
        public int RegisterCount { get; private set; }
        public bool WaitForJobCancelled { get; private set; }

        /// <summary>长轮询挂起时长：默认 20s（模拟在途长轮询，供取消断言）；短值驱动外层循环反复注册。</summary>
        public TimeSpan WaitDelay { get; set; } = TimeSpan.FromSeconds(20);

        public Task RegisterAsync(CancellationToken cancellationToken = default)
        {
            RegisterCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ServerJobPayload>> FetchPendingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ServerJobPayload>>([]);

        public async Task<bool> WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            try
            {
                // 挂起模拟服务端长轮询；协调器停止旧 worker 时经停止令牌取消此处
                await Task.Delay(WaitDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WaitForJobCancelled = true;
                throw;
            }

            return false;
        }

        public Task ReportResultAsync(string jobId, ServerJobResult result, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ReportProgressAsync(string jobId, ServerJobProgress progress, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"lfcoord-{Guid.NewGuid():N}");
        public ServiceProvider Services { get; }
        public ConcurrentQueue<FakePoller> CreatedPollers { get; } = [];

        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            var store = new SqliteLabelJobStore(Path.Combine(Directory, "jobs.db"));
            store.InitializeAsync().GetAwaiter().GetResult();
            var queue = new LabelJobQueue(store);
            var templates = new TemplateStore(Path.Combine(Directory, "templates.db"));
            templates.InitializeAsync().GetAwaiter().GetResult();
            var (manager, registry) = TestTransportRegistry.CreateManagerWithRegistry();
            var submission = new JobSubmissionService(
                queue,
                new LabelFrame.Core.Encoding.ZplImageEncoder(),
                dpiProvider: () => 203,
                new SkiaLabelRenderer(),
                templates,
                manager,
                registry,
                TestTransportRegistry.CreateContext(),
                TextWriter.Null);

            var services = new ServiceCollection();
            services.AddSingleton(submission);
            services.AddSingleton(queue);
            services.AddSingleton<ILogger<ServerRoutingWorker>>(NullLogger<ServerRoutingWorker>.Instance);
            Services = services.BuildServiceProvider();
        }

        public ServerRoutingCoordinator CreateCoordinator(HostOptions options) =>
            new(Services, options, _ => { }, (http, _) =>
            {
                var poller = new FakePoller(http);
                CreatedPollers.Enqueue(poller);
                return poller;
            });

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                System.IO.Directory.Delete(Directory, true);
            }
            catch (IOException)
            {
                // 临时目录清理失败忽略
            }
        }
    }

    private static HostOptions Options() => new()
    {
        PollIntervalSeconds = 1,
        ProgressIntervalMs = 100,
    };

    /// <summary>轮询等待条件满足（协调器 worker 异步启动，注册需数毫秒到达）。</summary>
    private static async Task UntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50, cancellationToken);
        }

        Assert.True(condition(), "等待条件超时未满足。");
    }

    [Fact]
    public async Task Apply_should_start_route_and_register_when_initially_unconfigured()
    {
        // 启动未配置地址 → 空转；保存后首次开启路由（worker 未在启动期创建也能热开启）
        // CI 慢机上此类集成等待可超 15s（本地秒级），放宽避免环境性超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator(Options());

        Assert.Null(coordinator.CurrentServerUrl);
        await coordinator.ApplyServerUrlAsync("http://127.0.0.1:6001", cts.Token);

        Assert.Equal("http://127.0.0.1:6001", coordinator.CurrentServerUrl);
        var first = Assert.Single(fixture.CreatedPollers);
        await UntilAsync(() => first.RegisterCount > 0, cts.Token);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task Apply_new_url_should_rebuild_worker_register_new_and_clean_old_connection()
    {
        // 机制核心：切换后新 poller 向新地址注册、旧 worker 停止（长轮询取消）、旧 HttpClient 释放
        // CI 慢机上此类集成等待可超 15s（本地秒级），放宽避免环境性超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator(Options());

        await coordinator.ApplyServerUrlAsync("http://127.0.0.1:6001/", cts.Token);
        var oldPoller = Assert.Single(fixture.CreatedPollers);
        await UntilAsync(() => oldPoller.RegisterCount > 0, cts.Token);

        await coordinator.ApplyServerUrlAsync("http://127.0.0.1:6002", cts.Token);

        // 新 poller 已创建并向新服务端注册
        Assert.Equal(2, fixture.CreatedPollers.Count);
        var newPoller = fixture.CreatedPollers.ToArray()[1];
        Assert.Equal("http://127.0.0.1:6002", coordinator.CurrentServerUrl);
        await UntilAsync(() => newPoller.RegisterCount > 0, cts.Token);

        // 旧连接清理：旧 worker 停止令牌取消（在途长轮询中断）+ 旧 HttpClient 连接池释放
        await UntilAsync(() => oldPoller.WaitForJobCancelled, cts.Token);
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => oldPoller.Http.GetAsync("http://127.0.0.1:6001/healthz"));

        await coordinator.StopAsync();
    }

    [Fact]
    public async Task Apply_same_url_should_be_idempotent_without_rebuild()
    {
        // 保存同一地址（仅尾斜杠差异）不打断进行中的注册 / 长轮询：
        // 热切换的重建 + 旧 worker 停止在 ApplyServerUrlAsync 返回前同步完成——
        // 返回后无新 poller、旧 poller 未被取消即为「未重建」的确定性证据
        // CI 慢机上此类集成等待可超 15s（本地秒级），放宽避免环境性超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator(Options());

        await coordinator.ApplyServerUrlAsync("http://127.0.0.1:6001", cts.Token);
        var poller = Assert.Single(fixture.CreatedPollers);
        await UntilAsync(() => poller.RegisterCount > 0, cts.Token);

        await coordinator.ApplyServerUrlAsync("http://127.0.0.1:6001/", cts.Token);

        Assert.Single(fixture.CreatedPollers);
        Assert.Equal("http://127.0.0.1:6001", coordinator.CurrentServerUrl);
        Assert.False(poller.WaitForJobCancelled);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task Start_with_configured_url_should_enable_route_and_stop_should_clean_up()
    {
        // 启动接线（WinHostApp 路径）与停机清理（Bind → StopAsync）
        // CI 慢机上此类集成等待可超 15s（本地秒级），放宽避免环境性超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var fixture = new Fixture();
        var options = Options();
        options.ServerUrl = "http://127.0.0.1:6003";
        var coordinator = fixture.CreateCoordinator(options);

        await coordinator.StartWithConfiguredUrlAsync(cts.Token);
        var poller = Assert.Single(fixture.CreatedPollers);
        await UntilAsync(() => poller.RegisterCount > 0, cts.Token);

        await coordinator.StopAsync();
        Assert.Null(coordinator.CurrentServerUrl);
        await UntilAsync(() => poller.WaitForJobCancelled, cts.Token);
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => poller.Http.GetAsync("http://127.0.0.1:6003/healthz"));
    }
}
