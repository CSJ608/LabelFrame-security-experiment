using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Primitives;

namespace LabelFrame.Server;

/// <summary>受信代理自诊断中间件：非全量信任启用时，对携带 X-Forwarded-For 但对端不在受信集合的请求按对端 IP 进程内去重告警（提示补充 LABELFRAME_SERVER_TRUSTED_PROXIES）。</summary>
internal sealed class TrustedProxyDiagnosticsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TrustedProxySet _trusted;
    private readonly object _warnedPeersLock = new();
    private readonly HashSet<IPAddress> _warnedPeers = [];

    public TrustedProxyDiagnosticsMiddleware(RequestDelegate next, TrustedProxySet trusted)
    {
        _next = next;
        _trusted = trusted;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 对端为 null（如内存测试宿主）无从定位与去重，跳过；非全量信任才需要自诊断
        var remote = context.Connection.RemoteIpAddress;
        if (remote is not null
            && !_trusted.AllowAll
            && context.Request.Headers.TryGetValue(ForwardedHeadersDefaults.XForwardedForHeaderName, out var forwardedFor)
            && !StringValues.IsNullOrEmpty(forwardedFor)
            && !_trusted.Contains(remote))
        {
            WarnOnce(remote);
        }

        await _next(context);
    }

    private void WarnOnce(IPAddress remote)
    {
        lock (_warnedPeersLock)
        {
            // 长轮询心跳高频重复：同一对端只告警一次
            if (!_warnedPeers.Add(remote))
            {
                return;
            }
        }

        Console.WriteLine(
            $"[LabelFrame] 请求携带 X-Forwarded-For，但对端 {remote} 不在受信代理列表（LABELFRAME_SERVER_TRUSTED_PROXIES）中，已忽略其转发头、来源保持 TCP 对端。"
            + $"若 {remote} 确为反向代理，请把该地址或其所在网段加入 LABELFRAME_SERVER_TRUSTED_PROXIES 后重启生效。");
    }
}
