namespace LabelFrame.Bootstrapper.Ba;

using System.Net.Http;
using System.Windows.Forms;
using LabelFrame.Bootstrapper.Downloads;
using LabelFrame.Bootstrapper.Manifest;
using LabelFrame.Bootstrapper.OfflineLayout;
using LabelFrame.Bootstrapper.Prerequisites;
using LabelFrame.Bootstrapper.Wizard;
using WixToolset.BootstrapperApplicationApi;

/// <summary>
/// LabelFrame 引导 BA（WiX Burn out-of-proc， / ；/ 问卷收敛）：两层问卷（基础三步 / 高级角色模式，只读
/// 就绪页自动加载清单）→ 确认页「安装」→ <see cref="IEngine.Plan"/> + <see cref="IEngine.Apply"/> 真装；装后按问卷写入客户端服务端地址。
/// 执行边界契约见 DESIGN / （确认前绝不 Apply）。
/// 下载体验（/ ，DESIGN）：多源回退（CacheAcquireResolving 消费清单 urls，决策核心在
/// <see cref="CacheSourceFallback"/>）+ 下载侧进度 / 失败分类 / 换源提示的事件映射。
/// 离线布局：<c>--layout &lt;目录&gt;</c> 生成模式（<see cref="OfflineLayoutBuilder"/>）
/// 与安装期本地源优先（<see cref="IEngine.SetLocalSource"/>，清单所在目录 = 隐式优先源目录）。
/// </summary>
internal sealed class LabelFrameBootstrapperBa : BootstrapperApplication
{
    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(30);

    private readonly object _plannedPackagesLock = new();
    private readonly List<PlannedPackage> _plannedPackages = [];
    private readonly TaskCompletionSource<int> _detectCompleted = CreateSource();

    /// <summary>每轮执行（含重试）重置的等待源：Plan / Apply。</summary>
    private readonly object _runLock = new();
    private TaskCompletionSource<int> _planCompleted = CreateSource();
    private TaskCompletionSource<int> _applyCompleted = CreateSource();

    /// <summary>安装状态快照（引擎事件线程写、UI 线程读；整体替换保证一致视图）。</summary>
    private readonly object _stateLock = new();
    private InstallState _state = new();

    /// <summary>多源回退状态机（DESIGN ，/）：每次 Apply 从当轮清单重建；引擎事件线程独占访问。</summary>
    private CacheSourceFallback? _sourceFallback;

    /// <summary>缓存重试策略：包级兜底 / 载荷获取 / 校验三重试点共用每包预算
    /// （指数退避 + 连续无进展终止）；每次 Apply 复位。引擎事件线程独占访问。</summary>
    private CacheRetryPolicy _cacheRetryPolicy = new();

    /// <summary>下载侧辅助状态锁（缓存命中识别集合；引擎事件线程读写）。</summary>
    private readonly object _downloadSideLock = new();

    /// <summary>自本包 CachePackageBegin 起是否发生过获取（Acquire）——用于区分「缓存命中跳过下载」与真实下载。</summary>
    private readonly HashSet<string> _acquiredSincePackageBegin = new(StringComparer.Ordinal);

    /// <summary>远程载荷键集合（CacheAcquireBegin 时 PayloadContainerId 为空 = 不在内嵌容器中，才走下载源）——
    /// 排除伴生内嵌载荷（如 PayloadToolDependencies）的获取 / 校验事件对回退计数与换源播报的干扰。</summary>
    private readonly HashSet<string> _remotePayloadKeys = new(StringComparer.Ordinal);

    /// <summary>已播报换源的（包 id, 源下标）组合——引擎的校验重试会重复触发获取开始，换源只播报一次。</summary>
    private readonly HashSet<string> _announcedRotations = new(StringComparer.Ordinal);

    public LabelFrameBootstrapperBa()
    {
        // 启动命令（握手期 OnCreate 传入）：动作（Install/Uninstall/…）与显示级别决定交互 / 非交互路径
        Create += (_, args) => _command = args.Command;

        DetectComplete += (_, args) => _detectCompleted.TrySetResult(args.Status);

        // 包级口径（DESIGN）：记录同 UpgradeCode 相关 Bundle（已装引导程序）版本——升级走查证据 + 日志留痕
        // 不驱动安装决策（组件级清单已覆盖用户视角）
        DetectRelatedBundle += (_, args) =>
        {
            Log($"检测到相关引导程序：{args.ProductCode}，版本 {args.Version}，关系 {args.RelationType}，{(args.PerMachine ? "机器级" : "用户级")}。");
        };
        PlanPackageComplete += (_, args) =>
        {
            lock (_plannedPackagesLock)
            {
                _plannedPackages.Add(new PlannedPackage(args.PackageId, args.Requested));
            }
        };
        PlanComplete += (_, args) =>
        {
            lock (_runLock)
            {
                _planCompleted.TrySetResult(args.Status);
            }
        };
        ApplyComplete += (_, args) =>
        {
            lock (_runLock)
            {
                _applyCompleted.TrySetResult(args.Status);
            }

            UpdateState(state => state with
            {
                Phase = args.Status == 0 ? InstallPhase.Completed : InstallPhase.Failed,
                ApplyStatus = args.Status,
                Restart = args.Restart,
                OverallPercentage = 100,
            });
        };

        // ---- 进度 / 分包事件（DESIGN UI 事件映射 + 下载侧细化，/） ----
        CachePackageBegin += (_, args) =>
        {
            lock (_downloadSideLock)
            {
                _acquiredSincePackageBegin.Remove(args.PackageId);
            }

            UpdateState(state => state with
            {
                Packages = WithPackage(state, args.PackageId, new PackageRunState(PackagePhase.Downloading, 0)),
                CurrentPackageId = args.PackageId,
            });
        };
        CacheAcquireProgress += (_, args) => UpdateState(state =>
        {
            // 按组件粒度下载进度（下载侧细化——OverallPercentage 之外补单包百分比）
            var percent = args.Total > 0 ? (int)Math.Min(100, 100 * args.Progress / args.Total) : 0;
            return state with
            {
                Phase = InstallPhase.Downloading,
                CurrentPackageId = args.PackageOrContainerId,
                OverallPercentage = Math.Max(state.OverallPercentage, args.OverallPercentage),
                Packages = WithPackage(state, args.PackageOrContainerId, run => run with { Percent = percent }),
            };
        });
        CachePackageComplete += (_, args) =>
        {
            UpdateState(state => state with
            {
                Packages = WithPackage(state, args.PackageId, new PackageRunState(args.Status == 0 ? PackagePhase.Downloaded : PackagePhase.Failed, args.Status)),
            });

            // 多源回退兜底驱动（DESIGN）：包级缓存失败且仍有未试源 → 覆写引擎动作为 Retry
            // 下一轮 CacheAcquireResolving 由 CacheSourceFallback 提供下一源（无源则不空转，走失败报告）。
            // 重试预算：只判「有无未试源」不判「重试间是否推进」的兜底重试对无源可换类失败
            // （附加容器获取 0x80070002——失败不进多源回退计数）形成无退避无终止热循环（真机实测 5 分钟 9.3 万次重试、
            // 日志 219–240MB）；改经 CacheRetryPolicy 指数退避 + 连续无进展三次即终止交失败报告页。
            if (args.Status != 0 && SourceFallback?.HasUntriedSources(args.PackageId) == true)
            {
                var decision = _cacheRetryPolicy.ShouldRetry(args.PackageId, SourceFallback.FailureCount(args.PackageId));
                if (decision.AllowRetry)
                {
                    Log($"多源回退：组件 {args.PackageId} 获取失败（0x{args.Status:X8}），清单内仍有未尝试的源，驱动引擎重试换源（第 {decision.GrantedRetries} 次，退避 {(int)decision.Delay.TotalMilliseconds}ms）。");
                    Thread.Sleep(decision.Delay); // 引擎缓存事件回调线程——阻塞应答即重试节流
                    args.Action = BOOTSTRAPPER_CACHEPACKAGECOMPLETE_ACTION.Retry;
                }
                else
                {
                    Log($"多源回退：组件 {args.PackageId} 连续 {decision.StalledRetries} 次重试无进展（重试未推进源游标），停止重试，交引擎按失败回滚进失败报告。");
                    AppendError($"[{args.PackageId}] 连续 {decision.StalledRetries} 次获取重试均无进展，已停止重试——请查看安装日志定位失败组件（常见：安装源不完整或被占用）。");
                }
            }
        };

        // ---- 多源回退与下载侧事件（引擎事件线程调用，状态经 UpdateState 快照发布） ----
        // WiX 版本对应：Burn v3 的 ResolveSource（BA 以 DownloadSource 提供备选源）在 v4+（本仓 v7）拆为
        // 「CacheAcquireBegin / CacheAcquireComplete 内调用 IEngine.SetDownloadSource」+ 事件返回 Action=Retry——
        // 官方文档口径（事件说明："The BA can change the source using SetLocalSource or SetDownloadSource"）。
        CacheAcquireBegin += (_, args) => HandleCacheAcquireBegin(args);
        CacheAcquireComplete += (_, args) => HandleCacheAcquireComplete(args);
        CacheVerifyComplete += (_, args) => HandleCacheVerifyComplete(args);
        // 缓存命中识别：获取开始前先做「哈希跳过获取」校验（CacheContainerOrPayloadVerify*）——本包未发生获取即命中缓存
        CacheContainerOrPayloadVerifyBegin += (_, args) =>
        {
            bool acquired;
            lock (_downloadSideLock)
            {
                acquired = args.PackageOrContainerId is not null && _acquiredSincePackageBegin.Contains(args.PackageOrContainerId);
            }

            if (!acquired)
            {
                UpdateState(state => state with
                {
                    DownloadHint = "组件已在本地缓存（校验通过后跳过下载）。",
                    Packages = WithPackage(state, args.PackageOrContainerId, run => run with { FromCache = true }),
                });
            }
        };

        ExecutePackageBegin += (_, args) =>
        {
            if (args.ShouldExecute)
            {
                UpdateState(state => state with
                {
                    Packages = WithPackage(state, args.PackageId, new PackageRunState(PackagePhase.Installing, 0)),
                    CurrentPackageId = args.PackageId,
                });
            }
        };
        ExecuteProgress += (_, args) => UpdateState(state => state with
        {
            Phase = InstallPhase.Installing,
            OverallPercentage = Math.Max(state.OverallPercentage, args.OverallPercentage),
        });
        ExecutePackageComplete += (_, args) => UpdateState(state => state with
        {
            Packages = WithPackage(state, args.PackageId, new PackageRunState(args.Status == 0 ? PackagePhase.Succeeded : PackagePhase.Failed, args.Status)),
        });

        // 引擎错误（下载失败 / 包退出码非零等）：追加中文报告素材，不打断流程（失败判定以阶段 Status 为准）
        Error += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.ErrorMessage))
            {
                AppendError($"[{args.PackageId ?? "引擎"}] 0x{args.ErrorCode:X8} {args.ErrorMessage}");
            }
        };
    }

    /// <summary>失败退出码（Windows 安装致命错误惯例 ERROR_INSTALL_FAILURE = 1603）。</summary>
    private const int ExitCodeFailure = 1603;

    /// <summary>安装状态变化通知（引擎事件线程触发；UI 订阅方自行封送到 UI 线程）。</summary>
    public event Action? StateChanged;

    /// <summary>当前安装状态快照（线程安全读取）。</summary>
    public InstallState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    /// <summary>启动时的运行时探测结果（确认页「已装则跳过」标注与完成页摘要共用）。</summary>
    public RuntimeProbeResult RuntimeStatus { get; private set; } = new(false, null, false, null, false);

    protected override void Run()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // UI 线程未处理异常显式诊断（异常不得被吞）：写 Burn 日志 + 对话框后退出，不静默继续
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        var command = _command;

        // 布局目录生成模式：--layout <目录>（双横线，引擎透传 BA）或引擎原生 -layout
        // （LaunchAction.Layout + LayoutDirectory，单横线）→ 同一生成流程——优先于一切安装路径（生成机无需问卷 / 探测）；
        // 参数无效（--layout 缺值等）同样进入本路径显式失败，不静默落到安装向导
        var layoutArguments = LayoutModeArguments.Parse(command?.CommandLine);
        var layoutDirectory = layoutArguments.LayoutDirectory ?? command?.LayoutDirectory;
        if (layoutArguments.Errors.Count > 0 || !string.IsNullOrWhiteSpace(layoutDirectory))
        {
            RunLayoutGeneration(layoutDirectory, layoutArguments);
            return;
        }

        // 运行时探测变量（链内 DetectCondition 消费，DESIGN）：必须在 engine.Detect 之前写入
        // 探测异常按「未装」处理（fail-open 到计划安装，运行时安装器自身幂等兜底）
        try
        {
            RuntimeStatus = new RuntimeProbe().Probe();
            Log($"运行时探测：.NET Desktop Runtime = {(RuntimeStatus.DesktopRuntimeInstalled ? $"已装 {RuntimeStatus.DesktopRuntimeVersion}" : "未装")}；ASP.NET Core Runtime = {(RuntimeStatus.AspNetCoreRuntimeInstalled ? $"已装 {RuntimeStatus.AspNetCoreRuntimeVersion}" : "未装")}；WebView2 = {(RuntimeStatus.WebView2Installed ? "已装" : "未装")}");
        }
        catch (Exception ex)
        {
            Log($"运行时探测失败（按未装处理，引擎兜底）：{ex}");
        }

        engine.SetVariableNumeric(BundleVariableMap.DesktopRuntimeVariable, RuntimeStatus.DesktopRuntimeInstalled ? 1 : 0);
        engine.SetVariableNumeric(BundleVariableMap.AspNetCoreRuntimeVariable, RuntimeStatus.AspNetCoreRuntimeInstalled ? 1 : 0);
        engine.SetVariableNumeric(BundleVariableMap.WebView2Variable, RuntimeStatus.WebView2Installed ? 1 : 0);

        // 落位目标目录于启动期写入：纯函数重算（ProgramData 派生），交互 / 非交互路径全覆盖——
        // ARP 卸载与升级链移除旧 Bundle 的会话不进问卷向导，UninstallArguments 的 [变量] 引用须在此会话内可解析
        engine.SetVariableString(BundleVariableMap.WebUiTargetDirVariable, BundleVariableMap.WebUiTargetDir(), false);
        engine.SetVariableString(BundleVariableMap.PluginZebraTargetDirVariable, BundleVariableMap.PluginZebraTargetDir(), false);

        // 落位凭据探测：读落位目录凭据文件（labelframe-bundle-placement）比对当版 Bundle 版本——
        // 「本版本 Bundle 落位在位」才写 1（清理包 DetectCondition 消费：Present → 卸载 / Modify 改选时执行 -clean）；
        // ARP 卸载非交互会话同样在此判定（卸载的清理包 Present 依据）。用户经客户端通道 / 手动放置的插件无凭据 → 永不 1
        var bundleVersion = ReadBundleVersion();
        var webUiPlaced = Placement.PlacementMarker.IsPlacedByBundle(BundleVariableMap.WebUiTargetDir(), bundleVersion);
        var zebraPlaced = Placement.PlacementMarker.IsPlacedByBundle(BundleVariableMap.PluginZebraTargetDir(), bundleVersion);
        engine.SetVariableNumeric(BundleVariableMap.WebUiPlacementPresentVariable, webUiPlaced ? 1 : 0);
        engine.SetVariableNumeric(BundleVariableMap.ZebraPluginPlacementPresentVariable, zebraPlaced ? 1 : 0);
        Log($"落位凭据探测：Bundle 版本 {bundleVersion ?? "<未知>"}；webui 在位 = {(webUiPlaced ? "是" : "否")}；zebra 插件在位 = {(zebraPlaced ? "是" : "否")}");

        // 非交互启动（升级链补全）：升级时 Burn 以 Uninstall 动作驱动旧 Bundle（RelatedBundle 升级链尾）
        // ARP 卸载 / 静默参数同理——此时不进问卷向导（无人应答会卡死升级链），自动 Detect → Plan → Apply → Quit
        if (command is null)
        {
            Log("启动命令不可得（握手未完成），按交互安装路径继续。");
        }
        else if (command.Action != LaunchAction.Install
            || command.Display is Display.None or Display.Passive or Display.Embedded)
        {
            Log($"非交互启动：动作 {command.Action}，显示级别 {command.Display}——跳过向导，自动执行。");
            RunUnattended(command.Action);
            return;
        }

        // 布局目录隐式检测+ 命令行 --manifest 显式覆写（/ ：测试注入与指定版本安装路径）：
        // 优先级 = 命令行 > 同目录布局清单 > 稳定通道；就绪页进入即按此来源自动加载
        var session = new WizardSession();
        if (!string.IsNullOrWhiteSpace(layoutArguments.ManifestSource))
        {
            session.ManifestSource = layoutArguments.ManifestSource!;
            Log($"命令行指定清单来源：{layoutArguments.ManifestSource}");
        }
        else
        {
            var adjacentManifest = TryDetectAdjacentLayoutManifest();
            if (adjacentManifest is not null)
            {
                session.ManifestSource = adjacentManifest;
                Log($"检测到引导程序同目录安装清单，默认离线布局安装（清单所在目录 = 本地优先源）：{adjacentManifest}");
            }
        }

        using var wizard = CreateWizardOrExit(session);
        if (wizard is null)
        {
            return; // 装配失败已显式处理（诊断 + 非零退出）
        }

        var exitCode = 0;
        Application.ThreadException += (_, args) =>
        {
            Log($"向导 UI 未处理异常：{args.Exception}");
            MessageBox.Show(
                wizard,
                $"向导发生未处理异常，安装引导即将退出。\n\n{args.Exception.GetType().Name}: {args.Exception.Message}\n\n详情已写入安装日志。",
                "LabelFrame 安装引导",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            exitCode = ExitCodeFailure;
            wizard.Close();
        };

        // 触发 Detect（只读探测已装包状态；Plan 前必须 Detect 完成）
        engine.Detect();
        Application.Run(wizard);

        // 问卷关闭即结束 BA；执行过安装则以上轮 Apply 状态为准（0 = 成功，非零传给引擎退出码）
        lock (_stateLock)
        {
            if (_state.Phase is InstallPhase.Completed or InstallPhase.Failed)
            {
                exitCode = _state.ApplyStatus;
            }
        }

        engine.Quit(exitCode);
    }

    /// <summary>非交互执行（Uninstall / 静默启动）：Detect → Plan(action) → Apply → 以 Apply 状态 Quit（无 UI）。</summary>
    /// <remarks>
    /// 升级链的真实依赖：新 Bundle Apply 末段以 Uninstall 动作运行旧 Bundle EXE——旧 BA 若弹向导将无人应答、
    /// 升级链卡死（升级走查 S2 实测暴露）。卸载计划由包已装状态驱动（Present / Obsolete → 移除或跳过），问卷变量不参与。
    /// Apply 需要有效属主窗口句柄（out-of-proc BA 传 <see cref="IntPtr.Zero"/> 实测抛 ArgumentException）——
    /// 以隐藏窗口承载（仅用于 UAC / 引擎弹窗的属主，无需消息泵）。
    /// </remarks>
    private void RunUnattended(LaunchAction action)
    {
        var applyTimeout = TimeSpan.FromMinutes(30); // 无人值守不设 UI 超时；仅防进程滞留的上限
        try
        {
            engine.Detect();
            if (!_detectCompleted.Task.Wait(StageTimeout))
            {
                engine.Quit(ExitCodeFailure);
                return;
            }

            engine.Plan(action, BundleScope.Default);
            if (!CurrentPlanSource().Task.Wait(StageTimeout))
            {
                engine.Quit(ExitCodeFailure);
                return;
            }

            using var owner = new Form
            {
                ShowInTaskbar = false,
                WindowState = FormWindowState.Minimized,
                Visible = false,
            };
            engine.Apply(owner.Handle);
            if (!CurrentApplySource().Task.Wait(applyTimeout))
            {
                Log("非交互执行 Apply 超时，强制退出。");
                engine.Quit(ExitCodeFailure);
                return;
            }
        }
        catch (Exception ex)
        {
            Log($"非交互执行异常：{ex}");
            engine.Quit(ExitCodeFailure);
            return;
        }

        int status;
        lock (_stateLock)
        {
            status = _state.Phase == InstallPhase.Completed ? _state.ApplyStatus : ExitCodeFailure;
        }

        Log($"非交互执行完成：动作 {action}，状态 0x{status:X8}。");
        engine.Quit(status);
    }

    /// <summary>
    /// 布局目录生成模式（/ ，，DESIGN）：<c>--layout &lt;目录&gt;</c> / 引擎 <c>-layout</c>——
    /// 在线机器把当版全部组件 + 官方 manifest / latest 原样字节 + 引导 EXE 汇集到目标目录（目标机拷目录双击 EXE 即离线首装）。
    /// 纯 BA 侧下载（<see cref="OfflineLayoutBuilder"/>：urls 顺序回退 + sha256 逐字节校验 fail-closed），不进问卷、
    /// 不 Plan / Apply；EXE 源 = 自身（<c>WixBundleOriginalSource</c>）；进度经 <see cref="Ui.LayoutProgressForm"/> 呈现。
    /// </summary>
    private void RunLayoutGeneration(string? layoutDirectory, LayoutModeArguments arguments)
    {
        var display = _command?.Display ?? Display.Full;
        int exitCode;
        string message;
        if (string.IsNullOrWhiteSpace(layoutDirectory))
        {
            exitCode = ExitCodeFailure;
            message = arguments.Errors.Count > 0
                ? "布局生成参数无效：" + string.Join("；", arguments.Errors)
                : "布局生成缺少目标目录（用法：--layout <目录> 或 --layout=<目录>）。";
            Log(message);
        }
        else
        {
            var source = string.IsNullOrWhiteSpace(arguments.ManifestSource)
                ? WizardSession.StableChannelManifestUrl
                : arguments.ManifestSource!;
            Log($"布局目录生成开始：目标 {layoutDirectory}，清单来源 {source}。");

            var form = new Ui.LayoutProgressForm(Log);
            // Progress 在 UI 线程构造：回调经同步上下文封送（LayoutProgressForm.Report 自带 InvokeRequired 防御）
            var progress = new Progress<OfflineLayoutProgress>(form.Report);
            var completion = new TaskCompletionSource<(int ExitCode, string Message)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = new HttpClient();
                    var manifestJson = await InstallManifestLoader.LoadTextAsync(source, http, form.CancellationToken).ConfigureAwait(false);
                    var manifest = InstallManifest.Parse(manifestJson);
                    var latestJson = await TryLoadLatestTextAsync(source, http, form.CancellationToken).ConfigureAwait(false);
                    var bootstrapper = GetBundleOriginalSourceOrFail();
                    var result = await OfflineLayoutBuilder.BuildAsync(
                        manifest, manifestJson, latestJson, layoutDirectory!, bootstrapper, http, progress,
                        cancellationToken: form.CancellationToken).ConfigureAwait(false);
                    completion.TrySetResult((0,
                        $"布局目录生成完成：{layoutDirectory}\n"
                        + $"组件 {result.ComponentCount} 个（下载 {result.DownloadedCount} / 复用 {result.ReusedCount}），"
                        + "install-manifest.json" + (latestJson is null ? string.Empty : " + latest.json") + " 已写入，"
                        + $"引导 EXE 已复制（{result.BootstrapperFileName}）。\n\n把整个目录拷贝到目标机器，直接运行其中的引导程序即可离线安装。"));
                }
                catch (Exception ex)
                {
                    completion.TrySetResult((ExitCodeFailure, $"布局目录生成失败：{ex.Message}"));
                }
            });
            form.AttachCompletion(completion.Task);
            Application.Run(form);
            (exitCode, message) = completion.Task.GetAwaiter().GetResult();
            Log($"布局目录生成结束：exit=0x{exitCode:X8}。{message}");
        }

        if (display == Display.Full)
        {
            MessageBox.Show(
                message,
                "LabelFrame 离线布局目录",
                MessageBoxButtons.OK,
                exitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        engine.Quit(exitCode);
    }

    /// <summary>latest.json 原样文本尽力读取（来源可推导才读；失败静默跳过——指针文件可选， 口径）。</summary>
    private static async Task<string?> TryLoadLatestTextAsync(string manifestSource, HttpClient http, CancellationToken cancellationToken)
    {
        var source = LatestPointer.DeriveSource(manifestSource);
        if (source is null)
        {
            return null;
        }

        try
        {
            return await InstallManifestLoader.LoadTextAsync(source, http, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>引导 EXE 自身路径（<c>WixBundleOriginalSource</c>——out-of-proc BA 的 Assembly.Location 是引擎解压临时目录，不可用）。</summary>
    private string GetBundleOriginalSourceOrFail()
    {
        string path;
        try
        {
            path = engine.GetVariableString("WixBundleOriginalSource");
        }
        catch (Exception ex)
        {
            throw new OfflineLayoutException($"无法定位引导 EXE（WixBundleOriginalSource 读取失败：{ex.Message}）——布局目录必须内含引导 EXE。");
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new OfflineLayoutException($"无法定位引导 EXE（WixBundleOriginalSource = \"{path}\"）——布局目录必须内含引导 EXE。");
        }

        return path;
    }

    /// <summary>引导 EXE 同目录的布局清单检测（隐式检测）：在位返回路径，否则 / 不可得返回 null（按无布局安装处理）。</summary>
    private string? TryDetectAdjacentLayoutManifest()
    {
        try
        {
            var folder = engine.GetVariableString("WixBundleOriginalSourceFolder");
            if (string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }

            var candidate = Path.Combine(folder, OfflineLayoutNaming.ManifestFileName);
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception)
        {
            return null; // 变量尚未建立等——兜底按无布局处理（不阻断安装流程）
        }
    }

    /// <summary>安装主入口（确认页「安装」触发）：问卷答案 → Burn 变量（含落位目标）→ Plan → Apply → 终态。</summary>
    /// <remarks>失败后可再次调用（重试 = 全新 Plan + Apply；链内已装包由引擎检测跳过，幂等口径见）。</remarks>
    public async Task<InstallState> ExecuteInstallAsync(WizardSession session, IntPtr windowHandle)
    {
        lock (_runLock)
        {
            _planCompleted = CreateSource();
            _applyCompleted = CreateSource();
        }

        lock (_plannedPackagesLock)
        {
            _plannedPackages.Clear();
        }

        // 每轮 Apply 从当轮清单重建多源回退状态机（重试 = 失败计数归零、从头按有效源序；
        // / ：本地清单所在目录作为隐式优先源目录——布局文件在位的包有效源序 = [本地文件] ++ urls）
        _sourceFallback = CacheSourceFallback.FromManifest(
            session.Manifest ?? throw new InvalidOperationException("尚未加载安装清单，无法开始安装。"),
            session.LocalSourceDirectory);
        _cacheRetryPolicy.Reset();  // 缓存重试预算同轮复位（「重试」= 全新 Plan + Apply）

        SetState(new InstallState { Phase = InstallPhase.Planning });

        var plan = session.BuildPlan();
        var variables = BundleVariableMap.ToVariables(plan);
        WriteVariablesToEngine(variables);

        // evergreen 源变量：布局目录本地文件在位优先 → 清单 urls；空 = 工具官方 fwlink 兜底
        // （获取与验签在 WebView2Runtime 包执行期的工具内完成，引擎零远程载荷—— evergreen 获取机制）
        var webView2Source = EvergreenRuntime.BuildWebView2SourceValue(session.Manifest, session.LocalSourceDirectory);
        engine.SetVariableString(BundleVariableMap.WebView2SourceVariable, webView2Source, false);
        Log($"WebView2 evergreen 源：{(string.IsNullOrEmpty(webView2Source) ? "<空——工具按官方 fwlink 兜底>" : webView2Source)}");

        await WaitStageAsync(_detectCompleted.Task, "包探测（Detect）").ConfigureAwait(true);
        engine.Plan(LaunchAction.Install, BundleScope.Default);
        var planStatus = await WaitStageAsync(CurrentPlanSource().Task, "安装计划（Plan）").ConfigureAwait(true);

        if (planStatus != 0)
        {
            return FailAs(unchecked((int)0x8000F0DE), $"安装计划（Plan）阶段失败（0x{planStatus:X8}），请查看安装日志。");
        }

        SetState(State with { Phase = InstallPhase.Downloading, OverallPercentage = 0 });
        Log($"开始安装：预设 {variables[BundleVariableMap.PresetVariable]}，组件 [{string.Join(",", plan.Components.Select(item => item.Component.Id))}]");

        // Apply 无超时：厂商直链下载耗时取决于网络；取消 / 退出由向导关闭与引擎 Quit 承担。
        // 失败不走异常路径——ApplyComplete 事件已把真实引擎状态写入快照（失败报告展示真实状态码与回滚语义）
        engine.Apply(windowHandle);
        await CurrentApplySource().Task.ConfigureAwait(true);

        lock (_stateLock)
        {
            // 装后服务端地址落位（与 WinHost HostConfigStore 同一事实源）：
            // 仅计划含打印客户端且问卷已确认地址时写入；写失败不改变安装结果（客户端「设置」页仍可手改）
            if (_state.Phase == InstallPhase.Completed)
            {
                SaveServerUrlIfPlanned(plan, session);
            }

            return _state;
        }
    }

    /// <summary>装后服务端地址落位：写入 %ProgramData%\LabelFrame\Client\settings.json——装完客户端即按问卷地址连接服务端，无需进设置页手改。</summary>
    private void SaveServerUrlIfPlanned(Topology.TopologyPlan plan, WizardSession session)
    {
        var normalized = ServerUrlInput.Normalize(session.ServerUrl);
        if (normalized is null || !plan.Components.Any(item => item.Component.Id == "client-msi"))
        {
            return;
        }

        try
        {
            new ServerUrlStore().Save(normalized);
            Log($"服务端地址已按问卷写入客户端配置（{ServerUrlStore.DefaultPath()}）：{normalized}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log($"服务端地址写入失败（可在客户端「设置」页手动填写）：{ex.Message}");
        }
    }

    /// <summary>记录引擎日志（供 Burn 日志（%TEMP%\LabelFrame*.log）回溯问卷决策与执行序列）。</summary>
    public void Log(string message) => engine.Log(LogLevel.Standard, message);

    /// <summary>Burn 日志文件路径（失败报告展示；不可得时返回 null，由 UI 兜底 %TEMP% 通配说明）。</summary>
    public string? GetBundleLogPath()
    {
        try
        {
            var path = engine.GetVariableString("WixBundleLog");
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception)
        {
            return null; // 变量尚未建立（日志未启动）等——兜底文案处理
        }
    }

    /// <summary>当版 Bundle 版本（引擎内建变量；落位凭据探测比对口径，/）。读取失败返回 null。</summary>
    private string? ReadBundleVersion()
    {
        try
        {
            var value = engine.GetVariableString("WixBundleVersion");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception)
        {
            return null; // 变量尚未建立等极端场景——凭据比对按不匹配处理（fail-closed 到不清理）
        }
    }

    /// <summary>构造失败终态快照（Plan / Apply 前置阶段异常等未走到引擎事件的失败）。</summary>
    public InstallState FailAs(int status, string message)
    {
        AppendError(message);
        return UpdateState(state => state with { Phase = InstallPhase.Failed, ApplyStatus = status });
    }

    private void WriteVariablesToEngine(IReadOnlyDictionary<string, string> variables)
    {
        engine.SetVariableString(BundleVariableMap.PresetVariable, variables[BundleVariableMap.PresetVariable], false);
        engine.SetVariableNumeric(BundleVariableMap.ServerVariable, ParseFlag(variables[BundleVariableMap.ServerVariable]));
        engine.SetVariableNumeric(BundleVariableMap.ClientVariable, ParseFlag(variables[BundleVariableMap.ClientVariable]));
        engine.SetVariableNumeric(BundleVariableMap.WebUiVariable, ParseFlag(variables[BundleVariableMap.WebUiVariable]));
        engine.SetVariableNumeric(BundleVariableMap.ZebraPluginVariable, ParseFlag(variables[BundleVariableMap.ZebraPluginVariable]));
        // 落位目标目录已在启动期写入（Run 入口，：卸载会话同样需要 [变量] 可解析），此处不重复写
    }

    private TaskCompletionSource<int> CurrentPlanSource()
    {
        lock (_runLock)
        {
            return _planCompleted;
        }
    }

    private TaskCompletionSource<int> CurrentApplySource()
    {
        lock (_runLock)
        {
            return _applyCompleted;
        }
    }

    private void AppendError(string message) =>
        UpdateState(state => state with { Errors = [.. state.Errors, message] });

    /// <summary>多源回退状态机（引擎事件线程与 Apply 调用线程间仅整体替换，读取不加锁——引用原子性足够）。</summary>
    private CacheSourceFallback? SourceFallback => _sourceFallback;

    /// <summary>启动命令（握手期 Create 事件传入；Run 时已就位）。</summary>
    private IBootstrapperCommand? _command;

    /// <summary>
    /// 多源回退 源注入点（DESIGN ，/）：每次获取尝试开始时，按清单 urls 顺序把
    /// urls[min(失败数, count-1)] 设为引擎下载源（<see cref="IEngine.SetDownloadSource"/>——v3 ResolveSource
    /// 的 DownloadSource 覆写在 v7 的官方后继；运行时清单是源顺序权威，：顺序即优先级）。
    /// 仅作用于<b>远程载荷</b>（不在内嵌容器中——伴生内嵌载荷经容器解压获取，与下载源无关）；
    /// 不覆写 <c>CacheOperation</c>：本地 / 容器来源（缓存命中、旁置文件）保持引擎优先，保住断网续装能力。
    /// </summary>
    private void HandleCacheAcquireBegin(CacheAcquireBeginEventArgs args)
    {
        // 附加容器获取本地源注入（DESIGN 落位机制）：容器获取形态（payload id 为空
        // 托管事件对容器取 string.Empty 而非 null， 沙箱实测）只发生在引擎以「引擎桩副本」运行的会话——
        // 提权工作副本与包缓存 Bundle EXE 均只含引擎桩（无 WixAttachedContainer），
        // 内嵌载荷须经源解析搜索路径获取，原始源变量失效即 0x80070002 获取永不能完成（真机升级链三连败根因）。
        // 注入运行中的引导 EXE（WixBundleOriginalSource）为容器首搜索路径：引擎仍按最小尺寸探测校验，
        // 路径失效（不在位 / 截断）自动落回引擎默认搜索序（无害兜底）。
        if (string.IsNullOrEmpty(args.PayloadId) && string.Equals(args.PackageOrContainerId, AttachedContainerId, StringComparison.Ordinal))
        {
            InjectAttachedContainerSource();
        }

        var payloadKey = PayloadKeyOf(args.PackageOrContainerId, args.PayloadId);
        var isRemote = string.IsNullOrEmpty(args.PayloadContainerId);
        if (isRemote)
        {
            lock (_downloadSideLock)
            {
                _remotePayloadKeys.Add(payloadKey);
                if (args.PackageOrContainerId is not null)
                {
                    _acquiredSincePackageBegin.Add(args.PackageOrContainerId);
                }
            }
        }

        if (SourceFallback is null || !isRemote)
        {
            return; // 未装载清单（防御分支）或内嵌容器载荷：不参与多源回退
        }

        var outcome = SourceFallback.ResolveAcquire(args.PackageOrContainerId);
        if (outcome.Decision != SourceFallbackDecision.UseSource)
        {
            return; // 源耗尽 / 未知包：保留引擎当前源（构建期 DownloadUrl）
        }

        if (outcome.IsLocal)
        {
            // 本地源优先（/ ， 源解析顺序契约）：布局目录文件经 SetLocalSource 交给引擎本地
            // 获取——引擎对本地源副本同样按包内嵌摘要（= manifest sha256，构建期实测锁定）强制校验（fail-closed：
            // 篡改布局文件 → 校验失败 → 按既有推进语义换源重取，断网则源耗尽失败，篡改内容绝不落装）
            engine.SetLocalSource(args.PackageOrContainerId, args.PayloadId, outcome.LocalPath!);
            var localName = Path.GetFileName(outcome.LocalPath!);
            Log($"本地源命中：组件 {args.PackageOrContainerId} 使用布局目录文件 {localName}（源 {outcome.SourceIndex + 1}/{outcome.SourceCount}，离线优先不联网）。");
            UpdateState(state => state with
            {
                CurrentSourceIndex = outcome.SourceIndex,
                CurrentSourceCount = outcome.SourceCount,
                DownloadHint = $"本地布局源：{localName}（离线优先，不联网获取）",
            });
            return;
        }

        if (string.IsNullOrEmpty(outcome.Url))
        {
            return; // 防御（UseSource 携带空 URL）：保留引擎当前源
        }

        if (!string.Equals(args.DownloadUrl, outcome.Url, StringComparison.Ordinal))
        {
            engine.SetDownloadSource(args.PackageOrContainerId, args.PayloadId, outcome.Url, null, null, null);
        }

        var host = DescribeHost(outcome.Url!);
        if (outcome.Rotated)
        {
            var announcementKey = $"{args.PackageOrContainerId}|{outcome.SourceIndex}";
            lock (_downloadSideLock)
            {
                if (_announcedRotations.Add(announcementKey))
                {
                    Log($"多源回退：组件 {args.PackageOrContainerId} 切换到第 {outcome.SourceIndex + 1}/{outcome.SourceCount} 个源（{host}）。");
                }
            }
        }

        UpdateState(state => state with
        {
            CurrentSourceIndex = outcome.SourceIndex,
            CurrentSourceCount = outcome.SourceCount,
            DownloadHint = outcome.Rotated
                ? $"主源获取失败，已切换备用下载源（第 {outcome.SourceIndex + 1}/{outcome.SourceCount} 个）：{host}"
                : $"下载源 {outcome.SourceIndex + 1}/{outcome.SourceCount}：{host}",
        });
    }

    /// <summary>获取 / 校验事件的载荷键（包载荷的 payload id = 包 id；容器获取无 payload id 时回退包 id）。</summary>
    private static string PayloadKeyOf(string? packageOrContainerId, string? payloadId) => payloadId ?? packageOrContainerId ?? string.Empty;

    /// <summary>WiX v4+ 内嵌载荷默认附加容器 id（Burn 引擎内置命名，链内 Compressed=yes 载荷全部收入其中）。</summary>
    private const string AttachedContainerId = "WixAttachedContainer";

    /// <summary>
    /// 附加容器本地源注入：把运行中的引导 EXE 原始路径交给引擎作为
    /// <c>WixAttachedContainer</c> 的首搜索路径（<see cref="IEngine.SetLocalSource"/>——与 本地源注入同一事件入口）。
    /// 诊断行（原始源在位性）随注入留痕——此类故障此前在 Burn 日志中无 BA 侧可定位信号。
    /// </summary>
    private void InjectAttachedContainerSource()
    {
        string? originalSource = null;
        try
        {
            originalSource = engine.GetVariableString("WixBundleOriginalSource");
        }
        catch (Exception)
        {
            originalSource = null; // 变量尚未建立等极端场景——按不可注入处理
        }

        var present = !string.IsNullOrWhiteSpace(originalSource) && File.Exists(originalSource);
        Log($"附加容器获取：引导 EXE 原始源 = {originalSource ?? "<未知>"}（{(present ? "在位，已注入为容器本地源" : "不在位，保留引擎默认搜索序")}）。");
        if (present)
        {
            engine.SetLocalSource(AttachedContainerId, null, originalSource!);
        }
    }

    /// <summary>该载荷是否为远程下载载荷（依据获取开始时的容器归属登记；未登记 = 内嵌或未发生获取）。</summary>
    private bool IsRemotePayload(string? packageOrContainerId, string? payloadId)
    {
        lock (_downloadSideLock)
        {
            return _remotePayloadKeys.Contains(PayloadKeyOf(packageOrContainerId, payloadId));
        }
    }

    /// <summary>单次获取结束（下载 / 拷贝，仅远程载荷）：失败时计数 + 分类呈现，仍有未试源则驱动引擎逐载荷重试（下一轮换源）。</summary>
    private void HandleCacheAcquireComplete(CacheAcquireCompleteEventArgs args)
    {
        if (args.Status == 0)
        {
            UpdateState(state => state with
            {
                Packages = WithPackage(state, args.PackageOrContainerId, run => run with { Percent = 100 }),
            });
            return;
        }

        if (!IsRemotePayload(args.PackageOrContainerId, args.PayloadId))
        {
            return; // 内嵌容器载荷的获取失败不参与回退计数（其来源为解压，与下载源无关）
        }

        var category = DownloadFailureClassifier.Classify(args.Status);
        var fallback = SourceFallback;
        fallback?.RecordAcquireFailure(args.PackageOrContainerId);
        var hasUntried = fallback?.HasUntriedSources(args.PackageOrContainerId) == true;

        UpdateState(state => state with
        {
            Packages = WithPackage(state, args.PackageOrContainerId, run => new PackageRunState(
                PackagePhase.Failed,
                args.Status,
                Percent: run.Percent,
                FromCache: run.FromCache,
                Failure: category)),
        });
        var describe = DownloadFailureClassifier.Describe(category);
        var exhaustion = hasUntried
            ? string.Empty
            : $"（清单全部 {SourceFallback?.ResolveAcquire(args.PackageOrContainerId).SourceCount ?? 1} 个源均已尝试失败——源耗尽）";
        AppendError($"[{args.PackageOrContainerId ?? "引擎"}] 下载获取失败（0x{args.Status:X8}）：{describe}{exhaustion}");
        Log($"下载获取失败：[{args.PackageOrContainerId ?? "引擎"}] 0x{args.Status:X8}，分类 = {category}。{describe}{exhaustion}");

        if (hasUntried)
        {
            // 逐载荷重试 → 重新 Resolving → 换源；重试预算：换源推进源游标属合法重试不衰减
            // 退避（事件回调内阻塞）避免快失败源的紧密轮询
            var decision = _cacheRetryPolicy.ShouldRetry(args.PackageOrContainerId, fallback?.FailureCount(args.PackageOrContainerId) ?? 0);
            if (decision.AllowRetry)
            {
                Thread.Sleep(decision.Delay);
                args.Action = BOOTSTRAPPER_CACHEACQUIRECOMPLETE_ACTION.Retry;
            }
            else
            {
                Log($"多源回退：组件 {args.PackageOrContainerId} 获取重试连续 {decision.StalledRetries} 次无进展，停止重试交引擎失败处置。");
            }
        }
    }

    /// <summary>获取后校验（哈希，仅远程载荷）结束：坏哈希（篡改内容 / 传输损坏）同样按序换源——
    /// 引擎自身最多推荐 2 次重取，用尽后若清单仍有未试源则追加驱动 RetryAcquisition（有界：每次失败推进源游标）。</summary>
    private void HandleCacheVerifyComplete(CacheVerifyCompleteEventArgs args)
    {
        if (args.Status == 0 || !IsRemotePayload(args.PackageOrContainerId, args.PayloadId))
        {
            return;
        }

        var category = DownloadFailureClassifier.Classify(args.Status);
        var fallback = SourceFallback;
        fallback?.RecordVerifyFailure(args.PackageOrContainerId);
        var hasUntried = fallback?.HasUntriedSources(args.PackageOrContainerId) == true;

        UpdateState(state => state with
        {
            Packages = WithPackage(state, args.PackageOrContainerId, run => run with { Failure = category }),
        });
        var describe = DownloadFailureClassifier.Describe(category);
        AppendError($"[{args.PackageOrContainerId ?? "引擎"}] 校验失败（0x{args.Status:X8}）：{describe}");
        Log($"下载校验失败：[{args.PackageOrContainerId ?? "引擎"}] 0x{args.Status:X8}，分类 = {category}。{describe}");

        if (hasUntried && args.Recommendation == BOOTSTRAPPER_CACHEVERIFYCOMPLETE_ACTION.None)
        {
            // 重试预算：换源推进属合法重试；连续无进展超限即停（同载荷获取试点）
            var decision = _cacheRetryPolicy.ShouldRetry(args.PackageOrContainerId, fallback?.FailureCount(args.PackageOrContainerId) ?? 0);
            if (decision.AllowRetry)
            {
                Thread.Sleep(decision.Delay);
                args.Action = BOOTSTRAPPER_CACHEVERIFYCOMPLETE_ACTION.RetryAcquisition;
                Log($"多源回退：组件 {args.PackageOrContainerId} 校验失败且引擎重取额度用尽，清单内仍有未尝试的源，追加换源重取（第 {decision.GrantedRetries} 次，退避 {(int)decision.Delay.TotalMilliseconds}ms）。");
            }
            else
            {
                Log($"多源回退：组件 {args.PackageOrContainerId} 校验重取连续 {decision.StalledRetries} 次无进展，停止重试交引擎失败处置。");
            }
        }
    }

    /// <summary>源 URL → 展示主机（host:port；解析失败回退原串截断）。</summary>
    private static string DescribeHost(string url)
    {
        string candidate;
        try
        {
            var uri = new Uri(url);
            candidate = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }
        catch (UriFormatException)
        {
            candidate = url.Length <= 64 ? url : url.Substring(0, 64) + "…";
        }

        return candidate;
    }

    /// <summary>复制并更新单包状态（net48 Dictionary 无 (IDictionary, IComparer) 构造重载）。</summary>
    private static Dictionary<string, PackageRunState> WithPackage(InstallState state, string? packageId, PackageRunState run)
    {
        var packages = new Dictionary<string, PackageRunState>(StringComparer.Ordinal);
        foreach (var pair in state.Packages)
        {
            packages[pair.Key] = pair.Value;
        }

        if (packageId is not null)
        {
            packages[packageId] = run;
        }

        return packages;
    }

    /// <summary>复制并按现有值更新单包状态（保留未知字段；net48 无 Collection 表达式便捷写法）。</summary>
    private static Dictionary<string, PackageRunState> WithPackage(InstallState state, string? packageId, Func<PackageRunState, PackageRunState> update)
    {
        var packages = new Dictionary<string, PackageRunState>(StringComparer.Ordinal);
        foreach (var pair in state.Packages)
        {
            packages[pair.Key] = pair.Value;
        }

        if (packageId is not null)
        {
            packages[packageId] = packages.TryGetValue(packageId, out var existing)
                ? update(existing)
                : update(new PackageRunState(PackagePhase.Downloading, 0));
        }

        return packages;
    }

    private void SetState(InstallState state)
    {
        lock (_stateLock)
        {
            _state = state;
        }

        StateChanged?.Invoke();
    }

    private InstallState UpdateState(Func<InstallState, InstallState> update)
    {
        InstallState updated;
        lock (_stateLock)
        {
            updated = update(_state);
            _state = updated;
        }

        StateChanged?.Invoke();
        return updated;
    }

    /// <summary>
    /// 装配向导窗体（含首页装配，；会话由本类创建——布局目录隐式检测后改写默认清单来源）
    /// 失败时显式失败——诊断对话框 + Burn 日志 + 非零退出，绝不带着未装配状态（-1 索引、空白内容区）进入消息循环
    /// （Issue 验收回流教训）。
    /// </summary>
    private Ui.WizardForm? CreateWizardOrExit(WizardSession session)
    {
        try
        {
            return new Ui.WizardForm(this, session);
        }
        catch (Exception ex)
        {
            Log($"向导装配失败：{ex}");
            MessageBox.Show(
                $"向导装配失败，安装引导无法继续。\n\n{ex.GetType().Name}: {ex.Message}\n\n详情已写入安装日志。",
                "LabelFrame 安装引导",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            engine.Quit(ExitCodeFailure);
            return null;
        }
    }

    private static TaskCompletionSource<int> CreateSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static long ParseFlag(string value) => value == "1" ? 1 : 0;

    private static async Task<int> WaitStageAsync(Task<int> stage, string stageName)
    {
        var completed = await Task.WhenAny(stage, Task.Delay(StageTimeout)).ConfigureAwait(true);
        if (completed != stage)
        {
            throw new TimeoutException($"{stageName}超时（{StageTimeout.TotalSeconds:0} 秒），请查看安装日志。");
        }

        var status = await stage.ConfigureAwait(true);
        if (status != 0)
        {
            throw new InvalidOperationException($"{stageName}失败（0x{status:X8}），请查看安装日志。");
        }

        return status;
    }
}

/// <summary>Burn Plan 阶段产出的单个包计划状态（RequestState）。</summary>
internal sealed record PlannedPackage(string PackageId, RequestState State);

/// <summary>单包执行状态（phase + 退出码 + 下载侧细化字段，/）。</summary>
/// <param name="Phase">阶段。</param>
/// <param name="Status">引擎状态码（失败分类输入）。</param>
/// <param name="Percent">下载侧单包百分比（0-100；CacheAcquireProgress 的 Progress/Total）。</param>
/// <param name="FromCache">是否命中本地缓存跳过下载（获取前哈希校验通过）。</param>
/// <param name="Failure">下载侧失败分类（获取 / 校验失败时写入；null = 未发生或未分类）。</param>
internal sealed record PackageRunState(
    PackagePhase Phase,
    int Status,
    int Percent = 0,
    bool FromCache = false,
    LabelFrame.Bootstrapper.Downloads.DownloadFailureCategory? Failure = null);

/// <summary>单包阶段。</summary>
internal enum PackagePhase
{
    /// <summary>计划纳入但尚未开始。</summary>
    Pending,

    /// <summary>下载 / 校验中。</summary>
    Downloading,

    /// <summary>下载完成（等待执行）。</summary>
    Downloaded,

    /// <summary>安装 / 落位执行中。</summary>
    Installing,

    /// <summary>执行成功（含落位幂等跳过）。</summary>
    Succeeded,

    /// <summary>执行失败（引擎已回滚）。</summary>
    Failed,
}

/// <summary>安装整体阶段。</summary>
internal enum InstallPhase
{
    /// <summary>尚未开始（问卷中）。</summary>
    Idle,

    /// <summary>引擎 Plan 中。</summary>
    Planning,

    /// <summary>下载 / 校验包。</summary>
    Downloading,

    /// <summary>执行安装链。</summary>
    Installing,

    /// <summary>Apply 成功完成。</summary>
    Completed,

    /// <summary>失败（引擎已回滚本次已执行包）。</summary>
    Failed,
}

/// <summary>安装状态快照（不可变整体替换）。</summary>
internal sealed record InstallState
{
    public InstallPhase Phase { get; init; }

    public int OverallPercentage { get; init; }

    public string? CurrentPackageId { get; init; }

    /// <summary>当前下载源序号（0 起；-1 = 未知——多源回退提示，/）。</summary>
    public int CurrentSourceIndex { get; init; } = -1;

    /// <summary>当前组件清单源总数（0 = 未知）。</summary>
    public int CurrentSourceCount { get; init; }

    /// <summary>下载侧状态提示（换源发生 / 缓存命中；null = 无）。</summary>
    public string? DownloadHint { get; init; }

    public IReadOnlyDictionary<string, PackageRunState> Packages { get; init; } =
        new Dictionary<string, PackageRunState>(StringComparer.Ordinal);

    public IReadOnlyList<string> Errors { get; init; } = [];

    public int ApplyStatus { get; init; }

    public ApplyRestart Restart { get; init; }
}
