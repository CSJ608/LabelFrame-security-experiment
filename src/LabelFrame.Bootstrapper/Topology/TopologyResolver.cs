using LabelFrame.Bootstrapper.Manifest;

namespace LabelFrame.Bootstrapper.Topology;

/// <summary>「预设 + 开关 → 组件集合」解析器实现（DESIGN 映射表 + 实现要点）。</summary>
public sealed class TopologyResolver : ITopologyResolver
{
    private const string ManagementUiComponentId = "webui";
    private const string PluginIdPrefix = "plugin-";

    public TopologyPlan Resolve(InstallManifest manifest, TopologyPreset preset, TopologyOptions options)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(options);
#else
        // net48 腿无 ArgumentNullException.ThrowIfNull（.NET 6+ API）
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
#endif

        // server-docker / server-linux 已移出问卷（/）：Docker = Release compose 指引
        // Linux = install.sh；本解析器只服务问卷三角色，manifest 中的 server-docker / server-linux
        // topologies 标记保留（install.sh 消费），不再有对应枚举值。
        var topologyId = preset.ToManifestId();
        var selected = new Dictionary<string, ManifestComponent>(StringComparer.Ordinal);
        foreach (var component in manifest.Components)
        {
            // 开关组件仍受 topologies 过滤（如 webui 未标记 client：追加客户端预设开开关也不纳入）
            if (!ContainsTopology(component, topologyId))
            {
                continue;
            }

            if (component.Id == ManagementUiComponentId)
            {
                if (options.IncludeWebUi)
                {
                    IncludeWithDependencies(manifest, component, selected);
                }

                continue;
            }

            if (IsBrandPlugin(component))
            {
                if (options.SelectedBrands.Contains(BrandIdOf(component.Id)))
                {
                    IncludeWithDependencies(manifest, component, selected);
                }

                continue;
            }

            // 核心组件：命中预设即默认纳入
            IncludeWithDependencies(manifest, component, selected);
        }

        var ordered = OrderByDependencies(manifest, selected);
        var planned = ordered
            .Select(component => new PlannedComponent(component, InstallTargets.Describe(component, preset)))
            .ToList();

        return new TopologyPlan(preset, planned, planned.Sum(item => item.Component.SizeBytes));
    }

    /// <summary>品牌插件条目判定（id 前缀 plugin-；品牌 → 组件映射语义由 完整化）。</summary>
    internal static bool IsBrandPlugin(ManifestComponent component) => component.Id.StartsWith(PluginIdPrefix, StringComparison.Ordinal);

    /// <summary>从组件 id 提取品牌 id（plugin-zebra → zebra）。</summary>
    internal static string BrandIdOf(string componentId) => componentId.Substring(PluginIdPrefix.Length);

    /// <summary>manifest 中可选择的品牌集合：仅为已有 plugin-&lt;brand&gt; 条目（无条目则无可选项）。</summary>
    public static IReadOnlyList<string> AvailableBrands(InstallManifest manifest) =>
        manifest.Components.Where(IsBrandPlugin).Select(component => BrandIdOf(component.Id)).ToList();

    private static bool ContainsTopology(ManifestComponent component, string topologyId) =>
        component.Topologies.Contains(topologyId, StringComparer.Ordinal);

    /// <summary>纳入组件并递归拉入 dependsOn 闭包（依赖只约束同集合内组件的安装顺序；缺引用在清单解析时已拒绝）。</summary>
    private static void IncludeWithDependencies(InstallManifest manifest, ManifestComponent component, Dictionary<string, ManifestComponent> selected)
    {
        if (selected.ContainsKey(component.Id))
        {
            return;
        }

        selected.Add(component.Id, component);

        foreach (var dependencyId in component.DependsOn)
        {
            if (selected.ContainsKey(dependencyId))
            {
                continue;
            }

            var dependency = manifest.Components.FirstOrDefault(candidate => candidate.Id == dependencyId)
                ?? throw new InvalidOperationException($"清单组件 {component.Id} 依赖的 {dependencyId} 不存在（清单已损坏）。");
            IncludeWithDependencies(manifest, dependency, selected);
        }
    }

    /// <summary>按依赖序排列（Kahn 算法，同层取清单声明序靠前者优先；环状依赖拒绝）。</summary>
    private static List<ManifestComponent> OrderByDependencies(InstallManifest manifest, Dictionary<string, ManifestComponent> selected)
    {
        // 声明序索引：同层稳定排序依据
        var declarationIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < manifest.Components.Count; i++)
        {
            declarationIndex[manifest.Components[i].Id] = i;
        }

        // 选中集合内的依赖边（dep → component）；只统计选中集合内的依赖（闭包保证依赖已选中）
        var remainingDependencies = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in selected)
        {
            var edges = pair.Value.DependsOn.Where(depId => selected.ContainsKey(depId)).ToList();
            remainingDependencies[pair.Key] = edges;
            foreach (var depId in edges)
            {
                if (!dependents.TryGetValue(depId, out var list))
                {
                    list = [];
                    dependents[depId] = list;
                }

                list.Add(pair.Key);
            }
        }

        var ordered = new List<ManifestComponent>(selected.Count);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        while (ordered.Count < selected.Count)
        {
            // 同层取声明序最小的就绪节点（确定性输出，测试矩阵可断言顺序）
            var ready = remainingDependencies
                .Where(pair => pair.Value.Count == 0 && !emitted.Contains(pair.Key))
                .Select(pair => declarationIndex[pair.Key])
                .DefaultIfEmpty(-1)
                .Min();

            if (ready < 0)
            {
                var stuck = string.Join("、", remainingDependencies.Where(pair => !emitted.Contains(pair.Key)).Select(pair => pair.Key));
                throw new InvalidOperationException($"清单组件依赖存在环或无法满足：{stuck}。");
            }

            var nextId = declarationIndex.First(pair => pair.Value == ready).Key;
            emitted.Add(nextId);
            ordered.Add(selected[nextId]);

            if (dependents.TryGetValue(nextId, out var unlocked))
            {
                foreach (var dependentId in unlocked)
                {
                    remainingDependencies[dependentId].Remove(nextId);
                }
            }
        }

        return ordered;
    }
}
