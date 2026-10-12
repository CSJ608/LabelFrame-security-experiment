using System.Reflection;
using System.Runtime.Loader;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 插件发现探针（安装预检用，加载口径同 PluginDirectoryLoader =）：
/// 目录单 collectible ALC 整体字节加载（全部 DLL 预载进同一上下文，消灭惰性解析窗口），
/// 跨程序集发现 <see cref="ITransportPlugin"/> 并返回插件 ID；只做发现，不 Create 传输实例。
/// 预检完显式 <see cref="AssemblyLoadContext.Unload"/>（确定性释放，不等 GC）；
/// 字节加载不锁文件，预检后临时目录可正常移动 / 删除。
/// </summary>
public static class PluginProbe
{
    /// <summary>逐 DLL 加载失败记录（安装预检把「DLL 无效」与其他失败区分给用户可行动消息用）。</summary>
    public sealed record PluginAssemblyLoadError(string FileName, Exception Exception);

    /// <summary>探针结果：发现的插件 ID（去重）+ 逐 DLL 加载失败明细。</summary>
    public sealed record PluginProbeResult(IReadOnlyList<string> PluginIds, IReadOnlyList<PluginAssemblyLoadError> LoadErrors);

    /// <summary>发现目录内插件 ID（去重；目录不存在 / 空返回空列表；单 DLL 失败只记日志）。</summary>
    public static IReadOnlyList<string> DiscoverPluginIds(string? directory, TextWriter hostLog)
        => Discover(directory, hostLog).PluginIds;

    /// <summary>
    /// 发现目录内插件并返回逐 DLL 失败明细：DLL 不是有效 .NET 程序集（BadImageFormatException）
    /// 与其他加载失败都进 LoadErrors（宿主日志仍逐条记录英文原始消息，供排障）。
    /// </summary>
    public static PluginProbeResult Discover(string? directory, TextWriter hostLog)
    {
        var ids = new List<string>();
        var errors = new List<PluginAssemblyLoadError>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new PluginProbeResult(ids, errors);
        }

        var dlls = Directory.GetFiles(directory, "*.dll");
        if (dlls.Length == 0)
        {
            return new PluginProbeResult(ids, errors);
        }

        // 单目录单 ALC：全部 DLL 预载同一上下文，跨程序集发现
        var alc = new AssemblyLoadContext($"plugin-probe:{Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))}", isCollectible: true);
        alc.Resolving += (context, name) => ResolveDependency(context, name, directory);
        try
        {
            foreach (var dll in dlls)
            {
                try
                {
                    var assembly = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(dll), writable: false));
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || type.IsInterface || !typeof(ITransportPlugin).IsAssignableFrom(type))
                        {
                            continue;
                        }

                        if (Activator.CreateInstance(type) is ITransportPlugin plugin)
                        {
                            ids.Add(plugin.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 单 DLL 失败不阻断包内其他程序集（逐 DLL 明细透出）
                    errors.Add(new PluginAssemblyLoadError(Path.GetFileName(dll), ex));
                    hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 插件预检加载失败（{Path.GetFileName(dll)}）：{ex.Message}");
                }
            }
        }
        finally
        {
            // 预检完确定性释放（探针实例仅读取 Id 字符串，卸载后无引用残留）
            alc.Unload();
        }

        return new PluginProbeResult(ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), errors);
    }

    /// <summary>包内伴生依赖解析：宿主默认上下文已加载的同名程序集优先（Core 等），其次包内伴生 DLL（字节加载，不锁文件）。</summary>
    private static Assembly? ResolveDependency(AssemblyLoadContext context, AssemblyName name, string directory)
    {
        var inDefault = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
        if (inDefault is not null)
        {
            return inDefault;
        }

        var candidate = Path.Combine(directory, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromStream(new MemoryStream(File.ReadAllBytes(candidate), writable: false)) : null;
    }
}
