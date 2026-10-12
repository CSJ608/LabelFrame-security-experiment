using System.Text;
using LabelFrame.Bootstrapper.Downloads;
using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.OfflineLayout;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>
/// 多源回退状态机测试：urls 顺序即优先级、失败计数逐源推进、源耗尽停止干预、单源清单退化行为、校验失败同样换源；
/// 布局目录本地源优先——有效源序 = [布局文件（在位时）] ++ urls，本地失败按序落到 urls、
/// 布局不在位 / 无布局目录时行为与现状完全一致（回归锚点）。
/// </summary>
public sealed class CacheSourceFallbackTests
{
    private const string ShaA = "3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c";
    private const string ShaB = "4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d4b2907e5c3a18f6d";

    /// <summary>构造多源清单（server-msi 两源 / client-msi 单源——镜像位预留现状）。</summary>
    private static InstallManifest MultiSourceManifest()
    {
        var json = $$"""
        {
          "schemaVersion": 1,
          "labelframeVersion": "0.27.0",
          "generatedAt": "2026-09-14T03:00:00Z",
          "components": [
            {
              "id": "server-msi", "type": "msi", "version": "0.27.0", "dependsOn": [],
              "urls": ["https://main.example/LabelFrame-Server-0.27.0.msi", "https://mirror.example/LabelFrame-Server-0.27.0.msi"],
              "sha256": "{{ShaA}}", "sizeBytes": 11534336, "silentArgs": "",
              "topologies": ["standalone", "server-win", "offline"], "notes": "服务端"
            },
            {
              "id": "client-msi", "type": "msi", "version": "0.27.0", "dependsOn": [],
              "urls": ["https://main.example/LabelFrame-Client-0.27.0.msi"],
              "sha256": "{{ShaB}}", "sizeBytes": 12582912, "silentArgs": "",
              "topologies": ["standalone", "client", "offline"], "notes": "客户端"
            }
          ]
        }
        """;
        return InstallManifest.Parse(json);
    }

    [Fact]
    public void First_acquire_resolves_primary_source_without_rotation()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        var outcome = fallback.ResolveAcquire("ServerMsi");

        Assert.Equal(SourceFallbackDecision.UseSource, outcome.Decision);
        Assert.Equal("https://main.example/LabelFrame-Server-0.27.0.msi", outcome.Url);
        Assert.Equal(0, outcome.SourceIndex);
        Assert.Equal(2, outcome.SourceCount);
        Assert.False(outcome.Rotated);
        Assert.True(fallback.HasUntriedSources("ServerMsi"));
    }

    [Fact]
    public void Acquire_failure_advances_to_next_source_in_manifest_order()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        fallback.RecordAcquireFailure("ServerMsi");
        var outcome = fallback.ResolveAcquire("ServerMsi");

        // 顺序即优先级——主源失败后提供镜像源，且标记换源（UI 换源提示依据）
        Assert.Equal(SourceFallbackDecision.UseSource, outcome.Decision);
        Assert.Equal("https://mirror.example/LabelFrame-Server-0.27.0.msi", outcome.Url);
        Assert.Equal(1, outcome.SourceIndex);
        Assert.True(outcome.Rotated);
        Assert.True(fallback.HasUntriedSources("ServerMsi"));
    }

    [Fact]
    public void Verify_failure_also_advances_source()
    {
        // 坏哈希（下载成功但校验失败）语义 = 换源重取，而非重复同一损坏源
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        fallback.RecordVerifyFailure("ServerMsi");

        Assert.Equal("https://mirror.example/LabelFrame-Server-0.27.0.msi", fallback.ResolveAcquire("ServerMsi").Url);
    }

    [Fact]
    public void Exhausted_sources_stop_intervening()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        fallback.RecordAcquireFailure("ServerMsi"); // 主源失败
        fallback.RecordAcquireFailure("ServerMsi"); // 镜像源失败 → 耗尽

        var outcome = fallback.ResolveAcquire("ServerMsi");
        Assert.Equal(SourceFallbackDecision.KeepEngineUrl, outcome.Decision);
        Assert.Null(outcome.Url);
        Assert.False(outcome.Rotated);
        Assert.False(fallback.HasUntriedSources("ServerMsi")); // 不再驱动重试（防无源空转）
    }

    [Fact]
    public void Single_source_manifest_fails_closed_without_rotation()
    {
        // 首期生产形态：urls 仅主源一个元素（镜像位预留）——失败即耗尽，结构不变
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        var before = fallback.ResolveAcquire("ClientMsi");
        Assert.Equal("https://main.example/LabelFrame-Client-0.27.0.msi", before.Url);

        fallback.RecordAcquireFailure("ClientMsi");

        var after = fallback.ResolveAcquire("ClientMsi");
        Assert.Equal(SourceFallbackDecision.KeepEngineUrl, after.Decision);
        Assert.False(fallback.HasUntriedSources("ClientMsi"));
    }

    [Fact]
    public void Unknown_or_null_package_keeps_engine_source()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        var unknown = fallback.ResolveAcquire("SomeOtherPackage");
        var nullId = fallback.ResolveAcquire(null);

        Assert.Equal(SourceFallbackDecision.KeepEngineUrl, unknown.Decision);
        Assert.Equal(SourceFallbackDecision.KeepEngineUrl, nullId.Decision);
        Assert.False(fallback.HasUntriedSources("SomeOtherPackage"));
        Assert.False(fallback.HasUntriedSources(null));

        // 未知包的失败不参与计数（如 Bundle 自身载荷）
        fallback.RecordAcquireFailure("SomeOtherPackage");
        Assert.Equal(0, fallback.FailureCount("SomeOtherPackage"));
    }

    [Fact]
    public void Failure_counters_are_isolated_per_package()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());

        fallback.RecordAcquireFailure("ServerMsi");

        Assert.Equal(1, fallback.FailureCount("ServerMsi"));
        Assert.Equal(0, fallback.FailureCount("ClientMsi"));
        Assert.Equal("https://main.example/LabelFrame-Client-0.27.0.msi", fallback.ResolveAcquire("ClientMsi").Url);
    }

    [Fact]
    public void Reset_clears_counters_for_new_apply()
    {
        // 重试按钮 = 全新 Plan + Apply：失败计数归零、从头按 urls 顺序
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());
        fallback.RecordAcquireFailure("ServerMsi");

        fallback.Reset();

        Assert.Equal(0, fallback.FailureCount("ServerMsi"));
        Assert.Equal("https://main.example/LabelFrame-Server-0.27.0.msi", fallback.ResolveAcquire("ServerMsi").Url);
    }

    [Fact]
    public void FromManifest_maps_all_six_chain_packages_from_fixture()
    {
        var manifestJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Manifests", "install-manifest.full.json"));
        var manifest = InstallManifest.Parse(manifestJson);
        var fallback = CacheSourceFallback.FromManifest(manifest);

        foreach (var packageId in new[] { "DotNetDesktopRuntime", "WebView2Runtime", "ServerMsi", "ClientMsi", "WebUiPlacement", "ZebraPluginPlacement" })
        {
            var outcome = fallback.ResolveAcquire(packageId);
            Assert.Equal(SourceFallbackDecision.UseSource, outcome.Decision);
            Assert.NotEqual(0, outcome.SourceCount);
        }

        var snapshot = fallback.FailureSnapshot();
        Assert.Empty(snapshot);
    }

    [Fact]
    public void Rotation_indexes_clamp_to_last_source_when_failures_exceed_count()
    {
        // 三源清单失败 5 次：源游标钳制在末位下标，不再覆写（KeepEngineUrl）
        var json = $$"""
        {
          "schemaVersion": 1,
          "labelframeVersion": "0.27.0",
          "generatedAt": "2026-09-14T03:00:00Z",
          "components": [
            {
              "id": "server-msi", "type": "msi", "version": "0.27.0", "dependsOn": [],
              "urls": ["https://a.example/s.msi", "https://b.example/s.msi", "https://c.example/s.msi"],
              "sha256": "{{ShaA}}", "sizeBytes": 11534336, "silentArgs": "",
              "topologies": ["standalone"], "notes": "服务端"
            }
          ]
        }
        """;
        var fallback = CacheSourceFallback.FromManifest(InstallManifest.Parse(json));

        var sequence = new List<string>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var outcome = fallback.ResolveAcquire("ServerMsi");
            if (outcome.Url is not null)
            {
                sequence.Add(outcome.Url);
            }

            fallback.RecordAcquireFailure("ServerMsi");
        }

        // 逐源推进 a → b → c，c 之后保持耗尽（不再提供源）
        Assert.Equal(
            new[] { "https://a.example/s.msi", "https://b.example/s.msi", "https://c.example/s.msi" },
            sequence);
        Assert.Equal(5, fallback.FailureCount("ServerMsi"));
        Assert.False(fallback.HasUntriedSources("ServerMsi"));
    }

    [Fact]
    public void Outcome_carries_source_metadata_for_ui_hints()
    {
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest());
        fallback.RecordAcquireFailure("ServerMsi");

        var outcome = fallback.ResolveAcquire("ServerMsi");

        // 进度页「源 i/N」提示与换源文案依赖下标与总数（1 基展示由 BA 层转换）
        Assert.Equal(1, outcome.SourceIndex);
        Assert.Equal(2, outcome.SourceCount);
        Assert.True(outcome.Rotated);
    }

    [Fact]
    public void FromManifest_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => CacheSourceFallback.FromManifest(null!));
    }

    // ---- 布局目录本地源优先（有效源序 = [布局文件] ++ urls） ----

    /// <summary>临时布局目录：放入指定组件的布局文件（内容任意——状态机只判在位，哈希校验由引擎承担）。</summary>
    private static string CreateLayoutDirectory(params string[] fileNames)
    {
        var dir = Path.Combine(Path.GetTempPath(), "lf-source-fallback-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var fileName in fileNames)
        {
            File.WriteAllText(Path.Combine(dir, fileName), "layout-file-content");
        }

        return dir;
    }

    [Fact]
    public void Layout_file_present_is_offered_first_before_urls()
    {
        using var layout = new TempDirectoryScope(CreateLayoutDirectory("LabelFrame-Server-0.27.0.msi"));
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest(), layout.Directory);

        var outcome = fallback.ResolveAcquire("ServerMsi");

        // 第 0 次获取解析 = 布局目录本地文件（隐式优先源；有效源总数 = 1 本地 + 2 urls = 3）
        Assert.Equal(SourceFallbackDecision.UseSource, outcome.Decision);
        Assert.True(outcome.IsLocal);
        Assert.Null(outcome.Url);
        Assert.Equal(Path.Combine(layout.Directory, "LabelFrame-Server-0.27.0.msi"), outcome.LocalPath);
        Assert.Equal(0, outcome.SourceIndex);
        Assert.Equal(3, outcome.SourceCount);
        Assert.False(outcome.Rotated);
        Assert.True(fallback.HasUntriedSources("ServerMsi"));
    }

    [Fact]
    public void Layout_acquire_failure_advances_to_first_url_with_rotation()
    {
        // 本地源拷贝失败（文件被占用 / 移除等）→ 按既有推进语义落到 urls[0]（换源标记 = 真）
        using var layout = new TempDirectoryScope(CreateLayoutDirectory("LabelFrame-Server-0.27.0.msi"));
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest(), layout.Directory);

        fallback.RecordAcquireFailure("ServerMsi");
        var outcome = fallback.ResolveAcquire("ServerMsi");

        Assert.False(outcome.IsLocal);
        Assert.Equal("https://main.example/LabelFrame-Server-0.27.0.msi", outcome.Url);
        Assert.Equal(1, outcome.SourceIndex);
        Assert.Equal(3, outcome.SourceCount);
        Assert.True(outcome.Rotated);
    }

    [Fact]
    public void Layout_verify_failure_tampered_file_also_rotates_to_urls()
    {
        // 篡改布局文件（引擎校验失败）→ 换源重取干净副本；断网（urls 均不可达）则源耗尽 fail-closed
        using var layout = new TempDirectoryScope(CreateLayoutDirectory("LabelFrame-Server-0.27.0.msi"));
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest(), layout.Directory);

        fallback.RecordVerifyFailure("ServerMsi");

        Assert.Equal("https://main.example/LabelFrame-Server-0.27.0.msi", fallback.ResolveAcquire("ServerMsi").Url);
    }

    [Fact]
    public void Layout_plus_urls_exhaustion_takes_one_more_failure_than_urls_only()
    {
        // 有效源 3（1 本地 + 2 urls）：前 3 次失败各换源，第 4 次即耗尽
        using var layout = new TempDirectoryScope(CreateLayoutDirectory("LabelFrame-Server-0.27.0.msi"));
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest(), layout.Directory);

        var sequence = new List<string?>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var outcome = fallback.ResolveAcquire("ServerMsi");
            sequence.Add(outcome.IsLocal ? outcome.LocalPath : outcome.Url);
            fallback.RecordAcquireFailure("ServerMsi");
        }

        Assert.Equal(
            new[]
            {
                Path.Combine(layout.Directory, "LabelFrame-Server-0.27.0.msi"),
                "https://main.example/LabelFrame-Server-0.27.0.msi",
                "https://mirror.example/LabelFrame-Server-0.27.0.msi",
                null, // 耗尽（KeepEngineUrl）
            },
            sequence);
        Assert.False(fallback.HasUntriedSources("ServerMsi"));

        var exhausted = fallback.ResolveAcquire("ServerMsi");
        Assert.Equal(SourceFallbackDecision.KeepEngineUrl, exhausted.Decision);
        Assert.Equal(3, exhausted.SourceCount); // 报告口径「已尝试全部 3 个源」
    }

    [Fact]
    public void Layout_directory_without_file_for_component_keeps_urls_only_for_that_package()
    {
        // 布局目录只含 server 组件文件：ClientMsi 有效源序退化为纯 urls（不受其他组件影响）
        using var layout = new TempDirectoryScope(CreateLayoutDirectory("LabelFrame-Server-0.27.0.msi"));
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest(), layout.Directory);

        var client = fallback.ResolveAcquire("ClientMsi");
        Assert.False(client.IsLocal);
        Assert.Equal("https://main.example/LabelFrame-Client-0.27.0.msi", client.Url);
        Assert.Equal(1, client.SourceCount); // 单源 urls：与无布局目录完全一致

        fallback.RecordAcquireFailure("ClientMsi");
        Assert.False(fallback.HasUntriedSources("ClientMsi")); // 失败即耗尽（现状语义不回归）

        var server = fallback.ResolveAcquire("ServerMsi");
        Assert.True(server.IsLocal);
        Assert.Equal(3, server.SourceCount);
    }

    [Fact]
    public void Layout_directory_present_but_empty_files_makes_all_urls_only()
    {
        // 布局目录在位但组件文件缺失（部分拷贝 / 损坏分发）→ 全部包退化为纯 urls（同构回归锚点）
        using var layout = new TempDirectoryScope(CreateLayoutDirectory("unrelated-file.txt"));
        var fallback = CacheSourceFallback.FromManifest(MultiSourceManifest(), layout.Directory);

        var server = fallback.ResolveAcquire("ServerMsi");
        Assert.False(server.IsLocal);
        Assert.Equal("https://main.example/LabelFrame-Server-0.27.0.msi", server.Url);
        Assert.Equal(2, server.SourceCount);
        Assert.Equal(0, server.SourceIndex);
    }

    [Fact]
    public void Fwlink_component_uses_fixed_layout_name()
    {
        // 查询型直链组件（runtime-webview2）：布局文件名走固定名兜底表（OfflineLayoutNaming 契约）
        using var layout = new TempDirectoryScope(CreateLayoutDirectory("MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe"));
        var manifestJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Manifests", "install-manifest.full.json"));
        var fallback = CacheSourceFallback.FromManifest(InstallManifest.Parse(manifestJson), layout.Directory);

        var outcome = fallback.ResolveAcquire("WebView2Runtime");

        Assert.True(outcome.IsLocal);
        Assert.Equal(Path.Combine(layout.Directory, "MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe"), outcome.LocalPath);
    }

    /// <summary>临时目录作用域（测试结束删除）。</summary>
    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope(string directory) => Directory = directory;

        public string Directory { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, true);
            }
        }
    }
}
