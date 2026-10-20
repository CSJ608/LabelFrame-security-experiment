# LabelFrame.AndroidHost 模块设计

> 覆盖：`src/LabelFrame.AndroidHost/`（31 个 .cs 文件，约 4,970 行）＋ `test/LabelFrame.AndroidHost.Tests/`（2 测试文件）· 基线：`master` `7486dae`（2026-10-19）
> 分工与模板说明见 [README.md](README.md)；决策原文见 [DESIGN.md](../DESIGN.md) 决策表。

## 1. 职责与边界

**做什么**：Android / PDA 上的**后台打印执行服务**（决策 #93 / #95）——作业队列、Android 平台位图渲染、`^GF` 编码、Zebra SDK 传输（TCP 默认 / 蓝牙 SPP / USB OTG）、Server 注册轮询、开机自启保活全部在宿主内，不依赖任何 PC 侧组件。自研 App 形态：前台常驻服务（`PrintHostService`）+ 唯一配置 Activity（`MainActivity`）+ 本地 HTTP（`EmbeddedHttpServer`，仅 127.0.0.1:53970，TcpListener 极简实现，决策 #27——不承载 ASP.NET Core）+ LF_* 错误码 zh/en 码表（决策 #167）。打印入口有两个：Server 路由模式（主，`ServerPoller` 长轮询领取定向作业）与同机直连模式（「JS 桥」，第三方 WebView / 浏览器页面直接调本地 HTTP，决策 #93）。

**不做什么**：

- PDA 业务打印界面（扫码即打 / 模板列表 / 字段表单）——明确不做（决策 #95），由第三方程序经 JS 桥或路由承接，宿主零改动；
- Android SDK / AAR / Intent / 广播 / ContentProvider 等新集成契约形态——不做（决策 #93），第三方只拿 jobId + 状态 + 错误码；
- ZPL 矢量编码、版式解析、作业队列与校验、`.lfplugin` 插件加载 / 校验——Core 承担，本模块只做 Android 侧装配与路由；
- Skia 渲染——自带 `AndroidLabelRenderer` 用 Android.Graphics 实现同一编码契约（决策 #17 / #28），不引用 LabelFrame.Rendering / LabelFrame.Api（保持 APK 依赖闭包最小，DTO 为自持副本）；
- 插件包构建 / 分发——Server 托管 `.lfplugin` 包，本模块只做安装 / 卸载 / 动态加载。

## 2. 内部结构

| 构建块 | 类型 | 角色 |
|---|---|---|
| 应用骨架 | `HostApplication` / `BootReceiver` / `PrintHostService` | 进程入口（首行注册 `CrashGuard`）→ 开机 / 更新广播拉起前台服务 → 服务常驻（`StartCommandResult.Sticky`，`ForegroundServiceType=DataSync`，渠道 `labelframe_host` / 通知 id 1001） |
| 配置面 UI | `MainActivity` + 六页面 partial（HomeScreen 242 / ServerScreen 111 / PrinterScreen 419 / PluginsScreen 358 / DeviceScreen 79 / Widgets 212） | 唯一 UI（代码动态布局，无 XML 布局），主页状态卡 + 四个子页（服务器 / 打印机 / 插件管理 / 本机信息），视图构建后缓存往返不丢输入；已按页面拆 partial（主文件 290 行，架构评审 R-2 落地） |
| 本地 HTTP | `EmbeddedHttpServer` | TcpListener 极简 HTTP（见 §3.3 端点表）；状态页为内嵌单页 HTML |
| Server 路由 | `ServerPoller` | 注册（兼心跳）/ 长轮询通知 / 领取定向作业 / 终态回报 / 进度增量上报，与 WinHost `ServerRoutingWorker` 同构、内联实现 |
| 打印链 | `SubmissionService` / `Rendering/AndroidLabelRenderer` / `Rendering/AndroidTextWidthMeasurer` | 校验 → Android 整版位图（1bpp）→ Core `ZplImageEncoder` `^GF` → 本地作业队列（SQLite，`SqliteLabelJobStore`） |
| 传输 | `Transport/ZebraSdkTransport` / `Transport/Plugins/*` | Zebra Link-OS SDK 传输（tcp / bluetooth / usb）＋ 插件通道（内置 zebra 注册 + 外部 `.lfplugin` 动态加载 + 品牌路由，决策 #157） |
| 横切设施 | `LabelHostConfig` / `HostStatus` / `HostLog` / `CrashGuard` / `HostInfo` / `HostStrings` / `HostLocale` | SharedPreferences 配置、不可变状态快照（打印机目标存 PrinterFacts 结构化事实、渲染时按当前语言生成文案——不存语言字符串，切换零残留）、logcat+滚动文件日志、双通道崩溃捕获、版本读取、资源文案取值、语言判定 |
| API 契约副本 | `Api/Dtos.cs` / `Api/ImagePayloadDecoder.cs` | SubmitJobRequest / TemplateDto（含 `Images` base64，决策 #184）/ ErrorView（含可选 `params`，决策 #166 ②）/ JobView 等自持副本——不引用 LabelFrame.Api 工程；纯托管的 `ImagePayloadDecoder` 与 `LfErrorCatalog` 按文件链接编译进 net10.0 测试工程（android TFM 不可被整工程引用） |
| 码表 | `Errors/LfErrorCatalog` | LF_* 错误码 zh/en 字典 + `Resolve` 按码翻译（决策 #167） |

**配置模型**（`LabelHostConfig`，SharedPreferences `labelframe`）：结构化 `{ brand, connectionType, host, port, bluetoothMac }`（决策 #95 ①，品牌 / 连接类型是未来传输插件的路由键）；设备号 = `ANDROID_ID` 原值不可配置（坏值 `9774d56d682e25f8` 与取不到时本地 UUID 兜底），设备名默认 `PDA-<码后 4 位>`。默认值：zebra / tcp / 192.168.1.50:9100 / 轮询 5s / DPI 203。**保存即重启宿主服务生效**（传输与路由实例在服务启动时创建）。

**插件通道**（决策 #157 双档模型）：SDK 档（Zebra）编译进 APK 内置；轻量档纯托管 DLL 经 `.lfplugin` 外置动态加载——`PluginHost.Load` 在服务启动时装配（内置 `zebra` 注册 → `{FilesDir}/plugins/` 目录扫描，单插件失败留痕不阻断），`AndroidTransportFactory` 按配置品牌路由（zebra / 空 → 内置 SDK；外部插件 id → 注册表装配传输，参数固定 `{ connectionType, host, port }`；未安装 → 回退内置 Zebra + 中文留痕），`ZebraBuiltinTransportPlugin` 是 brand = zebra 的路由锚点与外部包占用内置 id 的拒绝锚点。

## 3. 关键流程

### 3.1 服务启动与打印主链（`PrintHostService.OnCreate` → `PrintLoopAsync`）

1. `CrashGuard.Register()` 兜底注册（进程首行 `HostApplication` 已注册，幂等）→ `NotePreviousRunCrash()` 检测上次崩溃摘要；
2. `Java.Lang.JavaSystem.LoadLibrary("e_sqlite3")`（**必须先装载**，见 §6 坑 1）→ `LabelHostConfig.Load` → `PluginHost.Load` 装配插件通道 → `AndroidTransportFactory.Create` 按品牌建传输；
3. `SqliteLabelJobStore.InitializeAsync` → `LabelJobQueue` → `SubmissionService(dpi=203)` → `EmbeddedHttpServer` 启动（127.0.0.1:53970）；
4. 起三个并行循环：`PrintLoopAsync`（打印 Worker）、`NotificationLoopAsync`（5s 一帧、文案变化才重发通知）、配置了 ServerUrl 时 `RoutingLoopAsync`（§3.2）；
5. 打印 Worker：`MarkInterruptedJobsSuspendedAsync`（进程被杀的中断作业转 Suspended）→ 循环 `ClaimNextItemAsync` 领取 → `_transport.SendAsync(item.Zpl)` → `CompleteItemAsync` / `FailItemAsync(TransportSendFailed)`；空转 200ms，领取异常退避 1s。状态经 `HostStatus.NotePrint*` 供 UI / 通知读取。

**提交子链**（`SubmissionService.SubmitAsync`，恒为图片打印）：requestId / template（contract+layout）/ labels 非空校验 → 逐张 `LabelValidator.Validate` → `LabelDocument` + `ImagePayloadDecoder.Decode(Template.Images)` → `AndroidLabelRenderer.RenderLabelBitmap`（1bpp）→ `ZplImageEncoder.EncodeImage` → `queue.SubmitAsync`（requestId 幂等）。失败返回问题码（不建作业）：base64 无效 → `InvalidRequest`；缺图片资源 → `MissingImageResourceException` 消息（决策 #184 ③ 显式报错不灰占位）。

### 3.2 Server 路由模式（`RoutingLoopAsync` + `ReportFinishedLoopAsync`，与 WinHost 同构）

主循环：`RegisterAsync`（兼心跳）→ 内层无限循环：`WaitForJobAsync(20s)` 长轮询（作业到达立即返回，超时也照常领取一次兜底）→ `FetchPendingAsync` 领取 → 逐作业本地 `SubmitAsync`（本地 jobId ↔ server jobId 记入 `localToServer` 映射；提交失败立即向 Server 回报 Failed）→ 仅超时信号时退一轮补注册心跳。网络异常记 `HostStatus.NoteServerError`、退避 `PollIntervalSeconds`（5s）重试。

回报循环（1s 周期并行）：终态作业立即 `ReportResultAsync` 并移除映射；进行中**计数有变化才** `ReportProgressAsync`（节流）；进度上报失败**静默降级**——Server 不可达不阻塞打印主链路，下一轮携带最新计数自然重试。

### 3.3 本地 HTTP 直连（`EmbeddedHttpServer`，JS 桥）

统一缓冲读取（头 + 体同一份累积字节解析；**不能用 StreamReader 预读**，见 §6 坑 8）；所有响应带宽松 CORS 头（`OPTIONS` 直接 204），第三方跨源页面可直连。端点与 WinHost 同构：

| 端点 | 行为 |
|---|---|
| `GET /healthz` | 存活 + 版本 |
| `GET /` | 内嵌状态页 HTML（本机 / 服务器 / 打印机状态 + 测试打印按钮，10s 轮询打印机状态） |
| `POST /api/jobs` | 提交作业（202 新建 / 200 幂等重放 / 400 问题码） |
| `GET /api/jobs?limit=` / `GET /api/jobs/{id}` | 作业列表（limit 夹取 1~500）/ 作业视图 |
| `POST /api/jobs/{id}/suspend|resume|cancel` / `POST /api/jobs/{id}/items/{n}/retry` | 状态迁移 / 失败项重打（404 / 409） |
| `GET /api/printer/status` / `POST /api/printer/test` | SDK `PrinterStatus` 状态 / 固定 ZPL 测试页发送 |
| `GET` / `POST /api/host/config` | 配置读写（写 = SharedPreferences，重启生效） |
| `POST /api/host/test-print` | 内置测试标签（60×40mm，`TestLabelTemplate`）走完整链路，返回作业供轮询 |
| `GET /api/plugins/installed` / `POST /api/plugins/install` / `POST /api/plugins/uninstall` | 插件列表（加载状态 / 失败原因透出）/ 安装（请求体 = .lfplugin 原始字节，三层校验含平台门在 Core `PluginInstaller` 内）/ 卸载（删目录，重启生效） |

### 3.4 插件安装与品牌扩展（`MainActivity` 插件子页）

拉取服务器 `GET {serverUrl}/api/plugin-packages` → 渲染包卡片（platforms 不含 android 的禁用安装按钮）→ 下载（独立 `HttpDownload`，5 分钟预算——包可达 64MB）→ `POST /api/plugins/install`（字节直传）→ `RestartServiceAsync`（stop → 800ms → start → 轮询 `/healthz` 就绪，打印机子页视图缓存失效重建）→ 品牌选项由内置 Zebra + 已装外部插件动态扩展。卸载为销毁类操作：红底 danger 按钮 + 原生 AlertDialog 确认（决策 #161），确认后删目录 + 重启；配置还指向被卸载品牌时同步回退 zebra。

### 3.5 崩溃捕获（`CrashGuard`）

双通道：.NET `AppDomain.UnhandledException` + Java 层 `Thread.DefaultUncaughtExceptionHandler`（记录后交回原处理器保持系统崩溃流程；托管异常最终也以 Java 形态到达，此时摘要只补 logcat）。完整堆栈入 `HostLog`（Error）；摘要文件 `{FilesDir}/crash/crash-yyyyMMdd-HHmmss.txt`（保留 3 份，**单份只落第一次**）供下次启动读证（`adb pull` 取证），不回传服务端。

## 4. 对外契约与依赖

**对外接口面**（无被依赖方——终端 APK）：

- 本地 HTTP 端点表（§3.3）即对外契约：请求 / 响应形状与 WinHost / Server 同构（`SubmitJobRequest` / `JobView` / `ErrorView` 码 + 可选 `params` + 中文兜底 message），错误码经 `LfErrorCatalog` 按码翻译（en 已知码插值 / 未知码或参数不全回退后端中文 / zh 直用后端 message——任何输入不出裸占位符）；
- Server 路由契约（消费方视角）：`POST /api/devices`（注册）、`GET /api/devices/{id}/jobs/notify?timeout=`（长轮询，clamp 1~30s）、`GET /api/devices/{id}/jobs/pending`（领取）、`POST /api/devices/{id}/jobs/{jobId}/result|progress`（回报 / 进度）；
- 配置结构 `{ brand, connectionType, host, port }`（SharedPreferences 键 `server_url` / `printer_brand` / `connection_type` / `tcp_host` / `tcp_port` / `bluetooth_mac` / `device_name`）。

**被谁依赖**：无代码消费方（grep 全仓 csproj / sln 无引用；不在 `LabelFrame.slnx`，决策 #104）。构建消费：CI `ci.yml` android job（第三项必需检查「Android 构建（PDA 宿主）」，Release + `EmbedAssembliesIntoApk=true`）、`release.yml`（`v*` tag 出 `LabelFrame-AndroidHost-<版本>.apk` 上传 GitHub Release）、`scripts/build-androidhost.ps1` / `create-android-keystore.ps1` / `generate-install-manifest.ps1` / `test-pda-plugin-spike.ps1`。运行时消费方 = 第三方 PDA 程序（JS 桥）与 Server（路由投递目标）。

**依赖**：`LabelFrame.Core`（唯一工程引用：文档 / 布局解析 / 校验 / `ZplImageEncoder` / 作业队列 / SQLite 存储 / 传输契约 / 插件加载与安装）＋ NuGet：`Zebra.Printer.SDK 5.0.3685`（纯托管 DLL，TCP / SPP / USB + `PrinterStatus`）、`SQLitePCLRaw.lib.e_sqlite3.android 2.1.11`（版本钉定，见 §6 坑 11）、`ZXing.Net 0.16.11`、`SkiaSharp.NativeAssets.Android 3.119.2`（Zebra SDK 图形工具引用）＋ 毒丸引用清单（全部 `ExcludeAssets="all"`，见 §6 坑 12）。目标框架 `net10.0-android` 单目标（minSdk 23 / targetSdk 36 / 单 `android-arm64` ABI）。

## 5. 设计取舍（决策索引）

| 主题 | 决策 |
|---|---|
| 本地服务统一入口（打印经宿主、直连与路由并存） | #7 |
| 中文渲染架构：整版位图 + `^GF`，Android 平台位图同契约 | #17（Android 实现口径 #28） |
| Android 本地 HTTP 用 TcpListener 极简实现（不承载 ASP.NET Core） | #27 |
| PDA 测试模式（pc_host）——已废止，状态页收敛后移除 | #42（由 #95 ⑤ 废止） |
| Api 共享库收敛（AndroidHost 保持自持 DTO 副本，未随入） | #75 |
| PDA 接入边界：宿主唯一 + JS 桥 / 路由两种集成、不做 SDK / AAR / Intent 契约 | #93 |
| PDA 宿主定位 = 后台打印执行服务；配置面 / 设备号 / 保存即重启 / 常驻通知 / 状态页 | #95 |
| PDA 自动化构建（CI 第三必需检查、不在 slnx、自签 keystore） | #104 |
| Zebra 状态映射（`GetCurrentStatus` 官方语义，Message 最小口径） | #109 |
| 区域水平锚定按实测文本宽度、度量接口注入（`AndroidTextWidthMeasurer` 为 Android 实现） | #110 |
| 双端复用 Zebra 官方 SDK：三连接类型、存量 tcp 配置无感迁移、单 arm64 ABI、毒丸清单 | #111 |
| 官方传输插件体系（`.lfplugin`，brand → pluginId 映射） | #123 |
| PDA 插件机制——双档模型（SDK 档内置 / 轻量档外置）与跨端格式 | #157 |
| 删除类操作 danger 规范（卸载确认对话框） | #161 |
| 多语言支持范围（打印内容不进范围、安装链维持中文单一、前端 key 策略） | #164 |
| ErrorView 可选 `params` 与展示端 LF_* 码表（PDA 副本对齐） | #166 |
| PDA 宿主多语言落地——资源体系 + 码表 + 渠道名不改名 + 诊断维持中文 | #167 |
| 图片控件：`TemplateDto.Images` 契约（PDA 不锁死）、单色统一阈值 128、缺资源显式报错 | #184 |
| 注释极简化与模块文档承接（本文档口径） | #189 |

## 6. 平台坑与不变量

修改本模块前必读（源码对应位置保留白名单注释）：

1. **e_sqlite3 必须先 `JavaSystem.LoadLibrary` 装载**：SQLitePCLRaw 原生库需先进 Android 链接器命名空间，否则 `raw.SetProvider` 的 `DllImport("e_sqlite3")` 抛 `DllNotFoundException`——APK 内已含 `libe_sqlite3.so` 也不行（`PrintHostService.OnCreate`）。
2. **毒丸引用清单是兼容义务**：Zebra SDK 官方不支持非 MAUI .NET 10 的 Android，实测成因是依赖链毒化（`System.Drawing.Common` 夹带 `System.Private.Windows.Core.dll` 致 AOT 失败；MAUI 链经 buildTransitive 注入 AOT profile）——csproj 内 `ExcludeAssets="all"` 清单须逐包显式排除（`ExcludeAssets` 不裁剪传递闭包），SDK 升级时复查。
3. **SQLitePCLRaw 钉定 2.1.11**：2.1.12/2.1.13 的 android 包误装 glibc 构建的 so（DT_NEEDED 含 libc.so.6，Android 装载失败）；2.1.11 为 NDK 构建且 LOAD 段 16KB 对齐，真机验证通过。
4. **HTTP 读取不能用 StreamReader 预读**：StreamReader 预读缓冲会把请求体一并吞掉，后续按 Content-Length 直读网络流将永久等待（POST 带体请求挂死）——头与体必须从同一份累积字节解析（`ReadRequestAsync`）。
5. **崩溃注册时机越早越好**：`HostApplication` 进程创建首行 + `PrintHostService.OnCreate` 兜底（幂等）；`HostApplication` 的 `(IntPtr, JniHandleOwnership)` 构造器是 .NET Android 约定，系统重建应用对象经 JNI 调用，**必须保留**。
6. **度量 / 绘制同源**：`AndroidTextWidthMeasurer` 与 `AndroidLabelRenderer` 共用同一 Paint 管线（`TextSize` / `FakeBoldText`）——任何一侧单独改动都会造成区域锚定偏移错位（决策 #110 的存在理由）。锚定文本框高 = 解析锚定框高（与 PC Skia 同口径），禁改用非锚定兜底框再垂直居中（字形下沉穿出盒底，曾有真机缺陷）。
7. **图片阈值同源**：`DecodeMonochrome` 预单色化与整版 1bpp 转换 luma 阈值必须同为 128（加权 299/587/114）——两端（Skia / Android）口径一致 = 阈值同源，逐像素一致不作承诺（缩放插值实现不同）。`SourceKey` 空 = 未指定图片，整元素跳过（含边框，「未指定 = 不存在于打印面」）；key 非空缺资源才显式报错——两条路径语义不同，不可合并（决策 #184）。
8. **语言经哨兵资源判定，不解析 `Configuration`**：`current_language`（values = zh / values-en = en）读实际命中的资源表，回退规则交给 Android 资源机制本身；带参模板用 `{0}` 位置占位 + InvariantCulture。
9. **通知渠道名不可双语化**：Android 渠道创建后不可改名，维持中文现状（决策 #167 ①）。状态页 HTML、HostLog 诊断、崩溃摘要维持中文（诊断边界）。
10. **蓝牙 / USB 权限形态**：API 31+ 走 `BLUETOOTH_CONNECT` 运行时权限（拒绝不阻塞保存，打印 / 测试时再给可行动提示）；legacy `BLUETOOTH` / `BLUETOOTH_ADMIN` 限 maxSdk 30（Zebra SDK SPP 需要）；USB 无需静态权限，首次连接由 SDK 触发系统授权弹窗（重新插拔后需再次允许）。
11. **每次发送 / 测试 / 状态查询独立建连**（不持有长连接，与裸 socket 时代行为一致）；USB 发现器为 Java 回调式，阻塞等待须有界超时（3s）。
12. **外部插件不得覆盖内置 id `zebra`**（三层校验拒绝锚点）；插件通道任何失败（扫描 IO / 单包加载）只留痕不阻断服务启动——回退纯内置 zebra 能力；PDA 平台门只装 manifest `platforms` 含 android 的包，无标记的存量 Windows 包拒绝。
13. **长轮询不能被 HttpClient 全局 Timeout 掐断**：notify 请求可达 20s+，全局 `Timeout = InfiniteTimeSpan`、按请求单独 CancelAfter。
14. **配置保存后必须重启宿主服务生效**（传输 / 路由实例在服务启动时创建）；设备号坏值 `9774d56d682e25f8`（Android 8 前部分设备重复返回）按取不到处理，本地 UUID 兜底。
15. **`OnBackPressed` 暂保留**：API 33 起被标记过时（CA1422，为预测性返回让路），但未启用 `enableOnBackInvokedCallback` 前框架仍回调它；为避免引入 AndroidX 依赖按既有行为保留。
16. **aapt 默认裁剪字符串首尾空白**：资源文案需保留尾部空格时用引号包裹（values-en `common_name_version`）。
17. **MainActivity 已按页面拆 partial**（六屏 partial + 主文件 290 行）：新增页面 / 大块 UI 优先落对应 partial 而非回填主文件；UI 层无组件级单测不变（验证通道 = 真机走查）。
