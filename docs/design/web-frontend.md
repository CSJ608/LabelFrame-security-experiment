# web 前端模块设计

> 覆盖：`web/src/`（根 / pages / lib / components / i18n / state，约 68 代码文件 + 58 测试）· 基线：`master` `7486dae`（2026-10-19）
> 分工与模板说明见 [README.md](README.md)；决策原文见 [DESIGN.md](../DESIGN.md) 决策表。

## 1. 职责与边界

**做什么**：LabelFrame 的全部 Web UI——模板设计器（画布编辑）、数据与打印、工作台 / 作业历史 / 在线设备 / 下载中心 / 插件管理 / 设置 / 帮助 / 新手引导，以**同一前端工程双构建**产出两种形态（决策 #63）：`web/dist`（client，Client MSI 内嵌、WinHost 静态托管）与 `web/dist-server`（server，服务端管理界面插件、`Server.WebUiPath` 托管，决策 #62）。双构建由构建期常量 `VITE_UI_MODE` 分支（`lib/uiMode.ts`），非运行时配置。前端是纯 API 客户端：不持有持久化、不做打印执行、不渲染打印位图（出图一律请求后端渲染端点）。

**不做什么**：

- 打印渲染 / ZPL 编码——后端 `render-image` / `render-images` 端点（Rendering 模块）；前端画布只负责设计态预览（Konva）；
- Excel 解析——后端 `POST /api/import/excel`；前端只做列映射建议（`lib/excel/mapping.ts`）；
- 模板 / 作业 / 设备的存储与调度——Server / WinHost；前端经 `lib/api/client.ts` 双 base（`serverApi` 服务端地址 / `localApi` 页面来源＝本机 WinHost）消费；
- 传输连接 / 打印机驱动的执行——本机 WinHost；前端设置页只发起 `POST /api/transport`（先测试后生效）；
- 安装链（Bootstrapper / MSI）界面——WinForms / WiX 体系，与本模块无关。

## 2. 内部结构

| 分区 | 构建块 | 角色 |
|---|---|---|
| 根 | `main.tsx` | 入口：i18n 模块加载即同步初始化（先于 React 挂载）→ 全局错误监听 → `ErrorBoundary` + `StrictMode` 包裹挂载 |
| 根 | `App.tsx`（Shell） | 应用壳：左侧主导航 + 底部状态栏 + 日志抽屉；`switchTab` 统一切页入口（透传设计器离开守卫）；`useHashRoute` 路由接线；`ROUTE_ALLOWED` = 本构建 tab 集 |
| state | `AppContext.tsx` | 全局状态：连接探测（服务端 `connected` 与本机 `localServiceUp` 各自独立）、机器级配置（deviceId / deviceName / ips / version）、传输配置、状态栏消息、日志（上限 300 行）、`printDraft` 草稿、默认目标设备 |
| state | `draft.ts` | DataPrint 会话草稿纯逻辑（可注入存储）：`selectedName` / 按模板分键的字段值与 dirty 键 / `debugMode` / `jobId`；`mergeDraftValues` 按 key 存在性合并（用户清空不被 testData 顶回） |
| lib/api | `client.ts` / `types.ts` | fetch 封装（双 base、超时分档 normal 30s / heavy 120s、超时与网络错误归一化、`ApiError`）；`serverApi`（业务 API + 客户端 / PDA / 插件包分发 + IPv4 候选）与 `localApi`（transport / printer / host config / print-settings / 插件安装卸载 + 单机降级业务 API）；`types.ts` 为后端契约 DTO 镜像 |
| lib/router | `router.ts` | 自研 hash 路由（决策 #177）：`parseHash`（含旧 `#dc=` 折算）纯函数 + `useHashRoute` hook（pushState 入栈带序号戳 / replaceState 不入栈 / 守卫挂起 pre-revert 按戳差回退） |
| lib/tabs·uiMode | `tabs.ts` / `uiMode.ts` | 主导航元数据单源（`SERVER_TABS` / `CLIENT_TABS`，供导航渲染 / 路由白名单 / 帮助页集三处派生）；构建期 `isServerUi` |
| lib/design | `geometry.ts` | 几何换算与常量（`PX=4` 即 1mm=4px、`PAD_MM=10`、`RULER=20`）、拖动落点提交 `commitDragPosition`（模型盒域）、多选刚性补丁 `multiDragRigidPatches`、视口坐标换算（含页面缩放修正 `viewportToLogic`） |
| lib/design | `snapping.ts` | 吸附纯函数：`elementModelRect`（按模型 mm 计算的元素盒，宽高钳制 `max(2, pxv(w))`）、`unionModelRect`（多选联合盒）、`othersModelRects`（候选排除）、`computeSnap`（边缘 / 中心参考线，阈值 8px，无目标网格兜底 1mm） |
| lib/design | `history.ts` | 快照式撤销 / 重做栈（JSON 序列化快照，容量 100） |
| lib/design | `convert.ts` | 设计器内部模型 ↔ 后端模板契约双向转换：`toBackendElement`（严格镜像后端 converter 的省略规则——非默认值才写字段）、`fromBackendElements`（region 按是否被锚定判别容器 / 矩形）、`applyContractDisplayNames`（契约显示名回填元素，往返不丢） |
| lib/design | `types.ts` / `model.ts` / `fields.ts` | 7 型元素模型（Text / Barcode / QrCode / Rect / Image / Line / Region）与默认值、内容态判别（`elementContentState`，不依赖本地化字符串比较）、容器锚定重算（`containerRegionIdFor`：中心点落入 Region 即锚定）、契约字段自动推导（`deriveFieldInfos`，字段填充元素按序去重、显示名取首个非空） |
| lib/design | `barcode.ts` / `imageCompress.ts` / `format.ts` | 条码 / 二维码 canvas 渲染（JsBarcode CODE128 / qrcode-generator，未绑定占位框）；上传图片浏览器内压缩（>1MB 降采样长边 ≤2000px + 重编码 JPEG q0.85 / BMP→PNG，产物仍超限且无透明通道才转 JPEG）；设计 JSON 交换格式（`labelframe-web-design` v1）导入导出 |
| lib | `transport.ts` | 连接方式的显示与参数逻辑：模式 / 插件标签（随语言）、旧模式 ↔ 插件 id 映射、徽标格式化、插件参数 spec 工具（条件可见性 `isSpecVisible` 与后端逐条对齐） |
| lib | `pluginText.ts` | 插件自描述文案展示端翻译（决策 #192）：有键且 pluginLabels 域有词条 → 当前语言词条、否则原文回退；键由插件侧声明（spec `labelKey` / `hintKey` 与接口 DIM），本模块不拼键；插件管理页包名映射 `pluginPackageName` 按 pluginId 查词条（manifest 键通道待「插件包内嵌词条」机制） |
| lib | `guide.ts` / `help.ts` / `helpLink.ts` | 新手引导纯数据（五步、localStorage 首见标记 `labelframe.guide.client.v1`，决策 #170）；帮助体系 V3 纯数据（`HELP_PAGES` 八页、演示预设 `HELP_DEMO_PRESETS`、深链示例元素 `HELP_LINK_ELEMENTS`，决策 #176 / #178 / #179）；深链页内轮询 hook（80ms / 4s） |
| lib | `settings.ts` / `clipboard.ts` / `connection.ts` / `download.ts` / `errorLogging.ts` / `jobStatus.ts` / `pluginLimits.ts` / `update.ts` / `deviceDisplay.ts` / `excel/mapping.ts` | 服务端地址 localStorage 兜底；剪贴板降级（Clipboard API → execCommand / 一次性 paste 捕获）；下载中心默认地址选择；blob 下载；全局错误 console 留痕；作业状态文案 hook；插件包 64MB 预检；客户端检查更新版本比较；设备显示名回退 |
| i18n | `index.ts` / `errorMessages.ts` | i18n 基座（决策 #165）：14 个域命名空间、zh-CN 源语言 + en 全覆盖（vitest 断言）、`fallbackLng`/`fallbackNS: common`、语言切换单点 `changeLocale`（持久化 + `<html lang>` / `<title>` 同步）；LF_* 错误码展示端翻译（zh 直接后端 message，en 已知码按码表插值、参数不全回退后端中文，决策 #166） |
| components | `Modal` / `Popover` / `HelpDot` / `Guide` / `Icon` / `TransportPanel` / `ErrorBoundary` | 通用组件：模态框（Esc / 遮罩关闭）；锚定弹出菜单（portal + fixed，防 `overflow:hidden` 裁剪——决策 #152）；「?」就地文档点（受控气泡，与帮助文章同源词条）；分步气泡引导（spotlight box-shadow 挖洞，自研）；内联 SVG 图标集；设置页连接方式完整面板；渲染异常错误页 |
| pages | `Designer.tsx`（586 行编排壳）+ `designer/` 组件与 hooks | 设计器页：顶栏 / 左栏（控件 / 字段 / 图层）/ 画布 / 右栏（属性 / 测试数据）三栏 + 三个 Modal（覆盖确认 / 离开三选 / 快捷键）；编排已拆 hooks——`useDesignerActions`（元素操作）/ `useHelpLinkDraft`（深链演示）/ `useImageAssets`（图片资源）/ `useTemplateSave`（保存）/ `useLeaveGuard`（离开保护）＋ `designState` / `images` 纯逻辑（机制层 lib/design/ 不动）；打印 DPI＝客户端全局口径（预览按设置页生效值缩放、无设计器局部 DPI 控件）；画布组件 `CanvasViewport`（react-konva：拖动高频操作直走 Konva 实例、dragend 才提交状态）、`ElementNode`（元素声明式渲染，图片三态）、`PropsPanel`（分组属性 + 文档点 + 演示插槽）、`SidePanel`、`shortcuts` |
| pages | `DataPrint.tsx` / `Workbench.tsx` / `JobHistory.tsx` / `Devices.tsx` / `Settings.tsx` / `DownloadCenter.tsx` / `PluginPackages.tsx` / `Help.tsx` / `WorkbenchPreview.tsx` / `useTemplatePreview.ts` | 其余八页：数据与打印（表单 / 调试出图 / Excel 映射 / 作业进度）、工作台（卡片网格 + 预览缩略图缓存）、作业历史（行展开明细）、在线设备（server 专用）、设置（分节左菜单＋右内容——帮助页文档站版式同构，菜单项样式与 help-toc-item 分组单源；右区仅渲染选中分节且不套卡片、连接方式内容常显无折叠、帮助深链锚点映射分节自动选中）、下载中心（三 tab，sub 段承载）、插件管理（server 专用）、帮助页（左目录树文档站版式） |

**双构建页集差异**（`lib/tabs.ts` 单源）：client 六页（workbench / designer / data / jobs / settings / help）；server 八页（+devices / packages / plugin-packages，−settings）。路由白名单、帮助页集、导航渲染均从 tab 集派生，页集增减单点跟随。

## 3. 关键流程

### 3.1 启动与连接探测（main → AppContext）

1. `main.tsx`：import `./i18n`（模块加载即完成 i18next 初始化 + 语言探测 + `<html lang>` 对齐）→ `installGlobalErrorLogging()`（`window.onerror` / `unhandledrejection` console 留痕）→ 挂载；
2. `AppProvider` 挂载（client 构建）：`localApi.getHostConfig()`（机器级配置）→ 成功则 `serverUrl` 生效（`setServerBaseUrl` + 内存态），失败（旧客户端无此端点）回退 localStorage → `configReadyRef` 置位后才放行首次服务端探测（探测串行化，消除默认地址与实际地址交错的双态竞态）；并行拉 `localApi.getTransport()`；
3. server 构建跳过本机配置（`serverApi` base 恒同源相对路径 `''`），直接探测；
4. `Shell` 挂载后每 10s 周期探测：服务端 `healthz`（`serverApi`，5s 超时；`serverMode` = server / standalone）与本机打印服务 `localApi.healthz()`（client 构建状态栏「本机打印服务」数据源）各自独立；地址为空（client 未配置服务端）不发起网络探测、直接判 standalone——空串 base 落同源会打到本机 WinHost `/healthz` 200 谎报「已连接」；
5. 在途探测以序号（`probeSeqRef`）作废：地址切换或判空后，旧探测结果不覆盖新终态。

### 3.2 hash 路由与导航守卫链（决策 #177）

1. `switchTab(id)` 是全部切页统一入口：目标即当前页直接放行；当前在设计器且 dirty 时交其注册的守卫——守卫返回 `false`＝弹三选 Modal（保存并离开 / 放弃更改 / 继续编辑）挂起，返回 `true`＝已直接放行（`setTab` 已执行不得回退）；
2. tab → hash：守卫放行后 `setTab` 触发 `useHashRoute` 的同步 effect，`pushState` 入栈（state 写序号戳，戳与栈位置严格对齐）；新入栈条目 sub 清空（主导航重进 = 页内次级回默认）；
3. hashchange（后退 / 前进 / 手动改 URL）：解析落点页，与当前不同则经 `requestPage`（即 `switchTab`，继承守卫）切页；守卫挂起时按「当前条目戳 − 落点条目戳」`go(反向步数)` 精确回退（pre-revert，页面与 hash 均不前进）；无戳落点降级为 URL 重写；
4. sub 段（页内次级，如下载中心页内 tab、帮助深链 `#/designer/<anchor>`）：`setSub` 走 `replaceState` 不入栈；`parseHash` 兼容旧 `#dc=<v>` 分享链接折算为 `#/packages/<v>` 再按构建白名单裁剪。

### 3.3 设计器编辑与保存链

1. 画布交互（`CanvasViewport`）：拖动 / 缩放过程中的高频节点操作直走 Konva 实例（不触发 React 渲染），`dragend` / `transformend` 才提交；
2. 吸附全程走**模型盒域**（决策 #180）：`elementModelRect` 按模型 mm 纯函数计算（脱离 Konva 渲染包围盒——`getClientRect` 会把居中描边半线宽计入盒外）；多选拖动为**整体刚性**（决策 #182）：dragStart 快照各选中元素初始坐标，`computeSnap` 以联合盒为被拖盒、组内元素零参与，Δ = 主导终态 − 主导初始，各元素补丁 = `commitDragPosition(初始 + Δ)` 同 Δ 落库（松手瞬移防御见 §6）；
3. 提交经 `Designer.commit` → `historyRef.commit`（快照入撤销栈）→ `setState`；`stateRef` 为同步真相（事件回调内先更新再 setState）；
4. 保存：`deriveFieldInfos` 推导契约字段（决策 #37）→ `toContract` / `toLayout`（`toBackendElement` 严格镜像后端省略规则）→ 图片资源仅提交被元素引用的 key（孤儿 GC）、剥 dataURL 前缀、恒发显式字典（决策 #184）→ `biz.saveTemplate`（业务 API 跟随 serverMode：unknown 不发请求待探测，server = `serverApi`、standalone = `localApi`）；
5. dirty 判定 = 撤销栈 `undoCount > 0`（任何已提交更改）；未保存离开经三选守卫（§3.2）；图片资源表不进 `DesignState` / 历史快照（防 base64 撑爆撤销栈）。

### 3.4 数据与打印提交链

1. 模式判定（client 构建）：进入页面 `serverApi.listDevices()` + `localApi.getHostConfig()` 并行探测——设备列表可达且本机 deviceId 在列且在线 = `server` 路由（作业经服务端投递）；未注册 / 离线 = `direct`（降级本机 WinHost 直连）；无设备列表 = 单机 `standalone`；每 10s 随健康探测刷新（server 构建恒服务端模式）；
2. 表单值 = `mergeDraftValues(testData, 用户值, dirty 键)`（决策 #51 草稿语义）；显示值标签一律 `displayName || key` 回退；
3. 提交：server 构建 / client 服务端路由 = `templateName` 引用服务端模板库 + `targetDeviceId` 定向投递（决策 #52 契约）；降级直连 / 单机 = 自包含 `template`（旧 WinHost 兼容）；`requestId` 用 uuid v4（`crypto.randomUUID` 是安全上下文专属 API，HTTP 局域网访问时 undefined，见 §6）；server 构建提交前现拉设备列表核对在线（不复用缓存）；
4. 调试模式：打印按钮改为后端渲染出图下载（单张 PNG `render-image` / 批量 zip `render-images`，heavy 超时档），不建作业；「图片预览」实时取当前字段值渲染图页内弹层呈现（预览与下载分离）；
5. 提交后 `jobId` 写入草稿（切页保留），`JobPanel` 1.5s 轮询 `getJob`（终态停止、失败 2s 退避）；本机直连作业有逐张 `items`（逐张重试），服务端下发作业只有汇总（`failedItems` / `errorMessage`）。

### 3.5 错误与文案渲染链（i18n）

1. 后端错误统一 `ApiError`（code + 展示 message + 可选 fieldKey / params）；传输层失败归一化为 `TIMEOUT` / `NETWORK_ERROR`（消息按目标区分「服务端」/「本机客户端」）；
2. `resolveApiErrorMessage`（决策 #166）：zh-CN 直接采用后端中文 message（中文权威在后端）；en 已知 LF_* 码按 `errorCodes` 域码表插值渲染，模板参数不全或未知码回退后端中文——任何输入不出现裸 `{{key}}` 残缺；
3. 组件内文案经 `useTranslation('<域>')` 订阅（语言切换即时重渲染）；**异步闭包**（轮询 catch、请求失败分支）不捕组件 `t`（语言切换后闭包过期），改读 i18next 单例 `i18next.t('<域>:<key>')`（构造期即当时语言）；
4. lint 防线：`web/.oxlintrc.json` 自定义规则 `labelframe/no-bare-cjk-jsx` 拦截裸中文 JSX（防新增文案绕过 key 化）。

## 4. 对外契约与依赖

**对外接口面**（本模块是 UI 叶子，无下游代码消费方；「对外」= 构建产物与 HTTP 消费契约）：

- 构建产物：`web/dist`（client）与 `web/dist-server`（server）——由 CI（`pnpm lint / test` + 双模式构建）产出，WinHost 静态托管 dist（`WinHostApp.cs`，探测 `web/dist` 未找到仅提供 API）、Server 经 `WebUiPath` 插件目录托管 dist-server（`ServerPluginUi.cs`）；
- HTTP 消费契约：`lib/api/types.ts` 是后端契约 DTO 的前端镜像（对齐 [archive/FRONTEND-SPEC.md](../archive/FRONTEND-SPEC.md) 历史交付规格，现行以后端契约为准），覆盖 healthz / 模板 CRUD + 导入导出 + 预览 / 作业提交与查询 / 设备目录 / 日志 / Excel 导入与模板下载 / transport / printer / host config / print-settings / 客户端与 PDA 与插件包分发 / 插件安装卸载；
- 构建期约定：`VITE_UI_MODE`（`server` | 缺省 `client`）、vite dev proxy（`/api` + `/healthz` → 53961（server 模式）或 53960）；
- 前端产物自检：release 工作流按构建特征串断言双产物区分（决策 #150）——改动页集 / 特征文案须同步自检断言。

**依赖**：React 19 + react-dom；react-konva 19 + konva 10（画布）；react-i18next 17 + i18next 26；jsbarcode（CODE128）、qrcode-generator（二维码）；uuid（requestId）；构建 vitest + jsdom + oxlint + @testing-library/react（测试断言边界 = 组件 onChange 回调与画布同数据源，决策 #82）。

**被依赖**：WinHost（托管 dist、提供 localApi 全部端点 + 单机降级业务 API）、Server（托管 dist-server、提供 serverApi 端点）；测试 = `web/src` 内 58 个 `*.test.*`（单测 / 组件测 / 双模式构建测，不跨仓）。

## 5. 设计取舍（决策索引）

| 主题 | 决策 |
|---|---|
| 前端工程化（Vite + React + TS + Konva、原型冻结） | #40 |
| 设计器 2.0 交互（多选 / 手柄缩放 / 吸附 / 快捷键） | #38 |
| 容器控件（拖入自动锚定，Region 后台化） | #36 |
| 契约字段自动推导（字段集合 = 字段填充元素去重） | #37 |
| previewValue（字段填充预览值）+ testData 后端派生 | #45 |
| 文本垂直对齐契约（heightMm / verticalAlign，与渲染端同源） | #46 |
| 元素契约第二批字段（wrap / lineHeight / fitMode / fontFamily / qrEcc / qrMargin / displayValue） | #47 |
| 加粗契约（bold，ZPL 字体变体 / Skia fontStyle） | #49 |
| DataPrint 会话草稿（sessionStorage 保留） | #51 |
| 服务端 / 客户端拆分（templateName + labels 契约） | #52 |
| UI 归属反转（Client 托管完整 Web UI） | #53 |
| 插件自描述文案展示端翻译（spec 键 + 接口 DIM，pluginLabels 查词条回退原文） | #192 |
| 机器级 ServerUrl（/api/host/config，优先于 localStorage） | #57 |
| 服务端管理界面插件形态（WebUiPath 运行时检测） | #62 |
| 前端双构建模式（VITE_UI_MODE，client / server 双产物） | #63 |
| connection.json 兼容演进（旧模式 ↔ 插件 id 映射） | #69 |
| Excel 模板下载（显示名表头 + testData 示例行） | #70 |
| 测试体系（onChange 为断言边界） | #82 |
| 工作台信息架构（卡片网格 + 作业轮询） | #103 |
| 错误页口径（仅文案 + 重新加载） | #106 |
| 区域锚定按实测文本宽度（前端画布与渲染度量一致性） | #110 |
| 下载中心设立（pda-packages + 统一页） | #119 |
| 客户端检查更新提示闭环（MSI 文件名版本比较） | #126 |
| 界面文案用户化原则 | #138 |
| 「PDA 日志 / 设备日志」页下线 | #140 |
| 前端产物自检特征（双模式区分断言） | #150 |
| 模板直达打印 + ⋯ 溢出菜单（首个 Popover，portal + fixed） | #152 |
| 删除类操作警示规范（实心红 + 确认弹窗） | #161 |
| 多语言支持范围（zh-CN 源 + en，打印内容不进范围） | #164 |
| Web 前端 i18n 地基（react-i18next、域组织、key 惯例） | #165 |
| API 错误消息多语言链路（params + LF_* 码表） | #166 |
| 下载中心改版（三 tab + 连接信息卡） | #169 |
| 新用户首次使用引导（spotlight、localStorage 首见） | #170 |
| 帮助体系 V2 撤回（转 V3 方向） | #171 |
| 品牌 v02（UI 主题色不随品牌色） | #172 |
| 帮助体系 V3 定稿（帮助页 + 文档点 + 实时演示） | #176 |
| 前端路由地基（hash 路由、自研不引库） | #177 |
| 帮助 V3.1 观感返工（气泡去标题 + 目录树 + 深链开草稿） | #178 |
| server 管理界面纳入帮助体系 | #179 |
| 吸附与提交链统一模型盒域（描边外扩不传染 mm） | #180 |
| 多选拖动提交语义统一（跟随同位移 + 锚定重算） | #181 |
| 多选拖动整体刚性（联合盒吸附 + 同 Δ） | #182 |
| 松手瞬移修复（纯提交段 + 假拖动防御） | #183 |
| 图片控件（上传持久化 + 单色统一 + 缺资源显式报错） | #184 |
| 插件 spec 条件可见性（字段集按连接类型切换） | #186 |
| 源码注释 rubric（禁过程标记） / 注释极简化（本文档体系） | #187 / #189 |
| 图标承形色品牌橘 #C9553B（深浅背景双态可访问） | #190 |
| 界面审查修复（测试打印媒介尺寸 / 参数表单行层级 / 设计器空名校验） | #197 |

## 6. 平台坑与不变量

修改本模块前必读（源码中对应位置保留白名单注释）：

1. **模型盒域不变量**：吸附候选、拖动落点提交、多选联合盒全部走 `elementModelRect` / `commitDragPosition`（按模型 mm 纯函数计算）——不得改用 Konva `getClientRect`（渲染包围盒把居中描边半线宽计入盒外：线每侧 +0.25mm / 边框 +0.15mm，会传染 mm 坐标，用户可感知的 ~0.2mm 对齐偏差即源于此，决策 #180）。宽高镜像 `ElementNode` 渲染下限钳制 `max(2, pxv(w))`。
2. **多选刚性口径**：Δ 基准 = dragStart 快照（非逐步差分），补丁 = 各元素 `commitDragPosition(初始 + Δ)`；`Transformer._proxyDrag` 触发的「假拖动」节点事件一律忽略（否则同一次拖动产生第二步历史、破坏撤销单步，决策 #183）；dragend 只取末次吸附后主导位置（对松手瞬间节点扰动免疫）。
3. **页面缩放修正**：`viewportToLogic` 必须先除掉 `rect.width / clientWidth`（浏览器页面缩放 125% 时 `getBoundingClientRect` 同比放大而 `clientWidth` 不变，实测 125% 下落点偏差 ~35%）——Konva `getPointerPosition` 已内置此修正，HTML5 DnD / 原生 click 路径必须手动做。
4. **空 base 不可冒充同源**：client 构建服务端地址为空 = 未配置——不得探测 / 拉数据（空串 base 落同源会打到本机 WinHost `/healthz` 200，谎报「服务端已连接」）；server 构建 base 恒 `''` 是同源服务端自身，语义不同（`lib/api/client.ts`）。
5. **`crypto.randomUUID` 是安全上下文专属 API**：HTTP + 局域网 IP 访问 server UI 时为 undefined 直接抛 TypeError——requestId 一律用 uuid 包 v4（不可用时回退 `crypto.getRandomValues`）。
6. **旧客户端兼容义务**（client 页面须同时服务旧 WinHost）：`/api/host/config` 404 → localStorage 兜底；`/api/transport` 无 `availablePlugins` → 回退内置 4 模式；提交降级自包含 `template`；`TransportApplyRequest.params` 后端字典为 string→string，Int / Bool 必须序列化为字符串。
7. **下载文件名解析**：ASP.NET Core 对非 ASCII 文件名同时发 `filename=`（ASCII 回退）与 `filename*=UTF-8''…`（RFC 5987）——优先解析 `filename*` 并 `decodeURIComponent`，畸形回退 `filename=`（`parseDispositionFilename`）。
8. **错误码插值安全**：en 码表模板带参但后端未提供 `params` 时必须回退后端原文，不得输出裸 `{{key}}` 残缺（`errorMessages.ts`）。
9. **`previewTemplate` 必须携带空 JSON 体 + 显式 Content-Type**：旧版 Server 对无 Content-Type 的裸 POST 按路由不匹配返回空体 404（真机验收实证；新版宽容、旧版不宽容）。
10. **异步闭包文案读单例**：组件 `t` 在语言切换后闭包过期——轮询 catch / 请求失败分支等异步路径一律 `i18next.t('<域>:<key>')`；非 React 模块（lib 层标签）改为函数调用期读取。
11. **localStorage 显式 `window.localStorage` + typeof 守卫**：Node 26 实验性全局 localStorage 会遮蔽 jsdom 注入版（测试环境坑）；隐私模式存取异常一律容错（语言偏好跳过持久化、引导标记视为未看过）。
12. **存储选型**：printDraft 用 sessionStorage（标签页天然隔离，会话级）；语言偏好 / 引导首见标记 / 默认目标设备用 localStorage（跨标签页、持久）——语义相反不可互换。
13. **`Object.hasOwn` 防原型链属性名**：路由 sub 段进入 `HELP_LINK_ELEMENTS` 等 Record 查找前必须 `Object.hasOwn`（`'toString'` 等原型链名经 `in` 判定为真、取值非数组致渲染崩溃）。
14. **图片上传链不变量**：限制按入库（压缩后）字节判定（单图 2MB / 10 张 / 总量 10MB）；>1MB 先走压缩管道，产物仍超限才拒收；PNG 含透明通道不可转 JPEG（`pngAlphaKept` 给针对性指引）；资源 key 净化 `\p{L}\p{N}._-` 与后端 zip `SanitizeKey` Unicode 感知口径对齐（中文文件名保留）；保存恒发显式 images 字典（有图非空 / 无图空字典）。
15. **esc / 关闭语义并存边界**：Modal / Popover / HelpDot / Guide 的 Esc 均为 document 级监听、同键并存时同帧双响应——既有先例边界，不做监听互斥（Guide 激活期遮罩已拦截常规路径）；Popover 页面滚动即关闭（fixed 定位不随锚点滚动）。
16. **文件 BOM（utf-8-sig）**：本模块源文件以 utf-8-sig 读写——编辑工具不得剥 BOM。
