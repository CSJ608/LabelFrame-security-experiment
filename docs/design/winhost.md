# LabelFrame.WinHost 模块设计（Client Host）

> 覆盖：`src/LabelFrame.WinHost/`（40 文件，约 5600 行）＋测试 `test/LabelFrame.WinHost.Tests/`（65 文件，含 `Rendering/` 子目录＝Rendering 模块的测试）、`test/LabelFrame.ClientHost.Tests/`（Linux 目标）、`test/LabelFrame.Benchmarks/`（渲染 / 编码 / 队列热路径基准）· 基线：`master` `7486dae`（2026-10-19）
> 分工与模板说明见 [README.md](README.md)；决策原文见 [DESIGN.md](../DESIGN.md) 决策表。

## 1. 职责与边界

**做什么**：Client Host——单进程本地打印服务（决策 #7 本地服务统一入口）。同一工程多目标编译两形态（决策 #9 / #84）：

- **Windows 完整客户端**（`net10.0-windows10.0.26100`，WinExe）：HTTP API＋完整 Web UI 静态托管（决策 #53）＋WebView2 界面壳＋系统托盘＋单实例守卫（决策 #99）＋内置传输（log / tcp9100 / winspool）＋外部传输插件加载＋插件安装 / 卸载＋官方插件（zebra / tspl）存量懒激活迁移（决策 #123 ③ / #195）＋统一退出协调（决策 #112）；
- **Linux 无头 Log 客户端**（`net10.0`，AssemblyName `LabelFrame.ClientHost`）：仅注册内置 log 传输，不加载外部插件、不启动浏览器或托盘；作业队列 / Server 轮询 / Skia 渲染 / 结果回报与 Windows 共用同一代码路径（Docker 镜像随发版发布，决策 #87）。

两形态的业务核心一致：本机作业提交→队列→打印 Worker 串行出纸；连接管理（先测试后生效，决策 #50）；Server 路由（注册 / 长轮询领取 / 回报 / 进度上报，决策 #21 / #60 / #101）；批次节流（决策 #74 / #100）；Log 模拟打印（PNG 出图＋作业数据留痕，决策 #139）。

**不做什么**：

- 作业模型 / 队列语义 / SQLite 持久化——Core 的 `LabelJobQueue` / `SqliteLabelJobStore`（`jobs.db`）；模板库——Core 的 `TemplateStore`；设备日志库——Core 的 `SqliteLogStore`；
- 渲染与 ^GF 编码——Rendering 的 `SkiaLabelRenderer`、Core 的 `ZplImageEncoder`（宿主只做装配与调用）；
- 模板 / 调试出图 / 设备日志端点的**实现**——LabelFrame.Api 共享实现（决策 #75），本模块经 Options 传入宿主错误码装配；
- Zebra / TSPL 品牌与指令集传输——外置官方插件 `src/LabelFrame.TransportPlugin.Zebra` / `src/LabelFrame.TransportPlugin.Tspl`（`.lfplugin` 包，决策 #123 / #195），宿主只保留存量懒激活迁移（`OfficialPluginMigration`）与别名归一；
- Android / PDA 宿主——`LabelFrame.AndroidHost`（SDK 档内置，决策 #157）；
- 打包 / 升级编排——Bootstrapper 三件与 MSI（宿主只提供 `/api/host/shutdown` 打印忙态门槛配合，决策 #163）。

## 2. 内部结构

| 分组 | 类型 | 文件 | 角色 |
|---|---|---|---|
| 装配 / 生命周期 | `Program` | Program.cs | Main：配置绑定＋环境变量覆盖＋`--autostart`；单实例守卫；host.log 写入器；界面壳 / 托盘 / 退出编排；启动失败中文提示（`DescribeStartupFailure`） |
| | `WinHostApp` | WinHostApp.cs | 应用装配（DI＋全部端点＋Web UI 托管）；集成测试经同一装配拉 TestServer（决策 #82「测到的就是生产的」） |
| | `HostOptions` | HostOptions.cs | 配置模型：appsettings `WinHost` 节＋`LABELFRAME_*` 环境变量（约 30 项，决策 #19）；全部路径默认值 |
| | `HostExitCoordinator` | HostExitCoordinator.cs | 统一退出：优雅停止＋事件驱动提前退出（500ms 稳定窗）＋限时兜底强退（2.5s 宽限）（决策 #112 及其演进补记） |
| | `SerilogSetup` | SerilogSetup.cs | Serilog 文件日志唯一装配入口（app-*.log 按日轮转＋SelfLog 失败可见化）（决策 #102） |
| | `DailyRotatingFileWriter` | DailyRotatingFileWriter.cs | host.log 按日轮转写入器（host-yyyyMMdd.log，保留 31 个；永不抛出，失效自禁用）（决策 #108） |
| 界面壳（Ui/） | `IUiShell` / `WindowUiShell` / `BrowserUiShell` | UiShell.cs、WindowShell.cs | WebView2 窗口形态（独立 STA UI 线程＋WinForms 主窗体）／运行时不可用回退浏览器（决策 #99 D1/D2） |
| | `SingleInstanceGuard` | SingleInstanceGuard.cs | `Global\` 互斥锁＋跨进程激活事件（二次启动→显示已有窗口即退出） |
| | `UiReadiness` / `UiUrl` | UiReadiness.cs、UiUrl.cs | 就绪探测（窗口加载态期间轮询本地 HTTP）；界面地址规范化（通配监听→127.0.0.1）与窗口内导航边界 |
| 托盘 | `TrayIconService` | TrayIconService.cs | 原生 P/Invoke 托盘（隐藏消息窗口＋`Shell_NotifyIcon`，无 WinForms 依赖，决策 #43）；explorer 重启 `TaskbarCreated` 重注册 |
| | `TrayMenuPresenter` / `TrayQuitSignaler` | TrayMenuPresenter.cs、TrayIconService.cs | 右键菜单三件套标准前置模式（KB135788）；WM_QUIT 投递＋限时 Join 退出信号器（两者 Win32 调用点可注入替身） |
| 作业（Jobs/） | `JobSubmissionService` | Jobs/JobSubmissionService.cs | 提交链路：契约校验→按连接 `printMode` 分派（image：Skia 渲染→图片编码按插件分派（`ILabelImageEncoder` 命中则品牌二进制指令、否则 `^GF` 文本，决策 #195）；native：插件编译器逐张编译）（决策 #137）→幂等入队（双形态载荷）；Log 模式出图＋留痕＋顺带清理 |
| | `JobPrintWorker` | Jobs/JobPrintWorker.cs | 后台打印 Worker：串行领取→批次节流（发送前暂停）→经当前传输发送（回放载荷双形态：`CommandBytes` 二进制优先、否则 `Zpl` 文本，决策 #195）；信号唤醒＋5s 兜底轮询（决策 #92） |
| | `BatchPrintPolicy` / `JobDataLogFormatter` | Jobs/BatchPrintPolicy.cs、JobDataLogFormatter.cs | 节流纯函数判定（按作业重置计数，决策 #100）；留痕行格式化（去重＋截断 2048 字符，决策 #139） |
| 传输（Transport/） | `ITransportManager` / `TransportManager` | Transport/TransportManager.cs | 运行时连接管理：单一连接生效、先测试后生效、持久化 connection.json；启动回退护栏（插件缺失→默认连接→Log） |
| | `TransportConfig` | Transport/TransportConfig.cs | 连接配置模型：`pluginId + params` 新格式；旧字段（Mode / TcpHost 等）读取迁移＋保存同步写出（决策 #69） |
| | `RawPrinterTransport` / `WinspoolTransportPlugin` | Transport/RawPrinterTransport.cs、WinspoolTransportPlugin.cs | winspool raw 传输（P/Invoke，决策 #18）及其插件声明 |
| | `OfficialPluginMigration` / `ZebraTransportKind` | Transport/OfficialPluginMigration.cs、ZebraTransportKind.cs | 存量升级懒激活：已用官方插件（zebra / tspl）配置且未装→自动安装 MSI 附带官方包（决策 #123 ③ / #195，官方 id 清单驱动）；旧枚举仅存于配置兼容面 |
| 路由（Routing/） | `ServerRoutingCoordinator` | Routing/ServerRoutingCoordinator.cs | 路由独占资源持有者（地址＋HttpClient＋poller＋worker）；保存服务端地址热切换（重建＋旧连接清理）（决策 #141） |
| | `ServerRoutingWorker` | Routing/ServerRoutingWorker.cs | 主循环（注册心跳→长轮询 notify→领取→投入本地队列）＋独立回报循环（终态回报＋进度节流上报）（决策 #60 / #101） |
| | `ServerJobPoller` / `IServerJobPoller` / `ProgressThrottle` | Routing/ServerJobPoller.cs、IServerJobPoller.cs、ProgressThrottle.cs | Server HTTP 客户端（注册 / pending / notify / result / progress 五端点）；单作业进度节流（计数有变化才发＋最小间隔，失败不 Mark 可重试） |
| API 端点（Api/） | `JobsApi` / `TransportApi` / `PrinterApi` / `PluginApi` / `HostApi` / `PrintSettingsApi` / `Contracts` | Api/ 目录 | 宿主专属端点（分组实现）；`Contracts.cs` 只留宿主专属 DTO（通用契约在 LabelFrame.Api，决策 #75） |
| 持久化小件 | `HostConfigStore` / `PrintSettings` / `PrintSettingsStore` / `PrintImageRetentionCleaner` / `LocalIpAddresses` | 根目录 | 机器级 serverUrl 存取（决策 #57）；打印设置：批次节流（决策 #74）＋打印 DPI（203/300/600 白名单，未设置继承宿主配置，GET 回生效值、保存即生效）；出图目录保留清理（决策 #136）；本机 IPv4 枚举（决策 #61） |

**多目标条件编译**（csproj）：Windows 目标＝WinExe＋`UseWindowsForms`＋WebView2 包＋内嵌托盘白色图标资源；Linux 目标＝`Compile Remove` 托盘 / 界面壳 / winspool / 官方插件迁移六文件，`Program` / `WinHostApp` 内 `#if WINDOWS` 分支裁剪（Linux 强制 `Transport=Log`、无浏览器无托盘）。源码内 `#if WINDOWS` 与 csproj Remove 两侧共同圈定 Windows 专属面。

**路径约定**：用户级（`%LOCALAPPDATA%\LabelFrame\`）＝jobs.db / templates.db / logs.db / connection.json / print-settings.json / print\ / logs\（app-*.log）/ host.log；机器级（`%ProgramData%\LabelFrame\Client\`）＝settings.json（ServerUrl）与 plugins\（外部插件目录，决策 #68）。

## 3. 关键流程

### 3.1 启动链（`Program.Main`，Windows 形态）

1. 配置装配：appsettings `WinHost` 节绑定 → `ApplyEnvironmentOverrides()`（`LABELFRAME_*` 优先级最高）→ `--autostart` 强制「仅托盘不弹界面」（决策 #99 D4）；
2. host.log 写入器：`DailyRotatingFileWriter`（`TextWriter.Synchronized` 包裹；打开失败回退 `TextWriter.Null` 不阻断启动）；
3. 单实例：`SingleInstanceGuard.TryAcquire()` 拿不到锁 → `SignalActivate()`（已有实例显示窗口）→ 本次启动退出，不再走到端口监听；
4. `WinHostApp.BuildAsync`（Serilog 经 `configureBuilder` 挂真实 builder——见 §6.1）：传输注册表装配（内置三件＋外部插件目录扫描＋`OfficialPluginMigration.Run` 须先于扫描）→ 各 Store 初始化 → 全部端点映射 → Web UI 探测托管（`web/dist` 配置优先，未找到仅 API）→ 路由协调器按当前配置拉起（未配置地址空转）；
5. 界面壳：`WindowUiShell.TryStart`（WebView2 可用时）否则 `BrowserUiShell` 回退；托盘 `TrayIconService.Start`（双击 / 菜单「打开界面」→ `uiShell.OpenUi()`；「退出」→ 退出协调器）；
6. `RunAsync` 异常 → `DescribeStartupFailure`（端口占用转中文可行动提示）→ 有界面壳走 `ShowFatalError` 消息框、无界面壳同时写 stderr；`finally` 统一 `RunCleanup()` → `MarkNaturalExitCompleted()` → `Environment.Exit`。

### 3.2 作业提交与打印链（`JobSubmissionService` → `JobPrintWorker`）

1. 入口两路：本机 `POST /api/jobs`；Server 路由领取（`ServerRoutingWorker.HandleClaimedJobAsync` 转构 `SubmitJobRequest`，Server 附带模板含 Images base64 透传）；
2. requestId 必填（幂等键）；按**当前连接**的 `printMode` 参数分派（连接级属性，同一 Server 作业投给不同客户端按各自连接出纸，决策 #137 ③）：
   - **native**：先幂等短路（重放不重新编译）→ 无编译能力插件显式失败 `LF_ENC_003` → 逐张 `compiler.CompileAsync`（契约校验先行；任一失败整体拒绝，产物写 `LabelJobItem.Zpl`）；
   - **image**（默认 / 缺失 / 非法回退）：逐张 `LabelValidator.Validate` → `RenderLabelBitmap`（图片资源：base64 附带优先、按名回退本地模板库，与共享出图端点同源）→ 图片指令编码按连接插件分派（`GetImageEncoder` 命中→插件 `ILabelImageEncoder` 产二进制流（如 TSPL BITMAP）；未实现（Zebra / 内置通道）→ `ZplImageEncoder` `^GF` 文本，输出逐字节不变，决策 #195）；测试页端点 `POST /api/printer/test` 随同一分派；
3. `queue.SubmitAsync(requestId, payloads)` 入队（requestId 幂等；`LabelJobPayload` 文本 / 二进制双形态）；
4. Log 连接＝模拟打印：渲染 PNG 落盘 `print\{jobId}\label-N.png`＋`JobDataLogFormatter` 留痕作业数据＋顺带 `PrintImageRetentionCleaner.CleanupExpired()`（native 路径无位图，仅留痕）；
5. `JobPrintWorker`（BackgroundService）：启动恢复中断作业 → 循环「EXISTS 轻量探测→`ClaimNextItemAsync`→批间计数按作业重置→`BatchPrintPolicy.ShouldPauseBeforeSend` 则 `Task.Delay(batchIntervalMs)`→`TryBeginPrintSendAsync` 领发送租约→`CurrentTransport.SendAsync`（`CommandBytes` 二进制优先，否则 `Zpl` 文本）→完成 / 失败（失败记结构化日志：作业 / 索引 / 插件 / 目标 / 错误码，决策 #102 ③）」；空转等待信号唤醒（5s 超时兜底），领取落空回退 200ms 周期。

### 3.3 连接管理与传输装配（`TransportManager`）

- 启动初始化：`BuildBaseConfig`（appsettings / 环境变量→pluginId+params）→ `LoadPersisted`（connection.json 存在即最高优先；旧格式自动迁移；插件缺失 / 损坏 / 读取失败回退默认连接并留痕）→ `CreateTransportOrDefault`（生效配置引用的插件不可用时回退默认连接，再回退 Log——「未装外置包则该品牌不可用」的可控行为）；
- `ApplyAsync`（连接切换 / 测试）：`_applyLock` 串行化（防两个「先测试后生效」交错）→ 旧格式迁移＋旧字段同步 → 按插件 spec 校验（必填只对**当前可见**字段生效——`VisibleWhen` 条件可见性，决策 #186；类型 / Select 对已提供字段照常）→ `printMode=native` 合法性校验（保存层）→ 经注册表创建候选实例 → `ITestableTransport.TestAsync` 测试 → `testOnly` 只返回结果 / 否则切换＋持久化；
- 并发口径：`_config` / `_transport` 读写经 `_stateLock`；打印 Worker 每张发送前取**当前**传输（切换后下一张生效）。

### 3.4 Server 路由（`ServerRoutingCoordinator` → `ServerRoutingWorker`）

1. 启动：`StartWithConfiguredUrlAsync`（`HostOptions.ServerUrl` 或机器级 settings.json，环境变量优先）——未配置空转，首次保存即开启；
2. 热切换（`POST /api/host/config` 保存成功后调 `ApplyServerUrlAsync`）：地址未变化幂等；变化时**先起新路由**（新服务端尽快注册，最小化不可用窗口）→ 再停旧路由（取消在途长轮询→5s 上限等收尾→释放旧 HttpClient 连接池）（决策 #141 否决了「poller 每轮读配置」形态）；
3. Worker 主循环：`RegisterAsync`（注册即心跳）→ `WaitForJobAsync` 长轮询（20s 超时，作业到达立即唤醒，决策 #90 服务端挂起前预检）→ `FetchPendingAsync` 领取 → 逐作业投入本地提交链；异常退避 `PollIntervalSeconds`；
4. 独立回报循环（1s 周期，不被长轮询阻塞，决策 #60）：本地作业终态→`ReportResultAsync`（载荷＝status＋计数＋errorMessage，失败明细取首个带原因的标签；`ServerJobResult.ErrorCode` 字段为本地侧加法，当前未入 HTTP 载荷）；进行中→`ProgressThrottle` 节流（计数有变化且距上次成功上报达间隔才发；上报失败不 `MarkReported` 下一轮自然重试；Server 不可达静默降级不阻塞打印主链路，决策 #101）。

### 3.5 退出链（`HostExitCoordinator`，两入口共用）

托盘菜单「退出」与 `/api/host/shutdown`（仅回环可写；HTTP 路径先经队列锁 `TryPrepareForShutdownAsync` 原子拒绝打印中的退出并留 200ms 响应缓冲，决策 #163）→ `RequestShutdownAsync`（幂等，首个请求执行序列）→ `StopApplication` → 三选一等待：主流程自然退出（`MarkNaturalExitCompleted` 置位→看门狗收手）／宿主已停止而主流程未返回（500ms 稳定窗观察→届满判定卡死→清理＋强退）／宽限超时（2.5s，宿主停止本身挂死的兜底）→ `RunCleanup`（幂等单入口：托盘 `Dispose`＝投递 WM_QUIT＋限时 Join，消息循环退出后在**托盘线程内** NIM_DELETE 消除幽灵图标；界面壳释放）。全链各阶段写 host.log 记账（「点了退出没反应」可定位阶段）。

### 客户端安全重启

`HostActivityState` 统一维护界面会话、在途写操作、路由领取和未确认结果；`HostRestartCoordinator` 串行化请求，先设维护门闩，再由队列锁检查整个 Pending/Printing/Suspended 批次。所有已登记界面须在 8 秒内暂停输入、携带本次 preparationId 确认最终 dirty/busy 状态；超时或存在未保存内容均恢复运行。准备后新业务写请求返回 503，领取与发送受队列门闩保护；安装器原 shutdown 语义保留。

路由成功打印及本地提交失败的结果均须确认回报，失败提交的结果也交由独立回报循环重试。断网期间保留维护保护，确认后释放；不把失联或编译失败误判为全部收尾。

`GET /api/host/lifecycle` 暴露进程标识与插件待重启标记。仅回环同源的 `POST /api/host/ui-activity`、`POST /api/host/restart` 支持界面协作和重启。活动报告有单调序号，关闭报告保留序号墓碑，迟到请求不能复活旧 dirty 状态；失联不自动过期为安全。

`RestartProcess` 先启动同用户等待器并通过命名事件确认就绪；等待旧 PID 与启动时间匹配的进程退出（最多 30 秒），再启动同一可执行文件。保持配置、环境和原参数，页面请求仅追加固定插件设置位置；托盘请求保持窗口可见性。等待器准备失败保留旧进程；启动失败写 `%LocalAppData%/LabelFrame/restart.log` 并提示手工恢复。普通客户端不提权，不重启操作系统。

## 4. 对外契约与依赖

**HTTP 端点面**（默认监听 `http://127.0.0.1:53960`；Linux 容器形态 `0.0.0.0`）：

| 端点 | 实现 | 说明 |
|---|---|---|
| `GET /healthz` | WinHostApp | service / platform / headless / transport / pluginId / displayText（旧字段 transport 兼容旧前端徽标） |
| `POST/GET /api/jobs`、`GET /api/jobs/{id}`、`suspend/resume/cancel`、`items/{index}/retry` | JobsApi | 提交 / 列表 / 详情 / 状态迁移 / 失败项重打；Log 模式视图附带 `printImageDir` / `printImageCount` |
| `GET/POST /api/transport`、`GET /api/transport/plugins` | TransportApi | 连接查询 / 切换 / 测试（切换与测试统一 200＋`ok` 布尔）；已装配插件列表（含 `supportsDocumentCompile` 能力位） |
| `GET /api/printer/status`、`POST /api/printer/test` | PrinterApi | 状态（按传输能力降级）；测试页（与正式打印同源：Skia 整版位图按连接插件分派图片指令编码——命中 `ILabelImageEncoder` 出品牌二进制，否则 ^GF） |
| `GET /api/plugins/installed`、`POST install/uninstall` | PluginApi（仅 Windows） | 插件安装 / 卸载（写文件＋重启生效） |
| `GET/POST /api/host/config` | HostApi | 机器级 ServerUrl（仅回环可写；GET 含只读 `version`＝客户端「检查更新」比较口径）＋本机 `ips` |
| `GET/POST /api/host/print-settings` | PrintSettingsApi（经 HostApi 挂载） | 打印设置：批次作业＋打印 DPI（仅回环可写、保存即生效；GET 的 dpi 一律为生效值，POST 缺 dpi 保留已存值） |
| `POST /api/host/shutdown` | HostApi | 本机关闭（仅回环；打印中 409） |
| 模板 / 出图 / 日志端点 | LabelFrame.Api 共享实现 | `MapTemplateApi` / `MapRenderApi` / `MapLogApi`（错误码用宿主前缀，决策 #75） |
| Web UI 静态托管 | WinHostApp（仅 Windows） | `web/dist` 探测（配置 / 安装目录 / 仓库开发路径三级候选）＋SPA 回退（未匹配 `/api` 返回 index.html） |

**配置面**（`HostOptions`，决策 #19）：appsettings `WinHost` 节＋`LABELFRAME_*` 环境变量覆盖（监听 / 三库路径 / 传输默认 / 路由轮询与进度间隔 / 日志级别与目录 / 保留天数 / 界面与托盘开关等约 30 项；`ProductVersion = "1.0.0"` 与打包脚本同步）。**回调契约**：`SubmitJobRequest.callbackUrl` 接受但忽略（直连模式无出站回调，决策 #154 ④）。

**数据文件**：`connection.json`（用户级，TransportManager 持久化，旧格式自动迁移）；`settings.json`（机器级 ServerUrl，决策 #57）；`print-settings.json`（批次设置，原子写）；`print\{jobId}\`（Log 出图，按天保留默认 31，决策 #136）；`host-yyyyMMdd.log` / `app-yyyyMMdd.log`（按日轮转保留 31，决策 #108）。

**依赖**：`LabelFrame.Core`（作业 / 模板 / 传输插件接口 / 插件安装器 / 日志库）、`LabelFrame.Api`（共享端点与 DTO）、`LabelFrame.Rendering`（渲染器）；NuGet：`Microsoft.Web.WebView2`（仅 Windows 目标）、`Serilog.AspNetCore`、`SQLitePCLRaw.lib.e_sqlite3`、`Microsoft.Extensions.DependencyModel`（显式钉 10.0.7，决策 #130）。

**被依赖**：生产侧零消费（终端 Exe——全仓 grep 证实仅 Core 两处注释提及）；消费方＝Web 前端（`web/` 经 HTTP 调用上述端点，client 构建形态）、MSI / Bootstrapper（打包产物与升级链，经 `/api/host/shutdown` 交互，决策 #163）；测试：`WinHost.Tests`（单测＋集成，`WinHostApp.BuildAsync` 同一装配）、`ClientHost.Tests`（Linux 目标 headless 断言＋Log 出图扫码解码端到端）、`Benchmarks`（引用 Core＋Rendering，打印管线与批量伸缩基准）。

## 5. 设计取舍（决策索引）

| 主题 | 决策 |
|---|---|
| 运行平台（多目标：Windows 完整＋Linux 无头） | #9（Linux 无头形态 #84；发布与同制品门禁 #87） |
| 传输分层（winspool P/Invoke 在 WinHost） | #18 |
| 配置模型（appsettings＋LABELFRAME_* 环境变量） | #19 |
| Server 投递「宿主轮询」（不开入站端口） | #21（离线暂存 #22；notify 预检 #90；Claimed 失联回收 #98） |
| 在线状态 / 测试页端点 | #26（Zebra 状态映射修正 #109） |
| 连接管理（单一连接、先测试后生效、connection.json） | #50 |
| 客户端机器级 ServerUrl（/api/host/config＋settings.json） | #57（本机 ips #61） |
| 回报独立循环（不等长轮询） | #60（进度增量上报＋节流＋静默降级 #101） |
| 传输插件统一接口与参数模型 | #67（加载 / 卸载 / 使用 #68；connection.json 兼容演进 #69；字节加载不锁文件 #73；ALC 生命周期——注册表强持有＋包目录单 ALC #127） |
| 插件包分发闭环（.lfplugin、三层校验、64MB 上限） | #72（局域网信任模型——不做鉴权 #79；平台字段跨端格式 #157） |
| 批次作业节流（claim-then-delay） | #74（计数按作业重置修正 #100） |
| API 契约与端点共享库（LabelFrame.Api） | #75（全局异常处理与错误契约 #78；错误消息多语言 params #166） |
| 测试体系（生产同一装配、TimeProvider 注入） | #82（性能 / 稳定性三层体系 #83；SQLite provider 初始化隔离 #85） |
| Worker 信号量唤醒（空转轮询消除） | #92 |
| 界面壳：WebView2 嵌入窗口（D1~D4 四决议） | #99 |
| 批次节流日志→Serilog 文件日志引入 | #74（装配修复＋级别可配置＋失败上下文结构化 #102） |
| 日志基础设施加固（按日轮转＋保留 31＋启动防护） | #108（量闸升级 #173） |
| 设备日志链路（补挂 /api/logs＋按行拆分入库） | #106 |
| 退出路径统一（优雅停止＋兜底强退＋托盘清理） | #112（MSI 运行中升级退出协调 #163） |
| Zebra 外置官方插件体系（存量自动迁移安装） | #123（MSI 附带包接线 #131） |
| 发布工件依赖版本固化（SkiaSharp / DependencyModel 下限） | #130 |
| 出图目录保留清理（按天 31，LastWriteTime 判龄） | #136 |
| 插件命令打印契约（printMode 分派＋编译持久化） | #137 |
| 模拟打印作业数据留痕（去重＋2KB 截断） | #139 |
| 路由热切换「保存即生效」＋连接状态三名义 | #141 |
| 终态回调 webhook（直连接受 callbackUrl 但忽略） | #154 |
| 连接参数 spec 条件可见性（VisibleWhen＋二进制兼容义务） | #186 |

## 6. 平台坑与不变量

修改本模块前必读（源码中对应位置保留白名单注释）：

1. **Serilog 必须挂在被 Build 的真实 builder 上**：`SerilogSetup.Apply` 只接受 `WinHostApp.BuildAsync` 的 `configureBuilder` 传入的 builder——挂在 Main 里只用于配置绑定的 builder 上**不会生效**（缺陷 #14 的根因；文件日志曾静默停写两周）。回归测试以「同一装配路径构建→写事件→断言文件存在」结构性锚定（决策 #102）。
2. **WebView2 / WinForms 界面线程三坑**（决策 #99 ⑥，均本机冒烟实证）：UI 线程须显式 `SetApartmentState(STA)`（.NET 默认 MTA，WebView2 报 `RPC_E_CHANGED_MODE`）；须显式安装 WinForms 同步上下文且窗口经消息队列投递显示（泵启动前 Show 会把 await 续体落到线程池，WebView2 报 UI-thread-only）；首显须重申 `ClientSize`（WebView2 初始化与布局竞态下窗口停在未展开尺寸）。另外 `Application.Run` 不挂 MainForm（会自动显示窗口，与 `--autostart` 仅托盘冲突），`FormClosed` 手动 `ExitThread`。
3. **托盘菜单三件套（KB135788）与重注册**：`SetForegroundWindow(owner)` → `TrackPopupMenu(TPM_RETURNCMD)` → `PostMessage(WM_NULL)` 缺一不可——owner 非前台时菜单不收起、不清模态态时下次弹出被立即取消；模态运行期间重入直接早退（嵌套 TrackPopupMenu 未定义）。explorer 重启广播 `TaskbarCreated` 必须重新 `NIM_ADD`（否则图标永久消失、窗口隐藏型应用失联）；退出必须投递 WM_QUIT 让消息循环退出后在**托盘线程内** NIM_DELETE（跨线程删除留幽灵图标）。`NOTIFYICONDATA` 的 HICON 在图标存续期间必须存活（`Icon` 提前释放即销毁句柄）。
4. **外部插件 ALC 强持有**：`TransportPluginRegistry` 必须随插件实例强持有其 `AssemblyLoadContext` 根——曾因加载器丢弃 ALC 引用被 GC 发起卸载，首用惰性解析命中卸载态上下文抛 `FileLoadException(VerifyIsAlive)`（决策 #127 根因）。包目录＝原子单元（单 ALC＋预载全部伴生依赖）。
5. **`TransportParameterSpec` 历史 7 参构造器不可删**：已发布第三方插件 DLL 按旧签名绑定构造器，「宿主已升级而插件未随发」的旧二进制加载即 `MissingMethodException` 崩宿主（真机实证）——`VisibleWhenKey/Values` 可选参数走新构造器，旧构造器显式保留并委托（决策 #186 兼容义务，Core.Tests 反射断言锚定）。
6. **依赖版本下限为兼容义务**：`Microsoft.Extensions.DependencyModel` 显式钉 10.0.7（闭包内传递下限 8.0.0）；Rendering 侧 SkiaSharp 3.119.2——低于基线会在 Client MSI 覆盖升级链触发 Windows Installer 组件规则拒装降版 keyfile（决策 #130，升版须同步上调 `scripts/assert-client-deps-baseline.ps1`）。
7. **批次节流计数按作业重置**：领取到不同作业时计数归零，保证每个作业首张立即发送——不得改回跨作业累计（batchSize=1 / 间隔 5s 时每个新作业首张前白等 5s，决策 #100 修正的缺陷）。发送前暂停（claim-then-delay）与「已发送数 > 0」守卫不动。
8. **单一连接生效＋先测试后生效**：`ApplyAsync` 整体经 `_applyLock` 串行化；切换成功才持久化（失败保持原连接）；`CurrentTransport` 每张发送前现取（切换后下一张生效，正在发送的不受影响）——两个时序都是既定语义不是竞态。
9. **进度上报失败不 `MarkReported`**：同一计数下一轮仍放行（Server 不可达静默降级后自然重试）；终态只走 result 端点、progress 不延寿（与 Claimed 超时回收正交，决策 #101）。
10. **直连模式 `callbackUrl` 接受但忽略**：`SubmitJobRequest` 为三端共用契约，宿主不产生任何出站回调（决策 #154 ④）——删掉该字段会破坏契约反序列化。
11. **winspool raw 传输无法读回状态**：`GetStatusAsync` 统一按在线处理（`PrinterStatusInfo` 带说明文案），不要尝试补状态查询（决策 #26 边界）；TCP 连接测试必须收到 `~HS` 响应才算成功（能连端口 ≠ 打印机，TransportManagerTests 锚定）。
12. **`UiReadiness` 探测异常口径**：仅调用方取消才中断等待；探测自身的超时（HttpClient Timeout 抛 TaskCanceled）视为未就绪继续等——两类 `OperationCanceledException` 语义不同。本机探测禁用系统代理（`UseProxy = false`）。
13. **`/api/host/shutdown` 的队列锁门槛**：`TryPrepareForShutdownAsync` 在队列锁内原子拒绝正在打印的退出并停止新提交 / 领取——MSI 升级链（决策 #163）依赖「检查与开始打印之间无竞态」这一语义，放宽会复现升级期文件占用弹窗。
14. **csproj 毒丸清单与良性告警**：WebView2 包 buildTransitive 引入 Wpf 变体产生 MSB3277（良性，降级为 Message）；`SkiaSharp.NativeAssets.Win32` 附带 81MB 原生 pdb，Publish 后删除目标（决策 #111 ⑥）；测试工程引用 Zebra 插件工程时 MAUI / WinUI 链毒丸清单须逐包 `ExcludeAssets="all"` 显式排除（决策 #111 ⑤，SDK 升级须复查）。
15. **Linux 目标的编译裁剪面**：csproj `Compile Remove` 六文件（托盘 / 界面壳 / winspool / 官方插件迁移）与源码 `#if WINDOWS` 必须同步维护——把 Windows 专属类型泄漏进 net10.0 目标即断 Linux 构建（ClientHost.Tests 锚定 headless＋仅 log 插件＋无插件端点）；重命名 Windows 专属文件时须同步刷新 Remove 列表（`ZebraPluginMigration` → `OfficialPluginMigration` 更名时曾漏改——残项指向不存在文件＝no-op、类实际进入 Linux 编译面，已修复复位）。
