# LabelFrame.Api 模块设计

> 覆盖：`src/LabelFrame.Api/`（8 文件，约 640 行）· 基线：`master` `7486dae`（2026-10-19）
> 端点契约细节（路径 / 请求响应形态 / 错误码全集 / 状态机）见 [DESIGN.md](../DESIGN.md) §5「API 概览」，本篇不重复；分工与模板说明见 [README.md](README.md)。

## 1. 职责与边界

**做什么**：Server 与 WinHost 两宿主的**共享 HTTP 契约层**（决策 #75）——把两宿主重复的 DTO、模板库 / 调试出图 / Excel / 设备日志端点收敛为一处实现，一处修复两端生效：

- 请求 / 响应 DTO（`Contracts.cs`：`SubmitJobRequest` / `TemplateDto` / `LabelDto` / `TemplatePackageDto` / `PreviewRequest` / `PushLogRequest` / `ExcelTemplateRequest` / `ExcelTemplateColumnDto` / `ErrorView`）；
- 共享端点实现（`Endpoints/` 四组，路由与形态见 §5.1 / §5.2）；
- 错误响应基础设施：问题码注册表（`ApiErrorCodes`）、`ErrorView` 形态（含可选 `params`，决策 #166）、全局异常处理器（`GlobalExceptionHandler` + 宿主注册扩展）。

**不做什么**：

- 作业模型与执行（提交后的队列 / 状态机 / 投递）——Server 的 `ServerService`、WinHost 的 `JobSubmissionService`；本模块的 `SubmitJobRequest` 只定义请求形态（`CallbackUrl` 仅路由模式 Server 消费，决策 #154）；
- 宿主专属端点——Server `Api/`（设备 / 作业 / 投递 / 分发）、WinHost `Api/`（作业 / 连接 / 插件 / 机器级 / 打印机）；
- 错误码全集——`LF_JOB_*`（Core `JobErrorCodes`）、`LF_SRV_*`（Server `ServerErrorCodes`）不在本模块；本模块注册表只管共用端点码（`LF_TPL_*`）与跨端兜底（`LF_API_BAD_BODY` / `LF_INTERNAL_001`），另集中定义 WinHost 连接 / 插件端点消费的 `LF_TRANSPORT_*` / `LF_PLUGIN_*`（见 §2）；
- 模板存储 / Excel 读写 / 日志存储 / 渲染——Core 的 `TemplateStore` / `TemplatePackageSerializer` / `ExcelTemplateWriter` / `ExcelTableReader` / `SqliteLogStore` 与 `LabelFrame.Rendering`，端点只做编排；
- **AndroidHost**——自带 DTO 与错误码字面量副本（`EmbeddedHttpServer` / `Errors/LfErrorCatalog.cs`，决策 #27 / #167），不引用本工程；这是边界不是遗漏，但码值 / 契约变更须三端同步（见 §6）。

## 2. 内部结构

| 类型 | 文件 | 角色 |
|---|---|---|
| `ApiErrorCodes` | ApiErrorCodes.cs | 共用端点问题码注册表（`LF_TPL_001/002`、`LF_API_BAD_BODY`、`LF_INTERNAL_001`）＋ WinHost 连接 / 插件 / 测试页码（`LF_TRANSPORT_*`、`LF_PLUGIN_*`，消费方为 WinHost `Api/TransportApi` / `PrinterApi` / `PluginApi`，集中定义保持跨工程一处） |
| 9 个 record | Contracts.cs | 两宿主共享 DTO 与 `ErrorView`（错误响应形态：`code` + 中文 `message` + 可选 `fieldKey` / `params`） |
| `GlobalExceptionHandler` ＋ Extensions | GlobalExceptionHandler.cs | `IExceptionHandler` 分类兜底（§3.3）＋ `AddLabelFrameExceptionHandler` 宿主注册扩展 |
| `TemplateEndpoints` | Endpoints/TemplateEndpoints.cs | 模板库端点（CRUD / 导入导出 / 预览 7 条路由）＋ 图片三态解析与硬校验 |
| `RenderEndpoints` | Endpoints/RenderEndpoints.cs | 调试出图（单张 PNG / 批量 zip）＋ 图片资源解析 `ResolveImagesAsync` |
| `ImportEndpoints` | Endpoints/ImportEndpoints.cs | Excel 模板生成 / xlsx 解析（前端做列映射） |
| `LogEndpoints` | Endpoints/LogEndpoints.cs | 设备日志回传 / 查询 |

**Options 错误码注入**（决策 #75 的机制核心）：每组端点一个 Options record（`TemplateApiOptions` / `RenderApiOptions` / `ImportApiOptions` / `LogApiOptions`），宿主接线时传入各自错误码常量与 DPI，同一实现对外保持两宿主原有错误码。当前接线（`Server/Program.cs:153`、`WinHost/WinHostApp.cs:208`）：

| 端点组 | Server | WinHost |
|---|---|---|
| Template | `InvalidRequest=LF_SRV_002`、`TemplateNotFound=LF_SRV_006`、`ImageInvalid=缺省 LF_TPL_002` | `InvalidRequest=LF_API_001`、`TemplateNotFound=LF_TPL_001`、`ImageInvalid=缺省 LF_TPL_002` |
| Render | `LF_SRV_002` | `LF_API_001` |
| Import | `LF_SRV_002` | **未接线**（WinHost 不调 `MapImportApi`；实现保持宿主无关，任一宿主可接线） |
| Log | `LF_SRV_002` | `LF_API_001` |

模板图片校验失败码 `ImageInvalid`（`LF_TPL_002`）为可选覆盖位，两端当前均用注册表缺省值。

## 3. 关键流程

### 3.1 模板保存与图片三态解析（`POST /api/templates`）

1. 基本校验：`name` / `contract` / `layout` 缺失 → 400 + `InvalidRequestCode`；
2. `ResolvePostedImagesAsync` 三态解析（决策 #184，normative 语义见 §5.1「模板保存图片契约」）：
   - `images` 为 null（**缺省与显式 null 在 record 可选参数反序列化下同为 null、不可区分，契约合并为一态**）→ 读模板存量图片并入＝保留；
   - 显式字典 → 逐图 base64 解码＋硬校验（魔数探嗅仅 PNG / JPG / BMP、单图 ≤2MB、≤10 张、总量 ≤10MB，**均按解码后原始字节而非 base64 长度**）——空字典自然产出空集＝清空，非空＝全量覆盖（落库走 `SaveAsync` 先删后插）；
   - 校验失败抛 `FormatException`（坏 base64，消息为键名）或 `InvalidOperationException`（上限 / 格式，中文消息含键名）→ 400 + `LF_TPL_002`；
3. `Group` 缺省落「默认」；保存 OK 返回 `{ name, group }`；
4. 已知取舍：保留态「先读存量再并入」与 `TemplateStore` 的 testData 读-改-写先例同构——两步非同事务存在竞态窗口，单设计器场景可接受。

### 3.2 预览与调试出图

- **预览**（`POST /api/templates/{name}/preview`）：模板缺失 → 404（模板化消息 `模板不存在:{templateName}。` + `params`）；`LabelDocument{ Layout, Data = 请求 data ?? 模板 testData 回退 }` → `RenderLabelBitmapPng(dpi, package.Images)` → PNG。DPI 取宿主 Options 配置（决策 #24 预览与打印共用渲染器，并入 #75）。
- **单张出图**（`render-image`）：校验（contract+layout、labels ≥1）→ `ResolveImagesAsync`（请求附带 base64 优先、按名回退本地模板库——两宿主语义统一）→ 首张标签 → PNG 下载（`{name}-print.png`）。
- **批量出图**（`render-images`）：逐张 `RenderLabelBitmap` → `LabelBitmapPng.ToPng` → zip（`label-N.png`，包名 `{name}-debug-{时间戳}.zip`）。
- 渲染抛 `MissingImageResourceException`（Core 定义）→ 400 + 图片码（预览走 `LF_TPL_002`、出图走 `InvalidRequestCode`）——key 非空缺资源显式报错，不静默灰占位照打（决策 #184）。
- 404 / 解析失败消息一律「模板 + 参数」构造（`ErrorMessageTemplates.Format`，Core），参数键 `templateName` / `detail`——同码所有调用点键集一致（决策 #166 ⑥，见 §6）。

### 3.3 错误响应链路（`GlobalExceptionHandler`）

1. `OperationCanceledException` 且 `RequestAborted` → 客户端断开导致的取消，静默结束（不是服务错误）；
2. `BadHttpRequestException` / `JsonException` → 400 + `LF_API_BAD_BODY`（中文消息，提示检查 JSON / UTF-8 / Content-Type）；原始解析异常（含行 / 列位置）只进日志 Warning 不透出客户端——minimal API 模型绑定把非法 JSON / 非 UTF-8 / 类型不匹配包装为 `BadHttpRequestException`（400 语义，决策 #107）；
3. 其余未捕获 → 500 + `LF_INTERNAL_001`（固定中文兜底，不透出堆栈 / 内部路径 / 异常消息）；
4. `AddLabelFrameExceptionHandler` 三件套：`AddExceptionHandler` ＋ `AddProblemDetails`（**无参 `UseExceptionHandler()` 的必需配套**，缺失则中间件构建即抛）＋ `ThrowOnBadRequest = true`（Production 下框架默认把绑定失败短路为「400 空 body」，固定开启保证任何环境都进入上面的统一分类）。

### 3.4 Excel 与设备日志

- `POST /api/import/excel-template`：columns 过滤空 key、`displayName` 缺省回退 key → `ExcelTemplateWriter.CreateTemplate` → xlsx 下载（生成逻辑在 Core，决策 #70）；
- `POST /api/import/excel`：`IFormFile` → `ExcelTableReader.ReadTextTable` → `{ headers, rows }`（列映射由前端做）；解析失败 400 + 模板化消息 `Excel 解析失败：{detail}`（params `detail` 为异常原因）；TemplateFrame 3.0 资源超限同样走此失败出口，不返回部分 rows，具体预算见 [Core 设计](core.md#excel-资源边界)；
- `POST /api/logs` 回传（`SqliteLogStore.AppendAsync`，一次多行按物理行拆独立记录）；`GET /api/logs?deviceId=&since=` 查询（新记录在前）。

## 4. 对外契约与依赖

**接口面**（全部公开于 `LabelFrame.Api` / `LabelFrame.Api.Endpoints`）：

- DTO 9 record、`ApiErrorCodes` 常量、`ErrorView`；
- 4 组 `Map*Api` 扩展 ＋ 4 个 Options record；
- `AddLabelFrameExceptionHandler`（宿主还需 `app.UseExceptionHandler()` 激活）；
- `RenderEndpoints.ResolveImagesAsync`（public static——WinHost `JobSubmissionService.cs:335` 打印链复用同一图片解析语义：附带优先、按名回退）。

**依赖**：`LabelFrame.Core`（`LabelContract` / `LabelLayout`、`LabelDocument`、`MissingImageResourceException`、`TemplateStore` / `TemplatePackage` / `TemplatePackageSerializer`、`SqliteLogStore`、`ExcelTemplateWriter` / `ExcelTableReader` / `ExcelTemplateColumn`、`ErrorMessageTemplates`）＋ `LabelFrame.Rendering`（`ILabelBitmapRenderer` / `LabelBitmapPng`）＋ `FrameworkReference: Microsoft.AspNetCore.App`。目标框架 `net10.0` 单目标。

**被依赖**：

| 消费方 | 用途 |
|---|---|
| LabelFrame.Server（`Program.cs:106-165`） | 4 组端点全接线＋异常处理器；码前缀 `LF_SRV_*` |
| LabelFrame.WinHost（`WinHostApp.cs:163-220`） | Template / Render / Log 三组接线＋异常处理器（Import 未接）；码前缀 `LF_API_001` / `LF_TPL_001`；`JobSubmissionService` 复用 `ResolveImagesAsync`；`TransportApi` / `PrinterApi` / `PluginApi` 消费 `LF_TRANSPORT_*` / `LF_PLUGIN_*` 常量 |
| test/LabelFrame.Api.Tests | TestServer 集成测试＋错误码参数键集一致性源扫描（§6） |
| LabelFrame.AndroidHost（**间接**，无项目引用） | `Errors/LfErrorCatalog.cs` / `Api/Dtos.cs` 维护同值副本（决策 #167）；码值或 `ErrorView` 形态变更须同步 |
| web 前端（**间接**） | 消费端点契约与 LF_* 码表（`web/src/i18n/locales/<locale>/errorCodes.json`，决策 #166） |

## 5. 设计取舍（决策索引）

| 主题 | 决策 |
|---|---|
| API 契约与端点共享库（本模块存在理由；Options 错误码注入、两宿主对外错误码不变） | #75 |
| AndroidHost 极简 HTTP、自带副本不依赖本工程 | #27 / #167 |
| 预览与打印共用渲染器（DPI 取宿主配置） | #24（共享化并入 #75） |
| 模板包格式 `.lfpkg`（zip + manifest + images/） | #23 |
| 端点无鉴权——局域网信任模型 | #79 |
| 错误响应分类修正（`LF_API_BAD_BODY` 400 化、`LF_TRANSPORT_TEST_FAILED`） | #107 |
| `SubmitJobRequest.CallbackUrl` 终态回调（仅路由模式 Server 消费，直连接受但忽略） | #154 |
| 多语言基线（错误码权威化＋展示端翻译，后端零语言状态） | #164 |
| `ErrorView` 可选 `params`、LF_* 码表、单码单参数键集 | #166 |
| PDA 码表与 ErrorView 副本三端对齐 | #167 |
| 模板保存图片三态语义＋解码硬校验上限 | #184 |
| Excel 模板下载端点（生成逻辑下沉 Core） | #70（共享化并入 #75） |

## 6. 平台坑与不变量

修改本模块前必读（源码中对应位置保留白名单注释）：

1. **码字面量唯一性**：`LF_API_BAD_BODY` / `LF_INTERNAL_001` / `LF_TPL_*` / `LF_TRANSPORT_*` / `LF_PLUGIN_*` 的字面量在后端全仓仅 `ApiErrorCodes` 一处定义，其余代码一律引用常量——新增错误构造不得写字面量。新增 / 改码须同步三端码表：web（`errorCodes.json` zh-CN ＋ en）与 PDA（`LfErrorCatalog.cs`），漏表在 CI 即红（决策 #166 ④）；AndroidHost 的同值字符串是展示端码表词条，不是重复定义。
2. **`ThrowOnBadRequest = true` 与 `AddProblemDetails` 是环境一致性的两道闸**：关掉前者，Production 下模型绑定失败被框架短路为「400 空 body」，错误响应出现非 ErrorView 形态；删掉后者，无参 `UseExceptionHandler()` 中间件构建即抛错。
3. **`params` 序列化逐字节等价是兼容义务**：`ErrorView.Params` 带 `JsonIgnore(WhenWritingNull)`，缺省 / null 时 `params` 不得出现在 JSON（`fieldKey` null 照旧输出）——旧调用方（WMS 等）依赖旧形态。给 `ErrorView` 加可选字段照此模式：追加在尾部＋ WhenWritingNull。
4. **单码单参数键集**：同一错误码所有携带 params 的调用点参数键集必须一致，且与 zh / en 词条占位符逐键相等——否则该变体 en 态触发「参数不全」守卫回退中文（LF_SRV_006 的 `{name}` vs `{templateName}` 即历史缺陷，返修后统一 `templateName`）。`ErrorParamKeyConsistencyTests` 做源扫描断言：`TemplateEndpoints.cs` / `ImportEndpoints.cs` 文件名 → Options 类型映射、Options 构造**位置参数索引**（TemplateApiOptions 第 5 参数、ImportApiOptions 第 1 参数）均登记在测试内——重命名端点文件或调整 Options 参数顺序，须同步该测试，否则扫描静默失配（测试另有站点数下限防假绿）；新惯用法不在扫描器两种模式（`Templated(...)` / 邻近 `new Dictionary` + 码表达式）内会直接红。
5. **图片三态的 null 合并是契约定死**：缺省与显式 `images: null` 反序列化同为 null、不可区分，合并为一态（保留）；若未来要「显式 null＝清空」须改 DTO 形态，不得在端点层猜（决策 #184 本轮定死不留歧义）。
6. **模板化消息与旧内插文案逐字等价**：`模板不存在:{templateName}。` 冒号后无空格是既有文案形态，改动任一字符都会破坏「后端 message 与改造前内插消息等价」锚点与既有消息断言。
7. **共享端点禁写宿主专属逻辑**：端点实现内只准用 options 注入的码位，不得直引 `ServerErrorCodes` / `JobErrorCodes`；宿主差异（码、DPI、模板库实例）全部经 Options 传入——这是「一处实现两端生效」的前提。
8. **客户端断开的取消不是服务错误**：`OperationCanceledException` ＋ `RequestAborted` 静默返回；按 500 处理会在页面关闭 / 压测时刷错误日志误导排障。
9. **DPI 由宿主注入不落模块常量**：预览 / 出图像素尺寸随宿主 Options 的 dpi 变化（回归锚点：`SharedEndpointsTests` 203 / 300 两档像素宽不同断言）。
