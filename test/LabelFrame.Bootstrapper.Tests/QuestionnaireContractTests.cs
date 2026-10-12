using System.Net;
using LabelFrame.Bootstrapper.Tests.TestInfrastructure;
using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>问卷只读契约断言（起源； 修订为「确认前只读」——执行边界 = 确认页「安装」后 Engine.Plan + Apply）。</summary>
/// <remarks>执行能力不在会话层：会话（问卷）保持零下载 / 零安装 / 零系统改动；真装由 BA 调 Burn 引擎承担。</remarks>
public sealed class QuestionnaireContractTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Manifests", fileName);

    [Fact]
    public async Task Local_manifest_full_flow_should_not_touch_filesystem()
    {
        // 探针目录：清单文件所在目录（走完整个问卷流程后逐项比对，无任何新增 / 修改）
        var probeRoot = Path.Combine(Path.GetTempPath(), $"labelframe-bootstrapper-dryrun-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeRoot);
        var manifestPath = Path.Combine(probeRoot, "install-manifest.json");
        File.Copy(FixturePath("install-manifest.current.json"), manifestPath);

        var programDataLabelFrame = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabelFrame");
        var programDataExistedBefore = Directory.Exists(programDataLabelFrame);

        try
        {
            var before = Snapshot(probeRoot);

            var session = new WizardSession(installedPrinterNames: () => ["ZDesigner ZD421-203dpi ZPL"])
            {
                ManifestSource = manifestPath,
            };
            await session.LoadManifestAsync();

            // 走完全部预设 × 开关组合（问卷所有路径；问卷层不含执行动作）
            foreach (TopologyPreset preset in Enum.GetValues<TopologyPreset>())
            {
                session.Preset = preset;
                session.IncludeWebUi = !session.IncludeWebUi;
                _ = session.BuildPlan();
            }

            Assert.Equal(before, Snapshot(probeRoot));
            if (!programDataExistedBefore)
            {
                Assert.False(Directory.Exists(programDataLabelFrame), "问卷阶段不得创建 ProgramData\\LabelFrame");
            }
        }
        finally
        {
            Directory.Delete(probeRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Local_manifest_load_should_issue_zero_network_requests()
    {
        using var handler = new RecordingHttpMessageHandler(File.ReadAllText(FixturePath("install-manifest.current.json")));
        using var http = new HttpClient(handler);

        var session = new WizardSession { ManifestSource = FixturePath("install-manifest.current.json") };
        await session.LoadManifestAsync(http);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Url_manifest_load_should_issue_single_readonly_get_and_never_fetch_components()
    {
        // URL 来源：清单获取是问卷阶段唯一的网络访问——单次 GET（无网络写入），组件 URL 不被触碰
        using var handler = new RecordingHttpMessageHandler(File.ReadAllText(FixturePath("install-manifest.full.json")));
        using var http = new HttpClient(handler);
        const string manifestUrl = "https://example.invalid/install-manifest.json";

        var session = new WizardSession { ManifestSource = manifestUrl };
        await session.LoadManifestAsync(http);

        session.Preset = TopologyPreset.Standalone;
        session.SelectedBrands.Add("zebra");
        session.IncludeWebUi = true;
        _ = session.BuildPlan();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(manifestUrl, request.RequestUri!.ToString());
    }

    [Fact]
    public void Session_should_expose_no_execution_apis()
    {
        // 会话层不存在执行模式：公开方法面不得出现下载 / 安装 / 应用 / 写入语义（防执行开关被误引入会话层）
        var forbidden = new[] { "download", "install", "apply", "write", "execute", "cache", "remove", "delete" };
        var offending = typeof(WizardSession).GetMethods()
            .Where(method => !method.IsSpecialName)
            .Where(method => forbidden.Any(keyword => method.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Select(method => method.Name)
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Execute_boundary_notice_should_state_readonly_before_confirm()
    {
        // 执行边界 UI 明示（精简表述、语义不变）：确认页可见行必须含「点击「安装」前不下载 / 不安装 / 不改动系统」语义
        Assert.Contains("「安装」前", ExecuteBoundaryNotice.Banner, StringComparison.Ordinal);
        Assert.Contains("不会下载", ExecuteBoundaryNotice.Banner, StringComparison.Ordinal);
        Assert.Contains("不安装", ExecuteBoundaryNotice.Banner, StringComparison.Ordinal);
        Assert.Contains("不改动系统", ExecuteBoundaryNotice.Banner, StringComparison.Ordinal);
        Assert.Contains("管理员", ExecuteBoundaryNotice.Banner, StringComparison.Ordinal);
    }

    /// <summary>递归快照（相对路径 + 文件大小 + 修改时间），用于「无系统改动」比对。</summary>
    private static string[] Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => $"{Path.GetRelativePath(root, path)}|{(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "<dir>")}")
            .Order(StringComparer.Ordinal)];
}
