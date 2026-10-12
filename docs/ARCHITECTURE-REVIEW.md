# LabelFrame 架构评审报告（1.0 前 · 只读评估）

> 基线：`origin/master` `951abe2`（v0.34.0，2026-10-12）· 评审时点：2026-10-12 · 承载：迭代 137（Issue #341）。
> 性质：**只读评审，零代码改动**；本报告为时点快照，后续演进以代码与 [DESIGN.md](DESIGN.md) 决策表为准，引用行数为「含注释与空行」的 wc 口径（附录 A）。
> 待决议处置（Issue D1 / D2）：执行会话询问未获实时应答，按建议项执行、用户过目（AC-06）时可否决——
> **D1 对标清单**＝Issue 原名单（BarTender / NiceLabel / ZebraDesigner「等」）扩至 CODESOFT 与消费级档（DYMO / Brother）＋开源同类＋前端画布设计器＋.NET 服务端通用形态；
> **D2 报告去处**＝独立本文件＋DESIGN 决策表留索引行（决策 #188；并行迭代 134（#342）先合入占用 #186/#187，本行让号）。

## 1. 结论摘要

1. **无「1.0 前必须处理」的契约级债务——可放心对 1.0 作 semver 兼容承诺。** 四个契约面（模板包格式 / 打印 API / 作业模型 / 插件 ABI）经 133 个迭代全部以「可选字段加法」演进：读取端对未知字段天然容错（JSON 反序列化忽略未知键）、`LabelJobItem.Zpl` 等易误判项均未进对外契约面、`.lfplugin` manifest 已预留 `minHostVersion` 宿主门槛字段（当前仅展示未启用校验，属已设计好的逃生门）。逐面审计见 §4.1。
2. **架构形态与业界成熟形态一致，无需结构性重构。** 分层（零依赖 Core 居中＝Clean Architecture 依赖向内）、插件契约独立面（仅依赖 Core＝微软官方插件教程建议形态）、持久化队列＋BackgroundService 投递（官方示例模式＋SQLite 补强）、设计器＋无头渲染引擎双部件、宿主桥接 Web→本地打印（QZ Tray 验证过的拓扑）——五项均有一流先例（§3）。
3. **三项「1.0 后建议」内部重构候选**（非阻塞、建议随功能迭代搭车而非专项立项）：前端 `Designer.tsx`（1,213 行，v0.34 周期 +49%，真实增长热点）、AndroidHost `MainActivity.cs`（1,601 行 / 97 方法，全仓最大文件）、Server `ServerDb.cs`（736 行，SQLite 访问单文件）。逐项成本 / 收益 / 风险 / 时点见 §5。
4. **Issue 前提校准**：Issue 点名的「App.tsx / TransportPanel.tsx 体量持续增长」与事实不符——App.tsx 现 393 行（全史峰值 433 行，2026-10-09 帮助 V2 时期），TransportPanel.tsx 440 行（v0.22 的 489 行以来净减）；两者均不构成热点（§2.4）。
5. **明确不建议** 6 项（契约包拆分、画布换第三方 SDK、API 版本前缀、模块化单体改造、Vertical Slice 重构、Minimal API 迁移），理由见 §5 表。

## 2. 体量度量（AC-01）

### 2.1 后端（`src/`，232 个 C# 文件 / 28,050 行）

| 项目 | 文件数 | 行数 | 职责（README 口径） |
|---|---:|---:|---|
| LabelFrame.Core | 72 | 4,503 | 契约 / 版式模型、数据校验、ZPL 图片编码、作业队列、模板库、传输插件接口（共享内核，零项目引用） |
| LabelFrame.Rendering | 5 | 857 | Skia 整版渲染（打印与预览同源） |
| LabelFrame.Api | 7 | 658 | Server / WinHost 共享的 HTTP 契约（DTO / 错误码）与端点实现 |
| LabelFrame.Server | 25 | 3,496 | 无头服务端：模板库 / 作业中心 / 设备投递 / 调试出图 / 日志 |
| LabelFrame.WinHost | 40 | 5,511 | Client Host 共享实现：Windows 完整客户端 + Linux 无头 Log 客户端 |
| LabelFrame.AndroidHost | 25 | 4,968 | Android / PDA 打印宿主（只依赖 Core） |
| LabelFrame.TransportPlugin.Zebra | 7 | 893 | Zebra 官方外置传输插件（只依赖 Core＝插件 ABI 面） |
| LabelFrame.Bootstrapper（三件） | 51 | 7,164 | 安装引导（共享库 3,833 / BA 3,052 / PayloadTool 279） |
| **合计** | **232** | **28,050** | |

后端最大文件 Top10：`MainActivity.cs` 1,601 · `LabelFrameBootstrapperBa.cs` 1,136 · `EmbeddedHttpServer.cs` 767 · `ServerDb.cs` 736 · `SkiaLabelRenderer.cs` 714 · `ServerService.cs` 462 · `PrintHostService.cs` 446 · `AndroidLabelRenderer.cs` 445 · `LabelJobQueue.cs` 442 · `WindowShell.cs` 407。

### 2.2 项目引用关系（分层事实）

```
Core（零依赖）
 ├── Rendering ──► Core
 ├── Api ──► Core + Rendering          # Server / WinHost 共享契约与端点
 │    ├── Server ──► Api + Core + Rendering
 │    └── WinHost ──► Api + Core + Rendering
 ├── AndroidHost ──► Core（仅此一项；自带极简 HTTP 与 DTO 副本，决策 #27 / #167）
 ├── TransportPlugin.Zebra ──► Core（仅此一项＝插件 ABI 边界）
Bootstrapper 家族独立（Ba / PayloadTool ──► Bootstrapper）
```

分层评价：**依赖方向全部向内**（Core 居中零依赖、宿主与插件在最外、无环），与 Clean Architecture 的依赖原则一致（§3.4）；Api 作为 Server / WinHost 的共享契约层消除了两端点实现的重复（迭代 27 工程治理成果）；AndroidHost 不依赖 Api 是「不引入 ASP.NET Core 共享框架」的有意取舍（APK 体积与依赖面），代价是约 95 行 DTO 副本（`Api/Dtos.cs` + `ServerPoller` 内 `JobPayloadDto`），已在决策 #167 记录并按文件链接复用码表——属可接受的已文档化重复。

### 2.3 前端（`web/src/`，117 个 TS/TSX 文件 / 25,193 行＝生产 60 文件 12,271 行＋测试 57 文件 12,922 行）

| 分区 | 生产（文件 / 行） | 测试（文件 / 行） | 说明 |
|---|---|---|---|
| pages/（含 designer/ 子目录） | 16 / 6,839 | 17 / 5,777 | 页面与设计器子组件 |
| lib/（design / api / excel + 根工具） | 29 / 3,082 | 18 / 3,265 | 纯逻辑层：几何 / 吸附 / 历史 / 转换 / 路由 / API client |
| components/ | 7 / 1,187 | 9 / 1,243 | 通用组件（Modal / Popover / Guide / HelpDot / TransportPanel…） |
| i18n/ | 2 / 238 | 6 / 1,036 | 机制层；13 个域文件（zh / en 双份 JSON 不计行数） |
| state/ | 3 / 506 | 1 / 126 | AppContext（405 行）与打印草稿 |
| src 根 | 3 / 419 | 6 / 1,475 | App 壳 393 行 / main / 类型声明 |

生产代码最大文件 Top10（测试另计）：`Designer.tsx` 1,213 · `DataPrint.tsx` 1,076 · `Settings.tsx` 837 · `designer/CanvasViewport.tsx` 678 · `DownloadCenter.tsx` 667 · `designer/PropsPanel.tsx` 482 · `lib/api/client.ts` 466 · `TransportPanel.tsx` 440 · `state/AppContext.tsx` 405 · `App.tsx` 393。

运行时依赖 9 个（react / konva / react-konva / i18next / react-i18next / jsbarcode / qrcode-generator / uuid / react-dom）——同栈商业 SDK Polotno 亦以 React+Konva 为底（§3.3），依赖面属克制水平。

### 2.4 增长趋势（按版本 tag 采样行数）

| 文件 | v0.22.0 | v0.28.0 | v0.32.0 | v0.34.0 | 趋势判定 |
|---|---:|---:|---:|---:|---|
| App.tsx | 194 | 242 | 358 | 393 | 缓增但绝对量小；**全史峰值 433**（2026-10-09），非热点 |
| TransportPanel.tsx | 489 | 432 | 442 | 440 | **净减后持平**，非热点（迭代 134 连接设置简化属交互面而非体量面） |
| Designer.tsx | 724 | 835 | 849 | **1,213** | **真实热点**：v0.34 周期 +364 行（图片控件 / 深链 / 多选拖动三连修均落此文件） |
| DataPrint.tsx | 849 | 1,034 | 1,076 | 1,076 | v0.28 Excel 批量后趋稳，缓变区 |

设计器已有的分层事实：机制已下沉 `lib/design/`（19 文件：geometry / snapping / history / convert / multidrag / model 等纯函数层，均带机制测试）、画布交互在 `pages/designer/CanvasViewport.tsx`（678 行）与 `ElementNode.tsx`，`Designer.tsx` 承担页面编排（状态 / 历史 / 面板接线 / 深链 / 演示注入）。即「store / 画布 / 面板」的业界分层（§3.3 B4）已具雏形，剩余问题是编排层单文件偏大。

### 2.5 测试形态

| 维度 | 事实 |
|---|---|
| .NET 测试 | 153 文件 / 22,872 行；`[Fact]` 834 + `[Theory]` 46 ≈ 880 个测试方法（Perf / Soak 排除在日常 CI 外，nightly 承载） |
| 分布 | WinHost.Tests 319（文件 54 / 行 8,875）· Server.Tests 148（32 / 5,133）· Bootstrapper.Tests 211（25 / 3,778）· Core.Tests 150（28 / 3,367）· Api.Tests 34（6 / 1,061）· AndroidHost.Tests 17（2 / 216，码表与纯托管面按文件链接编译）· ClientHost.Tests 1 |
| 前端测试 | 56 个测试文件 / 约 643 处用例定义（grep 口径）；CI 按 client / server 双模式各跑一遍 |
| 测试:生产比 | .NET 行数比 22,872 / 28,050 ≈ 0.82；前端行数比 12,922 / 12,271 ≈ 1.05（测试文件占全部文件 57 / 117） |
| 结构性空档 | AndroidHost UI 层（MainActivity 动态构建 UI）无组件级单测，验证通道＝真机走查——是 §5 R-2 风险评级的主要依据 |

### 2.6 耦合热点与候选重构点识别（量化依据）

| 候选点 | 量化事实 | 类别 |
|---|---|---|
| R-1 `Designer.tsx` | 1,213 行（前端最大）；v0.34 周期 +49%；近三个迭代（130/131/132）的主要改动落点 | 增长热点 / 编排层 |
| R-2 AndroidHost `MainActivity.cs` | 1,601 行 / 约 97 个方法（全仓最大文件）；配置 / 状态 / 打印 / 插件管理四类页面共存单 Activity；UI 层无单测 | 体量极值 / 低测试覆盖 |
| R-3 Server `ServerDb.cs` | 736 行：jobs / items / devices / callbacks / logs 五类 SQLite 访问共置一文件 | 聚合边界模糊（温和） |
| — `App.tsx` / `TransportPanel.tsx` | 393 / 440 行，消费面清晰（AppContext 生产消费方 12 文件） | **非热点**（Issue 前提校准） |
| — Core 内部分区 | Transport 28 / Layout 15 / Jobs 9 / Validation 4 / Templates 3…（4.5k 行共享内核） | 规模健康，无需再拆（§5 W-1） |

## 3. 业界对标（AC-02）

> 对标维度按 Issue 范围：模块划分 / 插件（扩展）机制 / 设计器与服务端关系 / 发布形态。全部结论附来源；官方来源优先，第三方渠道标注「第三方」。

### 3.1 商业标签打印产品

**Seagull Scientific BarTender**（行业头部，Windows 桌面套件）

- 模块划分：单一套装内拆十余个伴随应用——Administration Console / Data Builder / History Explorer / **Integration Builder**（业务集成，前身 Commander）/ **Librarian**（文档存储与版本）/ **Print Portal**（Web 打印界面，前身 Web Print Server）/ Print Station / Process Builder / Reprint Console 等（来源：http://help.seagullscientific.com/2019/en/Content/Companion_Apps.htm ）。
- 插件机制：扩展面收敛于「自研打印机驱动库（官方称 12,000+ 型号）+ 20 余种数据源连接 + VB 脚本 + .NET SDK / REST API（Automation 版起）」；**未查到开放第三方指令集插件的公开 API**（来源：https://www.bartendersoftware.com/product/pricing/ 、https://support.seagullsoftware.com/hc/en-us/articles/4413434027799-BarTender-REST-API-Overview ）。
- 设计器与服务端关系：桌面设计器与打印引擎同套装；自动化打印由 Integration Builder 以 Windows 服务监听触发；Enterprise 版 Librarian 集中文档库＋版本控制；BarTender Cloud 另提供 Web 设计器（Cloud Designer）（来源：同上帮助文档、https://www.bartendersoftware.com/product/cloud/ ）。
- 发布形态：Starter / Professional / Automation / Enterprise 四级；按打印机数或工作站授权；订阅与永久许可双轨（来源：https://www.bartendersoftware.com/product/pricing/ ）。

**Loftware NiceLabel**（与本仓四件套最直接可比）

- 模块划分：**Desktop Designer（桌面设计器）+ Control Center（Web 管理平台，内嵌 Web Designer 与 Web Printing）+ Automation（独立打印自动化服务，由 Control Center 的 Automation Manager 集中管理）**（来源：https://help.nicelabel.com/4402860489713-About-NiceLabel-Control-Center ）。
- 插件机制：自有打印机家族驱动体系（Release Notes 持续更新）＋文件 / TCP / SAP 等触发集成＋.NET API；未查到公开第三方插件 SDK（来源：https://help.nicelabel.com/ 的 Integrationen 与 .NET API 章节）。
- 设计器与服务端关系：**模板存于服务端文档库（Document Storage，支持 WebDAV、版本历史与审批工作流）**；on-premise 部署形态为 IIS + SQL Server（来源：https://help.nicelabel.com/ Document Storage and Versioning / Administration 章节）。
- 发布形态：按打印机数授权（Loftware License Center 统一管理）；Loftware Cloud 为订阅制 SaaS（来源：https://help.nicelabel.com/ Printer licensing 章节）。

**Zebra ZebraDesigner 3**（打印机厂商「配件」逻辑的下限形态）

- 模块划分：纯单体桌面软件，无服务端、无管理平台；Essentials（免费）/ Professional / Developers 三档（来源：https://support.zebra.com 官方 KB「Differences between ZebraDesigner Essentials 3 and Professional 3」、官方 User Guide）。
- 插件机制：Pro 版扩展＝数据库连接 / RFID / VB 脚本；打印走 ZDesigner 驱动；Web 场景由独立产品 **Browser Print**（JS API → 本地服务 → USB / 网络打印机，附带源码可扩展）承接，属 Print DNA 套件（来源：https://www.zebra.com/software/printer-software 、Browser Print User Guide）。
- 设计器与服务端关系：模板本地文件、设计打印同进程，无任何服务端概念（官方 User Guide 全文佐证）。
- 发布形态：Essentials 免费 / Professional 单 PC 永久许可（第三方渠道 $219–295，标注：第三方）；无云无订阅。

**TEKLYNX CODESOFT**（拆分最彻底的样本）

- 模块划分：设计器（CODESOFT）与自动化（**SENTINEL**）、文档管理（**LABEL ARCHIVE**）各自独立成产品，组合为企业套装 TEKLYNX CENTRAL（来源：https://www.teklynx.com/products/licensing-options ）。
- 插件机制：自研驱动覆盖 4,000+ 型号＋VBScript＋TEKLYNX SDK；SENTINEL 提供 RESTful Web API 打印通路（来源：https://www.teklynx.com 媒体库与产品页）。
- 设计器与服务端关系：桌面设计器＋模板本地文件；服务端能力（集中存储 / 角色权限 / 审批 / 审计）由 LABEL ARCHIVE 承担；无 Web 设计器（未查到公开资料）。
- 发布形态：Pro（限 3 台打印机）/ Enterprise 两档；永久 / 订阅 / 网络许可三轨（来源：https://www.teklynx.com/codesoft ）。

**消费级档**（1~2 条事实）：DYMO Connect 与 Brother P-touch Editor 均为免费本地桌面软件＋本地模板＋直连打印，无服务端无扩展面（来源：https://www.dymo.com/support 、https://support.brother.com ）。

**跨产品共性**（对本仓定位的支撑）：

1. 「桌面设计器＋同进程打印引擎」是行业基线；企业级差异全部落在**服务端伴随模块**（文档库 / 版本审批 / 自动化触发 / Web 打印门户）——与本仓「Server＋模板库＋作业中心＋下载中心」的组合方向一致。
2. Web 设计器是近五年新增能力且**一律依托服务端文档库**（NiceLabel Web Designer、BarTender Cloud Designer）——本仓「Web 设计器＋服务端模板库」同构。
3. Web 打印通路只有两种拓扑：服务端代理打印（Print Portal / Web Printing）与**客户端本地中间件**（Zebra Browser Print）——本仓 PC / PDA 宿主属后者，且以插件化传输超出其单厂商指令集限制。
4. **未查到任何厂商开放第三方指令集插件的公开 API**——本仓 `.lfplugin` 传输插件体系在该维度超出商业惯例，属差异化设计而非欠账。
5. 商业模式主流＝版本分级 × 按打印机数授权 × 订阅推云——与本仓开源免费形态是两种极端，不构成对标压力。

### 3.2 开源同类

| 项目 | 形态 | 与本仓可比点（来源） |
|---|---|---|
| glabels（GNOME） | 3.x（C/GTK，官方停止支持、仓库归档）与 4.x（C++/Qt6 重写，仍在开发无正式 release）；`glabels` / `glabels-batch`（无头批打印）/ `glbarcode` 三分 | 「设计器＋无头批处理」双部件分离的先例；模板 XML 与代码分离分发（https://glabels.org/ 、https://github.com/j-evins/glabels-qt ） |
| QZ Tray | Java 本机常驻打印桥（WebSocket `wss://localhost:8181`）＋JS 库；raw（ZPL/EPL/ESC 等）与像素双通道 | 「本机宿主桥接 Web→打印机」拓扑的成熟验证；差异点＝静默打印强制数字签名＋白名单的安全投入（https://qz.io/docs/ 、https://github.com/qzind/tray ） |
| Labelary | 闭源在线 ZPL 渲染 API（`POST /v1/print/{dpmm}/{w}x{h}/`） | 「渲染即服务」形态；本仓调试出图同能力但本地化（https://labelary.com/service ） |
| shashi089/qr-code-label-designer | TS 库：`qrlayout-core`（无头渲染）+ `qrlayout-ui`（可视化设计器），JSON 布局单一事实源＋`{{变量}}`绑定，导出 ZPL/PDF/PNG | 与本仓「前端设计器＋后端渲染」同思想的最小实现（https://github.com/shashi089/qr-code-label-designer ） |
| AmigoUK/BarcodeLabelGen | 自托管全栈：React+Konva 前端 / Flask 后端 / 独立 Go 打印代理（RAW TCP 9100） | 开源界最接近「设计器＋服务端＋打印代理」三件套的样本；无插件体系（https://github.com/AmigoUK/BarcodeLabelGen ） |

结论：开源同类普遍**无第三方插件面**（能力内置或编译期可选依赖）；本仓插件体系更接近商业扩展位设计，在开源同类中属少数派——是主动选择而非落后。

### 3.3 前端画布设计器通用形态

- **Polotno**（React+Konva 商业 SDK，与本仓同栈）：`createStore()` 为唯一事实源（pages / elements / selection / scale 全在 store），Workspace / SidePanel / Toolbar / ZoomButtons 是并列组件靠 store 通信；撤销重做为 `history.transaction(fn)`（多变更并一个历史步）/ `history.ignore(fn)` / `undo()/redo()`；导出直接从 store 渲染不经 DOM（来源：https://polotno.com/docs/store 、https://polotno.com/docs ）。
- **tldraw**：`@tldraw/store` signals 响应式记录库（shape/page/binding/asset 记录带类型前缀 id，document / session / presence 三种 scope）；变更走 `editor.run(...)` 批量＝一个 undo 步；快照＋迁移（migrations）支撑文档演进；扩展面＝ShapeUtil 类（一种形状一个类注册全部行为）（来源：https://tldraw.dev/sdk-features/store 、https://tldraw.dev/docs/shapes ）。
- **Fabric.js 生态**：扁平对象模型＋`toJSON()/loadFromJSON()` 持久化；撤销重做通行快照式（社区插件 fabric-history）或命令式（Redux past/present/future）（来源：https://github.com/fabricjs/fabric.js/issues/10011 、https://www.npmjs.com/package/fabric-history ）。
- **Konva 官方口径**：Konva 是纯渲染引擎，撤销重做属应用状态管理问题——保存状态、恢复旧状态、重渲染；**禁止把 Konva 节点实例存进历史**；新编辑截断 redo 分支（来源：https://konvajs.org/docs/posts/canvas-undo-redo 、https://konvajs.org/docs/react/Undo-Redo ）。
- **通用结论（对本仓）**：①「自研 Konva 画布＋独立文档模型」是通行形态而非异类；②撤销通行「数据快照 / diff＋事务边界」，本仓 `lib/design/history.ts`（模型快照＋`commitNow` 事务）与 Polotno / tldraw 等价；③大文件通行拆法＝「store / 画布 / 面板 / 工具」分层——本仓已有该分层雏形（§2.4），差距集中在编排层单文件体量。

### 3.4 .NET 服务端与插件成熟形态

- **插件契约独立面＝官方建议**：微软官方插件教程明文建议「契约类型独立成库，可单独发布而不必随整个应用分发」；插件引用契约须 `ExcludeAssets=runtime`（否则契约被复制进插件目录、被插件 ALC 加载成「另一个类型」导致宿主找不到命令）；每插件独立 ALC＋`AssemblyDependencyResolver` 是标准加载形态；「可回收 ALC 卸载」受官方支持但属进阶（协作式卸载、ReadyToRun 失效、引用泄漏排查等工程代价）；ALC 不是安全边界（来源：https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support 、https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability ）。本仓 Zebra 插件仅依赖 Core＝与该建议同构（差异仅「契约与内核同程序集」，见 §5 W-1）。
- **分层参照**：微软电子书口径——Application Core 零外向依赖、Infrastructure / UI 依赖向内，是组织 non-trivial 单体的最合适方式（来源：https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures ）；本仓 Core 居中零依赖与之一致。官方参考应用 dotnet/eShop 当前为「services-based + Aspire」多服务形态（非模块化单体；其 `src/Shared` 仅工具类，跨服务契约走提供方 proto＋集成事件）——与本仓可比的是 Orchard Core（模块化单体、模块即扩展单元，https://github.com/OrchardCMS/OrchardCore ）与 kgrzybek/modular-monolith-with-ddd（`Modules + BuildingBlocks`，14k stars）一派，但两者解决的是远大于本仓的规模问题。
- **BackgroundService 队列模式**：官方文档三大示例模式之一即「Queued background tasks run sequentially」（Channel 队列＋HostedService 循环执行，`202 Accepted` 立返）；eShop 的 OrderProcessor / PaymentProcessor 亦为 Worker 服务消费队列（来源：https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services ）。官方示例为进程内 Channel 不持久化——本仓 SQLite 持久化队列＋重启不丢作业属自行补强的合理超出（无官方背书亦无反对先例）。
- **Minimal API vs Controller**：官方对新建项目推荐 Minimal API（来源：https://learn.microsoft.com/en-us/aspnet/core/fundamentals/apis ）；本仓 Api 层以端点类组织的原因是 **Server 与 WinHost 两宿主共享同一端点实现**（Minimal API 的 lambda 形态不利于跨宿主共享复用），属合理偏离而非欠账。
- **多宿主共享业务内核**：「一个共享库＋N 宿主」有官方级范例（eShop 的 WebAppComponents 被 Web 与 MAUI Hybrid 复用；Orchard Core 框架被多 Web 宿主引用），但共享物均为 UI 组件 / 框架能力；**「同一业务内核 DLL 被 Windows 桌面、Android、服务器三端复用」的完全同构开源样本未查到**——本仓属该形态的先行实践，风险由 900+ 测试与三端 CI 门禁对冲。

## 4. 差距清单（AC-03，三级分类）

### 4.1 契约级债务审计（「1.0 前必须」档）——结论：**无**

> 立场纪律（Issue 约束）：本档仅限「模板包格式 / 打印 API / 作业模型 / 插件 ABI」四面的契约级债务。

| 契约面 | 审计事实 | 结论 |
|---|---|---|
| 模板包格式（zip：manifest＋contract＋layout＋`images/`，决策 #23） | manifest 无格式版本字段；但 133 迭代演进全为可选字段加法（testData #41、displayName 复用 #144、图片三态 #184），读取端对未知 JSON 键天然忽略；真正被 semver 禁止的破坏性变更（删字段 / 改语义）本就不允许 | **无债务**；可选 `formatVersion` 字段留作 1.0 后保险（纯加法，任何时点可补，§5 R-4） |
| 打印 API（提交 / 查询 / 回调 / ErrorView） | 演进全为加法：`callbackUrl`（#154，至少一次＋死信语义成文）、ErrorView `params`（#166，缺省序列化与旧形态逐字节等价）、错误码注册表＋展示端翻译；`zpl` 命名未进任何对外契约面（实测 `Api/Contracts.cs` 与 Server→宿主投递载荷 `JobPayload` 均不含）；无鉴权属决策 #79 局域网信任模型（升级触发条件已在案） | **无债务** |
| 作业模型（Job / Item / requestId 幂等 / Claim 超时 / Expired） | 状态机与超时语义成文稳定（#16 / #98 / #37）；`LabelJobItem.Zpl` 命名泛化为「编译产物」但为 Core 内部持久化字段，非契约面 | **无债务**（内部命名债见 §5 R-5） |
| 插件 ABI（`.lfplugin` manifest＋Core 接口） | manifest 必填 pluginId / name / version，可选 description / author / **minHostVersion（已预留、仅展示未启用校验）** / platforms（#157）；版本比较语义（新版覆盖 / 同版幂等 / 降级拒绝）双端一致；PDA 保守 API 面为编写纪律（#159）且有门槛用例锚定、方案 B 升级路径已登记 §7 | **无债务**；minHostVersion 校验启用＝已设计好的逃生门（1.0 后按需） |

**1.0 前必须处理项：无。** 可放心承诺。

### 4.2 「1.0 后建议」档（内部重构，不破坏 semver）

| # | 条目 | 依据 |
|---|---|---|
| R-1 | `Designer.tsx` 编排层拆分（页面壳 / 状态编排 / 面板接线分离，机制已下沉 `lib/design/`） | §2.4：1,213 行、v0.34 周期 +49%、连续三个迭代的改动落点；业界通行「store / 画布 / 面板」分层（§3.3） |
| R-2 | AndroidHost `MainActivity.cs` 按页面拆分（配置 / 状态 / 打印 / 插件管理） | §2.6：1,601 行 / 97 方法全仓最大；UI 层无组件级单测（真机走查为验证通道）放大维护风险 |
| R-3 | `ServerDb.cs` 按聚合拆分（jobs / devices / callbacks / logs 各自数据访问类） | §2.6：736 行五类聚合共置；Server.Tests 148 项可作安全网 |
| R-4 | 模板包 manifest 增可选 `formatVersion` 字段 | §4.1：廉价保险、纯加法；「若未来模板格式需要不兼容演进」时降低歧义 |
| R-5 | `LabelJobItem.Zpl` 内部命名中性化（如 `CompiledPayload`） | §4.1：语义自决策 #77 起已泛化；内部字段 / 列名，涉及 SQLite 列迁移，收益＝可读性 |
| R-6 | AndroidHost DTO 副本治理保持现状但增设漂移断言（若未来字段增多） | §2.2：约 95 行已文档化重复；当前量级不值得统一，超 200 行再评估 |

### 4.3 「不建议做」档（收益不抵风险）

见 §5 W 表（契约包拆分 / 画布换 SDK / API 版本前缀 / 模块化改造 / VSA / Minimal API 迁移）。

## 5. 重构建议（AC-04）

### 5.1 建议项（逐项成本 / 收益 / 风险 / 时点）

| # | 条目 | 成本 | 收益 | 风险 | 建议时点 |
|---|---|---|---|---|---|
| R-1 | Designer.tsx 拆分：页面壳留在 Designer.tsx，深链 / 演示注入 / 草稿编排等效应拆为 hooks 或子模块（机制层不动） | 中（约 1~2 迭代内搭车完成；页面级测试 685 行可护航） | 高（最高频改动文件的可维护性；后续控件迭代的改动面收敛） | 低（纯模块搬移＋既有测试；jsdom 无法覆盖的画布交互已有 repro 脚本先例 #183） | **1.0 后、下一个触及设计器的功能迭代搭车**，不专项立项 |
| R-2 | MainActivity.cs 按页面拆分（partial class 或每页面一类；PDA 无 XML 布局的动态 UI 形态不变） | 中高（拆分本身机械，但 PDA 验证依赖真机走查） | 中（PDA 侧可维护性；后续 PDA 功能迭代改动面收敛） | 中（UI 无单测，回归靠真机；建议小步拆、每步走查） | 1.0 后、下一个 PDA 功能迭代搭车 |
| R-3 | ServerDb.cs 按聚合拆分 | 低（148 项集成测试安全网） | 中（数据访问边界清晰） | 低 | 1.0 后机会式（触碰该文件的迭代顺带） |
| R-4 | 模板包 formatVersion 可选字段 | 极低（单字段＋读写默认） | 低（未来不兼容演进的判别依据） | 极低（纯加法，旧端忽略未知键） | 任意时点（可随 1.0 发版记账顺带，亦可 1.0 后） |
| R-5 | Zpl 命名中性化 | 中（SQLite 列迁移＋两端宿主同步） | 低（可读性） | 中（数据迁移类改动） | 不主动排期；下次触碰该持久化面的迭代再议 |
| R-6 | AndroidHost DTO 副本漂移断言 | 低 | 低（防双份漂移） | 低 | 触发式（副本超约 200 行或字段第三次增改时） |

### 5.2 不建议项

| # | 条目 | 不做的理由（依据） |
|---|---|---|
| W-1 | Core 拆出独立契约包（Core.Abstractions，微软教程字面形态） | 当前仅 2 个官方插件、Core 全量 4.5k 行；拆分收益在「第三方插件生态扩大后契约独立发版」，现在做只增打包复杂度。**semver 后逃生门＝TypeForwardedTo 类型转发**（.NET 标准机制），届时可无破坏迁移。等价官方一致性与该后路均有来源支撑（§3.4） |
| W-2 | 画布换第三方 SDK（Polotno / tldraw） | Polotno 商业授权（报价制）、tldraw 生产需 license key；自研 Konva 画布已是业界通行形态且能力集对齐（§3.3）；迁移成本高、许可与 9 依赖的克制面冲突 |
| W-3 | API 加 `/v1/` 版本前缀 | semver 承诺＋「仅加法演进」纪律已足够；业界（Labelary `/v1/`、BarTender REST）的前缀服务于云端多版本并存，本仓自托管单实例无此需求；加前缀反而使存量调用方断链 |
| W-4 | 模块化单体 / 微服务化（Modules 划分） | 解决的是远大于 28k 行规模的复杂度（Orchard Core 上百模块、eShop 多服务）；本仓分层清晰无环（§2.2），改造收益为负 |
| W-5 | Vertical Slice Architecture 重构 | 该派前提是团队重构成熟度与请求级隔离诉求（Bogard 原文口径，§3.4）；本仓按「契约 / 渲染 / 宿主」的领域切分已成立，双派在此规模均通行、无需改宗 |
| W-6 | Api 层迁移 Minimal API | 官方推荐 Minimal API 针对新建单宿主服务；本仓 Api 的价值恰在 **Server / WinHost 跨宿主共享端点实现**（§2.2），lambda 形态不利共享——偏离有因 |

## 6. 1.0 决策输入

- **semver 承诺：无阻断项。** 四契约面零债务（§4.1）；内部结构均可在 1.0 后不破坏契约地演进（§5.1 全部为内部重构，R-4 虽涉模板包格式但为可选加法）。
- **1.0 前无必做重构**——「评估先行、重构决策后置」的结论是：候选点全部满足「等一等更便宜」（搭车优于专项）；唯一接近 1.0 时机的是 R-4（可选、极廉价），但非必须。
- **结构性优势应对外承诺**：三端同构宿主＋插件 ABI 仅依赖 Core＋预览打印同源渲染——三项在开源同类中属少数派形态（§3.2 / §3.4），是 1.0 文档可强调的架构资产。
- **观察项（非本评审范围但与 1.0 相邻）**：无鉴权信任模型的升级触发条件（决策 #79 / #154 已登记：跨网段 / 公网暴露前须补回调 HMAC 与 API 鉴权）；PDA 插件保守 API 面（决策 #159 方案 B 未决）——两者均已有登记与触发条件，无需本评审重复立项。

## 附录 A：度量口径

- 行数＝`git ls-files` 跟踪文件的 `wc -l`（含注释与空行）；基线 `951abe2`；构建产物（bin/obj/dist/node_modules）与 `LabelFrame-evidence/`（未跟踪证据目录）不计。
- 测试计数：.NET＝`[Fact]`＋`[Theory]` 特性 grep（Theory 为参数化、实际用例数更多）；前端＝`it(`/`test(` 行数 grep（CI 按双模式各跑一遍，报告数与 CI 输出可能略有出入）。
- 增长趋势＝对 `v0.22.0 / v0.28.0 / v0.32.0 / v0.34.0` tag 的 `git show <tag>:<file> | wc -l` 采样。
- 大文件阈值参考：业界无权威「单文件多少行算大」的量化标准（§3.3 调研明确未查到），本报告以仓库内相对分布（TopN）与增长速率作判定，不引入绝对阈值。
