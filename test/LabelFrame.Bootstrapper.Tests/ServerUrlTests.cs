using LabelFrame.Bootstrapper.Wizard;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>服务端地址链路：问卷输入规范化（D1 口径）→ settings.json 存取（与 WinHost HostConfigStore 同一事实源）→ 会话只读探测预填。</summary>
public sealed class ServerUrlTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Manifests", fileName);

    [Theory]
    [InlineData("192.168.1.10", "http://192.168.1.10")]                       // 裸主机名补 http://
    [InlineData("192.168.1.10:53961", "http://192.168.1.10:53961")]           // host:port
    [InlineData("http://192.168.1.10:53961", "http://192.168.1.10:53961")]    // 完整 URL 原样
    [InlineData("https://labels.example.com", "https://labels.example.com")]  // https 保留
    [InlineData("  192.168.1.10  ", "http://192.168.1.10")]                   // 去空白
    public void Normalize_should_complete_scheme_and_trim(string input, string expected)
    {
        Assert.Equal(expected, ServerUrlInput.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]        // 无主机
    [InlineData("ftp://192.168.1.10")] // 非 http(s) scheme
    [InlineData("not a url !")]
    public void Normalize_should_reject_unusable_input(string? input)
    {
        Assert.Null(ServerUrlInput.Normalize(input));
    }

    [Fact]
    public void Server_url_store_should_roundtrip_and_survive_missing_or_corrupt_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"labelframe-serverurl-{Guid.NewGuid():N}", "settings.json");
        var store = new ServerUrlStore(path);

        Assert.Null(store.Load()); // 文件不存在 = 无预填

        store.Save("http://192.168.1.10:53961");
        Assert.Equal("http://192.168.1.10:53961", store.Load());

        File.WriteAllText(path, "{ not json");
        Assert.Null(store.Load()); // 损坏 = 无预填，不抛
    }

    [Fact]
    public async Task Session_load_should_probe_existing_server_url_for_prefill()
    {
        // 已装机重跑引导：settings.json 已配地址 → 会话只读探测（ExistingServerUrl），问卷地址页预填（D1）
        var session = new WizardSession(existingServerUrl: () => "http://192.168.1.10:53961")
        {
            ManifestSource = FixturePath("install-manifest.current.json"),
        };
        await session.LoadManifestAsync();

        Assert.Equal("http://192.168.1.10:53961", session.ExistingServerUrl);
    }
}
