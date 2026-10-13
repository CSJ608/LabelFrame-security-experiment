using System.Runtime.Loader;
using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 传输插件注册表实现：注册（内置 / 外部）→ 按 ID 装配（ID 忽略大小写）。
/// 外部插件的 AssemblyLoadContext 根随注册强持有（宿主生命周期内保持插件 ALC 存活）——
/// collectible ALC 被丢弃后 GC 发起卸载会破坏其状态机，此后惰性解析 LoadFromStream 抛 VerifyIsAlive；
/// 同 id 重新注册时旧根随旧条目释放。
/// </summary>
public sealed class TransportPluginRegistry : ITransportPluginRegistry
{
    private readonly Dictionary<string, (TransportPluginDescriptor Descriptor, ITransportPlugin Plugin)> _plugins =
        new(StringComparer.OrdinalIgnoreCase);

    // 外部插件的 ALC 生命周期根（不进 Descriptor——避免泄漏进 API 序列化契约）
    private readonly Dictionary<string, AssemblyLoadContext> _loadContexts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>注册插件（内置 IsExternal=false；外部 DLL 由目录加载器注册并附来源路径与 ALC 根）。</summary>
    public void Register(ITransportPlugin plugin, bool isExternal = false, string? assemblyPath = null, AssemblyLoadContext? loadContext = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (string.IsNullOrWhiteSpace(plugin.Id))
        {
            throw new ArgumentException("插件 ID 不能为空。", nameof(plugin));
        }

            _plugins[plugin.Id] = (
            new TransportPluginDescriptor(
                plugin.Id,
                plugin.DisplayName,
                plugin.Description,
                plugin.Parameters,
                isExternal,
                assemblyPath,
                plugin is ILabelCommandCompiler,
                plugin.DisplayNameKey,
                plugin.DescriptionKey),
            plugin);
        if (loadContext is not null)
        {
            _loadContexts[plugin.Id] = loadContext;
        }
        else
        {
            _loadContexts.Remove(plugin.Id);
        }
    }

    /// <summary>
    /// 注册外部插件：外部插件不允许覆盖内置 / 非外部插件 ID——
    /// 冲突时记录日志并跳过（返回 false），避免误装 / 恶意包覆盖内置功能。
    /// </summary>
    public bool RegisterExternal(ITransportPlugin plugin, string? assemblyPath, TextWriter hostLog, AssemblyLoadContext? loadContext = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(hostLog);
        if (string.IsNullOrWhiteSpace(plugin.Id))
        {
            throw new ArgumentException("插件 ID 不能为空。", nameof(plugin));
        }

        if (_plugins.TryGetValue(plugin.Id, out var existing) && !existing.Descriptor.IsExternal)
        {
            hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 外部传输插件 {plugin.Id}（{plugin.DisplayName}）与内置插件 ID 冲突，已跳过。");
            return false;
        }

        Register(plugin, isExternal: true, assemblyPath: assemblyPath, loadContext: loadContext);
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<TransportPluginDescriptor> ListPlugins()
        => _plugins.Values.Select(p => p.Descriptor).ToList();

    /// <inheritdoc />
    public TransportPluginDescriptor? GetPlugin(string id)
        => id is not null && _plugins.TryGetValue(id, out var p) ? p.Descriptor : null;

    /// <inheritdoc />
    public ILabelCommandCompiler? GetCommandCompiler(string id)
        => id is not null && _plugins.TryGetValue(id, out var p) ? p.Plugin as ILabelCommandCompiler : null;

    /// <inheritdoc />
    public IPrintTransport CreateTransport(string id, TransportPluginParameters parameters, ITransportPluginContext context)
    {
        if (id is null || !_plugins.TryGetValue(id, out var p))
        {
            throw new InvalidOperationException($"传输插件不存在：{id}。");
        }

        return p.Plugin.Create(parameters, context);
    }

    /// <inheritdoc />
    public string Describe(string id, TransportPluginParameters parameters)
        => id is not null && _plugins.TryGetValue(id, out var p) ? p.Plugin.Describe(parameters) : id ?? "未知插件";
}
