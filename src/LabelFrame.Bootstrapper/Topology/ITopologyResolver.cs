using LabelFrame.Bootstrapper.Manifest;

namespace LabelFrame.Bootstrapper.Topology;

/// <summary>「角色 + 开关 → 组件集合」解析契约（DESIGN ，形状细化见；/ 问卷收敛为本机角色三选）——专项 5/8（下载引擎）与 6/8（安装编排）的公共契约。</summary>
public interface ITopologyResolver
{
    /// <summary>按角色与开关计算组件集合：核心组件 = topologies 命中角色即纳入；开关组件（webui / plugin-&lt;brand&gt）由开关决定且仍受 topologies 过滤；dependsOn 闭包纳入。</summary>
    /// <param name="manifest">安装清单（已通过 schema 校验）。</param>
    /// <param name="preset">本机角色（offline 不进问卷枚举；server-docker / server-linux 已移出问卷）。</param>
    /// <param name="options">仅有的两项自由开关：品牌多选（brand id）+ 是否带管理界面。</param>
    TopologyPlan Resolve(InstallManifest manifest, TopologyPreset preset, TopologyOptions options);
}

/// <summary>问卷仅有的两项自由开关（形状微调 ：<see cref="ISet{T}"/>——net48 BA 复用约束，net48 无 IReadOnlySet&lt;T&gt;，集合成员资格语义不变）。</summary>
/// <param name="SelectedBrands">选中的品牌 id 集合（如 "zebra"，映射 manifest 的 plugin-&lt;brand&gt; 条目）。</param>
/// <param name="IncludeWebUi">是否带管理界面（webui 组件落位服务端 plugins/web-ui）。</param>
public sealed record TopologyOptions(ISet<string> SelectedBrands, bool IncludeWebUi);

/// <summary>计划安装的组件：清单条目 + 目标安装位置描述（目录约定对齐 DEPLOY；实际落位 / 修改语义由 定案）。</summary>
public sealed record PlannedComponent(ManifestComponent Component, string InstallTarget);

/// <summary>解析结果：供确认页展示与 / 消费的结构化组件清单。</summary>
/// <param name="Preset">命中的本机角色。</param>
/// <param name="Components">按依赖序排列的组件清单（含依赖闭包；同层按清单声明序）。</param>
/// <param name="TotalSizeBytes">全部组件体积合计（字节）。</param>
public sealed record TopologyPlan(
    TopologyPreset Preset,
    IReadOnlyList<PlannedComponent> Components,
    long TotalSizeBytes);
