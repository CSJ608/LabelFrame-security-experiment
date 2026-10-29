using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LabelFrame.Server.Security;

/// <summary>保存有界、可撤销的单进程管理员会话。</summary>
public sealed class AdminTicketStore(TimeProvider clock) : ITicketStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AuthenticationTicket> _tickets = new(StringComparer.Ordinal);

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        lock (_gate)
        {
            foreach (var expiredKey in _tickets.Where(pair => Expired(pair.Value)).Select(pair => pair.Key).ToArray())
                _tickets.Remove(expiredKey);
            if (_tickets.Count >= 128) throw new InvalidOperationException("管理员会话数量已达上限。");
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _tickets.Add(key, ticket);
            return Task.FromResult(key);
        }
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        lock (_gate)
        {
            // 已撤销会话不能由并发请求的续期重新创建。
            if (_tickets.ContainsKey(key)) _tickets[key] = ticket;
        }
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(key, out var ticket)) return Task.FromResult<AuthenticationTicket?>(null);
            if (!Expired(ticket)) return Task.FromResult<AuthenticationTicket?>(ticket);
            _tickets.Remove(key);
            return Task.FromResult<AuthenticationTicket?>(null);
        }
    }

    public Task RemoveAsync(string key)
    {
        lock (_gate) _tickets.Remove(key);
        return Task.CompletedTask;
    }

    private bool Expired(AuthenticationTicket ticket) =>
        ticket.Properties.ExpiresUtc is not { } expires || expires <= clock.GetUtcNow();
}
