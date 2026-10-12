namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 传输插件 id 策略（DESIGN）：官方插件 id 前缀与品牌映射、内置保留 id
/// 旧内置时代 id 别名——客户端（WinHost 配置 / 插件安装）、服务端（plugin-packages 上传校验）
/// 与官方插件工程共用同一常量来源。
/// </summary>
public static class TransportPluginIdPolicy
{
    /// <summary>官方插件 id 前缀（如 labelframe-transport-zebra）。</summary>
    public const string OfficialPrefix = "labelframe-";

    /// <summary>Zebra 品牌传输官方插件 id（.lfplugin manifest.pluginId / DLL 插件 Id / 连接配置 pluginId 三处一致）。</summary>
    public const string ZebraPluginId = "labelframe-transport-zebra";

    /// <summary>旧内置时代 Zebra 插件 id（≤0.26 客户端内置；读取别名，不落盘迁移）。</summary>
    public const string LegacyZebraPluginId = "zebra";

    /// <summary>品牌 id（引导问卷 / install manifest plugin-&lt;brand&gt; 后缀）→ 官方插件包 pluginId 映射（品牌映射表；后续品牌同构扩展）。</summary>
    public static readonly IReadOnlyDictionary<string, string> BrandPluginIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["zebra"] = ZebraPluginId,
    };

    /// <summary>内置传输插件保留 id（客户端核心 + WinHost 内置，禁止外部包占用；服务端上传拒绝）。</summary>
    public static readonly IReadOnlyList<string> ReservedBuiltinIds = ["log", "tcp9100", "winspool"];

    /// <summary>是否官方插件 id（labelframe- 前缀；覆盖安装版本比较适用范围）。</summary>
    public static bool IsOfficial(string pluginId)
        => !string.IsNullOrWhiteSpace(pluginId)
           && pluginId.StartsWith(OfficialPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否内置传输保留 id（log / tcp9100 / winspool；与注册表一致忽略大小写）。</summary>
    public static bool IsReservedBuiltin(string pluginId)
        => !string.IsNullOrWhiteSpace(pluginId)
           && ReservedBuiltinIds.Contains(pluginId.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 旧内置时代 id 读取别名归一：zebra → labelframe-transport-zebra
    /// 其余原样返回。仅用于读取存量配置的内存态映射，不做落盘迁移。
    /// </summary>
    public static string NormalizeAlias(string? pluginId)
        => string.Equals(pluginId?.Trim(), LegacyZebraPluginId, StringComparison.OrdinalIgnoreCase)
            ? ZebraPluginId
            : pluginId ?? string.Empty;
}
