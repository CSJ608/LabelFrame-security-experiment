using LabelFrame.Core.IO;
using LabelFrame.Core.Transport.Plugins.Package;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>已安装插件视图（GET /api/plugins/installed；WinHost 与 AndroidHost 同构）。</summary>
public sealed record InstalledPluginView(
    string PluginId,
    string Name,
    string Version,
    string? Description,
    bool Loaded,
    string? LoadError,
    string? PackageDir,
    string Source,
    DateTimeOffset? InstalledAt);

/// <summary>
/// 客户端插件安装 / 卸载服务（自 WinHost 下沉 Core 供 Windows / Android 双端共用）：
/// 安装 = 三层校验（① zip + manifest 必填与平台门 ② 内置 id 拒绝 ③ 临时 ALC 预检核对插件 id）→
/// 解压到 plugins/&lt;pluginId&gt;/（覆盖旧目录）→ 重启生效；
/// 平台门：宿主平台不在 manifest <c>platforms</c> 声明集合内拒绝（无字段的既有包按 Windows 端解释——向后兼容）；
/// 官方插件（labelframe- 前缀）覆盖安装先做版本比较：新版本覆盖 / 同版本幂等跳过 / 降级拒绝（④；第三方维持不做版本比较）
/// 卸载 = 删除 plugins/&lt;pluginId&gt;/ → 重启生效；运行时热卸载不做（口径跨端一致）。
/// </summary>
public sealed class PluginInstaller
{
    private readonly string _pluginsPath;
    private readonly ITransportPluginRegistry _registry;
    private readonly TextWriter _hostLog;
    private readonly IReadOnlyDictionary<string, string> _lastLoadErrors;
    private readonly string _hostPlatform;

    /// <summary>创建安装服务（插件目录不存在自动创建）。</summary>
    /// <param name="pluginsPath">插件根目录（Windows %ProgramData%\LabelFrame\Client\plugins；Android {FilesDir}/plugins）。</param>
    /// <param name="registry">传输插件注册表（内置 id 拒绝按注册表动态判定）。</param>
    /// <param name="hostLog">宿主日志写入器。</param>
    /// <param name="lastLoadErrors">启动装配期的加载失败（已装列表透出原因用）。</param>
    /// <param name="hostPlatform">宿主平台 id（PluginPlatforms.Windows / Android；默认 Windows——既有调用方向后兼容）。</param>
    public PluginInstaller(string pluginsPath, ITransportPluginRegistry registry, TextWriter hostLog,
        IReadOnlyDictionary<string, string>? lastLoadErrors = null, string hostPlatform = PluginPlatforms.Windows)
    {
        _pluginsPath = pluginsPath ?? throw new ArgumentNullException(nameof(pluginsPath));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _hostLog = hostLog ?? throw new ArgumentNullException(nameof(hostLog));
        _lastLoadErrors = lastLoadErrors ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _hostPlatform = PluginPlatforms.Normalize(hostPlatform)
            ?? throw new ArgumentException("宿主平台 id 不能为空。", nameof(hostPlatform));
    }

    /// <summary>
    /// 已安装插件列表：子目录安装包（source=package，读 manifest）+ 平铺手动 DLL（source=manual，只读）。
    /// loaded = 注册表已装配（该插件 ID 来自本目录）；manifest 损坏的包目录仍列出并给出原因。
    /// </summary>
    public IReadOnlyList<InstalledPluginView> ListInstalled()
    {
        var views = new List<InstalledPluginView>();
        if (!Directory.Exists(_pluginsPath))
        {
            return views;
        }

        // 子目录安装包
        foreach (var dir in Directory.GetDirectories(_pluginsPath))
        {
            var manifestPath = Path.Combine(dir, PluginPackageReader.ManifestFileName);
            var dirName = Path.GetFileName(dir);
            if (!File.Exists(manifestPath))
            {
                continue; // 非安装包子目录（管理员手动组织），不列出
            }

            try
            {
                var manifest = PluginPackageManifest.Parse(File.ReadAllText(manifestPath));
                var loaded = IsLoadedFrom(dir, manifest.PluginId);
                var loadError = loaded ? null : FindLoadError(dir);
                views.Add(new InstalledPluginView(
                    manifest.PluginId,
                    manifest.Name,
                    manifest.Version,
                    manifest.Description,
                    loaded,
                    LoadError: loadError,
                    dirName,
                    Source: "package",
                    Directory.GetCreationTimeUtc(dir)));
            }
            catch (Exception ex)
            {
                views.Add(new InstalledPluginView(
                    dirName,
                    dirName,
                    "?",
                    null,
                    Loaded: false,
                    $"manifest 解析失败：{ex.Message}",
                    dirName,
                    Source: "package",
                    Directory.GetCreationTimeUtc(dir)));
            }
        }

        // 平铺手动 DLL（无安装包归属，只读）
        foreach (var dll in Directory.GetFiles(_pluginsPath, "*.dll"))
        {
            var descriptor = _registry.ListPlugins()
                .FirstOrDefault(d => string.Equals(d.AssemblyPath, dll, StringComparison.OrdinalIgnoreCase));
            views.Add(new InstalledPluginView(
                descriptor?.Id ?? Path.GetFileNameWithoutExtension(dll),
                descriptor?.DisplayName ?? Path.GetFileNameWithoutExtension(dll),
                "?",
                null,
                descriptor is not null,
                LoadError: descriptor is not null ? null : (_lastLoadErrors.TryGetValue(dll, out var msg) ? msg : null),
                PackageDir: null,
                Source: "manual",
                File.GetLastWriteTimeUtc(dll)));
        }

        return views;
    }

    /// <summary>
    /// 安装插件包：三层校验（① zip + 根 manifest + 必填字段与平台门 ② 内置插件 ID 拒绝 ③ 临时 ALC 预检核对插件 id）→
    /// 解压到 plugins/&lt;pluginId&gt;/（覆盖旧目录）。失败抛 <see cref="PluginPackageException"/> /
    /// IOException（中文消息）。
    /// </summary>
    public async Task<InstalledPluginView> InstallAsync(Stream packageStream, string? fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        using var buffer = new MemoryStream();
        await packageStream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            throw new PluginPackageException("插件包为空。");
        }

        if (buffer.Length > PluginPackageLimits.MaxBytes)
        {
            throw new PluginPackageException($"插件包超过大小上限（{PluginPackageLimits.Display}）。");
        }

        var bytes = buffer.ToArray();

        // ① zip 完整性 + 根 manifest + 必填字段 + zip-slip（PluginPackageReader.Read 内部校验）
        var content = PluginPackageReader.Read(bytes);

        // ①b 平台门：宿主平台不在 manifest platforms 声明集合内拒绝——
        // 无字段的既有包按 Windows 端解释（Windows 兼容安装、PDA 拒绝存量 Windows 包）
        if (!content.Manifest.SupportsPlatform(_hostPlatform))
        {
            throw new PluginPackageException(
                $"插件「{content.Manifest.Name}」面向 {content.Manifest.PlatformsDisplay()} 平台，与本机平台（{_hostPlatform}）不匹配，无法安装——请选择与本机平台匹配的插件包。");
        }

        // ② 内置插件 ID 拒绝（按宿主注册表动态判定：Windows = log/tcp9100/winspool；PDA = zebra（SDK 档）+ Core 保留 id）
        var existing = _registry.GetPlugin(content.Manifest.PluginId);
        if (existing is { IsExternal: false })
        {
            throw new PluginPackageException($"插件 ID「{content.Manifest.PluginId}」与内置插件冲突，禁止安装。");
        }

        // pluginId 目录名安全校验（防解压路径穿越）
        var safeId = SafeFileName.Normalize(content.Manifest.PluginId)
            ?? throw new PluginPackageException($"pluginId「{content.Manifest.PluginId}」不是合法的插件目录名。");

        // ②b 官方插件覆盖安装版本比较（④，率先于第三方启用；起双端一致）：新版本覆盖 / 同版本幂等 / 降级拒绝
        var installedManifest = ReadInstalledManifest(safeId);
        if (installedManifest is not null && TransportPluginIdPolicy.IsOfficial(content.Manifest.PluginId))
        {
            var comparison = PluginVersionComparer.Compare(content.Manifest.Version, installedManifest.Version);
            if (comparison == 0)
            {
                // 同版本幂等：不重复解压（避免无谓覆盖），返回已装视图
                _hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 官方插件 {content.Manifest.PluginId} 已安装相同版本 {installedManifest.Version}，幂等跳过安装。");
                return InstalledViewOf(safeId, installedManifest);
            }

            if (comparison < 0)
            {
                throw new PluginPackageException(
                    $"官方插件「{content.Manifest.PluginId}」已安装较新版本 {installedManifest.Version}，拒绝降级安装 {content.Manifest.Version}——如需降级请先卸载再安装。");
            }
        }

        // ③ 临时目录解压 + 预检（临时 collectible ALC 发现插件并核对 id，不 Create 传输实例）
        var tempDir = Path.Combine(Path.GetTempPath(), $"lfplugin-install-{Guid.NewGuid():N}");
        try
        {
            PluginPackageReader.ExtractTo(bytes, tempDir);
            var probe = PluginProbe.Discover(tempDir, _hostLog);
            if (probe.PluginIds.Count == 0)
            {
                // DLL 无效（不是 .NET 程序集）给具体可行动消息；其他加载失败维持通用提示
                var invalidDll = probe.LoadErrors.FirstOrDefault(e => e.Exception is BadImageFormatException);
                if (invalidDll is not null)
                {
                    throw new PluginPackageException(
                        $"插件包内「{invalidDll.FileName}」不是有效的 .NET 程序集（DLL 无效或已损坏），请重新导出插件包后再试。");
                }

                throw new PluginPackageException("插件包内未发现 ITransportPlugin 实现。");
            }

            if (!probe.PluginIds.Contains(content.Manifest.PluginId, StringComparer.OrdinalIgnoreCase))
            {
                throw new PluginPackageException($"manifest.pluginId「{content.Manifest.PluginId}」与插件实际 ID（{string.Join(" / ", probe.PluginIds)}）不一致。");
            }

            // 覆盖安装：删除旧目录（字节加载不锁文件，正常运行时可直接删除；被外部占用时明确提示重启后重试）
            Directory.CreateDirectory(_pluginsPath);
            var targetDir = Path.Combine(_pluginsPath, safeId);
            if (Directory.Exists(targetDir))
            {
                try
                {
                    Directory.Delete(targetDir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"插件「{content.Manifest.PluginId}」正在使用中，请重启后重试。", ex);
                }
            }

            try
            {
                Directory.Move(tempDir, targetDir);
            }
            catch (IOException)
            {
                // 跨卷回退：复制 + 清理临时目录
                CopyDirectory(tempDir, targetDir);
                TryDeleteDirectory(tempDir);
            }

            _hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 已安装插件包：{content.Manifest.PluginId}（{content.Manifest.Name} {content.Manifest.Version}，重启后生效）。");
            return new InstalledPluginView(
                content.Manifest.PluginId,
                content.Manifest.Name,
                content.Manifest.Version,
                content.Manifest.Description,
                Loaded: false,
                LoadError: null,
                Path.GetFileName(targetDir),
                Source: "package",
                Directory.GetCreationTimeUtc(targetDir));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// 卸载已安装插件包：删除 plugins/&lt;pluginId&gt;/（仅 source=package，需包内 manifest）。
    /// 失败抛 <see cref="PluginPackageException"/> / IOException（中文消息）。
    /// </summary>
    public void Uninstall(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new PluginPackageException("缺少插件 ID。");
        }

        var safeId = SafeFileName.Normalize(pluginId)
            ?? throw new PluginPackageException($"pluginId「{pluginId}」不是合法的插件目录名。");

        var targetDir = Path.Combine(_pluginsPath, safeId);
        if (!Directory.Exists(targetDir))
        {
            throw new PluginPackageException($"插件「{pluginId}」未安装。");
        }

        if (!File.Exists(Path.Combine(targetDir, PluginPackageReader.ManifestFileName)))
        {
            throw new PluginPackageException("该插件为手动放置（无安装包 manifest），不支持界面卸载，请手动删除插件目录。");
        }

        try
        {
            Directory.Delete(targetDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"插件「{pluginId}」正在使用中，请重启后重试。", ex);
        }

        _hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 已卸载插件包：{pluginId}（重启后生效）。");
    }

    /// <summary>取目录内首个加载失败的 DLL 错误消息（未加载时透出启动期 loadError）。</summary>
    private string? FindLoadError(string dir)
        => _lastLoadErrors.FirstOrDefault(kv => kv.Key.StartsWith(dir, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>读取已安装包目录内的 manifest（无包目录 / 无 manifest / 解析失败返回 null——版本比较按未安装处理）。</summary>
    private PluginPackageManifest? ReadInstalledManifest(string safeId)
    {
        try
        {
            var manifestPath = Path.Combine(_pluginsPath, safeId, PluginPackageReader.ManifestFileName);
            return File.Exists(manifestPath)
                ? PluginPackageManifest.Parse(File.ReadAllText(manifestPath))
                : null;
        }
        catch (Exception)
        {
            // 已装包 manifest 损坏：交由覆盖安装 / ListInstalled 既有路径处理，版本比较按未安装对待
            return null;
        }
    }

    /// <summary>由已装 manifest 构造视图（同版本幂等跳过时返回；加载状态与错误取自当前注册表）。</summary>
    private InstalledPluginView InstalledViewOf(string safeId, PluginPackageManifest manifest)
    {
        var dir = Path.Combine(_pluginsPath, safeId);
        var loaded = IsLoadedFrom(dir, manifest.PluginId);
        return new InstalledPluginView(
            manifest.PluginId,
            manifest.Name,
            manifest.Version,
            manifest.Description,
            loaded,
            LoadError: loaded ? null : FindLoadError(dir),
            safeId,
            Source: "package",
            Directory.GetCreationTimeUtc(dir));
    }

    /// <summary>插件是否从指定目录加载（注册表描述 AssemblyPath 位于该目录下且 id 匹配）。</summary>
    private bool IsLoadedFrom(string dir, string pluginId)
    {
        var descriptor = _registry.GetPlugin(pluginId);
        return descriptor is { IsExternal: true } &&
               !string.IsNullOrEmpty(descriptor.AssemblyPath) &&
               descriptor.AssemblyPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var sub in Directory.GetDirectories(source))
        {
            CopyDirectory(sub, Path.Combine(target, Path.GetFileName(sub)));
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // 临时目录清理失败不阻断（文件被 collectible ALC 锁定，进程退出后由系统清理）
        }
    }
}
