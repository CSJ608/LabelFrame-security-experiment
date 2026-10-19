# LabelFrame.Bootstrapper（三件）模块设计

> 覆盖：`src/LabelFrame.Bootstrapper`（34 文件）· `src/LabelFrame.Bootstrapper.Ba`（16 文件）· `src/LabelFrame.Bootstrapper.PayloadTool`（1 文件）· `test/LabelFrame.Bootstrapper.Tests`（25 文件）——三件共约 7,200 行 · 基线：`master` `831cb9e`（2026-10-12，迭代 138）
> 分工与模板说明见 [README.md](README.md)；安装链公共契约（manifest schema、链序表、卸载清理、多源回退口径等）见 [DESIGN.md](../DESIGN.md) §6——本文只引 §6 小节与决策号，重心放三件内部结构与运行时机制。

## 1. 职责与边界

**做什么**：LabelFrame 的 **Windows 安装引导程序（setup）**——一个小 EXE，回答少量问题（本机角色 / 打印机品牌 / 服务端地址 / 管理界面开关）后解析安装清单（install manifest，CI 随发版生成）、按需下载组件并完成安装。形态 = **WiX Burn Bundle + 托管 BA（out-of-proc）**：下载 / 校验 / 链装 / 回滚 / 提权 / 日志由 Burn 引擎承担，自研面 = 问卷编排、清单消费、多源回退决策、非 MSI 落位、升级呈现（决策 #114 → #122 形态修订）。三件分工：

| 件 | 目标框架 | 角色 |
|---|---|---|
| `LabelFrame.Bootstrapper`（共享库） | `net48;net10.0-windows` 双目标 | UI 无关核心逻辑：manifest 解析 / 拓扑解析 / 多源回退状态机 / 重试策略 / 失败分类 / 运行时与已装探测 / 升级评估 / 离线布局生成 / 非 MSI 落位 / evergreen 获取验签 / 问卷会话与导航状态机 |
| `LabelFrame.Bootstrapper.Ba` | `net48`（WinForms EXE） | Burn 托管 BA：引擎握手、事件接线（下载 / 执行进度 → 状态快照）、两层问卷向导 UI、非交互执行、`--layout` 布局生成模式、首显置前 |
| `LabelFrame.Bootstrapper.PayloadTool` | `net48`（控制台 EXE） | 非 MSI 组件落位工具（Bundle 链内 ExePackage 载体）：webui zip / `.lfplugin` 解压落位、卸载清理、WebView2 evergreen 获取验签安装 |

**不做什么**：

- manifest / latest.json 的**生成**——CI 发版流水线（`scripts/generate-install-manifest.ps1`）承担，引导只读解析（DESIGN §6.2）；
- Bundle 链定义与 MSI 打包——`packaging/bootstrapper/Bundle.wxs` 与 `packaging/*.wxs`（构建 = `scripts/build-bundle.ps1`，发版 = `release.yml` bundle job）；
- Linux 服务端安装——`scripts/install-server-linux.sh`（决策 #134）；Docker——Release `compose.yml` 指引；
- PDA（Android）装机——服务端下载中心扫码（决策 #119），不进 PC 引导；
- 应用内自更新 / 无人值守静默更新——明确不做（决策 #118；Velopack 记为未来立项候选）；
- 引导问卷与向导文案**中文单语**，i18n 不进 setup（决策 #164）。

## 2. 内部结构

### 2.1 共享库（src/LabelFrame.Bootstrapper）

| 分组 | 类型 | 角色 |
|---|---|---|
| Manifest | `InstallManifest` / `ManifestComponent` / `InstallManifestLoader` / `LatestPointer` / `InstallManifestFormatException` | 清单解析与校验（fail-closed：schema 超新拒绝、字段 / 枚举 / sha256 形态 / dependsOn 引用全断言）；来源 = 本地路径（同步读）或 URL（单次只读 GET）；`LatestPointer` = 清单新鲜度指针（解析失败返回 null 静默跳过，`DeriveSource` 由清单来源推导 latest.json 地址） |
| Topology | `ITopologyResolver` / `TopologyResolver` / `TopologyPreset` / `TopologyOptions` / `TopologyPlan` / `PlannedComponent` / `InstallTargets` / `BrandPluginMap` | 「角色 + 开关 → 组件集合」：核心组件 topologies 命中即纳入、开关组件（webui / plugin-*）仍受 topologies 过滤、dependsOn 闭包递归纳入、Kahn 拓扑序（同层取清单声明序，环拒绝）；`InstallTargets` 给确认页展示用目标位置描述；`BrandPluginMap` = brand → pluginId 桥（指令集维度：`zebra → labelframe-transport-zebra`、`tspl → labelframe-transport-tspl`） |
| Downloads | `CacheSourceFallback` / `CacheRetryPolicy` / `DownloadFailureClassifier` | 多源回退状态机（每链包失败计数，有效源序 `[布局本地文件] ++ urls`，`min(N, count-1)` 取源，计数达 count 即源耗尽）；缓存重试策略（指数退避 500ms×2^(n-1) 上限 30s + 连续无进展 3 次终止）；HRESULT → 失败分类（源不可达 / 网络中断 / 校验失败 / 未分类）+ 差异化中文提示 |
| OfflineLayout | `OfflineLayoutBuilder` / `OfflineLayoutNaming` / `LayoutModeArguments` / `OfflineLayoutException` | 布局目录生成（逐组件 urls 顺序下载、按策略校验一致才落位：常规 = sha256，evergreen = Authenticode 发布者验签；幂等复用）；命名约定（urls[0] 末段 → 固定名兜底表 → null）；`--layout` / `--manifest` 参数解析（Windows 风格命令行拆分） |
| Placement | `PayloadPlacer` / `PlacementMarker` | 非 MSI 落位内核：zip 清空目录整包解压（staging 临时目录近似原子替换）；`.lfplugin` 版本比较（新覆盖 / 同版幂等跳过 / 降级拒绝）；落位凭据文件（`.labelframe-bundle-placement`，内容 = Bundle 版本）与清理（只清 Bundle 落位目录，父目录已空才移除，失败尽力而为） |
| Prerequisites | `RuntimeProbe` / `EvergreenRuntime` / `EvergreenPayloadAcquirer` / `IAuthenticodeVerifier` / `WinTrustAuthenticodeVerifier` / `AuthenticodePublisherPolicy` | 运行时探测（.NET 双 runtime = `dotnet\shared\<框架>` 目录枚举文件版本探测；WebView2 = EdgeUpdate 注册表 HKLM+HKCU）；evergreen 源解析（本地优先 → urls）与获取验签编排；WinVerifyTrust P/Invoke 验签 + 发布者 CN 策略 |
| Printing | `InstalledPrinters` / `PrinterBrandDetector` | 已装打印机名枚举（读不到按空集）与品牌预选（指令集维度双规则：`ZDesigner → zebra`、`TSC → tspl`） |
| Upgrade | `LocalInstallProbe` / `UpgradeAssessor` / `UpgradePresentation` / `VersionSemantics` | 本机已装快照（MSI 注册表 `MsiEnumRelatedProducts`+`MsiGetProductInfo` 按 UpgradeCode 枚举取最高 / 插件落位目录 manifest.json——Zebra / TSPL 两官方插件各一版本字段 / 运行时复用）；组件级动作判定（Install / Upgrade / UpToDate / LocalNewer / NotTracked）；呈现文案纯函数；统一版本比较（可解析 `Version` 按其比较、缺失段视为 0，否则字符串 Ordinal） |
| Wizard | `WizardSession` / `WizardNavigator<TPage>` / `WizardForegroundActivator` / `ServerUrlStore` / `ServerUrlInput` / `BundleVariableMap` / `ChainPackageMap` / `ExecuteBoundaryNotice` | 问卷会话（UI 无关状态机，全程只读）；导航状态机（绝对索引 + 惰性装配 + 越界防御）；首显置前组合拳（7 个 Win32 调用点全注入）；服务端地址存取（与 WinHost `HostConfigStore` 同一事实源）与输入规范化；Burn 变量名常量与拓扑计划 → 变量映射；链包 id ↔ 组件 id 映射；执行边界 UI 明示文案 |
| Compatibility | `IsExternalInit` | net48 腿的 record 支持（`#if NET48` 条件编译） |

### 2.2 BA（src/LabelFrame.Bootstrapper.Ba）

- `Program.Main` → `ManagedBootstrapperApplication.Run(new LabelFrameBootstrapperBa())`：WiX v5.4+ out-of-proc 握手，UI 运行在引擎派生 STA 线程，Main 只承担握手。
- `LabelFrameBootstrapperBa`（BA 主体）：构造器接线全部引擎事件（Detect / Plan / Apply / Cache / Execute / Error）；`Run()` 路由四条路径——布局生成（`--layout` / `-layout`）→ 非交互（Uninstall / 静默，无 UI）→ 交互向导；`ExecuteInstallAsync` = 安装主入口（写变量 → Plan → Apply → 装后写服务端地址）。
- **线程模型**：引擎事件回调单线程（Burn 回调线程）；跨线程共享 = 不可变 `InstallState` 快照整体替换（`_stateLock`）+ `TaskCompletionSource`（Plan / Apply / Detect 各一，每轮 Apply 重建）；`CacheSourceFallback` / `CacheRetryPolicy` 由事件线程独占无需加锁。
- `Ui/`：`WizardForm`（向导壳：品牌条 + 步骤条 + 内容区 + 导航按钮，`ShouldSkip` 越过不适用页）+ 八页（Ready / Role / Brand / ServerAddress / ManagementUi / Confirm / Progress / Complete）+ `LayoutProgressForm`（布局生成进度窗）+ `SizeFormat`；页面经 `IWizardPage`（OnEnter / CanProceed / ShouldSkip）接入导航。

### 2.3 PayloadTool（src/LabelFrame.Bootstrapper.PayloadTool）

单文件 net48 控制台 CLI，五种模式：`-place`（zip 覆盖落位）/ `-plugin`（`.lfplugin` 版本比较落位）/ `-clean`（卸载清理，与落位同目标对称）/ `-install-webview2`（evergreen 获取 + 验签 + 静默安装，退出码透传）/ `-verify`（同源解析验签不执行，诊断载体）；无参数 = 空操作成功 0（清理包 install 形态兜底）。退出码契约：0 成功 / 同版跳过；20 降级拒绝；30 包损坏；40 验签拒绝；41 全源获取失败；90 未预期；其余非零 = 安装器退出码透传。输出 UTF-8 写 Burn 包日志。

## 3. 关键流程

### 3.1 交互安装主链（`Run` → 向导 → `ExecuteInstallAsync`）

1. **启动期（`Detect` 前）**：`RuntimeProbe` 探测三项运行时 → 写 Burn 探测变量（`DesktopRuntimeInstalled` 等，链内 DetectCondition 消费）；写落位目标变量（`WebUiTargetDir` / `PluginZebraTargetDir` / `PluginTsplTargetDir` 纯函数重算——非交互卸载会话同样需要 `[变量]` 可解析）；读落位凭据（webui / zebra / tspl 三处）比对 `WixBundleVersion` 写 Present 变量（清理包 DetectCondition 消费）。
2. **命令路由**：`LayoutAction.Layout` / `--layout` → 布局生成；非 Install 动作或静默显示级别 → `RunUnattended`；否则进向导。
3. **清单来源**：命令行 `--manifest` > 引导 EXE 同目录邻接 `install-manifest.json`（`WixBundleOriginalSourceFolder` 探测，离线首装入口）> 稳定通道 URL（`WizardSession.StableChannelManifestUrl`）。
4. **问卷（只读）**：`WizardSession.LoadManifestAsync` 一次完成——加载清单 → 解析本地源目录 → 读 latest.json（可推导才读，失败跳过）→ 探测已配服务端地址（settings.json 预填）→ 本机已装快照 + 升级评估 → 按已装打印机名预选品牌。
5. **执行边界**：确认页「安装」→ `ExecuteInstallAsync`——重建 `CacheSourceFallback`（当轮清单 + `LocalSourceDirectory`）与重试预算复位 → `BuildPlan` → `BundleVariableMap.ToVariables` 写引擎 → `WebView2Source` 写入 → `Engine.Plan(Install)` → `Engine.Apply(向导句柄)`；失败不走异常路径（ApplyComplete 已写真实引擎状态）；成功且计划含 client-msi 且地址合法 → `ServerUrlStore.Save` 写 `%ProgramData%\LabelFrame\Client\settings.json`（写失败不改变安装结果）。

### 3.2 下载事件链与多源回退（`HandleCacheAcquireBegin/Complete` / `HandleCacheVerifyComplete`）

- **获取开始**（`CacheAcquireBegin`）：附加容器（`WixAttachedContainer`）获取时注入运行中引导 EXE 为首搜索路径（提权副本只有引擎桩，否则 `0x80070002` 永不能完成）；远程载荷（`PayloadContainerId` 为空）登记后交 `CacheSourceFallback.ResolveAcquire`——本地源 → `SetLocalSource`；远程源 → `SetDownloadSource`（与构建期 DownloadUrl 不同才覆写）；源耗尽 / 未知包不干预。
- **获取 / 校验失败**（`CacheAcquireComplete` / `CacheVerifyComplete` 非零，仅远程载荷）：失败计数 +1、`DownloadFailureClassifier` 分类呈现；仍有未试源时经 `CacheRetryPolicy.ShouldRetry` 判定——放行则事件回调内 `Thread.Sleep(退避)` 后返回 `Retry` / `RetryAcquisition` 动作，无进展超限则停止交引擎失败回滚。校验失败路径先等引擎自身 2 次重取额度用尽（`Recommendation == None`）才追加驱动。
- **包级兜底**（`CachePackageComplete` 非零且有未试源）：同预算判定 `Retry`——这是无退避热循环缺陷的修复位点（真机实测 5 分钟 9.3 万次重试、日志 219~240MB，决策 #156 ③）。
- **换源播报去重**：`(包 id, 源下标)` 组合只播报一次（引擎校验重试会重复触发获取开始）。

### 3.3 离线布局生成与离线首装（决策 #135）

- **生成**（`RunLayoutGeneration`）：`--layout <目录>`（或引擎单横线 `-layout`）优先于一切安装路径；`LayoutModeArguments` 解析失败也进本路径显式失败不落向导。后台任务：拉清单原文 → `OfflineLayoutBuilder.BuildAsync`（组件逐个获取校验落位 + manifest / latest 原样字节落盘 + 引导 EXE 自复制（`WixBundleOriginalSource`——out-of-proc BA 的 `Assembly.Location` 是引擎解压临时目录不可用））；进度经 `LayoutProgressForm` 呈现（任意线程封送 UI）；与 `scripts/make-offline-layout.ps1`（IT 脚本形态）语义一致。
- **首装**：目标机拷目录双击 EXE → 邻接清单隐式检测 → 清单来源 = 本地文件、`LocalSourceDirectory` = 其所在目录 → 每链包有效源序 `[布局本地文件] ++ urls`，第 0 次获取解析提供本地文件（`SetLocalSource`）；引擎对本地源副本同样按包内嵌摘要强制校验——篡改即校验失败按序换源，断网则源耗尽失败（fail-closed，篡改内容绝不落装）。布局文件不在位 / URL 来源 → 退化为纯 urls（在线行为与现状一致）。

### 3.4 非交互路径与升级 / 卸载链（`RunUnattended`）

升级时 Burn 以 **Uninstall 动作驱动旧 Bundle**（RelatedBundle 升级链尾）、ARP 卸载 / 静默参数同理——旧 BA 若弹向导将无人应答、升级链卡死（升级走查 S2 实测暴露，#126 升级路径迭代的验收回流）。`RunUnattended`：Detect → Plan(action) → Apply（**隐藏窗口承载**——out-of-proc BA 传 `IntPtr.Zero` 实测抛 `ArgumentException`）→ 以 Apply 状态 Quit；Apply 超时 30 分钟上限。卸载计划由包已装状态驱动（Present / Obsolete），问卷变量不参与；落位清理由链内成对清理包的 `UninstallArguments`（`-clean -target [变量]`）在引擎事务内执行（决策 #133）。

### 3.5 evergreen（WebView2）获取链（决策 #153）

`runtime-webview2` 不进引擎获取（无构建期摘要可钉）：BA 随确认页写 `WebView2Source` 变量（`EvergreenRuntime.BuildWebView2SourceValue` 纯函数：布局本地文件在位 → 本地路径；否则清单 urls 顺序 `;` 拼接；清单缺条目 → 空串）→ 链包 `WebView2Runtime` 执行期 PayloadTool 以 `-install-webview2 -source [WebView2Source]` 运行（引擎提权上下文）→ `EvergreenPayloadAcquirer.Acquire`：本地源优先 → urls 逐源下载，每源 `WinVerifyTrust`（GENERIC_VERIFY_V2，不做联网吊销检查）+ 签名者 CN = `Microsoft Corporation` 双条件，任一不满足换下一源，全源耗尽 fail-closed（退出码 40/41 区分验签拒绝与源耗尽）→ 通过则官方参数 `/silent /install` 静默安装、退出码透传（3010 → Burn `scheduleReboot` 既有映射）。非交互安装清单未加载 → 变量空 → 工具走官方 fwlink 兜底。

## 4. 对外契约与依赖

**接口面**（共享库全部公开于 `LabelFrame.Bootstrapper.*` 命名空间；BA / PayloadTool 类型均 internal，经 EXE 边界消费）：

- Manifest：`InstallManifest.Parse`（fail-closed 校验）、`InstallManifestLoader.LoadAsync / LoadTextAsync / IsHttpUrl`、`LatestPointer.Parse / DeriveSource`；
- Topology：`ITopologyResolver` / `TopologyResolver.Resolve / AvailableBrands / IsBrandPlugin / BrandIdOf`、`TopologyPreset` 扩展（`ToManifestId / DisplayName / Description / IncludesClient / IncludesServer`）、`InstallTargets.Describe`、`BrandPluginMap.PluginIdOf`；
- Downloads：`CacheSourceFallback.FromManifest / ResolveAcquire / RecordAcquireFailure / RecordVerifyFailure / HasUntriedSources / FailureCount / FailureSnapshot / Reset`、`CacheRetryPolicy.ShouldRetry / DelayFor / Reset`、`DownloadFailureClassifier.Classify / Describe`；
- OfflineLayout：`OfflineLayoutBuilder.BuildAsync`、`OfflineLayoutNaming.TryDeriveFileName / 常量`、`LayoutModeArguments.Parse / SplitCommandLine`；
- Placement：`PayloadPlacer.PlaceArchive / PlacePlugin / CleanPlacement / ComparePluginVersions`、`PlacementMarker.BundleVersionOf / IsPlacedByBundle / FileName`；
- Prerequisites：`RuntimeProbe.Probe / ParseVersion`、`EvergreenRuntime.ResolveSource / BuildWebView2SourceValue / 常量`、`EvergreenPayloadAcquirer`、`IAuthenticodeVerifier` / `WinTrustAuthenticodeVerifier` / `AuthenticodePublisherPolicy`；
- Upgrade：`LocalInstallProbe.Probe`、`UpgradeAssessor.Assess`、`UpgradePresentation.Summarize / DescribeFreshness / ConfirmBanner / DisplayName`、`VersionSemantics.Compare / EqualsVersion`；
- Wizard：`WizardSession`（问卷状态机）、`WizardNavigator<TPage>`、`WizardForegroundActivator.Activate / Describe`、`ServerUrlStore.Load / Save / DefaultPath`、`ServerUrlInput.Normalize / LocalServerDefault`、`BundleVariableMap`（Burn 变量名常量 + `ToVariables` + 目标目录函数）、`ChainPackageMap`、`ExecuteBoundaryNotice.Banner`。

**被依赖**（grep 全仓查证）：

| 消费方 | 用途 |
|---|---|
| `LabelFrame.Bootstrapper.Ba` / `.PayloadTool` | ProjectReference 共享库（仅有的两个产品消费方） |
| `packaging/bootstrapper/Bundle.wxs` | BA EXE 作 BootstrapperApplication；PayloadTool + 共享库 dll + 依赖（`PayloadToolDependencies` PayloadGroup）内嵌打包 |
| `test/LabelFrame.Bootstrapper.Tests`（xunit，`net10.0-windows`） | 24 个测试文件 + 3 份清单 fixture——覆盖全部共享库子系统（仅 `WizardSession` 层的契约测试含只读断言与反射 API 面检查） |
| `scripts/build-bundle.ps1` / `.github/workflows/release.yml` | 构建 / 发版 Bundle（BA + PayloadTool + 共享库） |
| `scripts/make-offline-layout.ps1` | IT 布局脚本，与 `OfflineLayoutBuilder` / `OfflineLayoutNaming` 语义一致、命名约定共用 |
| `scripts/test-bundle-*.ps1`（download-matrix / offline-layout / uninstall-walkthrough / upgrade-acquire / upgrade-walkthrough / wizard-foreground）+ `test-webview2-authverify.ps1` | 真机 / 沙箱走查脚本，驱动真实 BA 与 PayloadTool（`-verify` 诊断模式） |

**跨模块同源事实**（双维护，变更须两侧同步）：

- `BrandPluginMap`（`zebra → labelframe-transport-zebra`、`tspl → labelframe-transport-tspl`）与 Core `TransportPluginIdPolicy.ZebraPluginId` / `TsplPluginId` 常量一致——共享库不引用 Core（net48 腿不引用 net10 目标的 Core）；
- `ServerUrlStore`（`%ProgramData%\LabelFrame\Client\settings.json` 的 `serverUrl`）与 WinHost `HostConfigStore` 同一事实源；`ServerUrlInput.LocalServerDefault`（`http://127.0.0.1:53961`）与 Server `ServerOptions.DefaultListenUrl` 一致；
- `LocalInstallProbe` 的两个 UpgradeCode 常量与 `packaging/main-server.wxs` / `packaging/main.wxs` 事实对应。

**依赖**：`System.Text.Json 10.0.12`（共享库）；BA 另引 `WixToolset.BootstrapperApplicationApi 7.0.0` + `System.Net.Http` / `System.ServiceProcess`（net48 显式引用）；net48 腿显式引用 WinForms / System.Drawing / System.IO.Compression（SDK 默认引用集不含）。P/Invoke：`msi.dll`（MsiEnumRelatedProducts / MsiGetProductInfo）、`wintrust.dll`（WinVerifyTrust + WTHelper 链）、`user32.dll`（前台 / 置前 / 闪烁七调用点）。`InstalledPrinters` 用 `System.Drawing.Printing`（PrinterSettings 实际位于 Windows Desktop / WinForms——net10 腿因此 `UseWindowsForms=true`）。

## 5. 设计取舍（决策索引）

| 主题 | 决策（原文见 DESIGN.md 决策表；§N = DESIGN.md 章节号） |
|---|---|
| 形态：自研向导 → WiX Burn Bundle + 托管 BA（out-of-proc，net48 裸机免装运行时） | #114 → #122（§6.1） |
| manifest 格式（sha256 强制 / urls 多源数组 / CI 生成禁人工维护）、latest.json 指针 | #115（§6.2）、#120 |
| 拓扑预设 + 仅两项开关、`TopologyPlan` 形状、问卷只读契约 | #116（§6.3）、#121 |
| 信任模型：哈希即背书、多源同哈希、不推翻 #79 | #117（§6.4） |
| 更新策略（检查 + 重跑引导；不做静默自更新）、首期不购证书 | #118（§6.5 / §6.6） |
| 执行链：链序九包、运行时前置、ExePackage 包装 PayloadTool 落位、确认前绝不 Apply | #124（§6.9） |
| 多源回退消费（SetDownloadSource + 事件 Retry）、下载侧失败分类 | #125（§6.10） |
| 升级路径：本机探测 + 组件级口径 + latest.json 新鲜度、升级执行零新机制 | #126（§6.11） |
| AspNetCore 前置补齐（server 腿 / client 腿两次返修） | #128 / #129 |
| 升级链获取阶段三重缺陷返修（CacheId 版本化 / 附加容器本地源注入 / 重试退避终止） | #156 |
| 卸载清理：成对清理包 + 落位凭据 + 只清 Bundle 落位目录 | #133（§6.8） |
| 离线布局目录与本地源无网首装、`--layout` 双横线 | #135（§6.2 / §6.10） |
| 两层问卷（基础三步 / 高级选项）、清单自动加载、角色三选 | #151（§6.3） |
| evergreen 载荷改 Authenticode 发布者验签 + 已装跳过 acquire | #153（§6.2 / §6.9） |
| 引导体验：BA 首显主动置前组合拳 + 闪烁兜底 | #168 |
| 官方插件体系（brand → 组件 id → pluginId 三段映射、版本比较率先） | #123（§6.8） |
| 品牌映射改指令集维度（TSC 及 TSPL 兼容品牌共用 tspl 插件——预选规则 / BrandPluginMap / 变量与链包映射四处接线） | #195（§6.8） |
| 安装链中文单语（i18n 范围限定） | #164 |
| 品牌 v02（BA 图标 / 向导头部 lockup 嵌入资源） | #172 |
| 注释极简化与模块文档分工（本文档的存在依据） | #187 / #189 |

## 6. 平台坑与不变量

修改本模块前必读（源码中对应位置保留白名单注释）：

1. **net48 / net10 双腿 API 差异**：全部 `#if NET10_0_OR_GREATER` 分支是刻意形态，不是冗余——net48 腿无 `ThrowIfNull` / `ThrowIfNullOrWhiteSpace`（.NET 6+）、无 `ReadAsStringAsync(ct)` / `ReadAsStreamAsync(ct)` 重载、无 `File.Move(overwrite)`、流读写仅数组偏移重载（CA1835 Memory 重载为 .NET Standard 2.1+）、`IsNullOrWhiteSpace` 无 `NotNullWhen` 流注解。合并分支即断 net48 编译。
2. **out-of-proc BA 的路径与窗口事实**：`Assembly.Location` = 引擎解压临时目录，不可用——定位自身一律 `WixBundleOriginalSource`（变量）；`Engine.Apply(IntPtr.Zero)` 抛 `ArgumentException`——非交互路径须以隐藏窗口句柄承载。
3. **提权副本只有引擎桩**：`Compressed="yes"` 载荷收入 `WixAttachedContainer`，而提权工作副本与包缓存 Bundle EXE 均为截断副本——内嵌载荷获取若不注入 `SetLocalSource("WixAttachedContainer", null, WixBundleOriginalSource)`，即 `0x80070002` 三连败（#171 真机根因，决策 #156 ②）。容器获取形态的 payload id 为 `string.Empty` 而非 null（沙箱实测）。
4. **缓存重试必须三重试点共用预算**：包级兜底重试只判「有无未试源」不判「是否推进」——对不消耗源游标的失败形成无退避无终止热循环（实测 5 分钟 92,993 次、日志 219~240MB）。任何新增重试点必须走 `CacheRetryPolicy`；事件回调内 `Thread.Sleep` 即重试节流（阻塞应答线程）。
5. **WINTRUST_DATA 字段序 = Wintrust.h 原序**：无 `hWndParent` / `pgActionID` 成员——偏移错位会引发引擎读野指针崩溃；验签收尾须同结构体改 `WTD_STATEACTION_CLOSE` 再调一次（失败路径句柄为 0 时无须 CLOSE）。
6. **BA 32 位进程读 HKLM 必须 `RegistryView.Registry64`**：`SOFTWARE\WOW6432Node` 路径会被注册表重定向二次映射而读空（本机实测缺陷）。
7. **net48 运行时已装而注册表 sharedfx 键不存在**：.NET 运行时探测必须文件版本探测（`dotnet\shared\<框架>` 目录枚举），不可改注册表法（微软官方口径；MSI 侧 NetCoreCheck 同口径）。
8. **落位凭据 = 卸载清理边界的技术保证**：只有 Bundle 落位的目录携带 `.labelframe-bundle-placement`；用户经客户端「插件管理」安装的与手动放置的资产无此文件、永不清理。`IsPlacedByBundle` 版本不可得恒假（fail-closed 到不清理——用户资产安全侧）。
9. **本地源校验不豁免**：`SetLocalSource` 交给引擎的本地文件同样按包内嵌摘要（= manifest sha256）强制校验——这是篡改布局文件 fail-closed 的机制保证，不可为「离线可用」放宽。
10. **问卷只读契约**：`WizardSession` 无任何下载 / 写入 / 系统改动方法（测试以反射断言公开 API 面）；唯一装后写入（服务端地址）归 BA 且 Apply 成功后执行。清单获取 = 本地读取或单次只读 GET。
11. **升级链依赖旧 BA 不弹向导**：非交互启动（Uninstall 动作 / 静默显示级别）必须走 `RunUnattended` 自动执行——弹向导无人应答会卡死升级链（走查 S2 实测）。启动期变量写入（落位目标 / Present）必须覆盖非交互会话（卸载的 `[变量]` 引用须可解析）。
12. **`--layout` 双横线 vs `-layout` 单横线**：引擎自身解析单横线（`LaunchAction.Layout` + `LayoutDirectory`），双横线按未知参数透传 BA——`LayoutModeArguments` 只认双横线，单横线路径由 BA 按 `LaunchAction` 路由，两者必须进同一生成流程。
13. **引擎事件回调单线程**：`CacheSourceFallback` / `CacheRetryPolicy` 的无锁访问以「Burn 回调单线程」为前提；跨线程只经 `InstallState` 快照（整体替换）与 `TaskCompletionSource` 通信。
14. **`TopologyOptions.SelectedBrands` 是 `ISet<string>`**：net48 无 `IReadOnlySet<T>`（BA 复用约束，决策 #121 形状微调）——改接口形状即断 net48 腿。
15. **多源回退只作用远程载荷**：`PayloadContainerId` 非空的伴生内嵌载荷经容器解压获取，与下载源无关——不得纳入失败计数与换源（否则缓存命中 / 容器获取会误触发换源播报）。
