using System.Security.Claims;
using LabelFrame.Server.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Time.Testing;

namespace LabelFrame.Server.Tests;

/// <summary>验证内存管理员票据与登录额度的并发边界。</summary>
public sealed class AdminConcurrencyTests
{
    [Fact]
    public async Task Concurrent_stores_cannot_oversell_last_slot_or_evict_existing_sessions()
    {
        var clock = new FakeTimeProvider();
        var store = new AdminTicketStore(clock);
        var existing = new List<string>();
        for (var i = 0; i < 127; i++) existing.Add(await store.StoreAsync(Ticket(clock)));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            try { return await store.StoreAsync(Ticket(clock)); }
            catch (AdminSessionCapacityException) { return null; }
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(attempts);
        var winner = Assert.Single(results.Where(key => key is not null))!;
        Assert.Equal(31, results.Count(key => key is null));
        foreach (var key in existing) Assert.NotNull(await store.RetrieveAsync(key));
        Assert.NotNull(await store.RetrieveAsync(winner));
        await store.RemoveAsync(winner);
        var replacement = await store.StoreAsync(Ticket(clock));
        Assert.NotEqual(winner, replacement);
        Assert.NotNull(await store.RetrieveAsync(replacement));
        await Assert.ThrowsAsync<AdminSessionCapacityException>(() => store.StoreAsync(Ticket(clock)));
    }

    [Fact]
    public async Task Concurrent_remove_and_renew_never_restore_revoked_keys()
    {
        var clock = new FakeTimeProvider();
        var store = new AdminTicketStore(clock);
        var keys = new List<string>();
        for (var i = 0; i < 64; i++) keys.Add(await store.StoreAsync(Ticket(clock)));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = keys.SelectMany(key => new[]
        {
            Task.Run(async () => { await start.Task; await store.RemoveAsync(key); }),
            Task.Run(async () => { await start.Task; await store.RenewAsync(key, Ticket(clock)); }),
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(operations);
        foreach (var key in keys)
        {
            await store.RenewAsync(key, Ticket(clock));
            Assert.Null(await store.RetrieveAsync(key));
        }
        for (var i = 0; i < 128; i++) await store.StoreAsync(Ticket(clock));
        await Assert.ThrowsAsync<AdminSessionCapacityException>(() => store.StoreAsync(Ticket(clock)));
    }

    [Fact]
    public async Task Concurrent_login_attempts_share_one_fixed_window()
    {
        var clock = new FakeTimeProvider();
        var limiter = new AdminLoginLimiter(clock);
        async Task<int> AcquireBatch()
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
            {
                await start.Task;
                return limiter.TryAcquire();
            })).ToArray();
            start.SetResult();
            return (await Task.WhenAll(attempts)).Count(accepted => accepted);
        }
        Assert.Equal(10, await AcquireBatch());
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(0, await AcquireBatch());
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(10, await AcquireBatch());
    }

    private static AuthenticationTicket Ticket(TimeProvider clock) => new(
        new ClaimsPrincipal(new ClaimsIdentity("test")),
        new AuthenticationProperties { ExpiresUtc = clock.GetUtcNow().AddHours(8) }, AdminSecurity.Scheme);
}
