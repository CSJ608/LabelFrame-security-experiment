# LabelFrame.Core 模块设计（契约中枢）

> 覆盖：`src/LabelFrame.Core/`（76 文件，约 4,700 行）＋ `test/LabelFrame.Core.Tests/`（32 文件，约 3,600 行）· 基线：`master` `7486dae`（2026-10-19）
> 分工与模板说明见 [README.md](README.md)；决策原文见 [DESIGN.md](../DESIGN.md) 决策表。

## 1. 职责与边界

**做什么**：全仓共享内核——所有宿主（Server / WinHost / AndroidHost）与插件共用的领域模型、持久化与机制层，**零项目引用**（仅三个 NuGet 包，见 §4）。承载：

- **契约**（Contracts）：标签场景的字段清单模型（`LabelContract` / `LabelField`），可版本化（决策 #1 契约与版式分离）；
- **中间文档与版式**（Documents / Layout）：毫米坐标的版式模型（六类元素 + 区域锚定）、解析几何（`LabelLayoutResolver`）、中间文档（`LabelDocument`）、元素判别式 JSON 序列化（决策 #2、#33）；
- **校验**（Validation）：契约数据必填校验，问题码 `LF_VAL_xxx`（决策 #13）；
- **图片编码**（Encoding）：整版 1bpp 位图 → `^GF` ZPL（`ZplImageEncoder`，图片打印模式的物理载体，决策 #17 / #50）；
- **作业队列**（Jobs）：幂等提交、逐张状态、挂起 / 恢复 / 取消 / 重打、批内顺序，SQLite 持久化（决策 #5 / #14 / #16 / #25）；
- **模板库**（Templates）：模板 CRUD（SQLite）＋ 模板包 zip 导入导出（决策 #23 / #41 / #45）；
- **传输与插件**（Transport）：打印传输接口与 TCP 9100 / Log 实现（决策 #18）、传输插件 ABI 全套（插件接口 / 注册表 / 目录加载 / 安装 / 包格式 / 参数模型，决策 #67 / #68 / #72 / #73 / #123 / #127 / #157）、文档编译能力接口与打印方式参数（决策 #137）；
- **基建**：SQLite 公共基建（Data，WAL + 连接串 + 时间格式化，决策 #80）、设备日志库（Logs，量闸回收，决策 #173）、Excel 模板读写（Excel，决策 #158）、安全文件名（IO）、错误消息模板插值（Errors，决策 #166）。

**不做什么**（边界外去向）：

- 位图渲染——`LabelFrame.Rendering`（Skia，Server / WinHost）与 `AndroidHost.AndroidLabelRenderer`（平台位图）各自实现，Core 只定义 `LabelBitmap` 与 `ITextWidthMeasurer` 接口；
- HTTP 端点与 DTO——`LabelFrame.Api`（Server / WinHost 共享契约层）与各宿主自己的 API 面；
- 作业投递 / 设备目录 / 进度上报 / Claimed 超时回收——Server 侧（`ServerService` 等，决策 #98 / #101）；
- 打印 Worker 编排、连接管理（connection.json）、模拟打印出图落盘——WinHost / AndroidHost 宿主层；
- Windows 驱动打印（winspool）与 Zebra 品牌传输 / 品牌原生指令编译——`LabelFrame.WinHost` 与官方外置插件 `LabelFrame.TransportPlugin.Zebra`（决策 #18 / #123 / #137）；
- 前端动态表单渲染——web 按 `TransportParameterSpec` 自行渲染（Core 只声明规格）。

## 2. 内部结构

| 子系统 | 目录 | 关键类型与机制 |
|---|---|---|
| Contracts | `Contracts/` | `LabelContract` / `LabelField` / `LabelFieldType`（Text / Number；`Pattern` 仅存元数据不执行校验） |
| Documents | `Documents/` | `LabelDocument`（版式 + 字段数据 + 附带图片）；`LabelBitmap`（1bpp、行按字节对齐、MSB 在前 1=黑）；`MissingImageResourceException`（缺图显式报错，决策 #184） |
| Layout | `Layout/` | 六类元素（text / barcode / qrcode / image / line / region）+ `LabelLayout`；`LabelLayoutResolver`（区域锚定解析几何，决策 #33 / #110 / #146）；`LabelElementContent`（`Literal ?? data[SourceKey]`，决策 #35）；`LabelElementJsonConverter`（"type" 判别子类型）；`ITextWidthMeasurer`（度量注入接口，决策 #110） |
| Validation | `Validation/` | `LabelValidator`（仅必填缺失校验——底线「缺数据不打半张」）；`LabelProblemCodes`（`LF_VAL_001`） |
| Encoding | `Encoding/` | `ZplImageEncoder`（`^XA`/`^PW`/`^LL`/`^GFA` 整版位图输出；`DefaultDpi = 203`；毫米→点四舍五入远离零） |
| Jobs | `Jobs/` | `LabelJobQueue`（进程内互斥 `_gate` + 唤醒信号 `_pendingWake`；双载荷重载 `SubmitAsync(requestId, IReadOnlyList<LabelJobPayload>)`——文本 / 二进制指令二选一，决策 #195）；`LabelJobPayload`（`OfText` / `OfBytes`）；`SqliteLabelJobStore`（表 `jobs` / `job_items`——含 `command_bytes BLOB` 加列迁移，`request_id` UNIQUE 幂等）；`LabelJobException`（问题码 + 模板参数）；`JobErrorCodes`（`LF_JOB_001~004` / `LF_IO_001` / `LF_API_001` / `LF_ENC_001~003`） |
| Templates | `Templates/` | `TemplateStore`（SQLite 表 `templates` / `template_images`：`id`=不透明 uid、`name` 列承载模板名（唯一索引），保存带 uid＝同模板改名更新、无 uid＝新建，分组列表 + 旧库补列迁移）；`TemplatePackage`（契约 + 版式 + 图片 + testData + 可选 uid）；`TemplatePackageSerializer`（zip：`manifest.json` + `images/`，不携带身份字段） |
| Transport | `Transport/` | `IPrintTransport`（文本 `SendAsync(string)` + 二进制 `SendAsync(byte[])` DIM 缺省抛 `NotSupportedException`，决策 #195）+ `IPrinterStatusProvider` + `ITestableTransport`；内置实现 `Tcp9100PrintTransport`（发送 + `~HS` 状态解析 `ParseHostStatus`）与 `LogPrintTransport`（模拟打印，只记摘要） |
| Transport / Plugins | `Transport/Plugins/` | 插件 ABI：`ITransportPlugin`（含 `DisplayNameKey` / `DescriptionKey` 展示端翻译键 DIM，决策 #192）/ `ITransportPluginRegistry`（实现 `TransportPluginRegistry`，**强持有插件 ALC 根**）/ `ILabelCommandCompiler`（可选文档编译能力，决策 #137）、`ILabelImageEncoder`（可选图片编码能力＋`LabelImageEncodeOptions` 连接级选项，决策 #195 / #196）；`PluginDirectoryLoader`（两条加载路径，见 §3.2）；`PluginProbe`（安装预检探针）；`PluginInstaller`（安装 / 卸载 / 已装列表）；`BuiltinTransportPlugins`（log + tcp9100）；`TransportPluginIdPolicy`（官方前缀 / 保留 id / 旧 id 别名）；`TransportPrintMode`（image / native，决策 #137）；`TransportParameterSpec`（含 `VisibleWhen*` 条件可见性 + `LabelKey`/`HintKey` 展示端翻译键（init-only 属性、构造器签名零变化）+ 历史 7 参构造器兼容，决策 #186 / #192） |
| Transport / Plugins / Package | `Transport/Plugins/Package/` | `.lfplugin` 包读取：`PluginPackageReader`（zip-slip 防护 + 魔数判别）、`PluginPackageManifest`（含可选 `platforms` 平台门，决策 #157）、`PluginPackageLimits`（64MB）、`PluginVersionComparer`（官方覆盖安装版本比较，决策 #123）、`PluginPackageException` |
| Data | `Data/` | `SqliteSupport`：provider 初始化（进程内一次）、连接串（busy 5s + 连接池 + 父目录自动建）、WAL 打开、UTC ISO-8601 往返格式化（决策 #80 / #85 / #94） |
| Logs | `Logs/` | `SqliteLogStore`：设备日志入库 / 查询（最多 500 条）/ 按量闸循环「删最旧批次 → VACUUM + checkpoint(TRUNCATE)」（决策 #173） |
| Excel | `Excel/` | `ExcelTableReader`（xlsx → 文本表，日期 / 数值 InvariantCulture 定格）、`ExcelTemplateWriter`（契约字段 → 「表头 + 示例行」xlsx；Sheet 名「打印数据」） |
| IO | `IO/` | `SafeFileName`（只收普通文件名：拒路径分隔符 / `..` / 非法字符） |
| Errors | `Errors/` | `ErrorMessageTemplates`：`{key}` 占位符单遍替换，缺参保留原文不抛（决策 #166） |

**测试**（`test/LabelFrame.Core.Tests/`）：xunit，29 文件。按子系统分目录（Jobs / Json / Layout / Templates / Transport / Excel / IO / Encoding / Validation）；夹具：`TempJobDb`（临时 SQLite 作业库）、`LocationLabelSamples`（库位码契约 + 版式 + 文档样例）、`SqlitePclModuleInit`（`[ModuleInitializer]` 初始化 provider，防并行竞态）；外部插件装配矩阵用 `LabelFrame.TransportPlugin.Sample` / `Fake` 两个测试插件工程。Excel 回归测试直接用 OpenXml 构造第三方形态（与 Core 侧 `DocumentFormat.OpenXml` 同钉 3.3.0）。

## 3. 关键流程

### 3.1 作业生命周期（`LabelJobQueue` + `SqliteLabelJobStore`）

状态机：Job `Pending → Printing → Suspended / Completed / Failed / Cancelled`；Item `Pending → Printing → Completed / Failed / Cancelled`（决策 #5 / #14 / #16）。

1. **提交**（`SubmitAsync`）：`_gate` 锁内查 `request_id`——已存在直接返回既有作业（幂等，`Created=false`）；否则建 Job + N 个 Item（每张持久化完整打印机指令，**不可变**）落库，**写入提交后** `SignalPendingWake()` 唤醒 Worker；
2. **领取**（`ClaimNextItemAsync`）：同时扫 Pending 与在途 Printing 作业（最旧优先，批内可连续领取），取序号最小的 Pending Item 置 Printing；
3. **完成 / 失败**（`CompleteItemAsync` / `FailItemAsync`）：全部 Completed → Job Completed；失败但仍有 Pending → Job **Suspended**（等人工恢复），无剩余 Pending → Job Failed；
4. **挂起 / 恢复 / 取消 / 重打**：状态非法抛 `LF_JOB_002`；重打仅 Failed Item（`LF_JOB_004`，条目越界 `LF_JOB_003`），重打后 Failed 作业回 Pending、Suspended 保持挂起；恢复与重打同样在写入后发唤醒信号；
5. **重启恢复**（`MarkInterruptedJobsSuspendedAsync`）：宿主启动时把 in-flight（Printing）作业置 Suspended、在途 Item 重置 Pending——恢复后续打优先保证不漏打（决策 #16）；
6. **安全退出**（`TryPrepareForShutdownAsync` / `TryBeginPrintSendAsync`）：锁内确认无在途发送（`_activeSends` 计数）且无 Printing Item 才置 `_shutdownPrepared`，此后拒绝提交与领取；真实传输区间以 lease 登记。

### 3.2 传输插件装配（启动扫描 → 注册表 → 按需创建）

加载生命周期口径＝决策 #127（`PluginDirectoryLoader` 两条路径）：

- **平铺手动 DLL**：逐 DLL 独立 collectible ALC，`LoadFromStream` 字节加载（不锁文件，决策 #73），伴生依赖经 `Resolving` 惰性解析（宿主默认上下文同名程序集优先 → 同目录 DLL）；
- **子目录安装包**：包目录单 collectible ALC 整体加载，全部 DLL **预载**进同一上下文（消灭首用惰性解析窗口）＋跨程序集发现；
- 两条路径的 ALC 根都随发现结果返回，`TransportPluginRegistry.Register` **显式强持有**（collectible ALC 被丢弃后 GC 发起卸载会破坏其状态机，此后惰性解析抛 `VerifyIsAlive`——曾出的真机缺陷）；无插件产出的上下文显式 `Unload()` 确定性释放；
- 单 DLL 失败只记 `loadError`（宿主日志 + 结构化 errors 透出「已安装插件」列表），不阻断宿主启动。

使用侧：配置 `pluginId + params` → `TransportPluginRegistry.CreateTransport` 调插件工厂 → 得 `IPrintTransport`（+ 可选状态 / 连接测试能力）。插件对象另实现 `ILabelCommandCompiler` 即具备文档编译能力（descriptor 能力位 + `GetCommandCompiler` 取用，决策 #137）；实现 `ILabelImageEncoder` 即具备图片模式品牌编码能力（`GetImageEncoder` 取用——未实现维持宿主 `^GF`，决策 #195）。

### 3.3 插件包安装（`PluginInstaller.InstallAsync`，三层校验）

1. 包完整性：`PluginPackageReader.Read`——zip 魔数判别（PK 头）、根 `manifest.json` 必填字段、条目名 zip-slip 防护（拒绝绝对路径 / 盘符 / `..` 段）、至少一个 DLL；
2. 平台门与 id 门：宿主平台不在 manifest `platforms` 集合内拒绝（**无字段的既有包按 Windows 端解释**——存量包零迁移，决策 #157）；pluginId 与宿主注册表内置插件冲突拒绝（内置保留 id：log / tcp9100 / winspool）；官方插件（`labelframe-` 前缀）覆盖安装先做版本比较——新版覆盖 / 同版幂等跳过 / 降级拒绝（决策 #123）；
3. ALC 预检：临时目录解压 + `PluginProbe`（探针 ALC 发现插件并核对 manifest.pluginId 一致，不 Create 传输实例；DLL 非 .NET 程序集给可行动中文提示）；
4. 落位：删旧目录（字节加载不锁文件，运行中可直接删除；被外部占用则提示重启重试）→ `Directory.Move` 进 `plugins/<pluginId>/`（跨卷回退复制）——**重启后生效**，运行时热卸载不做。

### 3.4 版式解析与内容取值（Layout）

- `LabelLayoutResolver.ResolveBounds`：未锚定 → 绝对坐标 + 自然尺寸（文本自然高 = `HeightMm > 0 ? HeightMm : FontHeightMm`，条码自然宽 = 高 × 2.5）；锚定 → `x = 区域X +（区域宽 − 锚定宽）× 因子`、`y = 区域Y +（区域高 − 块高）× 因子`（Start/Center/End → 0/0.5/1）。自动宽度文本的水平锚定偏移按**实测文本宽度 + 2×有效水平内边距（夹取到区域宽）**计算（方案 A，决策 #110），块宽仍为区域全宽（块内 TextAlign / 边框 / 裁剪语义不变）；未注入度量器回退既有几何（锚定宽 = 块宽 → 偏移 ≈ 0）。垂直锚定：Y 为锚定框（自动高度时 = 字高框）顶部，两模式（渲染 / 原生指令）同框（决策 #146）；
- `LabelElementContent.Get / TryGet`：`Literal ?? data[SourceKey]`，两者皆无返回空；`Get` 缺数据抛 `ArgumentException`（编码链显式失败），`TryGet` 容错返回 false（预览链）——ZPL 编码与预览渲染共用保证一致（决策 #35）；
- `LabelElementJsonConverter`：读方向按 "type" 判别子类型后委托默认反序列化（不依赖 `JsonPolymorphic`，ASP.NET Core HttpJsonOptions 环境稳定）；写方向「非默认才写」省略风格，与前端 `convert.ts` 一致。

### 3.5 模板保存与 testData 合成（`TemplateStore.SaveAsync`）

单事务内：先删撞名行（`name` = 新名且 `id` ≠ 目标 uid，连带图片）——无 uid 保存因此等价于旧的按名覆盖（旧前端兼容）；目标行按 `id` upsert（带 uid＝同模板改名更新，无 uid＝生成新 uid 新建，决策 #205）。testData 读-改-写：以库中现有 `test_data_json` 为基底（基底定位＝带 uid 按 id 取本行——改名随行携带、撞名覆盖不并入被撞行；无 uid 按 name 取被替换行，决策 #205）→ 并入显式传入的 `TestData` → 被元素 `PreviewValue`（text / barcode / qrcode 字段填充模式）派生覆盖——**预览值优先**（单一事实来源，决策 #45）；图片全量替换（先删后插）；不传 testData 且布局无预览值时旧显式值保留。旧库自动补 `test_data_json` 列与 `name` 列（回填 `name = id`，存量行 id=旧名即 uid；ALTER 容错「已存在」，决策 #205）。模板包 zip（`TemplatePackageSerializer`）：`manifest.json`（name / group / contract / layout / testData）+ `images/`（键名 sanitize，仅字母数字 `.` `-` `_`）；导入缺 group 归「默认」、缺 testData 归空，身份字段不进出包（决策 #23 / #41）。

## 4. 对外契约与依赖

**接口面**（全公开于 `LabelFrame.Core.*`，被全仓消费的稳定面）：

- 模型：`LabelContract` / `LabelLayout` / 六类 `LabelElement` / `LabelDocument` / `LabelBitmap` / `TemplatePackage` / `LabelJob` / `LabelJobItem` 及各枚举；
- 解析与取值：`LabelLayoutResolver` / `LabelElementContent` / `ITextWidthMeasurer`（Rendering 与 AndroidHost 各提供实现）；
- 作业：`LabelJobQueue`（+ `ILabelJobStore` / `SqliteLabelJobStore`）、`LabelJobPayload`（文本 / 二进制双形态回放载荷，决策 #195）、`LabelJobException` / `JobErrorCodes`；
- 模板：`TemplateStore`、`TemplatePackageSerializer`；
- 传输：`IPrintTransport` / `IPrinterStatusProvider` / `ITestableTransport`、`Tcp9100PrintTransport.ParseHostStatus`（纯函数供单测）、`PrinterStatusInfo`；
- 插件 ABI（第三方 DLL 实现面）：`ITransportPlugin`（含 `DisplayNameKey` / `DescriptionKey` DIM 展示端翻译键，决策 #192）/ `ITransportPluginContext`（+ `TransportPluginContext` 默认实现）/ `ITransportPluginRegistry`（+ `TransportPluginRegistry`）/ `TransportPluginDescriptor` / `TransportParameterSpec` / `TransportParameterOption` / `TransportParameterType` / `TransportPluginParameters` / `ILabelCommandCompiler` + `LabelCommandCompileOptions/Result` / `ILabelImageEncoder` + `LabelImageEncodeOptions`（连接级图片编码选项，含 `InvertBitsParameterKey` 键约定，决策 #195 / #196） / `TransportPrintMode` / `TransportPluginIdPolicy` / `BuiltinTransportPlugins`；包格式：`PluginPackageReader` / `PluginPackageManifest` / `PluginPackageLimits` / `PluginVersionComparer` / `PluginPackageException`；
- 服务：`PluginInstaller` / `PluginDirectoryLoader` / `PluginProbe`、`SqliteLogStore`、`ZplImageEncoder`（`EncodeImage`）、`LabelValidator` / `LabelProblemCodes`、`ExcelTableReader` / `ExcelTemplateWriter`、`SafeFileName`、`SqliteSupport`、`ErrorMessageTemplates.Format`、`MissingImageResourceException`。

**依赖**（仅 NuGet，零项目引用；`net10.0` 单目标）：`Microsoft.Data.Sqlite.Core 8.0.4`、`SQLitePCLRaw.provider.e_sqlite3 2.1.13`（**原生库 `lib.e_sqlite3` 刻意不下沉 Core**，见 §6）、`TemplateFrame.Excel.Simple 2.4.1`（决策 #158）。

**被依赖**（直接 `ProjectReference`，grep 全仓查证）：

| 消费方 | 用途（实测引用面） |
|---|---|
| `LabelFrame.Api` | 模板端点（`TemplateStore` / `TemplatePackageSerializer`）、Excel 导入导出端点、日志端点（`SqliteLogStore`）、出图端点（`MissingImageResourceException`）、错误模板 |
| `LabelFrame.Server` | 插件包上传校验（`PluginPackageReader` / `TransportPluginIdPolicy`）、下载中心文件名（`SafeFileName`）、日志清理（`SqliteLogStore`）、模板下发、`SqliteSupport` |
| `LabelFrame.WinHost` | 打印主链（`LabelJobQueue` / `ZplImageEncoder` / `LabelValidator`）、插件装配（`PluginDirectoryLoader` / `PluginInstaller` / `TransportPluginRegistry` / `BuiltinTransportPlugins`）、模板库、`TransportPrintMode`、winspool 插件按 `ITransportPlugin` 注册 |
| `LabelFrame.AndroidHost` | PDA 宿主：作业队列 + SQLite 存储、插件双档（SDK 档内置注册 + 轻量档 `PluginDirectoryLoader`/`PluginInstaller` 动态加载）、平台位图渲染消费 `LabelBitmap` / `LabelLayoutResolver` |
| `LabelFrame.TransportPlugin.Zebra` | 官方外置插件：实现 `ITransportPlugin` + `ILabelCommandCompiler`（版式解析 / 内容取值 / 图片编码复用 Core） |
| `LabelFrame.Rendering` | 渲染器消费 `LabelDocument` / `LabelLayoutResolver` / `LabelElementContent` / `LabelBitmap`，提供 `ITextWidthMeasurer` 实现 |
| 测试 | `LabelFrame.Core.Tests`（本模块）；`Benchmarks`、`AndroidHost.Tests`、`TransportPlugin.Sample` / `TransportPlugin.Fake`（测试插件工程） |
| Bootstrapper（间接同构） | net48 腿**不引用** Core——`BrandPluginMap` 手工复刻 `TransportPluginIdPolicy` 常量（官方插件 id / 品牌映射），改 id 策略须双侧同步 |

## 5. 设计取舍（决策索引）

| 主题 | 决策 |
|---|---|
| 契约与版式分离、中间文档模型 + 毫米坐标 | #1 / #2 |
| 异步作业模型；作业模型与 SQLite 持久化；挂起 / 恢复语义；失败项单独重打 | #5 / #14 / #16 / #25 |
| 中文渲染架构（`LabelBitmap` 1bpp + `^GF`）；中文渲染下沉编码器；图片打印收敛 | #4 / #17 / #50 |
| 传输分层（TCP 9100 在 Core，winspool 归 WinHost） | #18 |
| 在线状态 / 测试页（`~HS` 基础解析）；Zebra 状态映射修正（官方字段表 + 无响应一等异常态） | #26 / #109 |
| 模板包格式（zip：manifest + images）；模板测试数据 testData；模板预览值派生 testData | #23 / #41 / #45 |
| 模板身份化（uid 主键 + name 列，改名保存＝同模板更新）；元素内边距显式 0 契约（在场即显值，缺省才兜底 paddingMm） | #205 |
| 元素样式与区域（格子）布局；元素内容来源（`Literal ?? data[SourceKey]`）；文本垂直对齐契约；元素契约第二批字段（wrap / lineHeight / fitMode / fontFamily / 双边内边距）；字体加粗契约 | #33 / #35 / #46 / #47 / #49 |
| 区域水平锚定按实测宽度（方案 A + 度量接口注入）；锚定文本垂直坐标语义统一 | #110 / #146 |
| 传输插件统一接口与参数模型；加载 / 卸载 / 使用；外部插件字节加载；ALC 生命周期（注册表强持根） | #67 / #68 / #73 / #127 |
| 插件包分发闭环（`.lfplugin`）；品牌插件外置化与官方插件体系（id 前缀 / 版本比较）；PDA 双档模型与跨端格式（`platforms` 平台门） | #72 / #123 / #157 |
| 插件命令打印契约（`ILabelCommandCompiler` + 连接级 printMode）；连接参数字段集声明（`VisibleWhen` 条件可见性 + 历史构造器兼容） | #137 / #186 |
| 二进制指令载荷＋图片编码器插件分派＋连接级编码选项（`CommandBytes` BLOB / `SendAsync(byte[])` DIM 重载 / `ILabelImageEncoder` 选项重载与 `invertImage` 键约定） | #195 / #196 |
| 插件自描述文案展示端翻译键（spec `LabelKey`/`HintKey` + 接口 DIM `DisplayNameKey`/`DescriptionKey`；展示端 pluginLabels 查词条、无键回退原文；旧插件源码与二进制不受影响） | #192 |
| 问题码约定（LF_*）；全局异常处理与错误契约；ErrorView `params` 与错误消息模板插值 | #13 / #78 / #166（多语言基线 #164） |
| 数据层并发模型（EXISTS 轻量探测）；SQLite WAL + 数据层基建；provider 初始化与测试隔离；SQLitePCLRaw 原生库 Android 打包 | #77 / #80 / #85 / #94 |
| Worker 信号量唤醒（`WaitForPendingWakeAsync`，写入后发信号） | #92 |
| 日志保留量闸（logs.db 按量闸 + VACUUM 空间回收） | #173 |
| 依赖升级 TemplateFrame.Excel.Simple 2.4.1（Excel 容错与读性能，OpenXml 3.3.0 同钉） | #158 |
| 图片控件（缺资源显式报错 `MissingImageResourceException`） | #184 |
| 打印机连接文案用户化（Log 插件「模拟打印」显示名，Id 保持 `log`） | #143 |

## 6. 平台坑与不变量

修改本模块前必读（源码中对应位置保留白名单注释）：

1. **SQLitePCLRaw 原生库不下沉 Core**：`lib.e_sqlite3` 若被 Core 引用，经 RID 回退图（android-arm64 → linux-arm64）会把桌面 glibc so 打进 Android APK、压过 AndroidHost 的 NDK so——原生包由各可执行项目自引（决策 #94）。
2. **唤醒信号必须在存储写入提交后发出**：先信号后提交会让 Worker 探测落空、信号已被消费而错过唤醒；信号语义是「可能有变化」而非「一定可领取」，超时轮询仅兜底（决策 #92）。
3. **插件 ALC 根必须强持有**：collectible ALC 被丢弃后 GC 发起卸载会破坏其状态机，此后任何 `LoadFromStream`（含惰性解析）抛 `VerifyIsAlive`——加载器返回的 ALC 根由注册表在宿主生命周期内持有；包目录必须预载全部伴生依赖（消灭首用惰性窗口）（决策 #127）。
4. **字节加载 = 不锁文件**：卸载 / 覆盖安装依赖「删除插件文件即可、运行中进程继续用内存镜像」——不得改回 `LoadFromAssemblyPath`（会锁 Windows DLL 文件）；副作用：插件 `Assembly.Location` 为空，自定位资源走 `ITransportPluginContext.DataDirectory`（决策 #73）。
5. **`TransportParameterSpec` 历史 7 参构造器不可删**：外部插件 DLL 按旧签名编译，宿主升级而插件未随发时仍须可加载——删掉旧构造器会在装配期 `MissingMethodException` 崩宿主（决策 #186；二进制兼容防线测试锚定）。
6. **`~HS` 无响应 / 超时 / 响应不完整 = 异常态**：官方声明缺纸 / 耗材用尽 / 打印头打开等状态下打印机可能完全不响应——绝不映射为正常；缺纸 / 暂停位在**首行第 2 / 3 字段**（决策 #109）。
7. **printMode 两个分支不可合并**：「缺省 / 非法回退 image」与「显式 native 但插件无编译能力」不同——后者保存校验拒绝 + 提交兜底 `LF_ENC_003` 显式失败，**不静默按图片打印**（决策 #137）。
8. **错误消息模板单遍替换**：参数值内嵌 `{x}` 不级联替换；参数缺失占位符原样保留不抛——展示端按码表翻译依赖此口径（决策 #166）。
9. **元素 JSON 写方向「非默认才写」**：省略风格与前端 `convert.ts` 一致（默认值清单见 §3.4）；`fitMode` 写出为小写 `shrink` / `overflow`（前端枚举形态，非 `ToString()`）。**例外——双边内边距「在场即显值」**：`paddingH` / `paddingV` 有值即写（含 0，`PaddingHMm` / `PaddingVMm` 为 `double?`——null＝未设、0＝显式零），缺省才走 `paddingMm → 0` 兜底；`paddingMm` 单值为兼容保留且仅双侧有效值（`EffectivePadding*`，`PaddingMm` 兜底后）相等且 >0 时写出该有效值（写 max 会把显式 0 经兜底链复活，决策 #205）。旧模板读取行为不变（决策 #47 / #205）。
10. **`Zpl` 字段与 DB 列名不改名**：`LabelJobItem.Zpl` 与 `job_items.zpl` 沿用历史命名（文本形态指令：图片模式 = `^GF` 位图 ZPL，原生指令模式 = 品牌原生指令）；二进制指令（如 TSPL BITMAP）走 `CommandBytes` / `command_bytes BLOB`（决策 #195）——**与 `Zpl` 互斥，非空者即回放载荷**；指令一经持久化不可变，重启后可续打且不重打——存量库兼容（旧行 NULL 走文本路径）。
11. **插件 id 兼容义务**：`log` / `tcp9100` / `winspool` 为内置保留 id（外部包禁止占用）；旧内置时代 id `zebra` 仅作**读取别名**归一到 `labelframe-transport-zebra`，不落盘迁移；Log 插件显示名用户化但 Id 保持 `log`（存量连接配置兼容）（决策 #123 / #143）。
12. **`platforms` 缺省按 Windows 解释**：无该字段的既有包在 Windows 宿主照常安装、PDA 拒绝；显式空数组非法（决策 #157）。
13. **WAL 不可用静默回退不阻断**：只读卷等场景 `PRAGMA journal_mode=WAL` 失败吞掉、回退默认日志模式（决策 #80）。
14. **量闸收敛判据只取 VACUUM + checkpoint 之后的合计大小**（主文件 + `-wal`）：DELETE 之后主文件不缩小，以删除间歇的大小判收敛会在首次超阈即删空全表；VACUUM 失败后度量不可信，本轮立即终止等下个周期（决策 #173）。
15. **Excel 依赖钉版**：`TemplateFrame.Excel.Simple` 全系列钉 `DocumentFormat.OpenXml 3.3.0`，Core 与测试项目同钉防版本漂移（决策 #158）。
16. **zip-slip 防护是安装链安全底线**：`PluginPackageReader.IsSafeEntryName` 拒绝绝对路径 / 盘符 / `..` 段，解压侧再叠加 `Path.GetFullPath` 前缀校验双层。
17. **缺图语义两条路径不同**：`sourceKey` 为空 = 未指定图片（渲染层跳过整元素）；key 非空缺资源 = 抛 `MissingImageResourceException` 显式失败——Core 定义异常，Skia / Android / 端点共用（决策 #184）。
18. **模板图片是全量替换、testData 是读-改-写**：`SaveAsync` 单事务内以库中现有 testData 为基底合并（预览值优先，不传不覆盖，决策 #45）；「缺省 = 保留存量图片」的三态语义在 Api 端点层实现（先读存量再并入，两步非同事务，单设计器场景可接受），Core 的 `SaveAsync` 收到什么图片就全量替换什么。
