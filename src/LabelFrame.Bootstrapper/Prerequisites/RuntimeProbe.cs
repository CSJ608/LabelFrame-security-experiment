using Microsoft.Win32;

namespace LabelFrame.Bootstrapper.Prerequisites;

/// <summary>运行时前置探测结果（DESIGN 检测口径；BA 在 engine.Detect 前写入 Burn 探测变量）。</summary>
/// <param name="DesktopRuntimeInstalled">.NET 10 Desktop Runtime（x64）是否已装（文件版本探测，≥ 10.0.0，latestMajor 前滚）。</param>
/// <param name="DesktopRuntimeVersion">已装的最高 Desktop Runtime 版本（目录名原样；未装为 null）。</param>
/// <param name="AspNetCoreRuntimeInstalled">ASP.NET Core Runtime（x64）是否已装（文件版本探测，机制同 Desktop；，——Server 组件 Sdk.Web 隐式 FrameworkReference 所需）。</param>
/// <param name="AspNetCoreRuntimeVersion">已装的最高 ASP.NET Core Runtime 版本（目录名原样；未装为 null）。</param>
/// <param name="WebView2Installed">WebView2 Evergreen 是否已装（EdgeUpdate Clients pv 注册表，HKLM + HKCU 双查）。</param>
public sealed record RuntimeProbeResult(
    bool DesktopRuntimeInstalled,
    string? DesktopRuntimeVersion,
    bool AspNetCoreRuntimeInstalled,
    string? AspNetCoreRuntimeVersion,
    bool WebView2Installed);

/// <summary>
/// 运行时前置探测（含 ASP.NET Core 腿）：.NET Desktop / ASP.NET Core Runtime 用
/// <b>文件版本探测</b>（注册表 sharedfx 实证不可靠——运行时已装而键不存在，见 与本仓 MSI 注释）
/// WebView2 用<b>注册表探测</b>（对齐 MSI 侧 WEBVIEW2_HKLM / WEBVIEW2_HKCU 先例）。
/// 目录枚举与注册表读取以委托注入（net10 测试腿可注入假探针；net48 BA 腿用默认实现）。
/// </summary>
public sealed class RuntimeProbe
{
    /// <summary>Desktop Runtime 最低版本（对齐 MSI 侧 NetCoreCheck：desktop / 10.0.0 / latestMajor 前滚）。</summary>
    public const string DesktopRuntimeMinimumVersion = "10.0.0";

    /// <summary>ASP.NET Core Runtime 最低版本（对齐 MSI 侧 NetCoreCheck：aspnetcore / 10.0.0 / latestMajor 前滚）。</summary>
    public const string AspNetCoreRuntimeMinimumVersion = "10.0.0";

    /// <summary>WebView2 Evergreen per-machine 注册表值（WOW6432Node 视图；MSI 侧 WEBVIEW2_HKLM 同键）。</summary>
    public const string WebView2PerMachineValue = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}\pv";

    /// <summary>WebView2 Evergreen per-user 注册表值（MSI 侧 WEBVIEW2_HKCU 同键）。</summary>
    public const string WebView2PerUserValue = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}\pv";

    private readonly Func<IReadOnlyList<string>> _enumerateDesktopRuntimeVersions;
    private readonly Func<IReadOnlyList<string>> _enumerateAspNetCoreRuntimeVersions;
    private readonly Func<string, string?> _readRegistryValue;

    /// <param name="enumerateDesktopRuntimeVersions">枚举 <c>dotnet\shared\Microsoft.WindowsDesktop.App</c> 下的版本目录名。</param>
    /// <param name="enumerateAspNetCoreRuntimeVersions">枚举 <c>dotnet\shared\Microsoft.AspNetCore.App</c> 下的版本目录名。</param>
    /// <param name="readRegistryValue">按完整注册表值路径读取（不存在返回 null）。</param>
    public RuntimeProbe(
        Func<IReadOnlyList<string>>? enumerateDesktopRuntimeVersions = null,
        Func<IReadOnlyList<string>>? enumerateAspNetCoreRuntimeVersions = null,
        Func<string, string?>? readRegistryValue = null)
    {
        _enumerateDesktopRuntimeVersions = enumerateDesktopRuntimeVersions ?? ListInstalledRuntimeVersions("Microsoft.WindowsDesktop.App");
        _enumerateAspNetCoreRuntimeVersions = enumerateAspNetCoreRuntimeVersions ?? ListInstalledRuntimeVersions("Microsoft.AspNetCore.App");
        _readRegistryValue = readRegistryValue ?? ReadRegistryValueOrDefault;
    }

    /// <summary>执行探测（只读，无任何系统改动）。</summary>
    public RuntimeProbeResult Probe()
    {
        var desktop = ProbeNetRuntime(_enumerateDesktopRuntimeVersions, DesktopRuntimeMinimumVersion);
        var aspNetCore = ProbeNetRuntime(_enumerateAspNetCoreRuntimeVersions, AspNetCoreRuntimeMinimumVersion);
        return new RuntimeProbeResult(
            desktop.Installed, desktop.Version,
            aspNetCore.Installed, aspNetCore.Version,
            ProbeWebView2());
    }

    /// <summary>文件版本探测：版本目录名按 <see cref="Version"/> 解析取最大，与最低版本比较（latestMajor 前滚 = 大版本 ≥ 即满足；Desktop / AspNetCore 同机制）。</summary>
    private static (bool Installed, string? Version) ProbeNetRuntime(Func<IReadOnlyList<string>> enumerateVersions, string minimumVersion)
    {
        var minimum = ParseVersion(minimumVersion);
        string? best = null;
        Version? bestVersion = null;

        foreach (var name in enumerateVersions())
        {
            var parsed = ParseVersion(name);
            if (parsed is null)
            {
                continue; // 非版本目录（残留 / 手工文件）忽略
            }

            if (bestVersion is null || parsed > bestVersion)
            {
                bestVersion = parsed;
                best = name;
            }
        }

        return (bestVersion >= minimum, best);
    }

    /// <summary>注册表探测：per-machine 或 per-user 任一 pv 非空即视为已装（Evergreen 自更新，不比版本）。</summary>
    private bool ProbeWebView2() =>
        !string.IsNullOrWhiteSpace(_readRegistryValue(WebView2PerMachineValue))
        || !string.IsNullOrWhiteSpace(_readRegistryValue(WebView2PerUserValue));

    /// <summary>枚举本机 x64 运行时版本目录（官方《How to check that .NET is installed》口径的目录枚举法；框架名 = Microsoft.WindowsDesktop.App / Microsoft.AspNetCore.App）。</summary>
    private static Func<IReadOnlyList<string>> ListInstalledRuntimeVersions(string frameworkName) => () =>
    {
        var frameworkDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", frameworkName);
        if (!Directory.Exists(frameworkDir))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(frameworkDir).Select(Path.GetFileName).OfType<string>().ToList();
        }
        catch (IOException)
        {
            return []; // 枚举失败按未装处理（fail-open 到「计划安装」，安装器自身幂等兜底）
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    };

    /// <summary>按完整注册表值路径读取（不存在返回 null）。HKLM 固定 64 位视图——BA（net48 AnyCPU/Prefer32Bit）以 32 位进程运行时，<c>SOFTWARE\WOW6432Node</c> 路径会被注册表重定向二次映射而读空（本机实测缺陷）。</summary>
    private static string? ReadRegistryValueOrDefault(string name)
    {
        const string HklmPrefix = "HKEY_LOCAL_MACHINE\\";
        const string HkcuPrefix = "HKEY_CURRENT_USER\\";

        if (name.StartsWith(HklmPrefix, StringComparison.OrdinalIgnoreCase))
        {
            using var baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            return ReadSubKeyValue(baseKey, name.Substring(HklmPrefix.Length));
        }

        if (name.StartsWith(HkcuPrefix, StringComparison.OrdinalIgnoreCase))
        {
            using var baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser, RegistryView.Default);
            return ReadSubKeyValue(baseKey, name.Substring(HkcuPrefix.Length));
        }

        return Microsoft.Win32.Registry.GetValue(name, null, null) as string;
    }

    private static string? ReadSubKeyValue(Microsoft.Win32.RegistryKey baseKey, string subKeyAndValueName)
    {
        var valueName = Path.GetFileName(subKeyAndValueName);
        var subKeyName = Path.GetDirectoryName(subKeyAndValueName);
        if (string.IsNullOrEmpty(subKeyName))
        {
            return null;
        }

        using var key = baseKey.OpenSubKey(subKeyName);
        return key?.GetValue(valueName) as string;
    }

    /// <summary>版本解析（容忍 "v" 前缀；不可解析返回 null）。落位侧（<c>PayloadPlacer</c>）版本比较共用。</summary>
    public static Version? ParseVersion(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(1);
        }

        return Version.TryParse(trimmed, out var version) ? version : null;
    }
}
