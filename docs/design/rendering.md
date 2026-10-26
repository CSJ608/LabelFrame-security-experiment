# LabelFrame.Rendering 模块设计

> 覆盖：`src/LabelFrame.Rendering/`（6 文件，约 810 行）· 基线：`master` `7486dae`（2026-10-19）
> 分工与模板说明见 [README.md](README.md)；决策原文见 [DESIGN.md](../DESIGN.md) 决策表。

## 1. 职责与边界

**做什么**：把 `LabelDocument`（Core 的中间文档模型）整版渲染为 **1bpp 位图**（`LabelBitmap`，白底黑字）或 **PNG 字节**，供图片打印（`^GF` 编码前置）、预览 / 调试出图、Log 模拟打印出图共用。SkiaSharp 后端，Windows / Linux 宿主同一实现（决策 #17 中文渲染架构、#24 预览渲染——预览与打印共用渲染器，避免平台渲染漂移）。

**不做什么**：

- 布局解析（区域锚定、边界盒计算）——Core 的 `LabelLayoutResolver` 承担，本模块只消费其结果；
- ZPL 编码（`LabelBitmap` → `^GF`）——Core 的 `ZplImageEncoder`；
- 字体文件管理——依赖系统字体与部署基线（Linux 容器 fontconfig + 文泉驿微米黑，决策 #88）；
- Android 渲染——`LabelFrame.AndroidHost` 自带 `AndroidLabelRenderer` 用平台位图实现同一编码契约（决策 #17 / #28），不引用本模块。

## 2. 内部结构

| 类型 | 文件 | 角色 |
|---|---|---|
| `ILabelBitmapRenderer` / `SkiaLabelRenderer` | SkiaLabelRenderer.cs | 整版渲染器：元素分发绘制、文本适配、条码 / 二维码（ZXing）、图片、1bpp 转换、中间态表面池 |
| `SkiaTypefaceLookup` | SkiaTypefaceLookup.cs | 字型查找与系统回退（internal，测试经 `InternalsVisibleTo` 直查） |
| `SkiaTextWidthMeasurer` | SkiaTextWidthMeasurer.cs | `ITextWidthMeasurer`（Core 接口）的 Skia 实现——锚定度量的默认实现（决策 #110） |
| `LabelBitmapPng` | LabelBitmapPng.cs | `LabelBitmap`（1bpp）→ PNG 静态工具 |

**表面池**（决策 #92 引入，#223 裁剪托管暂存）：整版渲染中间态 `SKBitmap` 原生像素内存按尺寸池化复用；黑白转换直接同步读取该位图的只读像素 span，不再分配/保留整幅托管暂存；输出（`LabelBitmap` / PNG 字节）始终新分配。

- 池上限 `MaxPooledSurfaces = 4`（留出并发渲染——多请求提交 + 出图 / 预览的余量），满则 Dispose；
- 租用条件＝宽、高全匹配，任一不匹配的池内项即被弹出释放；位图统一为 BGRA/Opaque，不再有可失配的托管暂存；
- 位图与 PNG 路径均在 `finally` 归还 bitmap；两种路径交替调用时继续复用同尺寸中间态，输出保持独立。像素 span 只在本次租用期间同步读取，不逃逸、不在归还后使用；转换得到的新 1bpp 数组不借用池化像素。

## 3. 关键流程

`LabelBitmapPng.ToPng` 将 1bpp 像素直接展开到新建 SKBitmap 的原生像素 span，按 SKBitmap.RowBytes 定位行，不再分配整幅托管 BGRA 暂存或执行 Marshal.Copy。span 仅在本次位图存活期间同步使用，没有共享缓冲/新增池；位图和编码对象仍逐次释放。颜色类型、透明度、黑白规则、PNG 编码参数与返回字节保持不变；这一路主要服务模拟打印和调试出图，不是实体打印的传输提速。按位模式、非字节对齐宽度、多 DPI 尺寸、并发与旧实现逐字节对照保护兼容性。

### 3.1 整版渲染主链（`RenderLabelBitmap` / `RenderLabelBitmapPng`）

1. 尺寸：版式毫米 → 点（`mm / 25.4 × dpi`，四舍五入远离零，下限 0；调用侧再 `Math.Max(1, …)` 保证正尺寸）；
2. `RentSurface` 租中间态 → `SKCanvas` 清白底 → `LabelLayoutResolver.IndexRegions` 建区域索引 → 按 `Elements` 顺序逐元素 `DrawElement` 分发；
3. 位图路径：`GetPixelSpan` → `ToLabelBitmap(ReadOnlySpan<byte>)` 按原生 RowBytes 逐像素 luma 阈值二值化（等价于加权 `R×299 + G×587 + B×114 / 1000 < 128` 置黑）→ `finally ReturnSurface`；
   PNG 路径：`SKImage.Encode(Png, 100)` 直接编码；
4. 文本取值：`LabelElementContent.TryGet`（`Literal ?? data[SourceKey]`，决策 #35）。

黑白转换在整数域以 `R×299 + G×587 + B×114 < 128000` 判定黑点（决策 #221）：加权和非负，因此与原先整除 1000 后 `<128` 完全等价，最大值 255000 不溢出 int；不是更改阈值或改用浮点近似。源/目标行偏移按行计算，非字节对齐尾位仍保持初始零。`DecodeMonochrome` 保留原表达式，两者阈值语义相同；16777216 种 RGB、不同 alpha、行填充与宽度边界的原算法对照保证输出不变。

### 3.2 文本渲染与适配（`DrawText`）

**框高规则**（决策 #46 垂直对齐契约 + #146 锚定盒穿底修复）：

- `heightMm > 0`：显式块高；
- 无显式高且锚定（`regionId` 命中区域）：用 `ResolveBounds` 解析出的锚定框高（自动高度即字高）；
- 无显式高且非锚定：兜底 `max(字高 + 2 × max(paddingH, paddingV), 10mm)`——与前端读回兜底一致。

**三种适配模式**（`wrap` × `fitMode`，决策 #47）：

| 模式 | 行为 |
|---|---|
| `wrap=true` | 按内容区宽自动换行；整体超高则整体等比缩小（下限 1.5mm） |
| `wrap=false` + `Overflow` | 单行不缩小，按内容区裁剪（`ClipRect`） |
| `wrap=false` + `Shrink`（默认） | 单行，超宽 / 超高按比例缩小至下限 1.5mm |

**换行算法**（`WrapLines`）：按 `\n` 拆硬行 → 英文按空格断词 → 超宽单词 / 无空格文本（中文）逐字符试探拆行。行高系数 `lineHeight`（默认 1.2）。

**对齐**：水平 Left / Center / Right（仅内容区宽有限时生效；宽 0 = 不限宽，不裁右界）；垂直 Top / Middle / Bottom（按内容区高与总行高）。绘制基线 = 行顶 − `font.Metrics.Ascent`。

**边框**：画在元素框（`borderMm > 0` 且框宽 > 0）；内容区 = 元素框四边内缩 paddingH / paddingV（双边内边距，与前端 ElementNode 同语义）。

### 3.3 字型查找与回退（`SkiaTypefaceLookup.CreateTypeface`）

优先链：指定 `fontFamily`（空缺省 `LabelTextElement.DefaultFontFamily`）→ `"Microsoft YaHei"` → `SKTypeface.Default`。

文本含非 ASCII 时**按实际字符**走系统回退：逐字符取首个非 ASCII 字符（代理对按完整码点，孤立代理项跳过）交 `SKFontManager.Default.MatchCharacter(codePoint)`，无匹配回退 preferred。此策略修复此前硬编码以「中」匹配的缺陷——任意非 ASCII 文本被套中文字体，韩文 / 泰文等在雅黑无字形成豆腐块（缺陷修复）。已知取舍（单字型渲染管线固有限制）：首个非 ASCII 字符若属生僻范围（如扩展 B 区），可能匹配到仅覆盖该范围的字体，其余常用字符缺字形——按文本实际脚本优先保障。

**同源不变量**：文本渲染与字宽度量共用本查找器（同一字型 + 同一加粗口径 `Embolden`），保证区域锚定偏移的度量与实际绘制一致（决策 #110）。`SkiaTextWidthMeasurer` 在参考比例 16px/mm 下矢量度量后线性换算毫米——结果与渲染 DPI 无关；无状态单例。

### 3.4 图片渲染（`DrawImage`）

来源优先级与缺资源语义（决策 #184 图片控件）：

1. `sourceKey` 为空＝未指定图片——**整元素跳过（含边框）**，语义「未指定 = 不存在于打印面」；
2. `templateImages`（模板包资源）命中 → `DecodeMonochrome` 解码并单色化 → `DrawImage` 线性采样缩放到元素框；
3. `document.Images`（提交时附带的 1bpp 位图）命中 → 直接换算 BGRA 绘制；
4. 都未命中（key 非空缺资源）→ 抛 `MissingImageResourceException`（Core 定义，消息含键名）——显式报错不画灰占位，预览 / 出图 / 打印链一致失败。

`DecodeMonochrome`：解码 → 白底重绘（透明区按白，出纸无墨）→ luma 阈值 128 单色化（与 `ToLabelBitmap` 同阈值同读取序 BGRA）——预览 PNG 与打印 1bpp 输出一致。线性采样（`SKFilterMode.Linear`）用于大幅缩小（原图远大于元素盒）时防混叠失真、黑白硬边保边缘锐度。

### 3.5 条码与二维码（`DrawBarcode` / `DrawQrCode`）

ZXing writer 的格式、编码选项与 Encode 路径不变；取得最终 BitMatrix 后直接写入新建 BGRA/Opaque SKBitmap，不再调用 PixelDataRenderer 生成整幅托管数组及 Marshal.Copy。按 RowBytes 切分行，以本机字节序写不透明黑/白，逐行逐像素读取矩阵，不假设 Code128 行重复或 QR 行结构。原生像素 span 不逃逸位图生命周期，转换异常释放位图，正常由绘制调用方释放；无新增缓存/并行或公共接口。旧 PixelDataRenderer 逐字节对照覆盖最小尺寸、编码后宽度大于请求宽度、不同静区、QR 四档纠错和中文内容；任意非重复矩阵行及并发独立性另有回归保护。此处仅服务默认黑白纯码绘制，不扩展为自定义颜色或 ZXing 内建文字渲染器。

- 条码固定 **Code128**（ZXing `BarcodeWriter`，`Margin = 2` 模块静区）；`displayValue = true` 时底部数值文字带由渲染器自绘（ZXing 侧 `PureBarcode = true` 屏蔽其内建文字，字体走默认字体族）——字号取 `max(1.5mm, 内容高 × 15%)`、超宽等比缩小、基线贴内容区底（`contentY + contentH − Descent`），条码占剩余高度；
- 二维码保持**正方形**（内容区取 `min(宽, 高)`）并居中；ECC 档 L / Q / H、默认 M，边距 `qrMargin`（默认 2，决策 #47）；
- 条码 / 二维码值为空白时**整体跳过（含边框）**——与图片空 key 同语义；文本元素相反：空值先画边框再返回（边框仍在）。

## 4. 对外契约与依赖

**接口面**（全部公开于 `LabelFrame.Rendering` 命名空间）：

- `ILabelBitmapRenderer.RenderLabelBitmap / RenderLabelBitmapPng(document, dpi = 203, templateImages = null)`；
- `SkiaTextWidthMeasurer.Instance`（`ITextWidthMeasurer` 实现，Core 布局解析注入用）；
- `LabelBitmapPng.ToPng(LabelBitmap)`；
- internal：`SkiaTypefaceLookup`（经 `InternalsVisibleTo("LabelFrame.WinHost.Tests")` 供字型回退单测直查）。

**被依赖**（DI 注册均为单例：`Server/Program.cs`、`WinHost/WinHostApp.cs`）：

| 消费方 | 用途 |
|---|---|
| LabelFrame.Api `RenderEndpoints` | 预览 / 调试出图（`/api/print/render-image(s)` 端点，PNG 输出） |
| LabelFrame.Api `TemplateEndpoints` | 模板测试出图（PNG） |
| LabelFrame.WinHost `JobSubmissionService` | 打印链：`RenderLabelBitmap` 出 1bpp 供 `ZplImageEncoder`；Log 模拟打印用 `LabelBitmapPng.ToPng` 落盘 |
| LabelFrame.WinHost `PrinterApi` | 测试打印端点 |
| 测试 | `WinHost.Tests/Rendering/`（渲染器 / 元素 / 图片三组）与 `SkiaTypefaceLookupTests`；`Api.Tests`；`Benchmarks`（打印管线基准） |

**依赖**：`LabelFrame.Core`（模型与布局解析）＋ NuGet：`SkiaSharp 3.119.2`（版本下限固化，见 §6）、`SkiaSharp.NativeAssets.Linux`（Linux 原生库；Windows 由主包提供）、`ZXing.Net 0.16.11`。目标框架 `net10.0` 单目标。

## 5. 设计取舍（决策索引）

| 主题 | 决策 |
|---|---|
| 中文渲染架构：整版位图 + `^GF`，各宿主同构 | #17（Android 平台位图同契约 #28） |
| 预览与打印共用渲染器（避免平台渲染漂移） | #24 |
| 文本垂直对齐契约（`heightMm` / `verticalAlign`） | #46 |
| 文本适配字段（`wrap / lineHeight / fitMode / fontFamily`）、二维码 ECC / 边距、条码 `displayValue` | #47 |
| 加粗方案（`Embolden` 口径） | #49 |
| 图片打印收敛为整版位图、调试出图 | #50 |
| Linux 容器中文字体基线（fontconfig + 文泉驿） | #88 |
| 渲染中间态 SKBitmap 池化 | #92 |
| 区域锚定按实测文本宽度、度量接口注入 | #110 |
| 锚定文本框高语义（穿底边修复） | #146 |
| 图片单色统一 + 缺资源显式报错 | #184 |
| 字体回退按实际字符（豆腐块缺陷修复） | 缺陷修复（未立决策，无决策号） |

## 6. 平台坑与不变量

修改本模块前必读（源码中对应位置保留白名单注释）：

1. **度量 / 绘制同源**：`SkiaTypefaceLookup` 与 `Embolden` 口径同时供渲染与 `SkiaTextWidthMeasurer` 度量——任何一侧单独改动都会造成锚定偏移错位（决策 #110 的存在理由）。
2. **1bpp 阈值同源**：`DecodeMonochrome` 与 `ToLabelBitmap` 两处 luma 阈值必须同为 128、同为 BGRA 读取序——预览与打印输出一致性依赖它。
3. **锚定文本框高 = 解析锚定框高**：锚定路径不得改用非锚定兜底框（`max(字高 + 2×max(padding), 10mm)`）再垂直居中——字形会比锚定盒中心下沉约（兜底框高 − 字高）/2，小字号时穿出盒底（曾出的真机缺陷，决策 #146）。
4. **SkiaSharp 3.x 采样 API**：缩放采样走 `DrawImage(SKImage, rect, SKSamplingOptions)`——`SKPaint.FilterQuality` 已废弃。
5. **SkiaSharp 版本下限 3.119.2 为兼容义务**：低于该版本会在 Client MSI 覆盖升级链触发 Windows Installer 组件规则拒装降版 keyfile；升版须同步上调 `scripts/assert-client-deps-baseline.ps1` 基线（决策 #130）。
6. **`InternalsVisibleTo("LabelFrame.WinHost.Tests")`**：字型回退单测直查 internal 查找器，删除该声明即断测试。
7. **未指定图片 = 不存在于打印面**：`sourceKey` 为空跳过整元素（含边框）；key 非空缺资源才报错——两条路径语义不同，不可合并（决策 #184）。
8. **ZXing `PureBarcode = true` + 自绘文字**：条码数值文字带由渲染器自绘（字体 / 缩放与文本元素同源），ZXing 内建文字已被屏蔽——改用 ZXing 绘制会破坏字体一致性。
