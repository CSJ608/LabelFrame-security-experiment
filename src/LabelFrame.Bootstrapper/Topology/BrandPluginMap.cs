namespace LabelFrame.Bootstrapper.Topology;

/// <summary>
/// 品牌 → 官方插件包 pluginId 映射：manifest 组件 id（plugin-&lt;brand&gt;）与 .lfplugin 实际安装目录
/// （plugins\&lt;pluginId&gt;）是两个 id 命名空间，本表是两者的桥。
/// </summary>
/// <remarks>兼容义务：常量须与 LabelFrame.Core 的 TransportPluginIdPolicy 保持一致（本库独立于 Core，net48 腿不引用 net10 目标的 Core）——变更必须两侧同步。</remarks>
public static class BrandPluginMap
{
    /// <summary>品牌 id → 插件包 pluginId（指令集维度映射；无映射返回 null：清单出现未知品牌条目按组件 id 兜底展示）。</summary>
    public static string? PluginIdOf(string brand) => brand switch
    {
        "zebra" => "labelframe-transport-zebra",
        "tspl" => "labelframe-transport-tspl",
        _ => null,
    };
}
