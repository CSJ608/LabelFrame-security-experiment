using System.Runtime.InteropServices;
using System.Text.Json;
using LabelFrame.Bootstrapper.Prerequisites;
using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Wizard;

namespace LabelFrame.Bootstrapper.Upgrade;

/// <summary>本机已装组件快照（<see cref="LocalInstallProbe"/> 输出；null = 未装 / 探测失败按未装处理）。</summary>
/// <param name="ServerMsiVersion">服务端 MSI 已装版本（MSI 注册表，同族多产品取最高；未装为 null）。</param>
/// <param name="ClientMsiVersion">客户端 MSI 已装版本（同上）。</param>
/// <param name="ZebraPluginVersion">Zebra 官方插件已装版本（落位目录 manifest.json；未装为 null）。</param>
/// <param name="Runtime">运行时探测结果（NET Desktop Runtime / WebView2， 口径）。</param>
public sealed record LocalInstallSnapshot(
    string? ServerMsiVersion,
    string? ClientMsiVersion,
    string? ZebraPluginVersion,
    RuntimeProbeResult Runtime);

/// <summary>
/// 本机已装组件版本探测（DESIGN）：MSI 注册表（<c>MsiEnumRelatedProducts</c> 按 UpgradeCode 枚举 +
/// <c>MsiGetProductInfo</c> 读版本串，同族多产品取最高）+ 官方插件落位目录 manifest.json + 运行时探测复用。
/// 全程只读（问卷只读契约不变）；MSI 查询与插件 manifest 读取以委托注入（net10 测试腿可注入假源）。
/// </summary>
public sealed class LocalInstallProbe
{
    /// <summary>服务端 MSI UpgradeCode（packaging/main-server.wxs 事实， 表）。</summary>
    public const string ServerUpgradeCode = "{10BDDF3E-BD37-4C3A-A19E-56CA9EFE5B74}";

    /// <summary>客户端 MSI UpgradeCode（packaging/main.wxs 事实）。</summary>
    public const string ClientUpgradeCode = "{EE3F2357-56CE-4C0F-AEC1-4C04B6E6BCDA}";

    private const string InstallPropertyVersionString = "VersionString"; // INSTALLPROPERTY_VERSIONSTRING（msi.h；本机实测 1608 校正：无 "5.0.5" 前缀）

    private readonly Func<string, IReadOnlyList<string>> _msiProductsByUpgradeCode;
    private readonly Func<string, string?> _msiProductVersion;
    private readonly Func<string, string?> _pluginManifestTextReader;

    /// <param name="msiProductsByUpgradeCode">按 UpgradeCode（带大括号 GUID）枚举已装 ProductCode（空 = 未装）。</param>
    /// <param name="msiProductVersion">按 ProductCode 读版本串（不存在 / 不可得返回 null）。</param>
    /// <param name="pluginManifestTextReader">按插件 manifest.json 完整路径读文本（不存在返回 null）。</param>
    public LocalInstallProbe(
        Func<string, IReadOnlyList<string>>? msiProductsByUpgradeCode = null,
        Func<string, string?>? msiProductVersion = null,
        Func<string, string?>? pluginManifestTextReader = null)
    {
        _msiProductsByUpgradeCode = msiProductsByUpgradeCode ?? ListMsiProductsByUpgradeCode;
        _msiProductVersion = msiProductVersion ?? GetMsiProductVersion;
        _pluginManifestTextReader = pluginManifestTextReader ?? ReadPluginManifestTextOrDefault;
    }

    /// <summary>执行探测（只读；运行时探测结果由调用方传入——BA 启动时已探测一次，复用避免重复枚举）。</summary>
    public LocalInstallSnapshot Probe(RuntimeProbeResult runtime)
    {
        return new LocalInstallSnapshot(
            BestMsiVersion(ServerUpgradeCode),
            BestMsiVersion(ClientUpgradeCode),
            PluginVersion(BundleVariableMap.PluginZebraTargetDir()),
            runtime);
    }

    /// <summary>同族多产品取最高版本（正常只有一个；残留 / 多上下文并存时报告最新， 组件级口径）。</summary>
    private string? BestMsiVersion(string upgradeCode)
    {
        string? best = null;
        foreach (var productCode in _msiProductsByUpgradeCode(upgradeCode))
        {
            var version = _msiProductVersion(productCode);
            if (string.IsNullOrWhiteSpace(version))
            {
                continue; // 版本不可得的产品不参与比较（防御）
            }

            if (best is null || VersionSemantics.Compare(version!, best) > 0)
            {
                best = version;
            }
        }

        return best;
    }

    /// <summary>读取官方插件落位目录 manifest.json 的 version（目录约定；目录 / 文件 / 字段不存在返回 null）。</summary>
    private string? PluginVersion(string pluginDir)
    {
        var manifestPath = Path.Combine(pluginDir, "manifest.json");
        var text = _pluginManifestTextReader(manifestPath);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text!);
            return document.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null; // 损坏 manifest 按未装处理（升级走查不因残留文件误判）
        }
    }

    private static string? ReadPluginManifestTextOrDefault(string manifestPath) =>
        File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;

    private static List<string> ListMsiProductsByUpgradeCode(string upgradeCode)
    {
        var products = new List<string>();
        var buffer = new char[64];
        for (var index = 0; ; index++)
        {
            var length = buffer.Length;
            var error = MsiEnumRelatedProducts(upgradeCode, 0, index, buffer, ref length);
            if (error != 0)
            {
                break; // ERROR_SUCCESS 以外（含 ERROR_NO_MORE_ITEMS = 259）即枚举结束 / 失败按未装处理
            }

            products.Add(new string(buffer, 0, length).TrimEnd('\0'));
        }

        return products;
    }

    private static string? GetMsiProductVersion(string productCode)
    {
        var buffer = new char[64];
        var length = buffer.Length;
        var error = MsiGetProductInfo(productCode, InstallPropertyVersionString, buffer, ref length);
        return error == 0 ? new string(buffer, 0, length).TrimEnd('\0') : null;
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiEnumRelatedProducts(string upgradeCode, int reserved, int productIndex, char[] productCode, ref int productCodeLength);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiGetProductInfo(string productCode, string property, char[] value, ref int valueLength);
}
