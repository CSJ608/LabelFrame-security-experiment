using LabelFrame.Core.Jobs;
using LabelFrame.WinHost.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabelFrame.WinHost.Routing;

/// <summary>
/// 服务端路由协调器（评审 C-2 /）：兑现「服务端地址保存即生效」——
/// 保存成功后不重启进程即完成路由热切换：按新地址重建 poller / 路由 worker 并立即注册到新服务端，
/// 同时清理旧路由（取消在途长轮询 → 等待 worker 收尾 → 释放旧 HttpClient 连接池）。
/// 形态选择（决议 1 取「重建 worker」而非「poller 每轮读取配置」）：后者无法覆盖
/// 「启动时未配置地址 → 保存后首次开启路由」的转换（worker 未创建），且切换时延受长轮询挂起
/// （最长 20s）影响不确定；重建形态在保存请求内确定性完成，旧连接生命周期独占、清理边界清晰。
/// 不作为 IHostedService 注册（测试装配 RemoveAll&lt;IHostedService&gt; 后仍可经端点热切换），
/// 生命周期由 WinHostApp 启动接线 + IHostApplicationLifetime 停机联动管理。
/// </summary>
public sealed class ServerRoutingCoordinator : IAsyncDisposable
{
    /// <summary>切换 / 停止路由时等待旧 worker 收尾的上限（正常取消路径毫秒级完成，此为兜底）。</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly HostOptions _options;
    private readonly Action<string> _hostInfo;
    private readonly Func<HttpClient, string, IServerJobPoller> _pollerFactory;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private RoutingLease? _current;
    private IHostApplicationLifetime? _lifetime;
    private CancellationTokenRegistration _stoppingRegistration;
    private bool _disposed;

    /// <summary>创建协调器（pollerFactory 为测试注入点，生产固定构造 ServerJobPoller）。</summary>
    public ServerRoutingCoordinator(
        IServiceProvider services,
        HostOptions options,
        Action<string> hostInfo,
        Func<HttpClient, string, IServerJobPoller>? pollerFactory = null)
    {
        _services = services;
        _options = options;
        _hostInfo = hostInfo;
        _pollerFactory = pollerFactory ?? ((http, url) => new ServerJobPoller(http, url, options.DeviceId, options.DeviceName));
    }

    /// <summary>当前生效的服务端地址（未启用路由为 null；测试与日志观察用）。</summary>
    public string? CurrentServerUrl => _current?.ServerUrl;

    /// <summary>绑定宿主生命周期：应用停机时停止路由并清理连接（WinHostApp 装配时调用）。</summary>
    public void Bind(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _stoppingRegistration = lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // 停机路径尽力清理：失败不影响宿主退出
                _hostInfo($"服务端路由停机清理失败：{ex.Message}");
            }
        });
    }

    /// <summary>启动时按当前配置拉起路由；未配置服务端地址则空转（等待首次保存后热切换开启）。</summary>
    public Task StartWithConfiguredUrlAsync(CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(_options.ServerUrl)
            ? Task.CompletedTask
            : ApplyServerUrlAsync(_options.ServerUrl, cancellationToken);

    /// <summary>
    /// 服务端地址热切换（保存成功后调用）：地址未变化时幂等无动作；变化时先起新路由
    /// （新服务端尽快注册、最小化设备不可用窗口），再停旧路由（取消在途长轮询并释放旧连接）。
    /// </summary>
    public async Task ApplyServerUrlAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        var normalized = serverUrl.Trim().TrimEnd('/');
        await _switchLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.Equals(_current?.ServerUrl, normalized, StringComparison.OrdinalIgnoreCase))
            {
                // 地址未变化：不重建（保存同一地址不应打断正在进行的注册 / 长轮询）
                return;
            }

            var old = _current;
            var lease = CreateLease(normalized);
            _current = lease;

            // 先起新路由：worker 主循环立即向新服务端注册
            await lease.Worker.StartAsync(CancellationToken.None);
            if (old is null)
            {
                _hostInfo($"服务端路由已启用：{normalized}（设备将注册到该服务端）");
            }
            else
            {
                await StopLeaseAsync(old);
                _hostInfo($"服务端路由已切换：{old.ServerUrl} → {normalized}（旧连接已断开，设备已在新服务端重新注册）");
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    /// <summary>停止当前路由并清理连接（应用停机或协调器释放时调用；空转为无动作）。</summary>
    public async Task StopAsync()
    {
        await _switchLock.WaitAsync();
        try
        {
            var lease = _current;
            _current = null;
            if (lease is not null)
            {
                await StopLeaseAsync(lease);
                _hostInfo($"服务端路由已停止：{lease.ServerUrl}（连接已断开）");
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private RoutingLease CreateLease(string serverUrl)
    {
        var http = new HttpClient();
        var poller = _pollerFactory(http, serverUrl);
        var worker = new ServerRoutingWorker(
            poller,
            _services.GetRequiredService<JobSubmissionService>(),
            _services.GetRequiredService<LabelJobQueue>(),
            TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
            _services.GetRequiredService<ILogger<ServerRoutingWorker>>(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(Math.Max(100, _options.ProgressIntervalMs)));
        return new RoutingLease(serverUrl, http, poller, worker);
    }

    /// <summary>旧连接清理：取消在途长轮询（worker 停止触发停止令牌）→ 等待收尾 → 释放 HttpClient 连接池。</summary>
    private static async Task StopLeaseAsync(RoutingLease lease)
    {
        try
        {
            using var timeout = new CancellationTokenSource(StopTimeout);
            await lease.Worker.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // 超时兜底：worker 在后台自行收尾，下方连接释放强制断开剩余请求
        }

        lease.Worker.Dispose();
        lease.Http.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stoppingRegistration.Dispose();
        await StopAsync();
        _switchLock.Dispose();
    }

    /// <summary>一路路由的独占资源：服务端地址 + HttpClient（连接池）+ poller + worker。</summary>
    private sealed record RoutingLease(string ServerUrl, HttpClient Http, IServerJobPoller Poller, ServerRoutingWorker Worker);
}
