using LabelFrame.Bootstrapper.Manifest;

namespace LabelFrame.Bootstrapper.Upgrade;

/// <summary>组件级升级动作。</summary>
public enum ComponentUpgradeAction
{
    /// <summary>本机未装（全新安装）。</summary>
    Install,

    /// <summary>已装版本低于目标版本（升级）。</summary>
    Upgrade,

    /// <summary>已装版本等于（或满足）目标版本——已是最新。</summary>
    UpToDate,

    /// <summary>本机版本高于清单版本（清单旧于本机；不驱动安装，提示换新清单）。</summary>
    LocalNewer,

    /// <summary>不参与版本比较（webui 覆盖重写 / runtime-webview2 evergreen / 非本机安装条目）。</summary>
    NotTracked,
}

/// <summary>单个组件的升级评估条目。</summary>
/// <param name="ComponentId">manifest 组件 id。</param>
/// <param name="TargetVersion">目标版本（manifest 条目 version）。</param>
/// <param name="InstalledVersion">本机已装版本（null = 未装或不可探测）。</param>
/// <param name="Action">动作判定。</param>
public sealed record ComponentUpgradeEntry(
    string ComponentId,
    string TargetVersion,
    string? InstalledVersion,
    ComponentUpgradeAction Action);

/// <summary>
/// 升级评估结果（<see cref="UpgradeAssessor"/> 输出）。
/// </summary>
/// <param name="Entries">manifest 全部组件的评估条目（清单声明序）。</param>
/// <param name="AnyInstalled">本机是否检测到任一可版本比较组件已装。</param>
/// <param name="HasUpgradable">是否存在任一已装组件判定为「升级」。</param>
public sealed record UpgradeAssessment(
    IReadOnlyList<ComponentUpgradeEntry> Entries,
    bool AnyInstalled,
    bool HasUpgradable)
{
    /// <summary>整体「已是最新」：本机有已装组件且没有任何已装组件待升级。</summary>
    public bool IsUpToDate => AnyInstalled && !HasUpgradable;
}

/// <summary>
/// 升级评估（纯函数）：当轮 manifest 组件条目版本（目标）× 本机已装快照（现状）→ 逐组件动作 + 整体判定。
/// 「已是最新」只看已装组件（未装组件属新装不参与判定）；呈现按调用方过滤（确认页按拓扑计划过滤）。
/// </summary>
public static class UpgradeAssessor
{
    /// <summary>逐组件评估并汇总整体判定。</summary>
    public static UpgradeAssessment Assess(InstallManifest manifest, LocalInstallSnapshot local)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(local);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        if (local is null)
        {
            throw new ArgumentNullException(nameof(local));
        }
#endif

        var entries = manifest.Components.Select(component => AssessComponent(component, local)).ToList();
        var anyInstalled = entries.Any(entry => entry.InstalledVersion is not null || IsPresenceOnlyInstalled(entry));
        var hasUpgradable = entries.Any(entry => entry.Action == ComponentUpgradeAction.Upgrade);
        return new UpgradeAssessment(entries, anyInstalled, hasUpgradable);
    }

    private static ComponentUpgradeEntry AssessComponent(ManifestComponent component, LocalInstallSnapshot local)
    {
        string? installed = component.Id switch
        {
            "server-msi" => local.ServerMsiVersion,
            "client-msi" => local.ClientMsiVersion,
            "plugin-zebra" => local.ZebraPluginVersion,
            "runtime-desktop" => local.Runtime.DesktopRuntimeInstalled ? local.Runtime.DesktopRuntimeVersion : null,
            "runtime-aspnetcore" => local.Runtime.AspNetCoreRuntimeInstalled ? local.Runtime.AspNetCoreRuntimeVersion : null,
            _ => null,
        };

        var action = component.Id switch
        {
            // runtime-webview2：version 恒 evergreen，不比较版本——已装即最新（表）
            "runtime-webview2" => local.Runtime.WebView2Installed ? ComponentUpgradeAction.UpToDate : ComponentUpgradeAction.Install,
            // webui：覆盖重写语义，无独立版本概念
            "webui" => ComponentUpgradeAction.NotTracked,
            // 非本机安装组件（linux-server / pda-apk / 未知 id）
            "linux-server" or "pda-apk" => ComponentUpgradeAction.NotTracked,
            _ => CompareAction(installed, component.Version, isRuntime: component.Id is "runtime-desktop" or "runtime-aspnetcore"),
        };

        return new ComponentUpgradeEntry(component.Id, component.Version, installed, action);
    }

    private static ComponentUpgradeAction CompareAction(string? installed, string targetVersion, bool isRuntime)
    {
        if (installed is null)
        {
            return ComponentUpgradeAction.Install;
        }

        var order = VersionSemantics.Compare(installed, targetVersion);
        if (order == 0)
        {
            return ComponentUpgradeAction.UpToDate;
        }

        if (order > 0)
        {
            // runtime-desktop / runtime-aspnetcore 是共享系统组件：本机更新的版本即满足要求（执行侧 DetectCondition 只判 ≥ 10.0.0）
            return isRuntime ? ComponentUpgradeAction.UpToDate : ComponentUpgradeAction.LocalNewer;
        }

        return ComponentUpgradeAction.Upgrade;
    }

    /// <summary>只判存在性组件（runtime-webview2）的「已装」不计 InstalledVersion——AnyInstalled 判定兜底。</summary>
    private static bool IsPresenceOnlyInstalled(ComponentUpgradeEntry entry)
        => entry.ComponentId == "runtime-webview2" && entry.Action == ComponentUpgradeAction.UpToDate;
}
