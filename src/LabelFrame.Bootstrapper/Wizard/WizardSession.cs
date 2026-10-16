using System.Net.Http;
using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.Prerequisites;
using LabelFrame.Bootstrapper.Printing;
using LabelFrame.Bootstrapper.Topology;
using LabelFrame.Bootstrapper.Upgrade;

namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>问卷会话（UI 无关的核心状态机，BA 页面复用）：manifest 来源 → 已加载清单 → 本机已装探测与升级评估 → 本机角色 / 品牌多选 / 服务端地址 / 管理界面开关 → 组件集合。</summary>
/// <remarks>
/// 问卷只读契约（不变量）：本类不得出现任何下载、写入或系统改动方法——问卷阶段全程只读；
/// 执行边界 = 确认页「安装」，实际下载 / 链装由 Burn 引擎在 BA 调用 <c>Engine.Apply</c> 后承担
/// （编排见 <c>LabelFrameBootstrapperBa.ExecuteInstallAsync</c>）。
/// </remarks>
public sealed class WizardSession
{
    /// <summary>稳定通道清单 URL（GitHub latest 语义，DESIGN）。</summary>
    public const string StableChannelManifestUrl = "https://github.com/marci-labs/LabelFrame/releases/latest/download/install-manifest.json";

    private readonly ITopologyResolver _resolver;
    private readonly Func<IReadOnlyList<string>> _installedPrinterNames;
    private readonly LocalInstallProbe _localInstallProbe;
    private readonly Func<RuntimeProbeResult>? _runtimeProbe;
    private readonly HttpClient? _http;
    private readonly Func<string?> _existingServerUrlProbe;

    public WizardSession(
        ITopologyResolver? resolver = null,
        Func<IReadOnlyList<string>>? installedPrinterNames = null,
        LocalInstallProbe? localInstallProbe = null,
        Func<RuntimeProbeResult>? runtimeProbe = null,
        HttpClient? http = null,
        Func<string?>? existingServerUrl = null)
    {
        _resolver = resolver ?? new TopologyResolver();
        _installedPrinterNames = installedPrinterNames ?? InstalledPrinters.GetNames;
        _localInstallProbe = localInstallProbe ?? new LocalInstallProbe();
        _runtimeProbe = runtimeProbe;
        _http = http;
        _existingServerUrlProbe = existingServerUrl ?? (() => new ServerUrlStore().Load());
    }

    /// <summary>清单来源：本地路径或 URL（默认稳定通道；布局目录场景 BA 启动时改写为邻接本地清单）。</summary>
    public string ManifestSource { get; set; } = StableChannelManifestUrl;

    /// <summary>
    /// 隐式优先源目录（离线布局目录）：清单来源为<b>本地路径</b>时 = 其所在目录（Apply 期本地源解析优先）；
    /// URL 来源时为 null（纯 urls，在线安装行为不变——回归边界）。清单加载成功时更新。
    /// </summary>
    public string? LocalSourceDirectory { get; private set; }

    /// <summary>已加载并校验的安装清单（问卷后续步骤的前提）。</summary>
    public InstallManifest? Manifest { get; private set; }

    /// <summary>latest.json 指针（清单来源可推导且读取成功时非空； 清单新鲜度提示用，失败静默跳过）。</summary>
    public LatestPointer? Latest { get; private set; }

    /// <summary>本机已装组件快照（清单加载成功后探测）。</summary>
    public LocalInstallSnapshot? LocalInstall { get; private set; }

    /// <summary>升级评估（清单加载成功后计算；呈现由页面过滤——确认页按拓扑计划）。</summary>
    public UpgradeAssessment? Assessment { get; private set; }

    /// <summary>已选本机角色（问卷角色页；基础模式由就绪页默认置 <see cref="TopologyPreset.Client"/>）。</summary>
    public TopologyPreset? Preset { get; set; }

    /// <summary>是否进入高级模式（就绪页「高级选项」显式入口）：置位后问卷出现角色页与明细展开。</summary>
    public bool AdvancedMode { get; set; }

    /// <summary>问卷采集的服务端地址原始输入（地址页；规范化与校验见 <see cref="ServerUrlInput.Normalize"/>，装后落位由 BA 承担）。</summary>
    public string? ServerUrl { get; set; }

    /// <summary>本机已配置的服务端地址（清单加载成功时从 settings.json 只读探测，地址页预填来源；无配置为 null）。</summary>
    public string? ExistingServerUrl { get; private set; }

    /// <summary>已选打印机品牌集合（问卷第 3 步；选项来源仅为 manifest 已有 plugin-&lt;brand&gt; 条目，BA 品牌页直接增删）。</summary>
    public ISet<string> SelectedBrands { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>是否带管理界面（问卷第 4 步开关）。</summary>
    public bool IncludeWebUi { get; set; }

    /// <summary>当前清单可选择的品牌（无 plugin-* 条目则为空，品牌页展示说明）。</summary>
    public IReadOnlyList<string> AvailableBrands => Manifest is null ? [] : TopologyResolver.AvailableBrands(Manifest);

    /// <summary>加载并校验清单（本地路径 = 文件读取；URL = 单次只读 GET；进入就绪页自动触发）→ 探测本机已装与已配服务端地址 → 计算升级评估 → 按已装打印机名预选品牌。</summary>
    public async Task LoadManifestAsync(HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        var effectiveHttp = http ?? _http;
        Manifest = await InstallManifestLoader.LoadAsync(ManifestSource, effectiveHttp, cancellationToken).ConfigureAwait(false);
        LocalSourceDirectory = ResolveLocalSourceDirectory(ManifestSource);

        // 清单新鲜度（latest.json 消费）：推导得到来源才读，失败静默跳过（不阻断主流程）
        Latest = await TryLoadLatestAsync(effectiveHttp, cancellationToken).ConfigureAwait(false);

        // 已配服务端地址只读探测（地址页预填）：读不到 = null，不阻断
        try
        {
            ExistingServerUrl = _existingServerUrlProbe();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExistingServerUrl = null;
        }

        // 本机已装探测 + 升级评估（只读；探测异常按「全未装」处理——升级清单缺失不阻断安装流程）
        var runtime = ProbeRuntimeSafely();
        try
        {
            LocalInstall = _localInstallProbe.Probe(runtime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LocalInstall = new LocalInstallSnapshot(null, null, null, null, runtime);
        }

        Assessment = UpgradeAssessor.Assess(Manifest, LocalInstall);

        // 品牌预选：仅 Zebra——驱动名含 ZDesigner 且清单有对应条目才预勾选；其余品牌从零勾选
        SelectedBrands = new HashSet<string>(
            PrinterBrandDetector.DetectPreselectedBrands(_installedPrinterNames(), AvailableBrands), StringComparer.Ordinal);
    }

    private RuntimeProbeResult ProbeRuntimeSafely()
    {
        try
        {
            return _runtimeProbe is not null ? _runtimeProbe() : new Prerequisites.RuntimeProbe().Probe();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RuntimeProbeResult(false, null, false, null, false);
        }
    }

    /// <summary>隐式优先源目录解析：本地清单来源 → 所在目录（URL / 不可解析形态 → null，退化纯 urls）。</summary>
    private static string? ResolveLocalSourceDirectory(string manifestSource)
    {
        if (InstallManifestLoader.IsHttpUrl(manifestSource))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(manifestSource));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null; // 异常形态（file:// URI 等）：无本地源，不阻断加载
        }
    }

    private async Task<LatestPointer?> TryLoadLatestAsync(HttpClient? http, CancellationToken cancellationToken)
    {
        var source = LatestPointer.DeriveSource(ManifestSource);
        if (source is null)
        {
            return null;
        }

        try
        {
            var json = await InstallManifestLoader.LoadTextAsync(source, http, cancellationToken).ConfigureAwait(false);
            return LatestPointer.Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            return null;  // 推导源读取失败 = 无新鲜度提示（静默跳过）
        }
    }

    /// <summary>按当前问卷答案计算组件集合（确认页数据源；Burn 链 InstallCondition 消费其变量映射）。</summary>
    public TopologyPlan BuildPlan()
    {
        if (Manifest is null)
        {
            throw new InvalidOperationException("尚未加载安装清单，无法计算组件集合。");
        }

        if (Preset is null)
        {
            throw new InvalidOperationException("尚未确定本机角色，无法计算组件集合。");
        }

        return _resolver.Resolve(Manifest, Preset.Value, new TopologyOptions(SelectedBrands, IncludeWebUi));
    }
}
