using System.Reflection;
using System.Runtime.Loader;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>发现的插件：插件实例 + 来源 DLL 路径 + 所在 AssemblyLoadContext（生命周期根，调用方须强持有——见）。</summary>
public sealed record DiscoveredPlugin(ITransportPlugin Plugin, string AssemblyPath, AssemblyLoadContext LoadContext);

/// <summary>目录加载结果：成功发现的插件 + 逐 DLL 加载失败信息（loadError 结构化透出）。</summary>
public sealed record PluginDirectoryLoadResult(
    IReadOnlyList<DiscoveredPlugin> Plugins,
    IReadOnlyList<(string AssemblyPath, string Error)> Errors);

/// <summary>
/// 外部传输插件目录加载器（加载生命周期口径 = ，DESIGN 「加载生命周期」）：
/// 安装包子目录按「包目录单 collectible ALC 整体加载」——目录内全部 DLL 字节加载（LoadFromStream）进同一 ALC
/// 并预载全部伴生依赖（首用零惰性解析窗口），跨程序集发现 <see cref="ITransportPlugin"/>；
/// 无插件产出的目录显式 <see cref="AssemblyLoadContext.Unload"/> 丢弃（确定性释放）。
/// 平铺手动 DLL 维持逐 DLL 独立 ALC（同居目录多插件互不污染），伴生依赖经 Resolving 惰性解析。
/// 两条路径的 ALC 根都随发现结果返回——<b>调用方（注册表）必须显式强持有</b>：collectible ALC 被丢弃后
/// GC 会发起卸载破坏其状态机，此后任何 LoadFromStream（含惰性解析）抛 VerifyIsAlive。
/// 字节加载不锁插件文件（Windows LoadFromAssemblyPath 会锁 DLL）——卸载 = 删除插件文件 / 目录即可立即删除、
/// 覆盖安装可直接替换，运行中进程继续使用内存镜像，重启后按新文件装配。
/// </summary>
public static class PluginDirectoryLoader
{
    /// <summary>
    /// 加载目录下所有可发现的传输插件（等价于 <see cref="LoadWithErrors"/> 的 Plugins 部分，兼容旧调用；
    /// 返回含 ALC 生命周期根，勿丢弃——交由注册表强持有）。
    /// </summary>
    /// <param name="directory">插件目录（不存在 / 为空返回空列表）。</param>
    /// <param name="hostLog">宿主日志写入器（加载失败记录用）。</param>
    /// <returns>发现结果（插件实例 + 来源 DLL 路径 + 所在 ALC）列表。</returns>
    public static IReadOnlyList<DiscoveredPlugin> Load(string? directory, TextWriter hostLog)
        => LoadWithErrors(directory, hostLog).Plugins;

    /// <summary>
    /// 加载目录下所有可发现的传输插件，并返回逐 DLL 加载失败信息（loadError 结构化透出，
    /// 供「已安装插件」列表展示加载失败原因；单插件失败仍不阻断宿主启动）。
    /// </summary>
    /// <param name="directory">插件目录（不存在 / 为空返回空结果）。</param>
    /// <param name="hostLog">宿主日志写入器（加载失败记录用，与失败信息同时保留）。</param>
    /// <returns>成功插件列表（含 ALC 根）+ 失败 DLL 列表（路径 + 异常消息）。</returns>
    public static PluginDirectoryLoadResult LoadWithErrors(string? directory, TextWriter hostLog)
    {
        var plugins = new List<DiscoveredPlugin>();
        var errors = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new PluginDirectoryLoadResult(plugins, errors);
        }

        // 平铺手动 DLL：逐 DLL 独立 collectible ALC（同居目录多插件互不污染，向后兼容）
        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            LoadFlatDll(dll, directory, plugins, errors, hostLog);
        }

        // 子目录安装包：包目录单 ALC 整体加载（原子单元——全部 DLL 同一 ALC，预载消灭惰性窗口）
        foreach (var packageDir in Directory.GetDirectories(directory))
        {
            LoadPackageDirectory(packageDir, plugins, errors, hostLog);
        }

        return new PluginDirectoryLoadResult(plugins, errors);
    }

    /// <summary>平铺单 DLL 加载：独立 ALC + Resolving 惰性解析（宿主默认上下文优先 → 同目录伴生 DLL）。</summary>
    private static void LoadFlatDll(
        string dll,
        string directory,
        List<DiscoveredPlugin> plugins,
        List<(string, string)> errors,
        TextWriter hostLog)
    {
        var alc = new AssemblyLoadContext($"transport-plugin:{Path.GetFileName(dll)}", isCollectible: true);
        alc.Resolving += (context, name) => ResolvePluginDependency(context, name, directory);
        var before = plugins.Count;
        try
        {
            var assembly = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(dll), writable: false));
            TryDiscoverPlugins(assembly, dll, alc, plugins);
        }
        catch (Exception ex)
        {
            RecordLoadError(dll, ex, errors, hostLog);
        }
        finally
        {
            if (plugins.Count == before)
            {
                // 无插件产出（含加载失败 / 部分发现后中断）：抛弃型上下文显式卸载（确定性释放，不等 GC）
                alc.Unload();
            }
        }
    }

    /// <summary>
    /// 包目录整体加载：单 collectible ALC + 全部 DLL 预载 + 跨程序集发现插件；
    /// 单个 DLL 加载 / 类型发现失败只记逐 DLL error，不阻断包内其他程序集；
    /// 无插件产出显式 Unload 丢弃。
    /// </summary>
    private static void LoadPackageDirectory(
        string packageDir,
        List<DiscoveredPlugin> plugins,
        List<(string, string)> errors,
        TextWriter hostLog)
    {
        var dlls = Directory.GetFiles(packageDir, "*.dll");
        if (dlls.Length == 0)
        {
            return;
        }

        var alc = new AssemblyLoadContext($"transport-plugin:{Path.GetFileName(packageDir)}", isCollectible: true);
        alc.Resolving += (context, name) => ResolvePluginDependency(context, name, packageDir);
        var before = plugins.Count;
        try
        {
            foreach (var dll in dlls)
            {
                try
                {
                    var assembly = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(dll), writable: false));
                    TryDiscoverPlugins(assembly, dll, alc, plugins);
                }
                catch (Exception ex)
                {
                    // 单 DLL 失败不阻断包内其他程序集（逐 DLL error 透出）
                    RecordLoadError(dll, ex, errors, hostLog);
                }
            }
        }
        finally
        {
            if (plugins.Count == before)
            {
                // 无插件产出（含加载失败 / 部分发现后中断）：抛弃型上下文显式卸载（确定性释放，不等 GC）
                alc.Unload();
            }
        }
    }

    /// <summary>在程序集内发现 ITransportPlugin 实现（实例化并收录）。</summary>
    private static void TryDiscoverPlugins(
        Assembly assembly,
        string assemblyPath,
        AssemblyLoadContext loadContext,
        List<DiscoveredPlugin> plugins)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(ITransportPlugin).IsAssignableFrom(type))
            {
                continue;
            }

            if (Activator.CreateInstance(type) is ITransportPlugin plugin)
            {
                plugins.Add(new DiscoveredPlugin(plugin, assemblyPath, loadContext));
            }
        }
    }

    /// <summary>记录加载失败（宿主日志 + 结构化 errors）。</summary>
    private static void RecordLoadError(string dll, Exception ex, List<(string, string)> errors, TextWriter hostLog)
    {
        hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 传输插件加载失败（{Path.GetFileName(dll)}）：{ex.Message}");
        errors.Add((dll, ex.Message));
    }

    /// <summary>插件依赖解析：宿主默认上下文已加载的同名程序集优先（Core 等），其次目录内伴生 DLL（字节加载，不锁文件）。</summary>
    private static Assembly? ResolvePluginDependency(AssemblyLoadContext context, AssemblyName name, string? pluginDirectory)
    {
        var inDefault = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
        if (inDefault is not null)
        {
            return inDefault;
        }

        if (!string.IsNullOrWhiteSpace(pluginDirectory))
        {
            var candidate = Path.Combine(pluginDirectory, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                return context.LoadFromStream(new MemoryStream(File.ReadAllBytes(candidate), writable: false));
            }
        }

        return null;
    }
}
