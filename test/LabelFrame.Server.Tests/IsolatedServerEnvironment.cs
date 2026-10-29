using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.Server.Security;
using Microsoft.AspNetCore.Identity;

namespace LabelFrame.Server.Tests;

/// <summary>隔离完整 Server 的全部存储与配置，并通过正式登录端点建立测试会话。</summary>
internal sealed class IsolatedServerEnvironment : IDisposable
{
    private const string Password = "Synthetic-isolated-regression-password";
    private static readonly string Hash = new PasswordHasher<string>().HashPassword("admin", Password);
    private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);
    public string DirectoryPath { get; }

    public IsolatedServerEnvironment()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        DirectoryPath = Path.Combine(root!.FullName, "work", "security-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        foreach (var (key, path) in new Dictionary<string, string>
        {
            ["DB"] = "server.db", ["TEMPLATES_DB"] = "templates.db", ["LOGS_DB"] = "logs.db",
            ["LOG_FILE"] = "server.log", ["CLIENT_PACKAGES"] = "client-packages", ["PDA_PACKAGES"] = "pda-packages",
            ["PLUGIN_PACKAGES"] = "plugin-packages", ["CLIENT_UPDATES"] = "client-updates",
            ["ANDROID_UPDATES"] = "android-updates", ["WEB_UI"] = "web-ui",
        }) Set($"LABELFRAME_SERVER_{key}", Path.Combine(DirectoryPath, path));
        Set("LABELFRAME_SERVER_LISTEN", "http://127.0.0.1:0");
        Set("LABELFRAME_SERVER_PUBLIC_BASE_URL", "");
        Set("LABELFRAME_SERVER_TRUSTED_PROXIES", "");
        Set("Server__AdminPasswordHash", Hash);
    }

    public void Set(string name, string? value)
    {
        _previous.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    public static async Task LoginAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        client.DefaultRequestHeaders.Remove(AdminSecurity.CsrfHeader);
        client.DefaultRequestHeaders.Add(AdminSecurity.CsrfHeader, session.GetProperty("csrfToken").GetString());
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        response.EnsureSuccessStatusCode();
        session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.True(session.GetProperty("authenticated").GetBoolean());
        client.DefaultRequestHeaders.Remove(AdminSecurity.CsrfHeader);
        client.DefaultRequestHeaders.Add(AdminSecurity.CsrfHeader, session.GetProperty("csrfToken").GetString());
    }

    public void Dispose()
    {
        foreach (var (key, value) in _previous) Environment.SetEnvironmentVariable(key, value);
    }
}
