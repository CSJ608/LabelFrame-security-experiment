# LabelFrame.TransportPlugin.Tspl 模块设计

> 覆盖：`src/LabelFrame.TransportPlugin.Tspl/`（9 文件，约 1000 行）· 基线：`iter/143-tspl-plugin`（2026-10-16，迭代 143 / #378）
> 分工与模板说明见 [README.md](README.md)；决策原文见 [DESIGN.md](../DESIGN.md) 决策表（本迭代主决策 #195）。

## 1. 职责与边界

**做什么**：TSPL 指令集的**官方外置插件**（决策 #195：指令集维度单插件——服务 TSC 及全部 TSPL 兼容品牌，对打印机指令集的了解是指令集知识而非单品牌知识）。以 `.lfplugin` 纯托管包（KB 级，零第三方依赖）随发版分发（`scripts/build-tspl-plugin.ps1`），WinHost 宿主启动时字节加载装配（宿主不引用本工程）。一个插件对象承载三层能力：

- **传输工厂**：按连接参数创建 TCP 9100 / USB 免驱动直连（usbprint 类驱动 + CreateFile 直开绕打印队列，2026-10-16 TSC TT065-60 实测路线）两种形态的传输实例，提供发送（文本 / 二进制）、连接测试与打印机状态查询（`ESC ! ?` 单字节实时状态，带重试）；
- **可选能力 `ILabelCommandCompiler`**（决策 #137，契约见 DESIGN §5.4）：`LabelDocument` → 整页自包含 TSPL2 原生指令（TEXT / BARCODE / QRCODE / BAR / BOX——BAR / BOX 为 Zebra 原生模式不支持的差异能力），配合连接级 `printMode` 参数；
- **可选能力 `ILabelImageEncoder`**（决策 #195）：图片模式整版位图 → TSPL `BITMAP` 原始二进制指令流（非十六进制转码）。

**不做什么**：

- 插件包安装 / 加载 / 注册 / 版本比较——Core 的 `PluginInstaller` / `PluginDirectoryLoader` / `TransportPluginRegistry`（决策 #127）；
- 宿主侧编码分派时序与持久化——WinHost `JobSubmissionService`（按连接插件分派，未实现该接口的连接维持 `^GF` 逐字节不变）与 `LabelJobItem.CommandBytes`（BLOB，决策 #195）；
- 外部表单唤醒 / 变量灌装与 raw 指令透传——另立 Issue 挂账（#379）；
- PDA 侧 TSPL——待 Windows 真机效果验证后另议（DESIGN §5.4.7）；
- winspool 驱动通道——免驱动双通道已覆盖，驱动队列场景另议；
- 图片渲染——图片模式整链在宿主（Rendering + 位图分派），插件只做「位图 → BITMAP 字节流」编码。

## 2. 内部结构

| 类型 | 文件 | 角色 |
|---|---|---|
| `TsplTransportPlugin` | TsplTransportPlugin.cs | 插件入口：`ITransportPlugin` + `ILabelCommandCompiler` + `ILabelImageEncoder` 三实现；参数模型（kind / host / port / usbName / printMode / invertImage）与连接展示文本 |
| `TsplPrinterTransport` | TsplPrinterTransport.cs | 传输实例：`IPrintTransport` + `IPrinterStatusProvider` + `ITestableTransport`；双通道开链、`ESC ! ?` 查询带重试、状态位解析（public static `ParseStatusByte`＝单测锚点） |
| `TsplTransportKind` | TsplTransportKind.cs | 连接类型枚举（Tcp / Usb）；字符串参数跨插件边界后于此解析 |
| `ITsplLink` / `TcpTsplLink` / `UsbTsplLink` | TsplLink.cs | 通道链路抽象（一次打开的连接：写 / 读）：public 接口＝传输构造的注入缝（测试替身） |
| `UsbRawDevice` | UsbRawDevice.cs | USB 原始设备通道（internal）：CreateFile 同步 IO + 看门狗超时（重叠 IO 真机不收尾）；写超时抛中文错误 / 读超时返回 null |
| `UsbPrinterDeviceDiscovery` | UsbPrinterDevice.cs | USB 设备发现：注册表 `DeviceClasses\{GUID_DEVINTERFACE_USBPRINT}` 枚举；设备路径 = 接口键名前缀 `##?#` 换 `\\?\`（接口 GUID 尾巴保留、原大小写），`ParseDeviceKey` 纯函数单测锚定 |
| `TsplLabelCompiler` | TsplLabelCompiler.cs | `ILabelCommandCompiler` 实现：直接打印形态（SIZE→CLS→元素→PRINT，不下载表单——「自管模板不进打印机」口径）；纯函数性质 |
| `TsplTextWidthMeasurer` | TsplTextWidthMeasurer.cs | `ITextWidthMeasurer` 的 TSPL 缩放字体实现——驱动 `LabelLayoutResolver` 区域锚定换算 |
| `TsplImageEncoder` | TsplImageEncoder.cs | `ILabelImageEncoder` 实现：整版位图 → BITMAP 原始二进制流（头尾 ASCII + 位图字节一体） |
| `TsplBuiltInFontMetrics` / `TsplQrCodeMetrics` | 同名 .cs | internal 度量知识：缩放字体 0（Helvetica 同度量的 Triumvirate）比例字宽表 + ISO/IEC 18004 QR 容量表（与 Zebra 插件同构知识各自独立成表——插件间零共享依赖） |

**关键机制**：

- **能力挂插件对象而非传输实例**：编译器与图片编码器实现于 `TsplTransportPlugin`（指令集级能力，与连接形态无关——同一指令集 TCP / USB 共用）；宿主装配期以 `plugin is ILabelCommandCompiler` / `plugin is ILabelImageEncoder` 探测。
- **参数条件显隐**：`host` / `port` 仅 `kind=Tcp`、`usbName` 仅 `kind=Usb`、`invertImage`（反色处理，决策 #196）仅 `printMode=image` 可见（决策 #186 VisibleWhen）；`usbName` 留空 = 自动发现第一台 usbprint 设备、可填 VID（如 `VID_1203`）或设备名做包含匹配（决策 #195，对齐 zebra `usbName` 先例）。
- **逐次建连**：发送 / 测试 / 状态查询各自「建连 → 操作 → 关闭」（与内置时代行为等价）。

## 3. 关键流程

### 3.1 宿主装配与传输创建

`.lfplugin` 安装（三层校验 + 官方版本比较）→ 宿主启动扫描装配（决策 #127）→ 用户配置连接后 `Create(parameters, context)` 按 kind 解析构造 `TsplPrinterTransport`（TCP 缺地址即构造期抛 `ArgumentException` 中文提示）。MSI 附带 + 懒激活：客户端 MSI 附带 `plugin-packages\labelframe-transport-tspl-<版本>.lfplugin`，WinHost `OfficialPluginMigration` 检测 connection.json 引用该插件 id 且未安装时自动装（决策 #195，与 zebra 同构）；引导品牌页勾选 `tspl` → `TsplPluginPlacement` 落位包 + 成对清理包（决策 #133 机制）。

### 3.2 发送 / 连接测试 / 状态查询（`TsplPrinterTransport`）

三操作共用 `OpenLink()` 双通道分派：Tcp → `TcpTsplLink(host, port)`；Usb → 设备路径解析（`usbName` 空则发现列表第一台；指定则精确 / 大小写不敏感包含匹配，无匹配给「含可用设备清单」的中文错误）→ `UsbTsplLink(path)`。

- **发送**：文本指令 UTF-8 编码、二进制指令流原样（`SendAsync(string)` / `SendAsync(byte[])` 双重载）；异常统一包装中文 `InvalidOperationException`（消息含 `DescribeTarget()`）；
- **连接测试**（`TestAsync`）：`ESC ! ?` 有应答才算就绪（能连 ≠ 打印机活着）；
- **状态查询**（`GetStatusAsync`）：发送 `ESC ! ?`（0x1B 0x21 0x3F）读 1 字节，单次 500ms 超时、最多 3 次、间隔 200ms（实测语义：任务后短暂无应答）；`ParseStatusByte` 按 TSPL2 手册位语义解析（bit0 头开 / bit1 卡纸 / bit2 缺纸 / bit3 色带尽 / bit4 暂停 / bit5 打印中 / bit7 其他错误；TT065-60 真机定案：头开即时报 0x01、多位按位或组合如 0x05、**缺纸位静态不报——走纸 / 打印尝试后才置 0x04 并锁存，装纸走纸后清除**），Message 最小口径只报可行动状态。

### 3.3 原生指令编译主链（`TsplLabelCompiler.CompileAsync`）

输入原始 `LabelDocument`；输出**整页自包含** TSPL2 文本：`SIZE w mm,h mm` + `DIRECTION 1` + `CODEPAGE UTF-8` + `CLS` → 逐元素 → `PRINT 1`。**GAP / BLINE 不发送**（媒介间隙 / 黑标属耗材属性，交打印机既有标定——真机验证正常）；DIRECTION 显式 1（打印机内存值不可依赖）。元素分发表：

| 元素 | 处置 |
|---|---|
| 区域（`LabelRegionElement`） | `BorderMm > 0` → `BOX`（右下角坐标形态）；无边框跳过——版式容器自身不出指令 |
| 文本 | 边框 BOX + `TEXT x,y,"0",0,xPt,yPt,"content"`（缩放字体 0 磅值字号＝连续缩放；宽度未指定随高度）；加粗近似 = 同内容偏移 1 点重绘（无粗体字体变体）；字符覆盖＝可打印 ASCII + Latin-1，中文等显式失败 `LF_ENC_002`（内置字体无汉字字形，装中文字库前不可表示） |
| Code 128 条码 | 边框 BOX + `BARCODE x,y,"128",h,human,0,narrow,1,"v"`（`128` 自动子集；narrow = 模块宽 dot、wide 恒 1；human = displayValue ? 2 居中 : 0）；中文等按 `LF_ENC_001` 拒绝 |
| 二维码 | 边框 BOX + `QRCODE x+inset,y+inset,ECC,cell,A,0,"v"`（mode A 自动；cell = 边长点数 ÷（矩阵 + 2×静区）钳位 1..10，矩阵按 ISO 容量表估版本 → 17+4v）；中文值经页级 CODEPAGE UTF-8 透传（真机定案可扫——AC-07） |
| 线（`LabelLineElement`） | 水平 / 垂直 → `BAR x,y,w,h`（跨度 × 线宽，dot）；斜线显式失败（BAR 仅正交） |
| 图片（`SourceKey` 空） | 静默跳过（「未指定 = 不存在于打印面」，与两渲染器同口径） |
| 图片（有 key）/ 未知类型 | **显式失败**（消息含「可将打印方式切回图片」）——不回退不混合（§5.4.4） |

**内容转义**：双引号改写 `["]`（TSPL 引号内唯一官方转义形态）。定位与对齐：与图片渲染共用 Core `LabelLayoutResolver`（区域锚定按 TSPL 缩放字体度量 `TsplTextWidthMeasurer` 估算——Triumvirate 即 Helvetica 度量比例字，尽力近似不强求等价，§5.4.1 裁剪原则）+ 块内 TextAlign / VerticalAlign 系数偏移。

### 3.4 图片模式编码（`TsplImageEncoder.EncodeImage`）

`SIZE` + `DIRECTION 1` + `CLS` + `BITMAP 0,0,RowBytes,Height,0,` + **原始位图字节** + `\r\nPRINT 1`。`LabelBitmap` 线格式（行优先 / MSB 在前）与 TSPL BITMAP 一致，**位极性相反**——TSPL 位 0 = 印点（黑）、1 = 空白（手册 + TT065-60 真机反色实证），逐字节取反落流；mode 0 = OVERWRITE（CLS 后缓冲整版覆盖）。**连接级选项**（决策 #196）：`ILabelImageEncoder` 带选项重载（DIM 委托旧签名），宿主分派按连接参数 `invertImage` 构造 `LabelImageEncodeOptions`（键约定常量随 Core 入库，缺省 / 非法值回退 false）——`InvertBits` 开启 = 跳过取反原样透传（位极性相反兼容机型的用户侧自救，真机两态验证见 #382）。产物为二进制指令流 → `LabelJobItem.CommandBytes`（BLOB）→ Worker `SendAsync(byte[])` 回放（决策 #195）。

## 4. 对外契约与依赖

**接口面**（命名空间 `LabelFrame.TransportPlugin.Tspl`）：

- public：`TsplTransportPlugin`（`ITransportPlugin` + `ILabelCommandCompiler` + `ILabelImageEncoder`）、`TsplPrinterTransport`（三接口 + public 注入构造）、`TsplTransportKind`、`TsplLabelCompiler`、`TsplTextWidthMeasurer`、`TsplImageEncoder`、`ITsplLink`（注入缝）；
- internal（`InternalsVisibleTo` WinHost.Tests——`ParseDeviceKey` 纯函数直测；其余经 public 面与 `CompileAsync` 输出间接覆盖）：`TsplBuiltInFontMetrics` / `TsplQrCodeMetrics` / `UsbRawDevice` / `UsbPrinterDeviceDiscovery` / 转义器；
- 插件 id 恒取 Core `TransportPluginIdPolicy.TsplPluginId`（`labelframe-transport-tspl`）——manifest.pluginId / DLL `Id` / 连接配置 pluginId 三处一致；i18n 键全套（DIM `DisplayNameKey` / `DescriptionKey` + spec `LabelKey` / `HintKey` / 选项 `LabelKey`，决策 #192），zh 词条 = 插件侧中文原文由 `web/src/lib/pluginText.test.ts` 镜像锚定。

**依赖**：`LabelFrame.Core` 是**唯一工程依赖 = 插件 ABI 边界**（纯托管，零 NuGet 依赖——与 zebra 插件的 SDK 毒丸清单对照，本插件无原生 / SDK 依赖）。目标框架 `net10.0-windows10.0.26100`（与 WinHost windows 腿一致——TFM 越前破坏装配；USB P/Invoke 用 Windows API）。

**被依赖**（宿主运行时零引用——经 `.lfplugin` 外置装配）：

| 消费方 | 用途 |
|---|---|
| WinHost.Tests | 唯一 ProjectReference 消费方：`TsplLabelCompilerTests`（编译矩阵）、`TsplPrinterTransportTests`（mock 链路传输矩阵）、`TsplImageEncoderTests`、`TsplImageDispatchTests`（分派字节不变 + 二进制载荷）、`OfficialPluginMigrationTests`（懒激活） |
| scripts + 发版流水线 | `build-tspl-plugin.ps1`（.lfplugin 构建，64MB 上限断言）；`release.yml` 构建附件 + Client MSI 附带断言 + Bundle 落位 + 离线包收录 |
| web | `pluginLabels.json` zh + en 词条（键空间 `plugin.labelframe-transport-tspl.*`） |

## 5. 设计取舍（决策索引）

| 主题 | 决策 |
|---|---|
| 指令集维度单插件 + 品牌映射表指令集维度（brand tspl → plugin-tspl） | #195（§6.8） |
| 图片模式编码器按连接插件分派（`ILabelImageEncoder` 可选能力；未实现维持 `^GF` 逐字节不变） | #195（§5.4.1） |
| 二进制指令流入契约（`CommandBytes` BLOB + `SendAsync(byte[])` DIM 重载） | #195（§5.4.1） |
| 文档编译能力接口 + 连接级打印方式参数（提交时编译持久化 / 显式失败不回退） | #137（契约 §5.4） |
| 区域锚定按实测文本宽度、度量接口注入 | #110 |
| 参数条件显隐（VisibleWhen） | #186 |
| 外置插件加载 ALC 生命周期（单 ALC 整体加载 / 注册表持根） | #127 |
| MSI 附带 + 懒激活（zebra 同款） | #195 / #123 |
| 自描述文案多语言（键化：spec 属性 + 接口 DIM） | #192 |
| 官方插件体系（覆盖安装版本比较 / 服务端分发 / 三层校验） | #123（§6.8） |
| 图片模式「反色处理」连接级开关（`ILabelImageEncoder` 选项重载 + `invertImage` 参数键约定） | #196 |
| 注释极简化（本篇承接源码设计知识） | #189 |

**迭代 143 四项用户拍板**（Issue #378 待决议）：指令集维度单插件 / MSI 附带同款 zebra / USB 留空自动发现＋可指定 / TSPL2 基线＋反白（REVERSE）不做。

## 6. 平台坑与不变量

修改本模块前必读：

1. **只依赖 Core＝ABI 边界**：引用 WinHost / Rendering 任一工程即破坏外置性；本插件虽纯托管，仍随包自带全部伴生 DLL（当前仅主 DLL 一件——构建脚本断言「恰好 1 个 DLL」，混入宿主必带件即红）。
2. **usbprint 重叠 IO 真机不收尾**：TT065-60 实测 `FILE_FLAG_OVERLAPPED` 写入挂起（OVERLAPPED 结构对齐修正后仍超时），同步 IO 即时完成——本通道一律同步 CreateFile + 看门狗（Task 等待超时即按失败语义处理，被放弃的阻塞线程随链路关闭、句柄释放中止未决 IO 退出）。
3. **USB 设备路径来自注册表键名**：`DeviceClasses\{28d78fad-5a12-11d1-ae5b-0000f803a8c2}`（GUID_DEVINTERFACE_USBPRINT）子键名前缀 `##?#` 对应 CreateFile 路径前缀 `\\?\`、**接口 GUID 尾巴必须保留**、保留原大小写（旧实现剥 `#\` 段与 GUID 尾巴 → 路径不存在，TT065-60 真机实证）；接口 GUID 勿凭记忆猜（2026-10-16 本机双设备 VID_0A5F / VID_1203 实证）。
4. **`ESC ! ?` 位语义按 TSPL2 手册**（bit0 头开 / 1 卡纸 / 2 缺纸 / 3 色带尽 / 4 暂停 / 5 打印中 / 7 其他）；`ESC` 前缀 = 立即响应、`~` 前缀 = 就绪才响应（`~!?` 非 TSC 官方指令）。实测错误位任务后锁存、紧贴任务短暂无应答——查询带重试（3 × 500ms）为此设计。
5. **BITMAP 数据必须精确 width×height 字节原始二进制**：不转十六进制（决策 #195 明确）；行优先 / MSB 与 `LabelBitmap` 一致，**位极性相反（TSPL 位 0 = 印点）——必须逐位取反**（TT065-60 真机反色实证，勿「优化」掉取反）。
6. **GAP / BLINE 不发**：媒介间隙是耗材属性，发错比不发更糟（打印机既有标定兜底）；DIRECTION 必须显式（打印机内存值不可依赖）。真机验证通过（GAP 不发走纸正常、DIRECTION 1 方向正确——AC-07）。
7. **QR 中文 = CODEPAGE UTF-8 透传（真机定案）**：TT065-60 实测中文 QR 内容手机可扫、原文无损（AC-07）；无静默过滤，不需要显式失败分支。
8. **native 编译显式失败，不回退不混合**：不支持元素 / 字符以失败结果返回（`LF_ENC_001` / `LF_ENC_002`），不自动回退图片、不做元素级混合；不要往编译器加排版模拟（自动换行 / 缩放 / 图片均裁剪——§5.4.1 裁剪原则）。
9. **文本字号单位是磅不是倍率**：TSPL 缩放字体 "0" 的 x/y-mult 单位为 point（1 pt = 1/72 inch）；点阵字体 1~5 才是整数倍率（1..10）且不支持连续字高——编译器只用字体 0，勿按倍率口径换算。
10. **TFM 与宿主 windows 腿一致**（`net10.0-windows10.0.26100`）：插件由宿主运行时字节加载，TFM 越前破坏装配；P/Invoke 依赖 Windows API（usbprint CreateFile），非 Windows 端不装配本插件（`.lfplugin` 平台门，决策 #157）。
11. **OEM 资料分辨率不可尽信**：TT065-60 OEM 规格标 203dpi，真机实测 **300dpi**（版面缩比 203/300 实证；`~!T` 回读机型名不含分辨率信息）；DPI 是主机级配置（`HostOptions.Dpi` / `LABELFRAME_DPI`），接 300dpi 机型必须配置一致，否则原生编译版面整体缩小约三分之一。
