using LabelFrame.Bootstrapper.Manifest;

namespace LabelFrame.Bootstrapper.Upgrade;

/// <summary>
/// 升级评估的呈现文案（DESIGN ，纯函数——BA 欢迎页 / 确认页与测试共用，文案口径单一）：
/// 组件显示名、可升级清单行（组件、现版本 → 新版本）、「已是最新」与「清单非最新」提示。
/// </summary>
public static class UpgradePresentation
{
    /// <summary>组件显示名（/ 约定；未知 id 原样返回）。</summary>
    public static string DisplayName(string componentId) => componentId switch
    {
        "server-msi" => "服务端",
        "client-msi" => "打印客户端",
        "webui" => "管理界面",
        "plugin-zebra" => "Zebra 官方插件",
        "runtime-desktop" => ".NET Desktop Runtime",
        "runtime-webview2" => "WebView2 运行时",
        "linux-server" => "Linux 服务端归档",
        "pda-apk" => "PDA 宿主 APK",
        _ => componentId.StartsWith("plugin-", StringComparison.Ordinal) ? componentId.Remove(0, "plugin-".Length) + " 插件" : componentId,
    };

    /// <summary>欢迎页升级摘要（一行式；<paramref name="entries"/> 为呈现范围——欢迎页全清单、确认页按拓扑计划过滤）。</summary>
    public static string Summarize(IReadOnlyList<ComponentUpgradeEntry> entries)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(entries);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }
#endif

        var upgradable = entries.Where(entry => entry.Action == ComponentUpgradeAction.Upgrade).ToList();
        var installed = entries.Where(entry => entry.InstalledVersion is not null).ToList();
        var hasInstalled = installed.Count > 0
            || entries.Any(entry => entry.ComponentId == "runtime-webview2" && entry.Action == ComponentUpgradeAction.UpToDate);

        if (!hasInstalled)
        {
            return "未检测到本机已安装的 LabelFrame 组件，将执行全新安装。";
        }

        if (upgradable.Count > 0)
        {
            var lines = upgradable.Select(entry => $"{DisplayName(entry.ComponentId)} {entry.InstalledVersion} → {entry.TargetVersion}");
            return "检测到可用更新：" + string.Join("；", lines) + "。";
        }

        var installedText = string.Join("、", installed.Select(entry => $"{DisplayName(entry.ComponentId)} {entry.InstalledVersion}"));
        return string.IsNullOrEmpty(installedText)
            ? "已安装组件均为最新版本。"
            : $"检测到已安装：{installedText}——已是最新版本，无需重复安装。";
    }

    /// <summary>清单新鲜度提示（manifest 版本落后于 latest.json 通道最新时非空； latest.json 消费）。</summary>
    public static string? DescribeFreshness(InstallManifest manifest, LatestPointer? latest)
    {
        if (latest is null)
        {
            return null;
        }

        return VersionSemantics.Compare(manifest.LabelframeVersion, latest.LabelframeVersion) < 0
            ? $"清单版本 {manifest.LabelframeVersion} 不是最新（通道最新 {latest.LabelframeVersion}），建议改用官方稳定通道获取最新版。"
            : null;
    }

    /// <summary>确认页横幅（呈现范围 = 拓扑计划内组件；null = 无特别提示）。</summary>
    public static string? ConfirmBanner(IReadOnlyList<ComponentUpgradeEntry> entries)
    {
        var upgradable = entries.Any(entry => entry.Action == ComponentUpgradeAction.Upgrade);
        if (upgradable)
        {
            return "检测到旧版本组件：确认后将执行升级（覆盖升级，用户配置保留）。";
        }

        var installedTracked = entries.Any(entry => entry.InstalledVersion is not null)
            || entries.Any(entry => entry.Action == ComponentUpgradeAction.UpToDate && entry.ComponentId == "runtime-webview2");
        if (installedTracked)
        {
            return "已是最新版本，无需重复安装——可以直接关闭向导；继续执行将按既有语义跳过或幂等覆盖。";
        }

        return null;
    }
}
