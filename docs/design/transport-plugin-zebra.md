# LabelFrame.TransportPlugin.Zebra 模块设计

> 覆盖：`src/LabelFrame.TransportPlugin.Zebra/`（8 文件，约 850 行）＋ 测试夹具 `test/LabelFrame.TransportPlugin.Fake/`、`test/LabelFrame.TransportPlugin.Sample/`（4 文件，约 200 行）· 基线：`master` `7486dae`（2026-10-19）
> 分工与模板说明见 [README.md](README.md)；决策原文见 [DESIGN.md](../DESIGN.md) 决策表。

## 1. 职责与边界

**做什么**：Zebra 品牌传输的**官方外置插件**（决策 #123）——以 `.lfplugin` 包（决策 #72 格式 / #157 跨端）随发版分发（`scripts/build-zebra-plugin.ps1`），WinHost 宿主启动时字节加载装配（宿主**不引用本工程**，彻底外置）。一个插件对象承载两层能力：

- **传输工厂**：按连接参数创建 TCP / USB（SDK 自动发现）/ Windows 驱动三种形态的传输实例（Zebra 官方 Link-OS SDK 5.0.3685，双端版本统一，决策 #111），提供发送 ZPL、连接测试（`~HS` 探测）、打印机状态查询（SDK `GetCurrentStatus`）；
- **可选能力 `ILabelCommandCompiler`**（决策 #137，契约细节见 DESIGN §5.4）：把 `LabelDocument` 编译为**整页自包含 ZPL 原生指令**，配合连接级打印方式参数 `printMode`（`image` / `native`）构成命令打印第二条路径。

**不做什么**：

- 插件包安装 / 加载 / 注册 / 版本比较——Core 的 `PluginInstaller` / `PluginDirectoryLoader` / `TransportPluginRegistry`（加载生命周期口径见决策 #127）；
- 宿主侧编译时序与持久化——WinHost `JobSubmissionService`：提交时逐张编译、产物写入 `LabelJobItem.Zpl`、任一失败整体拒绝（§5.4.3）；
- 旧内置时代 id `"zebra"` 的别名归一——Core `TransportPluginIdPolicy.NormalizeAlias`（读取存量配置的内存态映射，插件侧恒用官方 id `labelframe-transport-zebra`）；
- Android 端 Zebra 传输——AndroidHost 内置 `ZebraSdkTransport`（SDK 档维持内置，决策 #157 双档模型），不加载本插件；
- 图片渲染 / `^GF` 位图编码——图片模式整链在宿主（Rendering + Core `ZplImageEncoder`），插件只在 `native` 模式介入。

测试夹具两枚（同为插件 ABI 消费者）：**Fake**——PDA 轻量档外置插件通道验证（manifest `platforms:["android"]`，决策 #157；保守 API 面约束，决策 #159）；**Sample**——外部 DLL 目录加载机制的最小示例插件（Windows 端测试用）。

## 2. 内部结构

| 类型 | 文件 | 角色 |
|---|---|---|
| `ZebraTransportPlugin` | ZebraTransportPlugin.cs | 插件入口：`ITransportPlugin` + `ILabelCommandCompiler` 双实现；参数模型（kind / host / port / printerName / usbName / printMode）与连接展示文本 |
| `ZebraPrinterTransport` | ZebraPrinterTransport.cs | 传输实例：`IPrintTransport` + `IPrinterStatusProvider` + `ITestableTransport` 三接口；三形态连接、逐次建连、异常转中文 `InvalidOperationException` |
| `ZebraTransportKind` | ZebraTransportKind.cs | 连接类型枚举（Tcp / Usb / Driver）；字符串参数跨插件边界后于此解析 |
| `ZebraLabelCompiler` | ZebraLabelCompiler.cs | `ILabelCommandCompiler` 实现：`LabelDocument` → 整页自包含 ZPL 文本；纯函数性质（同输入同输出、不访问打印机） |
| `ZebraTextWidthMeasurer` | ZebraTextWidthMeasurer.cs | `ITextWidthMeasurer`（Core 接口）的 Zebra 内置字体实现——驱动 `LabelLayoutResolver` 的区域锚定换算 |
| `ZebraBuiltInFontMetrics` | ZebraBuiltInFontMetrics.cs | 内置字体度量知识（internal）：位图字体 A–D 官方设计点阵 + 缩放字体 0 的 Helvetica 同度量比例字宽表 |
| `ZebraQrCodeMetrics` | ZebraQrCodeMetrics.cs | QR 符号度量知识（internal）：ISO/IEC 18004 字节模式容量表 + 版本→模块数换算 |
| `FakeTransportPlugin` / `FakePrintTransport` | test/…/FakeTransportPlugin.cs | 假想品牌通道验证：发送落盘 + 宿主日志、测试恒成功、状态恒在线；保守 API 面的门槛用例 |
| `SampleTransportPlugin` / `SamplePrintTransport` | test/…/SampleTransportPlugin.cs | 示例插件：五种参数类型全覆盖的参数模型 + 日志记录传输 |

**关键机制**：

- **能力挂在插件对象而非传输实例**：`ILabelCommandCompiler` 实现于 `ZebraTransportPlugin`（品牌级能力，与连接形态无关——同一品牌 TCP / USB / 驱动共用一份编译器）；宿主装配期以 `plugin is ILabelCommandCompiler` 探测（§5.4.1），无状态编译器以组合持有（`_compiler` 字段）。
- **参数条件显隐**：`host` / `port` 仅 `kind=Tcp`、`printerName` 仅 `kind=Driver`、`usbName` 仅 `kind=Usb` 可见（`VisibleWhenKey` / `VisibleWhenValues`，前端动态表单消费）；`printMode` 为连接级 Select 参数（默认 `image`，§5.4.2 决议 2）。
- **逐次建连**：传输实例不持有连接——每次发送 / 测试 / 状态查询各自「建连 → 操作 → 关闭」，关闭失败吞异常不影响结果（与内置时代行为等价）。

## 3. 关键流程

### 3.1 宿主装配与传输创建

`.lfplugin` 安装（`PluginInstaller` 三层校验 + 官方插件版本比较）→ 宿主启动扫描 `plugins/` 目录，单 collectible ALC 字节加载包内全部 DLL（决策 #127；本工程及其 SDK 伴生依赖约 29 个 DLL 整体进包）→ 跨程序集发现 `ITransportPlugin` → 注册表按 `Id` 注册（ALC 根随插件实例强持有）→ 用户配置连接后 `Create(parameters, context)` 按字符串参数解析出 `ZebraTransportKind`（解析失败回退 Tcp）构造 `ZebraPrinterTransport`（TCP 缺地址 / 驱动缺打印机名即构造期抛 `ArgumentException`，消息带 `LABELFRAME_TCP_HOST` / `LABELFRAME_PRINTER` 环境变量提示）。

### 3.2 发送 / 连接测试 / 状态查询（`ZebraPrinterTransport`）

三操作共用 `CreateConnection` 三形态分派：`TcpConnection(address, port)` / USB（`usbName` 空则 `UsbDiscoverer.GetZebraUsbPrinters()` 自动发现第一台，无设备给可行动中文错误）/ `DriverPrinterConnection(printerName)`。

- **发送**（`SendAsync` → `SendCore`）：UTF-8 编码写指令；`ConnectionException` →「连接失败」、其余异常 →「发送失败」，均带 `DescribeTarget()`（如 `192.168.1.50:9100` / `USB:xxx` / `驱动:xxx`）包装为中文 `InvalidOperationException`；
- **连接测试**（`TestConnectionAsync`）：`~HS` 主机状态经 SDK `SendAndWaitForResponse`（2 秒超时）——收到非空响应才算就绪（能连端口 ≠ 打印机活着）；`TestAsync`（`ITestableTransport`）失败消息含打印目标与排查提示；
- **状态查询**（`GetStatusAsync`）：驱动（winspool）路径设计上无法读回状态，返回默认在线 + 说明文本；其余路径走 `ZebraPrinterFactory.GetInstance(connection).GetCurrentStatus()` 官方布尔属性（`isPaperOut` / `isPaused`），Message 最小口径只报缺纸 / 暂停；`ConnectionException` 返回离线 + 原因。

### 3.3 原生指令编译主链（`ZebraLabelCompiler.CompileAsync`）

输入原始 `LabelDocument`（宿主不做预解析——品牌字体度量只有插件知道，§5.4.1）；DPI 非正即失败。输出**整页自包含** ZPL：`^XA` + `^PW{宽}` + `^LL{高}` + `^CI28`（UTF-8 编码声明，覆盖 Latin-1）→ 逐元素分发 → `^XZ`，宿主不再包装追加。元素分发表：

| 元素 | 处置 |
|---|---|
| 区域（`LabelRegionElement`） | 跳过——版式容器仅参与锚定解析，自身不出指令（边框为图片模式能力，原生裁剪） |
| 文本 | 编译（§3.4）；字段缺数据 / 字符不可覆盖以失败结果返回 |
| Code 128 条码 | 编译（§3.5） |
| 二维码 | 编译（§3.5） |
| 图片（`SourceKey` 空） | 静默跳过——「未指定 = 不存在于打印面」，与两渲染器同口径 |
| 图片（有 key）/ 线元素 / 未知类型 | **显式失败**（`LF_ENC_002`，消息含「可将打印方式切回图片」）——不回退图片、不做元素级混合（§5.4.4） |

### 3.4 文本编译与对齐尽力近似（`CompileText`）

1. 取值 `LabelElementContent.Get`（缺字段数据＝失败结果，防御口径）；空值产出空指令；
2. **字符覆盖检查**：逐字符校验内置字体可表示集（可打印 ASCII + Latin-1 补充），未覆盖字符（中文等，无中文字库前提）显式拒绝 `LF_ENC_002`——不输出乱码；
3. 字体：模板保留的 ZPL 内置字体字符（A–Z / 0–9）原样，其余回退默认缩放字体 `0`；加粗近似 = 粗体字体变体映射（`BoldFontVariantMap`：`0`→`1`，按机型可调）；
4. **起始坐标换算**：与图片渲染共用 Core `LabelLayoutResolver`（区域锚定偏移按本品牌内置字体度量 `ZebraTextWidthMeasurer` 计算，决策 #110 的编译侧兑现）+ padding 内缩；块内对齐尽力近似——显式块宽按实测文本宽度偏移（TextAlign 三档系数 0 / 0.5 / 1），显式块高按（块高 − 字高）偏移（VerticalAlign 同构）；
5. 产出 `^FO` + `^A{字体}N,{字高点}{,{字宽点}}` + `^FH^FD{转义值}^FS`；字宽缺省 = 字体标准纵横比；超长文本**不换行不缩放不截断**直接输出（排版降级为预期常态，§5.4.1 裁剪原则）。

`^FH` 十六进制转义（`EscapeFieldData`）：`^` / `~` / `_` 三类指令控制字符转 `_5E` / `_7E` / `_5F`，**必须先转义 `_`**（后转会二次替换破坏已引入的前缀）；UTF-8 多字节序列不含 ASCII 字节，与转义不冲突。

### 3.5 条码与二维码编译（`CompileBarcode` / `CompileQrCode`）

- **Code 128**（`^BY{模块宽},3` + `^BCN,{高度},{Y|N},N,N`）：空白值跳过（与图片渲染同口径）；字符集检查——子集 B 仅可打印 ASCII，中文等按 `LF_ENC_001` 编码拒绝（与图片模式渲染层拒绝语义一致）；模块宽 1..10 直接透传（模板字段即 ZPL 模块宽度量纲，非毫米）；`displayValue` → interpretation line 开关。字段转义在 `^FH` 基础上追加 `>` → `>0`（Code 128 子集 B 的子集切换调用前缀——十六进制转义还原后的 `>` 仍会被数据层当作前缀，故不能用 `_3E`）；
- **二维码**（`^BQN,2,{放大倍数},{ECC}` + `^FD{ECC}A,{数据}`）：`^BQ` 无尺寸参数，须先把模板边长（毫米）换算为放大倍数——按 UTF-8 字节数 + ECC 查容量表（`ZebraQrCodeMetrics`，ISO/IEC 18004）估算最小可容版本 → 矩阵模块数（17 + 4v）→ 放大倍数 = 边长点数 ÷（矩阵 + 2×静区），钳位 **1..10**（官方现行上限 100、旧固件上限 10，保守兼容）；`qrMargin` = 静区语义，矩阵按 `^FO` 内缩居中使物理尺寸近似模板边长；ECC 档 L/M/Q/H 在 `^BQ` 与 `^FD` 开关两处一致输出；中文值经整页 `^CI28` 支持。

### 3.6 Fake / Sample 夹具链路

Fake 经 `scripts/build-fake-android-plugin.ps1` 打包（`platforms:["android"]`，单包单端）推入 PDA → AndroidHost 装配 → 品牌路由测试打印：发送内容一行「时间 + 字节数 + 内容」落盘 `fake-transport-sent.txt`（插件数据目录，走查经 adb 取证）+ 宿主日志；落盘失败仅记日志不阻断。Sample 由 Core.Tests / WinHost.Tests 直接以工程引用（同进程装配）驱动外部 DLL 目录加载与插件安装器测试。

## 4. 对外契约与依赖

**接口面**（命名空间 `LabelFrame.TransportPlugin.Zebra`，全部 public）：

- `ZebraTransportPlugin`（`ITransportPlugin` + `ILabelCommandCompiler`）、`ZebraPrinterTransport`（三接口）、`ZebraTransportKind`、`ZebraLabelCompiler`、`ZebraTextWidthMeasurer`；
- internal（无 `InternalsVisibleTo`，测试不直查，经 `CompileAsync` 输出间接覆盖）：`ZebraBuiltInFontMetrics` / `ZebraQrCodeMetrics` / `BoldFontVariantMap` / 转义器三方法；
- 插件 id 恒取 Core `TransportPluginIdPolicy.ZebraPluginId`（`labelframe-transport-zebra`）——manifest.pluginId / DLL `Id` / 连接配置 pluginId 三处一致（§6.8）。

**依赖**：`LabelFrame.Core` 是**唯一工程依赖 = 插件 ABI 边界**（接口 / 文档模型 / 布局解析 / 参数模型 / 错误码全部来自 Core）。NuGet：`Zebra.Printer.SDK 5.0.3685`（版本与 AndroidHost 内置统一，决策 #111）＋ 毒丸引用清单（MAUI / WinUI / WindowsAppSDK 链 17 包逐包 `ExcludeAssets="all"`，见 §6）。目标框架 `net10.0-windows10.0.26100`（与 WinHost windows 腿一致——插件 DLL 由宿主运行时字节加载）。Fake / Sample 为 `net10.0` 纯托管（轻量档，决策 #157），同样只依赖 Core。

**被依赖**（全仓 grep 核实；宿主运行时**零引用**——经 `.lfplugin` 外置装配）：

| 消费方 | 用途 |
|---|---|
| WinHost.Tests | 唯一 ProjectReference 消费方：`ZebraLabelCompilerTests`（编译器 + 度量器）、`ZebraPrinterTransportTests` / `PrinterStatusTests`（传输与状态）、`TransportParameterVisibilityTests`（参数显隐）、`OfficialPluginMigrationTests`（旧 id / 旧配置自动迁移安装——类已随 WinHost 重命名，覆盖 zebra / tspl 双官方插件）、`ZebraNativeSubmissionTests`（native 模式提交链）、`PluginLoadLifecycleTests` + `OfficialPackageFactory`（从插件 bin 目录取真实 DLL 组生产同构包）、`WinHostEndpointsTests`（Sample 外部 DLL 目录加载集成） |
| Core.Tests | 引用 Fake + Sample：插件包读写 / 目录加载 / 安装器跨平台 / 外部插件注册表 / SafeFileName 测试 |
| Server.Tests | `PluginPackagesServiceTests`（Sample.dll 名构造 zip，服务端分发校验） |
| Bootstrapper.Tests | Payload 落位 / 清理测试以 `labelframe-transport-zebra` manifest 字符串构造插件包 |
| scripts + 发版流水线 | `build-zebra-plugin.ps1` / `build-fake-android-plugin.ps1`（.lfplugin 构建，64MB 上限断言）；`release.yml` 构建插件 zip、Client MSI 附带断言、Release 附件；`test-pda-plugin-spike.ps1` / `test-bundle-uninstall-walkthrough.ps1`（走查脚本） |

## 5. 设计取舍（决策索引）

| 主题 | 决策 |
|---|---|
| Zebra 官方 SDK 5.0.3685 双端复用（AndroidHost 接入 + WinHost 统一；毒丸清单 / USB 自动发现 / SDK 状态语义） | #111 |
| 图片打印收敛（删除矢量 ZPL——原生编译此后归属品牌插件而非宿主） | #50 |
| 插件包格式 `.lfplugin`（zip + manifest）与分发闭环 | #72 |
| 品牌传输插件外置化与官方插件体系（id 前缀 / 版本随主版本 / 彻底外置 / 旧 id 别名） | #123（策略细节 §6.8） |
| 外置插件加载 ALC 生命周期（单 ALC 整体加载 / 预载伴生依赖 / 注册表持根） | #127（宿主侧机制，决定插件包形态） |
| 区域锚定按实测文本宽度、度量接口注入（编译侧换算的机制基础） | #110 |
| 文档编译能力接口 + 连接级打印方式参数（提交时编译持久化 / 显式失败不回退） | #137（契约细节 §5.4） |
| PDA 双档模型 + `.lfplugin` 跨端格式（轻量档纯托管动态加载） | #157 |
| 轻量档插件保守 API 面（禁 .NET 6+ BCL API） | #159 |
| 注释极简化（本篇承接源码设计知识） | #189 |

## 6. 平台坑与不变量

修改本模块前必读（源码中对应位置保留白名单注释）：

1. **只依赖 Core＝ABI 边界**：引用 WinHost / Api / Rendering 任一工程即破坏外置性——宿主字节加载时类型统一由宿主提供，Core 以外的宿主程序集不保证在场；SDK 及其全部传递依赖必须进包自解析（`build-zebra-plugin.ps1` 排除清单只剔宿主必带项）。
2. **毒丸引用清单是兼容义务**：Zebra SDK 5.x 传递的 MAUI / WinUI 链必须逐包 `ExcludeAssets="all"`（17 包，`ExcludeAssets` 不裁剪传递闭包）——漏排会混入 MAUI 类型与 WinForms / WebView2 投影冲突、或经 WindowsAppSDK buildTransitive 注入 win10-* RID 图（framework-dependent 工程报 NETSDK1083）。SDK 升级须复查清单（决策 #111 ⑦）。
3. **TFM 与宿主 windows 腿一致**（`net10.0-windows10.0.26100`）：插件由宿主运行时字节加载，TFM 越前会破坏装配。
4. **插件侧禁用旧 id `"zebra"`**：Id 恒为官方 id；旧 id 别名归一属 Core 读取侧（内存态、不落盘迁移）——插件侧引入旧 id 会与安装侧「内置保留 id / 官方 id」校验冲突。
5. **每次操作独立建连**：传输实例不持有 / 复用连接——发送、测试、状态各自建连→操作→关闭；改成长连复用会偏离与内置时代等价的行为基线（SDK 连接对象不可重开）。
6. **驱动路径状态恒「在线」是设计裁剪**：winspool 无状态读回通道（非缺陷）；TCP / USB 走 SDK `GetCurrentStatus`，Message 最小口径只报缺纸 / 暂停。
7. **`^FH` 转义顺序与条码 `>` 调用码**：先转义 `_` 再转义 `^` / `~`（二次替换坑）；Code 128 字面 `>` 必须 `>0` 调用码——`_3E` 十六进制还原后仍被数据层当子集切换前缀。
8. **QR 放大倍数上限 10**：官方现行上限 100、旧固件上限 10——保守取 10 兼容旧机型，超限按 10 输出（符号略小于模板框属预期）。
9. **native 编译显式失败，不回退不混合**：不支持元素 / 字符不可覆盖以失败结果返回（`LF_ENC_001` / `LF_ENC_002`），不自动回退图片、不做元素级混合（§5.4.4 决议 1）——回退会造成「以为在用指令、实际是图片」的效果混淆。图片模式是全功能基准，原生模式的功能差异（无换行 / 缩放 / 图片 / 线）是预期行为而非缺陷（§5.4.1 裁剪原则），不要往编译器加排版模拟。
10. **Fake 的保守 API 面约束**：禁用 .NET 6+ 新增 BCL API（`ReplaceLineEndings` / `ArgumentNullException.ThrowIfNull` 等）——Release AOT 宿主内 interpreter 对动态加载程序集的可解析面不含宿主 AOT 图未引用的新增 API，命中即方法体解析期 `MissingMethodException`（真机实证，决策 #159）；本插件即约束面的门槛用例，其打印链路跑通 = 约束面可用性下限成立。
