using System.Text.Json;
using LabelFrame.Bootstrapper.Prerequisites;
using LabelFrame.Bootstrapper.Upgrade;
using LabelFrame.Bootstrapper.Wizard;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>问卷会话的升级评估接线：清单加载后完成本机探测 + 评估 + latest.json 新鲜度（只读契约不变）。</summary>
public sealed class WizardSessionUpgradeTests : IDisposable
{
    private readonly string _dir;

    public WizardSessionUpgradeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lf-upgrade-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不阻断测试
        }
    }

    private string WriteManifest(string version)
    {
        var manifest = new
        {
            schemaVersion = 1,
            labelframeVersion = version,
            generatedAt = "2026-09-14T00:00:00Z",
            components = new object[]
            {
                new
                {
                    id = "server-msi", type = "msi", version, dependsOn = Array.Empty<string>(),
                    urls = new[] { "https://example.invalid/server.msi" }, sha256 = new string('a', 64),
                    sizeBytes = 1, silentArgs = "", topologies = new[] { "standalone" }, notes = "",
                },
                new
                {
                    id = "client-msi", type = "msi", version, dependsOn = Array.Empty<string>(),
                    urls = new[] { "https://example.invalid/client.msi" }, sha256 = new string('b', 64),
                    sizeBytes = 1, silentArgs = "", topologies = new[] { "standalone" }, notes = "",
                },
            },
        };
        var path = Path.Combine(_dir, "install-manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        return path;
    }

    private static LocalInstallProbe FakeProbe(string? server, string? client) => new(
        msiProductsByUpgradeCode: code => code == LocalInstallProbe.ServerUpgradeCode && server is not null
            ? ["{00000000-0000-0000-0000-000000000001}"]
            : code == LocalInstallProbe.ClientUpgradeCode && client is not null
                ? ["{00000000-0000-0000-0000-000000000002}"]
                : [],
        msiProductVersion: productCode => productCode.Contains("000000000001") ? server : client);

    private static WizardSession SessionWith(string manifestPath, LocalInstallProbe probe) => new(
        installedPrinterNames: () => [],
        localInstallProbe: probe,
        runtimeProbe: () => new RuntimeProbeResult(false, null, false, null, false))
    {
        ManifestSource = manifestPath,
    };

    [Fact]
    public async Task Load_manifest_probes_local_and_assesses_upgrade()
    {
        var manifestPath = WriteManifest("0.27.0");
        var session = SessionWith(manifestPath, FakeProbe(server: "0.26.0", client: "0.27.0"));

        await session.LoadManifestAsync();

        Assert.NotNull(session.LocalInstall);
        Assert.Equal("0.26.0", session.LocalInstall.ServerMsiVersion);
        Assert.NotNull(session.Assessment);
        Assert.False(session.Assessment.IsUpToDate);
        Assert.Equal(ComponentUpgradeAction.Upgrade,
            session.Assessment.Entries.Single(entry => entry.ComponentId == "server-msi").Action);
        Assert.Equal(ComponentUpgradeAction.UpToDate,
            session.Assessment.Entries.Single(entry => entry.ComponentId == "client-msi").Action);
    }

    [Fact]
    public async Task Load_manifest_with_everything_current_assesses_up_to_date()
    {
        var manifestPath = WriteManifest("0.27.0");
        var session = SessionWith(manifestPath, FakeProbe(server: "0.27.0", client: "0.27.0"));

        await session.LoadManifestAsync();

        Assert.True(session.Assessment!.IsUpToDate);
    }

    [Fact]
    public async Task Load_manifest_reads_same_directory_latest_json_for_freshness()
    {
        var manifestPath = WriteManifest("0.26.0");
        File.WriteAllText(Path.Combine(_dir, "latest.json"),
            """{ "labelframeVersion": "0.27.0", "manifestUrl": "https://example.invalid/install-manifest.json" }""");
        var session = SessionWith(manifestPath, FakeProbe(server: null, client: "0.26.0"));

        await session.LoadManifestAsync();

        Assert.NotNull(session.Latest);
        Assert.Equal("0.27.0", session.Latest.LabelframeVersion);
        Assert.NotNull(UpgradePresentation.DescribeFreshness(session.Manifest!, session.Latest));
    }

    [Fact]
    public async Task Missing_latest_json_is_silent()
    {
        var manifestPath = WriteManifest("0.27.0");
        var session = SessionWith(manifestPath, FakeProbe(server: null, client: null));

        await session.LoadManifestAsync();

        Assert.Null(session.Latest);  // 推导源不存在：静默跳过，不阻断主流程
        Assert.NotNull(session.Assessment);
    }

    [Fact]
    public async Task Probe_failure_falls_back_to_fresh_install_snapshot()
    {
        var manifestPath = WriteManifest("0.27.0");
        var session = new WizardSession(
            installedPrinterNames: () => [],
            localInstallProbe: new LocalInstallProbe(
                msiProductsByUpgradeCode: _ => throw new InvalidOperationException("注册表不可用")),
            runtimeProbe: () => new RuntimeProbeResult(false, null, false, null, false))
        {
            ManifestSource = manifestPath,
        };

        await session.LoadManifestAsync();

        // 探测异常按「全未装」处理：升级清单缺失不阻断安装流程
        Assert.NotNull(session.LocalInstall);
        Assert.Null(session.LocalInstall.ServerMsiVersion);
        Assert.True(session.Assessment!.Entries.All(entry => entry.Action == ComponentUpgradeAction.Install));
    }
}
