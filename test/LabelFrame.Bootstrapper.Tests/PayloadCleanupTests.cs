using System.IO.Compression;
using System.Text;
using LabelFrame.Bootstrapper.Placement;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>卸载清理：与落位同目标对称 + 清理边界（用户资产不误伤）。</summary>
public sealed class PayloadCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"labelframe-cleanup-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Clean_after_place_should_remove_target_and_empty_parent()
    {
        // 核心：装 → 卸载，Bundle 落位目录（web-ui 与 zebra 插件）清理干净
        var webUiTarget = Path.Combine(_root, "server", "plugins", "web-ui");
        var pluginTarget = Path.Combine(_root, "Client", "plugins", "labelframe-transport-zebra");
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlaceArchive(CreateWebUiZip(), webUiTarget));
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), pluginTarget));

        Assert.Equal(PlacementCleanupOutcome.Cleaned, PayloadPlacer.CleanPlacement(webUiTarget));
        Assert.Equal(PlacementCleanupOutcome.Cleaned, PayloadPlacer.CleanPlacement(pluginTarget));

        Assert.False(Directory.Exists(webUiTarget), "卸载后 web-ui 落位目录应清理");
        Assert.False(Directory.Exists(pluginTarget), "卸载后 zebra 插件落位目录应清理");
        // 父目录已空 → 一并移除（卸载后系统与装前等价）
        Assert.False(Directory.Exists(Path.Combine(_root, "server", "plugins")), "空 plugins 父目录应移除");
        Assert.False(Directory.Exists(Path.Combine(_root, "Client", "plugins")), "空 plugins 父目录应移除");
    }

    [Fact]
    public void Clean_missing_target_should_be_idempotent_absent()
    {
        // 幂等口径：目录不存在 = 清理成功（未装 / 已清理 / 重装不选品牌的落位包卸载会话均命中）
        var outcome = PayloadPlacer.CleanPlacement(Path.Combine(_root, "Client", "plugins", "labelframe-transport-zebra"));
        Assert.Equal(PlacementCleanupOutcome.AlreadyAbsent, outcome);
    }

    [Fact]
    public void Place_with_bundle_version_should_write_placement_marker()
    {
        // 落位凭据：Bundle 落位的目录携带 .labelframe-bundle-placement（内容 = Bundle 版本）——
        // 清理包检测比对凭据与 Bundle 版本判定「本版本落位在位」
        var pluginTarget = Path.Combine(_root, "Client", "plugins", "labelframe-transport-zebra");
        var webUiTarget = Path.Combine(_root, "server", "plugins", "web-ui");
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), pluginTarget, "0.28.0"));
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlaceArchive(CreateWebUiZip(), webUiTarget, "0.28.0"));

        Assert.Equal("0.28.0", PlacementMarker.BundleVersionOf(pluginTarget));
        Assert.Equal("0.28.0", PlacementMarker.BundleVersionOf(webUiTarget));
        Assert.True(PlacementMarker.IsPlacedByBundle(pluginTarget, "0.28.0"), "同版本凭据应判定在位（清理包 Present 依据）");
        Assert.False(PlacementMarker.IsPlacedByBundle(pluginTarget, "0.29.0"), "新版本 Bundle 不认旧落位（升级后旧清理包不清理，交新链重落位）");

        // 同版幂等跳过不重写凭据（目录未变）；降级拒绝同理
        Assert.Equal(PlacementOutcome.SkippedSameVersion, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), pluginTarget, "0.28.1"));
        Assert.Equal("0.28.0", PlacementMarker.BundleVersionOf(pluginTarget)); // 跳过路径不得改写凭据
    }

    [Fact]
    public void Marker_should_distinguish_bundle_placement_from_user_channels()
    {
        // 清理边界的技术保证：客户端 PluginInstaller 通道（无 Bundle 语境落位）与手动放置目录均无凭据 → 探测永不为在位 →
        // Bundle 卸载不触碰（用户资产）；无 -version 参数的落位调用（历史 / 通道形态）同样不写凭据
        var userChannelTarget = Path.Combine(_root, "Client", "plugins", "labelframe-transport-zebra");
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), userChannelTarget, bundleVersion: null));

        Assert.Null(PlacementMarker.BundleVersionOf(userChannelTarget));
        Assert.False(PlacementMarker.IsPlacedByBundle(userChannelTarget, "0.28.0"));

        var manualDir = Path.Combine(_root, "Client", "plugins", "my-manual-plugin");
        Directory.CreateDirectory(manualDir);
        Assert.Null(PlacementMarker.BundleVersionOf(manualDir));
        Assert.False(PlacementMarker.IsPlacedByBundle(manualDir, "0.28.0"));
    }

    [Fact]
    public void Clean_after_place_should_clear_marker_with_directory()
    {
        // 凭据随目录消亡（无记账漂移）：清理后探测立即归零——重装（任意版本）不受残留凭据干扰
        var target = Path.Combine(_root, "p-marker-clean");
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), target, "0.28.0"));
        Assert.True(PlacementMarker.IsPlacedByBundle(target, "0.28.0"));

        Assert.Equal(PlacementCleanupOutcome.Cleaned, PayloadPlacer.CleanPlacement(target));

        Assert.False(PlacementMarker.IsPlacedByBundle(target, "0.28.0"));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Clean_should_not_touch_user_placed_third_party_assets()
    {
        // 用户手动放置的第三方插件 DLL（平铺 + 子目录）卸载后保留（不误伤）
        var pluginsDir = Path.Combine(_root, "Client", "plugins");
        var zebraTarget = Path.Combine(pluginsDir, "labelframe-transport-zebra");
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), zebraTarget));

        var flatDll = Path.Combine(pluginsDir, "ThirdParty.Transport.dll");
        var manualDir = Path.Combine(pluginsDir, "my-manual-plugin");
        Directory.CreateDirectory(manualDir);
        File.WriteAllText(flatDll, "用户手动放置的第三方 DLL");
        File.WriteAllText(Path.Combine(manualDir, "keep.dll"), "用户手动放置的第三方插件目录");

        Assert.Equal(PlacementCleanupOutcome.Cleaned, PayloadPlacer.CleanPlacement(zebraTarget));

        Assert.False(Directory.Exists(zebraTarget), "Bundle 落位目录应清理");
        Assert.True(File.Exists(flatDll), "平铺手动 DLL 不得误伤（用户资产）");
        Assert.True(File.Exists(Path.Combine(manualDir, "keep.dll")), "手动插件子目录不得误伤（用户资产）");
        Assert.True(Directory.Exists(pluginsDir), "父目录非空（含用户资产）应保留");
    }

    [Fact]
    public void Uninstall_then_reinstall_without_brand_should_leave_brand_dir_absent()
    {
        // 组件集合口径（落位内核序列）：装（选 Zebra）→ 卸载清理 → 重装不选 Zebra（只落 webui）→ 品牌目录不存在
        var webUiTarget = Path.Combine(_root, "server", "plugins", "web-ui");
        var zebraTarget = Path.Combine(_root, "Client", "plugins", "labelframe-transport-zebra");

        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), zebraTarget));
        Assert.Equal(PlacementCleanupOutcome.Cleaned, PayloadPlacer.CleanPlacement(zebraTarget));

        // 重装不选品牌：只落 webui，zebra 落位动作不发生（InstallCondition 假 → 包不执行）
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlaceArchive(CreateWebUiZip(), webUiTarget));

        Assert.False(Directory.Exists(zebraTarget), "重装不选品牌后 zebra 落位目录不得存在（残留缺陷消除）");
        Assert.True(Directory.Exists(webUiTarget), "重装所选 webui 应正常落位");
    }

    [Fact]
    public void Reinstall_after_clean_should_place_same_version_without_refusal()
    {
        // 关联口径：卸载清理后再装同版本 = 全新落位（不命中「同版幂等跳过」，更不「降级拒绝」——目录既已清理）
        var target = Path.Combine(_root, "p-reinstall");
        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), target));
        Assert.Equal(PlacementCleanupOutcome.Cleaned, PayloadPlacer.CleanPlacement(target));

        Assert.Equal(PlacementOutcome.Placed, PayloadPlacer.PlacePlugin(CreatePluginZip("1.2.0"), target));
        Assert.Contains("1.2.0", File.ReadAllText(Path.Combine(target, "manifest.json")), StringComparison.Ordinal);
    }

    /// <summary>构造 webui zip（根 = 静态站点文件）。</summary>
    private string CreateWebUiZip()
    {
        return CreateZip("webui", [("index.html", "<html>ui</html>"), ("assets/app.js", "v1")]);
    }

    private string CreatePluginZip(string version) =>
        CreateZip("plugin-" + version, [("manifest.json", $$"""{"pluginId":"labelframe-transport-zebra","name":"Zebra","version":"{{version}}"}"""), ("LabelFrame.TransportPlugin.Zebra.dll", "bin")]);

    private string CreateZip(string name, IReadOnlyList<(string Path, string Content)> entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{name}.zip");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }

        return path;
    }
}
