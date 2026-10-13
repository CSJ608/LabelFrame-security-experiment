# LabelFrame.Server 模块设计

> 覆盖：`src/LabelFrame.Server/`（26 文件，约 3,530 行）＋ `test/LabelFrame.Server.Tests/`（33 文件，约 5,130 行）· 基线：`master` `831cb9e`（2026-10-12，迭代 138 全量铺开）
> 分工与模板说明见 [README.md](README.md)；决策原文见 [DESIGN.md](../DESIGN.md) 决策表。

## 1. 职责与边界

**做什么**：集中部署的**无头服务端**（决策 #52 拆分、#53 默认无头）——设备目录（注册 / 心跳 / 在线状态 / 来源 IP）、作业中心（幂等提交 → 定向暂存 → 宿主轮询领取 → 回报 / 进度 / TTL 过期 / 超时回收，决策 #6 / #21 / #22）、终态回调 webhook 投递（决策 #154）、调试出图与模板库共享端点装配、安装包 / 插件包分发（决策 #71 / #72 / #119）、文本日志与历史数据清理（决策 #56 / #108 / #173）、管理界面插件托管（决策 #62）。同一代码双形态运行：Windows 服务 `LabelFrameServer`（`net10.0-windows`，决策 #54）与 Linux 容器 / systemd（`net10.0`，决策 #59）。

**不做什么**：

- 打印执行——客户端（WinHost / AndroidHost）领取后本地渲染打印；Server 只做调试出图（Skia 同源），最终打印以客户端渲染为准（决策 #52）；
- HTTP 契约与共享端点实现——`LabelFrame.Api` 承担（`SubmitJobRequest` / `TemplateDto` / `ErrorView` 等共享 DTO 与模板 / 出图 / Excel / 日志端点，决策 #75），本模块只经 Options 装配；
- 模板库 / 日志库存储实现——Core 的 `TemplateStore` / `SqliteLogStore`，本模块只构造与注入；
- Web UI——默认无头；管理界面是静态前端包目录插件（`plugins/web-ui`，放入即托管、移除即无头，决策 #62）；
- 鉴权 / 传输加密——局域网信任模型，API 全部无鉴权（决策 #79，真实防护 = 部署边界）。

## 2. 内部结构

| 分组 | 文件 | 角色 |
|---|---|---|
| 入口与装配 | Program.cs | top-level 组装：配置绑定、日志通道、DI 单例、4 个后台服务、端点映射、SPA fallback |
| | ServerOptions.cs | 配置面：全部默认值 + `LABELFRAME_SERVER_*` 环境变量覆盖（约 18 项） |
| 领域服务 | ServerService.cs | 设备 / 作业业务编排：提交（幂等门）、领取、回报、进度、视图组装、IP 规范化 |
| | ServerException.cs / ServerErrorCodes.cs | 携问题码领域异常与 `LF_SRV_001~013` 码表（新增码须同步 web 端码表） |
| 存储 | ServerDb.cs + ServerDbStore.cs / ServerDeviceStore.cs / ServerJobStore.cs / JobCallbackStore.cs | SQLite 三表访问按聚合拆分：ServerDb＝建库与旧库迁移入口、ServerDbStore＝共享基类（每操作一连接）、设备 / 作业 / 回调三存储类 |
| 后台任务 | PendingJobExpirationService.cs | Pending 超 TTL 扫描 → Expired 终态（决策 #89） |
| | ClaimedJobTimeoutService.cs | 失联 Claimed 扫描 → Failed 回收（决策 #98） |
| | JobCallbackDeliveryService.cs | 回调投递扫描（含 `HttpJobCallbackSender`，决策 #154） |
| | DataCleanupService.cs | 终态作业 / 日志按期删除 + logs.db 量闸（决策 #56 / #173） |
| 通知 | PendingJobNotifier.cs | 设备粒度 TCS 等待者字典：提交脉冲唤醒长轮询 |
| 回调模型 | JobCallbacks.cs | `JobCallbackTask` / 状态枚举 / `JobCallbackDeliveryPolicy`（退避常量）/ `JobCallbackUrl`（scheme 白名单） |
| 分发 | FilePackageService.cs | 包目录基类：路径穿越防护、列表 / 查询 / 删除 |
| | ClientPackagesService.cs / PdaPackagesService.cs / PluginPackagesService.cs | 三类包目录（客户端 MSI / PDA APK 仅 .apk 上传 / 插件包上传即校验 manifest） |
| 端点 | Api/DevicesApi.cs / Api/ServerJobsApi.cs | 服务端专属端点（设备 / 作业 / 长轮询 / 领取 / 回报 / 进度 / 包分发） |
| | ServerPluginUi.cs | 管理界面插件运行时检测与 SPA fallback 解析 |
| 日志 | FileLoggerProvider.cs | 极简文本日志：按日 + 单文件大小轮转、按个数清理、启动 / 运行中防护 |
| 模型 | Model.cs / Contracts.cs / RoutingJson.cs / LocalIpv4Candidates.cs | 领域模型与视图、作业载荷 JSON 选项、本机 IPv4 候选（决策 #169） |

**关键机制**：

- **每操作一连接**：所有数据访问经 Core `SqliteSupport.OpenAsync`（连接串 `Pooling=true` + `PRAGMA journal_mode=WAL; wal_autocheckpoint=1000`，决策 #80），无跨请求共享连接；
- **存储已按聚合拆分**（架构评审 R-3 落地）：ServerDb（132 行，建库与旧库迁移入口）＋ ServerDbStore（共享基类）＋ ServerDeviceStore（171）/ ServerJobStore（432）/ JobCallbackStore（125）三聚合存储类；148 项集成测试作安全网；
- **旧库迁移**：`InitializeAsync` 对 `last_ip` / `callback_url` / `error_code` 三列逐个 `PRAGMA table_info` 探测补列，失败静默忽略（保持旧库可启动优先）；
- **提交路径进程内门**：`_gate` 信号量只覆盖提交（requestId 幂等是「查询-再插入」两步，防并发重放双双插入），跨进程由 `request_id UNIQUE` 兜底；注册 / 领取 / 回报无 check-then-act 不再串行化（决策 #77）。

## 3. 关键流程

### 3.1 作业全链路（提交 → 通知 → 领取 → 回报）

1. **提交** `POST /api/jobs`：校验 requestId / callbackUrl（scheme 白名单，非法即拒不入队）→ 目标解析（`targetDeviceId` 优先，缺省按 `targetIp` 查 `last_ip`，未找到 `LF_SRV_012`）→ 模板解析（`templateName` 引用服务端模板库含图片 base64，否则自包含 `template`）→ `_gate` 内查-插（已有 requestId 直接返回既有作业）→ INSERT Pending + `LogJobCreated` + `_notifier.Notify(targetDeviceId)`。响应：新作业 202、重放既有作业 200（以 `Status == "Pending"` 判定，端点层）；
2. **长轮询通知** `GET /api/devices/{id}/jobs/notify?timeout=1..30`：先 `TouchDevice` 心跳保活 → **积压预检**（有未过期 Pending 立即返回 `hasPending=true`，防提交脉冲早于本请求已空放——决策 #90）→ `PendingJobNotifier.WaitAsync` 挂起至脉冲或超时；
3. **领取** `GET /api/devices/{id}/jobs/pending`：`TouchAndClaimPendingJobsAsync` **单写事务**（心跳 + 圈定合批，决策 #92）：圈定 = 单条 `UPDATE … WHERE status=Pending AND target_device_id=$id AND ($ttlCutoff IS NULL OR created_at >= $ttlCutoff) ORDER BY created_at, id LIMIT 10 RETURNING id`（原子、多实例不重复领取）→ 事务提交后同连接逐个加载载荷反序列化为 `ClaimedJob`；
4. **回报** `POST …/result`：作业归属校验（非领取者 `LF_SRV_004` 403）→ 终态幂等重放（Completed / Failed 直接返回）→ 非 Claimed 拒绝（`LF_SRV_005` 409）→ UPDATE 终态 + `LogJobFinished` → 带 callbackUrl 的作业经 `EnqueueJobCallbacksAsync` 登记投递（异步、不内联回报路径）；
5. **进度** `POST …/progress`：仅 Claimed 接受；两计数按字段 `MAX()` 单调递增；不触碰 status / finished_at / claimed_at（决策 #101）；终态幂等 no-op 返回当前视图。

**视图组装**（`ToJobViewAsync`）：作业行 + 目标设备在线状态（`OnlineWindow` 30s + `ClockSkew` 2s）+ 回调投递状态（无 callbackUrl 不查投递表——绝大多数作业零开销）。

### 3.2 生命周期终态（TTL 过期 / 失联回收）

两个扫描服务同构（`BackgroundService` + `PeriodicTimer` + 注入 `TimeProvider`，启动延迟 30s 后按 `ExpirationScanIntervalMinutes`（默认 5 分钟）执行；配置 0 / 负值时任务直接退出）：

- **TTL 过期**：`created_at` 早于 `now − PendingJobTtl`（默认 12h）的 Pending → Expired（`LF_SRV_011`，文案含 TTL 时长与「新 requestId 重发」指引）；TTL 只对 Pending 计龄，Claimed / 终态豁免（决策 #89）；
- **失联回收**：`COALESCE(claimed_at, created_at)` 早于 `now − ClaimedJobTimeout`（默认 30 分钟）的 Claimed → Failed（`LF_SRV_009`，文案明示「结果未知，可能已实际打印」）；判定只以服务端时钟计龄，与设备在线状态无关（宿主重启后照常心跳，「在线」无法区分映射丢失）——决策 #98；
- **正确性兜底不依赖扫描**：领取查询与 notify 预检的 TTL 过滤在扫描未运行时依然拦截超期作业（扫描只负责把状态标记为 Expired 让历史可见）；
- **回收与回报竞态**由「`WHERE status = Claimed`」条件收敛：回报先落库则回收不命中；回收先落库则回报幂等重放返回既有终态——两个方向互不覆盖；**回收为终态即最终，不重新投递**；
- 两扫描各自完成后都执行 `EnqueueJobCallbacksAsync` 全量登记——与回报路径合计**三处终态转移点**，全部经同一幂等登记出口（`job_id` 主键 + `INSERT OR IGNORE`），保证带回调的作业三终态无漏报。

### 3.3 终态回调投递（决策 #154）

1. **登记**：三处终态转移点（§3.2）调用 `EnqueueJobCallbacksAsync`（`jobId` 非 null 时单作业精准登记）；投递扫描每轮开头再跑一次全量登记作**自愈兜底**（补偿「终态已落库但登记缺失」窗口，如回报路径两写之间进程崩溃；幂等 SQL，无遗漏时零写入）；
2. **扫描** `JobCallbackDeliveryService`：启动延迟 5s，周期 `ScanInterval` 5 秒；取到期任务（Pending 且 `next_retry_at <= now`，批 20）逐个投递；
3. **发送** `HttpJobCallbackSender`（安全边界）：POST `application/json`、超时 10 秒（覆盖连接与响应读取）、**不跟随重定向**（3xx 原样按失败重试，防投递目标被指向任意地址）、响应体读弃不落地、只以 2xx 判成功；
4. **退避与死信**：失败按 `基值 30s × 2^(n-1)` 指数退避（30s / 1m / 2m / 4m，位移封顶防溢出），至多 `MaxAttempts = 5` 次，超限转 DeadLetter 终态不再重试；投递状态持久化于 `job_callbacks` 表——进程重启后未完成任务继续投递（**至少一次语义**，调用方按 `requestId` / `jobId` 去重）；
5. **载荷**：`JobCallbackPayload`（camelCase：jobId / requestId / status / completedAt / totalItems / completedItems / failedItems / errorMessage）——字段名与顺序即对外契约，不含 params / errorCode。

### 3.4 日志与历史清理

**文本日志**（`FileLoggerProvider`，`LABELFRAME_SERVER_LOG_FILE` 启用）：按日基名 `<名>-yyyyMMdd.log` + 单文件超上限（默认 50MB）切当日序号文件（`.1.log` → `.2.log`…）；按个数清理（默认 31 个，匹配 `<名>-yyyyMMdd[.N]<扩展名>`，按（日期, 序号）元组序删最旧）——文本层总占用上界 ≈ 31 × 50MB。三级防护：构造失败（路径无效 / 磁盘不可用）不抛异常、`FileChannelEnabled=false` 由宿主告警后跳过文件通道；轮转开新文件失败退回旧文件继续写（下次再试，不自我禁用）；写入失败自我禁用并告警一次，`Log` 永不抛出（不打断请求路径）。业务事件（作业创建 / 认领 / 终态、投递三态）经 `[LoggerMessage]` 源生成走同一 `ILogger` 管道，默认 INFO 可按类别降级（决策 #108）。

**历史清理**（`DataCleanupService`，默认 24h 周期，启动 60s 后先跑一次）：终态作业按 `JobRetentionDays`（30 天，投递行随作业同口径删除）；设备日志按 `LogRetentionDays`（90 天）；随后 **logs.db 量闸**（`LogsDbMaxSizeMB` 默认 256MB，0 / 负 = 关闭）：库文件（logs.db + -wal 合计）超阈值按大小删最旧日志直至回到阈值内——**不受保留期下限约束（有意行为）**，删除后 `VACUUM + wal_checkpoint(TRUNCATE)` 真实回收磁盘，异常态（空库仍超阈等）返回警告经 ILogger 记 Error 不中断（决策 #173）。

## 4. 对外契约与依赖

**接口面**（HTTP，全部无鉴权）：

| 端点 | 说明 |
|---|---|
| `POST/GET /api/devices`、`GET /api/devices/by-ip/{ip}` | 注册 / 目录（含在线状态）/ 按 IP 查找 |
| `POST/GET /api/jobs`、`GET /api/jobs/{jobId}` | 提交（幂等 / targetIp / callbackUrl）与集中查询（deviceId 过滤） |
| `GET /api/devices/{id}/jobs/notify` / `jobs/pending` | 长轮询通知（心跳保活）/ 领取（含载荷） |
| `POST /api/devices/{id}/jobs/{jobId}/result` / `progress` | 终态回报 / 进度增量（错误语义一致：404 / 403 / 409 / 400） |
| `GET/POST/GET/DELETE /api/client-packages`、`/api/pda-packages`、`/api/plugin-packages` | 三类包列表 / 上传 / 下载 / 删除（文件名路径穿越防护；APK 下载 MIME 专用；插件包上传先校验后落盘） |
| `GET /healthz`、`/api/server/info`、`/api/server/ipv4-candidates` | 健康检查 / 版本与 UI 探测 / 本机 IPv4 候选（下载中心连接信息卡） |
| 共享端点（LabelFrame.Api 装配） | `/api/templates*`、`/api/print/render*`、`/api/import/*`、`/api/logs*` |

错误体系：`LF_SRV_001~013`（新增码须同步 `web/src/i18n/locales/*/errorCodes.json`，覆盖断言测试挡漏译）；错误消息模板化，`params` 随异常透传 `ErrorView` 供展示端按码表插值（决策 #166）。

**依赖**：`LabelFrame.Core`（`SqliteSupport` / `TemplateStore` / `SqliteLogStore` / `SafeFileName` / `PluginPackageReader` / `TransportPluginIdPolicy` / `ErrorMessageTemplates` / `LabelElementJsonConverter`）＋ `LabelFrame.Api`（共享 DTO 与端点）＋ `LabelFrame.Rendering`（`SkiaLabelRenderer` 单例，模板预览 / 调试出图共用）＋ NuGet：`Microsoft.Data.Sqlite.Core 8.0.4` + `SQLitePCLRaw.provider/lib.e_sqlite3 2.1.13`、`Microsoft.Extensions.Hosting.WindowsServices 10.0.0`（仅 windows TFM）。目标框架 `net10.0;net10.0-windows` 双目标（决策 #59）。

**被依赖**：无 C# 工程引用本模块（叶可执行，仅自测试工程引用）。HTTP 消费方——`LabelFrame.WinHost` `ServerJobPoller`（注册 / notify / 领取 / 回报）、`LabelFrame.AndroidHost` `ServerPoller`（同一契约）、web 前端（下载中心 / 连接信息 / server 模式管理界面）、业务系统（`POST /api/jobs` + 终态回调 webhook）。部署面——Server MSI（`packaging/*-server.wxs`）、Ubuntu 镜像（`packaging/ubuntu/Dockerfile`，`LABELFRAME_SERVER_LISTEN=0.0.0.0:53961` + `/var/lib/labelframe/server` 卷）、systemd 单元、release / nightly-perf workflow。

## 5. 设计取舍（决策索引）

| 主题 | 决策 |
|---|---|
| 设备定向投递、宿主轮询投递（不开入站端口） | #6 / #21 |
| 离线暂存 Pending；暂存 TTL 过期（Expired 终态）修订「不设过期」 | #22 / #89 |
| notify 挂起前积压预检 | #90 |
| Claimed 作业超时回收（宿主失联 → Failed） | #98 |
| 作业进度增量上报（max 单调、不带宿主时间戳） | #101 |
| 终态回调 webhook（至少一次、退避、死信） | #154 |
| 服务端 / 客户端拆分；UI 归属反转（服务端默认无头） | #52 / #53 |
| 服务端管理界面插件形态（plugins/web-ui） | #62 |
| Windows 服务部署；数据目录 ProgramData | #54 / #55 |
| 跨平台部署（多目标、Linux /var/lib、systemd） | #59 |
| 停机超时 5s（安装 / 升级链依赖） | #60 |
| 设备 IP 记录与按 IP 查找；同 IP「最近活跃优先」 | #61 / #113（缺陷 #46 修复） |
| 客户端 / PDA / 插件包分发（三目录 + API） | #71 / #119 / #72 |
| API 契约与端点共享库（LabelFrame.Api） | #75 |
| 数据层并发模型（UPDATE…RETURNING、提交门收窄） | #77（写合批 #92） |
| 全局异常处理与错误契约（500 不透堆栈） | #78 |
| 局域网信任模型（无鉴权） | #79 |
| SQLite WAL + 数据层基建（SqliteSupport 收拢） | #80 |
| 日志基础设施加固（轮转 / 保留 / 启动防护 / 业务事件） | #108 |
| 日志保留量闸（文本 50MB / logs.db 256MB / VACUUM） | #173 |
| 错误消息多语言链路（ErrorView.params + 码表） | #166 |
| 下载中心连接信息卡（ipv4-candidates） | #169 |
| 测试体系（同装配集成测试、TimeProvider、Perf/Soak Trait、SQLite provider 竞态与测试串行） | #82 / #83 / #66 / #85 |

## 6. 平台坑与不变量

修改本模块前必读（源码中对应位置保留白名单注释）：

1. **提交幂等双防线缺一不可**：进程内 `_gate` 串行化「查询-再插入」（防并发重放双双插入），DB `request_id UNIQUE` 兜底跨进程——只留 DB 层会在高并发下产生两次 INSERT 竞态窗口。门的覆盖面只限提交路径（决策 #77）。
2. **领取原子性**：心跳 + 圈定必须在**同一写事务**，圈定必须是单条 `UPDATE … RETURNING`——拆成两个自动提交事务会在 SQLite 单写锁下多排一次队且引入竞态（决策 #77 / #92）。
3. **TTL 过滤是正确性兜底，不是优化**：领取查询与 notify 预检都内联 `created_at >= ttlCutoff` 过滤，不依赖扫描周期；扫描只做状态标记（Expired）让历史可见。改任何一侧须保持「CreatedAt + TTL」判定口径一致，否则超期作业会被下发。
4. **三处终态转移点必须都经登记出口**：宿主回报、失联回收、超 TTL 过期——任何一处直接改状态而不调 `EnqueueJobCallbacksAsync`，带回调的作业就漏报（调用方永久悬挂）。登记幂等（`job_id` 主键 + `INSERT OR IGNORE`），重复到达安全。
5. **回收终态即最终，不重新投递**：与回报的竞态由 `WHERE status = Claimed` 收敛；迟到的真实回报按幂等重放返回既有终态、不覆盖。把回收改成「重投」会破坏「结果未知」语义（可能重复打印）。
6. **进度上报不得触碰 `claimed_at`**：`claimed_at` 是超时回收的计龄依据，刷新它会让失联作业永不被回收；进度只 `MAX()` 单调改两个计数（决策 #101）。
7. **回调发送器安全边界**：不跟随重定向（3xx 按失败重试，防投递目标被重定向指向任意地址）、10s 超时、响应读弃不落地、只以 2xx 判成功——放宽任一项即破坏决策 #154 的安全边界。发送器为 Server 专属出站通道，不与任何宿主共享 HttpClient。
8. **`FileLoggerProvider.Log` 永不抛出**：构造失败不抛（宿主必须能启动，降级由调用方告警）；轮转失败 ≠ 写入失败（退回旧文件继续写，不自我禁用）；只有真实写入失败才自我禁用。清理必须按（日期, 序号数值）元组序——文件名 Ordinal 串序下 `.10` 排在 `.2` 之前、基名文件误排最新，极端压力会选中写入中文件去删，Windows 打开句柄 `File.Delete` 抛 IOException 中止整轮清理（已修复的缺陷，见测试锚点）。
9. **旧库迁移失败静默是特性**：补列 ALTER 失败（列已存在等竞态）catch 静默——「保持旧库可启动」优先于「迁移必须成功」。
10. **集成测试隔离依赖装配顺序**：测试经进程环境变量 `LABELFRAME_SERVER_DB` 等指向临时库拉起完整 Program，测试类**禁止并行**（`AssemblyInfo` `DisableTestParallelization`）；`SQLitePCLRaw` provider 经 `[ModuleInitializer]` 一次性初始化防并发首开竞态（决策 #66 / #85）。改 Program 的配置读取顺序或拆 DB 初始化会破坏隔离。
11. **Kestrel 请求体上限已接 64MB**：`MaxRequestBodySize = PluginPackageLimits.MaxBytes`——默认约 30MB 会让插件包上传返回 413 且无错误体。
12. **停机超时 5s 是安装链契约**：MSI 升级 / 卸载依赖 Server 5 秒内停止（决策 #60），调大会拖慢安装链。
13. **文件名一律经 `SafeFileName` 规范化**：三个包目录服务共享基类的路径穿越防护（拒绝路径分隔符 / `..`），绕过即在服务端开目录穿越读写。
14. **错误码新增的联动义务**：`LF_SRV_*` 新码须同步 web 端码表（`errorCodes.json`，覆盖断言测试挡漏译）；同一语义不得复用旧码带不同参数键集（历史拆码 #113 / LF_SRV_012、LF_SRV_013 均为此）。
