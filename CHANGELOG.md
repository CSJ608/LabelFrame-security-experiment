# Changelog

本文件记录每个迭代的变更。

## 功能：前端路由地基——hash 路由全页面接入＋浏览器历史栈（#312） · 2026-10-11

- **hash 路由**：自研路由 hook（约百行，不引第三方库，循决策 #170 自研先例）——URL hash（#/<page>/<sub> 两级形态，sub 段为帮助深链预留、本轮由下载中心页内 tab 首用）成为页面位置载体：client 与 server 双构建全部页面 hash 与当前页双向同步；打开带 hash 的 URL 直达对应页、刷新停留在当前页。
- **浏览器历史栈**：应用内切页入历史栈，浏览器后退 / 前进在应用页面间导航（不再一步离开应用）；设计器未保存离开守卫照常生效——守卫取消时页面与 hash 均不前进且不入栈（按历史条目序号精确回退）。
- **server 构建一致接入**：server 页面集同形态路由，受限页集不可经 hash 越权进入（非法 / 越权 hash 回退默认页并规范化 URL）；下载中心页内 tab 从旧 #dc= 形迁至 #/packages/<sub>（旧 #dc= 分享链接兼容直达）。
- **评审阻断修复（PR #313 修复轮）**：旧 `#dc=` 兼容映射先折算为正典形 `#/packages/<v>` 再走同一构建白名单裁剪——client 构建旧链接同样回退 workbench，不再构成越权旁路；守卫取消 pre-revert 的序号戳与栈位置严格对齐（入栈戳＝来点条目戳＋1，back 截断前向历史后重推不跳号）——「back 放行后再切页、守卫取消」截断序列下 hash 与页面仍一致回退。
- 后端 / 宿主 / CI 治理面零改动；无界面文案变化（决策 #177）。

## 功能：帮助体系 V3——帮助页文档底座＋组件级「?」就地文档＋内嵌实时效果演示（#308） · 2026-10-10

- **帮助页**：主导航新增第 6 项「帮助」（客户端构建，服务端管理界面不挂载）——功能索引＋五个界面的分界面文章（工作台 / 设计器 / 数据与打印 / 作业历史 / 设置），每篇＝页面导览段＋主题小节；文章主题与界面内文档点词条同源（单源双视图），有文档点的小节支持「去界面查看」深链跳转并自动弹出对应文档点（锚点轮询超时给通用降级提示）。
- **组件级「?」文档点**：设计器属性面板 10 个分组与设置页 7 张卡片标题新增「?」按钮（data-help 语义锚点），点击弹出就地文档气泡，讲清组内各字段作用与生效边界。
- **内嵌实时演示**：设计器「填充」组文档点内嵌「看实时效果」——临时注入示例面板状态、画布实时反映、退出自动还原；演示期间面板输入与画布编辑禁用；零模板保存 / 零写请求 / 零跨页。
- **UI 精简（保守迁移）**：被文档点覆盖的教学性括号提示迁入文档——设计器 12 条分组与字段标签精简（如「字段名（打印时用数据填充）」→「字段名」）、3 条常驻提示行（单行溢出 / 字段填充 / 兼容图片说明）与设置页 4 条常驻教学提示（语言 / 服务端地址 / 插件 / 连接方式）移除，信息不丢失（文档点可查）；带插值与条件渲染的状态提示按拍板留界面维持现状。
- **i18n**：新增 help 域语言包（zh-CN 源＋en 全量、语义 key、双 JSON＋i18n 注册＋覆盖断言三处同步）。

## v0.33.0 迭代 122～125 汇总发布 · 2026-10-09

- **打包范围**：v0.32.0 之后合入 master 的全部迭代与修复——品牌：迭代 122（品牌资产 v02 全端统一接入，#288，决策 #172）；可运维性：迭代 123（日志保留量闸——文本单文件大小上限＋logs.db 按量闸与空间回收，#291，决策 #173）与首清时机修复（#296：升级/重启后清理即时生效而非等 24h）；体验：迭代 125（产品面信息精简——部署包与用户文档去除内部过程标记，#301，决策 #175）；工程内部（不随包出库）：迭代 124 管线标准件泛化（#294，决策 #174）与 lf-implement 加固（#301 运行实证）。详见各条目。
- **本版可感知变化（品牌）**：LabelFrame v02 视觉全端统一——Web favicon 与导航 logo、Windows exe/窗口/托盘/快捷方式与双 MSI 向导位图及 ARP 图标、Android 启动图标 18 件（含 Android 13+ 主题单色层）、README 头图；UI 主题色不随品牌色变化。
- **本版可感知变化（日志治理）**：文本日志单文件默认 50MB 上限（超限自动轮转序号文件，总占用上界 ≈31 文件×50MB）；logs.db 默认 256MB 按量阈值（超阈自动删最旧并 VACUUM 真实回收磁盘，不受保留期下限约束）；服务启动后 60 秒即执行首次清理（升级/重启后立即生效）；均可经 LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB / LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB 环境变量调整（0=不限）。
- **本版可感知变化（文档与部署包）**：离线部署包脚本、Docker compose、DEPLOY/README 等产品面文件全面去除内部过程标记（迭代号/Issue 号引用），仅保留部署、升级、排障必需信息；文档内部节引全部改节名文字引用。
## 修复：lf-* 长正文评论落稿 ENAMETOOLONG——改真实临时文件 --body-file（#308 运行实证） · 2026-10-10

- **根因**：#298 泛化后方案 / 草稿 / 定稿评论携带「界面文案清单」json 契约，正文超 Windows spawn 命令行 32K 上限，`gh issue comment --body <全文>` spawn `ENAMETOOLONG`（lf-design 落稿步实证——设计方案本体已完成、仅落稿失败，经 AmendWorkflow 缓存复放续跑恢复）。
- **修复**：lf-design / lf-copy / lf-copy-review / lf-close 四件的大正文评论统一改走 `postIssueCommentByFile`——node 分块追加写入（每块 12K，临时文件在仓库外 `../.lf-comment-<N>.md`）后 `gh --body-file` 提交并清理；lf-implement / lf-review 的评论为摘要级（历史几 KB 上限）维持直传。
- 管线内部修复（工程内部，不随包出库），产品代码零改动。

## 工程：lf-copy / lf-copy-review 文案段泛化——「界面文案清单」契约驱动（#298） · 2026-10-10

- **lf-design**：「界面文案清单」契约节升级——每条文案项声明槽位（name＋zh/en 字数上限，0=不限），需用户挑选的措辞另列候选组声明（name/desc/slots/count）；清单节新增 json 契约块（items＋candidates）供下游机器解析，prose 行保留可读性。
- **lf-copy / lf-copy-review**：从导览/帮助类专用（任务清单 A–H、行数 15~45、候选恰 3 组等硬编码）改为清单契约驱动——任务面、行集、槽位集、字数上限、候选组数全部来自方案；定量断言重设计为 checkContract（行集与清单恰好一致、槽位齐全且逐槽非空与上限、候选组数按声明），草稿/定稿的文案表与候选表按槽位动态渲染。
- **lf-implement**：文案判别补「方案清单 × 定稿形态」双检 fail-closed——方案有清单→定稿必须含 json 块（防漏跑文案段拿占位声明开工）；方案无清单→定稿不得含 json 文案块（防错配定稿）。
- 管线内部契约变更（工程内部，不随包出库），产品代码零改动；首个消费方＝帮助体系 V3（迭代 126，#308）。

## 治理：轮值协议 v8/v9 修订入库与残留清理（轮值停用档案完备） · 2026-10-10

- `.zcode/automations/` 两文件补入 2026-10-08 本地修订（当时未走 PR）：v8/v9「工作流接管」豁免条款（带该标签的 Issue 由工作流管线自治/陪验负责，轮值扫描一律跳过——9-21 曾加未入库散失，10-08 随工作流实验重启恢复）；两轮值已于 2026-10-09 停用，本入库使协议档案完整，未来重建轮值时即为现成基线；
- 顺手清理根目录 0 字节残留文件 `nul`（历史会话产物，早前已登记待清）。

## 修复：安装器构建模板注释精简（#303） · 2026-10-10

- `packaging/bootstrapper/Bundle.wxs`、`packaging/main.wxs`、`packaging/main-server.wxs` 三个 WiX 模板的过程标记注释清理（#301 拍板②挂账收尾）：迭代号 / Issue / 决策号 / § 节引 / AC / 返修出处全部去除（出处型括注直删，行为约束与非显然理由注释保留——CacheId 版本化机理、Permanent 维持原因、验签退出码等）；外部上游引用（wixtoolset/issues）保留。XML 注释不编入 MSI 产物不随包分发，本清理为仓库内部一致性（与 #301 同判据）。三文件复扫零命中、XML 解析完好。

## 修复：lf-implement 标准件两处加固（#301 管线运行实证） · 2026-10-09

- **PR 改动面断言并集扩产品面前缀**：`packaging/` `docs/` `scripts/` `README.md` 并入（`web/ ∪ src/ ∪ test/ ∪ .zcode/` 基础上）——#301（产品面精简迭代）实证纯产品面改动面会命中 fail-closed 拦截。
- **PR 创建步幂等**：重放 / AmendWorkflow 续跑时分支已有 PR 则回查既有 PR 继续（`already exists` 不再中断），其余失败照旧抛出。

## 迭代 125：产品面信息精简——随包分发文件与用户文档去除内部过程标记（#301） · 2026-10-09

- **产品面去过程标记**：随包分发文件与用户文档中的内部协作出处（迭代号 / Issue 号 / 决策号 / AC / § 节引 / PR salvage）全部隐去——`packaging/offline/install.sh`（头部说明与预建目录、自动分发两处注释去出处，行为理由保留）、`packaging/offline/README.md`、`packaging/ubuntu/Dockerfile`、`packaging/ubuntu/docker-compose.yml`（五处挂载注释收敛为纯用途说明）、`packaging/recheck-webview2.vbs`、`docs/DEPLOY.md`、`README.md`、`scripts/install-server-linux.sh`（注释与用户可见输出文案，按用户拍板纳入本轮）；`§N` 跨节引用改「节名」文字引用（引用与实际标题逐处核对一致；DEPLOY / README 两处含迭代括注的章节标题同步去括注，GitHub 自动锚点 ID 随之变化，旧外部链接可能失配）。保留注释逐条过 rubric（仅行为约束与非显然理由），纯过程叙述（契约出处、组包来路、「（迭代 NN，决策 #MM）」括注）删除。
- **执行逻辑零变化**：`install.sh` 仅注释行改动（剥离注释后与 master 逐字节一致，echo 输出本无标记零改动）；compose / Dockerfile / vbs 非注释行零改动；`install-server-linux.sh` 非注释变更仅 usage / die 输出字符串内的节引文字（命令、顺序、判断、退出码零改动，bash -n 通过）。
- **痕迹扫描与残留声明**：范围文件集（8 个改动文件 + 7 个零命中负对照：service / appsettings×2 / e2e×3 / linux-client Dockerfile）对固化命令（`grep -E "迭代 ?[0-9]+|[Ii]ssue ?#[0-9]+|决策 ?#[0-9]+|(^|[^A-Za-z0-9])AC-[0-9]+|§|PR ?#[0-9]+|salvage|#[0-9]+"`，AC 边界用 ASCII 显式形态以保证跨 grep 版本可复现）零命中，命令与输出见 PR；安装器构建模板按不在范围口径缓办（.wxs 的 XML 注释不编入 MSI 产物、不随包分发，与源码注释同逻辑），残留以同一固化命令本机实测声明：`Bundle.wxs` 38 行 / `main.wxs` 9 行 / `main-server.wxs` 6 行（共 53 行；设计段记录的 38/8/5 为其环境实测，行级计数随 grep 版本有 ±1 漂移，以各环境固化命令输出为准），随本 Issue 结项时开挂账 Issue 清理。
- **纪律决策**：`docs/DESIGN.md` 决策表新增「产品面文件禁内部过程标记」纪律行（决策 #175，用户拍板采纳），防后续迭代再引入同类标记。

## 修复：日志清理首清时机与告警链路（#296） · 2026-10-09

- **首清时机修复（#296 主体）**：`DataCleanupService.ExecuteAsync` 原「延迟 60 秒后等首个周期 tick 才清理」——默认周期 24h 下升级 / 重启后一天内不做任何清理（终态作业、日志按期删、logs.db 量闸全部延迟，注释与实现不符）；改为延迟 60 秒后**立即执行一次** `CleanupAsync` 再进周期循环（异常防护与周期执行同级）。
- **量闸异常态告警走 ILogger（#291 评审留观项②）**：`SqliteLogStore.EnforceSizeLimitAsync` 返回值从 `int` 改 `SizeLimitResult(DeletedRows, VacuumWarning)`——VACUUM 失败 / 表空仍超阈等异常态由 `DataCleanupService` 经 ILogger 记 Error（服务宿主可能无控制台，原 `Console.Error` 不可依赖）；新增「表空仍超阈」警告（提示阈值可能小于空库体积）。
- **文本日志清理权限异常容错（#291 评审留观项①）**：`FileLoggerProvider.CleanupOverRetained` 单文件删除 catch 从仅 `IOException` 扩至 `UnauthorizedAccessException`（Windows 只读属性 / ACL 拒绝只跳过该文件，不再中止该轮剩余清理）。
- **测试**：新增「表空仍超阈 → 警告非空、零删除」锚定 1 项；既有量闸测试适配 `SizeLimitResult`（含正常路径警告为 null 断言）；首清时机为时序行为（60s 延迟）不宜单测，以真实进程沙箱复验为证（见 Issue）。

## 迭代 124：管线标准件泛化——lf-* 六段从引导专用改通用（#294） · 2026-10-09

- **lf-design 泛化（六段结构 / 评论协议 / 双员评审 / 幂等守卫机制不变，只改任务口径与断言的「引导假设」）**：设计任务改「按 Issue 目标 / 范围 / AC 勘察仓库现状产出实施方案」，方案 schema 固定为目标回顾 / 现状锚点（文件:行号）/ 设计（逐 AC）/ 计划改动文件表 / 测试计划 / CHANGELOG 草稿 / 风险 / 待拍板项 +（仅含界面文案迭代必填）界面文案清单（供 lf-copy 消费的契约节）；产物编号 vN 递增——取号扫描用宽松首行正则 `/^\*\*📐 方案 v(\d+)/`（数字后不要求紧跟 `**`，#291 式错位存档标题「方案 v1（错位产出·仅存档）」计入防撞号）取在场最大 N 产 v{N+1}（只认首行：正文前瞻提及不虚增）；方案定位与幂等守卫另用严格正则 `/^\*\*📐 方案 v(\d+)\*\*/`——存档评论不算有效方案（评审阻断修复：取号误用严格版会致仅存档在场时 max=0、重跑产 v1 撞号）；勘察发现范围矛盾 / 前置缺失时改产「⚠️ 设计处置报告」（不匹配方案正则、不占 vN 槽）；幂等守卫从「存在精确 v1 前缀即退出」改 commentLastIndex 式新旧比较——最新方案评论晚于最新「🔧 PR 已建 / 🔨 修复轮」评论才退出，否则产 v{N+1}；独立方案评审员任务与 artifact 标题 / conclusion 同步通用化（conclusion 条件式：含界面文案起 lf-copy，无文案由主控贴占位声明后直接起 lf-implement）。
- **lf-implement 泛化**：任务模板改「按最新方案的『计划改动文件』与设计规格逐项落地、按 Issue AC 补测试、更新方案要求的记账文件」；文案句条件式（「✅ 文案定稿 v1」含 json 块 → 逐字使用；无文案占位声明 → 零文案执行）；方案读取 fail-closed——首行正则取最新「📐 方案 vN」评论，取不到即 throw（含最新评论为处置报告的场景），不静默降级；PR 改动面断言从强制 `web/` 改并集 `web/ ∪ src/ ∪ test/ ∪ .zcode/`（纯 docs 改动面命中失败为 fail-closed 显式信号而非静默）；slug 默认 `guide`→`iter`（metadata 与运行时两处）；`?? 280` 硬编码回退删除；文案守卫与「主控贴占位声明」四段轻装仪式保留（whenToUse 注明）。
- **lf-review / lf-close 适配**：review 对照基准改「最新『📐 方案 vN』评论（fail-closed：取不到即 throw）」+ 文案双形态口径（含 json 块 → 逐字一致为阻断口径；无文案占位声明 → 无文案基准），范围合规员 persona 改「对照方案设计规格逐文件核对、找实现与方案的偏离」；close 合并后校验 `git pull --ff-only`（动主仓工作区，多会话共用主仓时干扰占用会话）改 `git fetch origin master` + `git grep FETCH_HEAD`（不动主仓工作区，fetch 失败同级 fail-closed），本地分支回收后补远端分支删除 `gh api -X DELETE git/refs/heads/<branch>`（尽力而为不阻断——仓库开合并后自动删远端分支）；slug 默认两处 `iter`；needsHuman 语境改真机 / 长测 / 用户观感通用句式。
- **lf-copy / lf-copy-review 收窄（评审 B4：收窄而非泛化）**：两件写死面远超任务模板（必产清单 A–H、group 枚举、checkRows 定量契约 rows 15~45 / 候选恰 3 组均为 119/121 导览形态专属）——whenToUse 与 description 收窄为「导览 / 帮助类文案迭代专用（119/121 形态）；通用文案能力待后续 Issue 泛化」；仅修 B3 硬伤（建场守卫精确 `**📐 方案 v1**` 前缀改最新方案 vN 首行正则 fail-closed，防 vN>1 含文案迭代起跳即炸）与 `?? 280` 回退删除；全面泛化显式登记为后续 Issue（本 Issue 结项时开）。
- **测试与元数据**：六件 metadata（description 单行——saved 元数据折行坑）同步；tsc strict + 临时 facade 声明逐件类型检查六件全过；node 文本锚定直测 61 项断言全过（关键正则 / 断言字面量逐字在场——方案 vN 首行正则、`startsWith(".zcode/")` 并集、`FETCH_HEAD`、`git/refs/heads/` 等；四件无「新用户 / 引导」残留；六件 description 单行）+ 谓词行为样例（vN 递增含错位存档计入与正文提及不虚增、幂等新旧比较、改动面 web/src/test/.zcode 四形态与纯 docs 拒绝、CC_RE / CLOSE_RE——正则自源文本提取构造非副本）；评审阻断修复（取号扫描与方案定位/守卫拆用正则）后复跑直测 16 项全过，新增原缺敏感样例——#291 实际存档标题宽严两版匹配行为、仅存档在场→最大 1 不撞号（原「v1＋存档＋v3→最大 3」因 v3 在场对存档计入不敏感）、处置报告文案 v{N+1} 插值；DESIGN 决策 #174 记账。

## 迭代 123：日志保留量闸——文本日志单文件大小上限轮转＋logs.db 按量闸与空间回收（#291） · 2026-10-09
- **文本日志单文件大小上限**（默认 50MB，`LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB`，0=不限）：超限轮转序号文件（(日期,序号) 序清理、单日内亦受限），总占用上界 ≈ 31 文件 × 上限；「写入永不抛出」防护语义不变（DESIGN 决策 #173）。
- **logs.db 按量闸与空间回收**（默认 256MB，`LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB`，0=不限）：超阈值按大小删最旧（不受 90 天保留下限约束——有意行为），删后 `VACUUM` + `wal_checkpoint(TRUNCATE)` 真实回收磁盘；`auto_vacuum=INCREMENTAL` 因头删尾插 workload 无效被否决（依据入 #173）。
- **测试**：`FileLoggerProviderTests` 增补 4 项（超限切序号文件且旧文件封存、单日多次轮转总文件数 ≤ 上限、(日期,序号) 元组序清理且写入中文件不被选中〔含 `.10`/`.2` 构造〕、轮转失败回退旧文件通道不自禁用）；`DataCleanupServiceTests` 增补 4 项（KB 级阈值直测量闸——最旧前缀删除 + 合计口径空间回收 + 不受保留期下限、MB 级阈值经 `ServerOptions` 走 `CleanupAsync` 编排、量闸关闭时按期行为回归、两个新环境变量解析三态）。

## 迭代 122：品牌资产 v02 全端统一接入（#288） · 2026-10-09

- **品牌 v02（方向 04「剥离 / Peel & Feed」、Poppins Bold 转曲字标、深青 #123B3C + 珊瑚 #D96C4F）替换三套旧视觉**（Web「标签纸」蓝 #1a5fd0 / 桌面安装链「L 标」蓝 #1668DC / `web/public` 紫色模板残留），全端品牌资产单源于新入库母版库 `assets/brand/`（SVG 精简集 + `Poppins-OFL.txt` + `BRAND-GUIDE.md` + bootstrapper-lockup 源 + tray 三件备用），落库 41 项资产**提交 blob** 逐项 SHA256 与交付包 manifest 一致（`.gitattributes` 对母版库与交付源文本资产钉 `-text` 禁 eol 归一——Windows autocrlf 会把 CRLF 源归一成 LF 破坏字节级一致性，实证两个 JSON 首提交被归一后修复）；旧品牌生成脚本四件退役删除（`generate-icon` / `generate-installer-branding` / `generate-android-icons` / `generate-social-preview.ps1`，防再生成旧标）+ 旧 L 标残留 `assets/labelframe.png`（主色 #1668DC、零引用）删除，`docs/DEPLOY.md` 辅助脚本清单同步。
- **Windows 图标与安装链**：`assets/labelframe.ico`（147KB 多尺寸）原位覆盖——WinHost / Server csproj `ApplicationIcon`、窗口 `ExtractAssociatedIcon`、托盘（EXE 资源 32512 链路）、快捷方式与双 MSI ARP 一处替换全链生效；双 MSI 向导位图 `installer-dialog.bmp`（493×312）/ `installer-banner.bmp`（493×58）换新（wxs 按 `$(var.AssetsDir)` 路径引用，原位生效零改动）。
- **Android 资源 18 件**：10 个 mipmap 位图 + `mipmap-anydpi-v26`×2 覆盖 + **新增 `mipmap-anydpi-v33`×2（Android 13+ 主题单色层 `ic_launcher_monochrome`）** + drawable×3（foreground / monochrome / ic_stat）+ `colors.xml`（色名 `labelframe_icon_background`=#F2F5F3 替换旧 `launcher_background`=#1668DC，旧色名全仓清零）。
- **Web（AC-05）**：favicon 改**外链最小集**（拍板①）——`index.html` data URI → `favicon.svg`（主）+ `favicon.ico`（alternate）+ `apple-touch-icon.png` 三行外链，规避浏览器缓存旧 data URI；`web/public/icons.svg` 零引用模板残留下线（MSI 文件清单为构建期自动生成，public 增删自动反映）；导航 `LabelLogo` 以母版 `symbol-color-24.svg` 几何重写（24px 方形、深青标签体 + 珊瑚剥离角固定填充，不再走 currentColor/`--accent`——UI 主题色不随品牌色，决策 #172）；`.nav-logo` 样式适配。
- **引导 Bundle 品牌（拍板② spike 未走降级）**：`Bundle.wxs` 增 `IconSourceFile`（WiX v7 原生支持，构建期嵌最终 EXE；v5 窗体图标回归 wixtoolset/issues#8104 已于 v7.0.0-rc.1 修复）+ `build-bundle.ps1` 补 `AssetsDir` 接线；BA csproj 增 `ApplicationIcon`；`WizardForm` 窗体显式 `Icon`（ExtractAssociatedIcon，WinHost 同口径）+ 顶部 44px 品牌条（lockup 以嵌入资源携带，缺失降级不阻断安装；窗体高 560→604 保持内容区不变）。
- **README 头图 + 社交预览**：顶部品牌 v02 彩色 lockup（`assets/brand/lockup-color.svg`，alt="LabelFrame"）；`assets/social-preview.png` 换深底主版（拍板③；GitHub Settings 上传为用户手工动作，结项时提示）。
- **记账与守门**：DESIGN 决策 #172（品牌 v02 定稿与旧标退役、UI accent 不随品牌色、spike 结论）；新增 `Icon.brand.test.tsx`（LabelLogo v02 几何 / 固定填充 / 尺寸等比 + index.html favicon 外链三件与 data URI 移除断言）+ `Icon.brand.server.test.tsx`（server 构建导航 logo 守门——品牌标非 UI 模式裁剪面，双构建同源出新标）；本迭代无 UI 文案面，i18n 零改动。

## 迭代 120：工作流实验收尾——六标准件与评审基线入库＋引导测试防线补强（#278） · 2026-10-08
- **工作流基建入库（防「基建衰变三断点」重蹈）**：`.zcode/workflows/` 六段管线标准件（lf-design / lf-copy / lf-copy-review / lf-implement / lf-review / lf-close，含 Windows `world.run("pnpm")` 须用 `pnpm.CMD` 的平台修复）与 `docs/CODE-REVIEW-BASELINE.md` 评审基线随仓库版本化——迭代 119 复盘实证三类基建均处本地未入库状态必然随会话消散；平台踩坑要点记入提交信息：**AmendWorkflow 不携带 args**（saved workflow 草稿须自带固定回退值）、**链式 amend 经零结算中间 run 缓存全丢**（修复须一次改全再 amend，勿增量多轮）。入库后实证：六件 `description` 手工折行（YAML 多行纯量）会使 `CreateWorkflow` saved 元数据校验失败（`args.issue.required` 被读成字符串、起流被拒），合并为单行修复。
- **引导测试防线补强（PR #277 评审 6 条建议中的代码项）**：`Guide.test.tsx` 增 en 五步标题 / 正文 / 步骤按钮（Next ×4 / Done）与「✅ 文案定稿 v1」逐字断言——此前仅 zh 逐字、en 只断言 key 非空，AC-04 双语防线不对称；测试文件归位 `web/src/components/`（Guide.test.tsx / Guide.server.test.tsx 与被测对象同目录，既有断言零丢失）。
- **文档措辞收敛**：CHANGELOG 迭代 119 条目与 DESIGN 决策 #170 ⑤中「dist-server 产物不含引导」收敛为「server 构建不挂载 / 不渲染」——产物级裁剪未作断言，不再写未经产物验证的机制断言；DESIGN §7「帮助体系升级」条目更新为已转立项 #280（迭代 121，讨论已全部定案）。
- **管线运维修正（随治理 PR）**：lf-copy / lf-copy-review 扩容为「迭代全部文案面」（分组文案表 tour/help/demo/modal＋入口措辞 3 组候选、分组字数断言 60/130、行数 15~45、双 json 块解析）——迭代 121 首跑实证原模板只产导览表骨架；lf-implement 实施口径从「引导组件」泛化为按方案文件清单实施＋入口措辞采用拍板组＋args 回退；修复「🔧 PR 已建」评论时间戳 `[object Promise]` 插值缺陷（async tsIso 未 await）；`.gitattributes` 钉 `*.dwf.ts` eol=lf（CRLF 会使 saved workflow 元数据解析失败，#282 后续）。

## 迭代 119：新用户首次使用引导——Web UI 分步气泡引导（client 模式）（#276） · 2026-10-08
- **首次自动分步引导（形态与实现路线以 #276 拍板评论记入决策表 #170：分步气泡 spotlight、自研实现）**：client 构建（`web/dist`）首次进入（首见标记 `labelframe.guide.client.v1` 无值）挂载后延迟约 300ms 自动启动 5 步引导，覆盖核心链「工作台（新建模板）→ 设计器（空态新建入口，介绍排版）→ 数据与打印（模板选择器）→ Excel 导入 → 收尾（左侧导航回顾 + 重看入口指引）」；步骤定义为纯数据（`web/src/lib/guide.ts` `GUIDE_STEPS`：tab + `data-guide` 语义锚点 + i18n key），切页经既有 `switchTab` 统一入口（继承 #150 设计器离开守卫语义），且仅在步骤变更时驱动切页（引导打开期间用户自行导航不被拽回）；自研组件 `web/src/components/Guide.tsx`：createPortal 到 body + fixed 锚定（决策 #152① 同法）、遮罩 = box-shadow 巨影法 spotlight + 主色描边（无 SVG mask）、气泡下方优先 / 放不下翻转 / 贴边（Popover 视口约束同口径）、锚点超时或零矩形降级全屏暗幕 + 居中气泡；跳过 / Esc / 完成均写首见标记（决策 #170 ⑧⑨），引导中途刷新不写（首见语义未成立），刷新 / 重启 / 新标签页不再自动出现（AC-01/02）。
- **重新查看入口（决策 #170 ⑤）**：底部状态栏 meta 区「日志」旁常驻小图标 ghost 按钮「使用引导」（问号图标 + title/aria），点击无视首见标记再放完整一轮，重放完成 / 跳过不改变已看状态（写入幂等）（AC-03）。
- **server 模式整特性不挂载 / 不渲染**：引导组件与重看入口均以 `!isServerUi`（构建时常量）条件渲染，server 构建不挂载、不渲染引导（产物级裁剪未作断言；V1 范围仅 client，菜单结构差异留待后续迭代）；`Guide.server.test.tsx` 守门断言 server 分支不渲染。
- **存储对照（决策 #170 ③④）**：首见标记为「每浏览器一次」偏好型数据走 localStorage（键带版本号，未来改版换键即可整体重放）——#51「禁 localStorage」适用范围是会话草稿（printDraft 需标签页隔离故 sessionStorage），本标记语义相反不适用；与 #164 ④语言偏好（`labelframe.locale`）、默认目标设备（`labelframe.defaultTargetDeviceId`）同类先例；不新增后端 API，不走 #57 机器级 settings.json；读写显式 `window.localStorage` + typeof/try-catch 容错（Node 26 全局遮蔽坑、隐私模式降级为每次出现）。
- **i18n（AC-04）**：新 `guide` 域（zh-CN 源 + en 同步，语义 key），气泡文案与「使用引导」入口全部走 t()，步骤标题 / 正文以 #276「✅ 文案定稿 v1」逐字为准（单步正文 zh ≤ 40 字）；guide 域同步登记进 `locales.test.ts` 静态清单（静态 import + NAMESPACES，键集一致断言随既有 CI 防线覆盖新域）；`Guide.test.tsx` 另锚定步骤引用 key 在两语言包真实存在 + zh 定稿逐字比对。
- **样式与层级**：引导层 z-index 110（styles.css 原最高 .modal-mask=100，另两处 .preview-modal=95 / .popover=90 均低于此）压过全部弹层；气泡 / 按钮复用既有 CSS 变量与 `btn` / `popIn` 视觉语言；窄窗口气泡 `max-width: calc(100vw - 16px)` + 贴边翻转（AC-05 观感项陪验）。
- **测试**：新增 `Guide.test.tsx` 19 项（步骤定义顺序 / tab / 锚点 / 定稿 key 对应、zh 定稿逐字 + ≤40 字、双语词条存在、首见标记读写幂等、组件行为——首步渲染 / spotlight 矩形外扩 / 下一步切页推进 / 末步完成 / 跳过 / Esc、App 集成——首见自动启动 / 跳过与完成写标记 / 重挂载不再自动出现 / 5 步全链切页 / 重看入口重放）+ `Guide.server.test.tsx` 2 项（server 不自动启动、不渲染重看入口）；净增 21 项，`locales.test.ts` 12 域；既有壳层测试（App.client / App.langSwitch / i18n）以「已看过引导」用户运行（beforeEach 预置首见标记，首见行为归 Guide.test.tsx），App 壳层 / Workbench / DataPrint 存量用例全部通过。

## 迭代 118：下载中心改版——快速访问三 tab 与连接信息卡（#272） · 2026-10-08
- **下载中心三 tab（消费 / 管理分离，决策 #169）**：`DownloadCenter.tsx` 重构为页内三 tab——「快速访问」（默认）｜「Windows 包管理」｜「Android 包管理」；tab 状态写入 URL hash（`#dc=quick|windows|android`，replaceState 不新增历史记录，不动主导航既有机制），刷新 / 直链打开均还原当前 tab；视觉沿用仓库页内 tab 先例（`btn` + `active` 分段风格，不新造设计语言）。
- **快速访问首屏（拿包 + 连服务器一屏完成）**：上排 Windows / Android 各一张「最新上传」卡——货架语义按上架时间（修改时间倒序第一条）展示，管理员换货（含故意传旧版）即生效、删除后自动回退剩余最新一条；卡内含文件名 / 大小 / 上传时间 / 下载二维码（内容 = origin + 下载路径，放大约 150px）/ 下载按钮 /「全部版本 →」跳对应管理 tab，空态引导跳转上传；下区块「服务端信息」卡：大二维码居中（内容 = 选中的裸地址 URL，不做任何包装协议 / deep link）＋等宽地址文本 + 复制按钮（**陪验收口**：整区限宽 720px 居中防卡片随屏宽拉伸、双区块标题「客户端」/「服务端信息」替代原 ①② 步骤眉标、连接卡改与下载卡同款 panel 背景消除割裂感）。
- **服务端新增只读接口 `GET /api/server/ipv4-candidates`**：枚举本机 IPv4 候选（仅启用网卡的非回环地址，去重；私网（RFC1918）优先排序——参照 WinHost `LocalIpAddresses.cs` 先例独立实现，Server 不引用 WinHost 工程）；前端默认取与当前 origin 匹配者，origin 为回环地址（localhost / 127.0.0.1 / [::1]）时回退首个候选（不产生 localhost 废码），**其余不匹配形态（Docker 端口映射 / DNS 名访问）保持 origin**——陪验实测发现容器内枚举到的是 docker0 内网 IP（172.17.0.5，容器网络命名空间看不到宿主网卡），旧「无匹配一律回退首个候选」会把用户正在访问的正确宿主地址替换为设备不可达的容器 IP，已修复并以 Docker 形态单测锚定（残余局限：Docker + localhost 打开时前端与服务端均无可靠信息，候选仍为容器 IP，生产主路径宿主 IP 访问已正确）。，多网卡可切换候选，切换后二维码 / 地址文本 / 复制内容同步更新（选择规则收敛 `web/src/lib/connection.ts` 纯函数）。
- **管理 tab 行为零变化（回归）**：Windows（client-packages）/ Android（pda-packages）上传 / 下载 / 删除（确认 Modal）、Android 授权提示与数据接口不动；客户端设置页「更新与安装包」仍走 `GET /api/client-packages`。
- **措辞平台优先 + i18n**：页面副标题改「LabelFrame 客户端各平台安装包」语义，分区命名平台化（Windows / Android）；`downloadCenter` 域 zh-CN + en 全量 key 化（tabs / section / quick / connection / windows / android 重组，无硬编码用户可见文案）。
- **测试**：前端更新 / 净增 vitest（tab 切换与 URL 还原 / 直链 / hashchange、最新一条选取（含乱序输入）、空态引导、复制行为、地址候选回退与切换（`lib/connection.ts` 纯函数单测 + 组件级）、管理 tab 回归、双语冒烟改版断言）→ 502×双模式全绿；服务端新增 `LocalIpv4CandidatesTests`（RFC1918 判定 + 枚举有效 / 去重 / 私网优先）与 `ServerIpv4CandidatesEndpointsTests`（端点 HTTP 集成）。
- **验收（2026-10-08 陪验）**：AC-08 用户桌面复验通过——三 tab / 双卡 / 连接信息卡（复制 / 候选切换）全过＋**PDA 扫码枪实扫连接卡二维码键盘直填成功**（证据评论 6053534528）；陪验中按用户反馈完成观感收口 4 项（见上）并实测发现 / 修复 Docker 候选回退缺陷（本机容器 53961:53961 映射复现、修复后 Docker 形态复验通过）；陪验收口后前端 502×双模式全绿 / lint 0 警。

## 迭代 117：HTTP 部署非安全上下文 randomUUID 崩溃——uuid 包替换与回归测试（#268） · 2026-10-08
- **根因修复（方案 A：引入 `uuid` 包）**：`crypto.randomUUID` 是浏览器安全上下文专属 API（仅 HTTPS / localhost 暴露），HTTP + 局域网 IP 访问服务端 Web UI（Linux Docker / 离线部署包场景）时为 undefined，「数据与打印」页所有走 `buildRequest` 的操作（图片预览 / 打印测试 / Excel 批量打印）同步抛 `TypeError: crypto.randomUUID is not a function`——预览弹层不打开、打印请求无法构造（自迭代 11 首版 Web 前端即存在，v0.30.0 起 Linux 离线部署铺开后暴露面变大）。`DataPrint.tsx` `buildRequest` 三处（job server 构建 / job client 服务端路由 / debug 出图）`crypto.randomUUID()` 替换为 `uuid` 包 `v4()`（import 单处收敛）；uuid 在 randomUUID 可用时仍走原生、不可用时回退 `crypto.getRandomValues`（不受安全上下文限制），安全 / 非安全上下文行为一致，`requestId` 服务端契约（string + 非空白幂等键）不变。
- **回归测试**：新增 `DataPrint.randomUUID.test.tsx`（vitest，server 构建分支）——显式把 `crypto.randomUUID` 重定义为 undefined 模拟非安全上下文（jsdom 30 已提供该 API，需显式破坏；afterEach 精确还原不污染后续用例），断言 debug（图片预览：renderImage 请求构造 + 弹层出图）与 job（打印测试：submitJob 提交）两分支不抛 TypeError、`requestId` 为合法唯一 UUID v4 形态；净增 3 项 → 481×双模式。
- **回归确认**：localhost / PC 客户端 / PDA 宿主（本地加载均为安全上下文）行为不变——randomUUID 可用时 uuid v4 直接用原生实现，生成口径与旧实现一致。
- **验收（2026-10-08）**：AC-01~04 全部达成——AC-01（非安全上下文单测）/ AC-04（localhost 回归）随 PR #270 自证；AC-02/03 沙箱自证（stub 服务器 0.0.0.0:4188 托管 dist-server 产物＋系统 Chrome headless 经局域网 IP 访问，实测 `isSecureContext=false`：图片预览弹层出图、打印测试 1/1、模拟出图直下、三枚互异合法 UUID v4，证据评论 6050905543）——按分工协议 A 类客观断言委托自动结项；修复随下版 `v*` 发版出库，报障环境（HTTP 局域网访问 v0.31.0 部署）升级后自愈。

## 迭代 116：安装程序提权提示不前置——BA 向导主动置前组合拳（#265） · 2026-10-02
- **根因修复（BA 窗口显示策略，WiX Burn 结构不动）**：`LabelFrame.Bootstrapper.Ba` 向导（`WizardForm`）与离线布局进度窗（`LayoutProgressForm`，等效首显路径）首显时完全依赖系统默认前台授予链——启动者无前台权（计划任务 / 后台进程触发、Explorer 未持前台、下载后立即切走）时窗口静默不前置，用户以为安装无响应（#265 复现 S1 / S3 / S3b；S2 实证 MSI 直装路径提权请求甚至完全静默挂起）。现首显主动置前（用户拍板选项①组合拳＋闪烁兜底）：已在前台则零调用零扰动（正常双击链不回归）；否则 `AttachThreadInput`（本 UI 线程挂接当前前台线程）→ `ShowWindow(SW_MINIMIZE→SW_RESTORE)`（还原动作的前台授予独立于置前配额，S3c 预试证明单靠挂接 + `SetForegroundWindow` 会被前台锁拒）→ `SwitchToThisWindow` → `SetForegroundWindow`，仍被拒时 `FlashWindowEx(FLASHW_ALL|FLASHW_TIMERNOFG)` 任务栏强闪烁兜底（闪至前置自停）；结果写 Burn 日志（「向导首显置前：」行，机器可读证据）。
- **可测性（AC-02 单测部分）**：置前逻辑收敛到新类 `LabelFrame.Bootstrapper.Wizard.WizardForegroundActivator`（前台探测 / 线程查询 / 输入队列挂接 / 显示切换 / 会话切换 / 前置请求 / 闪烁共七个 Win32 调用点可注入替身，沿用 `TrayMenuPresenter` 先例·#263 修法，落共享库供 BA（net48）与测试（net10）双引用）；净增 7 项单测：已在前台零扰动、组合拳全序列（挂接→最小化→还原→切换→前置→解除，无闪烁）、被拒转强闪烁兜底（解除不缺席）、请求成功即短路、同线程跳挂接、挂接失败照常组合、无前台窗口防御。
- **S1 采样断言脚本化（AC-02）**：新增 `scripts/test-bundle-wizard-foreground.ps1`——移植 #265 复现采样器（每 0.9s `GetForegroundWindow` 归属＋被观察 BA 进程主窗口 `fg_match` 比对，日志行格式与复现日志一致），由 Interactive 计划任务驱动即为「无前台权启动者」S1 形态，断言向导 5 秒内成为前台（PASS/FAIL 退出码）；闪烁兜底为视觉形态不参与自动断言（Burn 日志行佐证）。
- **安装触发链全景（AC-01，结论回写 Issue）**：双击 / 下载运行 / WinHost 升级触发三类链逐条取证——WinHost 无安装程序启动点（仅两处 `Process.Start` 均为打开浏览器 URL），客户端设置页「检查更新」仅版本比对＋下载链接（决策 #71 / #72 维持不做应用内自动升级）→ WinHost 链无实际缺陷，登记观察项（若未来应用内触发升级，须从用户点击的 UI 上下文启动安装程序保持前台链）。
- **回归**：提权路径归一口径维持——用户走 Bootstrapper 向导时由确认页显式点击「安装」触发提权（此时向导已前置，授予链成立）；S2 的 msiexec 直装链属 Windows Installer 系统行为不在范围（Issue 明示）。AC-02 VM 重放与 AC-03/04 桌面复验实现时转 `待验收`，后全部补验通过（见验收条）。
- **验收（2026-10-08）**：AC-02 VM 重放补验通过（2026-10-02：LF-Accept-Win10 基线双形态——标准 Interactive 计划任务首次前置 2.0s、严格 S1 无前台权＋第三方持前台组合置前 1.9s，均远低 5s 阈值；锁屏无效跑如实披露、闪烁兜底路径存在性佐证，证据评论 5954837598）；AC-03/04 用户桌面复验通过（2026-10-08：提权链测试 Bundle 0.90.10——同源修复 BA 重打 per-machine 变体，向导双击前置零扰动（Burn 日志行）＋点「安装」UAC 正常出现（提权会话成功）＋Client MSI 双击界面正常不回归，证据评论 6050532842）。附带观察项：BA 卸载动作为非交互无 UI（决策 #126 设计，升级链尾无人应答不可弹窗），用户从控制面板卸载 Bundle 条目时无感知界面、静默完成零残留——体验观察项登记（正式产品「LabelFrame Client」MSI 条目卸载有标准 msiexec 交互界面）。

## 迭代 115：WinHost 托盘右键菜单标准前置模式补齐——点击外部自动关闭与反复弹出修复（#263） · 2026-10-02
- **根因修复（三件套，KB135788）**：`TrayIconService.ShowMenu()` 原裸调 `TrackPopupMenu`，缺 Win32 通知图标上下文菜单标准前置模式——现 `TrackPopupMenu` 前 `SetForegroundWindow(owner)` 前置隐藏 owner 窗口（修复症状 1：右键弹菜单后点击其他位置菜单不收起）；菜单关闭后 `PostMessage(owner, WM_NULL)` 清除菜单模态状态（修复症状 2：「打开界面」抢前台后再次右键菜单弹不出）；`SetForegroundWindow` 从托盘线程直接调用（Win32 标准用法）。
- **衍生隐患消除**：`TrackPopupMenu` 模态循环运行期间再次右键会重入 `ShowMenu` 嵌套调用（行为未定义）——以菜单运行标志早退消除。
- **可测性（AC-01）**：弹出逻辑收敛到新类 `TrayMenuPresenter`（窗口前置 / 菜单弹出 / 消息投递三个 Win32 调用点可注入替身，沿用 `TrayQuitSignaler` 先例·缺陷 #58 修法）；净增 4 项单测断言三件套调用时序（前置先于弹出、WM_NULL 后于菜单关闭、owner 一致）、模态期间重入早退无嵌套、菜单关闭后可再次弹出。
- **回归**：双击打开界面、菜单「打开界面 / 退出」分发逻辑不变（`TrayIconService` 仅改为委托 presenter 取命令 Id）。
- **验收（2026-10-02）**：AC-02~04 用户桌面复验通过（右键点外部自动关闭 / 开界面后再弹 / 双击与两菜单路径回归，三动作逐条确认；隔离 DB + Log 模拟形态，不触已装客户端数据），证据回写 #263 后结项。

## 迭代 114：服务端构建语言切换入口——壳层导航尾部切换器（#260） · 2026-10-01
- **壳层语言切换器（待决议-1 按建议项①）**：`App.tsx` nav-foot（连接状态点旁）新增循环单按钮「中 / EN」——当前语言短名可视（语言自名不随语言翻译，决策 #165 ③ 口径），点击互切即点即生效；client / server 两构建统一显示，server 构建无设置页也能一步切换（不再依赖 F12 改 localStorage，#246 陪验痛点）。
- **双入口并存（AC-03）**：client 构建设置页语言卡保留不动，与壳层切换器同一 `changeLocale` 单点（localStorage 持久化 + changeLanguage + `<html lang>` 同步），显示同步、互切一致。
- **词条（AC-04）**：`shell` 域新增 `langSwitch` 组 4 条（zhName / enName / switchToZh / switchToEn）zh-CN + en 镜像，en 覆盖断言自动纳管；`App.tsx` 已在全站圈禁内，无裸文案。
- **测试**：新增 7 项 vitest——client 切换器在位 / 点击互切（词条 + html lang + localStorage）/ 全新模块图重载（等价刷新）保持 / 双入口一致（select 与按钮互相同步）；server 构建在位（无设置页断言重申）/ 点击切换 / 刷新保持（净增 → 478×双模式）。
- **布局走查（AC-05）**：沙箱 stub API（独立端口，避开本机真实 WinHost 的 53960）+ 双 vite 实例（client / server 构建）+ playwright headless Chrome——35/35 程序化断言（切换器在位、不出 60px 导航边界、不遮连接状态点、无横向溢出、切换词条即时变化、reload 保持、0 未捕获异常）＋ 10 截图中英两态取证。

## 迭代 112：Web 文案迁移第二批与全站收尾——设置、设计器与下载中心（#246） · 2026-09-29
- **页面 key 化（末批，108/111 惯例沿用）**：`Settings.tsx` / `TransportPanel.tsx` / `Designer.tsx` / `designer/SidePanel.tsx` / `designer/PropsPanel.tsx` / `designer/CanvasViewport.tsx` / `designer/ElementNode.tsx` / `DownloadCenter.tsx` / `PluginPackages.tsx` / `Devices.tsx` 用户可见文案全部 t() 化；按页新增域 `designer.json` / `downloadCenter.json` / `devices.json` / `pluginPackages.json`，`settings.json` 从语言卡片扩至全页，`common` 增补 action（save/download/delete/deleting/uploading/confirmDelete）、api.pluginTooLarge、transport.mode / zebraKind——zh-CN 值与原硬编码逐字一致（中文回归零变化）。
- **lib 层兜底与显示名**：`design/types.ts`（typeLabel / layerLabel / elementContent / defaultElement）、`design/barcode.ts`（未绑定占位）、`design/format.ts`（导入错误）、`designer/shortcuts.ts`（快捷键清单改函数式）、`transport.ts`（MODE_LABELS / ZEBRA_KIND_LABELS 改 modeLabel / zebraKindLabel 函数）、`pluginLimits.ts`、`useTemplatePreview.ts`、`AppContext.tsx` 状态消息——调用期读 i18next 单例，消费组件订阅重渲染；条码占位判别从本地化字符串比较改为语义态 `elementContentState`（跨语言不再依赖文案比对）。
- **待决议-1（数据性默认值，按建议项）**：UI 层默认值随 locale、模板存储值不受语言影响——新建文本元素默认占位「文本」/「Text」（`designer:elementDefaultText`）、设计器分组默认「默认」/「Default」；存量模板 Literal 不回写不回译（双语冒烟测试锚定）。PropsPanel 字体下拉**显示名**随语言（微软雅黑 → Microsoft YaHei 等；字体内部名 / 模板存储值不变，白名单 6 项维持现状）。
- **全站圈禁（AC-01/02）**：`labelframe/no-bare-cjk-jsx` 从「已迁文件清单」升级为全站顶层规则（语言包 JSON 不在 oxlint 范围；测试 fixtures 白名单仅 Popover / ErrorBoundary 两测试文件），新增页面自动受保护；en 覆盖 zh-CN 全 key 断言纳入 4 个新域（11 域逐文件，缺失即红）。
- **机制文档（AC-05）**：`web/src/i18n/README.md`——key 惯例、新增文案流程、en 词条维护 + 覆盖断言、圈禁白名单口径、数据性默认值语义（待决议-1 结论）。
- **测试与走查**：新增第二批双语冒烟 6 项（设计器 / 属性面板 / 设置 / 下载中心 zh→en→zh + 待决议-1 锚定；净增 → 471）；存量中文断言仅 shortcuts.test.ts 随函数式接口机械调整（zh 断言值不变）；AC-03 英文布局走查（沙箱 stub API + 双 vite 实例 + playwright headless）22/22 程序化断言＋20 截图：设置页（连接表单 / 卸载弹窗）、设计器（属性面板密集表单 16 行两档滚动位 / 快捷键弹窗 / 离开保护三选）、下载中心（双分区表格 / PDA 长提示 / 删除确认弹窗）中英两态无溢出无未插值、0 未捕获 JS 异常。
- en 翻译口径沿用 #245 术语表；107 底账 web 侧逐文件销账（结果评论回写 #240）。
- **验收（2026-10-01）**：AC-06 用户陪验全站英文态终验通过（设置 → 设计器 → 下载中心 → 工作台全流程；陪验中插件名称显示疑点定性为无效假包素材的按设计降级展示，非产品缺陷），证据回写 #246 后结项。

## 迭代 111：Web 文案迁移第一批——工作台、作业历史与数据打印（#245） · 2026-09-29
- **页面 key 化（108 惯例沿用）**：`Workbench.tsx` / `WorkbenchPreview.tsx` / `JobHistory.tsx` / `DataPrint.tsx`（含 MappingModal / ImagePreviewModal / JobPanel）用户可见文案全部 t() 化；按页新增域文件 `workbench.json`（39 词条）/ `jobHistory.json`（25）/ `dataPrint.json`（112），跨页共用词条入 `common`（action / state / jobStatus / column / device / value / api / transport 组，+42）——共用作业状态口径抽 `useJobStatusLabel()` hook（未知状态回退原始值与迁移前一致）；zh-CN 值与原硬编码逐字一致（中文回归零变化）。
- **壳层残余收尾**：AppContext 状态栏初始「就绪」与启动消息（`shell.statusbar.ready` / `shell.startup.*`）、ErrorBoundary 错误页（`shell.errorBoundary.*`）、`document.title` 随语言切换（`shell.appTitle`，108 遗留项）；`NativePrintModeHint` 徽标 key 化（`common.transport.*`，同时用于设置页表单——面板其余归 112）。
- **client.ts HTTP 兜底消息（109 明确归本轮）**：TIMEOUT / NETWORK_ERROR / HTTP_* 与 blob 类失败回退文案 key 化（`common.api.*`），错误构造期读 i18next 当前语言；`formatTransport` 本地格式化跟随语言（新后端 displayText 优先语义不变，zh 值逐字一致）。
- **富文案**：词条内嵌标签统一 react-i18next Trans 位置语法（`<0>…</0>` + components 数组）——删除确认弹窗加粗模板名、作业进度区「作业历史」内联跳转链接（链接语义按钮行为不变）。
- **防线扩展**：圈禁清单 2 → 7 文件（+Workbench / WorkbenchPreview / JobHistory / DataPrint / ErrorBoundary）；en 覆盖 zh-CN 全 key 断言纳入 3 个新域（缺失即红）。
- **测试**：新增三页面双语渲染冒烟 3 项（zh 断言 → 切 en 全英文且无 CJK → 切回恢复；含 document.title 与状态栏初值回归），存量中文断言零改动全绿（vitest.setup 钉 zh 首启；净增 3 项 → 441）；AC-03 英文布局走查（沙箱 stub + playwright）18/18 程序化断言＋9 截图：工作台卡片脚按钮组 1440/1100 两档无裁剪（#225 前科复核）、删除确认弹窗 / 表单 / 作业历史明细中英两态无溢出。
- en 翻译口径（待决议-1 按建议项）：执行会话初翻＋术语表回写 #245（作业 = job、模板 = template、目标设备 = target device 等）。Settings / Designer / PropsPanel / DownloadCenter（迭代 112）不在本轮。
- **验收（2026-10-01）**：AC-06 用户陪验英文态抽验通过（工作台 / 数据与打印 / 作业历史三页面，术语对照术语表无异议），证据回写 #245 后结项。

## 迭代 109 返修：LF_* 错误码参数键统一与拆码——单码单参数键集（#242） · 2026-09-29
- **根因修复（AC-04 走查失败项 1a）**：LF_SRV_006 同码两处后端调用点参数键不一致（`TemplateEndpoints.cs` GET/导出/预览键 `name` vs `ServerService.cs` 作业提交链键 `templateName`），en 码表词条 `{{templateName}}` 只适配后者 → 模板库全部共享端点（工作台编辑/打印打开、数据页模板加载、预览、导出）在 en 态回退中文。统一为 `templateName`（共享端点同时覆盖 WinHost 接线的 LF_TPL_001），中文渲染输出不变（占位名变化不改变替换后文案）。
- **同型风险全仓清扫（逐码处置，决策 #166 ⑥「单码单参数键集」）**：审查全部 `LF_*` 码的多调用点参数键变体——① LF_SRV_001 `{ip}`（按 IP 查设备 ×2）/`{deviceId}`（设备未注册 ×3）语义不同 → 拆码 `LF_SRV_012`（按 IP 未找到设备 `{ip}`），LF_SRV_001 保留 `{deviceId}`；② LF_JOB_002 条目级重打变体（`{itemIndex}` 越界、`{itemIndex}`+`{itemStatus}` 非 Failed）与 `{status}` 状态转移语义不同 → 拆码 `LF_JOB_003` / `LF_JOB_004`；③ LF_SRV_002 的 callbackUrl 校验变体 `{callbackUrl}` 与 `{detail}` 变体键集不一致 → 拆码 `LF_SRV_013`；④ 其余多调用点码（LF_JOB_001 / LF_IO_001 / LF_PLUGIN_INVALID / LF_SRV_003~006 等）键集已一致，词条无占位符的通用桶码（LF_SRV_002 / LF_API_001 / LF_TRANSPORT_INVALID）params 仅透传不插值、维持现状。拆码仅细分码语义：HTTP 状态码与中文 message 均不变（by-ip 404、callbackUrl 400、条目重打 409 映射保持）。
- **码表同步**：web `errorCodes.json`（zh-CN + en）与 PDA `LfErrorCatalog.cs`（zh + en）同步新码词条、LF_TPL_001/LF_SRV_006 占位 `{{name}}`→`{{templateName}}`、LF_SRV_001 代表性词条改为 `{{deviceId}}` 变体；覆盖断言（web vitest + AndroidHost.Tests 解析注册表）自动要求新码入表。
- **防复发断言**：① dotnet 源扫描测试（`ErrorParamKeyConsistencyTests`）——解析 `src` 全部携带 params 的错误构造调用点（Templated 助手 / Dictionary 惯用法，含共享端点 Server/WinHost 接线展开），按码分组断言键集一致，并与 zh / en 两份码表词条占位符逐键对齐；站点数下限 + 解析完整性断言防正则失配假绿。② web en 渲染变体矩阵（`errorParamVariants.test.ts`）——全部 19 个变体逐条断言英文词条、插值正确、无 `{{` 残留（含 LF_SRV_006 两变体与 LF_SRV_001/012 各变体）。③ 端点集成锚点：模板库 404 / 提交链 / by-ip / callbackUrl 的 code+params 断言。
- 不在范围（维持验收侧）：渲染层多键兼容、接受变体回退、en 界面整轮复测（walk 脚本与证据链可复用）。
- **验收（返修后 en 界面整轮复验，2026-09-29）**：沙箱走查 18/18 断言通过——原失败项 1a（模板库端点变体）en 渲染 `Template not found: <name>.` 英文词条＋插值在位；拆码 LF_SRV_012（`No device found for IP: <ip>.`）/ LF_SRV_013（`Invalid callbackUrl …: <url>.`）经「真实后端 ErrorView 捕获＋UI 提交链重放」复验通过；未知码回退后端中文、无码回退前端文案、zh-CN 直用后端 message、0 未捕获异常。证据回写 #242 后结项。

## 迭代 110：PDA 宿主多语言——Android 资源体系与错误码翻译（#244） · 2026-09-29
- **Android 资源体系（决策 #164 ⑤，待决议-1 按建议项）**：`Resources/values/strings.xml`（缺省＝中文，与现状文案逐条等价）＋`Resources/values-en/strings.xml`（135 键含语言哨兵 `current_language`），MainActivity 各页面 / 对话框 / 保存提示 / Toast、`[Activity(Label)]` 与 manifest 应用名（`@string/app_name`）、PrintHostService 常驻通知标题与状态内容、`LabelHostConfig.PrinterDisplay` 连接方式摘要全部改资源 id 引用——语言跟随系统（其余系统语言回退缺省中文），带参模板用 `{0}` 位置占位（string.Format + InvariantCulture）。**通知渠道名不改名**（Android 渠道创建后不可改名，维持中文现状——决策 #164 ⑤ 既有口径）；HostLog 诊断 / 崩溃摘要维持中文（#164 ②）。
- **PDA 侧 `LF_*` 错误码表（语义对齐迭代 109 的 web 端）**：`Errors/LfErrorCatalog.cs` zh / en 静态字典（27 码，模板与 web `errorCodes.json` 逐码对齐，占位按端各自惯例——web `{{key}}` / PDA `{key}`，插值复用 Core `ErrorMessageTemplates.Format` 单遍语义）；展示规则——en 已知码本地翻译、未知码或模板所需参数不全回退后端中文 message（不出现裸占位符），zh 直接采用后端 message；语言判定经资源哨兵（不解析 Configuration）。宿主自有 ErrorView 副本补可选 `params` 字段（与 API 契约同形，缺省序列化逐字节等价），`GET /api/jobs/{id}` 404、测试页发送失败、插件安装 / 卸载包校验失败四处透出模板参数；插件安装兜底失败从 `LF_PLUGIN_INVALID` 修正挂 `LF_PLUGIN_INSTALL_FAILED`（翻译语义与注册表对齐）。接入点：测试打印失败项与提交失败、插件安装 / 卸载错误提示。
- **danger 规范同步（决策 #161）**：插件卸载确认对话框双语化仅换文案来源——标题 / 后果说明（含不可恢复语义与自动重启提示）/ 确认按钮文案经资源 id 引用，红底触发按钮、确认按钮红字 / 取消次要色与「先确认后执行」语义不变。
- **测试与自证**：新增 `test/LabelFrame.AndroidHost.Tests`（net10.0，码表纯托管源码按文件链接编译——AndroidHost 为 android TFM 不可整工程引用），14 项单测——注册表覆盖双断言（解析 ApiErrorCodes / ServerErrorCodes / JobErrorCodes 源码，新增码漏表即红）、zh / en 键集一致、同码占位符键集一致、en 已知码插值 / 未知码与参数不全回退中文 / zh 直用后端 message、单遍不级联；AC-01 裸中文扫描（MainActivity 除 HostLog 诊断外 0 命中；PrintHostService 仅渠道名白名单＋日志）与 AC-02 zh 等价对照（97 条逐字命中＋30 条内插模板片段等价，日志 / 渠道名按范围排除）自证入 PR。本地验证：Android Release APK（CI 同款命令）0 警 0 错＋aapt2 取证（en 标签 / 哨兵资源入包）；`dotnet build` / `dotnet test`（排除 Perf/Soak）全绿。
- **验收（2026-10-08）**：AC-05 真机走查通过——UROVO DT50（Android 11）真机 zh / en 双态走查设置（连接服务器 / 连接打印机 / 插件管理 / 本机信息四页面）文案逐条对照（en 全英文渲染、zh 与现状逐字一致）、`dumpsys notification` 常驻通知双语对照（en「Server: not set · Printer … · Unreachable」/ zh「服务器：未设置 · 打印机 …」）、`POST /api/host/test-print` 作业链 Pending→Failed（LF_IO_001）状态流转正确；语言跟随系统语言列表切换实证生效（`settings put system_locales` 在该 ROM 命令式不生效属设备特性）；测毕系统语言回 zh-CN、卸载构建版装回 v0.30.0 完整复原（证据评论 6051142537，截图 13 张＋UI dump 本地留档）。

## 迭代 109：API 错误消息多语言链路——ErrorView 可选 params 与 LF_* 码表（#242） · 2026-09-29

- **ErrorView 可选 `params`（决策 #164 ③ / #166，契约见 DESIGN §5）**：错误响应增可选 `params` 字段——扁平字符串键值对象（如 `{ "ip": "192.168.1.5" }`），键名与消息模板占位符 `{key}` 一致；缺省 / null 时序列化输出与旧形态**逐字节等价**（单测 + HTTP 原文断言锚定），纯加法、旧调用方无感；WMS 终态回调载荷（#154 schema）字段集零变化（断言锚定）。
- **后端消息模板化**：带内插的中文错误消息（按 IP 查设备、作业不存在、非归属回报、模板不存在、插件包无效、Excel 解析失败、测试页发送失败、连接方式不支持等）全部改「模板 + 参数」构造——插值收敛到 Core `ErrorMessageTemplates.Format`（`{key}` 单遍替换、缺参占位符原样保留不抛、值内嵌 `{x}` 不级联）；`ServerException` / `LabelJobException` 增可选 `Parameters` 随异常透传；后端 message 输出渲染后中文，与改造前内插文案等价（既有后端测试消息断言全绿即等价锚点）。PDA 宿主（AndroidHost 自有 ErrorView 副本）不在本轮，随 #244。
- **`server_jobs.error_code` 落库（可空）**：SQLite 自动补列（先例 #154 callback_url；旧库升级测试锚定）；宿主回报体增可选 `errorCode` 字段（WinHost 路由回报取首个失败项原因码）、失联回收 `LF_SRV_009`、超期放弃新增注册表码 `LF_SRV_011`；error_message 中文存量不迁移。
- **web 端 LF_* 码表（并入 108 语言基座）**：`locales/<locale>/errorCodes.json` 命名空间（码 → 文案模板，zh-CN + en，i18next `{{key}}` 插值与后端 params 同名对接）；`client.ts` 展示规则——zh-CN 直用后端中文 message（中文权威在后端），en 已知码按码表插值渲染、未知码或参数不全的消息变体回退后端中文 message（不出现裸 `{{key}}`）；`ApiError` 携带 `params`。
- **覆盖防线（AC-05）**：vitest 解析 `ApiErrorCodes` / `ServerErrorCodes` / Core `JobErrorCodes` 注册表源码提取码字面量，断言 zh + en 两份码表全覆盖且无死码（新增码漏表即红），并入 locales en↔zh key 一致性断言；删表取证（注入删除 LF_SRV_011 → 两条断言红 → 还原）见 PR。
- 测试净增 30 项（dotnet 17：ErrorView 序列化等价 / 模板插值 / 端点 params / error_code 落库与旧库迁移 / 回调 schema；web 13：码表覆盖 + 翻译渲染 + client 链路）；后端注册表加码提示同步码表。PDA 码表（#244）、作业历史存量翻译、后端日志维持中文不在本轮。

## 迭代 108：Web 前端 i18n 地基——框架接入、语言切换与防线（#241） · 2026-09-29

- **i18n 库接入（决策 #164 ④ / #165 定版）**：react-i18next 17 + i18next 26（React 19 peer 兼容无补丁），资源静态 import 打包（client / server 双构建离线一致）；语言包 `web/src/i18n/locales/<locale>/<域>.json` 按域单文件——初始域 common（跨页复用）/ shell（应用壳层）/ settings，zh-CN 源 + en 全量覆盖。
- **语言状态与切换**：设置页新增「语言」卡片（选项名固定各自语言原文）；localStorage 键 `labelframe.locale` 持久化（显式切换才写入）；首启无偏好时跟随浏览器语言（`zh*` → zh-CN，其他 → en）；`<html lang>` 由 languageChanged 事件动态同步（`index.html` 静态默认仅作首帧）。
- **Intl 化**：清理硬编码 `toLocaleString('zh-CN')` 三处调用点（AppContext 日志时间戳、Workbench 模板卡片日期 / 悬停时间）改跟随当前 locale；页面文案迁移不在本轮（111/112）。
- **样板迁移（锚定惯例）**：App.tsx 壳层（导航 / 状态栏 / 日志抽屉 / 清空确认弹窗 / 设计器空态）与通用 Modal 组件（关闭按钮 aria-label）文案全部 key 化——key 语义 camelCase 按 UI 区域分组、插值 `{{name}}`、`useTranslation('<域>')` + common fallbackNS，惯例记录 #241 Issue 评论供 111/112 沿用。
- **CI 防线（决策 #164 ⑦）**：① oxlint jsPlugins 自定义规则 `labelframe/no-bare-cjk-jsx`（`web/oxlint-plugin-labelframe.cjs`）——圈禁文件内 JSX 文本 / JSX 属性字符串含 CJK 即 error，圈禁清单在 `.oxlintrc.json` overrides 显式维护（初始 `src/App.tsx` + `src/components/Modal.tsx`）；② vitest 断言 en 语言包覆盖 zh-CN 全部 key（缺失 / 多余均红，`src/i18n/locales.test.ts`）。
- 测试环境 vitest.setup 钉住 `navigator.language = zh-CN`（既有中文断言确定性）；新增 i18n 行为测试 14 项（AC-01/02 组件自证：jsdom 模拟 localStorage 与浏览器语言）。后端零改动。
- 页面文案批量迁移（111/112）、错误码表（109）、PDA（110）不在本轮。
- 验收（AC-06 沙箱补验，2026-09-29）：双语走查通过——server / client 双构建中英两轮全导航（工作台 / 设计器 / 数据与打印 / 在线设备 / 作业历史 / 下载中心 / 插件管理 / 设置）无白屏 / 无乱码 / 壳层无横向溢出（含 1280×720 复检与清空日志确认弹窗中英两态），语言卡片切换即时生效且刷新保持，33/33 程序化断言全绿、0 未捕获 JS 异常；DOM 取证与截图留存位置披露回写 #241（用户委托自动结项：2026-09-11 分工协议）。

## 迭代 107：多语言治理——决策 #164 文档入库与全仓文案盘点底账（#240） · 2026-09-29

- **决策入库**：**#164「多语言（英文）支持范围与架构基线」**决策表行（Web UI / API 错误消息 / PDA 三端，zh-CN 源 + en；打印内容 / 安装链 / 日志 / RTL 四类边界显式不做；错误消息 = 错误码权威化 + 展示端翻译（后端零语言状态、不做 .resx）；前端语义 key + `Intl` 化 + 语言偏好持久化；PDA 补建 Android 资源体系；CI 防线与落地路径）——该行已随并行迭代 113（#247，字体回退修复，其 ⑥ 内增补实施记录）先行合入 master，本 PR rebase 保留已合入行不重复添加；本 PR 补齐其余入库件：`docs/REQUIREMENTS.md` §7「多语言」条目从「不做」修订为「限定范围支持（见决策 #164）」，DESIGN §6 定位段与 §6.13 开放点两处「REQUIREMENTS §7『多语言』边界不变」引用改挂决策 #164。
- **文案盘点脚本**：新增 `scripts/inventory-ui-text.ps1`（pwsh；只读仓库、可重复运行、无人工干预）——扫描 `web/src`（*.ts/tsx，排除测试）与 `src`（*.cs，排除 bin/obj/test），把含 CJK 文本的条目按「注释 / 日志与诊断 / 用户可见 UI 文案」三类启发式分类（`//` 与 `/* */` / `[LoggerMessage]`、ILogger、Serilog、HostLog、Console、console.* 调用语境 / 其余字符串字面量；.tsx 的 JSX 文本节点计入 UI；C# 插值孔内含引号等解析盲区以「残留」通道按同一日志语境归并，不丢失），输出 Markdown 统计表（三类总量 + 每文件明细 + Top 重灾区，支持 `-OutputFile` 落 UTF-8 文件）。
- **本轮底账（盘点基线 master `39b27e4`）**：用户可见 UI 文案约 **2,097 条 / 15,504 汉字**（web/src 881 条 / 5,893 汉字，src/*.cs 1,216 条 / 9,611 汉字），注释 9,716 条 / 75,206 汉字，日志与诊断 152 条 / 1,270 汉字；Top 重灾区：DataPrint.tsx（154）、MainActivity.cs（150）、Settings.tsx（128）、BA 主向导（106）、PropsPanel.tsx（84）。底账全文评论回写 #240（供迭代 111/112 按页面迁移排期），盘点结果不落 docs/ 长期文件（Issue 即事实源，WORKFLOW §3）。
- 无产品代码行为变更；未接 i18n 框架（迭代 108）、不动错误消息链路（迭代 109）、不改 ROADMAP（状态总览行随结项 docs 走轮值）。

## 迭代 113：渲染字体回退按实际字符匹配——SkiaTypefaceLookup 缺陷修复（#243） · 2026-09-29

- **动机（#243，来源 DESIGN 决策 #164 ⑥）**：`SkiaTypefaceLookup` 字体回退硬编码 `MatchCharacter('中')`——文本含**任意**非 ASCII 字符即以『中』字匹配系统字体（CJK 中心缺陷）：韩文 / 泰文等脚本被套上中文字体，雅黑无对应字形即整段豆腐块。属健壮性缺陷修复而非 i18n 功能，不依赖多语言排期。
- **修复**：回退改按文本**首个非 ASCII 字符**（代理对取完整码点，孤立代理项跳过不抛错）交系统回退链匹配，无匹配时维持 preferred 字体；策略与取舍记入代码注释。Windows 实测：韩文 → Malgun Gothic、泰文 → Leelawadee UI（修复前雅黑分别缺 5/5、10/10 字形），中文常用汉字经回退链与『中』解析到同一字体。
- **零回归锚定**：中文样本以「修复前基线路径（复刻 MatchCharacter('中')）vs 修复后路径」同参数栅格化，位图逐字节一致（SHA-256 `87825D67…93AC568` 相等）+ 字型同族同款断言；整版中文标签两次渲染像素逐字节一致（含表面池复用路径）；既有中文渲染测试全绿。
- **单测（不假绿）**：假名 / 韩文 / 扩展 B 区样本按实际字符命中对应字体并全覆盖字形（`GetGlyphs` 无零）——环境字体存在性探测（`MatchCharacter` 本身）+ 缺失运行时跳过（引入官方 `Xunit.SkippableFact`，仅测试依赖）；孤立代理项不抛错维持 preferred。
- **Android 核查（AC-03）**：`AndroidLabelRenderer` / `AndroidTextWidthMeasurer` 用 `Android.Graphics.Paint` 默认字体、无显式回退匹配，Android 系统字体栈自动按脚本逐段回退——无同型缺陷，核查通过（结论回写 #243）。
- **记账**：DESIGN 决策 #164 全文（取自 #240 附录 A，基线未含 #164 行）落位 #163 行后，⑥ 内追加本迭代实施记录（与 #240 并行的记账冲突按 WORKFLOW §7 后合并方 rebase）；设计器字体白名单维持现状 6 项不动（#243 待决议-1）。

## v0.32.0 迭代 107～119 汇总发布 · 2026-10-09

- **打包范围**：v0.31.0 之后合入 master 的全部迭代与缺陷修复——多语言工程：迭代 107（决策 #164 入库与盘点底账）、108（Web i18n 地基，决策 #165）、109（API 错误消息多语言链路，#242 含 #256 返修，决策 #166）、110（PDA 宿主多语言，#244，决策 #167）、111/112（Web 全站文案迁移两批，#245/#246）、113（渲染字体回退按实际字符匹配，#243）、114（壳层语言切换器，#260）；修复：迭代 115（托盘右键菜单，#263）、116（BA 安装提示置前，#265）、117（HTTP 非安全上下文 randomUUID，#268）；体验：迭代 118（下载中心快速访问三 tab，#272 含 #275 陪验收口）、119（新用户首见引导，#276）。迭代 120/121（工作流实验治理；帮助体系 V2 交付后陪验撤回）净零出库，不影响本版。详见各迭代条目。
- **本版可感知变化（多语言）**：Web UI / API 错误消息 / PDA 宿主三端中英双语（zh-CN 源＋en 全量）；语言切换双入口（设置页＋壳层导航尾部「中/EN」单按钮——server 构建无设置页也可一步切换），localStorage 持久化、首启跟随浏览器语言、日期时间格式随语言；`LF_*` 错误码表 zh/en（已知码插值本地翻译、未知码回退后端中文）；渲染字体回退按文本实际字符匹配（日文假名 / 韩文 / 泰文等非中文脚本不再错字体，跨版本中文渲染逐字节一致）。
- **本版可感知变化（修复）**：托盘右键菜单点击外部自动收起、开界面后再右键不再失灵（#263）；安装程序 UAC 提权提示 5 秒内前置或任务栏强闪烁（#265）；**HTTP 局域网部署「数据与打印」图片预览 / 打印崩溃修复（`crypto.randomUUID` 非安全上下文不可用，uuid 包替换，#268）——受影响环境升级本版自愈**。
- **本版可感知变化（体验）**：下载中心快速访问三 tab——Windows / Android「最新上传」卡（货架语义）＋「服务端信息」大二维码卡（PDA 扫码枪键盘直填零集成，决策 #169）；客户端首见自动 5 步气泡导览（核心链总览、每浏览器一次、可跳过 / 状态栏重看，决策 #170）。
- **版本同步**：ServerOptions / HostOptions `ProductVersion` 与 e2e compose 默认版本更新为 `0.32.0`（客户端「检查更新」比较口径随升级，决策 #126）。

## v0.31.0 迭代 102～106 汇总发布 · 2026-09-29

- **打包范围**：v0.30.0 之后合入 master 的全部迭代与缺陷修复——安装升级：迭代 106（Windows Client MSI 运行中升级安全退出 WinHost，#229，含 #235 Formatted 剥花括号返修）；使用体验：迭代 104（工作台卡片操作收纳＋删除类操作警示统一，#225，含 #237 编辑图标满幅小修）与迭代 102（下载文件名中文变下划线修复，#220）；部署形态：迭代 103（离线部署包固定目录升级口径＋开箱自动分发 packages，#222 并入 #221）、#213 返修（#219 非 root 部署者挂载目录预建）与迭代 105（离线包可选接入外部 Docker 网络，#230）。详见各迭代条目。
- **本版可感知变化（Windows 客户端升级治本）**：Client MSI 覆盖升级不再出现「文件占用循环 / 强杀进程」——安装器在 `CostFinalize` 后请求 WinHost 安全退出（等待上限 15 秒，CA 协调退出实测约 4 秒），打印忙态明确拒绝且作业不丢不重打（老宿主短并发窗口已刻画，决策 #163）；静默升级约 8 秒有界完成。首轮候选的 MSI Formatted 剥空 `catch {}` 花括号缺陷（CA 1722）已在验收轮抓出并返修（#235），纯净 VM 四场景（全 UI / 静默 / Bundle 链接 / 卸载）整轮复验全绿。
- **本版可感知变化（管理界面）**：工作台卡片脚改「编辑（主色列首）＋打印（常驻）＋⋯ 折叠菜单」，修复迭代 85 引入的「删除」按钮在最窄卡片被整枚裁掉不可见不可点的回归（修订决策 #152①）；删除类操作警示统一实心红＋确认弹窗（新增决策 #161）——工作台 / 插件包 / 下载中心 / 设置各确认弹窗、日志抽屉「清空」（原无确认直执行）、PDA 插件「卸载」（原点击即删）全部先确认后执行。导出 / 调试出图中文名模板下载不再变下划线（前端解析 `filename*` RFC 5987）。
- **本版可感知变化（Linux 离线部署包）**：三连修随包生效——①非 root 部署者挂载目录预建（#219，权限拒绝修复）；②固定部署目录解压（`--strip-components=1`）＋同目录升级口径（换版本号目录升级会新建空卷丢数据，#222）＋ `install.sh` 开箱自动分发 packages 三件（下载中心装完即非空，无需手工 cp）；③可选接入单个现有 Docker 网络（`network.env` 的 `LABELFRAME_EXTERNAL_NETWORK`，决策 #162）。v0.30.0 包内仍为旧版 install.sh / README，新口径以 docs/DEPLOY.md 为权威。
- **版本同步**：ServerOptions / HostOptions `ProductVersion` 与 e2e compose 默认版本更新为 `0.31.0`（客户端「检查更新」比较口径随升，决策 #126）。

## 迭代 106：Windows Client MSI 运行中升级安全退出 WinHost（#229） · 2026-09-28

- Client MSI 在 `CostFinalize` 后、`InstallValidate` 前请求 WinHost 安全退出，等待上限 15 秒；状态读取失败、发现打印中项目、API 不可达或超时均中止安装，不再强制结束进程。
- 安装器预检旧版 `/api/jobs` 的作业与逐张状态；新版 shutdown API 在队列锁内阻止退出检查与新领取 / 提交竞态。旧版预检与关闭之间的并发窗口见 DESIGN 决策 #163，待 VM 验收验证。
- 增加队列退出门闩与实际传输 lease 单测、shutdown API 忙态集成测试和 MSI 执行序 / CustomAction 结构断言。Windows 10/11 的直接 MSI、静默、Bundle 与真实打印忙态走查由独立验收会话执行，#229 保持开放。
- 返修（验收实测 AC-02/03 失败）：`ShutdownWinHost` 命令中的空 `catch {}` 被 MSI Formatted 语义剥除花括号对，CA 实际执行的 32 位 powershell.exe 解析失败（CA 1722，0.30.99 候选全新安装与覆盖升级全部中止）；探测失败分支改为非空体 `catch { $null }`，MSI 结构断言新增「Target 不含空花括号 + 经真实引擎 FormatRecord 转换后仍可解析 PowerShell」静态检查防复发，Formatted 约束补记 DESIGN 决策 #163。AC-02～05 归验收会话整轮重测。
- 验收（返修后整轮复验，2026-09-28）：从 master `e99e531` 重建候选 `0.31.99`（新版本号与失败的 0.30.99 区分），LF-Accept-Win10 纯净基线四场景整轮重测全部通过——全 UI 覆盖升级（无 RM 占用弹窗、CA 4 秒协调退出、ARP/配置正确）、静默升级（8.2 秒有界完成 exit 0）、Bundle 链接升级（向导确认页检出 0.30.0 → 0.31.99、链内 CA 返回 1、无死循环）与 MSI 卸载（静默 exit 0、CA 按条件 0 次执行、无残留无重启挂起）；忙态打印明确拒绝（设计内 1722 + 可观察文案、状态零变化），老宿主短并发窗口实测（shutdown 131ms 退出、in-flight 回滚 Pending、跨升级作业零丢失零重打），空闲对照升级成功。原失败签名 `1722 2: ShutdownWinHost` 与剥花括号形态全部场景 0 次。证据回写 #229 后结项。

## 迭代 105：离线包可选接入一个外部 Docker 网络（#230） · 2026-09-28

- 离线包 `install.sh` 支持 `network.env` 中的 `LABELFRAME_EXTERNAL_NETWORK`（进程环境变量优先）：启动前验证现有本地用户自定义 bridge 网络，生成最小 Compose 覆盖文件并将 Server 同时接入原默认网络和指定网络，提供 `labelframe-server` 别名；未配置时保持独立部署、端口和数据卷不变。
- 同目录升级保留 `network.env`，重跑脚本恢复网络；空值退回独立模式，错误配置明确拒绝。在线 Compose 分发入口及多网络不在本轮范围；部署说明和脚本回归测试同步更新，决策 #162。
- 验收：AC-01～06 由独立验收会话复核，2026-09-28 全部通过（AC-02/04 以 Release v0.30.0 原始包与 master 组包做同目录覆盖升级链补验：外部网络经标准 `install.sh` 接入、`network.env` 保留、命名卷 by-IP 数据连续），证据回写 #230 后结项。


## 迭代 104：工作台卡片操作收纳（修复删除按钮被裁回归）＋删除类操作警示统一（#225） · 2026-09-23

- **动机（#225）**：**回归**——迭代 85 卡片脚新增「打印」后四按钮平铺，最小需宽约 216px 超出最窄卡片可用宽度 176px（`.wb-grid` 轨道 `minmax(196px,1fr)`、`.wb-card` `overflow:hidden`），「删除」整枚被裁不可见不可点（2026-09-23 用户截图实证）；用户设计意见「编辑高频高亮、打印 / 导出 / 删除低频折叠」「删除按钮要醒目、警示不要轻易点击」。**警示缺口**（同日全仓审查）——日志抽屉「清空」灰 ghost 无确认直接执行（`App.tsx`）；PDA 插件「卸载」点击即删插件目录并自动重启打印服务（`MainActivity.cs`，风险最高）；`.btn.danger` 为红字浅描边轻量样式醒目度不足。
- **改动 ①（工作台卡片脚，修订决策 #152①）**：主操作 = 「编辑」（`btn sm primary` 列首），「打印」降常驻普通按钮，低频「导出 / 删除」收进 ⋯ 图标按钮（`Icon.tsx` 新增 `more` 三点图标）锚定弹出菜单；删除为 danger 菜单项、点击仍走既有确认 Modal（文案含「不可恢复」）。新增仓库首个下拉菜单组件 `web/src/components/Popover.tsx`：**portal 到 body ＋ `position:fixed` 锚定**（防 `.wb-card` `overflow:hidden` 裁剪——实施已知坑），点击菜单外 / Esc / 页面滚动关闭，配套 `.popover` / `.menu-item` 样式（`.wb-more` 图标按钮不占弹性宽度——卡片脚最窄两按钮一图标不被裁）。
- **改动 ②（删除类警示统一，新增决策 #161，实心红方案用户立项拍板）**：`.btn.danger` 升级实心红（红底白字、hover 加深）——新增 `--danger-strong` 供 hover，`--danger` / `--danger-soft` 语义不变（状态灯 / badge / banner / 错误文本等非按钮场景复用不受影响）；工作台 / 插件包 / 下载中心 / 设置各确认弹窗确认按钮（及设计器属性面板触发按钮）随类名全局统一。日志抽屉「清空」→ 实心红＋点击先弹确认 Modal（不可恢复文案，Esc / 遮罩默认取消），确认后才清空（`App.tsx`）。PDA「插件管理」卸载 → 红底白字触发按钮（`DangerButton`）＋原生 `AlertDialog` 确认（danger 文案：品牌暂不能打印 / 不可恢复 / 自动重启提示；确认按钮红字），确认后才执行卸载＋重启服务（`MainActivity.cs`）。
- **记账**：`docs/DESIGN.md` 先行修订决策 #152①（卡片主操作：打印→编辑；低频操作收纳）＋新增决策 #161「删除类操作警示规范」（触发与确认按钮实心红 danger＋确认弹窗＋Esc / 遮罩默认取消；收编迭代 93 #151 F-04 只记 CHANGELOG 未入决策表的口径；设计器元素删除可撤销惯例与 Settings 覆盖安装维持现状的边界一并记明）。
- **验证**：`pnpm lint` 零告警；`pnpm test` client / server 双模式 407 项全绿（净增 14 项回归：Popover 组件 portal＋fixed＋关闭语义 5 项、工作台卡片脚结构与 ⋯ 菜单交互与删除确认流 7 项、日志抽屉清空确认流 2 项；既有 Workbench.preview AC-03 适配菜单化删除路径）；`pnpm build` / `build:server` 通过；`dotnet build LabelFrame.slnx` 0 警 0 错、`dotnet test`（排除 Perf/Soak）951 项全绿（src/ 仅 AndroidHost UI 层改动，门禁例行）；AndroidHost Release（EmbedAssembliesIntoApk，同 CI 命令）本地构建 0 警 0 错。AC-01 视觉裁剪以结构断言＋样式走查自证（卡片脚恒 3 枚按钮、菜单不在卡片子树且 fixed）；**AC-05 PDA 交互以代码走查自证（确认对话框先于卸载执行）＋「Android 构建（PDA 宿主）」必需检查 PR CI 覆盖，真机走查归验收；AC-07 用户实际走查转 `待验收`**（恢复条件 = 用户在工作台 / 日志抽屉实际走查布局与警示效果）。
- **验收（2026-09-29 用户陪验结项）**：AC-07 用户实际走查通过——⋯ 菜单最窄窗口可达删除、模板删除 / 日志清空均先确认、实心红警示效果获用户确认；PDA 原生卸载确认记遗留观察项（实现面已由代码走查与 Android 构建检查覆盖，待设备在场时目验）。陪验发现工作台「编辑」按钮 `edit` 图标 SVG 路径仅占 24 格画布左上半幅（实际渲染约 6.5px，较同排 `printer` 偏小约 40% 且偏上，历史遗留、非本轮回归——编辑按钮被提为主操作列首位后显眼暴露），随结项 PR 换满幅铅笔路径（该图标全局唯一用点，零副作用）。

## 迭代 102：导出与调试出图下载文件名中文变下划线——前端 Content-Disposition 解析支持 filename*（RFC 5987） · 2026-09-23

- **动机（#220）**：用户反馈导出中文名模板，下载到本地的文件名中文全部变 `_`（如「货架标签」导出为 `____.lfpkg`）。根因（立项前已定位并实测）：后端 ASP.NET Core 对非 ASCII 文件名按 RFC 6266/5987 同时发两个参数——`filename=____.lfpkg`（ASCII 回退值）与 `filename*=UTF-8''%E4%B8%AD...`（百分号编码，无损）；前端 `makeFetchBlob` 解析正则命中**第一个** `filename=`（下划线回退值），未优先读 `filename*`。后端行为符合 RFC 不改（浏览器直链下载正常即证），单点修前端解析；受影响三端点：模板导出 `{name}.lfpkg`、调试出图单张 `{name}-print.png`、批量 `{name}-debug-{ts}.zip`。
- **修复（web/src/lib/api/client.ts，下载型端点解析仍收敛 fetchBlob 一处）**：新增 `parseDispositionFilename`——优先解析 `filename*=UTF-8''...`（RFC 5987，`decodeURIComponent` 还原中文；非 UTF-8 charset 不误解直接回退）；无 / 畸形（截断百分号编码）的 `filename*` 回退现行为 `filename=`，再回退调用方 fallbackName，全程容错不抛未捕获异常；纯 ASCII 名（后端仅发 `filename=`）行为不变。
- **回归测试（client.test.ts，fetch stub + Response headers 实测头形态，净增 6 项）**：双参数实测头 → 解析出中文名（exportTemplate / renderImage 两条调用链各一）；纯 ASCII 未加引号实测形态不回归（带引号既有用例全保）；仅 `filename*`（无 `filename=`）直接解码取用；畸形值回退 `filename=` 回退值与 fallbackName 两向。
- **验证**：`pnpm lint / test / build` 全绿（前端 393 项含新增 6 项）；`dotnet build LabelFrame.slnx` 0 警 0 错、`dotnet test`（排除 Perf/Soak）951 项全绿（src/ 零改动，门禁例行）。**AC-05 真浏览器走查（管理界面导出中文名模板核对下载文件名）转 `待验收`**（恢复条件 = LF-Accept-Win10 或用户环境实际导出一个中文名模板并核对）。

## 迭代 103：Linux 离线部署包——固定部署目录解压与升级口径修正＋开箱自动分发 packages（#222，并入 #221） · 2026-09-23

- **动机（#222，两项缺陷均实测定性）**：**缺陷①（升级路径口径）**——DEPLOY §4.2 / 包内 README / install.sh 的升级口径均为「解压到与包同名的版本号目录、升级换新目录重跑，数据在命名卷 `labelframe-data` 不动」；实测（docker compose config）compose 卷未钉 `name:`，实际卷名 = `<部署目录名>_labelframe-data`——目录名含版本号时换版本目录升级即创建**新空卷**（模板 / server.db / logs.db 不跟随），叠加 `container_name: labelframe-server` 固定，按现行文档升级结果是新部署「数据全空」。**缺陷②（下载中心为空）**——`install.sh` 从不把包内 `packages/` 三件拷入 compose 挂载目录，仅在完成横幅打印手工拷贝提示（用户部署机 192.168.1.187 v0.30.0 实测：三件齐备、三挂载目录空、三 API 返回 `[]`；手动 `cp` 后即时恢复，证明缺陷仅在自动分发缺失）。原 #221（缺陷②）范围与实测证据并入本迭代（用户决策），salvage 复用其已验证实施（PR #224 `bf777f2` 经 cherry-pick，四场景容器演练与三项检查当时全绿）。
- **改动（纯 packaging / 文档，`src/` 与 `web/` 零改动；不碰 `.github/workflows/`、不改 compose.yml）**：**缺陷①**——`docs/DEPLOY.md` §4.2 解压命令改固定部署目录形式（`mkdir -p labelframe-offline` + `tar -xzf <包> -C labelframe-offline --strip-components=1`，占位只出现一处、对任意版本通用）；「幂等 / 升级」条改「同一部署目录解压新版本包重跑」口径（注明实际卷名与勿换目录的后果）；新增存量版本号目录用户迁移说明（新包解压到当初部署目录重跑）与旧版本镜像 tar 可选清理提示；「分发闭环」条同步自动分发口径。`packaging/offline/README.md`「三步部署」前补「解压（固定部署目录）」一节；常见问题「升级」条改同目录口径（含勿换目录警示）、「数据在哪」补实际卷名 = `<部署目录名>_labelframe-data`。`packaging/offline/install.sh` 文件头注释与结尾横幅升级提示改固定目录口径（纯字符串，逻辑不动）。**缺陷②**——`install.sh` 在 #219 预建挂载目录步骤之后、`docker compose up` 之前新增自动分发步骤（步骤计数 [x/5]→[x/6]）：`packages/client/*.msi` → `client-packages/`、`packages/pda/*.apk` → `pda-packages/`、`packages/plugin/*.lfplugin` → `plugin-packages/`；`cp -f` 幂等覆盖、包内 `packages/` 原件保留（重跑第 1 步 `sha256sum -c SHA256SUMS` 仍全过，故不用 `mv`）；与 `PKGDIR_BLOCKED` 语义衔接——不可写目录整类跳过拷贝并在完成输出标注，不阻断部署；单件缺失告警继续；完成横幅改「已自动分发」口径并保留后续手工增删 / 管理界面上传指引；README 分发章节同步开箱自动分进口径。
- **记账**：`docs/DESIGN.md` 决策 #160——备注「升级 = 新包目录重跑，数据在命名卷不动」修正为固定目录口径（实际卷名 `<部署目录名>_labelframe-data`、存量迁移口径）＋②补注「开箱即分发」；`COMPOSE_PROJECT_NAME` / 卷 `name:` 根因加固登记 §7「风险与未决问题」（属 compose 契约改动且只对未发布包生效，真实需求再立项）。README / install.sh 修正只对下版发包生效，已发布 v0.30.0 包内仍为旧版——DEPLOY.md 为权威在线文档，新口径对存量包用户同样可循。
- **验证**：`bash -n` 通过；shellcheck（koalaman/shellcheck:stable，LF 归一化副本）零告警。**AC-05 本地 Docker 全链走查通过**（本机 Docker Desktop linux/amd64，以 v0.30.0 Release 离线包为底、替换为本分支 install.sh / README——包缓存与 Release 附件 sha256 一致后复用）：`--strip-components=1` 解压到固定目录后关键件全在位（README / install.sh / compose.yml / .env / images / plugins / packages / SHA256SUMS）；`install.sh` 全链成功（校验 → load → up → /healthz ok）；三挂载目录自动出件（MSI 24,104,960B / APK 22,873,639B / .lfplugin 10,726,885B，与 Release 产物一致）且 `/api/client-packages` 等三 API 各 1 条非空、MSI 全量下载 HTTP 200 逐字节一致；同一目录二次解压同一包重跑幂等（SHA256SUMS 仍全过、exit 0）；`docker compose config` 卷名 = `labelframe-offline_labelframe-data`；数据连续性实证（卷内写标记 → `docker compose down` → 重跑 `install.sh` → 标记仍在）。salvage 原验证（PR #224）：ubuntu:24.04 容器非 root 真实 uid 四场景演练全过（全新分发 / 幂等重跑 / root 残留跳过标注 / 单件缺失告警）。`dotnet build` / `dotnet test`（排除 Perf/Soak）通过（无 .NET / 前端改动，门禁例行）。**AC-08 真离线复验转 `待验收`**（恢复条件 = 下版 `v*` 自动组包或 master 本地组包，全新目录 `bash install.sh` 后三列表 API 非空且两版本升级数据连续；可与 #213 AC-06 复跑合并执行）。
- **验收（2026-09-29 结项）**：AC-08 真离线复验在断网卡 VM 通过——固定目录 strip 解压、install.sh 自动分发（三列表 API 非空，全程无手工 cp）、卷名 `labelframe_labelframe-data`、同目录重跑幂等（容器不重建）、v0.30.0 Release 原始包 → master 组包同目录覆盖升级数据连续（数据卷 CreatedAt 不变、by-IP 查回同一设备且 registeredAt 一致、服务 healthy；两包同标 0.30.0 以 install.sh 行为区分），证据回写 #222 后结项。

## #213 返修（迭代 101 AC-06）：离线部署包非 root 部署者挂载目录预建——install.sh 预建三目录与 README 手动路径同步 · 2026-09-23

- **动机（AC-06 真离线走查 FAIL×3）**：纯净 Ubuntu 24.04 物理断网走查（非 root 部署者 uid 1000 + docker 组）中，按包内 README 字面把 `packages/` 三件（Client MSI / PDA APK / `.lfplugin`）拷入挂载目录全部 `Permission denied`，下载中心三个列表 API 均返回 `[]`；其余走查项（SHA256SUMS / load / up / healthz / 管理界面）全部通过。
- **根因**：`docker compose up -d` 时三个 bind-mount 宿主目录（`client-packages/`、`pda-packages/`、`plugin-packages/`）缺失，Docker 守护进程（root）自动创建为 `root:root 0755`——非 root 部署者 `cp` 被拒，「U 盘拷贝即用」主路径失效。
- **修复（采纳走查建议「预建目录」；compose 挂载形态不动——与在线版逐字节同源是 AC-01 契约与决策 #160 口径）**：① `packaging/offline/install.sh` 在 compose up 前预建三挂载目录（部署者属主——目录先于 Docker 存在则守护进程不再以 root 重建）；目录已存在但当前用户不可写（历史 root 残留）时输出警告与 `sudo chown` 处置提示，不自动 sudo、不阻断服务部署，完成输出同步标注受阻目录；既有幂等重跑语义不变（另有「路径被同名文件占用」fail-closed 检查）。② `packaging/offline/README.md` 手动三步走路径第 2 步补「首次 up 前先 `mkdir -p` 三目录」说明，「分发安装包」一节补目录属主要求与上传替代通道，常见问题新增「拷入安装包报 Permission denied」条目（成因 + chown 处置 + 预防）。
- **验证**：`bash -n` / shellcheck 通过；本机关键段实机演练通过——目录预建（三目录以部署者属主创建）、root:root 残留警告分支（Linux 容器内真实 uid/属主语义复现）、幂等重跑静默通过、同名文件占位 fail-closed；`dotnet build` / `dotnet test`（排除 Perf/Soak）通过（`src/` 零改动）。**AC-06 真离线复跑走查留待验收侧**（合并后转 `待验收`）。

## v0.30.0 迭代 96、99、100、101 与 #171 返修、#199 流程治理汇总发布 · 2026-09-23

- **打包范围**：v0.29.0 之后合入 master 的全部迭代、缺陷修复与流程治理——打印生态：迭代 96（PDA 插件机制——`.lfplugin` 跨端契约与 AndroidHost 外置插件通道双档模型，决策 #157）与其收口（fake 插件 API 面约束，决策 #159）；安装升级：缺陷返修 #171（升级链获取阶段三重缺陷，决策 #156）；部署形态：迭代 101（Linux 离线部署包，决策 #160，修订 #64）；依赖与性能：迭代 100（TemplateFrame.Excel.Simple 2.4.1，决策 #158）；流程与测试债：#199 release.yml 签名链修复（流程治理）、迭代 99（nightly-perf 治理，决策 #155）、#183 残留补治（#208）。详见各迭代条目。
- **本版可感知变化（覆盖升级治本）**：v0.28.0 → v0.29.0 覆盖升级确定性失败的获取阶段三重缺陷全部修复——四个落位 / 清理包 `CacheId` 版本化（升级零缓存哈希冲突 `0x80091007`）、BA 容器获取注入运行中引导 EXE 为首搜索路径（根治重跑 `WixAttachedContainer 0x80070002`）、缓存重试指数退避 + 连续无进展三次终止进失败页（日志有界）；真机复验四组断言全绿（#171）。旧版本引导器无法事后修复，**升级本版引导器即治本**。
- **本版可感知变化（PDA 插件机制）**：`.lfplugin` manifest 新增可选 `platforms` 字段（无字段的存量 Windows 包零迁移）；PDA 宿主支持外置插件动态加载——配置页新增「插件管理」子页（浏览服务端插件包 → 安装 / 卸载，自动重启打印服务生效）、「连接打印机」新增品牌选择（Zebra 内置 + 已装插件动态扩展，插件品牌固定网口 tcp 起步）；AndroidHost 本地 HTTP 新增插件三端点（与 WinHost 同构）；插件 API 面按近似 netstandard2.0 级保守约束（决策 #159，禁 .NET 6+ 新增 BCL API）。
- **本版可感知变化（部署与依赖）**：Release 首发 **Linux 离线部署包**单附件 `labelframe-offline-<版本>-linux-x64.tar.gz`（镜像 tar（ghcr 全名 tag 保留）+ 与在线版逐字节同源 compose + 版本钉定 .env + `install.sh` 强制 SHA256SUMS 校验 + packages 三件（Client MSI / PDA APK / zebra `.lfplugin`）+ 管理界面预解压开箱可用；`bash install.sh` 一键离线部署，U 盘 / 内网拷贝场景 fail-closed）；Excel 导入升级 TemplateFrame.Excel.Simple 2.4.1——第三方「表头不在 A 列起始」表格不再错读 / 丢列、损坏 XML 容错、读性能优化（上游 5000 行样本 1690ms → 199ms），存量模板跨版本续填实证不回退。
- **本版可感知变化（发版链）**：release.yml 签名链缺陷修复（#199 四层）——签名 Secrets 配置后双 MSI 与引导 EXE 首次真正完成 Authenticode 签名（此前证书从不落盘、静默不签名）；nightly-perf 治理（迭代 99）——Perf 失败语义独立显红 + 阈值分环境口径，恢复性能监控信号。
- **版本同步**：ServerOptions / HostOptions `ProductVersion` 与 e2e compose 默认版本更新为 `0.30.0`（客户端「检查更新」比较口径随升，决策 #126）。

## 迭代 101：Linux 离线部署包——Release 附件自带镜像 tar + 同源 compose + 分发产物（决策 #160） · 2026-09-23

- **动机（#213）**：离线 / 内网 Linux 服务器部署此前只有 systemd 裸机一条路（`install-server-linux.sh --manifest <布局目录>`，迭代 71）；而 DEPLOY §4 推荐的 Docker 形态必须联网拉 ghcr 镜像——在线推荐 Docker、离线只有裸机，形态倒挂。用户 2026-09-23 四项拍板：不带 linux-client 镜像、带 `packages/`、带 `install.sh`、真离线自验进发版流水线。
- **决策（#159，修订 #64「Release 不含 docker 离线包」）**：发版新增单附件 `labelframe-offline-<版本>-linux-x64.tar.gz`，结构 = 镜像 tar（`docker save | gzip`，**ghcr 全名 tag 原样保留**：load 后本地命中 tag、compose 默认 pull=missing 不再联网拉取，包内 `compose.yml` 因此与在线分发附件**逐字节同源**防漂移）＋ 版本钉定 `.env` ＋ README / `install.sh` ＋ `SHA256SUMS`（镜像 + 分发产物全件，`install.sh` 强制校验，U 盘 / 内网拷贝场景 fail-closed）＋ `packages/`（Client MSI / PDA APK / zebra `.lfplugin`——拷入挂载目录即经下载中心分发，离线环境分发闭环）＋ 管理界面 zip **预解压**至 `plugins/web-ui/`（即 compose 默认挂载的服务端默认 WebUiPath，管理界面开箱可用，免手工解压）。整包不进 install-manifest（与 compose 分发附件同口径：自足分发单元而非可安装产物，#116 / #134）。
- **流水线接线（release.yml）**：docker job 推 ghcr 后 `docker save | gzip` 导出原料（只取版本 tag）；新增 `offline-bundle` job（needs docker + package + android）调 `scripts/make-offline-bundle.sh` 组包 → 对最终 tar.gz 的 fail-closed 结构断言（README / compose / .env / SHA256SUMS / 镜像 tar / packages 三件 / 管理界面逐项在场）→ **真离线自验**：与 docker job 不同 runner 天然无本地镜像，解压最终 tar.gz 后以包内 `install.sh` 走用户路径全链（校验 → load → compose up → `/healthz` 就绪），不可用即整次发版失败；release job needs 增列并挂附件（`fail_on_unmatched_files` 兜底）。
- **用户侧**：解压后 `bash install.sh` 一键（SHA256SUMS 校验 → load → `docker compose up -d` → 就绪轮询 + 访问地址与分发指引输出），或按包内 README 三步手动；幂等重跑，升级 = 新版本包目录重跑（数据在命名卷 `labelframe-data` 不动）。文档 DEPLOY §4 快速启动补离线指引、新增 §4.2。
- **验证**：AC-02 本地 Docker 全链演练通过（构建镜像 → 组包 → 篡改拒装 → load / up / healthz / 管理界面 → 幂等重跑），证据见 #213；AC-01 结构断言随组包脚本落地、AC-03 接线随本 PR；**AC-04 发版流水线全链与 AC-06 用户离线环境走查转 `待验收`**（恢复条件 = 下一版 `v*` tag 发版 / 真实离线环境可得）。
- **验收（2026-09-29 结项）**：AC-04 已随 v0.30.0 tag 发版实证（offline-bundle job 全链 + Release 附件在场）；AC-06 真离线走查经 #219 返修后在断网卡 VM 复验通过（install.sh 全链 / healthz 精确 ok / 管理页 200 / 下载中心三端点自动分发非空 / Client MSI 全量下载 SHA-256 三方一致 / 篡改 fail-closed 拒装；走查所用 master 本地组包工件的 CRLF 行尾修复经过披露于 #213），全部 AC 达成，证据回写 #213 后结项。

## 迭代 100：依赖升级 TemplateFrame.Excel.Simple 2.0.0 → 2.4.1——Excel 导入容错与读性能（决策 #158） · 2026-09-23

- **动机（#210）**：上游已发 2.4.1，与本仓 Excel 导入场景直接相关的改进——2.2.0 修复「表头不在 A 列起始的第三方表格错读（前几列恒空、末列静默丢弃）」与「zip 有效但 XML 损坏时漏裸 `XmlException`」；2.4.0 读性能优化（行 / 单元格索引复用，上游 5000 行样本 1690ms → 199ms）；2.4.1 文档与包描述修正（直接上 2.4.1）。全仓唯一引用点 `src/LabelFrame.Core/LabelFrame.Core.csproj`，产品代码零改动。
- **兼容性（升级前评估实证）**：上游全系列钉死 `DocumentFormat.OpenXml 3.3.0`（无传递依赖冲突，升级后断言复核）；2.1.0 破坏性变更（`ITemplateEngine` 移除 DIM 成员）不涉及本仓（只用 `SimpleExcel` 静态类，不实现引擎接口）；传输插件包 `HostProvided` 排除清单按 DLL 文件名匹配，文件名不变无影响；用户手中 2.0.0 生成的存量模板跨版本读取已实证不回退。
- **回归测试补强（AC-03）**：`test/LabelFrame.Core.Tests/Excel/ExcelTableReaderRegressionTests.cs` 新增 2 项——① **续填行识别**（2.0.0 升级修复场景，防上游回退）：`ExcelTemplateWriter.CreateTemplate` 生成模板（命名区域 `TF_Table` 仅覆盖表头 + 示例行）→ OpenXml 模拟用户在命名区域下方续填 2 行（inline string，Excel 手工输入形态）→ `ExcelTableReader.ReadTextTable` 断言全部 3 个数据行（含续填行）识别、值正确；② **非 A 列起始表头读取**（2.2.0 修复场景）：OpenXml 直接构造表头在 C/D 列的第三方表格（A / B 列空）→ `SimpleExcel.Read` / `ExcelTableReader` 双层断言表头与数据完整读出、无前导空列、无末列丢失。测试项目显式引用 `DocumentFormat.OpenXml 3.3.0`（与 Core 传递依赖同钉，防版本漂移）。
- **不在范围**（#210 明示）：上游新能力（`ParseDetailed` / `ConversionFailed` 告警 / 服务层并发与映射缓存改进）的接入启用；v0.29.1 发版决策（与本迭代正交）。真机「下载模板 → 续填 → 导入」冒烟转 `待验收` 欠账（AC-05，恢复条件 = LF-Accept-Win10 或用户环境实际跑一轮）。


## #171 返修：升级链获取阶段三重缺陷——CacheId 版本化 / 附加容器获取本地源注入 / BA 重试热循环终止（决策 #156） · 2026-09-22

- **动机（#171 真机验收走查不通过）**：v0.28.0 → v0.29.0 覆盖升级三次尝试均无法越过获取阶段——①落位 / 清理包常量 `CacheId` 跨版本共享 `%ProgramData%\Package Cache\` 子目录，PayloadTool.exe 每版本哈希必变 → 升级会话缓存校验必冲突（`0x80091007` 删缓存转重取）；②重跑会话解析附加容器即 `WixAttachedContainer 0x80070002`（预删冲突缓存目录仍失败——与 ① 独立）；③BA 包级缓存重试无退避无终止（`e346` 热循环：实测 5 分钟 92,993 次重试、Burn 日志 219–240MB、向导滞留进度页永不出现失败页）。
- **引擎级定论（WiX Burn v7.0.0 源码考证 + 沙箱取证）**：`Compressed="yes"` 载荷全部收入 `WixAttachedContainer`（EXE 尾部附加容器，偏移 = `cbEngineSize`）；引擎按**运行中 EXE 实测尺寸**判定容器随行与否（section.cpp），而 Burn 存在两类**设计上不含附加容器**的「引擎桩副本」——提权工作副本（`.be` 目录，`CopyEngineWithSignatureFixup` 只复制 `cbEngineSize` 字节；ShellExecuteEx 无法继承句柄）与包缓存中的 Bundle EXE 本体（提权会话缓存调用链 apply.cpp `ApplyRegister` → `ElevationSessionBegin` → registration.cpp `RegistrationSessionBegin(wzEngineWorkingPath)` → cache.cpp `CacheCompleteBundle` 全程以 `.be` 工作副本为源——真机实测 0.28.0 缓存 EXE 1,576,024B < 原件 2,032,368B 即此设计而非损坏）；桩会话取内嵌载荷必须走源解析搜索路径，而默认搜索序对附加容器是**结构性死路**——附加容器内嵌文件名 = `bundle-attached.cab`（构建中间产物名，分发机器任何目录不存在 → 「搜索目录 + 相对名」路径永不命中）＋运行自包缓存的会话不派生原始源目录（`-burn.originalsource` 只设变量不派生目录）＋附加容器无 DownloadUrl（无下载兜底）→ 桩形态会话 `0x80070002` 确定性失败（e054 只可能出自桩形态会话——与真机「首装成功 / 重跑三连败」观测一致）；**分水岭 = 原始源可用性（沙箱实测补强）**：原始源 / 最近源目录可用且原件在位时引擎原生自救（`i336: Acquiring container … copy from <原始源>`），原件不在位或 per-machine 提权子进程内变量不可用（真机三连败形态）即结构性死路。
- **修复①（Bundle.wxs）**：四个落位 / 清理包 `CacheId` 版本化（`WebUiPlacement.<BundleVersion>` 等四包）——每版本独立缓存目录，升级零哈希冲突；`WebView2Runtime` 不动（`Cache="remove"` 无驻留冲突面）。
- **修复②（BA）**：容器获取事件（`CacheAcquireBegin` 容器形态——payload id 为 `string.Empty` 而非 null，沙箱实测修正判定）注入运行中引导 EXE（`WixBundleOriginalSource`，BA 非提权进程侧读数跨提权边界成立）为 `WixAttachedContainer` 首搜索路径（引擎仍按尺寸 / 摘要探测校验，注入失效无害落回引擎默认序）；补容器源诊断日志行（原始源在位性）——同类故障此前无 BA 侧可定位信号；边界：裸缓存桩重跑且原始源变量未建立的 ARP 维护会话不触发注入（引擎边界、非生产升级路径，沙箱 S4 如实记录该形态由 ③ 保证有界失败）。
- **修复③（BA + 核心库）**：新增 `CacheRetryPolicy`（净逻辑入 `LabelFrame.Bootstrapper`，net10 测试锚定）——三个缓存重试点（包级兜底 Retry / 载荷获取 Retry / 校验 RetryAcquisition）共用每包预算：指数退避（500ms × 2^(n-1)，上限 30s）+ **连续无进展三次终止**（「进展」= 多源回退源游标前进，合法换源不衰减）+ 终止交引擎失败回滚进既有失败报告页（「重试」= 全新 Plan + Apply）；每轮 Apply 复位。
- **测试**：新增 `CacheRetryPolicyTests` 7 项（退避调度 / 无进展终止 / 换源不衰减 / 进展后重新起算 / 复位 / 未知包 / 上限钳制）；新增 `scripts/test-bundle-upgrade-acquire.ps1`（合成 0.90.1→0.90.2 双 Bundle 升级链走查——复现-验证证据载体，场景 S1/S4（桩裸重跑复现 `0x80070002` + 热循环）/ S5（原始源在位注入验证）/ S2（升级全链路）/ S3（不可达源快速失败），支持 `-UserScope` 免提权用户态形态与 `-SourceRoot` 基线源构建）。全量 dotnet build 0 警 0 错 / test 930 项全绿（排除 Perf/Soak，净增 7 项）。
- **沙箱取证（-UserScope 用户态形态，2026-09-22 实测）**：基线（常量 CacheId + 未修 BA）——S2 升级会话四载荷 `0x80091007` 哈希冲突删缓存转重取、S4 桩会话 `WixAttachedContainer 0x80070002` + `e346` 热循环 30 秒采样 17,661 次 / 日志增量 41.87MB、S5 原始源在位时引擎原生 `i336` 自救（分水岭证据）；修复后（版本化 CacheId + 修复 BA）23 项断言全绿——S2 升级全链路（RelatedBundle 检测并移除旧链（子会话非交互执行留痕）→ 重落位 → 凭据 0.90.2 → exit 0，全程零哈希冲突）、S4 退避 500ms→1s→2s 后无进展终止进失败报告页（日志有界）、S5 注入生效（「在位，已注入为容器本地源」+ 容器经注入路径解析）、S3 不可达源快速失败页（日志 0.02MB，对照真机 219–240MB）。
- **遗留与边界**：旧引导器（≤ v0.29.0）升级失败无法事后修复（运行新引导器即治本——与 #153 同口径）；旧版本缓存目录可能残留约 3MB/版本（目录名隔离，无害）；是否出 v0.29.1 补丁版属发版决策（未推 tag）；真机复跑走查由验收侧执行（#171 维持待验收）。
- **验收结项（2026-09-23 真机复验通过）**：LF-Accept-Win10 纯净基线四组断言全绿——①升级全程零 `0x80091007`、版本化缓存目录成套落位；②提权桩会话内嵌容器载荷全量获取校验、零 `WixAttachedContainer 0x80070002`；③净结果（旧 Bundle 链尾卸载真实执行 +「新先旧后」序直接证据 / ARP 0.28.0 双条目消失 / 凭据 0.28.0 → 0.29.99 / 服务 Running / healthz 200）；④死源场景 11 秒终止、日志 0.031MB 有界、失败页呈现、回滚干净零悬挂 ARP。#171 原始两观察点达成，[证据](https://github.com/marci-labs/LabelFrame/issues/171#issuecomment-5781558411)；v0.29.1 发版仍为用户决策项。

## 迭代 96：PDA 插件机制——.lfplugin 跨端契约与 AndroidHost 外置插件通道（双档模型） · 2026-09-22

- **动机（#185）**：仓库未来引入非 Zebra 品牌标签打印机（国内 TSPL 兼容机为主力预期）时，PDA 宿主能以「外置插件」方式接入新品牌——管理员经服务端集中分发、PDA 在线安装，而不是为每个品牌发 APK 或等宿主发版（用户 2026-09-21 定稿：PDA 走插件模式，否决「一个品牌一个 APK」主路线）。
- **契约（决策 #157，先文档后代码——强化路径）**：`.lfplugin` manifest 新增可选字段 `platforms`（小写平台 id `windows` / `android`，单包单端起步；**无该字段的既有包按 Windows 端解释**——存量 zebra 官方包零迁移，`[]` 空数组非法拒绝）；三层校验新增**平台门**（宿主平台不在声明集合内拒绝：Windows 拒 `android` 包、PDA 拒 `windows` 包与未标记存量包），与内置 id 拒绝、ALC 预检同属安装端校验（服务端 `plugin-packages` 不做平台过滤，列表视图增量透出 `platforms`）；官方插件版本比较（#123 ④）语义双端一致；指令集分级登记：PostScript 不做、CPCL / ESC-POS 需求触发占位（ESC 语义待用户确认）、TSPL = 迭代 97。
- **双档模型与 PDA 通道**：SDK 档 Zebra Link-OS 维持内置 APK（决策 #111 口径不变，外置化评估结论 = 不做）；轻量档纯托管插件外置动态加载（含 Java binding / 原生 so 的厂商 SDK 插件不可行——JNI 类型注册是构建时机制）。Android 侧安装目录 = `{FilesDir}/plugins/<pluginId>/` 一插件一目录（语义对齐 Windows `%ProgramData%\LabelFrame\Client\plugins`：卸载 = 删目录 + 重启生效，不做运行时热卸载）；加载生命周期完全继承决策 #127（包目录单 collectible ALC 整体字节加载 + 注册表强持有 ALC 根；单插件加载失败 HostLog + 已装列表 loadError 留痕不阻断宿主）。实现载体：`PluginInstaller` 自 WinHost 下沉 Core（双端共用，Windows 行为不变）、AndroidHost 新增 `PluginHost` 装配 + `AndroidTransportFactory` 品牌路由（zebra → 内置 SDK 既有路径原样；插件品牌 → 插件传输 `{ connectionType: tcp, host, port }`；配置引用未安装插件回退 zebra + 中文留痕）。
- **PDA 界面与端点**：配置页新增「插件管理」子页（已装列表含加载失败原因 + 浏览服务端 `plugin-packages` 下载安装 + 卸载，安装 / 卸载自动重启打印服务生效）；「连接打印机」子页顶部新增品牌选择（Zebra 内置 + 已装插件动态扩展——决策 #95 预埋兑现，配置 `{ brand, connectionType, host, port }` 结构零变更；插件品牌固定网口 tcp 起步，Zebra 三连接方式不变）；AndroidHost 本地 HTTP 新增 `GET /api/plugins/installed` / `POST /api/plugins/install`（原始包字节）/ `POST /api/plugins/uninstall`（与 WinHost 同构，JS 桥可达）。
- **fake 插件单测矩阵（AC-03，19 用例全绿）**：新增 `test/LabelFrame.TransportPlugin.Fake`（纯托管假想品牌插件：发送内容落盘留痕）+ `PluginInstallerCrossPlatformTests`（platforms 契约解析 / 平台门双向 / 坏包拒绝——损坏 zip、manifest 缺失、非 zip、无 DLL、DLL 非程序集、内置 id 冲突、manifest id 不一致 / 官方版本比较（android 路径幂等 + 降级拒绝）/ 安装→加载→发现→装配→发送全链路 / 装后损坏 loadError 留痕）；既有 WinHost 插件 28 用例随 PluginInstaller 下沉全数通过（Windows 行为零回归）。
- **Spike 资产（AC-02 真机部分转待验收）**：`scripts/build-fake-android-plugin.ps1`（构建 `labelframe-transport-fake-<版本>.lfplugin`，platforms=android，宿主必带程序集排除对齐打包脚本）+ `scripts/test-pda-plugin-spike.ps1`（Release AOT APK 构建 → adb 安装 → 本地 HTTP 装插件 → 重启 → 发现 loaded=true → 配置路由 → 测试打印 → run-as 取证 sink 落盘 → 回退清理，全链路逐步 PASS/FAIL 输出）。worker 沙箱已实证：fake 包构建成功（0MB/1 程序集）+ Release AOT APK 构建成功（22.8MB，EmbedAssembliesIntoApk，与 CI「Android 构建」同型）；**真机动态加载实证（UROVO DT50 同级）转待验收**（恢复条件 = 真机可得，验收侧执行走查脚本；若跑出失败按 fail-fast 中止机制段转用户评估）。
- **不在范围**（#185 明示）：Zebra SDK 外置化 / 全插件化；PDA 侧 ZPL 命令打印（§5.4.7 维持远期）；任一真实指令集插件（TSPL = #186）；插件检测更新 / 自动更新（归「服务端分发枢纽」主题）；「一个品牌一个 APK」构建变体；Android 平台打印 API。
- **验收结项（2026-09-23 真机走查通过 + 插件 API 面约束决策 #159）**：AC-02 Spike 真机实证按 fail-fast 双出口收敛——首轮（UROVO DT50，Release AOT）机制四段（安装→重启装配→动态加载发现 Loaded=true→品牌路由，含删目录卸载语义）真机通过、发送段命中 interpreter BCL 可解析面缺口（`Method not found: string System.String.ReplaceLineEndings(string)`——宿主 AOT 图未引用的 .NET 6+ API，方法体解析期 MissingMethodException）；用户 2026-09-23 拍板：① **方案 A 插件 API 面约束先行**（决策 #159：近似 netstandard2.0 级保守 API 面，禁 .NET 6+ 新增 BCL API；fake 插件即门槛用例；方案 B「调宿主 AOT 配置扩大可解析面」记 DESIGN §7 未决）；② 走查脚本缺陷立即小 PR（PR #212：`function Adb` 遮蔽死递归 / Release 产物 crc* Activity 动态解析 / adb reverse→forward 方向 + 本地端口避让 + 取证步 run-as 不可用回退 logcat 双断言）；③ AC-02 记账口径 = 「fail-fast 形态达成」。约束面版 fake（`ReplaceLineEndings` / `ThrowIfNull` 改保守等价实现，19 项单测全绿）真机复跑：**全链含发送段通过**（测试打印作业 Completed 1/1，logcat `[FAKE] 模拟发送到 127.0.0.1:9100：38451 字符`，无 BCL 缺口）；**AC-04「插件管理」真机走查全链通过**（沙箱服务端 plugin-packages 浏览→UI 安装→「连接打印机」品牌选项卡扩展→配置 127.0.0.1:9100→测试打印「✓ 打印成功（1/1）」→重启后卸载干净 `installed=[]`、品牌回退 Zebra；截图 17 张 + logcat 取证留 Issue）。AC 终态：AC-01 ✅ / AC-02 ✅（fail-fast 形态达成）/ AC-03 ✅ / AC-04 ✅ / AC-05 ✅（[证据](https://github.com/marci-labs/LabelFrame/issues/185#issuecomment-5787771580)）。


## 流程治理（#199）：release.yml 签名链修复——bundle job 数组 splat 改哈希表＋MSI job 证书 env 映射补齐 · 2026-09-22

- **现象（v0.29.0 发版实证，run 35683434328）**：tag v0.29.0 首跑失败于「构建安装引导 EXE（Burn Bundle）」job——`A positional parameter cannot be found that accepts argument 'artifacts/cert/labelframe.pfx'`；「创建 GitHub Release」被跳过，出库未发生。
- **根因（三处叠加；签名 Secrets 配置后链条首次被真正走到即暴露）**：①bundle job 构建步骤以**数组** `$sign = @('-Sign','-PfxPath',…)` 做 `@sign` splatting——数组 splat 按位置逐元素绑定，`-Sign` / `-PfxPath` 被当作位置实参即报错（应**哈希表** splat 按参数名绑定）；②MSI job「准备签名证书」步骤缺 `env: MSI_SIGN_CERT_BASE64` 映射（#169 已定位的遗留）——证书从不落盘、`Test-Path` 恒假、双 MSI 静默不签名（两 job 证书从未在场也是 ① 此前不暴露的原因）；③MSI job 两打包步骤缺 `env: MSI_SIGN_PASSWORD` 映射（修复 ② 后即暴露的连带缺口）。
- **修复**：三处数组 splat 改哈希表 splat（`@{ Sign = $true; PfxPath = …; PfxPassword = … }`）；MSI job 证书步骤补 `MSI_SIGN_CERT_BASE64` env、Client / Server 两打包步骤补 `MSI_SIGN_PASSWORD` env；Secrets 未配置时各步骤仍按既有行为优雅跳过。
- **验证（AC-3）**：哈希表 splat 语义实证（同签名 dummy 函数：`Sign=True PfxPath=artifacts/cert/labelframe.pfx PwLen=9`——按名绑定正确）；release.yml YAML 解析通过（7 个 job 齐全）。出库恢复走 `workflow_dispatch`（version=0.29.0）——tag 不删不重推（迭代 94 / #167 先例：产物源 master 与 tag 仅差本 workflow 修复）；出库核验证据回写 #199。
- **根因第四层（dispatch 首跑实证，run 35685388254；修复① ② 后签名首次真正执行再暴露）**：`assert-client-deps-baseline.ps1` 经 WindowsInstaller COM `OpenDatabase`（只读）读 File 表后仅 `View.Close()`，`Database` / `Installer` / `Record` 的 RCW 未显式释放——该脚本被 `build-msi.ps1` 进程内调用（`&` 调用），只读句柄随 RCW 存活持有文件共享读锁，GC 未及时回收时紧随的 signtool 签名写 MSI 即被拒（`SignTool Error: The file is being used by another process`）。修复：循环内释放 `Record`、查询完 `ReleaseComObject` 三件套（view / db / installer）＋ `GC.Collect` + `WaitForPendingFinalizers`；v0.28.0 前签名从不执行故未暴露，仅 Client MSI 存在此「断言→签名」链（build-server-msi / build-bundle 无 COM 读 MSI）。
- **不在范围**：release.yml 签名 fail-closed 断言（#169 建议，后续单独治理）。

## 迭代 99：nightly-perf 治理——Perf 步骤失败语义修复与阈值 CI 口径重标（流程治理） · 2026-09-22

- **动机（#198）**：nightly-perf 9-14、9-21 连续失败且 soak / 微基准被连带跳过，近两周性能监控信号丢失。排查定性三层：① Perf 步骤两条 `dotnet test` 合写一个多行 pwsh 块、步骤退出码只取末条命令——Server Perf 断言失败自首轮（8-31）起被掩盖（8-31 / 9-7 两轮「成功」日志实有 FAIL）；② 阈值按开发机标定（p50 恒 3-9ms），GitHub 共享宿主实测 51-1169ms 且轮间抖动 10-300 倍无判别力——同代码本机复跑全达标（Routing device-1/5/20 p50=9/7/6ms、WinHost 单张 p50=8ms），排除代码回归；③ 9-14 起 runner 镜像 `win25-vs2026/20260824.214 → 20260907.229`，WinHost 单张 p50 20ms→31ms 作为末条命令首次把步骤顶红。
- **失败语义修复**：nightly-perf.yml Perf 段拆「延迟 / 并发（Server Perf）」「延迟（WinHost 单张 Perf）」两个独立步骤，失败独立显红、日志各自可定位，不再被退出码吞掉；actions 升级 `checkout@v4 → v7`、`setup-dotnet@v4 → v6`（清除 Node 20 弃用强制 Node 24 警告）。
- **阈值分环境口径（决策 #155）**：`GITHUB_ACTIONS` 区分——本地严格口径原样保留（Routing p50 < 50ms + p95 分层 2s / 5s；WinHost 单张 p50 < 20ms / p99 < 500ms）；CI 上 Routing 改「无错误 + p95 分层」硬断言（≤5 设备 < 5s、20 设备 < 10s）、p50 转观测，WinHost 单张 p50 < 100ms / p99 < 1s（按 4 轮 CI 实测极值留 ≥3 倍余量）。
- **文档**：`docs/PERF-BASELINE.md` 补 CI 分档口径、4 轮实测数据与掩盖事故记录；`docs/DESIGN.md` 决策 #155 + §7 环境敏感条目同步。

## v0.29.0 迭代 85、95、98 汇总发布 · 2026-09-22

- **打包范围**：v0.28.0 之后合入 master 的全部迭代、缺陷修复与流程治理——安装引导体验：迭代 95（安装引导程序体验收敛——两层问卷、清单自动加载与形态精简，决策 #151）、缺陷修复 #173（引导器 WebView2 evergreen 载荷校验改微软 Authenticode 发布者验签＋已装跳过 acquire，决策 #153）；使用体验：迭代 85（工作台模板直达打印与作业明细查看，决策 #152）；外部集成：迭代 98（打印任务终态回调 webhook，决策 #154）；流程与测试债：迭代 94（release.yml 前端产物自检特征过时修复与 v0.28.0 恢复出库，决策 #150）、CI 偶发失败治理（前端测试时序确定性改造与 FailureLog 超时根因修复，#183）、流程卫生（会话临时产物生命周期机制，#176）、测试债 #181（升级走查脚本横幅断言锚点对齐）。详见各迭代条目。
- **本版可感知变化（安装引导程序）**：就绪页进入即自动加载安装清单（命令行 / 邻接布局 / 稳定通道三来源），基础问卷收敛为三步（打印机品牌 → 服务端地址 → 简版确认），「高级选项」折叠承载角色模式 / 管理界面开关 / 组件明细；Docker / Linux 形态移出向导改文档指引；含客户端角色的安装把服务端地址装后自动写入 `settings.json`（装完即连，无需进设置页）。引导器对 WebView2 evergreen 载荷改微软 Authenticode 发布者验签——修复 v0.28.0 起微软轮换直链背后文件后、全新装机哈希校验必然失败（`0x80091007`）的缺陷（旧版引导器无法事后修复，升级本版引导器即治本）；已装 WebView2 / .NET 运行时的机器不再陪跑约 70MB 下载。
- **本版可感知变化（客户端与 Server）**：工作台模板卡片新增「打印」直达（预选模板切「数据与打印」页，两步内可打印）；作业历史行可展开——本机直连作业显示逐张明细（状态 / 失败原因），服务端下发作业显示汇总。路由模式 `POST /api/jobs` 提交可选 `callbackUrl`——作业到达终态（Completed / Failed / Expired，含失联回收）由 Server 异步至少一次回调外部系统（指数退避重试、超限死信、重启续投；HMAC 签名记 DESIGN 未决问题）。
- **版本同步**：ServerOptions / HostOptions `ProductVersion` 与 e2e compose 默认版本更新为 `0.29.0`（客户端「检查更新」比较口径随升，决策 #126）。

## 测试债修复（#183）：CI「构建与测试」偶发失败治理——前端测试时序确定性改造与 FailureLog 超时根因修复 · 2026-09-22

- **动机（#183，用户拍板 B 案）**：docs-only / 无前端改动 的 CI run 接连出现互不相同的前端时序失败（覆盖 JobHistory / DataPrint / Settings 三文件 5 处）与 WinHost `JobPrintWorkerFailureLogTests` 约 20s 超时特征失败，与改动内容无因果、与 runner 负载相关，偶发红阻塞无关 PR。
- **前端时序确定性改造（只改测试与测试基建，被测产品代码零改动）**：根因同源——AppProvider 启动链（getHostConfig → healthz / getTransport → serverMode → 各页列表加载）为多段 promise + React 真实宏任务调度，默认 1000ms 的 findBy / waitFor 与「固定轮数冲洗后同步断言」在 CI 高负载下被击穿或残留未落定渲染代际。①实证三文件：JobHistory / DataPrint 挂载链等待统一 MOUNT_WAIT 8000ms（3000ms 实证被击穿）；Settings 以 `configure({ asyncUtilTimeout: 8000 })` 统一口径，TCP 参数表单用例改为「连接配置落定（折叠头摘要出现）后再展开点选」消除与挂载期并发 mock 的渲染交错；②fake timers 用例（JobHistory 轮询 describe、DataPrint 设备探测周期、Devices 5s 轮询、Workbench.preview 全文件）：固定 1–2 轮冲洗改为有界落定循环（以「拉取已发生 + 列表已渲染 / mock 计数静默」为准出条件），消除迟到真实宏任务渲染落入计时窗口造成的轮询计数错位（实证：「2s 退避后重试」期望 2 实得 3）；③同类高频挂载用例（Workbench / App.server `configure` 统一、DataPrint.server MOUNT_WAIT 对齐、Devices / 旧连接方式用例同步点选改 `findByRole` 先等后点）；④`vitest.config.ts` 单测超时 5s → 20s（兜住全同步断言用例的墙钟停顿——实证：Settings「三分组渲染」5000ms 超时）。
- **WinHost FailureLog 根因结论（测试债，非产品债）**：worker 失败路径无锁 / 无固定睡眠（信号驱动唤醒 + WAL + 池化 SQLite、busy 超时 5s），本地 <1s 即落 Failed；CI 高负载下并行测试类放大轮询循环与 worker 的 SQLite 往返延迟，原「200 次 × 50ms 固定迭代轮询 + 15s 总 CTS 同管收尾」墙钟预算被击穿、CTS 轮询中途到期抛取消——呈约 20s 超时特征。修复：改按截止时间轮询（30s 预算 / 100ms 间隔）+ 总 CTS 放宽 60s 并与轮询解耦 + 未落 Failed 时显式断言报错（不再以取消异常形态失败）。
- **验证**：pnpm lint 0 警告 0 错误；双模式全量测试（client / server 各 387 项）全绿、`pnpm build` 全绿；dotnet build / test（排除 Perf/Soak）全绿零回归；治理过的前端测试文件连续 3 轮全绿、FailureLog 用例连续 3 轮全绿（轮次数字见 Issue 自评）。被测产品代码零改动。

## 迭代 98：打印任务终态回调（webhook）——路由模式提交附带回调 URL，终态异步通知外部系统 · 2026-09-21

- **动机（#190）**：外部业务系统经路由模式提交打印任务后只能轮询 `GET /api/jobs/{jobId}` 得知结果，成本高、时效差；期望提交时附带回调 URL（webhook），作业到达终态时由 Server 异步 POST 通知，载荷含状态与幂等键。
- **契约（决策 #154，先文档后代码）**：`SubmitJobRequest` 增可选可空 `callbackUrl`（三端共用契约向后兼容；DESIGN 决策 #154 + §5.1 同步 + §7 登记 HMAC 未决问题）；callbackUrl 只落 Server 侧 `server_jobs.callback_url` 新列（旧库自动补列），不进投递载荷——宿主零感知。
- **Server 回调投递**：三处终态转移点（宿主回报 `ReportResultAsync` / 失联回收 `ClaimedJobTimeoutService` / 超 TTL 置 Expired `PendingJobExpirationService`）经统一出口幂等登记进 SQLite `job_callbacks` 投递表；`JobCallbackDeliveryService` 后台扫描（5 秒周期）异步投递——不内联宿主回报路径，外部端点慢 / 挂不拖住打印；指数退避（30s 起翻倍）、至多 5 次、超限死信；投递扫描自带登记自愈（补偿「终态落库但登记缺失」窗口）；投递状态持久化，进程重启后未完成任务继续投递（至少一次）；作业历史清理时投递行随作业删除。
- **载荷与投递状态**：POST JSON `{ jobId, requestId, status（Completed / Failed / Expired）, completedAt, totalItems, completedItems, failedItems, errorMessage? }`（requestId 即幂等键，重复 POST 由调用方去重）；`GET /api/jobs/{jobId}` 视图新增 `callbackStatus` / `callbackAttempts` / `callbackLastError`（无回调为 null）。
- **安全边界（重试上限 / 超时 / 退避参数集中常量 `JobCallbackDeliveryPolicy`）**：URL scheme 白名单仅 http/https——提交即拒（400 + `LF_SRV_002` 中文错误，空串 / 裸字符串 / `file://` 等一律拒，作业不入队）；投递超时 10 秒；不跟随重定向（3xx 按失败重试）；响应体读弃不落地。
- **直连模式**：WinHost（53960）与 AndroidHost（53970）接受 `callbackUrl` 字段但忽略，不产生出站回调。
- **测试**：新增 23 项至 923 项（基线 900）——投递成功与载荷 schema / Failed / Expired / 失联回收三终态无漏报 / 指数退避重试（FakeTimeProvider）/ 死信 / 重启恢复（持久化）/ 登记幂等 / 非法 scheme 拒绝（file:、裸字符串、空串等 5 形态 + 端点级 400）/ 直连接受忽略 / 契约反序列化兼容；`HttpJobCallbackSender` 真实套接字测试锚定 2xx 判成功、不跟随重定向、非 2xx 与不可达分类。dotnet build / test（排除 Perf/Soak）全绿零回归；前端无消费方零改动。
- **记账**：DESIGN 决策 #154；ROADMAP 状态行随验收结项收口。

## 测试债修复（#181）：升级走查脚本两条横幅断言锚点对齐 #151 现行文案 · 2026-09-21

- **现象与根因（#175 AC-07 真机走查实证，产品零缺陷）**：`test-bundle-upgrade-walkthrough.ps1` S1/S2/S3 功能断言全绿，仅 2 条确认页横幅 UI 断言红——①横幅抓取过滤含 `*不会下载*`，抢先命中 #178（决策 #151 文案精简）后常驻确认页的执行边界横幅「点击『安装』前不会下载…」，首命中取错控件，S3「已是最新」断言随捕获文本错位；②S2 断言仍期望旧措辞「确认后将执行升级」，而确认页升级状态行现行文案为 `UpgradePresentation.Summarize` 的「检测到可用更新：…」。
- **修复**：抓取过滤收敛为 Summarize 三态锚点（`*检测到可用更新*` / `*已是最新版本*` / `*全新安装*`），移除撞常驻边界横幅的 `*不会下载*`（注释记录锚点集口径与教训）；S2 锚点更新为「检测到可用更新：服务端 0.90.1 → 0.90.2」（含版本号、与同场景 Burn 日志断言同口径），收紧宽松通配防误命中。
- **验证**：本地全量走查 S1/S2/S3 全部断言绿、exit=0（原 2 条红断言转绿，各场景捕获横幅均为升级状态行文本）；dotnet build / test（排除 Perf/Soak）零回归；产品代码与前端零改动。

## 迭代 85：模板直达打印与作业明细查看 · 2026-09-20

- **动机（#133；界面评审 #114 C-4 / C-5，用户 2026-09-17 拍板决议）**：工作台模板卡片操作只有「编辑 / 导出 / 删除」——从模板到打印须自行想到去「数据与打印」页再选一次模板；作业历史行不可点击、无明细展开，「这单打了什么、每张成败」没有查看路径，数据与打印进度区还以纯文本把用户指去别处（无链接）。
- **C-4 卡片「打印」直达（决议 1，AC-01）**：工作台卡片操作区新增「打印」（主色，列首）——`App` 侧把该模板写入打印草稿（与手动在下拉选择同一入口 `setDraftSelected`，字段值 / 调试开关等草稿行为一致）后切「数据与打印」页，两步内可填数据打印；双击卡片仍进设计器（现状不变）。
- **C-5 作业历史行展开明细（决议 2，AC-02 / AC-03）**：行首箭头（或点击行）展开——本机直连作业（WinHost 列表即返回逐张 `items`，数据已有、后端零改动）显示逐张表（每张状态徽标 / 失败原因：`errorMessage` 优先回退 `errorCode`，Log 模拟打印附出图目录与张数）；服务端下发作业（Server 不返回 items）显示汇总（状态 / 完成失败张数 / 失败原因全文），并如实占位「逐张明细仅本机直接打印的作业提供，服务端下发作业显示汇总」；「作业编号」复制按钮 `stopPropagation` 不触发展开。
- **进度区指引可点击（AC-03 附带）**：数据与打印进度区两处「可在作业历史查看」纯文字指引改为行内链接式按钮（`.link-like`，视觉为链接 / 语义为按钮），点击跳「作业历史」页；本机直连可重试作业维持「下方列表逐张重试」原文案不跳转。
- **出图查看入口（AC-02「可行则」）**：评估不做——图片在本机 `print\` 目录，浏览器无法直达本地路径，在线查看需 WinHost 新增文件服务端点（超出本轮「已有字段透传缺口」的最小后端配合边界，且属打印 API 公共契约扩展）；明细中以目录路径附注（可在资源管理器打开），设备侧出图回传 Issue 明确不在范围。
- **测试**：前端新增 9 项至 387 项（工作台打印直达 2 / 作业历史行展开 4 / 进度区指引跳转 3，含既有 G4 文案断言适配）；pnpm lint 0 警告 0 错误 + 双模式测试（client / server 各 387 项）+ 双构建（`pnpm build` / `pnpm build:server`）全绿；后端零改动，dotnet build / test（排除 Perf/Soak）零回归。
- **记账**：DESIGN 决策 #152；ROADMAP 状态行随验收结项收口。

## 迭代 95：安装引导程序体验收敛——两层问卷、清单自动加载与形态精简 · 2026-09-20

- **动机与决策（#175 用户三轮产品讨论拍板，决策 #151）**：引导界面第一受众是非技术用户，默认零技术细节；现状欢迎页一屏 6 块信息且要求用户理解「安装清单」概念手动加载，问卷五形态中 Docker 是零功能死胡同（空组件集 + 确认页拦截）、Linux 是纯展示（`linux-server` 归档不在 Burn 链、实际不下载，且 server-linux + webui 显示 Linux 路径实际落位本机 Windows 的矛盾）。
- **两层问卷（基础三步 / 高级角色模式）**：基础流程 = 就绪（自动加载）→ 打印机品牌 → 服务端地址 → 简版确认；「高级选项」（就绪页角落，D3）进入角色页（仅打印客户端（基础默认）/ 本机作为服务端 + 同机打印客户端 / 本机作为服务端）+ 管理界面开关（仅服务端角色）+ 确认页「显示明细」折叠区（D2：组件全表含来源 URL 与本机版本、清单来源覆写、离线安装说明）；不适用问卷页自动越过（`IWizardPage.ShouldSkip` + 向导壳双向跳页）。standalone / server-win 问卷面合并为「服务端角色 ± 同机客户端」语义，manifest `topologies` 标记与 schema **零修改**。
- **清单自动加载（口径变化：打开向导即联网）**：就绪页进入即按已定来源自动获取（命令行 `--manifest` 显式覆写 > 邻接布局清单 > 稳定通道 URL——`--manifest` 自此同时作用于交互安装路径，测试注入共用），成功自动进问卷；失败给可行动提示并展开手动来源区（浏览 / 重试 / 高级选项）；#75 断网自动切换维持独立 Issue。
- **server-docker / server-linux 移出问卷**：Linux 指向 `install.sh`（#134）、Docker 指向 Release `compose.yml`；webui 落位矛盾随形态移除消失；解析器契约同步收敛（`TopologyPreset` 三值、`DockerComposeGuidance` 字段移除）。
- **服务端地址采集与装后落位（D1）**：含客户端角色在问卷采集服务端地址（纯客户端必填、已装机从 settings.json 预填；同机角色预填 `127.0.0.1:53961` 可改）；Apply 成功后 BA 写 `%ProgramData%\LabelFrame\Client\settings.json`（与 WinHost `HostConfigStore` 同一事实源）——装完即连上服务端，无需进客户端设置页；升级摘要 / 新鲜度横幅随自动加载移至确认页（措辞精简、#124「确认前只读」语义保留）。
- **测试与脚本**：`QuestionnaireContractTests` 锚点适配新边界文案；拓扑 / 变量映射 docker、linux 用例移除，新增角色适用性与 ServerUrl 规范化 / 存取 / 预填用例；四个 E2E 走查脚本（download-matrix / offline-layout / uninstall / upgrade walkthrough）适配新问卷流（`--manifest` 注入 + 高级路径真实 UI 驱动）。dotnet build / test（排除 Perf/Soak）871 项全绿。
- **契约记账**：DESIGN 决策表 #151 先行 + §6.3 改写 + §6.10 / §6.11 / §7 同步；「下载中心带地址分发包」登记后续迭代方向、「局域网自动发现服务端」入 §7 未决问题。
- **不在范围**：manifest schema、源解析 / 离线布局 / 升级链机制（行为回归为验收锚点）、下载中心扩展（独立立项）、PDA 分发、失败报告机制（仅文案）。

## 流程卫生：会话临时产物生命周期机制 · 2026-09-20

- **动机**：主仓 `artifacts/` 累积 1.6 GB 过程产物（历史版本 MSI / 构建与发布暂存 / 已结项验收证据目录 / 证书生成中间 PEM）需人工清理——确立「产生临时文件的会话 / 脚本自负责清理」机制，配合既有 worktree 回收闭环（v7）补齐主仓侧生命周期。
- **迭代轮值协议 v8（`.zcode/automations/iteration-duty.md`）**：① worker 派单要点新增硬约束——临时文件与沙箱证据一律写自己 worktree 内、不写主仓 `artifacts/`（随 worktree 回收消灭）；② 「一」扫描新增主仓 `artifacts/` 对账兜底（每轮）：`cert/` 签名证书白名单常驻，其余残留（历史构建 / 暂存 / 已结项证据）即删并汇报，删不动（文件占用）留待下轮。
- **验收轮值协议 v7（`.zcode/automations/acceptance-duty.md`）**：结项流水线关闭 Issue 时同步删除本轮本地证据目录（`accept-<N>*` 等）——前提＝关键证据已以 Issue 评论 / 附件留存 GitHub，可溯后本地即删。
- **`scripts/create-signing-cert.ps1`**：PFX 导出成功后自删 `key.pem` / `cert.pem`（未加密私钥明文不留盘），删除失败显式报错提示手动处理。
- 两协议文件已入库版本化，本地事实源随 master 同步后下次轮值触发即生效。

## 缺陷修复（#173）：引导器 WebView2 evergreen 载荷校验改微软 Authenticode 验签＋已装跳过 acquire · 2026-09-20

- **缺陷与根因（#173；v0.28.0 真机实证，决策 #153）**：`runtime-webview2` 条目使用无版本 evergreen fwlink 直链且钉死 SHA-256——微软每轮换直链背后文件（同日实测四哈希），全体历史版本引导器全新装机 / 清缓存重装 fail-closed 失败（`0x80091007` 哈希不符）；且 WebView2 已装（Present）机器因 Burn「keep × Present ⇒ 尝试缓存」计划语义被迫陪跑这个注定失败的下载校验（缺陷放大器）。
- **校验策略变更（evergreen 载荷专项）**：`runtime-desktop` / `runtime-aspnetcore`（版本化直链）维持哈希钉死；`runtime-webview2`（version = `evergreen`）改**微软 Authenticode 发布者验签**——WinVerifyTrust 签名链完整 + 签名者 CN = Microsoft Corporation 双条件，任一不满足 fail-closed 拒装（免疫轮换、仍防投毒）；manifest 的 sha256 / sizeBytes 对 evergreen 条目降为信息性观测值（schema 零修改）。契约变更见 DESIGN §6.2 / §6.4 / §6.9 / §6.10（决策 #153）。
- **校验落点定案 = ExePackage 包装 PayloadTool（获取内移至包执行）**：`WebView2Runtime` 链包载体改内嵌 PayloadTool（`-install-webview2 -source [WebView2Source]`），evergreen 文件下载 / 验签 / 静默安装全部在包执行期（引擎提权上下文内）完成，引擎零远程载荷；`WebView2Source` 变量由 BA 写入（布局目录本地文件在位优先 → 清单 urls → 工具内官方 fwlink 兜底）。否决 BA 侧下载后交引擎（引擎对本地源副本同样按构建期摘要强制校验，轮换照旧失败）与构建期内嵌安装器（固化快照有违 evergreen 语义）。
- **已装跳过 acquire（配套）**：三个 runtime 包（Permanent 永不随 Bundle 卸载）`Cache` 改 `remove`——执行才缓存、结束即清，已装（Present）/ 未选组件零获取；已装机器不再陪跑 .NET 双运行时约 70MB 下载。MSI 与落位包缓存策略不动（MSI 修复需驻留缓存；落位包每次安装会话都执行）。
- **布局链路同策略**：布局生成（`OfflineLayoutBuilder` / `make-offline-layout.ps1`）对 evergreen 条目的落位 / 复用校验由 sha256 改发布者验签——微软轮换后重新生成布局不再因哈希漂移失败；安装期布局本地文件经 `WebView2Source` 交包装工具验签执行（离线首装零外网维持）。
- **构建链**：`build-bundle.ps1` 不再构建期下载 / 钉哈希 evergreen 文件（bundle 构建对轮换免疫）。
- **测试与实证**：新增单测 22 项（发布者策略矩阵 / CN 提取 / 源解析 / BA 源变量构造 / 获取编排正反例 / 布局 evergreen 策略，含真实 WinVerifyTrust 冒烟——微软签名内嵌文件通过、篡改 / 未签名拒绝），Bootstrapper 套件 255 项全绿；沙箱走查 `scripts/test-webview2-authverify.ps1` 九项全过——Part A 工具级（真实 fwlink 下载验签通过 + 篡改 / 未签名 / 发布者不符三反例 40 拒绝）、Part B 引擎级（per-user 测试 Bundle × 真实 BA：`Cache=remove` × Present **0 次请求**、默认 keep × Present 2 次请求（机制根源对照）、remove × 缺失正常获取）。
- **兼容影响与回退**：旧引导器（≤ v0.28.0）内嵌构建期哈希照旧、轮换后其全新装机仍失败（无法事后修复，只能升级引导器）；新引导器对旧 manifest 兼容；回退 = revert 本变更。发版链两阶段 evergreen 哈希跨 job 断言在轮换窗口可能误拦（fail-closed 方向，重跑即过）——出库链排查归 #169。真机全新装机走查（AC-4 真机口径）转「待验收」。
- **不在范围**：release.yml 与发布链出库路径（#173 次生发现归 #169）；应急 v0.28.1 止血（用户拍板不发）；MSI / 落位包缓存策略。
- **记账**：DESIGN 决策 #153；ROADMAP 状态行随验收结项收口。

## v0.28.0 迭代 68-84、86-87、89-93 汇总发布 · 2026-09-18

- **打包范围**：v0.27.1 之后合入 master 的全部迭代与缺陷修复——安装与分发链补全：迭代 68（流程治理：引导 EXE 随 Release 发布——release.yml bundle job 接线与 manifest 分阶段跨 job 一致性断言，决策 #132 / #134）、迭代 69（安装卸载收尾对称化——Bundle 卸载清理插件落位目录，决策 #133）、迭代 70（离线布局安装——`make-offline-layout.ps1` 与本地源无网首装，决策 #135）、迭代 71（Linux 服务端一键安装——install.sh + compose 随发版分发、linux 归档默认 self-contained，决策 #134）、迭代 72（模拟打印出图目录保留清理，决策 #136）；插件命令打印落地：迭代 76（契约设计，决策 #137）→ 77（宿主链路 1/4，`ILabelCommandCompiler` 与 `printMode` 分派）→ 78（Zebra 编译器·文本 2/4）→ 79（条码与二维码 3/4，`^BC` / `^BQ`）；使用体验：迭代 73 / 80-84 / 87（界面文案用户化与可读性系列）、74（模拟打印作业数据留痕）、75（「PDA 日志 / 设备日志」页面下线）；缺陷修复与健壮性：迭代 86（设置页空地址徽标谎报）、89（图片模式锚定文本垂直坐标语义，决策 #146）、90（Zebra 插件说明——原生指令模式 QR 不支持中文）、91（前端请求层健壮性——超时分档 / 竞态守卫，决策 #148）、92（设计器未保存离开保护，决策 #149）、93（前端交互一致性收尾——确认弹窗统一 / 横幅通用类 / lint 清零）；另有流程卫生一条（轮值协议补齐 worktree 与本地分支回收闭环，#126）与界面可用性评审（#114）。详见各迭代条目。
- **发布编排（本版可感知变化）**：推 `v*` tag 后自动构建并公开——既有产物（Server / Client 双 MSI、服务端 webui 插件 zip、linux-x64 归档、AndroidHost APK、Zebra 官方插件 .lfplugin、install-manifest.json + latest.json、ghcr 双镜像 `0.28.0` + `latest`）之外：**引导程序 Bundle EXE 首次随发版流水线构建并随 Release 发布**（迭代 68 接线，`LabelFrame-Bootstrapper-0.28.0.exe`，Release 推荐安装入口）；**compose.yml + .env 随 Release 分发**（迭代 71，`.env` 钉定 `LABELFRAME_VERSION=0.28.0`）；**linux-x64 归档默认 self-contained**（迭代 71，目标机免装 .NET 10 ASP.NET Core Runtime）。
- **版本同步**：ServerOptions / HostOptions `ProductVersion` 与稳定版 Compose 默认版本更新为 `0.28.0`（客户端「检查更新」比较口径随升，决策 #126）。

## 迭代 94：流程治理——release.yml 前端产物自检特征过时修复与 v0.28.0 恢复出库 · 2026-09-18

- **缺陷与根因（#166；v0.28.0 发版实证，run 35326712703，决策 #150）**：「测试与前端构建」job 的「前端产物自检（双模式区分，迭代 22 修复）」client 特征断言 `-notmatch 'PDA 日志'`——该字符串随迭代 75（#118）「PDA 日志 / 设备日志」页面下线从 client 产物消失，断言恒失败（fail-closed 拦截，未创建 Release，其余 dotnet / 前端测试步骤均通过）；顺带发现反标记 `'本机未注册到服务端'` 自迭代 80「三名义」改名后已无出处，对应断言空转（恒真无保护）。PR 必需检查不执行 release 专属自检步骤，记账 PR #165 三项全绿未拦截——发布链专属断言只能在发版时暴露。
- **修复**：client 特征与反标记统一改用 `'本机打印服务'`（App.tsx 状态栏 client 分支文案，`isServerUi` 构建期折叠后 server 产物不含；本地双构建取证：client 在场 / server 不在场，两形态均单 chunk）；server 特征 `'客户端下载'` 维持；步骤注释与文件头历史注释同步。
- **v0.28.0 恢复出库**：tag v0.28.0 不删不重推——修复合入 master 后以 `workflow_dispatch`（version=0.28.0）触发，产物源 master 与 tag 仅差本 workflow 修复、产物内容一致；出库后核验（Release 附件 / ghcr 双镜像 / 依赖基线 Snapshot）与待验收联动（#91 / #93 / #101）随发版会话执行，证据回写 #166。
- **验证**：本地双构建 grep 在场性取证（表见 #166）；release.yml YAML 解析自检；dotnet / 前端代码零改动（build / test 由 CI 必需检查覆盖）。

## 迭代 93：前端交互一致性收尾——确认弹窗统一 / 横幅通用类 / 空态刷新与文案 / lint 清零与死代码清理 · 2026-09-18

- **动机与范围（#151；前端评审 2026-09-18 F-04 / F-06 / F-07 / F-11 / F-16 / F-17 / F-20 / F-21，用户拍板 a 案——Devices 加载态 =「加载中…」文案行，不引入骨架组件）**：危险确认两套机制并存（模板删除走自研 `Modal`，安装包 / 插件包删除与插件覆盖安装 / 卸载走原生 confirm，WebView2 窗口壳中样式突兀、按钮文案不可定制）；同一段内联横幅样式在 7 处错误 + 3 处通知逐字复制（已现 Designer 版多带 `display:flex` 的不一致苗头）；Devices 首次拉取前闪现「暂无设备」；工作台加载失败无重试入口；列映射表头「字段键」为开发者语；lint 6 条 warning、死代码与 vitest 配置残留。八项小改动同一 PR 交付。
- **确认弹窗统一（F-04，AC-01）**：`DownloadCenter`（客户端安装包 / PDA APK 删除）、`PluginPackages`（插件包删除）、`Settings`（插件覆盖安装 / 卸载）的全部原生 confirm 改自研 `Modal` 确认（复用工作台删除确认模式：标题 + 危险语正文 + 取消 / 确认按钮，Esc / 点遮罩关闭）；`Designer` 剪贴板完全禁用时的终极兜底 `window.prompt` 按决议保留；对应页测试同步改为 Modal 交互断言（确认 / 取消两分支）。
- **横幅通用类（F-07，AC-02）**：`styles.css` 新增 `.banner` / `.banner.error` / `.banner.notice` 通用类（基类含 flex 布局——纯文本横幅与原 block 版视觉一致，横幅内需排按钮的场合可直接水平排列），7 处错误横幅（Designer / DataPrint / Devices / DownloadCenter / JobHistory / PluginPackages / Workbench）+ 3 处通知横幅（Devices / DownloadCenter / PluginPackages）统一引用，消除内联复制，视觉与改前一致。
- **Devices 空态（F-16 决议 a 案，AC-03）**：初始 `devices = null` +「正在加载设备列表…」文案行（与插件管理页同模式），首次拉取完成前不再闪现「暂无设备」；新增页级测试覆盖「挂起→加载态→空列表→真空态」路径。
- **工作台刷新（F-17，AC-04）**：页头新增「刷新」按钮（与作业历史同款：加载中禁用 + 「刷新中…」文案），失败横幅旁附「重试」按钮——列表加载失败不再只能切页重试。
- **文案（F-20，AC-05）**：DataPrint 列映射弹层表头「字段键」→「模板字段」（与选项「显示名（字段名）」格式自洽）。
- **lint 清零（F-06，AC-06）**：6 条 warning 逐一处理——`mapping.ts` 字符类内 `\-` 无谓转义修正（`[\s_-]` 语义不变）；`App.tsx` 周期探测 effect 解构出三个稳定成员入依赖（消除对 `app` 聚合对象的缺依赖告警，语义不变）；`CanvasViewport` 的 `clampStage`（useCallback，依赖同三项）声明上移并入 stageBox effect 依赖；`AppContext.useApp` 的 `only-export-components` 显式豁免并注明理由（React Context 标准模式）；其余两条 `only-export-components` 随死代码删除自然消除。`pnpm lint` 0 warnings 0 errors。
- **死代码与配置残留（F-11 / F-21，AC-07）**：删除全仓零引用的 `TransportQuickSwitch`（`TransportPanel.tsx`）、`findElement`（`CanvasViewport.tsx`）、`elementTypeName`（`ElementNode.tsx`）导出（git 历史可溯）；删除 `vitest.config.ts` 从 vite 配置拷贝残留的 `server.port` / `build.outDir` 无效段。
- **测试（全绿）**：前端新增 1 项至 378 项；pnpm lint 0 warnings 0 errors + 双模式测试（client / server 各 378 项）+ 双构建（`pnpm build` / `pnpm build:server`）全绿；后端零改动，dotnet build 0 警告 0 错误、dotnet test（排除 Perf/Soak）861 项零回归。
- **不在范围**：可访问性（F-08）与工作台预览惰性加载（F-18）、oxlint `--max-warnings=0` 纳入 CI（流程治理）、工作台卡片直达打印（#133 承载）、横幅以外样式体系重构。
- **记账**：ROADMAP 状态行随验收结项收口。

## 迭代 92：设计器未保存离开保护与页级测试补齐 · 2026-09-18

- **动机与范围（#150；前端评审 2026-09-18 F-02（P1）/ F-03（P1），立项前逐条核验属实，用户拍板 a 案——dirty 判定 = 历史栈有任一已提交更改（`undoCount > 0`，现成计数实现最小，决策 #149））**：设计器三处离开路径（Shell 导航切 tab、顶栏返回按钮、加载失败返回）均直接卸载组件——`stateRef` / `historyRef` 随之全部丢弃，工作台双击卡片误触、返回键误触、肌肉记忆点导航任一动作丢失整段排版工作（设计器属低频高成本操作）；且设计器是 `web/src/pages/` 下唯一没有页级 `.test.tsx` 的页面——加载分支、同名覆盖确认、`toContract` / `toLayout` 组装等装配逻辑回归无拦截。
- **未保存离开保护（F-02，AC-01~03）**：拦截两处离开路径——Shell 层导航 tab 切换统一经 `switchTab` 入口（设计器通过 `registerLeaveGuard` 向 Shell 注册守卫，挂载注册 / 卸载自动注销）与设计器返回按钮（含加载失败返回）；dirty（历史栈有已提交更改）时弹自研 `Modal` 三选「保存并离开 / 放弃更改 / 继续编辑」（复用既有 `components/Modal.tsx`，Esc / 点遮罩 / 右上角关闭 = 继续编辑）；「保存并离开」走既有保存链路（空名校验 / 同名覆盖确认），成功后执行挂起的离开动作（保存成功自然复位 dirty），失败停留设计器显示错误不丢编辑；挂起离开动作在各终止路径（继续编辑 / 保存失败 / 覆盖取消 / 名称缺失）就地作废，防陈旧动作误触发后续普通保存；无已提交更改（undoCount = 0）直接离开不弹窗；普通保存成功后回工作台的既有行为零变化。
- **页级测试补齐（F-03，AC-04）**：`Designer.test.tsx` 在迭代 91 既有 4 条 serverMode 用例（编辑加载成功 server / standalone 降级、加载失败错误条、新建不依赖模式）之上扩展（零重建零回归）——新增 10 项：页级保存链路 4 项（新建保存成功含 `toContract` / `toLayout` 组装断言、同名覆盖确认 → 取消 / → 确认、保存失败中文提示停留不回工作台）+ 未保存离开保护 6 项（三选「继续编辑」/「放弃更改」/「保存并离开」、导航切 tab 经注册守卫挂起路径、AC-02 无编辑直接离开不弹窗（编辑 / 新建两路径）、AC-03 保存失败停留且挂起动作作废后普通保存仍正常离开）；mock `biz`（单机模式 localApi）沿用 `DataPrint.test.tsx` 范式，画布仍为桩（维持决策 #82③ 断言边界）。
- **测试（全绿）**：前端新增 10 项至 377 项；pnpm lint 0 错误（6 条 warning 均存量）+ 双模式测试（client / server 各 377 项）+ 双构建（`pnpm build` / `pnpm build:server`）全绿；后端零改动，dotnet build 0 警告 0 错误、dotnet test（排除 Perf/Soak）861 项零回归（Bootstrapper 226 / Core 174 / Api 15 / ClientHost 1 / Server 106 / WinHost 339）。
- **不在范围**：画布级 Konva 交互测试（决策 #82③：以 onChange 为断言边界，Konva 本体留 E2E 层）、浏览器 `beforeunload` / 窗口关闭拦截（WebView2 壳关闭场景）、设计器功能增强。
- **记账**：DESIGN 决策 #149；ROADMAP 状态行随验收结项收口。

## 迭代 91：前端请求层健壮性——超时分档 / 导出错误通道 / 详情竞态 / serverMode 守卫 · 2026-09-18

- **动机与范围（#149；前端评审 2026-09-18 F-01 / F-09 / F-12 / F-13，用户拍板 a 案——普通请求 30s + 出图 / 上传类放宽至 120s，决策 #148）**：请求原语 `makeRequest` / `makeFetchBlob` 均未挂超时 signal——后端挂起（接了不回）时提交 / 出图 / 上传类操作永久「处理中…」，无错误码无恢复路径；`exportTemplate` 绕开统一 `fetchBlob` 自写 fetch，失败固定 `EXPORT_FAILED` 吞掉后端 ErrorView 真实原因；DataPrint 模板详情加载无竞态守卫（快速切换模板时慢响应可覆盖新选择）；Designer 加载 effect 缺 `serverMode === 'unknown'` 守卫（Workbench 有），启动早期以 `localApi` 误发请求。
- **业务请求超时分档（F-01，AC-01）**：两请求原语统一挂默认超时中止信号，超时归一化为 `ApiError('TIMEOUT', 中文文案)` 走既有错误通道（`NETWORK_ERROR` 语义不变）；分档——普通请求（列表 / 保存 / 提交等）30s，出图 / 上传类大负载端点（renderImage / renderImages / previewTemplate / exportTemplate / importTemplate / importExcel / excelTemplate / 客户端 / PDA / 插件包上传下载 / installPlugin）120s；fetchBlob 超时覆盖整个下载过程（含响应体读取）；既有 healthz / probeHealthz 5s 独立超时与迭代 86 空地址短路语义零变化（超时配置导出为可注入对象，单测秒级验证不真实等待）。
- **导出错误通道同构（F-09，AC-02）**：`exportTemplate` 改用 `makeFetchBlob`——失败解析后端 `ErrorView` code / message 呈现真实原因（无 JSON 体回退既有「导出失败（HTTP xxx）。」文案），Content-Disposition 文件名解析收敛到 fetchBlob 一处（消除两段重复）。
- **模板详情竞态守卫（F-12，AC-03）**：DataPrint 模板详情 effect 加 `cancelled` 守卫（与同文件预览弹层 `previewGenRef` 同一标准）——快速切换模板时前一个慢响应不覆盖新选择，字段表单 / 打印数据与选中模板始终一致。
- **serverMode 守卫对齐（F-13，AC-04）**：Designer 加载 effect 把 `serverMode`（原始值入依赖，无 context 无限循环问题）纳入守卫——`unknown` 阶段不以 `localApi` 误发请求，模式解析后按正确 base 正常加载（与 Workbench 行为一致）；单次闩锁保证模式中途翻转（10s 周期探测）不重拉模板、不重置编辑中状态；新建模板不依赖模式解析。
- **测试（全绿）**：前端新增 13 项至 367 项——超时分支（普通档挂起 TIMEOUT / heavy 档分档落点反向断言 / NETWORK_ERROR 不误判 / 成功路径零回归）、导出错误通道（ErrorView 真实原因 / 无 JSON 体回退文案 / Content-Disposition 解析）、DataPrint 慢响应竞态（守卫移除时确定性失败已验证）、Designer unknown 不请求 / server 解析后加载 / standalone 降级 / 新建不依赖模式 / 失败通道；pnpm lint 0 错误（6 条 warning 均存量）+ 双模式测试（client / server 各 367 项）+ 双构建全绿；后端零改动，dotnet build 0 警告 0 错误、dotnet test（排除 Perf/Soak）635 项零回归。
- **不在范围**：请求自动重试（评审明确不做）、API 层拦截器化重构、工作台卡片直达打印（#133 承载）。
- **记账**：DESIGN 决策 #148；ROADMAP 状态行随验收结项收口。

## 迭代 90：Zebra 插件说明「原生指令模式下 QR 不支持中文」 · 2026-09-18

- **动机与范围（#148；迭代 79（#121）Zebra 真机 + PDA 扫码枪陪验实证观察项，用户 2026-09-18 拍板 a 案——仅做说明文案，不升级 fail-closed 显式拒绝）**：原生指令模式（`^CI28`）下 QR 数据含中文时打印机侧**静默过滤中文字符**——标签印得出、扫出内容缺中文（「印得出扫不回」的静默数据丢失）；用户配置 Zebra 插件、选择「原生指令」打印方式时界面无任何提示。图片模式 QR 由本产品栈（ZXing UTF-8）渲染编码正确，能否解出取决扫码枪解码能力，不属插件承诺范围（不在本轮）。
- **说明文案（AC-01 / AC-02）**：`ZebraTransportPlugin` Description 追加「原生指令模式下二维码（QR）数据不支持中文（仅 ASCII），中文请用文本框（图片模式）。」；`printMode` 参数 Hint 追加「该模式下二维码（QR）数据不支持中文（仅 ASCII），中文请用文本框（图片模式）」——措辞限定「原生指令模式」（Hint 首句原生模式口径同句境限定，不外溢图片模式），句读与迭代 82 用户化文案风格一致；`GET /api/transport` 透出的 Description / Hint 为 descriptor 字段透传，设置页选中 Zebra 即直出该说明（前端零改动）。
- **编译行为不动（不在范围，显式约束）**：原生 QR 中文编译保持现状（`^CI28` 静默过滤），未升级 LF_ENC 系显式拒绝（如后续需要另行立项）；图片模式 QR 行为与扫码枪解码能力说明、其他插件（Log / TCP / Windows 驱动）文案均不动。
- **测试（全绿）**：WinHost 新增 1 项文案契约断言（`ZebraLabelCompilerTests`：插件 Description / `printMode` Hint 与注册表 descriptor——`GET /api/transport` availablePlugins 数据来源——均含该说明且限定原生模式、句读完整）；dotnet build 0 警告 0 错误、dotnet test（排除 Perf/Soak）零回归；前端零改动。
- **记账**：ROADMAP 状态行随验收结项收口（AC-01 沙箱走查留待验收环节）。

## 迭代 86：设置页「服务端已连接」徽标空地址谎报修复 · 2026-09-18

- **动机与范围（#142；迭代 80（#128）陪验走查观察项，用户 2026-09-18 拍板 a 案——空地址不做网络探测，徽标直接判「服务端未连接（单机模式可用）」，不回退默认地址 `127.0.0.1:53961` 探测，决策 #147）**：未配置服务端地址（单机安装形态）时 `GET /api/host/config` 返回 `serverUrl:""`，前端 `setServerBaseUrl('')` 使 serverApi base 落**同源**——探测与业务请求实际打到本机 WinHost（`/healthz` 200）→ 设置页徽标谎报「服务端已连接」，同页「更新与安装包」「插件管理」区却因接口 404 报错，同屏自相矛盾；且模块初始化探测（默认 127.0.0.1:53961）与机器级配置加载后的探测（空串同源）交错，同一状态先后显示「已连接 / 未连接」竞态双态。
- **空地址不探测（a 案，AC-01）**：`AppContext.checkConnection` 在 client 构建下生效地址为空时直接判未连接——不发 healthz、serverMode 落单机降级（standalone，业务走本机 localApi），安装包 / 插件区随 connected=false 呈现单机模式提示、不再拉服务端列表（同屏无矛盾）；server 构建不受影响（base 恒同源即服务端自身，探测语义正确）。设置页「测试连接」同口径：`probeHealthz` 空地址（空串 / 空白 / 纯斜杠）直接判失败，不落同源探测谎报可连接。
- **探测按配置就绪串行化（AC-02）**：client 构建机器级配置（GET /api/host/config）加载完成（含旧客户端回退 localStorage 兜底）前不发起服务端探测——首次探测在配置就绪后串行发起，消除「初始化按默认地址探测」与「配置加载后按实际地址探测」交错的竞态；探测结果按序号后发优先（地址切换 / 判空后，在途旧探测的结果作废不覆盖新终态），保存地址即按新地址立即重探测。
- **测试（全绿）**：前端新增 8 项至 354 项——设置页三态（空地址不探测且安装包 / 插件区单机提示同屏无矛盾、可达已连接拉列表、不可达未连接）、切换刷新（可达 ↔ 不可达往返转刷新、保存空地址即未连接且不再探测）、配置就绪串行化（配置未决不探测、就绪后首次探测仅一次）；probeHealthz 空地址语义（不发起 fetch）与非空行为不变 + lint 0 错误（6 条 warning 均存量）+ 双构建 / 双模式测试（client / server）全绿；后端零改动，dotnet build / test（856 项，排除 Perf/Soak）零回归。
- **记账**：DESIGN 决策 #147；ROADMAP 状态行。

## 迭代 89：图片模式锚定文本垂直坐标语义修复——锚定盒文字穿底边 · 2026-09-18

- **动机与范围（#147；迭代 78 / 79 真机陪验取证观察项，证据见 #120 取证评论）**：区域锚定（`regionId` + `regionHAlign/regionVAlign=Center`）的自动宽度 + 自动高度文本在垂直方向穿出锚定盒底边约 1.5–2 字高；同一版式非锚定文字位置全部正常；图片模式服务端渲染图与原生指令模式双重复核。
- **根因定案（执行会话定位）**：`LabelLayoutResolver.ResolveBounds` 锚定分支按**字高框**计算 y（格顶语义，与 Zebra 原生 `^FO` 格顶一致、本就居中——真机「原生略偏上部」即图片模式下沉的对照）；缺陷在图片模式渲染器：自动高度文本（`HeightMm ≤ 0`）的绘制框用决策 A 兜底 `max(字高 + 2×最大双边内边距, 10mm)`（≥10mm）却以锚定 y 为框顶，再按缺省 `VerticalAlign=Middle` 在框内居中——字形中心比区域中心下沉 `(兜底框高 − 字高)/2`（字高 2mm 时 4mm = 2 字高），穿出盒底。探针实证：修复前墨迹中心偏下 4.09mm、盒下越界墨迹至 28.78mm（区域底 28mm）；Zebra 原生 `^FO y=184` 点本就居中。
- **修复（锚定框 = 绘制框，同框原则）**：`SkiaLabelRenderer` 与 `AndroidLabelRenderer`（PDA 同源缺陷）对**锚定**自动高度文本改用 `bounds.HeightMm`（解析锚定框）作绘制框高——VerticalAlign / 边框 / 裁剪都在锚定框内；**非锚定**自动高度文本维持决策 A 兜底框零变化；显式高度锚定路径零变化；原生 Zebra 编译器零改动（本就同语义，修复后图片 / 原生两模式同框同位）。`LabelLayoutResolver` 补垂直锚定语义文档（Y = 锚定框顶部、Height = 锚定框高、渲染方须在该框内绘制）。
- **测试（全绿）**：新增 4 项——解析层：区域居中锚定自动宽高文本 bounds 完整位于盒内垂直居中（AC-01）+ Start/End 贴边方向；渲染层：锚定自动高度文本墨迹完整在盒内、重心垂直居中 ±1mm、Start/End 贴边不越盒（修复前必失败——中心偏 4mm + 穿底墨迹）+ 非锚定兜底框语义零变化 pin。dotnet build 0 警告 0 错误；dotnet test（排除 Perf/Soak）860 项全绿（Bootstrapper 226 / Core 174 / Api 15 / ClientHost 1 / Server 106 / WinHost 338）零回归；前端零改动。
- **口径复核**：迭代 78 的 Start↔End 起点偏移断言（`^FO80,260` / `^FO637,260`，自动高度 y=260 = 字高格顶居中）建立在正确的锚定语义上，无需修正；水平锚定（`regionHAlign` 实测宽度偏移路径）复核未见同源缺陷（无更高兜底框参与），不动。
- **记账**：DESIGN 决策 #146（锚定框 = 绘制框）；ROADMAP 状态行随验收结项收口。

## 迭代 87：连接选项「Log（模拟打印）」去技术前缀 · 2026-09-18

- **动机与范围（#143；迭代 82（#130）陪验走查观察项，用户 2026-09-18 拍板「去掉 LOG」）**：连接方式选项列表 Log 项仍显示「Log（模拟打印）」——技术 id 与用户名并列；迭代 82 已把摘要 / 图例 / 测试打印说明三处统一为「模拟打印」，唯选项列表保留技术前缀。
- **选项列表用户化**：`LogTransportPlugin.DisplayName`「Log（模拟打印）」→「模拟打印」——选项列表（设置页连接方式编辑区 radio）与快速切换下拉直出后端插件目录 DisplayName，一处改动两处生效；选中态说明文（Description，迭代 82 已用户化）与摘要（Describe「模拟打印」）本就同源，无需另行改动。**插件 Id `log` 不变**（connection.json / 机器配置存储值零迁移，存量兼容）。
- **测试（全绿）**：后端 dotnet 856 项零回归（排除 Perf/Soak）——更新 2 处既有断言（`TransportPluginRegistryTests`：DisplayName「模拟打印」且无「Log」字样 + Id 保持 `log`；`TransportPluginRegistryExternalTests`：内置未被外部覆盖断言）；前端同步镜像后端插件目录的 mock（Settings / DataPrint 测试）并新增 1 项选项列表契约用例（radio 显示「模拟打印」、选中态说明文一致、全页无「Log」字样）至 346 项 + lint 0 错误（6 条 warning 均存量）+ 双构建全绿。
- **不在范围**：`GetStatusAsync`「日志模拟在线。」措辞、回退路径 TCP 标签「网络打印机（TCP）」与插件路径不一致——另行立项；TCP / Windows 驱动既有文案不动。

## 迭代 84：打印页称谓与作业信息可读性——副标题 / 设备名 / 编号收敛 · 2026-09-17

- **动机与范围（#132；评审 #114 中优先项 A-3 / B-6 / B-7，决议 = 副标题「填写数据并打印 / Excel 批量打印」（决议 1，用户拟词）+ 编号收敛「隐藏请求编号、报障口径统一为作业编号」（决议 2），决策 #145）**：数据与打印页是唯一含字段填写 + 打印 + Excel 导入的日常主入口，副标题却自称「测试数据」（用户会疑惑「正式打印在哪里」）；同一设备两种叫法——作业进度与作业历史的「目标设备」直出裸设备 ID（如 `DESKTOP-IR7RCO3`），而在线设备页 / 目标设备下拉显示设备名；作业历史「请求编号」「作业编号」两列十六进制 ID 并列，用户报障不知该引哪个。
- **副标题用户化（AC-01，决议 1）**：数据与打印页副标题「测试数据 / Excel 批量打印 / 打印测试」→「填写数据并打印 / Excel 批量打印」（双形态同组件同文案，无「测试」字样；斜杠两侧空格与现版式对齐）。
- **目标设备统一显示设备名（AC-02，B-6）**：作业进度（数据与打印页）与作业历史（双形态）的「目标设备」由裸设备 ID 改为设备名——来源与在线设备页 / 目标设备下拉同源（`GET /api/devices` 的 `name`，新增共享助手 `deviceDisplayName`：名称去空白后优先，空 / 不在列表回退设备 ID）；作业进度复用本页已拉取的设备列表解析，作业历史在服务端模式拉取一次设备名映射（作业轮询不重复拉，拉取失败静默回退 ID 不阻断列表），单机降级无设备列表 = 恒回退 ID（本机作业本就显示「本机」）。
- **作业历史编号收敛（AC-03，决议 2 / B-7）**：删「请求编号」列——列表仅「作业编号」可见（前 8 位 + 复制图标按钮），点击一键复制**完整作业编号**（复用 `copyText`：Clipboard API → execCommand 降级，成功 / 失败均状态栏反馈）；「请求编号」不再单列但悬停可达——按钮 title 携带完整「作业编号 + 请求编号」两行提示，报障口径统一为作业编号。
- **测试（全绿）**：前端新增 9 项至 345 项——副标题双形态（client / server）文案断言（无「测试」字样）、作业进度目标设备名（已知设备显示名 / 未知设备回退 ID，双形态）、作业历史设备名解析（listDevices 同源 / 空名称回退 / 未知设备回退 / 拉取失败不阻断无错误横幅 / 轮询不重复拉）、编号收敛（表头仅「作业编号」、请求编号不直出、title 悬停可达、一键复制完整编号、复制失败不抛错）+ lint 0 错误（6 条 warning 均存量）+ 双构建全绿；后端零改动。
- **记账**：DESIGN 决策 #145；ROADMAP 状态行。

## 迭代 83：模板字段显示名与设计器属性提示修正 · 2026-09-17

- **动机与范围（#131；评审 #114 高优先项 B-3 / B-4（同源）/ A-2 后半，决议 = 显示名可选 + `displayName || key` 回退（决议 1）、「契约字段 / 键名称」→「打印字段 / 字段名」（决议 2），决策 #144）**：设计器内部术语直出（侧栏「契约字段（自动推导）」、属性面板「键名称（契约字段，自动建立）」）；英文键名原样成为打印页字段标签（占位符「字段 location 的值（打印时使用）」）——契约本就有 displayName、Excel 模板导出已按 `displayName || key` 取表头，唯独打印表单未用；属性面板「来源=固定值」时仍显示字段填充提示。
- **字段显示名落地（AC-01，决议 1）**：设计器字段填充分支新增可选「显示名（打印页显示，可选）」输入（文本 / 条码 / 二维码元素 `displayName`，内部模型字段）；保存路径显示名只进**契约 fields**（`toContract` 由键列表改为键 + 显示名列表，空 / 空白回退键）——**模板包契约格式零变化**（displayName 字段既有，版式元素 JSON 不加字段）；加载路径按 sourceKey 匹配回填元素（`applyContractDisplayNames`，设计器往返不丢显示名）；存量模板（旧口径 displayName = 键）不回填、显示处回退键名不报错。数据与打印页表单标签与占位符改按 `displayName || key` 渲染（值提交仍按字段名（键）绑定，行为不变）；设计器侧栏「打印字段」列表与「测试默认值」标签同一策略。
- **术语用户化（AC-02，决议 2）**：侧栏「契约字段」→「打印字段」（自动推导保留）；属性面板「键名称（契约字段，自动建立）」→「字段名（打印时用数据填充）」，placeholder 与徽标同步（显示名示例「库位；不填则显示字段名」）；界面无「契约」字样直出（组件测试断言）。
- **属性面板提示归位（AC-03，A-2 后半）**：字段填充提示「打印时从外界数据取「字段名」对应字段填充，预览值会被忽略。」移入「字段填充」分支——固定值模式不显示（此时字段名 / 显示名 / 预览值输入均不存在，不再说不相干的话）；切回固定值同步清空显示名（与清空字段名一致）。
- **测试（全绿）**：前端新增 13 项至 336 项——显示名推导（同键取首个非空）、契约往返不丢显示名（保存 → 读取回填）+ 存量模板回退键名不报错、PropsPanel 显示名输入 / 提示仅字段填充模式出现 / 无「契约」直出、SidePanel 打印字段徽标显示名优先、DataPrint 双形态（client / server）表单标签与占位符显示名渲染 + 值仍按键提交 + 存量回退；+ lint 0 错误（6 条 warning 均存量）+ 双构建全绿；后端零改动。Excel 导入映射界面未动（已用 displayName||key，无回归）。
- **记账**：DESIGN 决策 #144；ROADMAP 状态行。

## 迭代 82：打印机连接文案用户化收尾——LOG 直出 / 拼接残句 / TCP 选项说明 · 2026-09-17

- **动机与范围（#130；评审 #114 高优先项 B-1 / A-1 / B-5 / B-2 同源收尾，决议 = 命名组合 A 用户拍板，决策 #143）**：迭代 73（#108）界面用户化的漏网回归——插件路径 `Describe()` 直出「LOG」绕过前端 MODE_LABELS 映射（三处徽标：连接方式摘要 / 数据与打印图例 / 测试打印说明）；插件说明文含开发者用语「联调」且前端无参数时追加「（无参数）。」拼接残句；TCP 选项「TCP 9100」在列表层无解释。
- **Log 显示名用户化「模拟打印」（AC-01）**：后端 `LogTransportPlugin.Describe` 由 `"LOG"` 改 `"模拟打印"`——三处徽标经既有 `formatTransport`（displayText 优先）自动覆盖**插件路径**；**回退路径**（旧后端无 displayText，`MODE_LABELS` 本地格式化）迭代 73 已是「模拟打印」，两条路径一致；连接测试 / 切换成功消息（`TransportManager` 内嵌 Describe）随之用户语；displayText **契约字段本身不变**（字段与取值优先级维持迭代 22 口径，仅 Log 取值用户化，对外 API 兼容）。
- **说明文与选项标签（AC-02）**：Log 插件 Description 重写为「模拟打印：不连接真实打印机，作业按打印成功处理；图片模式的标签图片会保存到本机，便于先确认打印效果。」（去「联调」、句读完整）；`TransportPanel` 无参数插件说明直出 Description，不再追加「（无参数）。」残句；TCP 选项列表标签改「网口打印机（TCP 9100）」（`Tcp9100TransportPlugin.DisplayName`；选中态说明与参数表单保持现状）。
- **测试（全绿）**：后端新增 1 项 + 更新 2 处断言（Describe「模拟打印」、TCP DisplayName「网口打印机（TCP 9100）」、Log 文案契约——无「联调」/ 无「LOG」/ 句读完整），dotnet 856 项零回归（排除 Perf/Soak）；前端新增 5 项至 323 项（formatTransport 插件路径 Log 徽标、设置页摘要 + 测试打印说明「模拟打印」且无「LOG」直出、展开说明文无残句无「联调」、TCP 选项与参数表单、数据与打印图例插件路径）+ lint 0 错误 + 双构建全绿。
- **记账**：DESIGN 决策 #143；ROADMAP 状态行。

## 迭代 81：「图片预览」名副其实——页内预览弹层与下载入口分离 · 2026-09-17

- **动机与范围（#129；评审 #114 高优先项 C-1，决议按 #129 用户拍板——落地形态 = 页内预览弹层 + 弹层内显式「下载」按钮，预览与下载彻底分离，不做「仅改名下载」方案，决策 #142）**：数据与打印页按钮名为「预览」实为下载——点击后页面无任何预览出现，`label-print.png` 直接进浏览器下载目录（状态栏提示「标签图片已下载」）；用户想「看一眼打出来什么样」，得到的是要去下载文件夹找的文件。
- **页内弹层预览（AC-01）**：点击「图片预览」→ 页内弹层呈现**当前字段值的渲染图**（复用既有 `POST /api/print/render-image` 端点实时取图，请求形态不变；不建作业、不自动下载）——与工作台缩略图灯箱同交互（居中大图 / 透明底衬浅格、Esc / 点背景 / 点 × 关闭、点卡片本体不关闭，复用 `preview-modal*` 样式）；加载态「正在生成预览…」、失败态弹层内提示原因可关闭重试；blob URL 关闭即释放，在途请求按代数作废（防关闭后自动重开），页面卸载兜底释放。
- **下载显式分离（AC-02）**：弹层底部操作行显式「下载图片」按钮——下载当前预览的图片（复用弹层已持有的 blob，不重复请求），下载后弹层保持打开；预览动作本身零下载（`downloadBlob` 仅由显式点击触发）。
- **双形态与不回归（AC-03）**：客户端 / 服务端管理界面同源组件行为一致（server 构建补双形态走查用例）；「模拟出图」既有文案与行为零回归——勾选后按钮仍为「生成图片（单张）」直接下载、隐藏「图片预览」，批量 zip 链路（`render-images`）与工作台缩略图预览（迭代 46）现状保留未动。
- **测试（全绿）**：前端新增 / 更新至 318 项——弹层呈现当前字段值（改值后再预览按新值渲染）、预览阶段零下载 + 弹层内「下载图片」才触发下载且不重复请求、× / 背景 / Esc 三通道关闭与 blob URL 释放、在途关闭不重开、失败态与恢复、双形态一致、模拟出图不回归、静态标签空数据预览；后端零改动。
- **记账**：DESIGN 决策 #142；ROADMAP 状态行。

## 迭代 80：服务端地址切换「保存即生效」落地与连接状态一致性 · 2026-09-17

- **动机与范围（#128；评审 #114 高优先项 C-2（含数据正确性风险）/ B-9 / C-3 同根修复，决议按 #114 终版拍板）**：「保存并生效」后设备并未真正切换——启动时构造的 `ServerJobPoller` 固化 URL、保存仅写机器级配置，打印记录持续发往旧服务器直到重启进程；「服务端」一词在客户端同屏承载三种含义且可互相矛盾；数据与打印页设备加入状态只取一次不随探测刷新。
- **路由热切换（AC-01，决议 1 取「保存成功后重建路由 worker」形态，决策 #141）**：新增 `ServerRoutingCoordinator`（WinHost `Routing/`）持有当前路由独占资源（服务端地址 + HttpClient 连接池 + poller + worker）——`POST /api/host/config` 保存成功后热切换：先按新地址重建 poller / worker 并立即注册（最小化设备不可用窗口），再停旧路由（取消在途长轮询 → 等待收尾 → 释放旧 HttpClient 连接池）；地址未变化幂等无动作（不打断进行中的注册 / 长轮询）；未配置地址时空转，首次保存即可开启路由（覆盖「启动无地址 → 保存后开启」转换）。形态选择理由：「poller 每轮读取配置」无法覆盖无地址启动转换、切换时延受长轮询挂起（最长 20s）影响不确定；重建形态在保存请求内确定性完成、旧连接生命周期独占。协调器不注册为 IHostedService（测试装配移除后台 Worker 后仍可经端点热切换），生命周期 = WinHostApp 启动接线 + `IHostApplicationLifetime` 停机联动清理。
- **三名义（AC-03，决议 2：本机打印服务 / 服务端地址 / 已加入服务端各义一名）**：① 状态栏（client 构建）改「本机打印服务：运行中 / 未运行」——`AppContext` 新增 `localServiceUp`（页面来源 `/healthz` 独立探测，10s 周期），本机 IP / 本机设备名随其显示（本机事实不再依赖远程服务端连通性）；设置页「服务端地址」维持原状（含义②）；② 数据与打印页对服务端的状态一律「已加入 / 未加入服务端」（顶部徽标 + 目标设备行 + 打印按钮提示 + 底部说明，`已连接服务端 / 尚未加入` 全量改「加入」口径）；③ 连接方式面板徽标着色改随本机打印服务（本机事实）；在线设备页空态「安装并连接服务端后出现」改「加入服务端」。残留「连接 / 未连接服务端」仅为含义②语境（设置页安装包 / 插件分发、作业历史数据源提示）。
- **加入状态周期刷新（AC-02，C-3）**：数据与打印页设备列表 / routeMode / hostInList 由「进入页面取一次」改为随探测周期刷新（client 构建 10s，server 构建维持进入拉取一次 + 提交前现拉校验契约）——后台注册状态变化（含保存地址后的热切换）页面在探测周期内自动更新无需切页；周期刷新保留已选目标设备（仍在线时）。
- **测试（全绿）**：WinHost 新增 5 项——协调器单测 4 项（无地址启动空转后首次开启 / 切换后新 poller 注册 + 旧 worker 停止 + 旧 HttpClient 释放 / 同地址幂等不重建 / 启动接线与停机清理）+ HTTP 集成 1 项（生产装配 WinHostApp + 双 Kestrel stub 服务端模拟 AC-01：保存后设备在新服务端注册（`POST /api/devices` 可见）+ 旧服务端 notify 长轮询连接中止）；前端新增 / 更新至 311 项（状态栏三名义三态 + 各义独立探测 + 徽标已加入 / 未加入 / 未注册 + fake timers 周期刷新无需切页）。
- **记账**：DESIGN 决策 #141 + §5.2 交互口径补句；ROADMAP 状态行。
## 迭代 79：插件命令打印实现 3/4——Zebra 编译器·条码与二维码（^BC / ^BQ 编译） · 2026-09-17

- **动机与范围（#121；DESIGN §5.4.8 拆分步 3，依赖迭代 78 `b52f4be`；契约 §5.4 已定稿，本轮只落码不改契约）**：在文本元素（迭代 78）之上补齐条码与二维码的原生指令编译，使常见仓储标签（文本 + Code 128 + 二维码）可完整走原生指令路径；图片 / 线元素维持 §5.4.4 显式失败裁剪。
- **Code 128 条码编译（AC-01，ZPL 官方 ^BC / ^BY 口径）**：`^FO{x},{y}^BY{模块宽},3^BCN,{高度},Y|N,N,N^FH^FD{数据}^FS`——模块宽（模板字段即 ZPL 模块宽度 1..10，非毫米量纲）夹取透传；高度毫米按 DPI 换算（≤0 兜底 1 点）；`displayValue` → interpretation line 参数（Y 打印人眼可读行 / N 仅条码，默认下方）；不加 UCC 校验位、不指定子集模式（默认子集 B 直接编程，沿用仓库既有形态）。**转义**：^ / ~ / _ 走 ^FH 十六进制转义（同文本口径）；`>` 额外按官方调用码映射 `>0`（子集 B 中 `>` 为子集切换调用前缀，十六进制还原后仍会被条码数据层当作调用前缀）。**字符集**：Code 128 仅可打印 ASCII——中文 / Latin-1 / 控制字符按 `LF_ENC_001` 编码拒绝（含首个问题字符码点 + fieldKey），与图片模式渲染层拒绝同一语义（DESIGN §7 既有决策零回归；区别于文本元素的 `LF_ENC_002`——条码不支持中文属字符集编码问题，非内置字体覆盖问题）。
- **QR 二维码编译（AC-02，ZPL 官方 ^BQ 口径）**：`^FO{x},{y}^BQN,2,{放大倍数},{ECC}^FH^FD{ECC}A,{数据}^FS`——模型 2、掩码缺省 7 由打印机自选；`qrEcc` 双处一致映射（^BQ d 参数 + ^FD 必填 ECC 开关，官方 QR 开关形态 `<ECC>A,<数据>` 自动输入模式）；UTF-8 中文经整页 `^CI28` 可编译（真机需 JIS.DAT 表，归 AC-04 真机验收）。**尺寸与边距换算（^BQ 无边距参数，尽力近似口径）**：新增 `ZebraQrCodeMetrics`（ISO/IEC 18004 字节模式容量表 40 版本 × 4 ECC + 版本 → 模块数）估算符号版本 → 放大倍数 = 边长点数 ÷（矩阵模块 + 2×边距），夹取 1..10（兼容旧固件上限，官方现行 1..100）；`qrMargin` = 静区（与图片模式 ZXing Margin 语义一致），矩阵按 ^FO 内缩居中使符号物理尺寸近似模板边长。
- **测试（全绿）**：WinHost 编译器新增 16 个用例（理论展开 22 例：条码——^BC / ^BY 结构与 DPI 换算 / displayValue 映射 / 模块宽夹取 / 转义 / 空白跳过 / 中文 `LF_ENC_001` 与 fieldKey / Latin-1 与控制字符及多文种拒绝；二维码——^BQ 结构与放大倍数 / DPI 换算 / ECC 四档双处映射 / 边距 → 静区（放大倍数与内缩）/ 长内容版本估算 / UTF-8 中文 / 空白跳过；混合模板整页自包含（^XA…^XZ / ^PW / ^LL / ^CI28 / 三类元素各一 ^FD））+ 宿主集成 2 例（混合模板经真实 Zebra 插件 native 逐张编译（文本 + 条码 + 二维码 + 中文二维码数据）非 ^GF；Code 128 中文值端到端 `LF_ENC_001` + 插件 id + fieldKey）；迭代 78 的两条「条码 / 二维码显式失败」用例转为正向编译断言（净增 22 例）；全量 dotnet 850 项零回归（排除 Perf/Soak）。
- **记账**：ROADMAP 状态行；DESIGN 无契约变更（§5.4 未改动，实现细节按 ZPL 官方资料实施）；真机扫码对比（AC-04）转 `待验收`（恢复条件 = Zebra 真机 + 扫码枪可得）。

## 迭代 78：插件命令打印实现 2/4——Zebra 编译器·文本（内置字体度量与文本元素编译） · 2026-09-17

- **动机与范围（#120；DESIGN §5.4.8 拆分步 2，依赖迭代 77 宿主链路 `6b72a4a`；契约 §5.4 已定稿含功能裁剪补遗，本轮只落码不改契约）**：Zebra 官方插件 `labelframe-transport-zebra` 实现首批 `ILabelCommandCompiler` 能力——文本元素编译（整页自包含 / `^A` 字体与字高 DPI 换算 / 加粗近似 / `^FO` 定位）+ 内置字体度量接 `ITextWidthMeasurer`（锚定 / 对齐起始坐标尽力近似）+ `printMode` 参数声明 + 前端「无预览，效果以真机为准」提示。
- **Zebra 编译器（AC-02，`LabelFrame.TransportPlugin.Zebra` 新增 `ZebraLabelCompiler`）**：整页自包含结构 `^XA` / `^PW` / `^LL` / `^CI28`（UTF-8）/ `^XZ`（宿主不包装追加）；文本元素 `^FO{x},{y}^A{font}N,{h}[,{w}]^FH^FD{data}^FS`——毫米 → 点按 DPI 换算（与图片模式 `ZplImageEncoder` 同口径），模板保留的 ZPL 内置字体名原样输出（默认缩放字体 0），加粗近似 = 粗体字体变体映射（`0→1`，决策 #49 方案 A 先例，映射表可调）；字段数据 `^FH` 十六进制转义（`^` / `~` / `_`，`_` 先转义防二次替换）。**排版降级（§5.4.1 功能裁剪，不实现不模拟）**：超长文本不换行不缩放直接输出（溢出为预期常态，不做溢出校验）；锚定 / 块内 `TextAlign` / 垂直对齐按内置字体度量编译期换算起始坐标作尽力近似，无法近似即按起始坐标直接输出。**显式拒绝（§5.4.4）**：中文等内置字体未覆盖字符 → `LF_ENC_002`（含首个问题字符码点 + fieldKey）；条码 / 二维码（迭代 79）与图片 / 线元素按编译失败显式拒绝，不自动回退图片；区域元素 = 版式容器（仅参与锚定解析，边框为图片模式能力裁剪）。
- **内置字体度量（决策 #110 编译侧兑现）**：新增 `ZebraTextWidthMeasurer : ITextWidthMeasurer`——缩放字体 0 按 Helvetica 同度量比例字宽表（1/1000 字高）估算，位图字体 A-D 按官方设计点阵（9×5 / 11×7 / 18×10 / 18×10 点）单元格换算；度量仅供锚定近似（尽力项口径，不追求与图片模式等价）。`ZebraBuiltInFontMetrics` 内聚字体知识（品牌知识归属品牌插件，§5.4 动机）。
- **能力与参数声明（AC-01）**：`ZebraTransportPlugin` 同一插件对象实现 `ILabelCommandCompiler`（品牌级能力，与连接实例无关，委托无状态编译器）；参数清单增 Select `printMode`（必填、默认 `image`、Options 图片（默认）/ 原生指令）——注册表装配期能力位自动为真，`GET /api/transport` 经既有 DTO 透出（零前端品牌硬编码）；`Describe` 原生模式附加「（原生指令）」标识。
- **前端提示（AC-04，§5.4.2 口径）**：原生指令模式无指令级预览——连接为 native 时界面出现「原生指令模式无预览，效果以真机为准」提示（新增 `NativePrintModeHint`）：数据与打印页操作区（连接态判断 `isNativePrintMode`，与后端读取口径一致——缺失 / 非法值不视为 native）+ 设置页连接面板（选择即时反馈）；`TransportPluginInfo` 类型增 `supportsDocumentCompile` 增量字段。
- **测试（全绿）**：WinHost 新增 30 项——编译器与度量器 27 项（能力位与参数声明 / 整页结构与 DPI 换算 / 字体与加粗映射 / 转义 / Latin-1 接受 / 超长直出不换行不缩放 / 区域锚定 Start↔End 起点偏移可断言 / 块对齐近似 / 中文与多文种及控制字符 `LF_ENC_002` / 四类范围外元素显式失败 / 度量器确定性口径）+ 宿主集成 3 项（真实 Zebra 插件 native 分派：作业指令字段为原生指令非 `^GF`、逐张内容区分；image / 缺省路径经真实插件连接零回归；中文端到端失败含插件 id 与中文原因）；前端新增 7 项（`isNativePrintMode` 读取口径 / 设置面板提示出现与切回消失 / 数据打印页连接态提示出现与图片模式不出现）。AC-04 另附浏览器自动化走查截图证据（见 Issue #120 回评）。
- **记账**：ROADMAP 状态行；DESIGN 无契约变更（§5.4 未改动，实现细节按 ZPL 官方资料实施）；真机文本对比（AC-06）转 `待验收`。
## 流程卫生：轮值协议补齐 worktree / 本地分支回收闭环 · 2026-09-17

- **动机（#126）**：`C:\work\OpenCode` 累积 18 个 `LabelFrame-wt*` 半删残骸与 18 条已合并本地分支泄漏。根因：① `git worktree remove` 在 Windows 删不动 `web/node_modules`（pnpm junction / 文件锁）会**静默半删**（注册表已清、目录残留），迭代轮值虽有清理指令但无「验证目录消失」与兜底对账；② 验收 worker 自建 worktree（取证 / 结项 PR）用毕无人管，验收协议全文未提 worktree；③ squash 合并使 `git branch -d` 误判未合并，本地分支清理指令不可执行。
- **协议修订（`.zcode/automations/`，随本变更首次入库版本化）**：`iteration-duty.md` v7——「三」合并后回收加固（`remove --force` → 确认目录确实消失 → 残骸 rm -rf 补删 → 汇报；删分支核实 PR 已合并后用 `-D`）；「一」扫描加每轮 worktree 残留对账兜底（prune + 扫兄弟目录 `LabelFrame-wt*` 对照注册表与在途迭代）。`acceptance-duty.md` v6——worker 自建 worktree 用毕回收（进安全边界，与沙箱「用毕清理」同款纪律），漏网由迭代轮值对账兜底。
- **存量清理（本机一次性）**：18 个残骸目录与 18 条已合并本地分支（逐一对到已合并 PR 验证后删）已清除；在途 `wt78`（迭代 78）保留。纯流程 / 文档变更，无代码改动。

## 迭代 77：插件命令打印实现 1/4——宿主链路落码（能力接口与提交分支） · 2026-09-17

- **动机与范围（#119；DESIGN §5.4.8 拆分步 1，契约迭代 76 / #113 已定稿，本轮只落码不改契约）**：把插件命令打印契约的宿主侧链路落成代码——Core 契约类型 + 插件能力位 + `printMode` 连接参数消费 + 提交编译分派 + `LF_ENC` 新码入注册表。本迭代无品牌插件（Zebra 编译器实现归步 2 / 3，#120 / #121），全链路用 fake 编译器单测验证；界面无可见变化（前端零改动，首个真实 `printMode` 参数随步 2 出现）。
- **Core 契约（AC-01，签名与 DESIGN §5.4.1 逐字对齐）**：新增 `ILabelCommandCompiler`（`CompileAsync(LabelDocument, LabelCommandCompileOptions, CancellationToken)`）+ `LabelCommandCompileOptions(Dpi, Context)` + `LabelCommandCompileResult(Command, ErrorCode, ErrorMessage, FieldKey)`（`LabelFrame.Core.Transport.Plugins`，可选能力模式，先例 `ITestableTransport` / `IPrinterStatusProvider`）；`TransportPluginDescriptor` 增能力位 `SupportsDocumentCompile`（默认 false，装配期 `plugin is ILabelCommandCompiler` 判定），注册表新增 `GetCommandCompiler(id)` 取用；`GET /api/transport`（availablePlugins）与 `/api/transport/plugins` 经 `TransportPluginDescriptorDto` 透出该位（向后兼容增量字段 `supportsDocumentCompile`）。
- **printMode 消费（AC-03）**：新增 `TransportPrintMode`（Key `printMode`，`image` 默认 / `native`；读取口径 = 缺失 / 空白 / 非法值回退 image，忽略大小写，#69 兼容演进风格）；保存层校验——`TransportManager.Validate` 拒绝「native + 无能力位插件」（中文提示「请切回图片或更换插件」，走既有校验错误响应）；提交兜底——存量异常配置（手改 connection.json / 插件降级后能力消失）在提交时显式失败 `LF_ENC_003`，**不静默按图片打印**。
- **提交分派（AC-02，本地直连 `POST /api/jobs` 与 Server 路由领取 `HandleClaimedJobAsync` 共用 `JobSubmissionService` 入口）**：契约校验后按当前连接的打印方式分派——`image`（缺省 / 非法回退）→ 既有 Skia 整版渲染 → `^GF` 编码路径**零回归**；`native` → 插件编译器逐张编译（编译输入 = 原始 `LabelDocument`；意外异常由宿主捕获映射为同一失败语义，不透出堆栈），产物写 `LabelJobItem.Zpl`——**字段与 DB 列 `zpl` 不改名**，注释语义泛化为「持久化的打印机指令」；任一标签编译失败**整体拒绝**（直连不建作业 + 码 / 中文原因（含插件 id）/ fieldKey；Server 路由按既有失败回报路径回报 Failed，消息含问题码与中文原因）。
- **幂等不重编译（AC-04）**：native 路径提交前按 requestId 短路（`LabelJobQueue` 新增 `GetByRequestIdAsync`）——同 requestId 重放返回既有作业、fake 编译器调用计数零增加；图片路径维持既有「先渲染后入队幂等」顺序，行为零变化（模板后续修改不影响已入队作业的语义两模式一致）。
- **错误码注册表（Issue #119 待决议 1 用户定案：两新码）**：`LF_ENC_002`（指令编译失败——编译器无法原生表示元素 / 字符等，§5.4.4 口径）+ `LF_ENC_003`（打印方式配置异常——`printMode=native` 但插件无编译能力的提交兜底）入 `JobErrorCodes`，语义可区分、前端可编程分支（对齐 #107 注册表口径）。
- **测试（fake 编译器单测矩阵，全绿；fake 插件仅测试工程使用不进产品装配）**：Core 11 项（装配期能力位判定 + 编译器取用 / 内置插件能力位为假 / Options 携带 DPI 与上下文 / printMode 读取口径 8 例）+ WinHost 15 项（native 逐张编译入 Zpl 且非 `^GF` / 编译失败整体拒绝含码、插件 id、中文原因、fieldKey / 意外异常映射无堆栈 / 无能力兜底 `LF_ENC_003` 不静默 / 缺失与非法回退 image 且编译器零调用 / 幂等重放不重编译 / 契约校验先于编译 / native + Log 兼容传输数据留痕照常不出图 / 路由回报 Failed 含问题码 / 保存校验拒绝（native + 无能力）与放行（native + 有能力，含持久化）/ DTO 能力位透出 + HTTP 层校验拒绝）；既有套件全绿（dotnet 798 项，排除 Perf/Soak；本迭代无前端改动）。
- **记账**：ROADMAP 状态行；DESIGN 无契约缺口（§5.4 未改动）。实现边界备注：编译输入 `LabelDocument.Images` 首版保持缺省——品牌编译器按裁剪原则（§5.4.1）不消费图片元素，首个需要图片资源的实现迭代在提交分派处按需填充，不改契约。

## 迭代 76 补遗：原生指令模式功能裁剪原则——排版与对齐降级（契约补充） · 2026-09-17

- **契约补充（用户 2026-09-17 两轮补充口径；DESIGN §5.4.1 / §5.4.5 + 决策 #137 ⑥）**：确立**功能裁剪总原则**——图片模式是**全功能基准模式**（模板全部能力以图片渲染为实现基准）；原生指令（命令打印）模式是按品牌官方命令支持范围**裁剪的子集**，功能差异属预期行为而非缺陷，实现不了的不强求实现。具体口径：① 自动换行（`wrap`）/ 缩小适应（`fitMode`）/ 行距（`lineHeight`）等排版增强**一律不实现**（指令语法通常不支持等效语义，编译器不模拟）——文本超长溢出为预期常态，不因此编译失败、不做溢出校验；② 对齐类能力（块内 `TextAlign` / 区域锚定 / 垂直对齐）无指令原生等价（ZPL 基本模型 = 定起始坐标后绘制元素）——编译器可按内置字体度量编译期换算起始坐标作**尽力近似**（#110 机制），不强求等价，无法近似即按起始坐标直接输出；③ 效果验收口径同步——溢出与对齐差异不列为验收缺陷（锚定近似效果以观察记录为准，不作阻断项）。
- 纯文档修订，无代码改动；实现迭代（迭代 77 / #119、78 / #120、79 / #121）按修订后 §5.4 执行（#120 的锚定相关 AC 已同步调整）。

## 迭代 75：「PDA 日志 / 设备日志」页面下线——回传链路不存在前的界面收敛 · 2026-09-17

- **动机与范围（#112；两项待决议用户 2026-09-17 拍板，均取建议项——下线范围 = 双形态一起下线 / 代码处置 = 直接删除组件与测试）**：`web/src/pages/PdaLogs.tsx` 同源组件双形态（客户端「PDA 日志」+ Server 管理界面「设备日志」）的数据来源已断供——PDA 自动回传随迭代 40（决策 #95，pc_host 测试模式废止）移除、WinHost 亦无向 Server 回传日志的调用（仅有本地接收端点，迭代 51 决策 #106），两个形态均无写入方、页面永远为空，空态文案面向开发者且与事实不符。纯前端呈现收敛——无公共契约变更（决策 #140）。
- **页面下线**：双形态导航入口 + 页面挂载一并移除（`App.tsx` 两个 TABS 数组的 `logs` 项与页面渲染分支），`TabId` 联合类型移除 `'logs'`；`PdaLogs.tsx` 与 `PdaLogs.test.tsx` 直接删除（git 历史可找回，未来回传立项时按新需求重写）。前端 API client 的 `getLogs` / `LogEntry` 描述层保留（与后端端点同寿命）。
- **后端零改动（AC-03）**：`/api/logs` 接收 / 查询端点（Server 与 WinHost 共享实现，决策 #106）与 logs.db 存储维持现状——未来回传能力的既有地基；为端点存在的服务端测试一律不动。
- **测试同步清理**：客户端 / Server 菜单裁剪守门用例（`App.client.test.tsx` / `App.server.test.tsx`）断言更新为「PDA 日志 / 设备日志」双入口均不存在。
- **验证**：`pnpm lint`（0 错误，6 项既有警告）/ `pnpm test`（300 项全绿——删除 `PdaLogs.test.tsx` 4 项，菜单守门断言更新）/ 双构建（client + server）通过；后端零改动（CI 复跑 dotnet 全量）。
- **记账**：DEPLOY 客户端界面功能清单更新（移除「日志」页）；DESIGN 决策 #140；ROADMAP 状态行。

## 迭代 74：模拟打印作业数据留痕——记录模板名与传入数据集合 · 2026-09-17

- **动机与范围（#109；两项待决议用户 2026-09-17 拍板，均取建议项）**：Log 传输（模拟打印）此前只在宿主日志记「收到 N 字节（^GF 整版位图指令，内容省略）」——接收到作业时的**原始数据（模板名称 + 传入数据集合）不落日志**，PNG 出图只能看渲染结果、看不到作业输入；本轮在 Log 模式提交点补数据留痕（联调 / 排障对照物 =「这个作业带了什么数据进来」）。发送指令（^GF ZPL）摘要留痕明确不做（完整指令已持久化在作业库 `LabelJobItem.Zpl`，用户裁定原始数据才是关注点）。
- **留痕形态（决策 #139）**：记录点 = 宿主层 `JobSubmissionService` 判定 Log 模式处（与 PNG 出图同判定点、互不干扰；插件层 `SendAsync` 只拿到编码后指令字符串、拿不到文档数据）；每次新建作业记**作业 ID、模板名（请求级 `templateName` 优先，回退自包含 `template.name`，均缺省记「未命名」）、标签张数、数据集合（字段键值紧凑形式，键 Ordinal 排序，值内换行转义）**；① **多张标签去重**——相同数据集合记一条 + 重复张数（批量打印不刷屏），不同数据集合各自记录（首次出现顺序）；② **单条上限 2048 字符（≈2KB）**——超限截断数据文本并标注完整长度（防异常大数据刷爆日志）；留痕失败只记一行原因、不影响打印链路（与出图同一防护口径）；幂等重放（同 requestId）不重复留痕；非 Log 模式（tcp9100 / winspool / zebra）不触发，行为零变化。
- **实现**：新增纯函数格式化器 `JobDataLogFormatter`（WinHost `Jobs/`，去重 / 截断 / 转义单点）；`JobSubmissionService` Log 分支在 PNG 出图后调用 `LogJobData` 写宿主日志既有通道（零 DI / 配置变更）。
- **测试（LabelFrame.WinHost.Tests 新增 14 项全绿）**：格式化器 10 项（单张字段键值 / 批量相同去重 + 重复张数 / 相同 + 不同混合三组序 / 键序无关去重 / 超限截断 + 完整长度标注 + 行长不超上限 / 边界长度不截断 / null 与空数据占位去重 / 换行转义单行 / 未命名回退 / 空列表）+ 提交服务 4 项（Log 模式提交留痕含作业 ID / 模板名 / 张数 / 字段键值；批量去重两场景固定；幂等重放不重复留痕；Tcp 模式零留痕且作业照常创建——AC-04）。
- **记账**：DEPLOY §9 模拟打印排障说明补行；DESIGN 决策 #139；ROADMAP 状态行。
## 迭代 73：界面文案用户化——开发者向文案修正与连接配置低频收纳 · 2026-09-17

- **动机与范围（#108；两项待决议用户 2026-09-17 拍板：收纳形态 = 默认折叠 / 技术信息去处 = 直接删除——非提示气泡 / 折叠段收纳）**：不懂打印 / 网络技术的普通用户在首次配置连接、日常打印、查看作业历史时被开发者向术语卡住（英文模式名、系统路径、文件扩展名、端点 / 联调 / 后端渲染等内部机制）；连接配置是低频操作，不应常驻占据设置页主要篇幅。纯前端文案 / 布局迭代——无 API 契约与状态逻辑变更，行为零变化（决策 #138；迭代 76 先合入占用 #137，本条顺延 #138）。
- **连接配置低频收纳（决议 1）**：设置页「连接方式」面板默认折叠为当前连接摘要一行（数据源 = 既有 `formatTransport`——新后端 displayText 优先、旧后端按 mode 用户语格式化）+ 展开箭头，点击面板头一步展开完整编辑区（≤2 步进入编辑）；切换 / 测试 / 保存流程交互与请求行为不变，新增用例覆盖「默认折叠显示摘要 / 点击展开 / 再点收起」。
- **文案用户化（决议 2，逐条审查清单见 PR 描述）**：① 模式标签与连接徽标用户语——`Log（模拟）`→`模拟打印`、`TCP`→`网络打印机（TCP）`、`Zebra`→`Zebra 打印机`，旧后端徽标 `LOG`→`模拟打印`、`TCP …`→`网络打印机 …`、`WindowsDriver …`→`Windows 打印机 …`；② 「调试模式」→「模拟出图」（生成标签图片、不实际打印——保留专业语义），「调试出图 / 出图预览」→「生成图片 / 图片预览」；③ 删除系统路径（服务端地址说明的 `%ProgramData%\LabelFrame\Client\settings.json` 与默认端口）、文件扩展名与技术参数（`.lfpkg` / `.lfplugin` / `MSI / zip` / `仅 .apk` / 测试页字节数 / `条码 LABELFRAME-TEST`）、内部机制术语（端点 / 联调 / 后端渲染 / 心跳 / WinHost / Skia / 同源、PDA 日志页 `POST /api/logs` 手动验证示例、旧客户端「连接管理端点」说明、「插件 DLL 直接放入插件目录」指引）；④ 提示 / 错误文案可行动化（测试打印→确认出纸、旧客户端保存→已存本浏览器建议升级、检查更新→下载安装包运行安装或找管理人员、导入失败→所选文件不是有效模板文件等）；⑤ 表头用户语（`requestId`/`jobId`→请求 / 作业编号、`deviceId`/`lastIp`→设备 ID / 最近 IP、`完成-失败`→完成 / 失败）。后端插件 displayName 为后端数据不在本轮范围（前端回退标签已用户化，两者措辞差异容忍）。
- **验证**：`pnpm lint`（0 错误，6 项既有警告）/ `pnpm test`（304 项全绿——新增折叠收纳用例，既有断言仅同步文案措辞、无行为断言改动）/ 双构建（client + server）通过；后端零改动。
- **记账**：DESIGN 决策 #138；ROADMAP 状态行。

## 迭代 76：插件命令打印契约设计——文档编译能力接口与打印方式参数（纯文档） · 2026-09-17

- **契约先行（强化路径，#113；三项待决议用户拍板，均取建议项——编译失败 = 显式失败不自动回退图片 / 切换粒度 = 仅连接级参数（不做作业级覆盖）/ 接口形态 = 提交时编译并持久化（否决发送时文档直发））**：DESIGN 新增 §5.4「插件命令打印契约」+ 决策 #137——① 插件可选实现 `ILabelCommandCompiler` 文档编译能力（输入 `LabelDocument` + DPI + 插件上下文，输出整页自包含品牌原生指令文本或失败结果；先例 `ITestableTransport` / `IPrinterStatusProvider` 能力模式；编译为纯函数性质；版式解析以品牌内置字体度量驱动，兑现 #110 `ITextWidthMeasurer` 预留）；② 打印方式 = 连接级 `TransportParameterSpec` Select（`printMode`：`image` 默认 / `native`，随 connection.json 先测试后生效；不设全局开关、不做作业级覆盖，#45 教训；`native` + 无能力插件 = 保存校验拒绝 + 提交显式失败兜底）；③ 宿主提交时逐张编译并把指令写入 `LabelJobItem.Zpl`（字段与 DB 列不改名、语义泛化），Worker 发送 / 重启续打 / 失败项重打 / requestId 幂等（不重新编译）/ 挂起恢复 / 批次节流零改动，Server 作业载荷零感知；④ 编译失败 = 显式失败（`LF_ENC` 新码 + 中文原因；直连不建作业 / 路由回报 Server Failed），不自动回退图片，首版不做元素级混合模式；⑤ 效果验收口径：真机双模式并排对比（文本 / 条码 / 二维码 / 区域锚定 #110 不回归 + 出纸速度），参照迭代 55 取证先例。
- **后续实现迭代拆分建议**（Issue 级粒度，§5.4.8 表，可作立项依据）：宿主链路落码（fake 编译器全链路单测）→ Zebra 编译器·文本（SDK 内置字体度量接 `ITextWidthMeasurer`）→ 条码与二维码 → 效果验收收口（可选）。
- **记账**：DESIGN 术语表（编码器 / 新增「命令打印」术语）+ §5.4 + 决策 #137 + §7 边界（PDA 侧待 Windows 验证后评估）；ROADMAP 状态行。本迭代纯文档，无代码改动。

## 迭代 72：模拟打印出图目录保留清理——Log 出图 print 目录超期自动删除 · 2026-09-17

- **动机与范围（#107；两项待决议用户 2026-09-17 拍板，均取建议项）**：Log 传输（模拟打印）每个作业把渲染位图落盘到出图目录（`HostOptions.PrintOutputPath`，默认 `%LOCALAPPDATA%\LabelFrame\print`，结构 `print\<jobId>\label-N.png`，写入点 `JobSubmissionService.SaveLogPrintImages`），该目录此前不在任何清理范围内、无删除 / 封顶 / 过期机制，长期联调 / 演示机器无限累积占盘——本轮补保留清理机制（WinHost 客户端，Windows 窗口与 Linux 无头共用路径），出图目录磁盘占用有界。
- **清理口径（决策 #136，对齐 #108 保留先例）**：**按天保留、默认 31 天**，`LABELFRAME_PRINT_IMAGE_RETENTION_DAYS` 可调（与 `LABELFRAME_APP_LOG_RETENTION_DAYS` 同构），**≤0 = 不清理**（关闭语义）；判龄 = 作业子目录 `print\<jobId>` 的 **LastWriteTime**（与 jobs.db 解耦——删除只影响 Log 模式「查看出图」的目录 / 张数展示，作业历史记录本体不受影响）；触发时机 = **客户端启动时 + 每次模拟打印落盘后顺带执行**（新增 `PrintImageRetentionCleaner`，**不新增常驻后台任务**，对齐 #108「启动与轮转时顺带执行」轻量口径）；清理摘要记宿主日志一行；遇目录被占用 / 权限失败**降级留痕（日志）不抛出**（下次触发再试），不影响启动、打印与出图主链路。非 Log 模式（tcp9100 / winspool / zebra）落盘后清理不触发（调用点位于 Log 分支内），启动清理为目录维护与传输模式无关。
- **实现**：`HostOptions` 新增 `PrintImageRetentionDays`（默认 31）+ `LABELFRAME_PRINT_IMAGE_RETENTION_DAYS` 环境变量覆盖；`Program` 启动时执行一次；`WinHostApp` 装配清理器注入 `JobSubmissionService`（Log 落盘后顺带执行；测试构造缺省 null = 不清理）。
- **测试（LabelFrame.WinHost.Tests 新增 `PrintImageRetentionCleanerTests`，6 项全绿）**：超期删除（含内部 PNG + 摘要留痕一行）/ 保留期内不删（5 天前 + 当日新打）/ 关闭语义（0 与 -1 双参实证）/ 清理失败降级留痕（独占句柄占用超期目录，不抛出、目录留存）+ **主链路不受影响**（占用失败在场的模拟打印提交成功且当日出图正常落盘）。
- **记账**：DEPLOY §9 配置说明补行；DESIGN 决策 #136；ROADMAP 状态行。

## 迭代 70：离线布局安装——布局目录生成与本地源无网首装 · 2026-09-17

- **离线布局目录（offline layout，决策 #135；DESIGN §6.2 / §6.10，对标 VS 安装器 layout 模式）**：内网 / 无外网机器首装产品化——IT 在有网机器把当版全部组件（Server / Client MSI、webui zip、官方插件 `.lfplugin`、runtime 引导器，按 manifest 条目）+ `install-manifest.json`（官方原样字节）+ `latest.json` + 引导 EXE 预下载汇集到一个目录，拷贝分发后目标机**用同一个引导程序**无外网完成首装。组件文件名约定 = urls[0] 路径末段（须含扩展名）+ 查询型直链固定名兜底表（`runtime-webview2` → `MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe`，核心库 `OfflineLayoutNaming` 单点，生成与消费共用）；单 EXE 全内嵌（attached container）不做（布局目录满足需求，§7 开放点登记）。
- **生成双形态（用户拍板：两者都做）**：① 独立脚本 `scripts/make-offline-layout.ps1`（IT 手段；引导 EXE 来源 = `-BootstrapperPath` 本地产物，本地缺省时按 `-BootstrapperUrl` 从 Release 下载——引导 EXE 已随 Release 发布（迭代 68 / 决策 #132））；② 引导程序 `--layout <目录>` 参数（产品化；EXE 自复制 `WixBundleOriginalSource`，`--manifest <路径|URL>` 可覆写生成清单源，默认稳定通道；进度窗 + 取消 + 结果对话框，`-passive` 静默）。两者同一语义：逐组件按 urls 顺序下载、sha256 与 manifest 逐字节一致才落位（不符换下一源、全源失败非零退出 fail-closed）、重复生成幂等（已存在且哈希一致复用、篡改 / 旧版残留重下）。引擎原生单横线 `-layout`（`LaunchAction.Layout`）由 BA 路由进同一生成流程（`IBootstrapperCommand.LayoutDirectory`），与双横线 `--layout` 同语义。
- **源解析顺序契约（用户拍板：a) 隐式优先源目录，manifest schema 零修改；#115 联动）**：清单来源为**本地路径**时其所在目录 = 隐式优先源目录，每链包有效源序 = **[布局目录本地文件（在位时）] ++ urls**——失败计数与换源语义不变（本地获取 / 校验失败按既有推进语义落到 urls，全源耗尽 fail-closed）；BA 在 `CacheAcquireBegin` 以 `IEngine.SetLocalSource` 注入本地文件，引擎对本地源副本同样按包内嵌摘要（= manifest sha256）强制校验（#117 口径）——**篡改布局内组件被校验拦截**（在线换 urls 重取干净副本 / 断网源耗尽失败，篡改内容绝不落装）。布局不在位 / URL 清单 → 纯 urls，行为与现状完全一致（AC-03 回归锚点）。
- **离线首装入口（隐式检测）**：引导 EXE 同目录存在 `install-manifest.json` → 欢迎页清单来源默认该本地文件（`WixBundleOriginalSourceFolder` 探测 + 提示条），拷目录双击 EXE 即零网络起步；无邻接清单 → 默认稳定通道（现状不变）。欢迎页离线指引文案随布局目录落地更新（#53 决议 1 方案 A 指引的收口）。
- **实现**：核心库新增 `OfflineLayout/`（`OfflineLayoutNaming` / `OfflineLayoutBuilder` / `LayoutModeArguments`，net48 / net10 双腿）；`CacheSourceFallback` 扩本地源（`SourceFallbackOutcome` 增 `LocalPath` / `IsLocal`，`FromManifest(manifest, layoutDirectory)`）；`WizardSession` 增 `LocalSourceDirectory`；BA 接线（布局生成模式 + `SetLocalSource` + 邻接清单检测 + `LayoutProgressForm` 进度窗）；`InstallManifestLoader.IsHttpUrl` 公开。
- **测试与取证**：Bootstrapper 测试全数保留并新增至 **218 项全绿**（命名约定 / 布局生成 / 参数解析 / 本地源优先与回退 / WizardSession 本地源；既有多源回退用例全数保留通过——AC-03）；走查脚本 `scripts/test-bundle-offline-layout.ps1`（矩阵脚本同构：per-user 测试 Bundle + 真实 BA + 本地测试源）本地同构取证 **31 项断言全过**——G 生成（目录完整 + 哈希一致 + manifest 原样 + EXE 自复制）、I1 零外网首装（测试源在线但访问日志零命中 + 邻接清单默认 + 本地源命中日志）、I2 篡改 + 断网 fail-closed（校验拦截 + 源耗尽 + 篡改内容未落装）、I2b 篡改 + 在线换源重取（有效源序 [本地] ++ urls 实证）、R1 无布局在线回归（纯 urls 下载，行为与现状一致）；`make-offline-layout.ps1` 对本地源同构取证（成功生成 / 源不可达 fail-closed / 幂等复跑零组件下载）；既有下载矩阵脚本复跑通过（S1~S6 全绿）。**AC-02 断网实机口径（断网 VM / 实机 + 抓包）本机无法自证，转待验收**。
- **记账**：DESIGN §6.2（布局目录契约）/ §6.3（offline 预设落地形态）/ §6.4（分发源策略）/ §6.10（本地源优先源解析顺序）+ 决策表 #132 + §7 开放点；ROADMAP 状态行随结项更新。

## 迭代 71：Linux 服务端一键安装——install.sh 与 compose 随发版分发 · 2026-09-17

- **动机与范围（#91；用户 2026-09-15 会话定案「脚本 + compose 分发都做」）**：Linux 服务端部署从「Docker compose 手抄仓库文件 / systemd 裸机 DEPLOY §5 手工步骤（framework-dependent 需自备 runtime——与 Windows 侧 AspNetCore 前置教训 #128/#129 同源）」升级为「一条命令」——脚本即 Linux 的「安装程序」（契约假设 Linux 操作者具备 IT 能力，图形 / CLI 向导不做，#50 边界维持）。三项待决议用户拍板：self-contained 归档为默认（免 runtime 前置）/ 离线模式本轮支持 `--manifest` 本地路径与布局目录（衔接迭代 70 #89）/ runtime 缺失明确报错给官方直链、不自动安装。**Linux 部署契约先行入 DESIGN（决策 #134 + §6.12，强化路径）**。
- **`scripts/install-server-linux.sh`（新）**：消费 install-manifest.json（schemaVersion=1 超范围 fail-closed；sha256 逐源强制 + urls 多源回退，#115/#117/#125 同义）→ 拉取校验 Linux 归档 → staging 解包校验（入口在场 + 形态探测，失败不动存量安装）→ 整体替换 `/opt/labelframe/server`（`appsettings.json` 保留用户版本，对齐 #48；`/var/lib/labelframe` 数据与日志目录不动）→ systemd 单元安装 / 启用 / 重启 → 管理界面 zip 落位 `plugins/web-ui`（默认装，`--no-webui` 关）→ 自检 `/healthz` 并核对 `/api/server/info` 版本。参数：默认官方稳定通道；`--manifest <本地文件 | 布局目录 | URL>`（本地清单所在目录即布局目录，本地文件优先、urls 回退，全量本地命中零外网请求）。归档形态按内容探测（self-contained 含 `System.Private.CoreLib.dll`），FDD 检测 `Microsoft.AspNetCore.App >= 10`，缺失 fail-closed 报官方下载页直链不自动安装。行级 manifest 解析（无 jq 依赖）；libicu 预检（最小化镜像缺失时服务启动即崩，给 apt 提示）。
- **归档默认 self-contained**：`publish-server-linux.ps1` 默认翻转（`-FrameworkDependent` 为可选），Release 附件 `linux-server` 归档不再要求目标机预装 .NET 10；release.yml 调用零变化（默认值在脚本内翻转）。
- **compose 随发版分发**：release job 新增步骤——`compose.yml` 与 `packaging/ubuntu/docker-compose.yml` 同源复制（防漂移，含管理界面 / client-packages / pda-packages / plugin-packages 四组挂载注释）+ `.env`（`LABELFRAME_VERSION` 钉定当版 + 镜像覆盖注释模板）随 Release 附件发布；两者不进 install manifest（部署描述文件而非可安装产物，#116 server-docker「无下载组件」口径维持）。
- **随迭代修复**：`deploy-server-ubuntu.sh` 两处字面 `
` 编码缺陷（`DATA_DIR`/`LOGS_DIR` 拼行 + systemd 单元 LOG_FILE 行粘连——`set -u` 下重跑必挂）。
- **本地验证（Docker 容器自证：ubuntu:24.04 + systemd 特权容器）**：**AC-01 / AC-05**——离线容器（`--network none`）布局目录安装全流程：systemd active + `/healthz` ok + 管理界面 `/` 200（text/html）+ 版本 0.27.1 与清单一致 + 文件日志落盘，零外网请求；**AC-02**——篡改归档一字节 → 本地哈希不符 → urls 回退全败 → 拒绝安装（exit 1），存量服务 / 用户配置无损；**AC-03**——重跑幂等（appsettings 用户值、数据目录标记、三个 SQLite 库保留，staging 替换无陈旧残留，服务重启后版本一致）；在线模式——URL 清单 + 首源 404 回退第二源成功（本机 HTTP 源同构模拟公网通道，代码路径同一 curl 下载链）；FDD 双向——无 runtime 明确报错含官方直链（存量无损）/ 装官方 aspnetcore-runtime 10.0.12 后重跑成功；真实发布清单解析冒烟（gh 下载 v0.27.1 `install-manifest.json` 字节精确——版本 / 归档名 / urls 提取正确）；**AC-04 本地同构演练**——复刻 release job compose 步骤生成 `compose.yml` + `.env`（断言全过），`docker compose config` 解析镜像钉定 `ghcr.io/marci-labs/labelframe-server:0.27.1`。演练抓修两处脚本缺陷（schemaVersion 取值带逗号、最小镜像缺 libicu 预检缺失）。`dotnet build` 0 警告 0 错误；`dotnet test`（排除 Perf/Soak）**709 项全绿**（与迭代 68 基线一致）；前端无改动。
- **记账**：DESIGN 决策 #134 + §6.12 + §6.3 联动；DEPLOY §4 compose 快速启动 / §5 Ubuntu 一键安装推荐（手工路径降高级）/ §8 发布链 / §10 脚本索引；CHANGELOG 本条目；ROADMAP 状态行。真机 Ubuntu 走查与下次 `v*` 发版 Release 附件实证（compose / self-contained 归档公网通道）转 `待验收`。

## 迭代 69：安装卸载收尾对称化——Bundle 卸载清理插件落位目录 · 2026-09-17

- **缺陷与根因（#93；来源 = #55 AC-01 干净 VM 重验外观察项）**：Bundle 卸载时 web-ui 落位目录被清理（且依赖 Server MSI「清除用户数据」rmdir 的附带效应，非 Bundle 自身编排），**zebra 插件落位目录残留**（约 10MB / 29 DLL 含 Zebra SDK 全套伴生依赖）——卸载序列里品牌插件落位包缺少对称清理动作。后果不止垃圾残留：卸载 → 重装不选 Zebra 的机器上，客户端插件扫描仍加载残留目录、Zebra 品牌依然可用，违背 #56「彻底外置」决议与问卷组件集合口径。
- **卸载语义契约入 DESIGN（契约先行，强化路径）**：§6.8 新增「卸载清理」段 + §6.9 链表扩为九包 / 失败处置段修订 + 决策 #133——清理边界 = **只清 Bundle 落位的目录**，**用户手动放置的第三方 DLL 不动**（用户资产；`plugins` 父目录其余内容永不动，父目录仅在清理后已空时移除）；与 #123 覆盖安装、#68「删目录 + 重启生效」（客户端运行时口径不动）、#127 加载生命周期（不改客户端加载机制）的边界关系显式记账。
- **实现载体定案（决策 #133，两项待决议按 Issue #93 用户拍板执行；按 Burn 机制评估、构建实证 WiX v7 WIX0408 后收敛）= 成对清理包**：**落位包（`WebUiPlacement` / `ZebraPluginPlacement`）维持 `Permanent` 与既有语义零改动**——非 Permanent ExePackage 必须有 DetectCondition（WIX0408），而引擎级 Present 跳过会改写「webui 同版重跑覆盖重写 / 插件 #123 版本比较在工具内执行」两条既定语义；清理由链内新增的**非 Permanent 成对清理包**（`WebUiPlacementCleanup` / `ZebraPluginPlacementCleanup`）承担：install = PayloadTool 无参空操作（登记包已装供 Burn 卸载规划），`UninstallArguments` = `-clean -target [落位目标变量]`（PayloadTool 新增 `-clean` 模式，与落位同目标对称）。**否决 BA 卸载路径显式清理**：无包状态机（需自记落位清单）、引擎事务外执行（无回滚语义、与 MSI 卸载时序竞争），违背 #122「引擎负责链装 / 回滚 / 提权 / 日志」立场。
- **落位凭据 = 清理边界的技术保证**：落位成功时 PayloadTool 经 `-version` 在落位目录根写 `.labelframe-bundle-placement`（内容 = Bundle 版本）；BA 启动期（Detect 前）读凭据比对 `WixBundleVersion` 写入 Burn 变量，清理包 DetectCondition 消费——**只有 Bundle 落位的目录携带凭据**（客户端「插件管理」通道安装的官方插件与一切手动资产无此文件，永不被 Bundle 卸载清理）；凭据随目录消亡，无记账漂移；升级（RelatedBundle 移除旧 Bundle）的引擎执行序无论先移除旧链还是先装新链均正确（版本匹配使旧清理包不误删新落位）。
- **清理时机**：Bundle 卸载 / **Modify 改选**（问卷改组件集合即时生效——重跑取消品牌即清理其落位目录）/ 首次安装失败回滚（失败后回到装前状态，较此前「留落位内容」更对称）。容错：目录不存在 = 幂等成功；清理失败日志中文留痕、不阻断卸载主链。落位目标变量改由 BA 启动期写入（ARP 卸载非交互会话的 `[变量]` 引用同样可解析）。机制通用化：后续品牌落位包按同构成对扩展。
- **测试**：新增 `PayloadCleanupTests`（卸载清理断言 / 卸载 → 重装不选品牌组件集合断言 / 手动放置不误伤断言 / 清理后同版重装不拒绝 / 凭据写入与通道区分 / 凭据随目录消亡）+ `PlacementMarker` 探测单测 + `scripts/test-bundle-uninstall-walkthrough.ps1`（per-user 走查：真实 BA 向导 + 真实 PayloadTool 落位 / 凭据 / 清理，S1 装（选 Zebra）→ S2 ARP 形态卸载（目录清理干净 + 凭据断言 + 第三方资产保留 + MSI 卸载正常）→ S3 卸载 → 重装不选 Zebra（zebra 目录不存在对照实证），本地取证不进 CI；清理包 DetectCondition 以 `WixBundleInstalled` 替代 BA 凭据探测变量属测试口径差异，已注释说明）；既有 `PayloadPlacerTests`（#123 版本比较）不回归。

## 迭代 68：流程治理——引导 EXE 随 Release 发布（release.yml 接线）+ 安装引导文档补章 · 2026-09-17

- **动机与范围（#101；收口决策 #121 ④ / #122 ⑤ 两项后置——引导 Bundle 此前只能本机 `build-bundle.ps1` 构建，Release 附件不含，用户测试 / IT 分发都需先具备构建环境）**：推 `v*` tag 后发版链自动构建 `LabelFrame-Bootstrapper-<版本>.exe` 并随 GitHub Release 发布；`docs/DEPLOY.md` 补「安装引导程序」章节、README 安装入口同步（两项待决议用户拍板：签名复用 MSI 自签通道 a / 引导 EXE 列为推荐入口、MSI 降高级路径 a）。引导程序功能与行为零改动（专项 #53~#57 已交付的七页向导 / 升级模式 / 下载链不动）。
- **release.yml 接线（决策 #134）**：新增独立 Windows `bundle` job（needs = package + android——bundle 消费的 manifest 需六类产物在场）：下载 release-assets + android-apk → `generate-install-manifest.ps1` 按与 release job 完全相同参数生成当版 manifest → `build-bundle.ps1 -ManifestSource release/install-manifest.json` 构建引导 EXE（Secrets 在场时 `-Sign`）→ fail-closed 断言 EXE 在场 → 提取 runtime 三条目 sha256 作 job outputs → 上传 artifact；`release` job needs 增 bundle、下载 EXE 后纳入 Release `files`。**manifest 同源机制 = 分阶段生成 + 跨 job 一致性断言**（Issue 点名首要风险）：release job 既有「生成安装清单」「断言安装清单」两步骤 YAML 零变化，追加一致性断言步骤——发布清单与 bundle 消费侧的 runtime 哈希逐条比对，不一致即整次发版失败（防厂商在两次生成之间轮转直链文件导致发布 manifest 哈希 ≠ 引导 EXE 内嵌摘要的错位），重跑自愈。
- **脚本增强**：`generate-install-manifest.ps1` 新增可选 `-RuntimeDownloadDir`（bundle job 与 build-bundle.ps1 缓存目录打通，同 job 内 runtime 只下载一次；默认空 = 既有临时目录行为不变）；`build-bundle.ps1` 新增 `-Sign / -PfxPath / -PfxPassword`（signtool 定位与签名参数与 `build-msi.ps1` 完全对齐，SHA256 + RFC3161 时间戳）。发版时长评估结论：runtime 共约 120MB × 两 job 各一次直连下载，发版低频，Actions 缓存 key 与 .NET 补丁版本 / webview2 轮转联动复杂度不抵收益，维持不加（记入决策 #134 ③）。
- **文档补章**：DEPLOY.md 新增「§2 安装引导程序（推荐安装入口）」——下载入口 / 七页向导与拓扑预设流程 / 运行时前置链说明 / SmartScreen「未知发布者」处置 / Burn 日志位置（`%TEMP%\LabelFrame*.log`）；原 MSI 章节降为「§3 高级路径」，后续章节顺延至 §10（README / release.yml 注释 / AndroidHost README / `InstallTargets.cs` 注释交叉引用同步）；README 快速开始三个安装入口与部署形态对照改为引导 EXE 推荐；§8 自动化发布补引导 EXE 构建链与签名说明。
- **本地同构演练（#97 / #131 先例手法；三项必需检查不执行 release.yml，复核靠本地演练 + YAML 自检）**：以 v0.27.1 真实 Release 六类产物在场模拟 CI——① 分阶段生成：`generate-install-manifest.ps1 -RuntimeDownloadDir artifacts/runtimes` 生成 + 断言 9 组件全过；② bundle 全链：`build-bundle.ps1 -ManifestSource release/install-manifest.json` 三个 runtime **缓存命中零重复下载**（哈希逐条校验通过），Bundle 产出 1.92 MB（与迭代 62 基线一致，4 条警告为既有可接受项）；③ 一致性：release 侧重生成后 runtime 哈希与 bundle 消费侧、官方 v0.27.1 发布 manifest **三方逐条一致**（同源决定性实证）；④ 签名：`create-signing-cert.ps1` 演练证书 + `-Sign` 构建通过，`Get-AuthenticodeSignature` 签署者 CN=LabelFrame（未信任链 UnknownError 属自签预期，与 MSI 同口径）；⑤ fail-closed 反向用例（意外实证）：缺任一产物（演练时漏 linux 归档）manifest 生成即拒绝（缺产物 = 空条目不可能落盘）。**演练抓到并修复一个真实缺陷**：脚本局部变量 `$runtimeDownloadDir` 与新参数 `$RuntimeDownloadDir` 同名——PowerShell 变量名大小写不敏感，参数被首行赋空清掉、恒走临时目录分支（缓存共享失效）；局部变量改名 `$runtimeAcquireDir` 修复。
- **验证**：release.yml YAML 解析自检通过、bundle / release 两 job 步骤序静态复核（五类产物齐备后才生成 manifest、EXE 在场后才创建 Release）；`dotnet build`（0 警告 0 错误）+ `dotnet test`（排除 Perf/Soak）**709 项全绿**（与迭代 62/64 基线一致）；前端无改动。真实发版走查（Release 附件含引导 EXE，AC-02）与干净 Windows 环境下载运行七页向导（AC-03）转 `待验收`。
- **记账**：DESIGN 决策表 #132 + §6.2 生成点补分阶段口径；CHANGELOG 本条目。
## v0.27.1 补丁发布（迭代 64 返修 + 迭代 67） · 2026-09-16

- **打包范围**：v0.27.0 之后合入 master 的两项缺陷修复——迭代 64 返修（PR #96：覆盖升级链依赖降版——发布工件版本固化与降版回归断言，决策 #130）与迭代 67（PR #98：release.yml 步骤序修复——Client MSI 附带插件包，流程治理 #97，决策 #131）。无用户可见功能变更，dotnet / 前端代码零功能改动（仅依赖钉版）。详见各迭代条目。
- **覆盖升级崩溃修复（迭代 64 返修）**：v0.27.0 Client 发布工件较 v0.26.0 依赖降版（SkiaSharp / Microsoft.Extensions.DependencyModel），叠加确定性组件 GUID 跨版本复用触发 Windows Installer 组件规则拒装降版 keyfile → 覆盖升级净结果两文件缺失 → WinHost 首启即崩（全新安装不受影响）。本版将依赖钉至 0.26 实测值（SkiaSharp 3.119.2、Microsoft.Extensions.DependencyModel 10.0.7，全仓统一），并新增 `scripts/assert-client-deps-baseline.ps1` 降版回归断言挂接 `build-msi.ps1`（CI 必需检查与发版链共用入口，降版即构建失败）——0.26 直升与坏 0.27 修复升级两条路径组件版本语义干净。
- **Client MSI 插件包附带修复（迭代 67）**：v0.27.0 发版链步骤序缺陷（插件包构建晚于 Client MSI 打包且静默跳过）致公网 Client MSI 无 `plugin-packages\*.lfplugin`，存量升级的 zebra 自动安装不可能发生；本版插件包构建步骤上移至 Client MSI 打包之前，并加 fail-closed 断言（`scripts/assert-msi-plugin-package.ps1`，缺失即构建失败）——本版起发版 Client MSI 附带 `labelframe-transport-zebra-*.lfplugin`。
- **版本同步**：ServerOptions / HostOptions `ProductVersion` 与稳定版 Compose 默认版本更新为 `0.27.1`（客户端「检查更新」比较口径随升，决策 #126）。
- **发布产物**：与 v0.27.0 同清单（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档、AndroidHost APK、Zebra 官方插件 .lfplugin、install-manifest.json + latest.json）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.27.1` / `labelframe-client:0.27.1`（均含 `latest`）；Client MSI 本版起由发版链断言实证附带插件包。引导程序 Bundle EXE 仍不随发版构建（#122 后置决策），需要时本地 `scripts/build-bundle.ps1` 构建手动上传。

## 迭代 67：流程治理——release.yml 步骤序修复：Client MSI 附带插件包（升级自动安装链修复） · 2026-09-16

- **缺陷与根因（#97；来源 = #57 AC-01 + #56 AC-04 合并升级走查取证，v0.27.0 公网实证）**：release.yml 中「构建 Zebra 官方插件包（.lfplugin）」步骤（PR #80 引入）物理位置在「打包 Client MSI」**之后**，而其步骤注释自称"先于 Client MSI 构建"——`build-msi.ps1` 打包时检测不到 `artifacts\labelframe-transport-zebra-*.lfplugin` 即按设计静默跳过附带 → 公网发版 Client MSI 无 `plugin-packages\*.lfplugin`（File 表 48 文件 0 命中）→ 存量升级的 zebra 自动安装（决策 #123 迁移机制）不可能发生；对照实验已证附带包在位时迁移功能完全正常（自动安装 loaded=true / isExternal=true），缺陷仅在流水线步骤序。
- **修复（流程治理迭代，`.github/workflows/release.yml` 变更已获用户立项批准）**：插件包构建步骤整步上移至「打包 Client MSI」之前（步骤注释与实际顺序对齐）；「打包 Client MSI」后新增 fail-closed 断言步骤——`scripts/assert-msi-plugin-package.ps1`（新增，Windows Installer COM 查 File / Component / Directory 三表，断言 `labelframe-transport-zebra-*.lfplugin` 落在 `plugin-packages` 目录，手法同 `assert-client-deps-baseline.ps1` / 决策 #130），缺失即构建失败。`build-msi.ps1` 的缺包静默跳过行为**保留不动**——本地裸构建与 CI「MSI 结构断言」必需检查依赖该行为（两者本就无插件包产物）。
- **本地同构演练（#97 AC-01 / AC-02 允许口径；三项必需检查不执行 release.yml，复核靠本地演练 + diff 自查）**：release.yml YAML 解析自检通过；AC-01 正向——`build-zebra-plugin.ps1 -Version 0.0.1`（9.88 MB，29 DLL）→ `build-msi.ps1 -Version 0.0.1`（22.6 MB，构建日志「随 MSI 附带官方插件包：labelframe-transport-zebra-0.0.1.lfplugin」）→ 断言通过（exit 0，File 表命中行：`plugin-packages\labelframe-transport-zebra-0.0.1.lfplugin`，组件 `ZebraPluginPackageFile`，目录 `PluginPackagesDir`）；AC-02 反向——移走 `.lfplugin` 后重建（build-msi.ps1 静默跳过，MSI 13.1 MB），断言 exit 1 拦截（fail-closed 实证），恢复插件包重跑构建（22.6 MB）与断言（exit 0）通过。
- **记账**：DESIGN 决策表 #131；v0.27.1 补丁发版由用户在修复合并后推 `v*` tag 触发（发版后走查 Client MSI 附件含插件包交回验收）。dotnet / 前端代码零改动（`dotnet build` / `dotnet test` 全绿仅为 DoD 例行确认）。

## 迭代 64 返修：覆盖升级链依赖降版——发布工件版本固化与降版回归断言（AC-01 验收不通过回流） · 2026-09-16

- **缺陷与根因（决策 #130；#57 AC-01 真实公网覆盖升级走查实证，证据链 `artifacts/accept-upgrade-027/`）**：v0.27.0 Client 发布工件较 v0.26.0 依赖降版（SkiaSharp.dll 3.119.2.0→3.119.1.0、Microsoft.Extensions.DependencyModel.dll 10.0.726.21808→8.0.23.53103），叠加确定性组件 GUID 跨版本复用 → Windows Installer 组件规则拒装降版 keyfile（Client MSI 日志 6 处 Disallowing）→ 同次升级旧产品卸载删旧文件 → 净结果两文件缺失 → WinHost 首启即崩（FileNotFoundException: SkiaSharp）；全新安装不受影响，仅覆盖升级路径命中。根因（两版还原图对称 diff 实证）：0.26 的高版本是 WinHost 当时引用的 Zebra.Printer.SDK 5.0.3685 传递钉定顺带抬升，迭代 63（#123）外置化移出后闭包回落到本仓自钉低版（Rendering 直接引用 SkiaSharp 3.119.1；Serilog.Settings.Configuration 传递 DependencyModel 范围 `>= 8.0.0` 解析取最低版 8.0.0）——非浮动升，是「传递下限消失暴露自钉低版」；全闭包 diff 降版仅三件（含 SkiaSharp.NativeAssets.Win32；原生 libSkiaSharp.dll 无 File 表版本列未触发规则），无其他隐藏降版。
- **依赖版本固化**：Rendering `SkiaSharp` / `SkiaSharp.NativeAssets.Linux` 3.119.1→3.119.2（WinHost / Server 闭包随升，与 AndroidHost / Zebra 插件工程 / WinHost.Tests 毒丸清单全仓统一 3.119.2）；WinHost 显式钉 `Microsoft.Extensions.DependencyModel` 10.0.7（闭包内唯一传递来源为 Serilog 范围引用，显式钉抬下限）；`tools/LabelFrame.PrintImageVerifier` 同步 3.119.2（不入 MSI 闭包，纯仓库一致性防漂移）。钉至 0.26 实测值而非更高最新版——与上一发版工件同版本号，0.26→新版与坏 0.27→新版两条升级路径的组件版本语义最干净；全仓直接引用均精确钉版，还原确定性可复现。
- **降版回归防线**：新增 `scripts/assert-client-deps-baseline.ps1`——对 Client 发布目录（`-PublishDir`）或 MSI File 表（`-MsiPath`，Windows Installer COM 查询）断言关键程序集「不低于上一发版基线」（SkiaSharp.dll ≥ 3.119.2.0、Microsoft.Extensions.DependencyModel.dll ≥ 10.0.726.21808、libSkiaSharp.dll 按在表断言；文件缺失同样判失败），挂接 `build-msi.ps1`（wix build 后、签名前）——CI「MSI 结构断言」必需检查与 release.yml 发版链共用该入口，降版即构建失败，无法再静默出包；基线随发版只升不降维护（`-Snapshot` 抄录，上调即发版检查项）。
- **WiX 组件 GUID 策略评估（决策 #130 ④，不改代码）**：按「相对路径 + 盐」的确定性 GUID 是 MSI 升级 / 引用计数 / Repair 的正确标准做法（改每版本新 GUID 会使旧组件成孤儿、卸载残留）；「同组件 keyfile 版本只升不降」为该模型硬约束，正解是版本单调性（固化 + 断言）而非改 GUID 策略；未来确需降版（依赖替换）须显式处理旧文件清理与重装语义，另行立项。
- **本地验证**：`dotnet build LabelFrame.slnx` 0 警告 0 错误；`dotnet test`（排除 Perf/Soak，过滤器与 CI 一致）**709 项全绿**（0 失败）；Client MSI 本地重建（版本号 0.0.1）File 表实测 SkiaSharp.dll 3.119.2.0、Microsoft.Extensions.DependencyModel.dll 10.0.726.21808 ≥ 0.26 基线，挂接断言随构建通过；**防线反向用例**：固化前工件（SkiaSharp 3.119.1.0 / DependencyModel 8.0.23.53103）在 MSI 与发布目录两模式均被断言拦下（exit 1，两项降版全列）——即 0.27.0 缺陷形态可被自动捕获。前端未改动（web/ 无变更）。
- **记账**：DESIGN 决策表 #130 + §6.11 补记「发布工件版本基线与降版断言」；CHANGELOG 本条目；修复后真实公网覆盖升级走查交回验收轮值（恢复条件 = 下次 `v*` 发版后真机重跑引导升级，含 0.26 直升与坏 0.27 修复升级两条路径）。

## v0.27.0 安装引导专项（迭代 60-64）汇总发布 · 2026-09-15

- **打包范围**：v0.26.0 之后合入 master 的全部迭代与缺陷修复——安装引导专项 4/8~8/8：迭代 60（引导程序骨架——五步问卷与拓扑预设，WiX Burn 形态决策 #122，dry-run 契约）、迭代 61（引导下载体验补全——多源回退决策 #125 与失败分类）、迭代 62（Apply 执行链启用——运行时前置链 + 非 MSI 落位决策 #124；AspNetCore 前置 Server / Client 双链腿两轮返修 #128 / #129）、迭代 63（zebra 传输插件外置化与官方插件体系决策 #123；外置插件 ALC 加载生命周期返修 #127）、迭代 64（升级路径收尾决策 #126——BA 升级清单 + 客户端「检查更新」+ 专项回看记账）。**勘误**：v0.26.0 标题所列「迭代 64」实际晚于 tag v0.26.0 合入（PR #85，2026-09-15），其内容随本版发布。详见各迭代条目。
- **安装编排（本版主题）**：五步问卷向导（欢迎含离线指引 → 拓扑预设（全五种）→ 品牌多选（ZDesigner 驱动名预选 Zebra）→ 管理界面开关 → 确认页含本机版本列）→ 安装 = 运行时前置链（.NET 10 Desktop / AspNetCore / WebView2，缺失才装、已装跳过）→ Server / Client MSI（覆盖升级，配置保留 #48）→ webui 与 zebra 插件落位（新版本覆盖 / 同版幂等 / 拒绝降级）；失败停链 + 引擎逆序回滚 + 中文失败分类报告（源不可达 / 网络中断 / 校验失败）；改选重跑 = Burn Modify 语义；干净机安装三形态（单机一体 / 服务端 / 客户端分离）已经干净 Win10 VM 验收。
- **下载体验**：按 install-manifest `urls` 数组顺序多源回退（获取失败与坏哈希均推进源游标；镜像源落地只需填数组）、缓存命中跳过下载、断网 + 缓存齐备可续装；换源 / 缓存命中 / 逐源进度在进度页标注。
- **插件体系（客户端可感知）**：zebra 传输彻底外置（#123）——WinHost 客户端核心不再内嵌 Zebra SDK（瘦身），官方插件包 `labelframe-transport-zebra`（.lfplugin，约 9.9MB）随 Release 发布并随 Client MSI 附带；存量「已用 zebra 配置」升级本次启动即自动装外置包（连接配置别名兼容读取）；未装外置包该品牌不可用（回退默认连接 + host.log 留痕，不再崩溃）；官方 `labelframe-` 前缀插件安装带版本比较，服务端 `plugin-packages` 放行官方 id。
- **升级路径（客户端可感知）**：重跑引导程序 = 升级模式（检测本机已装组件版本 → 可升级清单「组件 现版本 → 新版本」→ 复用 Apply 链完成覆盖升级；已是最新明确跳过；latest.json 清单新鲜度提示；ARP 卸载 / 静默参数走非交互路径不再卡向导）；客户端设置页「检查更新」提示闭环（发现新版提示到服务端下载或重跑引导，不做应用内自动安装）。
- **版本同步**：`/api/server/info` 版本、客户端 `GET /api/host/config` 只读 `version`（HostOptions.ProductVersion 首次随发版更新，决策 #126）与稳定版 Compose 默认版本更新为 `0.27.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档、AndroidHost APK、**Zebra 官方插件 .lfplugin（首发）**、install-manifest.json（本版起收录 runtime 三条目 + plugin-zebra 共 9 组件）+ latest.json）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.27.0` / `labelframe-client:0.27.0`（均含 `latest`）。引导程序 Bundle EXE 的 CI 发版构建未排期（迭代 60 记录「release.yml 未动」，本地经 `scripts/build-bundle.ps1` 构建），本版 Release 不含该附件——如实记录。

## 迭代 62 二次返修：前置链缺 AspNetCore 运行时在 Client 链腿复发（AC-03 预设 B「追加打印客户端」验收不通过回流） · 2026-09-15

- **缺陷与根因（决策 #129，DESIGN §6.2 / §6.3 / §6.9；AC-03 预设 B 干净 VM 取证实证，证据链 `artifacts/accept-55/ac03b/`）**：WinHost 同为 `Microsoft.NET.Sdk.Web`（隐式 FrameworkReference `Microsoft.AspNetCore.App`），但 client 预设下引导链不装 AspNetCore（`runtime-aspnetcore` 条目 topologies 不含 `client`、Bundle `DotNetAspNetCoreRuntime` `InstallCondition="InstallServer"`、`client-msi` dependsOn 缺该条目）——干净机选「追加打印客户端」安装链全绿（Apply 0x0、分离语义完美），但客户端进程启动即挂（.NET Runtime 1023：No frameworks were found）。#87 / 决策 #128 的同型缺陷在 Client 链腿复发。验收边界实证：手动补装 aspnetcore-runtime 10.0.12（sha256 与 manifest 钉定值逐字节一致）后客户端即正常（53960 监听 + 本地 UI HTTP 200）。
- **client 链腿补齐（三处同步）**：`generate-install-manifest.ps1`——`runtime-aspnetcore` 条目 topologies 补 `client`（standalone / server-win / client / offline，含客户端的预设都需要）、`client-msi` dependsOn 补齐 = runtime-desktop + runtime-aspnetcore + runtime-webview2；`Bundle.wxs`——`DotNetAspNetCoreRuntime` `InstallCondition` 扩为 `InstallServer OR InstallClient`（对齐 `DotNetDesktopRuntime` 双预设条件）。
- **Client MSI 检测修正（同 #128 Server 侧手法）**：`main.wxs` NetCoreCheck `RuntimeType` desktop → **aspnet**（WinHost = Web SDK，隐式主依赖 AspNetCore.App）+ LaunchCondition / RuntimeMissingDlg 文案同步——只装 Desktop 的机器在 Client MSI 检测阶段即被拦截，而非装完进程起不来；取舍如实记录（DESIGN 决策 #129 ②）：NetCoreCheck 单检测单 RuntimeType，WinHost 双框架需求（WinForms 的 WindowsDesktop + Web 托管的 AspNetCore）中 Desktop 腿由引导链 `runtime-desktop` 补装承载。
- **观察项①小修（确认页文案按计划集合过滤，决策 #129 ③）**：BA 确认页「确认安装计划」日志的运行时三腿（Desktop / AspNetCore / WebView2「未装（将安装）」）此前无条件统一输出，与引擎 `execute: None` 计划矛盾（验收实证 client 预设下 AspNetCore 文案偏差）——改为按当轮计划集合过滤（`ConfirmPage.DescribeRuntimeProbes`）；顺带补齐完成页 runtime-aspnetcore 展示名与探测腿映射（#87 遗留缺口：`CompletePage.DescribeOutcome` 误落 WebView2 探测腿、`DescribeComponent` 展示为裸 id——client 链装 aspnetcore 后完成页将实际出现该条目）。
- **观察项②不修（排期提示，决策 #129 ④）**：client 预设装后 `ServerUrl` 默认 `127.0.0.1:53961`（问卷无服务端地址采集项）与装后自启体验涉 #88 未决问题（客户端装后自启），另行排期复核。
- **测试（全解决方案 709 项全绿，与 #87 基线持平——拓扑矩阵改写而非新增）**：fixture `install-manifest.full.json` 同步（runtime-aspnetcore topologies 补 client + client-msi dependsOn）；`TopologyResolverTests` client 预设四用例预期集合改写（含 runtime-aspnetcore + 依赖序不变式扩展到 client-msi）；`WizardSessionTests` 端到端 client 集合断言；`ManifestParsingTests` client-msi dependsOn 断言；standalone 依赖序断言补 aspnetcore 前置于 client-msi。
- **本地验证**：`dotnet build`（0 警告 0 错误）+ 全解决方案 `dotnet test`（排除 Perf/Soak，709 项全绿）+ `pnpm lint / test`（303 项全绿）；全套产物 0.26.1 重建——manifest 生成 + 断言 9 组件全过（runtime-aspnetcore topologies 含 client、client-msi dependsOn 含 aspnetcore；AspNetCore 直链 sha256 `e5d20698…` 与验收方手动补装件逐字节一致）；真 Bundle 七包链重建 1.92 MB（仅既有两条可接受警告）；**Client MSI 定向断言全过**（LaunchCondition / RuntimeMissingDlg 标题与正文 aspnet 口径 + UI 序列 1150 < 1160 < 1297 + Server MSI 维持 aspnet 口径不回归）+ `verify-msi-ui.ps1`（CI 同构）通过；**真 Bundle「追加打印客户端」预设走查**——DryRun（问卷 → 取消 Zebra 预选 → 确认 → 取消，exit 0 零执行）：确认页计划日志 `组件 [runtime-desktop,runtime-aspnetcore,runtime-webview2,client-msi]`（返修前为三组件）；PlanProbe（确认后点安装、Plan 落日志后提权前截断，零包执行）：引擎 `Planned package: DotNetAspNetCoreRuntime, state: Present, default requested: Present, ba requested: Present, execute: None`（入 client 计划 + 已装跳过 = AC-02 语义；返修前为 requested Absent 被排除）+ `ClientMsi … execute: Install` + `ServerMsi … requested: Absent`（分离语义不回归）；server-win 预设走查：组件集合不变（含 aspnetcore）且确认页不再输出计划外的 WebView2 腿文案（观察①过滤实证）。干净机「只装 Desktop 装 Client MSI 被检测拦截」路径无法本机实证（本机双运行时在位）——以 MSI 表断言 + WiX NetCoreCheck 机制论证，交回验收 VM 复验。
- **记账**：DESIGN §6.2（runtime 条目 topologies / dependsOn 口径与示例）/ §6.3（预设映射表三行补 runtime-aspnetcore）/ §6.9（链表 InstallCondition 双预设）/ §6 决策记账行 + 决策表 #129；CHANGELOG 本条目；ROADMAP 状态行维持迭代 62 既有口径（返修不另行）。

## 迭代 62 返修：前置链缺 AspNetCore 运行时致干净机 Server 服务启动失败（AC-01 干净 VM 验收不通过回流） · 2026-09-15

- **缺陷与根因（决策 #128，DESIGN §6.2 / §6.9；AC-01 干净 VM 取证实证，证据链 `artifacts/accept-55/`）**：Server 组件为 `Microsoft.NET.Sdk.Web`（隐式 FrameworkReference `Microsoft.AspNetCore.App`），但引导前置 `runtime-desktop` 条目只装 windowsdesktop-runtime（不含 AspNetCore，两者互不包含），且 Server MSI 的 .NET 检测 `RuntimeType="desktop"`——检测放行而运行时缺框架，干净机 `LabelFrameServer` 服务无法向 SCM 报告状态（30 秒超时 → MSI 1920 → 1603 → Burn 全链回滚）。验收边界实证：手动补装 aspnetcore-runtime 10.0.12 后重跑引导全链成功、终态五项全绿。
- **前置链补齐定案（新增 `runtime-aspnetcore` 条目，厂商直链 + CI 锁哈希，机制同 #115 / runtime-desktop）**：「改用涵盖两者的安装器」否决——微软无「WindowsDesktop + AspNetCore 合一」安装器（Hosting Bundle 不含 WindowsDesktop 且面向 IIS、SDK 含开发工具体积不可接受）。`generate-install-manifest.ps1` 新增条目：钉定与 runtime-desktop 同一 .NET 补丁列车版本（单参数 `-DotNetRuntimePatchVersion` 统管双条目，原 `-DotNetDesktopRuntimeVersion` 随返修并入）、直链 `builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/<版本>/aspnetcore-runtime-<版本>-win-x64.exe`（Runtime 路径段大写）、topologies = standalone / server-win / offline；`server-msi` dependsOn 补齐 = runtime-desktop + runtime-aspnetcore；断言同步（直链白名单 / x.y.z 钉定版本 / silentArgs 非空）。
- **Bundle 链扩为七包（§6.9）**：`DotNetAspNetCoreRuntime` 置于 Desktop 与 WebView2 之间——DetectCondition = `AspNetCoreRuntimeInstalled`、InstallCondition = `InstallServer`、`Permanent="yes"`；`RuntimeProbe` 补 AspNetCore 腿（文件版本探测 `dotnet\shared\Microsoft.AspNetCore.App` ≥ 10.0.0，机制同 Desktop），BA 启动写 Burn 探测变量、确认页标注与日志同步；`build-bundle.ps1` 消费接线（manifest 缺条目即构建失败）。
- **Server MSI 检测修正**：`main-server.wxs` NetCoreCheck `RuntimeType` desktop → **aspnet**（WiX netfx 合法枚举 aspnet / desktop / core，构建实测 WIX0021 后定值）——只装 Desktop 的机器在 MSI 检测阶段即被拦截（LaunchCondition + RuntimeMissingDlg 文案同步），而非装完服务起不来 1920；客户端 MSI（WinForms，desktop）维持不动。
- **观察项裁定（AC-01 验收评论观察 ①，不修代码，决策 #128 ④）**：「品牌页未选 Zebra 时 InstallPluginZebra 仍为 true」非解析器缺陷——standalone 默认集合不含 plugin-zebra（契约测试锚定）；实证机制 = ZDesigner 驱动名预选（`SelectedBrands` 仅驱动预选与复选框勾选两条置位路径；验收自动化品牌页只点「下一步」，Burn 日志「品牌 [zebra]」= 复选框进入页面即已勾选），品牌页提示文案已声明预选语义。AC-01 重验不装品牌插件时在品牌页取消勾选即可。
- **测试（Bootstrapper 177 → 183，全解决方案 709 项全绿）**：`RuntimeProbeTests` 补 AspNetCore 独立探测 / 最低版本矩阵（3 项）；`UpgradeAssessmentTests` 补 runtime-aspnetcore 共享组件语义（更新即满足 / 落后升级 / 未装新装）；fixture `install-manifest.full.json` 收录 runtime-aspnetcore（9 组件）+ server-msi dependsOn，解析 / 拓扑解析 / 变量映射断言同步；`ChainPackageMap` 七包映射。既有用例全保留适配。
- **本地验证**：`dotnet build`（0 警告 0 错误）+ 全解决方案 `dotnet test`（排除 Perf/Soak，709 项全绿）；manifest 生成 + 断言（9 组件，runtime 三条目全过）+ 破坏性用例（版本非法 / 直链白名单外 URL 拦截）；AspNetCore 直链下载实测 sha256 与验收方手动补装件逐字节一致；真 Bundle 七包链重建 + 走查（本机 .NET 10 双运行时在位——跳过路径实证；干净机拦截路径无法本机实证，交回验收 VM 复验）。AC-01 + AC-03 转待验收（恢复条件 = 干净 VM 快照还原后重跑）。
- **记账**：DESIGN §6.2（runtime 条目特殊语义）/ §6.9（链序七包 + 检测口径）/ §6.3（品牌勾选置位路径）/ §6.7 回看表不动（历史快照）+ 决策表 #128；CHANGELOG 本条目；ROADMAP 状态行维持迭代 62 既有口径（返修不另行）。

## 迭代 63 返修：外置插件加载 ALC 生命周期缺陷修复（AC-02 真机回归不通过回流） · 2026-09-15

- **缺陷与根因（决策 #127，DESIGN §6.8「加载生命周期」；宿主级复现器 100% 实证 + 程序集→ALC 归属转储 + Resolving 处理器内引用相等取证）**：装好外置 zebra 插件后首次实际使用（连接测试）抛 `FileLoadException: Could not load 'SdkApi.Core' → InvalidOperationException: AssemblyLoadContext is unloading or was already unloaded`，此后打印 / 状态 / 测试全部被装配护栏回退 log 模拟，与内置不等价。根因：旧 `PluginDirectoryLoader`「逐 DLL 独立 collectible ALC 扫描 + 依赖惰性 Resolving 字节加载」组合下，**插件主体 ALC 对象扫描后被加载器丢弃（无任何托管强引用）**——GC 对 collectible ALC 发起卸载（状态进入 unloading）；存活插件实例使卸载无法完成（程序集 / 代码持续可用）但状态机已破坏，首用惰性解析 `LoadFromStream → VerifyIsAlive` 命中卸载态上下文即抛。静态强引用对照实验证实持根后同一惰性解析路径完全正常。
- **修复（三方向组合：③ 骨架 + ② 消窗口 + ① 断根）**：① **注册表显式强持有插件 ALC**——`PluginDirectoryLoadResult.Plugins` 元素改为 `DiscoveredPlugin`（插件 + 路径 + ALC 根），`TransportPluginRegistry` 随插件实例存根（宿主生命周期不卸载，同 id 重注册旧根释放可回收）；② **安装包子目录装配期预载全部伴生依赖**进插件 ALC（首用零惰性解析窗口；修复后 ALC 转储实证 29 程序集全部预载）；③ **按包目录单 collectible ALC 整体加载探测**（原子单元，跨程序集发现 `ITransportPlugin`，单 DLL 失败只记逐 DLL error 不阻断；无插件产出的目录显式 `Unload()` 确定性丢弃——消灭 29 个抛弃型扫描 ALC 与卸载竞态环境）。平铺手动 DLL 维持逐 DLL ALC（同居互不污染），ALC 根同样经注册表持有；`PluginProbe`（安装预检）同构收敛为单目录单 ALC + 预检完显式 `Unload`，三层校验语义（逐 DLL `LoadErrors`、`BadImageFormatException` 可行动消息）不变。
- **生命周期契约（不变项）**：字节加载不锁插件文件（#73）——卸载 = 删目录、覆盖升级 = 替换目录 + 重启生效（#68）；运行时热卸载维持不做（#68 未决不变）；collectible 保留用于注册表弃置后 GC 回收（升级重装走重启装配，无泄漏路径）。观察项结论（不修）：扫描期 WinRT `ApplicationData.get_Current` FirstChance = CsWinRT 非打包进程回退探测，内部捕获不影响结果，与本缺陷无关；包内 `System.Private.Windows.Core.dll` 的 `GetTypes` 依赖宿主框架件 `System.Formats.Nrbf`（Default 未加载时无候选）——逐程序集容错记 error 不阻断包装配。
- **测试**：新增 `PluginLoadLifecycleTests`（WinHost，生产同构官方包——真实插件 DLL + SDK 全套伴生闭包：三层校验安装 → 「重启」装配（ALC 根只经注册表持有，加载结果即刻弃置 = 生产形态）→ 多轮强制 GC + 延迟 → 首用 TestAsync 不可达端口——断言无 FileLoadException 且返回干净连接失败消息，再 GC 再用锚定重复性）；既有加载 / 安装 / 版本比较 / 迁移 / 传输行为 / 装配护栏（回退留痕）用例全保留通过。全解决方案 `dotnet test`（排除 Perf/Soak）**703 项全绿**（返修基线 652 项 + 迭代 64 汇合 50 项 + 本轮生命周期 1 项，rebase 后全量复跑）。
- **开发自验（宿主级）**：复现器（WinHost 本体口径：同 loader / 注册表 / TestAsync 入口）修复前 3/3 + 10 轮 GC + 30s 延迟均复现 `FileLoadException(VerifyIsAlive)`，修复后同口径全部通过（连接测试返回「连接测试失败：Zebra 打印机不可达（127.0.0.1:1……）」= 加载路径已过，加载失败发生在网络动作之前）。真机三项回归（连接 `~HS` / 真实状态 `GetCurrentStatus` / 出纸）交回验收轮值陪验。
- **记账**：DESIGN §6.8「加载生命周期」+ 决策表 #127；CHANGELOG 本条目；ROADMAP 状态行维持迭代 63 既有口径（返修不另行）。
## 迭代 64 安装引导专项（8/8 收尾）：升级路径——BA 升级清单 + 客户端检查更新 + 专项收尾记账 · 2026-09-14

- **升级模式（决策 #126，DESIGN §6.11 契约先行；范围 = Issue #57「范围修订 v2（缩编重报）」——原「自研升级编排」被 Burn RelatedBundle + 固定 UpgradeCode 既有语义吸收，本轮只做「看」的部分）**：① **本机已装组件版本探测** `LocalInstallProbe`（核心库 net48 / net10 双腿，全程只读）：MSI 注册表（`MsiEnumRelatedProducts` 按 MSI UpgradeCode 枚举 + `MsiGetProductInfo(VersionString)`，同族多产品取最高；UpgradeCode 常量与 packaging/*.wxs 对齐）、官方插件落位目录 manifest.json、runtime 复用 `RuntimeProbe`；② **升级评估** `UpgradeAssessor`（纯函数）：当轮 manifest 组件版本为目标 × 本机快照 → 逐组件动作（新装 / 升级 / 已是最新 / 本机更新 / 不比较）+ 整体「已是最新」判定（只看已装组件，AC-02）；③ **呈现**：欢迎页升级摘要横幅（可升级清单「组件 现版本 → 新版本」/ 已是最新 / 全新安装）+ 清单新鲜度提示（latest.json 消费：稳定通道 URL / 本地同目录推导，清单版本落后通道最新即提示，读取失败静默）+ 确认页新增「本机版本」列与升级 / 已最新横幅（Burn 日志同步留痕供走查断言）；④ **版本比较统一语义** `VersionSemantics`（双方可解析 System.Version（容忍 v 前缀、缺失段视为 0）按其比较，否则 Ordinal）——#123 插件比较委托同一实现（§6.8 / §6.11 单一语义）。
- **升级执行零新机制 + 走查暴露缺陷修复**：升级 = 确认后复用现成 Apply 链（Burn 固定 UpgradeCode 的 RelatedBundle 覆盖升级 + MSI MajorUpgrade 既有语义，BA 无新执行代码路径）。走查实测暴露真实缺陷并修复：**升级链尾段 Burn 以 Uninstall 动作运行旧 Bundle EXE，旧 BA 若一律弹向导将无人应答、升级链卡死**——BA 增加非交互启动路径（`Create` 事件取启动命令：动作非 Install 或显示级别 None / Passive / Embedded → 跳过向导，自动 Detect → Plan → Apply → Quit；Apply 需有效属主窗口句柄，以隐藏窗口承载（out-of-proc BA 传 IntPtr.Zero 实测抛 ArgumentException））——ARP 卸载 / 静默参数同样受益。
- **升级走查脚本（`scripts/test-bundle-upgrade-walkthrough.ps1`，本地脚本化验证不进 CI）**：per-user 测试载体（真实双 MsiPackage 内嵌链 + 真实 BA；测试 MSI 用生产 Server / Client UpgradeCode 使探测按生产口径真实命中，**不带 Upgrade 表**——0.90.1 → 0.90.2 替换由 Burn RelatedBundle 升级驱动（旧 Bundle 卸载其自有 MSI），绝不触碰本机真实安装；生产 MSI 的 MajorUpgrade 为 MSI 既有语义不在 per-user 载体重复）；**双场景三通道断言（Burn 日志 / MSI 注册表族内版本 / 退出码）18 项全绿**：S1 装旧（全新安装 0.90.1）；S2 升新（确认页升级横幅 + BA 可升级清单「服务端 / 打印客户端 0.90.1 → 0.90.2」+ `Detected related bundle` version 0.90.1 + 旧 Bundle 非交互卸载 + 族内最高 0.90.2）；S3 已最新（「已是最新版本，无需重复安装」横幅 + 双包 `state: Present` 跳过 + 版本不变）。实测踩坑入账：MSI 版本属性名为 `VersionString`（"5.0.5VersionString" 返回 1608）；MsiEnumRelatedProducts 的 UpgradeCode 必须带大括号（缺省返回 87 参数无效）；Burn 装的 MSI 带 ARPSYSTEMCOMPONENT=1 不进 ARP（残留清理须走 MSI API 族枚举）。
- **客户端「检查更新」提示闭环（决策 #71 / #72 分发之上，不做应用内自动下载安装）**：WinHost `GET /api/host/config` 响应新增只读 `version`（`HostOptions.ProductVersion` 常量，与 ServerOptions 同口径随发版更新；POST 忽略）；前端设置页「更新与安装包」卡片按发版命名事实解析客户端 MSI 版本（`LabelFrame-Client-<版本>.msi`）取最高与本机比较——**有新版** → 「发现新版本 X.Y.Z（本机 A.B.C）：到服务端下载新版安装包（下方列表），或由 IT 重跑安装引导程序完成升级」；**无新版** → 「已是最新（本机 X.Y.Z）」；本机版本未知（旧客户端）或列表无可解析版本号 → 不比较不显示（无误导）。比较逻辑入 `web/src/lib/update.ts`（`VersionSemantics` 的前端等价实现，数值段比较、段缺失视为 0）。
- **专项收尾记账**：DESIGN §6.7 新增专项回看表（8 迭代关键决策串讲 + **Burn Pivot 完整脉络**：#114 自研向导 → 迭代 60 AC-04 体积实测 51.7MB 超标 → #122 改 WiX Burn 及其对 #54 / #55 / #57 的连锁「范围修订 v2」缩编）；ROADMAP 状态总览补专项行；逐 Issue（#50~#57）AC 状态核对表回写 #57（滞后项确认 `待验收` 标签与恢复条件在位，只读登记不动标签归属）。
- **测试（Bootstrapper 126 → 177，全解决方案 702 项全绿；前端 303 项全绿）**：新增 `UpgradeAssessmentTests`（组件级口径矩阵：未装 / 升级 / 同版（含缺失段） / 本机更新 / 未装不影响已最新判定 / 插件 / runtime 两条 / webui 不比较 + VersionSemantics 理论展开与 #123 委托回归）、`LocalInstallProbeTests`（族内最高 / 按码匹配 / 版本不可得跳过 / 插件 manifest 四态 / runtime 透传）、`LatestPointerTests`（解析负例 / 未知字段兼容 / 来源推导规则 / 新鲜度与呈现三态）、`WizardSessionUpgradeTests`（会话级接线：探测 + 评估 + 同目录 latest.json + 探测异常兜底）；WinHost 端点集成测试补 version 透出与不可写断言；前端 `update.test.ts`（解析 / 比较 / 取最高 / 结论三态）+ Settings 页四用例（有新版 / 已最新 / 版本未知 / 无可解析包）。既有问卷只读契约（#53 AC-03）、多源回退（#54）、落位幂等（#55）契约测试全保留通过。
- **记账**：DESIGN §6.11（升级模式与版本比较）+ §6.5 / §6.9 / §7 相应注记 + 决策表 #126 + §6.7 专项回看；CHANGELOG 本条目；ROADMAP 状态总览补专项行（8 迭代一行式汇总 + Issue 链接）。

## 迭代 61 安装引导专项（5/8）：引导下载体验补全——多源回退与下载行为测试覆盖 · 2026-09-14

- **多源回退（决策 #125，DESIGN §6.10 契约先行；范围 = Issue #54「范围修订 v2（缩编重报）」——原自研下载器范围被 Burn 引擎吸收，本轮补回退消费与体验）**：BA 按 install manifest `urls` 数组顺序（#115：顺序即优先级）逐源回退——WiX v3 `ResolveSource` 在 v7（本仓工具链）的后继 = **`CacheAcquireBegin` / `CacheAcquireComplete` 事件内调用 `IEngine.SetDownloadSource` + 失败时事件返回 `Retry`**（引擎 `AcquireContainerOrPayload` 获取 / 重试循环；`CacheAcquireResolvingEventArgs.DownloadUrl` 在 v7 只读，`SetDownloadSource` 才是覆写入口——引擎源码考古定案）。决策核心入核心库 `CacheSourceFallback`（`src/LabelFrame.Bootstrapper/Downloads/`，net48 / net10 双腿）：每链包失败计数（获取失败与**坏哈希校验失败各计一次**）逐源推进，第 N 次获取提供 `urls[min(N, count-1)]`；**运行时清单是源顺序权威**（每轮 Apply 从当轮清单重建、重试按钮归零从头重试）；源耗尽停止干预并提示「已尝试全部 N 个源」；重试驱动有界（获取失败 Retry / 坏哈希引擎 2 次额度 + BA 追加 RetryAcquisition / 包级兜底 Retry——每次失败推进源游标，无源不空转）；**仅远程载荷参与**（`PayloadContainerId` 空 = 非内嵌容器，伴生 DLL 解压获取不干扰计数）；本地 / 缓存来源不覆写（保住断网续装）。单源清单（首期生产形态）零 schema 变更退化为「失败即耗尽」，**镜像源落地只需填 urls 数组**（#117 镜像位预留兑现为消费链路）。
- **下载侧进度 / 失败事件映射补全（#55 已交付安装链进度，本轮补下载侧细化）**：`CacheAcquireProgress` 的 `Progress/Total` 映射**单包下载百分比**（进度页分组件行 + 阶段行「下载中 X%（源 i/N）」）；**换源提示**行（「主源获取失败，已切换备用下载源（第 i/N 个）：host」）；**缓存命中标注**（获取前哈希跳过校验通过且本包未发生获取 → 「已缓存（跳过下载）」）；**失败分类** `DownloadFailureClassifier`（纯函数入核心库）——HRESULT → 源不可达（404 / 文件不存在 / 域名未解析 / 无法连接）/ 网络中断（超时 / 连接中止 / 重置）/ 校验失败（0x80091007 坏哈希）/ 未分类，差异化中文提示（换源已自动发生 / 检查网络与代理 / 镜像源部署指引 / 勿绕过校验），失败报告与 Burn 日志双通道呈现（分类结论写日志供回溯）。
- **Burn 下载行为测试矩阵（`scripts/test-bundle-download-matrix.ps1`，本地脚本化验证不进 CI）**：per-user 测试 Bundle（双 ExePackage id=ServerMsi/ClientMsi——PayloadTool 无参空操作作载体，规避 MSI 与提权，复刻 #55 方法）+ **真实 BA 七页向导 Win32 UI 自动化**（本地清单注入 → 单机一体 → 确认安装）；双 TcpListener 测试源按 URL 路由内嵌行为（`/ok/` `/404/` `/hang/` `/corrupt/` `/truncate/`，访问日志落盘）；多源 url 序由运行时清单驱动（正是被测消费路径）。**七场景三通道断言（Burn 日志 / 退出码 / 访问日志）本地全绿**：S1 正常下载；S2 主源 404 → 换源镜像成功；S3 主源无响应挂起 → 换源成功；S4a 坏哈希 → 换源重取干净副本成功；S4b 坏哈希单源 → 引擎重取额度内同源重试仍坏 → 失败（fail-closed 不装不明文件，exit 0x80091007）；S5 半量截断中断 → 镜像完整重传成功；S6 缓存命中 + 断网续装（run1 挂起下载中强制中断（**实测口径：缓存阶段失败会被引擎回滚清缓存，进程中断才保留 InProgress 注册与包缓存**）→ run2 双源停机：已缓存包零网络请求 + 未缓存包逐源拒绝 → 换源 2/2 → 源耗尽 + 分类提示）。**Burn 载体下「续传」语义 = 失败后重试 / 换源重新获取**（引擎不做 HTTP Range 断点续传）——与原自研下载器 AC 口径的差异如实记录于 DESIGN §6.10。
- **测试（Bootstrapper 89 → 126 项）**：新增 `CacheSourceFallbackTests`（11 项：首源 / 失败推进 / 校验失败推进 / 耗尽 / 单源退化 / 未知包 / 计数隔离 / Reset / 六包映射 / 三源钳制 / UI 元数据）与 `DownloadFailureClassifierTests`（26 项理论展开：HRESULT 矩阵 + 差异化文案 + 三类互异）；问卷只读契约（#53 AC-03）与 #55 既有契约测试全保留通过。
- **工程注记（矩阵实测踩坑，入 DESIGN §6.10）**：wix build 会在输出目录留包载荷硬链接中间产物——不清除则 Burn 以 Bundle 同目录为本地源直接 copy 跳过下载；WinForms 控件为注册类名（`WindowsForms10.*`），UI 自动化需 `EnumChildWindows` 递归 + 类名子串匹配。
- **记账**：DESIGN §6.10（下载体验与多源回退）+ 决策表 #125 + §6.9 / §6.4 / §7 相应注记；CHANGELOG 本条目；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR）。

## 迭代 62 安装引导专项（6/8）：Apply 启用——运行时前置链与非 MSI 落位 · 2026-09-14

- **Apply 启用（决策 #124，DESIGN §6.9 契约先行；范围 = Issue #55「范围修订 v2」）**：引导 BA 从 dry-run 进入真装——问卷阶段保持 #53 只读契约（零下载 / 零安装 / 零系统改动，测试锚定），**执行边界 = 确认页「安装」**：BA 写入问卷变量 + 运行时探测变量 + 落位目标变量后 `Engine.Plan(Install)` + `Engine.Apply(向导句柄)`；#53「绝不 Apply」口径修订为「确认前绝不 Apply」（确认页横幅文案同步为「确认前只读」）。
- **Bundle 六包链（§6.9 链序，单一回滚边界）**：`DotNetDesktopRuntime` → `WebView2Runtime`（厂商直链、`Permanent="yes"`）→ `ServerMsi` → `ClientMsi`（既有）→ `WebUiPlacement` → `ZebraPluginPlacement`（非 MSI 落位包）；链内 `DetectCondition` / `InstallCondition` 消费 Burn 变量。
- **运行时前置检测口径（AC-02：缺失才装、已装跳过）**：.NET 10 Desktop Runtime = **文件版本探测**（枚举 `dotnet\shared\Microsoft.WindowsDesktop.App` 版本目录 ≥ 10.0.0，latestMajor 前滚对齐 MSI NetCoreCheck；注册表 sharedfx 键实证「已装而无键」不可靠）；WebView2 = EdgeUpdate Clients `pv` 注册表（HKLM+HKCU 对齐 MSI 先例）。探测入核心库 `RuntimeProbe`（委托注入可单测），BA 于 `engine.Detect()` 前写变量，链内 `DetectCondition` 消费——本地实测（真 Bundle）：本机双运行时均 `evaluates to true → state: Present`（跳过）。**修复实测缺陷**：BA 为 32 位进程（AnyCPU/Prefer32Bit），`SOFTWARE\WOW6432Node` 路径被注册表重定向二次映射读空——默认读取器改 `RegistryView.Registry64` 固定 64 位视图。
- **非 MSI 落位机制定案（ExePackage 包装解压工具；Burn 自定义动作否决——无包获取语义且引擎进程内加载受限）**：新工程 `src/LabelFrame.Bootstrapper.PayloadTool`（net48 控制台，随 bundle 压缩内嵌 + 伴生 DLL 以 PayloadGroup 挂载）；被落位 zip / `.lfplugin` 作为子 Payload 远程下载（Burn 下载校验缓存）；落位逻辑入核心库 `PayloadPlacer`——webui 覆盖解压、插件按 #123 版本比较（新版本覆盖 / 同版本幂等跳过 / 降级拒绝，退出码 20）。落位目标 = Burn 变量（`WebUiTargetDir` / `PluginZebraTargetDir`）注入 `InstallArguments`；落位包 `Permanent="yes"`（卸载编排不在本轮，#57）。
- **进度 / 失败 / 完成 UI**：进度页映射 `CacheAcquireProgress` / `ExecuteProgress` 总进度与 `Cache/Execute Package Begin/Complete` 分包状态（多源下载体验完整消费属 #54 修订范围）；失败报告 = 失败步骤（阶段 + 包 + 真实引擎状态码）+ Burn 日志位置（`WixBundleLog`）+ 建议动作（重试（重新 Plan+Apply）/ 查看日志 / 管理员重跑）；完成页 = 结果摘要（组件 + 版本 + 跳过项 + `LabelFrameServer` 服务状态尽力查询）+ 下一步指引（打开客户端 / 打开管理界面 `http://127.0.0.1:53961/` / **PDA 到下载中心扫码**——衔接 #52）+ 重启提示。执行期间锁定「上一步 / 取消」并拦截 Alt+F4 关闭。
- **失败处置与重跑幂等口径入 DESIGN（§6.9，引用 Burn 引擎内建回滚语义，不自造）**：包失败 → 停链 + 逆序回滚本次已执行包；重跑 = MSI 已装跳过 / 覆盖升级（appsettings.json 不覆盖 = #48）、runtime DetectCondition 跳过、webui 覆盖重写、插件 #123 版本比较、改选 = Burn Modify 语义（条件假即卸载）。
- **manifest runtime 条目落地（#115 消费）**：`generate-install-manifest.ps1` 随发版生成 `runtime-desktop`（钉定补丁版本，默认 10.0.12 可覆写；直链 = `builds.dotnet.microsoft.com/dotnet/WindowsDesktop/<版本>/...` 官方稳定模式）/ `runtime-webview2`（version=`evergreen`；go.microsoft.com 固定直链）条目——CI 下载（或 `-RuntimeFilesDir` 预置）实测 sha256 / sizeBytes；MSI 条目 `dependsOn` 补齐（server→runtime-desktop；client→runtime-desktop+runtime-webview2）；断言新增 runtime 专项（厂商直链白名单 / 版本规则 / silentArgs 非空，破坏性用例本地验证拦截）。`build-bundle.ps1` 按 manifest 获取 runtime 安装器（本地缓存 + 哈希校验，不符即构建失败 fail-closed）后交 wix build 内嵌摘要。
- **测试（Bootstrapper 42 → 89 项，全解决方案 614 项全绿）**：新增 `RuntimeProbeTests`（探测矩阵 11 项）、`PayloadPlacerTests`（落位 / 版本比较 / 拒绝降级 / 异常，10 项）、落位目标目录与链包映射契约（2 项）、问卷只读契约重锚定（执行边界文案 + 会话层无执行 API 反射断言）；既有 42 项全保留适配。
- **本地验证（VM 取证 = AC-01/AC-03 待验收欠账，本机无管理员权限）**：`dotnet build`（0 警告 0 错误）+ 全解决方案 `dotnet test`（614 项全绿）；manifest 生成 + 断言（8 组件）+ 破坏性用例（版本非法 / 直链白名单外 URL 均拦截）；**真 Bundle**（1.48 MB）构建 + 只读走查（问卷 → 确认 → 取消，engine exit=0，#53 零改动行为不回归）+ Burn 日志复核（双运行时 `DetectCondition` 真 → Present 跳过）；**per-user 测试 Bundle**（临时产物不进仓，复刻链机制规避提权）——成功链：问卷 → 确认 → Apply → 完成页（组件摘要 / 服务状态 / 下一步指引）+ 落位目录实测产出；失败链（注入 `exit 1` 包）：失败报告（组件 + 真实状态码 0x80070001 + 日志路径 + 建议动作）+ Burn 日志复核引擎回滚序列（exit code -2147024895 透传）。Bundle 构建含两条可接受警告（落位包无 DetectCondition——幂等执行设计内；双落位包共用工具 EXE 的 attached container 重名——仅影响 `wix burn extract` 调试场景）。
- **记账**：DESIGN §6.9（安装执行链与失败处置）+ §6.2 runtime 条目哈希锁定方式 + §6.3 问卷只读契约与变量契约扩展 + 决策表 #124；CHANGELOG 本条目；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR）。

## 迭代 63 安装引导专项（7/8）：品牌传输插件外置化与官方插件体系 · 2026-09-14

- **zebra 传输外置化（决议 1「彻底外置」，决策 #123，DESIGN §6.8 契约先行）**：`ZebraTransportPlugin` / `ZebraPrinterTransport` 从 WinHost 迁入新插件工程 `src/LabelFrame.TransportPlugin.Zebra`（net10.0-windows10.0.26100，Zebra SDK 5.0.3685 与毒丸引用清单随工程迁移），构建为官方 `.lfplugin` 包（`scripts/build-zebra-plugin.ps1`，版本随主版本演进）随 Release 发布；**WinHost 客户端核心不再引用 Zebra.Printer.SDK**（瘦身，SDK 依赖全部随插件包分发）。未装外置包时该品牌不可用（插件列表无此传输；连接引用时回退默认连接 + host.log 中文留痕——`TransportManager` 启动装配护栏，不再因缺插件崩溃）。
- **官方插件 id 与品牌映射（决议 2）**：官方前缀 `labelframe-`，zebra 插件 id = `labelframe-transport-zebra`（manifest.pluginId / DLL 插件 Id / 连接配置 pluginId 三处一致；Core 新增 `TransportPluginIdPolicy` 统一常量）；旧内置时代 id `zebra`（≤0.26）为读取别名（connection.json 读取时内存态映射，不落盘迁移）；brand → manifest 组件 id（plugin-zebra）→ 插件包 pluginId 三段映射表入 DESIGN §6.8，引导侧 `BrandPluginMap` 同构（后续品牌零契约扩展）。
- **存量升级兼容（AC-04，随升级包附带渠道）**：客户端 MSI 附带 `plugin-packages\labelframe-transport-zebra-<版本>.lfplugin`（main.wxs 条件编译块 + build-msi.ps1 自动检测产物传入；本地无产物可跳过不影响裸构建）；启动时 `ZebraPluginMigration` 检测「已用 zebra 配置」（connection.json 官方 id / 旧别名 / 旧 Mode=Zebra，或 appsettings / 环境变量 Transport=Zebra）且插件未装 → 自动从附带包安装（复用 PluginInstaller 三层校验），**本次启动即完成装配**（先于外部插件目录扫描）；无附带包 / 附带包损坏均只留痕引导安装，不阻断启动。
- **覆盖安装版本比较（决议 2，官方插件率先）**：`labelframe-` 前缀插件安装时与已装版本比较——新版本覆盖旧版本、**同版本幂等跳过**（不重复解压）、**降级拒绝**（提示先卸载再装，中文可行动消息）；第三方插件维持「覆盖安装不做版本比较」（决策 #72 4A）。比较语义：双方可解析 `System.Version` 按其比较（缺失段视为 0，1.0 == 1.0.0），否则字符串 Ordinal（Core 新增 `PluginVersionComparer`）。
- **服务端放行策略（AC-05）**：`plugin-packages` 上传校验新增内置传输保留 id（log / tcp9100 / winspool）拒绝——与客户端安装侧「注册表内置即拒绝」互为纵深；官方 id（`labelframe-` 前缀）放行，官方插件可经服务端集中分发、客户端「插件管理」安装。
- **manifest 收录与引导接线（AC-03）**：`scripts/generate-install-manifest.ps1` 收录 `plugin-zebra` 条目（type=lfplugin、version=主版本、topologies=standalone/client/offline、sha256 CI 实测）——本地实测：样例产物目录生成 + 断言通过（6 组件、逐条目哈希复核）；引导侧 `InstallPluginZebra` Burn 变量接线随清单条目生效（`BundleVariableMap` 既有映射，无需改代码），品牌页文案更新为外置语义、确认页 lfplugin 安装位置显示真实插件目录（`plugins\labelframe-transport-zebra`）；Bundle 链内 `.lfplugin` 的下载与安装归 #54 / #55。
- **发布流水线（对齐 #51 混合立项先例，仅改 release.yml）**：package job 新增「构建 Zebra 官方插件包」步骤（先于 Client MSI——MSI 附带该包），release-assets 产物与 GitHub Release 附件均含 `labelframe-transport-zebra-*.lfplugin`；三项必需检查（ci.yml）名称与语义不动。
- **测试（新增 48 个用例（理论展开计），全解决方案 576 项全绿）**：官方 id 策略 / 版本比较（Core，17 项矩阵）；官方包安装（生产同构包——真实插件 DLL + SDK 全套伴生闭包）通过内置冲突校验且重启装配成功（AC-02 装配面）、新版本覆盖 / 同版本幂等 / 降级拒绝 / 第三方同版本仍覆盖（WinHost，5 项）；存量升级迁移 9 场景（旧别名 / 官方 id / 旧 Mode / 环境变量 / 无配置 / 已装幂等 / 无附带包引导 / 附带包损坏不阻断 / 多版本选最新）；服务端放行 4 项（官方 id 放行 + 三内置 id 拒绝）；引导 fixture 升级（current 含 plugin-zebra 六组件 + 新增 pre-plugin 存量形态清单，既有断言同步）。既有 Zebra 传输行为测试（#49 AC-08 口径：连接失败 / 状态离线 / 测试失败消息含目标）随迁移全数保留通过。
- **本地验证**：`dotnet build LabelFrame.slnx -c Release`（0 警告 0 错误）+ `dotnet test`（排除 Perf/Soak，576 项全绿）；插件包实测 **9.88 MB**（29 DLL + manifest.json，含 Zebra SDK 全套伴生依赖，上限 64MB）；MSI 冒烟构建（含附带包与不含附带包两种路径）+ `verify-msi-ui.ps1` UI 契约断言通过 + MSI File 表确认 `plugin-packages\labelframe-transport-zebra-0.27.0.lfplugin` 在内；manifest 生成 + 断言 6 组件全过。
- **记账**：DESIGN §6.8（官方插件体系）+ 决策表 #123 + §6.2 / §6.3 品牌映射与变量契约更新；DEPLOY §6 与 README 传输行同步事实修正；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR）。

## 迭代 60 安装引导专项（4/8）：引导程序骨架——问卷与拓扑预设→组件集合（dry-run） · 2026-09-14

- **形态返工（决策 #122，Issue #53 决议评论用户拍板）**：首版「自研 WinForms self-contained 向导」实测 51.7MB 超 #114 ≤ 20MB 量级（WinForms/WPF self-contained 框架体积下限、裁剪被 SDK 阻断 NETSDK1175），用户权衡「买引擎」后改用 **WiX Burn Bundle + 托管 BA**——AC-04 口径随形态替换为 **Burn Bundle EXE 实测体积（MB 级）**；DESIGN §6.1 契约修订先行（强化路径），决策表 #122 记账；**迭代 61（#54）/ 62（#55）/ 64（#57）大半自研内容可被 Burn 吸收，各 Issue 拾取时重审范围**。
- **BA 技术形态（调研定案入 #122）**：WiX v5.4+（仓库现役 v7.0.0）BA 为 **out-of-proc 独立 EXE**（v3 工厂模型已废弃）——`src/LabelFrame.Bootstrapper.Ba`（net48 WinForms 五步问卷）引用 `WixToolset.BootstrapperApplicationApi` 7.0.0（支持 net462+），入口 `ManagedBootstrapperApplication.Run` 与引擎握手；**目标框架 net48**：.NET Framework 4.8 是 Win10 1809+ / Win11 的 OS 组件，裸机免装运行时（规避「首个 EXE 依赖 runtime」死循环），BA 载荷仅数 MB；.NET 10 SCD 复现 51.7MB、FDD 裸机不可用，均不取。
- **职责重划与复用**：拓扑编排仍自研——核心逻辑库 `src/LabelFrame.Bootstrapper` 重构为类库多目标 `net48;net10.0-windows`（BA 复用 + 测试 / #54 / #55 消费；`TopologyOptions.SelectedBrands` 形状微调 `IReadOnlySet`→`ISet`，net48 无前者、语义不变，§6.3 契约同步）；Manifest / Topology / Printing（ZDesigner → zebra 预选，决议 2）与既有测试全量复用；下载 / 校验 / 链装 / 回滚 / 升级引擎职责改由 Burn 承担。
- **五步问卷 BA**（欢迎含离线全量包指引（决议 1 方案 A）与清单来源本地路径 / URL → 拓扑预设（全五）→ 品牌多选（选项仅为 manifest 已有 plugin 条目）→ 管理界面开关 → 确认页）：确认页展示组件名称 / 版本 / 体积 / 来源 URL / 目标安装位置（对齐 DEPLOY），server-docker 展示 compose 指引。
- **dry-run 契约（AC-03）**：核心会话全程只读（清单获取 = 本地读或单次 GET，无下载 / 写入 / 系统改动代码路径）；BA 侧 dry-run = 写入 Burn 变量（契约 §6.3：`InstallPreset` / `InstallServer` / `InstallClient` / `InstallWebUi` / `InstallPluginZebra`，映射纯函数 `BundleVariableMap` 入核心库可测）后 `Engine.Plan(LaunchAction, BundleScope.Default)` **只计划不执行，绝不 `Engine.Apply`**；确认页「生成安装计划（仅预览）」展示引擎计划结果（每包 RequestState），UI 明示「仅预览：尚未下载、尚未安装」。
- **Bundle 与构建**：`packaging/bootstrapper/Bundle.wxs`（链 = Server / Client MSI，`Compressed="no"` + `DownloadUrl` web bundle——产物不进 EXE；`InstallCondition` 消费问卷变量；UpgradeCode 固定 GUID）；`scripts/build-bundle.ps1` 本地 / 发版构建（wix build 需真实 MSI 提取包元数据，故 Bundle 不进 slnx——按 #53 fallback「仅 PR 构建验证」口径如实记录，release.yml 未动）；核心库 + BA 工程入 `LabelFrame.slnx` 随 CI 构建验证。
- **测试（AC-01 / AC-02 / AC-03，48 项 = 既有 42 全保留 + 新增 6）**：样例 manifest fixture 两份（严格按 DESIGN §6.2 schema——`current` 对齐迭代 58 产物现状、`full` 含 runtime + dependsOn + plugin-zebra 模拟 #55 / #56 后形态）；全矩阵（五预设 × 开关 × 品牌，含依赖闭包顺序不变式、server-docker 空集合 + 双指引、webui 对 client 的 topologies 过滤）；dry-run 断言（探针目录快照一致 + ProgramData 不创建、本地清单零网络请求、URL 清单单次 GET 且组件 URL 不触碰、明示文案断言）；清单校验负例矩阵 + 品牌预选矩阵 + **Burn 变量映射矩阵（新增 6 项：全开 / 默认 / client / server-win / server-docker / 当前清单无插件条目）**。
- **AC-04 实测（新口径达标）**：`LabelFrame-Bootstrapper-0.26.0.exe` = **1.46 MB**（web bundle：EXE 只含 Burn 引擎 + 压缩 BA 载荷，MSI 按需经 DownloadUrl 下载——对比原 WinForms self-contained 方案 51.7MB）；本地冒烟：bundle 启动 → 引擎 Detect 双 MSI（Absent，含与已装 0.25.0 的 MajorUpgrade 关联识别）→ BA 进程（net48）承载问卷窗口，全程无 Apply、无安装动作。
- **记账**：DESIGN §6.1 形态修订 + §6.3 实现要点与变量契约 + 决策表 #122；CHANGELOG 本条目（Burn 形态）；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR，对齐先例）。
- **返修（2026-09-14 验收回流：向导装配缺陷致 AC-06 走查受阻）**：根因为确定性逻辑缺陷（非「异常被吞的宿主交互」）——`WizardForm.Navigate` 采用**增量**语义（`target = _currentIndex + delta`），构造期 `Navigate(0)` 意图「进入首页」实算 `-1 + 0 = -1` 被越界守卫静默 early-return：首页从未装配、索引停留 -1，窗体带空白内容区进入消息循环，点「下一步」即 `_pages[-1]` 越界异常（与验收实测症状逐项吻合，独立最小复现自证）。修复：导航状态机收口为 UI 无关的 **`WizardNavigator<TPage>`**（核心库新类型：绝对索引 + 惰性装配 + 越界拒绝 + 未装配访问显式 `InvalidOperationException` + 工厂异常传播不吞），`WizardForm` 改绝对 `NavigateTo` 并加装配自检（首页未就位即抛，禁止带 -1 进消息循环），BA `Run()` 装配失败显式失败（诊断对话框 + Burn 日志 + 非零退出 1603）+ UI 线程 `ThreadException` 兜底（不吞、写日志、对话框后退出）；新增导航状态机单测 11 项（构造未装配 → 装配首页 → 惰性前进 / 后退复用 / 越界防御 / 同页重进 / 未装配防御 / 工厂异常可重试 / null 工厂），全解决方案 587 项全绿。本地完整走查（Bundle 本地重建 1.46MB，UI 自动化 + 截图 7 张 + Burn 日志）：**复刻验收失败动作（空清单点「下一步」）得中文校验提示而非崩溃** → 本地清单加载自动进步 → 拓扑 / 品牌（ZDesigner 预选 Zebra 生效）/ 管理界面 → 「上一步」往返 → 确认页（五组件清单装载）→ 引擎 Plan 预览 → 正常退出（exit 0，日志全程无 Apply）。

## v0.26.0 迭代 49-59、64、65 汇总发布 · 2026-09-14

- **打包范围**：v0.25.0 之后合入 master 的全部迭代与缺陷修复——迭代 49（PDA 宿主自动化构建与品牌化）、迭代 50（错误响应分类修正）、迭代 51（客户端设备日志链路与前端可观测）、迭代 52（日志基础设施加固）、迭代 53（PDA 可观测性）、迭代 54（区域水平锚定修正）、迭代 55（Zebra 状态映射修正）、迭代 56（双端复用 Zebra 官方 SDK 5.0.3685）、迭代 57（安装引导专项设计契约 + 客户端退出提速）、迭代 58（发布流水线安装清单）、迭代 59（PDA 签名稳定化 + 服务端下载中心）、迭代 65（无字段模板打印测试修复），缺陷 #46（同 IP 双设备号解析最近活跃优先）与 #58（托盘「退出」不生效）。详见各迭代条目。
- **客户端可感知变更**：托盘「退出」与 Web UI 退出即时生效（统一退出路径 + 事件驱动提前退出，卡死路径约 0.5 秒，托盘图标不再残留）；无字段模板（静态标签）可在「数据与打印」页打印测试；自动宽度文本区域水平锚定按实测宽度对齐；Zebra 状态映射按官方字段表修正；WinHost 升级 Zebra 官方 SDK 5.0.3685。
- **服务端可感知变更**：同 IP 双设备号按 IP 解析改最近活跃优先（缺陷 #46，targetIp 投递不再路由到已停用旧设备号）；新增下载中心页（客户端与 PDA 安装包同页分区 + 条目二维码扫码下载，`pda-packages` 目录与 API）；日志按日轮转与业务事件日志。
- **PDA（AndroidHost）**：**APK 首次随 Release 分发**（专用 keystore Release 签名、版本号由 tag 注入、签名 Secrets 缺失即构建失败——换签名设备需卸载重装一次，见 DEPLOY §7）；接入 Zebra 官方 SDK（tcp 默认 / 蓝牙 SPP / USB OTG）；可观测性（HostLog 封装 + 崩溃捕获 + 本地滚动日志）。
- **发布流水线**：Release 附件新增 `install-manifest.json`（当版全部产物 + sha256 + 多源 URL，CI 实测生成与断言，迭代 58 决策 #115 / #120）与 `latest.json` 最新版本指针（稳定通道 `releases/latest/download/latest.json`）——均首次发布。
- **版本同步**：`/api/server/info` 版本与稳定版 Compose 默认版本更新为 `0.26.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档、AndroidHost APK（首发）、install-manifest.json + latest.json（首发））+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.26.0` / `labelframe-client:0.26.0`（均含 `latest`）。

## 迭代 58 安装引导专项（2/8）：CI 自动生成安装清单（install-manifest.json + latest.json） · 2026-09-14

- **release workflow 新增安装清单生成与断言（Issue #51 范围 1~3，契约 = 决策 #115，实现决议 = 决策 #120；流程治理 + 产品混合立项，对齐迭代 49 / #31 先例——只改 release.yml，不动三项必需检查）**：release job 在创建 GitHub Release 前调用新增脚本 `scripts/generate-install-manifest.ps1`，收集当版全部产物（Server MSI、Client MSI、管理界面插件 zip、Linux 归档、PDA APK），实测 sha256（小写 hex）与 sizeBytes，生成 `install-manifest.json`（`schemaVersion=1`；组件条目 id / type / version / dependsOn / urls / sha256 / sizeBytes / silentArgs / topologies / notes 与 DESIGN §6.2 schema 一致；urls 为**多源 URL 数组**，首期仅 GitHub Release 主源一个元素，镜像位按数组形态预留）；runtime / `plugin-*` 条目按 schema 预留、无产物不出现，dependsOn 首版留空（只引用同集合内条目，runtime 随专项 5/8 #55 落地时补）。manifest 由 CI 生成、禁止人工维护（人工改动会被断言的哈希复核拦截）。
- **断言粒度（用户 2026-09-14 决议）：schema 字段完整性 + 产物存在性 + 哈希抽验（AC-03）**：生成阶段任一组件缺产物立即失败（空哈希 / 空条目不可能落盘）；独立断言步骤（`-VerifyOnly`）**重读落盘清单**复核——顶层与条目必填字段齐全性、type / topologies 枚举、sha256 小写 64 位 hex、sizeBytes 正整数、dependsOn 引用存在性、每条 Release 源 URL 对应产物在产物目录存在、逐条目重算 SHA-256 比对（覆盖且强于抽样，5 个产物成本可忽略）；缺产物 / 缺哈希 / schema 不符任一命中即非零退出、job 失败、不创建 Release。
- **latest.json 最新版本指针同步生成（用户 2026-09-14 决议，Issue #51 待决议 1）**：内容 = 最新版本号 + 当版 manifest URL，随 Release 附件发布；专项 8/8（#57 升级检查）直接消费，稳定通道 URL = `releases/latest/download/latest.json`。
- **脚本工程约束**：兼容 Windows PowerShell 5.1（本地自验）与 PowerShell 7（GitHub Actions runner）；JSON 手写序列化 + UTF-8 无 BOM 落盘（跨版本输出一致，引导程序解析不假设 BOM）。本地自验 5 用例全数符合预期——样例产物目录正常生成 + 断言通过（sha256 与 sha256sum 独立交叉复核一致，Python 独立解析 JSON 通过）、缺产物失败（exit 1）、篡改 sha256 哈希抽验拦截、删除 sha256 字段（缺哈希）拦截、断言阶段产物缺失拦截；release.yml 改动过 YAML 语法校验。
- **记账**：DESIGN §6.2 标注 manifest 从契约变实现（生成点 = release workflow 脚本调用）与 latest.json 已实现（含稳定通道 URL），决策表补 #120（实现决议：生成点 / latest.json / 断言粒度）；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR，对齐 #50 / #52 先例）。
- **AC-01 / AC-02 待发版验证**：Release 附件含清单与下载复核 sha256 依赖真实发版 tag，转待验收口径（恢复条件 = 下一次 `v*` tag）。

## 迭代 57 客户端退出提速（事件驱动提前退出替代固定宽限） · 2026-09-11

- **退出看门狗改事件驱动：宿主停止信号 + 500 毫秒稳定窗提前退出（Issue #64，用户已确认方案）**：缺陷 #58 修复引入的固定 2.5 秒优雅宽限在卡死场景（托盘 / 界面消息循环在场、`RunAsync` 不自然返回）每次烧满——用户托盘退出体感 2~3 秒。`HostExitCoordinator` 序列改为：`StopApplication` 后**订阅 `ApplicationStopped`**，宿主已停止而主流程仍未返回（卡死判定）时，仅再留**稳定窗 500 毫秒**（`DefaultStabilizationWindow` 常量，不打配置面）即执行清理 + `Environment.Exit(0)` 强退——卡死路径端到端 ≈ 宿主停止延迟 + 500 毫秒（目标 <1 秒，现状 2.7 秒）；订阅采用「先挂回调再查已置位」双检，覆盖「订阅时宿主已停止」竞态。
- **优雅返回场景不强杀不回归（AC-03）**：`RunAsync` 自然返回的路径（Linux 无头端等）走既有 `finally` 自然收尾——稳定窗内主流程自然退出则看门狗收手（无强退、无重复清理）；宽限上限 2.5 秒**保留为兜底**（`ApplicationStopped` 未在宽限期内到来、宿主停止本身挂死时按现状超时强退），只是不再决定常见路径耗时。
- **host.log 记账补阶段行（AC-02）**：新增「宿主已停止（ApplicationStopped）而主流程未返回，进入稳定窗观察」「稳定窗内主流程自然退出，看门狗收手」「稳定窗届满判定卡死——稳定窗提前退出」三类阶段行，「点了退出没反应」可定位到稳定窗前后。
- **测试（AC-01 / AC-03）**：单测 7 → 10 项——新增 3 项（宿主停止后主流程未返回：稳定窗内清理 + 强退的**时序断言**——下界 ≥ 观察期结构性成立（Task.Delay 不提前）+ 「稳定窗提前退出」/「优雅等待超时」记账行互斥判定路径；稳定窗内自然退出看门狗收手不强杀；**订阅时已停止竞态**即时命中信号走稳定窗而非干等宽限），改写 1 项（原「宽限期超时强退」拆出「宿主停止信号永不到来」的兜底场景——`FakeLifetime` 加停止信号抑制开关）；既有自然退出 / 幂等 / 未绑定 / StopApplication 异常 / 清理幂等与异常用例全数保留。CI 满载（2 vCPU + 测试类并行 + 覆盖率收集）实测线程池调度延迟可达 9 秒级，时序上界断言因此剔除（环境噪声非被测行为），本地连续多轮实测退出点稳定在 500~600 毫秒。
- **沙箱进程级验证（AC-02，无头实例 HTTP 触发）**：真实客户端在运行并持有机器级单例互斥锁，Windows 托盘 TFM 沙箱不可起（会向真实客户端发激活信号）；以无头 net10.0 实例（独立端口 + 临时目录）实测 HTTP 触发退出：**进程退出 238 / 247 毫秒**（退出码 0，`{shuttingDown:true}` 契约不变），host.log 完整走「稳定窗观察 → 稳定窗内主流程自然退出收手」序列。托盘卡死场景的进程级计时以此环境不可行，由 AC-01 单测时序 + #58 真机日志推演自证，真机体感（AC-05，图标 <1 秒消失）留待用户复核。

## 迭代 59 安装引导专项（3/8）：PDA 签名稳定化 + 服务端下载中心（扫码下载） · 2026-09-11

- **`pda-packages` 目录 + API（决策 #119，与 client-packages 模式对称，三项待决议用户确认）**：服务端数据目录新增 `pda-packages`（`LABELFRAME_SERVER_PDA_PACKAGES` 可覆盖；Docker compose 默认挂载 `./pda-packages`）+ `GET/POST /api/pda-packages`、`GET/DELETE /api/pda-packages/{file}` 列表 / 上传 / 下载 / 删除——路径穿越防护共享 `FilePackageService`；**上传仅接受 `.apk`**（目录直放不限制扩展名，照常列出）；**APK 下载响应 MIME 固定 `application/vnd.android.package-archive`**（Android 浏览器识别为安装包直接拉起安装）；错误码增量 `LF_SRV_010`（不存在 404）。API 属服务端本体，无头服务端直放文件即可分发，管理界面（可选插件）只是操作入口。
- **统一「下载中心」页（Server UI，原「客户端下载」页升级）**：客户端安装包与 PDA 安装包同页分区展示（各自上传按钮、空态提示目录直放方式），条目按修改时间**倒序（最新在上）**（页面内排序先行，`latest.json` 落地后再对齐「推荐 / 最新」标记）；**每条目旁展示二维码**（`qrcode-generator` 渲染，内容 = 管理员浏览器正在访问的局域网地址 + 该条目下载路径）——PDA 与服务器同网扫码即得下载 URL；PDA 区常驻「未知来源 / 安装未知应用」授权步骤与同签名覆盖升级提示文案。**client-packages 既有行为不动**：客户端设置页「更新与安装包」卡片与既有端点零变更，仅并入下载中心展示。
- **release.yml 签名稳定化（流程治理授权范围；三项必需检查名称与语义不变）**：移除 debug 签名回退——Release 构建前逐项校验四个签名 Secrets（`ANDROID_KEYSTORE_BASE64` / `ANDROID_KEYSTORE_PASSWORD` / `ANDROID_KEY_ALIAS` / `ANDROID_KEY_PASSWORD`），任一缺失 `::error::` + exit 1 构建失败，**绝不静默降级**（回退路径 = 两次构建可能签名不一致：用户无法覆盖升级且换签名重置 ANDROID_ID 致设备号漂移，对决策 #104③ 过渡路径收口）；构建步骤签名参数由条件拼接改为恒定传入。日常 CI 第三必需检查「Android 构建（PDA 宿主）」（ci.yml）不动——仍 debug 签名验证可构建，不对外分发。
- **证书管理与换签名影响文档化（README / DEPLOY §7 / AndroidHost README）**：keystore 托管位置（GitHub Secrets + 生成方离线备份，不得入库）、备份要求（丢失 = 无法再发同签名升级包）、换签名影响（卸载重装 + ANDROID_ID 变 → 设备号变，对齐决策 #104 口径）与 PDA 扫码装机路径（DEPLOY §6 新增小节）。
- **测试**：服务端新增 12 项——服务级 7 项（`PdaPackagesServiceTests`：保存 / 列表 / URL、.apk 上传限制（含大小写）、路径穿越拒绝、目录直放非 apk 照常列出、下载路径解析、删除、同名覆盖）+ HTTP 集成 5 项（`PdaPackagesEndpointsTests`：上传后下载 **Content-Type = application/vnd.android.package-archive**、非 apk 上传 400 + 中文消息、缺失 404 + `LF_SRV_010`、目录直放列出、删除后二次 404）；前端新增 `DownloadCenter.test.tsx` 9 项（双分区列表 / 时间排序最新在上 / 二维码 title = origin + 下载路径 / Android 授权文案 / 双空态 / 加载失败 / 双分区上传与刷新 / PDA 删除确认与取消 / 客户端删除），`App.server.test.tsx` 随页更名与入口断言更新。
- **真机验收滞后（AC-02 / AC-03）**：PDA 真机扫码下载与同签名覆盖升级（设备号不变）转 `待验收`（恢复条件：UROVO DT50 等 PDA 与同网服务端可得）；AC-01（Secrets 缺失构建失败）以 workflow 文件审阅 + 逻辑推演自证，真实触发依赖发版 tag 属验收范畴。

## 迭代 57 安装引导专项（1/8）——设计契约：安装清单格式、拓扑预设与信任模型 · 2026-09-11

- **纯文档契约迭代（不写产品代码）**：`docs/DESIGN.md` 新增「安装引导（Bootstrapper）」章节（新 §6，原「风险与未决问题」顺延为 §7，PERF-BASELINE 两处引用同步），决策表新增 #114~#118——专项 2/8（#51 CI manifest）起的实现以此契约为准（AGENTS 强化路径：跨迭代契约先入 DESIGN 再改代码）。
- **引导程序形态与技术选型（决策 #114，用户已确认）**：自研 .NET 轻量向导（WinForms 分步问卷 + self-contained win-x64 单文件，体积量级 ≤ 20MB）；WiX Burn 不采用（迭代 10 #44 已放弃的 Bal 复杂度，且拓扑编排 / 中文问卷无论如何自研）；**Velopack 评估不用于引导程序**（模型错位：单应用安装器 + 应用内增量自更新框架，无多组件拓扑编排层；核心卖点增量自更新为第一期明确不做项；换打包契约与既有 WiX/MSI 体系冲突大）——记为未来「客户端应用内自更新」立项时的首选候选。
- **安装清单 install manifest 格式（决策 #115，AC-01）**：版本化 JSON schema（`schemaVersion` 首版 1，不兼容变更递增、引导程序超范围 fail-closed）；组件条目含 id / type（`msi`·`lfplugin`·`webui-zip`·`apk`·`runtime`·`archive`）/ version / dependsOn / **urls 多源数组（顺序即优先级、逐源回退）** / **sha256 强制（CI 实测、逐源强制校验）** / sizeBytes / silentArgs / topologies 拓扑标记；**由 CI 发版流水线从当次真实产物计算生成、禁止人工维护**（字段完整性断言）；runtime 条目厂商官方直链 + CI 锁哈希（厂商轮转 = fail-closed 接受）；Docker 镜像不进首版条目（registry digest 兜底）；`latest.json` 版本指针预留。附完整示例 JSON（脚本校验语法有效）。
- **拓扑预设（决策 #116，AC-02）**：单机一体 / 服务端（Windows 服务 · Docker · Linux systemd）/ 追加打印客户端 / **离线全量包（一等形态）**——「预设 → 组件集合」映射表 + **仅两项自由开关**（品牌插件多选、是否带管理界面），不提供任意组件勾选（防组合爆炸）；「预设 + 开关 → 组件集合」解析器契约（`ITopologyResolver`）为 #53 / #54 / #55 公共契约。
- **信任模型与分发源（决策 #117，AC-03 显式结论）**：manifest sha256 强制校验 = 公网完整性背书基线（镜像位零信任成本、哈希即背书）；**不推翻决策 #79**（局域网信任、插件不签名——公网引导分发的 `.lfplugin` 受 manifest 哈希背书，强于局域网三层校验）；插件 / manifest 签名升级触发条件显式衔接 #79（公网陌生第三方插件生态出现时先加签名）；manifest 同信道残余风险（信任根 = GitHub 账号 + TLS + tag 不可变）显式记录；已装服务端可作局域网优先下载源（复用 #71 / #72 分发通道）；离线全量包为一等形态。
- **更新策略与代码签名（决策 #118，AC-04）**：第一期 = 检查新版本（latest.json）+ 重跑引导程序升级（MSI 覆盖升级既有语义：appsettings 不覆盖 #48 等）；明确不做无人值守静默更新 / 服务端进程内自更新 / Velopack 增量更新。签名**首期不购证书**——MSI / APK / 引导 EXE 签名现状盘点入表（MSI 自签过渡 #65 / APK 专用自签 keystore #104 ③ / 引导 EXE 无签名风险最高），缓解 = manifest sha256 强制校验 + 发布页哈希与绕过说明；证书采购另行决策（开放点登记）。
- **未决问题同步**：DESIGN §7 新增「安装引导专项开放点」组（镜像源落地 / 证书采购 / 客户端自更新候选 Velopack / manifest 签名触发条件 / Docker digest 收录 / **i18n 不进 setup 问卷**——REQUIREMENTS §7 边界不变）；既有「插件包签名 / 服务端鉴权」条目补引 #117 / #118。
- **专项依赖清单（AC-05 前半）**：8 迭代依赖图与 Issue 链接表（#50~#57 全部已立项）入 DESIGN §6.7 并回链 Issue #50 评论；ROADMAP 状态行按流程留待结项（AC-05 阻断点 = 结项）。
- **本地检查**：纯文档改动；示例 JSON 语法脚本校验通过（含 sha256 64 位 hex 断言）；决策号 #114~#118 无冲突；`dotnet build LabelFrame.slnx` + `dotnet test`（排除 Perf/Soak）通过。

## 缺陷 #46 同 IP 双设备号按 IP 解析命中旧行修复（最近活跃优先） · 2026-09-11

- **排查确认（缺陷成立，本地复现不依赖真机）**：`devices.last_ip` 无唯一约束 / 无索引，设备号变更后新设备号从同 IP 注册**插入新行**，旧行 `last_ip` 无人清除（全仓仅 `ServerDb` 三处写 devices——注册 upsert / notify 心跳 / 领取事务，均单行更新，**无跨行清理路径**）；`FindDeviceByIpAsync` 原为 `WHERE last_ip = $ip COLLATE NOCASE LIMIT 1` **无 ORDER BY**——SQLite 无索引全表扫描按 rowid 序，稳定命中先注册的旧行。修复前实测：同 IP「旧行 stale / 新行活跃」场景，`FindDeviceByIpAsync`、`SubmitJobAsync` targetIp 解析、`GET /api/devices/by-ip/{ip}` 三路全部返回旧设备号（新增 4 项复现测试在未修复代码上全部失败，与用户现象一致）。
- **修复 = 按 IP 解析改「最近活跃优先」（决策 #113，Issue 候选方向 A）**：`FindDeviceByIpAsync` 加 `ORDER BY last_seen_at DESC, registered_at DESC, id`（后两项为确定性平手序）——设备号变更后必命中活跃新行；两个消费方（by-ip 端点与 targetIp 投递解析）共用该方法一并修复，按 IP 投递不再路由到已停用的旧设备号（长期 Pending 收不到）。**不取方向 B（写入时清除同 IP 其他行）**：NAT 共网出口下多台真机合法共用一个出口 IP，B 会让每次心跳抹掉其他设备的 last_ip（目录 IP 显示丢失、by-ip 退化为「最后写者」）且写放大；同 IP 多设备都活跃时 A 跟随最近一次心跳，尽力而为且不破坏目录数据。行为变化仅在「同 IP 多行」场景（先注册行 → 最近活跃行），单行场景零变化；无需数据迁移。
- **测试**：新增 4 项——服务级 3 项（FakeTimeProvider 时间确定：同 IP 旧行 stale / 新行活跃 → by-ip 与 targetIp 均命中新行；同 IP 双活跃设备解析跟随最近心跳）+ 端点集成 1 项（`RemoteIp` 请求头模拟同来源 IP 双注册，`/api/devices/by-ip/{ip}` 命中新行且在线）；`TempServer` 支持注入时间源。顺带修正 `ServerDb` 一处错位的文档注释（`CreateJobAsync` 的 summary 误置于 `FindDeviceByIpAsync` 上）。
## 缺陷 #62 无字段模板打印测试修复（数据与打印页静态标签可打印） · 2026-09-11

- **无字段模板（静态标签）可在「数据与打印」页打印测试（AC-01 / AC-02）**：模板无可填充字段（`contract.fields` 空且版式无字段填充元素，即 `fieldKeys` 为空）时，测试数据面板此前整体只渲染一条「不允许」语义提示（操作区不渲染），且 `testPrint()` 内还有同条件拦截——静态标签（固定文本 / 条码 / 二维码 / 线条 / 图片）完全无法打印测试。修复（纯前端，`web/src/pages/DataPrint.tsx`）：无字段时操作区照常渲染（调试模式复选框 / 「打印测试（单张）」/「出图预览」），提示改为说明性语义——静态标签按版式原样打印、无需填写数据；提交等价空数据 `labels: [{ data: {} }]`（模板包遗留 `testData` 键不外带）；移除 `testPrint()` 的无字段拦截守卫（`!pkg` 空守卫保留）。后端受理（空 `data` 兜底）/ 校验（空 `fields` 直接通过）/ 渲染（literal 全容错）三层本就支持，本轮零后端改动、公共契约零变更。
- **「下载 Excel 模板」维持禁用（AC-01）**：无字段无列可生成，按钮禁用与 tooltip 均不变。
- **测试（AC-02 / AC-03 / AC-04 / AC-05）**：DataPrint 双模式用例更新 + 新增——client：旧「无字段模板」断言随文案同步更新（含 testData 残留键不渲染输入框的守门），新增静态标签场景（操作区渲染可点与说明提示、打印测试提交 `labels: [{ data: {} }]` 且进度面板正常、调试开出图 / 调试关出图预览均空数据不建作业、Excel 模板仍禁用且 tooltip 不变）；server：无字段模板提交单张空数据到所选在线设备（双 base 守门）、出图预览空数据 render-image；有字段模板既有用例全数保留（回归不变）。
- **界面验收滞后（AC-06）**：真实界面打印测试走查（沙箱或真机）留待验收阶段补证，Issue #62 转 `待验收`。

## 缺陷 #58 托盘菜单「退出」不生效修复（统一退出路径 + 幽灵图标清理） · 2026-09-11

- **退出路径统一为「优雅停止 + 限时兜底强退」（决策 #112，AC-01 / AC-02 / AC-04）**：托盘菜单「退出」与 `/api/host/shutdown`（Web UI 设置页「退出程序」）此前一弱一强——托盘回调只调 `StopApplication()`，而 `RunAsync` 在托盘 / 界面消息循环在场时不自然返回（Issue #58 证据链：本机 2/2 复现进程僵死 >2 分钟，主线程停在 Main 的阻塞等待，`finally` 中的 `Environment.Exit` 永不执行）。修复 = 新增 `HostExitCoordinator` 统一入口：`StopApplication` → 有限等待（默认 2.5 秒）主流程自然退出 → 超时执行清理后 `Environment.Exit(0)` 强退兜底；两条入口共用同一序列（HTTP 路径保留 200ms 响应送达缓冲），不再有强弱退差异。
- **强退前显式托盘清理，消除幽灵图标（AC-03）**：托盘退出清理抽为 `TrayQuitSignaler`——向托盘消息循环线程投递 `WM_QUIT` 并限时 `Join`（默认 2 秒），消息循环退出后在托盘线程内执行 `Shell_NotifyIcon(NIM_DELETE)`（此前两条退出路径都到不了 `using` Dispose——托盘线程收不到 WM_QUIT，图标残留，本机已见幽灵 tooltip 累积）；自然退出路径与强退兜底共用同一清理（`uiShell?.Dispose()` 一并纳入），HTTP 退出路径同享收益。
- **退出链路阶段记账（AC-02）**：host.log 可定位「点了退出没反应」的具体阶段——收到退出请求（来源：托盘菜单 / HTTP）、StopApplication 发起（含异常续走兜底）、优雅等待超时与否（含超时秒数）、清理执行 / 完成 / 异常、最终退出方式（自然完成 / 强制 Environment.Exit(0)）。
- **测试（AC-05）**：新增 13 项——`HostExitCoordinator`（自然退出看门狗收手且不重复清理 / 超时强退 + 清理全序列记账 / 幂等重复请求 / 未绑定生命周期仍兜底 / StopApplication 异常仍强退 / 清理幂等 / 清理异常不阻断）+ `TrayQuitSignaler`（WM_QUIT 投递到登记线程 / 未登记不投递 / 幂等 / Dispose 默认路径 / Join 限时）+ `TrayIconService` 真实消息循环退出回收（Start → Dispose → 循环线程限时结束）。
- **真机验收滞后（AC-01 / AC-03）**：全局单实例互斥锁与端口 53960 归用户真机验收（Issue 复现步骤即恢复条件），本轮自动化验证到单测层；Issue #58 转 `待验收`——托盘「退出」≤5 秒进程退出、退出后图标即失不残留。

## 迭代 56 双端复用 Zebra 官方 SDK 5.0.3685（AndroidHost 接入 + WinHost 升级统一） · 2026-09-11

- **AndroidHost 接入官方 SDK（决策 #111，AC-01）**：打印传输从自研裸 socket `Tcp9100PrintTransport` 切换到 Zebra 官方 Link-OS SDK `5.0.3685`（net10.0-android36.0 纯托管 DLL，无 .so / 无 jar）——新 `ZebraSdkTransport` 实现 `IPrintTransport` / `IPrinterStatusProvider` / `ITestableTransport` 三接口（对齐 Core 传输契约，公共契约零变更）。连接类型扩展：`tcp`（**默认且一级交付路径**，SDK `TcpConnection`）/ `bluetooth`（SPP `BluetoothConnection`，MAC 地址手输——预授权决议 1）/ `usb`（OTG `UsbDiscoverer` 自动发现锁定第一台 Zebra 设备，无可选设备给可行动中文错误——预授权决议 2）；每次发送 / 测试 / 状态独立建连，与裸 socket 时代行为一致。**存量 `tcp_host` / `tcp_port` 配置键沿用，已装设备升级后零操作直入 SDK TCP 路径（AC-03 无感迁移）**。
- **PDA 状态 / 连接测试切 SDK `PrinterStatus` 语义（决策 #111 ②，预授权决议 3——不保留 `~HS` 双轨）**：`ZebraPrinterFactory.GetInstance(connection).GetCurrentStatus()` 官方布尔属性（`isPaperOut` / `isPaused`）；缺纸 / 暂停口径与迭代 55（#109）实证结论对齐，Message 最小口径只报缺纸与暂停，不回退其 AC。
- **WinHost 升级 3.0.3355 → 5.0.3685（决策 #111 ③，AC-08）**：`GetInstance` / `GetCurrentStatus` / `UsbDiscoverer.GetZebraUsbPrinters` 在 5.x 均保留，`ZebraPrinterTransport` 适配面极小；顺带把连接测试失败消息补上打印目标（对齐 #108 不泛化）。四模式（log / tcp9100 / winspool / zebra）行为回归由既有测试套件锚定；Core 的 `Tcp9100PrintTransport` 保留为跨平台兜底（Linux 客户端现状不变）。
- **毒丸引用清单入库（决策 #111 ④⑤，AC-01 / AC-02）**：官方支持矩阵「非 MAUI .NET 10 → Android ✗」的实测成因是依赖链毒化，全部 `ExcludeAssets="all"` 逐包排除（ExcludeAssets 不裁剪传递闭包）。**Android 实证版**：`System.Drawing.Common`（net10.0 lib 夹带 `System.Private.Windows.Core.dll`，Android AOT 失败）+ `SkiaSharp.Views.Maui.Controls` / `SkiaSharp.Views` / `SkiaSharp.Views.Maui.Core` + `Microsoft.Maui.*` 全家（含 `.Controls.Build.Tasks` 注入 MAUI AOT profile）；保留 `SkiaSharp` + `SkiaSharp.NativeAssets.Android`。**Windows 本轮验证版**：MAUI 链同 Android 清单**另加** `SkiaSharp.Views.WinUI` / `SkiaSharp.NativeAssets.WinUI` / `Microsoft.Maui.Graphics.Win2D.WinUI.Desktop` / `.Resizetizer` / `Microsoft.Graphics.Win2D` / `Microsoft.WindowsAppSDK` / `Microsoft.Windows.SDK.BuildTools`（后两者 buildTransitive 注入 win10-* RID 图触发 NETSDK1083；不排除则 MAUI 程序集混入编译——WinForms `Label` 二义、与 WebView2 投影类型冲突）；`System.Drawing.Common` Windows 可用不排除。
- **APK 产物收敛（决策 #111 ⑦，AC-02）**：`RuntimeIdentifiers` 收敛为单 `android-arm64`（现役 PDA 均为 arm64）——多 ABI 下 `libSkiaSharp.so` 与 SDK 依赖按 ABI 复制使 APK 达 53MB 超量级，单 arm64 后 **21.7MB < 现状 24.8MB**（对齐预研 19MB 量级）；构建级断言通过——APK 内无 `System.Private.Windows.Core.dll` / MAUI 产物（grep 0 命中）、`libSkiaSharp.so` ELF 全部 `PT_LOAD` 段 p_align=16384（16KB 对齐）。
- **SkiaSharp 原生 pdb 发布清理（Windows，决策 #111 ⑥）**：`SkiaSharp.NativeAssets.Win32` 的 runtimes 资产附带 81MB `libSkiaSharp.pdb`（非编译产物，`-p:DebugType=None` 不影响包内文件），WinHost csproj 加 Publish 后 Delete 目标，发布产物回到基线量级（win-x64 framework-dependent 55MB / 52 文件，MAUI 0 混入）。
- **配置页与权限（决策 #95 结构不变）**：打印机子页连接方式三选一（网线 / 蓝牙 / USB 数据线），参数随类型切换，配置存储 `{ brand, connectionType, ... }` 增量 `bluetooth_mac` 键；本地 HTTP `GET/POST /api/host/config` 增 `bluetoothMac` / 只读 `printerAddress` 字段（向后兼容增量）；AndroidManifest 蓝牙权限（API 31+ `BLUETOOTH_CONNECT` 运行时请求——选蓝牙保存时触发，legacy `BLUETOOTH` / `BLUETOOTH_ADMIN` 限 maxSdk 30）；USB 无需静态权限，首次连接由 SDK 触发系统授权弹窗。
- **许可证分发依据（决策 #111 ⑧）**：Development Tool License §3.3.3 允许以目标码随 Licensee Application 集成分发、禁止单独分发 SDK 本体——SDK 包本体不进任何 GitHub Release 附件。
- **顺带修复**：master 上 CHANGELOG.md 存在迭代 54 squash 合并引入的 Git 冲突残块（`=======` / `>>>>>>>` 重复块，c686342），本轮清理。
- **真机验收滞后（AC-04～07 及 AC-03 / AC-08 真机部分）**：SDK 三连接类型物理出纸与状态语义取证转 `待验收`（Issue #49，恢复条件：蓝牙 / USB 打印机与 192.168.2.121:9100 可得）；本地交付模拟级验证全绿。
- **测试**：新增 WinHost Zebra 传输 2 项（连接测试失败消息含目标 / 状态查询不可达时离线含原因，锚定 5.x 升级行为口径）；既有 Zebra 传输 3 项与传输配置 / 状态矩阵随升级回归通过。

## 迭代 54 区域水平锚定修正（自动宽度文本对齐语义） · 2026-09-11

- **区域水平锚定按实测文本宽度计算（决策 #110，AC-01，三项待决议按 Issue #44 评论用户确认——方案 A / 度量经字宽度量接口注入 / 兼容护栏全部生效）**：文本无显式 `WidthMm` 且锚定区域时，`LabelLayoutResolver.ResolveBounds` 的水平锚定偏移由「（区域宽 − 块宽）× 因子」（块宽被扩为区域全宽 → 偏移恒 ≈ 0，迭代 32 测试发现的缺陷）修正为按**实测文本宽度**计算——锚定宽度 = 实测单行宽度 + 2×有效水平内边距（夹取到区域宽），区域内 Start / Center / End 三种对齐可区分且符合直觉；**块宽仍为区域全宽（减单值内边距），块内 TextAlign / 边框 / 裁剪 / 缩小适应语义不变**（渲染器块内排版逻辑零改动）。三端一致：Web 预览与 ZPL 图片打印（Skia 渲染器）、PDA（Android 渲染器）同一解析结果驱动。
- **字宽度量接口 `ITextWidthMeasurer`（Core.Layout，毫米口径、与渲染 DPI 无关）**：解析器保持纯几何层、不直接依赖 Skia；默认实现 `SkiaTextWidthMeasurer`（字型查找与渲染器同源——抽取 `SkiaTypefaceLookup` 共享（含中文回退），Embolden 与加粗渲染一致），AndroidHost 提供 `AndroidTextWidthMeasurer`（Android.Graphics Paint 同一文本管线）。设计意图：解析几何（含锚定偏移）与渲染方式解耦，未来命令直发打印插件可提供打印机字体度量的接口实现复用同一解析几何（供后续插件模块重构迭代承接）。
- **兼容护栏全部生效（用户拍板接受渲染漂移）**：含 `RegionHAlign` 缺省 Center 的已发布模板——无显式宽度的已锚定文本位置随锚定语义修正而变化（此前水平锚定实际不生效，文本恒贴近块内对齐位置）；垂直锚定（按字高）与显式宽度路径零变化；未注入度量器的纯几何调用方回退既有行为（API 向后兼容）。
- **DESIGN 记账**：决策表新增 #110；「风险与未决问题」迭代 32 遗留的区域锚定条目清账移除。
- **测试**：新增 Core 解析器用例 8 项（自动宽度水平锚定实测宽度计算 / 锚定宽度含水平内边距 / 超宽夹取 / 3×3 水平垂直组合矩阵 / 无度量器回退 / 显式宽度公式不变 / 未锚定绝对坐标 / 非文本元素自然尺寸锚定）+ Skia 渲染端改写 1 项、新增 2 项（自动宽度三种水平锚定墨迹重心递增与文本不越区域、显式宽度块内 TextAlign 防回归）。
- **并行汇合**：与迭代 55（#45）双 worktree 并行开发，代码无重叠；本迭代后合并——DESIGN 决策号让位重排（#109 归迭代 55，本迭代记 #110），CHANGELOG 按合并时间序重排。

## 迭代 55 Zebra 状态映射修正（~HS 官方字段表 + SDK 细状态） · 2026-09-11

- **TCP 9100 `~HS` 解析按官方字段表修正（决策 #109）**：按 Zebra 官方 ZPL 指南 ~HS 字段表修正解析——响应为三行逗号分隔字段（每行 `<STX>…<ETX><CR><LF>`），**缺纸 = 首行第 2 字段、暂停 = 首行第 3 字段**；修正迭代 6 的盲写映射（原「第 2 字段=暂停、第 5 字段=缺纸」两处皆错：真缺纸会被误读为暂停、接收缓冲格式数会被误读为缺纸）；解析剥离 STX / ETX / CR 控制字符并提取为纯函数（`Tcp9100PrintTransport.ParseHostStatus`），PDA 与 WinHost 共用 Core 的 Tcp9100 解析，一处修复两端生效。
- **「无响应 / 超时 / 响应不完整」一等异常态（决策 #109）**：官方声明 MEDIA OUT / RIBBON OUT / HEAD OPEN / 回卷器满 / 打印头过热等状态下打印机可能完全不响应 `~HS`——状态查询超时、空响应、首行字段不足（截断）、标志位非 0/1 一律返回异常态（`IsOnline=false` + 中文原因，含目标地址与超时秒数），**绝不映射为「正常 / 在线」**（原实现超时与空响应均返回 `IsOnline=true`，缺纸场景可能被误报为正常）。
- **WinHost Zebra SDK 路径细状态（决策 #109）**：`GetStatusCore` 改用官方 `ZebraPrinterFactory.GetInstance(connection).GetCurrentStatus()`（`isPaperOut` / `isPaused` 等官方布尔属性，字段映射由 SDK 维护），消除「细状态恒 false + 待真实设备联调」欠账；连接 / 状态读取失败返回 `IsOnline=false` + 含目标与原因的中文消息（对齐决策 #108 不泛化）。**依赖调查结论**：既有 `Zebra.Printer.SDK` 3.0.3355 已具备 `GetCurrentStatus` 与全套 `PrinterStatus` 布尔属性（经 NuGet 包 XML 文档核对 + windows TFM 编译验证，与 5.0.3685 同构），**无需升级**——原代码注释「3.x 无公开状态字段」判断有误；winspool 驱动路径维持「默认在线」（不在范围）。Message 最小口径只报缺纸 / 暂停，`PrinterStatusInfo` 契约不动。
- **测试**：模拟报文单测矩阵 15 项替换原按旧映射构造的用例——解析级（官方三行报文四象限标志位 / 无控制字符单行 / 截断 / 非 0/1 标志 / 空响应两态 / null 入参）+ 端到端 socket（正常 / 缺纸 / 暂停 / 零字节关闭 / 静默超时）。
- **真机取证不在本轮执行（安全边界）**：三场景（正常 / 人工缺纸 / 人工暂停，打印机 192.168.2.121:9100）留待验收阶段用户配合取证（`~HS` 响应原文留存），Issue #45 转 `待验收`；AC-01~04 恢复条件见 Issue 自评表。

## 迭代 52 日志基础设施加固（轮转 / 启动防护 / 业务事件） · 2026-09-11

- **FileLoggerProvider 启动防护（决策 #108，AC-01）**：`LABELFRAME_SERVER_LOG_FILE` 指向无效路径（目录不存在且不可创建 / 磁盘不可用）不再启动即崩——构造不抛异常、跳过文件通道、控制台输出中文告警（含配置项名、失败原因与降级事实），宿主正常启动；运行中防护：每日轮转开新文件失败继续写旧文件（下次写入再试），实际写入失败自我禁用通道并告警一次，`Log` 永不抛出（不打断请求路径）。
- **轮转与保留统一口径「按日 + 默认保留 31 个」（AC-02，三项待决议按 Issue #35 评论用户确认）**：服务端 `server.log` 与客户端 `host.log` 改**按日轮转**——实际文件 `<名>-<yyyyMMdd>.log`（`LABELFRAME_SERVER_LOG_FILE` / `LABELFRAME_HOST_LOG` 仍是基准路径；历史单名文件不迁移不删除），保留上限默认 31、`LABELFRAME_SERVER_LOG_FILE_RETENTION_DAYS` / `LABELFRAME_HOST_LOG_RETENTION_DAYS` 可调（0 或负值 = 不清理；超期清理在启动与每日轮转时执行）；客户端 Serilog `app-*.log` 显式接线 `retainedFileCountLimit`（`LABELFRAME_APP_LOG_RETENTION_DAYS`，≤0 = 不清理；Serilog 包默认本为 31，此前未显式声明未开放配置）。客户端 MSI 卸载「清理用户数据」同步覆盖 `host-*.log` 轮转文件。
- **服务端业务事件日志（AC-03）**：作业创建（requestId → jobId / 目标设备 / 标签数）、设备认领、回报终态三条 **INFO**（`[LoggerMessage]` 源生成、作业粒度——批量作业不逐张一行、幂等重放不重复记录），正向生命周期可在 server.log 追溯（对照客户端双 ID 行）；默认 INFO，经标准 `Logging:LogLevel` 降级（`Logging__LogLevel__LabelFrame.Server.ServerService=Warning`，沿用决策 #102「不新造轮子」）。
- **杂项吞错留痕（AC-04 / AC-05）**：connection.json 读取 / 解析失败回退默认连接时写 host.log（含原因与回退目标——此前静默回退，用户打印机配置「消失」无痕迹）；TCP 连接测试失败原因透传——`TestAsync` 返回具体原因（连接被拒（SocketError）/ 3 秒超时 / 已连上但 ~HS 无响应等，含目标地址），连接测试结果消息不再泛化失败（迭代 50 的 `LF_TRANSPORT_TEST_FAILED` 测试页链路不变）；无界面壳（`LABELFRAME_OPEN_BROWSER=0` 且 `LABELFRAME_TRAY=0`）启动失败提示同时写控制台 / stderr（此前只入 host.log）。
- **范围外登记**：跨组件关联 ID（TraceId / Activity）按 Issue「不在范围」登记 DESIGN「风险与未决问题」（P2-9），不实现。
- **测试**：新增 15 项——Server 7（FileLoggerProvider：无效路径不抛异常 / 按日文件写入 / 启动清理超期 / 非日期文件不动 / 停用后写入不抛异常；集成：无效日志路径宿主正常启动 /healthz、全流程三条业务行落 server-<yyyyMMdd>.log（含幂等不重复））+ WinHost 8（DailyRotatingFileWriter：按日文件 / 清理超期 / 非日期文件不动 / 停用后写入不抛异常；connection.json 损坏与独占锁定回退留痕 2 项；TCP 测试失败原因具体化 2 项）。`dotnet build` / `dotnet test`（排除 Perf/Soak，421 项）通过；轮转跨天与长期清理行为属长测，建议随部署观察。

## 迭代 50 错误响应分类修正 · 2026-09-11

- **请求体反序列化失败分类（决策 #107）**：共享层 `AddLabelFrameExceptionHandler` 固定开启 `RouteHandlerOptions.ThrowOnBadRequest`（框架默认仅 Development 开启，Production 下参数绑定失败被短路为「400 空 body」，无法给出统一 ErrorView）——非法 JSON / 非 UTF-8 / 类型不匹配等绑定失败统一交给共享 `GlobalExceptionHandler` 分类为 **400 + `LF_API_BAD_BODY` + 中文可行动消息**（新增错误码入 `ApiErrorCodes`），不再落入 500 兜底误导业务方排查服务端；原始解析异常（含行 / 列位置）记 Warning 日志供排障，不透出客户端。Server 与 WinHost 双宿主经共享层自动一致。
- **测试页传输错误分类**：`POST /api/printer/test` 发送失败（打印机连接不可达 / 超时等传输故障）由裸 500 改为 **400 + 新增错误码 `LF_TRANSPORT_TEST_FAILED`**，消息含打印目标地址（host:port / 打印机名 / USB 名）与失败原因（口径对齐 `/api/printer/status` 降级信息）；客户端经 ILogger 留痕（目标 / 插件 / 原因）。
- **403 补 ErrorView**：result / progress 端点非归属设备回报（`NotJobOwner` / LF_SRV_004）由**空 body 403** 改为 403 + ErrorView——全系统错误响应契约统一为 `{ code, message }`，前端不再显示裸 `HTTP_403`。
- **`LF_INTERNAL_001` 常量化**：入 `ApiErrorCodes.InternalError`，全仓仅注册表一处字面量，其余引用常量。
- **插件包无效消息中文化（待决议三项按 Issue #33 评论用户确认）**：Core 业务性校验失败统一抛新增的 `PluginPackageException`（消息全中文可行动），四类高频——**非 zip / zip 损坏 / manifest 缺失或非法 / DLL 无效**（`PluginProbe` 增逐 DLL 失败原因区分 BadImageFormatException）——给具体中文消息；其余框架 `InvalidDataException` 在 API 边界（WinHost 插件安装 / 卸载、Server 插件包上传）转通用中文提示，不再直出英文原话（如 `End of Central Directory record could not be found.`）。
- **测试**：`LabelFrame.Api.Tests` 新增共享处理器分类用例（语法错误 / 非 UTF-8 / 类型不匹配 → 400 LF_API_BAD_BODY；未分类异常 → 500 常量码）；`LabelFrame.Server.Tests` 新增错误契约端点用例（绑定失败三类 + result/progress 403 ErrorView + 非 zip 上传中文消息）；`LabelFrame.WinHost.Tests` 新增绑定失败用例、测试页传输失败（connection.json 指向已关闭回环端口 → 400 LF_TRANSPORT_TEST_FAILED + 日志留痕断言）、Log 传输测试页成功路径回归、插件四类中文消息用例；既有插件包用例断言随异常类型同步更新。Debug / Release 全量 `dotnet test`（排除 Perf/Soak，403 项）与 `pnpm lint / test`（260 项）通过。
## 迭代 51 客户端设备日志链路与前端可观测 · 2026-09-11

- **WinHost 补挂设备日志端点（决策 #106，AC-01）**：审计发现 WinHost 装配了 `SqliteLogStore` 却从未调用共享 `MapLogApi`——客户端实例 `GET/POST /api/logs` 命中 SPA 回退返回 `index.html`（HTTP 200 + HTML）。修复 = `WinHostApp` 补挂共享端点（错误码 `LF_API_001`，与宿主其他共享端点一致），客户端实例返回 JSON。补挂后客户端 53960 开放日志写入接口，与决策 #79 局域网信任模型的关系已在 DESIGN 决策 #106 记录（无新增攻击面：Windows 客户端默认仅回环监听，Linux 容器与 Server 同暴露面）。
- **设备日志按行存储（决策 #106，AC-04，共享契约先文档后代码）**：`SqliteLogStore.AppendAsync` 按物理行拆分入库——多行提交（`lines` 多元素或元素内含 `\r\n` / `\n` / `\r`）拆为独立记录（每行一条、同一提交共用时间戳、单事务原子写入、空白行不落库），与前端按行展示对齐；Server 与 WinHost 同源共享实现，一处修复两端生效。既有 logs.db 历史「合并行」数据**不迁移**（用户确认：旧记录仍为含换行符的单条 line，`pre-wrap` 原样多行呈现）。
- **日志页失败显错（AC-02）**：查询失败（非 2xx / 解析失败——如旧版客户端 SPA 回退返回 HTML 的 200）显示明确错误条（复用现有错误横幅样式），解析失败新增数组形状校验（`响应格式异常` 文案），不再静默「暂无日志」；失败后自动刷新保持（5s 继续重试，恢复后错误条消失）。
- **前端全局错误兜底（AC-03）**：`ErrorBoundary`（错误页**仅文案 + 重新加载按钮**，不附错误摘要——用户确认，避免透出内部细节）挂应用根（`main.tsx` 最外层）；`window.onerror` / `unhandledrejection` 统一 `console.error` 留痕（开发线索，不做后端上报）。
- **测试**：新增 / 改写 13 项——WinHost 集成 1（`/api/logs` JSON 响应 + 两行两条独立记录 + 缺参 400 / LF_API_001）+ `SqliteLogStore` 3（多行提交拆为两条 / 元素内换行拆分 / 空白行跳过与全空提交不落库）+ 前端 9（日志页 4：非 2xx 显错 / 解析失败显错 / 成功按行渲染 / 失败后 5s 自动重试恢复；ErrorBoundary 3：错误页 + console.error 留痕不透出细节 / 点击重新加载 / 正常子树透传；全局监听 2：window.onerror / unhandledrejection 各自留痕）。

## 迭代 53 PDA 可观测性（logcat + 崩溃捕获 + 本地滚动日志） · 2026-09-11

- **logcat 日志封装（决策 #105）**：`HostLog` 轻量静态门面（Info / Warn / Error，tag 前缀 `LabelFrame.` + 区域名 Host / Http / Print / Server / Ui / Crash），零新依赖、写失败静默。关键路径埋点：本地 HTTP 请求失败（method / path / 异常消息 + 完整堆栈）、打印循环发送失败（作业 / 项 / 打印机目标 / 原因）与领取失败、Server 轮询与回报失败（目标地址 / 原因）、前台服务生命周期（启动一行含版本 / 设备 / 服务器 / 打印机配置、停止、开机自启广播）、配置页操作失败。
- **本地滚动日志（用户确认的新增交付，原 Issue 默认仅 adb 可查）**：与 logcat 同一封装同步落应用私有目录 `{FilesDir}/logs/host-<yyyyMMdd>-<NNN>.log`；单文件 512KB 上限滚动到下一序号，目录保留最近 6 个（名字序即时间序）——现场无法 adb 时可直接取证。
- **全局崩溃捕获**：Java 层 `SetDefaultUncaughtExceptionHandler`（记录后交回原处理器，保持系统崩溃流程）+ .NET `AppDomain.UnhandledException` 双通道 → logcat Error 完整堆栈（长堆栈分片）+ 崩溃摘要 `{FilesDir}/crash/crash-<时间戳>.txt`（时间 / 来源 / 版本 / 系统 / 设备 / 堆栈，保留最近 3 份）；注册时机 = 新增 `[Application]` 子类 `HostApplication` 进程创建首行（早于一切组件）+ 服务 OnCreate 首行幂等兜底；服务启动检测到上次崩溃摘要记录 Warn 提示（即「下次启动可读」）。崩溃摘要**不回传服务端**——回传管道登记 DESIGN「风险与未决问题」。
- **请求级吞错可见化（语义保持）**：`EmbeddedHttpServer` 每请求 catch 仍吞掉异常（单请求失败不影响服务），只加日志——请求行已解析出的处理期失败记 Error（含 method / path），对端断开 / 畸形请求（未解析出请求行）记 Warn 防噪；`/api/printer/test` 5xx 自身原因补 Error。
- **进度上报失败保持静默**（决策 #101「不告警刷屏」），仅 Server 轮询主循环与终态回报失败记周期 Warn。
- **测试**：AndroidHost 无测试体系（不在范围），AC 以 CI「Android 构建（PDA 宿主）」+ 真机验收为证据；本轮 `dotnet build / test`（排除 Perf/Soak，384 项）全绿（slnx 无涉改动），AndroidHost Release 本地构建通过（0 警告 0 错误）。

## 迭代 49 PDA 宿主自动化构建与品牌化 · 2026-09-10

- **CI 自动化（决策 #104，含流程治理：修改 workflows）**：`ci.yml` 新增「Android 构建（PDA 宿主）」job（ubuntu；JDK 17 + Android SDK 36 + .NET Android workload；Release 配置 + `-p:EmbedAssembliesIntoApk=true`），用户拍板**直接设为第三项必需检查**——master 门禁由双必需升三必需（既有两项名称与语义不变；落地顺序 = 先合入 job 的 PR 验证全绿、合并后立即补进门禁 ruleset）；AndroidHost 仍不进 `LabelFrame.slnx`（CI 单独构建该工程，不拖慢日常全仓构建）。
- **随发版出 APK**：`release.yml` 随 `v*` tag 构建 Release APK 上传 GitHub Release 附件（`LabelFrame-AndroidHost-<版本>.apk`；`ApplicationDisplayVersion` / versionCode 由 tag 注入，csproj 默认 `1.0`/`1` 兜底）。
- **签名（三项待决议用户拍板：自签 keystore + Secrets）**：CI 发版检测到 `ANDROID_KEYSTORE_BASE64` 等 4 个 Secrets 时用专用自签 keystore 签名，缺失回退 debug 签名并告警（对齐 MSI 自签过渡决策 #65 思路）；`scripts/create-android-keystore.ps1` 一次性生成并可选写入 Secrets。**换签名影响（README 已写明）**：已装真机（DT50）从 debug 签名包升级到正式签名包需**卸载重装**——配置清空需重填，且 Android 8+ 设备号（ANDROID_ID）绑定签名密钥、**设备号会变**（Server 设备目录出现新条目、旧条目停留离线）。
- **应用图标（品牌化）**：沿用 L 型品牌体系（主蓝 #1668DC + 白 L，与 MSI / 桌面图标同源）——`generate-android-icons.ps1` 生成各密度启动器位图（方形圆角 / 圆形），API 26+ 自适应图标（矢量前景 + 主蓝底），常驻通知小图标换为白色单色 L 矢量（替换系统默认机器人图标）；Manifest 补 `icon` / `roundIcon`。
- **版本信息（待决议拍板：配置页 + 本地 HTTP）**：配置页「本机信息」子页新增「程序版本」（只读）；本地 HTTP `GET /healthz` 与 `GET /api/host/config` 新增只读 `version` 字段（宿主本地契约向后兼容增量，DESIGN §5.3 已先行更新）。
- **脚本**：`build-androidhost.ps1` 支持 `-Configuration Release` 与 `-Version`（对齐 CI 参数）；`generate-android-icons.ps1`、`create-android-keystore.ps1` 新增。
- **测试**：AndroidHost 无测试体系（不在范围），AC 以构建产物与真机验收为证据；本轮 `dotnet build / test`（排除 Perf/Soak）全绿（slnx 无涉改动），AndroidHost Release 本地构建通过（含图标资源编译）。

## v0.25.0 迭代 48 发布 · 2026-09-10

- **打包范围**：工作台信息架构重构 + 作业历史自动轮询（迭代 48，决策 #103）。详见迭代条目。
- **客户端可感知变更**：工作台模板列表由表格升级为**卡片网格**（缩略图主视觉，自适应多列；搜索 / 分组过滤 / 缩略图灯箱放大 / 双击编辑 / 新建 / 导入 / 导出 / 删除等既有能力全部保留）；作业历史页**存在进行中作业时 1.5s 自动刷新**（列表全终态即停、页面隐藏暂停、恢复立即拉取），配合 v0.24.0 的进度增量上报可实时观察打印进度。
- **版本同步**：`/api/server/info` 版本与稳定版 Compose 默认版本更新为 `0.25.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.25.0` / `labelframe-client:0.25.0`（均含 `latest`）。AndroidHost（PDA）不随 Release 分发，需本地构建安装。

## 迭代 48 工作台信息架构重构 + 作业历史自动轮询 · 2026-09-10

- **工作台信息架构重构（决策 #103，用户定稿方案 A：卡片网格）**：模板列表由表格形态收敛为**自适应卡片网格**（缩略图主视觉，`minmax(196px, 1fr)` 多列自排）——卡片 = 大号缩略图（点击居中灯箱放大，迭代 46 机制复用）+ 名称 / 分组徽标 / 更新日期 + 编辑 / 导出 / 删除操作行，双击卡片进入设计器；既有能力全部保留：模板名搜索、分组过滤（叠加生效）、新建 / 导入 .lfpkg / 导出 / 删除确认、缩略图会话缓存随列表刷新失效。「卡片网格 vs 列表+详情面板」双形态先在本机沙箱（mock 数据 12 模板）实现并截图征求用户定稿，未采纳形态随收敛移除。
- **作业历史自动轮询（决策 #103，用户定稿建议组合）**：作业历史页在**存在进行中（非终态）作业时 1.5s 自动轮询**（对齐「数据与打印」useJobPolling 节奏），列表全终态即停（新提交作业需手动刷新）；**页面隐藏时暂停**、恢复可见立即拉取一次；轮询失败保留既有列表（瞬时错误只出横幅、不清空表格）并 2s 退避重试；手动「刷新」保留。API 跟随服务端 / 单机降级双模式，deviceFilter（客户端只看自己作业）语义不变；配合迭代 47 进度增量上报，停留页面即可见 completedItems 逐步增长。
- **测试**：前端新增 3 项（1.5s 轮询 + 全终态停止 / 页面隐藏暂停 + 恢复立即拉取 / 失败保留列表 + 2s 退避），既有工作台预览测试适配卡片形态（表格行定位 → 卡片定位）；前端 `pnpm lint / test`（260 项）与双构建通过；日常 `dotnet test` 384 项全绿（排除 Perf / Soak，无后端改动）。

## v0.24.0 迭代 40-47 汇总发布 · 2026-09-10

- **打包范围**：PDA 宿主配置与测试体验（迭代 40，含本机 HTTP 配置端点）、PDA 配置界面文案与视觉优化（迭代 41）、服务端 Claimed 作业超时回收（迭代 43）、Windows 客户端窗口化（迭代 44）、批次首张节奏修正 + 设置页布局自适应 + 工作台小修（迭代 45）、工作台模板缩略图预览（迭代 46）、作业可观测性——进度增量上报 + 日志细化 + 缺陷 #14 修复（迭代 47）；迭代 42（工作流重构）与流程治理（worktree 并行）为流程 / 文档变更，不影响产物。详见各迭代条目。
- **服务端行为变更（部署注意）**：Claimed 作业超时回收默认开启（30 分钟；`Server.ClaimedJobTimeoutMinutes` / `LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES`，0 或负值 = 关闭）——宿主失联的领取作业超期终态化 Failed（`LF_SRV_009`，文案明示「结果未知，可能已实际打印」，禁止自动重投）；长挂起场景（如打印机离线挂起数小时）可调大或关闭规避误判。
- **跨端公共契约新增（决策 #101，向后兼容）**：宿主→Server 新增 `POST /api/devices/{deviceId}/jobs/{jobId}/progress` 进度增量上报端点；旧版宿主不调用行为不变，新版宿主对旧版 Server 的 progress 上报收到 404 时静默降级、不影响打印主链路。**进度逐张增长需 Server 与宿主（客户端 / PDA）双侧均升级到本版本**。
- **客户端可感知变更**：客户端安装后为自有应用窗口（WebView2 嵌入、托盘常驻、单实例激活；MSI 自动引导安装 WebView2 运行时）；批次作业每个作业首张不再等待批间间隔（决策 #100，作业内节流语义不变）；日志级别可配置（`LABELFRAME_LOG_LEVEL` / `WinHost:LogLevel`，默认 Information）；修复 Serilog 逐张日志不再落盘缺陷（#14，`app-*.log` 恢复写入）。
- **版本同步**：`/api/server/info` 版本与稳定版 Compose 默认版本更新为 `0.24.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.24.0` / `labelframe-client:0.24.0`（均含 `latest`）。AndroidHost（PDA）不随 Release 分发，需本地构建安装。

## 迭代 47 作业可观测性（进度增量上报 + 日志细化 + 缺陷 #14 修复） · 2026-09-10

- **进度增量上报（决策 #101，跨端公共契约——强化路径）**：Server 新增 `POST /api/devices/{deviceId}/jobs/{jobId}/progress`——请求体 `{ completedItems, failedItems }`（无宿主时间戳，服务端时钟唯一权威）；仅 `Claimed` 接受，计数**按字段取 max 单调递增**（乱序 / 迟到 / 重复上报不回退）；只增不改终态（不写 status / finished_at、不刷新 claimed_at，与决策 #98 超时回收正交）；终态收到 progress 幂等 no-op 返回当前视图、`Pending` 409、错误语义与 result 端点完全一致；result 仍是唯一终态写入者（绝对值覆盖）。打印中查询 `GET /api/jobs/{jobId}` / 作业历史的计数逐步增长，不再终态一次跳变（本机联调实证：10 张节流作业轮询序列 Claimed 1→2→…→9→Completed 10）。
- **宿主接入（WinHost / Linux Client / AndroidHost 同迭代）**：进度上报放在既有回报循环（1s 周期）——**默认 1s 节流 + 计数有变化才发**（无变化零流量，`LABELFRAME_PROGRESS_INTERVAL_MS` 可调）；Server 不可达静默降级（失败不告警刷屏、不阻塞打印主链路，下一轮携带最新计数自然重试）；作业终态后停发、终态只走 result。WinHost 节流判定抽为 `ProgressThrottle`（TimeProvider 注入，FakeTimeProvider 确定性测试）；AndroidHost 同构内联实现。
- **缺陷 #14 修复（Serilog 逐张日志不再落盘）**：根因 = 迭代 32 装配抽取（0aca4dd）后 `UseSerilog` 留在 `Program.Main` 中只用于配置绑定、**从不 Build 的 builder** 上，文件日志配置自 2026-08-25 起即死代码（08-25～09-03 的 `app-*.log` 由更旧安装版写入，此后换装含缺陷构建即停写）。修复：配置收拢为 `SerilogSetup.Apply`，经 `WinHostApp.BuildAsync` 的 `configureBuilder` 扩展点挂到**真实 builder**；启用 SelfLog 落盘（同目录 `serilog-self.log`，sink 失败可见化）+ 启动自检事件；回归测试以生产同装配路径构建应用→写事件→断言 `app-*.log` 创建且含事件，结构性防止「配置挂在错误的 builder 上」复发。
- **日志细化（决策 #102）**：级别可配置——`LABELFRAME_LOG_LEVEL` 环境变量 / `WinHost:LogLevel` 配置节（默认 Information，启动读取生效，非法值回退并可用），Server 沿用标准 `Logging:LogLevel`；失败上下文结构化——发送失败日志在作业 / 项索引 / 原因之外补**传输插件 ID / 打印目标端点 / 错误码（LF_IO_001）**，经 Serilog 文件通道逐字段断言（测试锚定）。
- **测试**：新增 19 项——Server 服务级 3（max 单调 / 不改 claimed_at / 终态与错误语义）+ Server 端点级 2（视图增长 + result 唯一终态 / 409·404·403·400 错误对齐）+ WinHost 路由 3（FakeTimeProvider 节流时间轴 / 进度失败静默降级不阻塞终态回报 / poller progress 端点形状）+ `ProgressThrottle` 3 + Serilog 回归 3（文件创建含事件 / Warning 过滤 Information / 级别解析）+ 失败上下文日志 1，另有级别解析理论项内联；日常 `dotnet test` 384 项全绿（排除 Perf / Soak），前端 `pnpm lint / test` 通过（无前端改动）。

## 迭代 46 工作台模板缩略图预览（预览列内嵌出图） · 2026-09-10

- **预览列内嵌缩略图（验收修订：纯悬停触发不可发现，用户定稿 B 形态）**：工作台新增「预览」列，列表加载后按需调用既有 `POST /api/templates/{name}/preview`（按模板 TestData 渲染 PNG，Server 与 WinHost 双宿主均已实现）拉取全部模板预览，单元格内以 40px 高等比缩略图直出（所见即所得）；**点击缩略图居中灯箱放大**（二次验收修订：行旁 296px 小浮层 → 居中大图模态——图片按真实宽高比约束在视口 75% 且 ≤900px，203 DPI 原生约 560px、再往上为插值放大；Esc / 点背景 / 点 × 关闭，点卡片本体不关闭）。初版为行悬停 400ms 防抖触发的浮层（PR #19），真机验收时用户判定不可发现，当轮修订为预览列形态（验收教训：客户端托管 UI 的验收须先重建 web/dist；preview 请求须显式携带 JSON 头与空体——旧版 Server 对裸 POST 返回 404，PR #22）。前端新增 `previewTemplate(name)` blob 封装（与 renderImage 同构，serverApi / localApi 双 base 自动跟随业务模式——Client 与 Server UI 共享工作台组件，双端同时受益）。
- **会话内缓存与生命周期**：「模板名 → blob URL」会话内缓存，同一列表周期内每模板只请求一次（含在途去重与失败结果缓存，过滤切换 / 放大均不重复请求）；列表刷新（重新 load，如删除 / 导入后）缓存整体失效并释放全部 blob URL（防泄漏），周期代数防护使失效时仍在途的旧请求结果不写入新周期；组件卸载兜底释放。
- **加载 / 失败态**：拉取中单元格显示骨架占位（呼吸动画）；失败（模板已删除、旧宿主 404、网络不可达）显示警示占位（title 携带原因），不影响列表与其余功能；预览图透明底衬浅棋盘格（白底标签不「隐形」）。
- **测试**：前端测试 6 项（修订后）——列表加载即拉取全部（骨架 → 缩略图 blob URL）、单机降级走本机 API、过滤切换缓存命中不重复请求不重复建 blob URL、列表刷新失效（释放旧 URL 并对剩余模板重拉）、失败态占位与同周期失败缓存、点击放大不重复请求 + 遮罩 / Esc 关闭；日常 `dotnet test` 364 项全绿（排除 Perf / Soak），前端 `pnpm lint / test`（client / server 双模式）与双构建通过。

## 迭代 45 批次首张节奏修正 + 设置页布局自适应 + 工作台小修 · 2026-09-10

- **批次首张节奏修正（决策 #100，修正 #74 计数语义）**：WinHost 批间计数 `_sendsSinceBatch` 由「跨作业全局累计（仅重启清零）」改为**按作业重置**——打印循环领取到与上一次发送不同的作业时计数归零（记录 `_lastSentJobId`），**每个作业的首张立即发送、不再等待批间间隔**；作业内节流语义不变（发满 batchSize 整数倍后、下一张发送前暂停 batchIntervalMs，claim-then-delay 结构不动）。根因：原累计语义使任何历史发送都会让下一作业首张触发暂停（batchSize=1 / 间隔 5s 时每个新作业首张前白等 5s）；`BatchPrintPolicy` 纯函数判定与守卫（已发送数 > 0）本就正确，变化的只是喂给它的计数口径。公共契约零变更（PrintSettingsDto / API / 模板包不动），仅 WinHost 打印循环；AndroidHost / PDA 如有同类行为另行评估。既有「跨作业累计」断言的集成测试改写为按作业重置语义（含「B 作业从 0 重新计数」的口径证明）；辅助时间轴通道不再做反向断言（假时间 500ms 步进在调度噪声下会产生假间隔，8 轮稳定性验证通过）。
- **设置页布局自适应（待决议定稿 B：多列网格）**：内容容器由固定 `maxWidth: 640` 左对齐窄列改为 `repeat(auto-fit, minmax(560px, 1fr))` 自适应网格——宽窗自动双列（1600px 下两列 747px：服务端地址｜连接方式 / 打印批次｜打印机 / 更新与安装包｜插件管理），窄窗自动回退单列（800px 下约 700px，无横向溢出、纵向可滚动）；迭代 44 客户端窗口化后窗口可自由拉伸，空间利用随宽度自适应。A/B 两方案在本机沙箱（mock 数据）实现并出宽 / 窄两档截图征求用户后定稿 B。
- **工作台小修**：① 模板名搜索框（页头、搜索图标内嵌）——子串匹配、大小写不敏感、首尾空白忽略，与分组下拉过滤叠加生效（交集），清空恢复完整列表；无匹配时显示专用空态。② 操作列视觉修正（宽窗下操作列右侧无白底空带）：根因是 `.table .actions` 的 `display: flex` 同时命中 `th`，使表头单元格退出表格列宽分配——1600px 宽窗下表头操作列只有 240px（浅白底提前截断）而表体操作列宽 750px，右侧 510px 露出灰底；修正为 flex 只作用于表体按钮容器（`td .actions`）、`th` 保持 table-cell 并文字右对齐，表头 / 表体列缘完全对齐（DOM 几何断言证据）。
- **测试**：新增前端测试 3 项（搜索大小写不敏感子串 / 搜索 + 分组叠加与双清空恢复 / 首尾空白忽略），改写 WinHost 节流集成测试 2 项（跨作业不暂停 + 新作业从 0 重新计数）；日常 `dotnet test` 365 项全绿（排除 Perf / Soak），前端 `pnpm lint / test / build` 通过（250 项）。

## 迭代 44 Windows 客户端窗口化（WebView2 嵌入界面壳） · 2026-09-09

- **界面壳（决策 #99，D1=A）**：Client 界面从「启动弹默认浏览器」升级为**自有应用窗口**——WinHost（仅 Windows 目标）新增窗口壳：独立 STA UI 线程 + WinForms 主窗体（标题 LabelFrame / 应用图标 / 1280×800 起步）+ WebView2 控件加载本地 UI；HTTP 未就绪先显示加载态、就绪后自动导航；前端零改动、`/api/*` 契约零变更、Linux 无头客户端不受影响。本机地址留在窗口内导航，外链（帮助 / 下载等）自动转系统浏览器（防误导航）；WebView2 用户数据目录 `%LOCALAPPDATA%\LabelFrame\webview2`。
- **关窗 / 托盘 / 单实例（D3 / D4）**：关闭窗口 = 隐藏到托盘、服务常驻（真正退出走托盘「退出」）；托盘双击 / 右键「打开界面」与**单实例二次启动**（互斥锁 + 激活事件）共用「显示并前置」路径，二次启动不再报端口占用；`OpenBrowser` 配置语义演进为「启动时显示界面」（默认开），`--autostart` 保持仅托盘不弹窗。
- **WebView2 运行时缺失处理（D2=C，对齐决策 #44 模式）**：MSI 检测 EdgeUpdate Clients 产品键（per-machine / per-user 双视图）——全 UI 显示带官方下载链接的中文对话框，**装完运行时点「重新检测」即可继续，无需重启安装程序**；静默 / 基础 UI 由 LaunchCondition 拦截提示；应用启动时初始化失败（如装后被卸载）自动回退默认浏览器 + host.log 记录原因，功能不受损。
- **启动失败可见化**：端口占用等监听失败以中文消息框提示用户（含 ListenUrl 修改指引），不再只写 host.log 无感知退出；退出路径确认 WebView2 子进程不残留。
- **MSI / 断言**：WebView2Loader.dll 等随发布产物自动进包（MSI 约 +1.5MB）；快捷方式与安装选项页文案去掉「浏览器」表述；MSI 结构断言新增 WebView2 契约（缺失对话框 + 序列先于欢迎页 + 双注册表视图 + LaunchCondition + 重新检测链路），并修复既有运行时序断言因 PowerShell 单行展平形成的空转（`[int]` 转换失败静默回退字符串比较）。
- **本机冒烟实证三个线程 / 布局坑（已固化代码注释）**：UI 线程须显式 `SetApartmentState(STA)`（.NET 默认 MTA → WebView2 报 RPC_E_CHANGED_MODE）；UI 线程须显式安装 WinForms 同步上下文、窗口经消息队列投递显示（泵启动前 Show 使 await 续体落线程池 → WebView2 报 UI-thread-only）；首显须重申 ClientSize（WebView2 初始化与布局竞态使窗口停在未展开尺寸 864×293）。
- **测试**：新增单元测试 13 项（UiUrl 规范化与导航边界 / UiReadiness 就绪等待五路径含探测异常与超时 / SingleInstanceGuard 抢占 / 激活信号 / 启动失败文案端口占用识别）；日常 `dotnet test` 364 项全绿（排除 Perf / Soak）。本机冒烟：窗口首显 1280×800 + UI 加载、关窗隐藏服务存活、二次启动激活、`--autostart` 无窗口、端口占用中文报错框、运行时不可用回退浏览器 + 日志、退出后 WebView2 子进程零残留。

## 迭代 43 Claimed 作业超时回收（宿主重启后终态化） · 2026-09-09

- **Server 侧超时回收（决策 #98，用户拍板）**：作业被设备领取（Claimed）后，宿主崩溃 / 重启会使「本地作业 ↔ Server 作业」内存映射丢失、终态永不回报，此前作业停留 Claimed 直至 30 天历史清理——进度查询失真、失败项不能重打。现按「领取时间 + 超时时长」回收为终态 **Failed**（原因 = 宿主失联超时，错误码 `LF_SRV_009`，文案明确「结果未知，可能已实际打印；需重打请用新 requestId 重发」）。
- **判定语义**：超时默认 **30 分钟**（`Server.ClaimedJobTimeoutMinutes` / `LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES`，0 或负值 = 关闭回收、沿用现状）；只以 `claimed_at` + 服务端时钟计龄、与设备在线状态无关（宿主重启后照常心跳，「在线」无法区分映射丢失与正常打印）；后台独立扫描任务（复用 `ExpirationScanIntervalMinutes` 周期，与 Pending 过期扫描同构）。
- **不重投 + 终态即最终**：回收不自动重新投递（作业可能已实际打出，重投会重复打印）；同一 requestId 重放只返回既有作业；宿主迟到的真实回报按既有幂等重放返回 Failed 视图（200），不覆盖超时判定——与回报的竞态由「status = Claimed」UPDATE 条件收敛，两个方向都不互相覆盖。
- **误判权衡（记入 DESIGN 决策后果）**：宿主活着但长时间未回报（如打印机离线挂起数小时）也会被回收，业务系统据此重发可能与本地恢复续打叠加成重复打印——正常批量分钟级完成、30 分钟余量充足，长挂起场景可调大配置规避。
- **契约文档先行（强化路径）**：`docs/DESIGN.md` 决策表 #98 + §5.1 服务端作业状态机补充 Claimed 超时回收段；§6「宿主重启后 Claimed 作业无终态」未决问题收敛为「Server 侧已修复，客户端持久化映射 + 重启补报不做」。
- **测试**：新增单元（FakeTimeProvider）+ HTTP 集成测试 10 项——失联回收终态 / 正常回报零回归 / 只回收失联 Claimed / 迟到回报幂等重放 / 关闭开关回归 / 配置项默认值与环境变量覆盖 / 端到端（领取 → 失联 → 回收 → 查询终态与原因 → 无二次领取）；日常 `dotnet test` 338 项全绿（排除 Perf / Soak）。

## 迭代 42 工作流重构：Issue 驱动迭代 + PR 门禁 · 2026-09-09

- **工作流切换（决策 #97）**：迭代任务全面迁移 GitHub Issue（新增「迭代任务」模板立项，含 AC-xx 验收标准与启动命令）；变更走短主题分支 + PR + squash 合并；`master` 启用 ruleset 门禁——必须 PR + 双必需检查（「构建与测试（dotnet + 前端）」「MSI 结构断言（安装包 UI 契约）」，strict 目标分支新鲜度）+ 禁强推，管理员无豁免。CI 从推送后「事后发现」变为合并前拦截。
- **新增 docs/WORKFLOW.md**：平台事实、日常迭代流程、事实源唯一映射（Issue = 本轮需求 / 范围 / 决议 / 进度 / 验收）、风险裁剪（轻量 / 标准 / 强化）、门禁演练记录与未启用选配清单——流程的单一权威说明。
- **验收滞后不结项**：新增 PR 模板（自查清单）与 `待验收` 标签——PR 关联 Issue 用 `#N` 普通引用、禁用关闭关键词；真机 / 用户验收滞后时 Issue 保持开放追踪，存量欠账仍见 ACCEPTANCE-BACKLOG.md。
- **ROADMAP 瘦身**：迭代 0-41 详情段（约 1140 行）整体迁至 docs/archive/ROADMAP-ITERATIONS.md；ROADMAP 保留状态总览索引 + 待需求清单，结项只更新一行。
- **AGENTS.md 重写**：迭代方式（Issue 驱动 + PR 门禁）与 DoD（必需检查绿 + squash 合并 + Issue 回写证据）对齐新流程；发布机制不变（记账 PR 合并后推 `v*` tag → release.yml）。
- **门禁演练**：故意失败 PR 验证「检查红 → 合并被阻」，结项记账走首个正式 PR 全绿合并（证据见 docs/WORKFLOW.md §5）。
- 纯流程变更：产品代码与公共契约零改动，不涉及版本发布。

## 迭代 41 PDA 配置界面文案与视觉优化 · 2026-09-08

- **信息架构重组（决策 #96）**：配置页由单页长滚动改为「主页 + 三子页」——主页只放当前状态卡与三个设置入口（① 连接服务器 / ② 连接打印机 / ③ 本机信息，每行带当前值摘要），每屏一个任务；三个子页各自「编辑 → 测试 → 保存并重启服务」，**不设全局草稿**（保存动作紧贴修改处，无「改了忘保存」心智负担；往返导航不丢输入）。实现仍为原生 Activity + C# 代码布局（同一 Activity 内视图切换，零新依赖）。
- **框架评估结论（调研后用户定稿）**：跨端框架（Kotlin/Compose、Flutter、uni-app）会失去与 `LabelFrame.Core` C# 打印链路的代码共享，MAUI 在包体与低端机表现上对一个配置页得不偿失（社区证据：裁剪后仍难低于 ~18MB、低端真机卡顿议题活跃；纯 .NET Android 为 MAUI 团队认可的正式路径）——保留原生；重新评估触发条件 = PDA 业务打印界面立项（届时评估 MAUI 或按决策 #93 由第三方 Web 经 JS 桥承接）。
- **文案全面改写为人话**：禁用专业术语（宿主、服务端→统一改称「服务器」、终态、渲染、生效配置等一律不出现）；每个位置只说三件事之一（这里填什么 / 点这个按钮会发生什么 / 状态意味着什么 + 下一步怎么办）；按钮动词短语；错误信息转译为可行动提示（如「连不上服务器——请检查地址是否正确、PDA 是否连着 WiFi」「没打出来——请检查打印机是否开机、IP 是否正确、是否缺纸卡纸」，原始异常缩为小字附注供排障）；覆盖配置页全部文本、Toast、常驻通知、内置状态页 HTML、应用名（`LabelFrame 打印宿主` → `LabelFrame 标签打印`）。
- **视觉设计系统（代码布局实现）**：浅灰底 + 白色圆角卡片；状态卡语义色摘要（绿 = 一切正常 / 红 = 连不上 / 灰 = 服务未运行）+ 服务器 / 打印机明细行；主按钮「保存并重启服务」通栏蓝底白字（≥52dp）；次级按钮描边样式（≥48dp）；入口行 ≥64dp；设备号等宽字体防误读；保存校验错误红字显示在保存按钮上方（不再写进状态区）；「刷新状态」按钮移除（状态 5 秒自动刷新，卡内小字注明）。
- **诚实性修正**：测试打印走的是已保存的打印机地址（既有行为）——输入地址未保存时点「打印测试标签」提示「先保存再测试——上面填的地址还没保存，现在测试用的还是旧地址」，不再打到旧地址造成困惑（仅 UI 提示，打印链路零改动）。
- **通知 / 状态页**：常驻通知一句话说清（「服务器：已连接 / 连不上（自动重试中）/ 未设置 · 打印机 192.168.x.x」）；状态页 HTML 重排为本机 / 服务器 / 打印机三卡片 + 语义色 + 通栏「打印一张测试标签」大按钮。
- **顺带清零既有警告**：`LabelHostConfig` 两处可空链式调用警告拆为逐条语句；`MainActivity` 重写后 AndroidHost 构建 0 警告（OnBackPressed 的 API 33 过时提示以 pragma 屏蔽并注明理由——未启用 predictive back 前框架仍回调，避免为此引入 AndroidX）。
- **真机走查（UROVO DT50，Release APK）**：主页（绿色状态卡「一切正常，随时可以打印」+ 三入口行摘要）、服务器子页（真实地址回填 + 引导句）、测试连接（绿字「✓ 能连上服务器」）、打印机子页（IP / 端口 / 引导句）、**保存并重启服务（现场实际操作：新地址持久化 + 服务带新配置运行）**、常驻通知新文案（dumpsys 核对）、浏览器状态页（标题 / 副标题 / 按钮 / 加载文案）逐项核验通过；测试打印完整链路 API 确认 **Completed（1/1，真实 Zebra 出纸）**。
- **真机修正（用户反馈）**：主页三个设置入口行显示为巨大空白卡片——入口行容器误用竖向布局，文本列「宽度 0 + weight」在竖向父容器中被压成 0 宽（文字逐字换行、卡片又高又空）；改为横向布局并补齐箭头布局参数后真机复验通过（UI 树与截屏双重确认：三行标题 / 摘要 / › 齐全，高度正常）。修正后屏上打印结果文案 / 本机信息子页 / 未保存提示仍留待日常使用确认。
- **真机修正二（用户反馈）**：① 设备号去掉 `pda-` 前缀，直接用 `Settings.Secure.ANDROID_ID` 原值（如 `b4ee90bd59b33c53`；兜底随机码同样无前缀）——**升级后设备号变化，Server 设备目录会出现新条目、旧 `pda-…` 条目变离线残留，需在服务端删除**；② 本机信息页「设备号」标签去掉括号说明、设备名称提示精简为「服务器设备列表里显示的名字」、主页本机信息摘要只显示设备名称；③ 修复**旋转退回主页**——旋转触发 Activity 销毁重建，`ConfigurationChanges = Orientation | ScreenSize | KeyboardHidden` 声明后不再重建（子页与未保存输入在旋转后保留），真机横屏验证仍停留当前子页。
- **测试**：`dotnet build` 0 警告 0 错误；AndroidHost 构建 0 警告 0 错误；日常 `dotnet test` 328 项全绿（桌面零回归）。

## 迭代 40 PDA 宿主配置与测试体验 · 2026-09-08

- **PDA 宿主定位定稿（决策 #95，用户拍板）**：AndroidHost = 后台打印执行服务（打印入口在业务系统侧，承接 #93），本迭代收敛为 PDA 专项——PC 侧易用性候选项（模板快选 / 打印值记忆 / 失败提示行动化 / ServerUrl 免重启等）全部延期，PDA 业务打印界面（扫码即打 / 模板列表 / 字段表单）明确不做。跨端公共契约零变更（仅 AndroidHost 本地 HTTP 扩展）。
- **原生配置页（唯一 UI）**：点 App 图标打开（替代此前启动即 Finish 的无界面行为）——服务端地址 + 测试连接；打印机按「品牌（默认 Zebra）→ 连接类型（默认网口 TCP）→ IP + 端口（默认 9100）」两级结构（品牌 / 连接类型为未来传输插件路由键）；设备号只读自动展示 + 复制；设备名称可选编辑；运行状态区（服务 / 服务端最近通讯 / 打印机最近出纸或失败）；「测试打印」本地提交内置测试标签走完整链路（校验 → 渲染 → `^GF` → TCP 发送 → 终态）。
- **设备号自动生成**：取 `Settings.Secure.ANDROID_ID`（`pda-<id>`），不可配置——多台设备天然不撞号，消除配置出错导致互相领作业的风险；卸载重装不变、恢复出厂视为新设备；取不到（含 Android 8 前著名坏值）时本地 UUID 兜底。设备名称默认 `PDA-<码后 4 位>`，注册 Server 随 `name` 上报（设备目录 / 目标设备选择器 / `GET /api/devices` 可读）。**注意**：从旧版本升级后设备号会从 `android-pda-1` 变为自动码，Server 目录出现一台新设备、旧条目变离线残留，属预期。
- **保存并应用（免 force-stop）**：写 SharedPreferences 后自动重启宿主服务（同进程 stop/start），配置页等待本地 HTTP 就绪后刷新状态。
- **通知与状态页**：常驻通知点击打开配置页，文案随服务端连接 / 打印机端点状态刷新（5s 一帧、变化才重发）；内置浏览器页（`127.0.0.1:53970/`）收敛为轻量状态页（配置概览 + 打印机探测 + 测试打印）。
- **配置模型结构化**：`{ server_url, printer_brand, connection_type, tcp_host, tcp_port, device_name }`，旧装机 `tcp_host` 自动迁移；`GET/POST /api/host/config` DTO 扩展（TcpPort / PrinterBrand / ConnectionType / DeviceName，保留旧字段）。
- **移除 pc_host 测试模式（决策 #42 废止）**：`PcTemplateClient`、`/api/pc/*` 端点、`PcHostUrl` 配置项删除——状态页收敛后失去唯一消费方；WinHost `/api/logs` 接收端点保留，PDA 不再自动回传日志。
- **构建修复（决策 #95 ⑦）**：构建脚本改用 `-p:EmbedAssembliesIntoApk=true`——.NET Android 36.1.x 起 `AndroidFastDeployment` 属性失效，Debug 产物一度退化为 Fast Deployment 壳（纯 `adb install` 启动即 abort，真机冒烟时发现并修复）。
- **真机冒烟（UROVO DT50 / Android 11，全部通过）**：配置页渲染与旧配置迁移（server_url / tcp_host 读回）；设备号 `pda-b4ee90bd59b33c53` 自动生成、默认名 `PDA-3C53`；`/api/host/config` 新 DTO；状态页 HTML；打印机状态探测（在线 / 有纸 / 未暂停）；**测试打印真实 Zebra（192.168.2.121）物理出纸（1/1 Completed）**；「保存并应用」触发服务自动重启（ServiceRecord 重建验证）且 healthz 恢复；CORS 预检（OPTIONS 204 + 宽松头）。通知点击进配置页与 UI 视觉细节留待用户日常使用确认。
- **测试**：`dotnet build` 0 警告 0 错误；AndroidHost 构建 0 警告 0 错误；日常 `dotnet test` 328 项全绿（桌面零回归）。

## 迭代 25 Android PDA 宿主真机落地 · 2026-09-08

- **PDA 接入边界定界（决策 #93，用户拍板）**：AndroidHost 是 PDA 上唯一打印执行宿主；第三方 PDA 程序统一经既有 HTTP 公共契约集成——路由模式（Server `POST /api/jobs` + targetDeviceId）或直连模式（同机 `127.0.0.1:53970`，即 JS 桥）。零跨端契约变更；不做 Android SDK / Intent / AAR 新契约形态。
- **SQLitePCLRaw 原生库双坑修复（决策 #94）**：`lib.e_sqlite3.android` 定版 **2.1.11**（2.1.12/2.1.13 误装 glibc 构建 so，Android 装载即 LinkageError）；桌面版 `lib.e_sqlite3` 从 Core 下沉到各可执行项目自引（WinHost / Server / 各测试项目）——此前 Core 引用经 RID 回退图把桌面 glibc so 打进 APK、压过 android 包的 NDK so。桌面宿主行为零变化（328 项测试全绿）。
- **AndroidHost 修复**：启动装载 `e_sqlite3` 原生库（否则 SQLite 首开 DllNotFoundException，迭代 5 以来首次真机运行即崩）；本地 HTTP 修复 **POST 带体请求挂死**（StreamReader 预读吞掉请求体，改统一缓冲解析）；构建脚本关闭 Fast Deployment（默认 Debug 产物纯 `adb install` 后启动即 abort）。
- **AndroidHost 补齐（与 WinHost 同构）**：本地 HTTP 宽松 CORS + OPTIONS 预检（JS 桥）；`GET /api/jobs?limit=` 作业列表与 `POST /api/jobs/{id}/items/{index}/retry` 失败项重打端点；Server 轮询升级为 notify 20s 长轮询 + 独立 1s 回报循环（作业到达即领取）；分析器警告清零。
- **16KB 页构建级验证通过**：全部 arm64 so ELF 段对齐 ≥ 16KB + `zipalign -c -P 16` 通过 + 构建无 XA0141；运行时验证待 Android 15+ 设备（保留验收积压表）。
- **真机验收全通过（UROVO DT50 / Android 11）**：注册 / 心跳（notify 保活）、模板下发、作业打印（路由 + 直连双模式、批量 3 张、ZPL 字节级核对：^PW480/^LL320 精确换算 + ^GF 位图 QR 与中文内容可见）、离线恢复（Offline 判定 + Pending 暂存 + 恢复 7s 内领取）、断网重连（挂起 → resume 续打 → retry 补打 → 两端终态一致）、开机自启（重启 10s 内拉起）、前台服务保活。物理出纸以 TCP9100 模拟打印机验证，真实 IP 打印机出纸保留验收积压。
- **遗留（DESIGN 未决）**：宿主重启后本地↔Server 作业映射（内存态）丢失，Server 侧已 Claimed 作业无终态——全宿主既有语义，需跨端方案再立项。
- **测试**：`dotnet build` 0 警告 0 错误；日常 `dotnet test` 328 项全绿。
- **追加（同日真打印机联调）**：本地 HTTP 新增 `GET/POST /api/host/config`（tcpHost / serverUrl / deviceId / pcHost 读写，持久化 SharedPreferences，重启宿主生效）——补齐 WinHost 同构端点，第三方程序 / 联调可免 adb 改配置；真 Zebra 打印机（TCP9100）实测：PDA 侧状态探测在线 / 有纸 / 未暂停，直连作业渲染出纸链路 Completed（物理出纸由用户现场确认）。

## v0.23.0 迭代 36-39 汇总发布 · 2026-09-08

- **打包范围**：文档治理与路线图收口（迭代 36）、服务端暂存作业 TTL 过期 + notify 积压即时唤醒（迭代 37）、测试稳定性治理（迭代 38）、性能优化批次（迭代 39）；详见各迭代条目。
- **服务端行为变更（部署注意）**：Pending 作业 TTL 默认开启（12 小时；`Server.PendingJobTtlHours` / `LABELFRAME_SERVER_PENDING_TTL_HOURS`，0 或负值 = 关闭，行为回到「不设过期」）——设备长期离线期间暂存的作业超期进入新终态 **Expired**，幂等语义保持严格（同一 requestId 重放只返回既有作业，重打需新 requestId）；notify 挂起前积压预检消除纯积压清空场景的 20s 长轮询空等。
- **性能优化**：客户端提交到出纸系统侧 p50 205ms → 9ms（Worker 信号量唤醒）；大批量打印每张托管分配 2.3MB → 0.84MB（SKBitmap 池化）；服务端领取路径写事务合批（Touch + Claim 合一）降低高并发锁竞争。作业 / 路由 / 打印行为零变化。
- **版本同步**：`/api/server/info` 版本与稳定版 Compose 默认版本更新为 `0.23.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.23.0` / `labelframe-client:0.23.0`（均含 `latest`）。

## 迭代 39 性能优化批次（Worker 信号量唤醒 / SQLite 写合批 / SKBitmap 池）· 2026-09-07

- **Worker 信号量唤醒（决策 #92①）**：`LabelJobQueue` 新增「出现新待打项」唤醒信号——新提交 / 恢复 / 失败项重打 / 启动恢复中断四条路径在存储写入提交后发信号（先信号后提交会让 Worker 探测落空且信号被消费，错过唤醒）；`JobPrintWorker` 空转等待由 200ms 轮询改为信号即时唤醒（5s 超时兜底防信号遗漏，正常路径不触发），「EXISTS 轻量探测 → 完整领取」结构、批次节流（TimeProvider 注入保留）、挂起恢复语义零变化；「探测有 Pending 但领取落空」（挂起作业等不可领场景）保留 200ms 周期且同样可被信号提前唤醒。实测：单张全链路 **p50 205ms → 9ms**（p99 79ms 为首张预热），Perf 阈值收紧为 `p50 < 20ms` + `p99 < 500ms`。
- **SQLite 写事务合批——评估 + 实施一项（决策 #92②）**：评估结论记 DESIGN——提交（INSERT OR IGNORE + UNIQUE 兜底）与回报已是单写事务最细粒度，notify 心跳语义独立保持，busy_timeout 5s 为排队上限（调小变 SQLITE_BUSY 错误、调大延长尾部，均无收益）；**架构级合批（写队列串行化 / 提交缓冲 / 换存储）明确不做**——当前局域网规模（≤20 设备）p50 恒 3-4ms 不受影响、无错误无丢失，尾部排队是 SQLite 单写者可预期特征。实施：领取路径「Touch 心跳 + Claim 圈定」两个自动提交写事务合并为一个显式事务（`ServerDb.TouchAndClaimPendingJobsAsync`），20 设备并发每轮领取少一次单写锁排队——本机 ~39% CPU 负载 A/B：无合批 p95 3741ms → 合批 2954-3422ms；回报路径顺带移除 UPDATE 受影响后的冗余 id 回读。
- **SKBitmap 池化（决策 #92③）**：`SkiaLabelRenderer` 整版渲染中间态（SKBitmap 像素内存 + 托管像素暂存 byte[]）按「尺寸 + 暂存长度」匹配池化复用（池上限 4、lock 保护），租用后 Clear 白底全量重置保证与上一张内容无关；输出 LabelBitmap / PNG 始终新分配，对外 API 与渲染结果不变。bench 实测：**每张托管分配降 61%-67%**（整链路 60×40@203 ~990KB → 390KB；100×60@300 ~4.9MB → 1.61MB），批量均摊 2.3MB/张 → **0.84MB/张**（200 张 458MB → 165MB），Gen2 高频回收显著缓解。
- **基线更新**：`docs/PERF-BASELINE.md` 升级 v2——三项优化后新数据 + v1 旧值对照 + 遗留优化机会清单收敛（Worker 唤醒 / SKBitmap 池已了结；架构级写合批记不做）。
- **测试**：新增 Core 单测 3 项（提交即唤醒、幂等重放不发信号、恢复与失败项重打唤醒——确定性等待语义，无睡眠断言）；既有 FakeTimeProvider 批次节流测试零改动通过；Server 领取路径由既有集成测试覆盖（未注册设备 / TTL 过滤 / 领取不重复全绿）。
- **本地验证**：`dotnet build` 0 警告 0 错误；日常 `dotnet test` 328 项全绿（Core 108→111）；perf：WinHost 通过新阈值（p50=9ms）、Server 1/5 设备通过（20 设备在本机 ~39% CPU 负载下两臂同超 50ms 阈值——DESIGN §6 既有环境敏感特征、A/B 证实非本次回归，nightly 隔离把关）；soak 5 分钟通过（0 错误 / WAL 有界 / 托管堆稳定）；bench 全套通过。行为零变化：作业 / 路由 / 打印既有测试全部原样通过。
- **范围说明**：不改发布 / CI 工作流；不推 tag；AndroidHost 与跨端契约不在范围。

## 迭代 38 测试稳定性小治理 · 2026-09-07

- **flaky 根因核实与加固**：ci run `34081028327` 的 `DataPrint.test.tsx` 会话保留用例失败定位到 `findByDisplayValue('A-01')` 行——等待语义本身无误（已是 `findBy`），超因是 testing library 默认 1000ms 超时不足：DataPrint 挂载需串行走完「设备探测 → 模板列表 → 模板详情 → testData 预填」多段异步链，CI 高负载（多 worker CPU 争抢）下整链偶发被拖过默认超时。`DataPrint.test.tsx` / `DataPrint.server.test.tsx` 中守卫该挂载链（含页面卸载重挂后的整链重跑）的 `findBy*` / `waitFor` 统一显式放宽到 3000ms（`MOUNT_WAIT` 常量），仍在 vitest 5s 测试预算内；测试框架 / vitest 配置零变更（约定记入 DESIGN 决策 #91）。
- **同类模式排查（按范围仅加固、不批量重写）**：`web/src` 其余组件测试（Settings / JobHistory / Devices / PluginPackages / ClientPackages / App 双模式）均为「先 `findBy` 异步锚点、再同步断言」的正确模式，等待链为单段 fetch，未发现确有竞态风险的同步查询，维持现状。
- **本机构建产物清理**：删除 `src/LabelFrame.WinHost/bin/Debug/net10.0-windows/` 旧 TFM 遗留目录（bin 不入库，仅本机动作）；清理后 `dotnet run --project src/LabelFrame.WinHost -f net10.0-windows` 被 `NETSDK1005` 拒绝（提示该 TFM 不在 TargetFrameworks 内），不再静默运行月龄旧代码（此前联调会表现为端点大面积 404）。风险提示与「使用完整 TFM 名」约定记入 DESIGN「兼容性」。
- **收尾补记（同日，用户要求）——全仓旧 TFM 残留清理**：迭代收尾后按 csproj 现行 TFM 集合清理各项目 bin/obj（Debug / Release）下全部非现行目录——`net8.0`（Core / Core.Tests / Server）、`net8.0-windows` 与旧 `net10.0-windows`（WinHost / WinHost.Tests，后者含验证命令预建的半成品）、Rendering 旧多目标 `net10.0-windows`（Debug / Release）、Server.Tests 旧 `net10.0`，以及已移除项目 `test/LabelFrame.Studio.Tests` 的整目录构建残留（项目本体已随 a61fe60 删除，git 零跟踪）；清理后 `dotnet build` 0 警告 0 错误，git 工作区零变更（均为 gitignore 本机动作）。
- **范围说明**：不改产品代码行为、不改测试框架 / vitest 配置、不动发布 / CI 工作流、不推 tag；范围外发现的其余旧 TFM 残留在迭代收尾后按用户要求一并清理（见收尾补记）。
- **本地验证**：`dotnet build` 0 警告 0 错误；日常 `dotnet test` 325 项全绿；web client / server 双模式各连跑 3 轮（共 6 轮 × 247 项）全绿；lint 通过（既有 6 条 warning，0 errors）。

## 迭代 37 服务端暂存作业 TTL 过期 + notify 积压即时唤醒 · 2026-09-07

- **作业模型契约变更（文档先行）**：服务端作业新增终态 **Expired**（过期未投递）——设备离线期间暂存的 Pending 作业超过 TTL 视为「目标不可达」主动放弃，修订决策 #22 的「不设过期」（新决策 #89 / #90 记入 DESIGN）。TTL 只对 Pending 计龄（Claimed / Completed / Failed 豁免，领取后归客户端本地队列管理），过期判定只以服务端时钟为准；幂等语义保持严格——同一 requestId 重放只返回既有 Expired 作业、不重新投递，业务系统重打必须用新 requestId 重发。
- **服务端配置**：`Server.PendingJobTtlHours`（默认 12 小时；0 或负值 = 关闭过期，行为与现状一致）与 `Server.ExpirationScanIntervalMinutes`（默认 5 分钟）；均支持 appsettings 与 `LABELFRAME_SERVER_PENDING_TTL_HOURS` / `LABELFRAME_SERVER_EXPIRATION_SCAN_MINUTES` 环境变量覆盖。
- **领取过滤（正确性兜底）**：设备领取查询按「CreatedAt + TTL」过滤，超期作业一律不下发，不依赖过期扫描周期；notify 积压预检与领取过滤共用同一判定。
- **后台过期扫描**：新增 `PendingJobExpirationService`（与 DataCleanupService 分离的独立 BackgroundService——过期是状态转移需分钟级及时可见，清理是删除历史数据为小时级周期），把超期 Pending 批量标记 Expired 并写入中文失败原因；Expired 作业随既有 30 天历史清理回收。ServerService 时间取值统一到可注入 TimeProvider（测试 FakeTimeProvider 确定性驱动）。
- **notify 积压即时唤醒**：`GET /api/devices/{id}/jobs/notify` 挂起等待前先查一次该设备未过期 Pending 作业，有则立即返回 hasPending=true——消除纯积压清空场景每批 20 秒的长轮询空等（清空吞吐原被钉在约 30 个作业/分钟）。
- **前端**：作业历史页（与数据打印页同一状态映射）新增 Expired =「已过期」中文标签与中性终态徽标，失败原因列透出服务端放弃说明；不新增操作按钮，客户端零改动。
- **测试**：新增 10 项——TTL 单元（FakeTimeProvider：超期不领取 / 未超期照常领取 / Claimed 豁免 / 扫描只标记超期 / TTL 关闭无操作 / 配置默认值与环境变量）+ 完整宿主集成（超期作业在扫描未运行时仍被领取过滤拦截的双保险、扫描标记 Expired、requestId 重放返回 Expired 且不重新投递、notify 有积压立即返回）+ TTL 关闭（0）回归锚点类（超龄 Pending 照常下发，行为与现状完全一致）。
- **本地验证**：Debug / Release 构建 0 警告 0 错误；日常 .NET 测试 325 项全绿（Server 49→59）；web client / server 双模式各 247 项全绿，lint（既有 6 条 warning）与双构建通过。

## 迭代 36 文档治理与路线图收口 · 2026-09-07

- **路线图决定落地（2026-09-07 用户拍板，只改文档）**：迭代 26（Niimbot 蓝牙传输插件）标记「已放弃」（PDA 宿主走 IP 打印、无蓝牙承接需求；传输插件机制保留，有真实蓝牙打印机需求时按插件另行立项），ACCEPTANCE-BACKLOG §3 真机验收项随之取消；迭代 25（Android PDA 宿主）由「延后」改为「下一轮」，蓝牙打印移出其范围；需求 P1「PDA 蓝牙传输」降级至 P2 / 待需求（PDA 宿主本身走 IP 打印、不依赖蓝牙）。
- **Code128 中文校验决策不做**：Code 128 字符集仅 ASCII，中文值在渲染 / 编码层按编码异常拒绝（作业项 Failed + `LF_ENC_001` + 原因）即为正确语义，不加提交前专门校验（DESIGN「暂不做」已记录）——了结 v0.22.1 遗留的「作为独立校验问题后续处理」悬空承诺；历史 CHANGELOG 条目保持原样。
- **文档与代码一致性核对**：`docs/` 根 8 份活跃文档对照 `src/` 逐项核对 API 端点 / 错误码 / 配置与环境变量 / 默认端口（53960 / 53961 / 53970）/ 数据目录 / 作业状态机，主体一致；修正文档滞后项——DESIGN 术语表「编码器 / 传输」（矢量 ZPL 早已删除、蓝牙降级）、架构图 PDA 节点、错误码家族补 `LF_ENC` / `LF_TRANSPORT` / `LF_PLUGIN`；REQUIREMENTS 底线「中文渲染」机制表述；TEST-MATRIX（Niimbot / PDA 行、示例版本 0.22.2）；LINUX-CLIENT-E2E 示例版本；PERF-BASELINE Niimbot 展望；archive README 现行文档清单；CONTRIBUTING 测试命令改日常过滤口径。未发现代码偏离设计契约的问题。
- **注释清理（无行为变更）**：修正 7 处过时 / 失效注释（失效的规格 §9 / 契约 §4 / DESIGN 旧章节 / 迭代号引用；`LabelTextElement` 字体字段与 `SkiaLabelRenderer` 摘要按现行图片打印语义重写）。
- **过程性文档盘点**：`docs/` 根与 `docs/archive/` 无需归档移动；`artifacts/` / `outputs/` / `BenchmarkDotNet.Artifacts/` 已确认 gitignore 且未跟踪。
- **本地验证**：`dotnet build` 0 警告 0 错误；日常 `dotnet test` 315 项全绿。

## v0.22.2 Linux 容器中文字体切换 · 2026-09-07

- **中文字体基线**：Server Ubuntu/Docker 镜像与 Linux Log Client 镜像的预装中文字体从 `fonts-noto-cjk` 切换为 `fonts-wqy-microhei`，中文字符首选匹配 `WenQuanYi Micro Hei Regular`。
- **版本同步**：`/api/server/info` 的服务端版本同步为 `0.22.2`；稳定版 Compose 默认版本更新为 `0.22.2`。
- **范围说明**：不改变模板包、打印 API、作业模型或客户端打印链路；Windows 客户端实际打印仍使用客户端本机字体环境。
- **本地验证**：Release 构建 0 警告 / 0 错误；日常 .NET 测试 315 项全绿；本地候选 Server 与 Linux Client 镜像的中文字符首选均匹配 `WenQuanYi Micro Hei`，Server 中文文本出图预览返回 `HTTP 200`，PNG 11575 字节。

## v0.22.1 Server Docker 中文字体补丁 · 2026-09-03

- **Server 镜像中文字体**：Ubuntu / Docker Server 镜像默认安装 `fontconfig` 与 `fonts-noto-cjk`，并在构建时刷新字体缓存；服务端管理界面的模板预览 / 出图预览在 Linux 容器中具备中文文本渲染基线。
- **版本同步**：`/api/server/info` 的服务端版本同步为 `0.22.1`；稳定版 Compose 默认版本更新为 `0.22.1`。
- **范围说明**：不改变模板包、打印 API、作业模型或客户端打印链路；Code128 条码绑定中文导致的编码异常作为独立校验问题后续处理。
- **本地验证**：Release 构建 0 警告 / 0 错误；日常 .NET 测试 315 项全绿；本地候选 Server 镜像可见 `Noto Sans CJK SC`，中文文本框出图预览返回 `HTTP 200`，PNG 6653 字节。

## 迭代 35 Linux Client 正式发布 + P0/P1 测试补强 · 2026-08-28

- **双镜像发布**：新增正式 `ghcr.io/marci-labs/labelframe-client`（`linux/amd64`、仅 `log`）与只引用同版本镜像的 `compose.release.yaml`；Server / Client 候选各构建一次，Compose E2E 通过后直接推送原镜像的版本号与 `latest` 标签，避免验收制品与发布制品漂移。Server 发布镜像携带当前管理界面，但继续由配置决定是否启用。
- **P0/P1 发布门禁**：E2E 扩展到 Linux 能力裁剪、模板详情 / 列表 / 预览 / 包导入导出、Excel 生成上传、日志回传、幂等、单张 / 3 张、离线暂存、Client 重启持久化与重启后继续领取；逐张复制真实容器 PNG，检查非空白并解码 Code128。精确 `0.22.0` 本地候选双镜像已通过完整组合。
- **测试治理**：新增 `docs/TEST-MATRIX.md`，按 P0/P1/P2 记录功能、自动化层级、发布门禁与真机欠账；Release 与日常 CI 统一排除 Perf / Soak，由 nightly 独立负责性能与稳态判定。
- **Excel 续填实证**：真实下载模板的命名区域仍为 `A1:A2`，在其下续填 A3:A5 后由 Server 管理界面完整识别 4 行，关闭 TemplateFrame.Excel.Simple 2.0.0 升级后的人工冒烟欠账。
- **回归修复**：WinHost 多目标编译后，Windows MSI 发布脚本显式选择 `net10.0-windows10.0.26100`，恢复 Windows 发布与 MSI 流水线。
- **跨平台门禁修复**：独立 PNG 校验器补齐 Linux 原生 Skia 资产，确保 Release 的 Ubuntu runner 能解码候选镜像产出的 PNG；首次 `v0.22.0` 工作流在镜像推送前准确阻断了该工具链缺口。
- **正式发布**：`v0.22.0` Release 全绿，上传 Client / Server MSI、Server Web UI zip 与 Linux Server 归档；同一次候选 E2E 验收后的 Server / Client `0.22.0` 与 `latest` 镜像已推送 GHCR。发布后重新 pull 双镜像并以全新卷运行稳定 Compose，公共端点、能力边界、设备路由、离线暂存、重启持久化及 6 张 PNG 解码全部通过。
- **本地验证**：Release build 0 警告 / 0 错误；日常 .NET 315 项全绿；web client / server 各 246 项全绿，lint（保留既有 6 条 warning）与双构建通过；源码 Compose 与精确版本候选 Compose E2E 均通过。

## 迭代 34 Linux 无头客户端（Log 驱动）+ Docker Compose E2E · 2026-08-28

- **Linux Client Host**：`LabelFrame.WinHost` 增加 `net10.0` 目标并产出 `LabelFrame.ClientHost`；复用队列、Server 路由、Skia 渲染和批次 Worker，Linux 固定只暴露 `log`，不加载 Windows 驱动、Zebra SDK、外部插件、Web UI、浏览器或托盘。
- **容器化测试环境**：新增 Linux Client 多阶段镜像、稳定版 Server `0.21.0` + 当前 UI / Client 候选的 Compose，以及一键 E2E 脚本；数据与 PNG 使用命名卷，默认管理地址 `http://127.0.0.1:53910`。
- **闭环验证**：设备注册 / 心跳、单张与 3 张作业、客户端领取与终态回报、Log PNG 数量、重启恢复全部通过；浏览器在设计器实际新建并保存模板，再完成数据填写、在线设备选择、单张打印与作业历史，控制台无 warning / error。
- **严格补证**：新增 `LabelFrame.PrintImageVerifier`，E2E 从 Client 命名卷直接复制每个 Item 的 PNG，检查非空白并逐张解码预期 Code128；Client 重启后先断言旧作业仍在，再提交新作业并验证领取、出图与终态回报，补齐此前仅验证重新在线的缺口。
- **联调修复**：Log PNG 输出目录改为可配置且作业 API 返回同一目录 / 正确数量；环境变量 ServerUrl 优先于持久化配置；SQLite provider 初始化改为真正线程安全；Server 完整宿主测试按进程环境变量约束串行，消除数据库路径互相覆盖。
- **验证**：Release build 0 警告 / 0 错误；日常 .NET 315 项全绿，Server 5 分钟 soak 通过；web client / server 各 246 项全绿，lint 与双构建通过；Docker / 浏览器 E2E 通过。严格补证轮次中自动化 5 张 Compose PNG 与浏览器作业 PNG 均直接解码成功，重启后新作业 `Completed`。Perf 在低干扰轮次通过，但宿主 CPU 41%–46% 时既有延迟门槛会抖动失败，未降低阈值，风险记入 DESIGN。



## 依赖升级：TemplateFrame.Excel.Simple 1.0.5 → 2.0.0 · 2026-08-25

- **修复 Excel 模板续填行导入丢失**：1.0.5 的 `SimpleExcel.Read` 以命名区域 EndRow 为边界——我们生成的模板命名区域只覆盖「表头 + 示例行」，用户在下方续填的行不扩展区域、导入时被静默丢弃；2.0（含 1.0.7 读取容错修复）数据区顺延到工作表最后一行，续填行完整读入。第三方（WPS / Excel 生成）文件兼容性同步改善（共享字符串表头 / 富文本 / 缺 RowIndex 等修复）。
- **破坏性变更对照无影响**：项目仅用 Read / Write / SimpleExcelTable / SimpleExcelOptions 四个 API，2.0 签名不变、零代码改动；`Read` 损坏流异常统一为 `InvalidOperationException` + 本地化中文消息（导入端点 catch Exception，行为兼容且报错更友好）；基础包层面的破坏性变更（MissingElementPolicy 迁移 / 删除 6 类型等）未使用。
- 变更范围：`Core` 与 `Studio` 两个 csproj 版本号；dotnet build 0 错误、dotnet test 315 全绿；2.0.0 已发布 nuget.org，CI 可正常还原。

## 迭代 33 性能 / 稳定性测试（微基准 / 端到端延迟 / soak / nightly CI）· 2026-08-25

- **微基准**（test/LabelFrame.Benchmarks，BenchmarkDotNet + MemoryDiagnoser）：整链路单张 0.7-3.4ms、分配 1-5MB（面积线性）；50 张批量 36-170ms 远低于 1 秒预算。
- **端到端延迟**（Trait=Perf，`scripts/run-perf.ps1 perf`）：Server 路由 p50 恒 3-4ms，**发现 SQLite 单写者在 20 并发设备下的 p95 尾部特征**（写事务排队 2-3s，不丢不错）——分层阈值；WinHost 单张 p50 ≈ 205ms，**发现延迟主体是 Worker 200ms 空转轮询**（渲染仅 ~1ms），信号量唤醒记为优化机会。
- **稳定性 soak**（Trait=Soak，默认 5 分钟 / LF_SOAK_MINUTES 可调）：混合负载 0 错误、WAL 有界（显式声明 wal_autocheckpoint=1000）、托管堆稳定。
- **CI**：nightly-perf.yml 每周一 04:00 UTC（Perf + 15 分钟 soak + 基准）；日常 ci.yml 不含 Perf/Soak。
- **文档**：docs/PERF-BASELINE.md 首份基线 + 三个优化机会；dotnet test 314 全绿。

## 迭代 32 测试完善（端点集成 / 渲染元素 / TimeProvider 确定性 / 设计器组件 / MSI 断言 CI）· 2026-08-25

- **WinHost 端点集成测试 +10**：WinHostApp 装配抽取（与生产一致，托盘/浏览器/退出留 Main）；HostOptions 新增 ConnectionPath（测试隔离本机连接配置——实际抓到测试读到开发者本机 zebra 配置的缺陷）；覆盖 transport / jobs 全生命周期 / host config 回环 / 插件安装卸载等八组端点。
- **渲染器元素测试 +6**：图片 / 线 / 区域三种此前零测试的元素；发现 RegionHAlign 对自动宽度文本失效（DESIGN 风险，分轴断言绕过）。
- **TimeProvider 确定性（节流测试重写）**：JobPrintWorker Task.Delay 全经 TimeProvider；FakeTimeProvider 驱动消除 300 秒真实等待；移除测试程序集并行关闭——WinHost 套件 17s→2s。
- **设计器组件测试 +27**：PropsPanel/SidePanel 真实渲染（三类元素字段集互斥、onChange 回调、图层选中与层级、双面板主链 harness）。
- **MSI 断言进 CI**：ci.yml msi-verify job——安装包 UI 契约（向导 / 中文 / 选项页 / Run 键）回归提交即发现。
- dotnet test 314 全绿（298→314）；pnpm test 双模式 246 全绿；测试评审结论与基准来源见 ROADMAP 迭代 32。

## v0.21.0 发布（迭代 27-31：工程治理 + Excel 2.0 + 安装向导 UI）· 2026-08-25

- **安装体验**（迭代 31）：WixUI 全中文品牌向导（欢迎 / 许可 / 安装目录 / 完成）+ 品牌位图 + 完成页「立即打开」复选框 + Client「安装选项」页开机自启（默认勾选，--autostart 托盘模式）；控制面板元数据补全。
- **工程治理**（迭代 27-30）：日常 CI（push/PR）；LabelFrame.Api 共享契约库（两端端点/DTO 去重，修复预览 DPI / 错误码漂移）；数据层并发修复（领取原子化 / N+1 / Worker 轮询轻量化 / TransportManager 加锁）+ SQLite WAL；C# 分析器门禁（latest-recommended + 警告即错误）；Server 端点集成测试（发现并修复宿主启动即崩与 403 变 500 两个真 bug）；覆盖率收集；Studio（WPF）与原型移除；文档体系重组（README 三类角色快速开始 / DEPLOY / 归档）。
- **依赖升级**：TemplateFrame.Excel.Simple 1.0.5 → 2.0.0——修复「下载 Excel 模板下方续填行导入丢失」。
- dotnet test 298 全绿；`dotnet build` 0 警告（分析器全开）。

## 迭代 31 安装包 UI 专业化（WixUI 向导 / 品牌位图 / ARP 元数据）· 2026-08-25

- **安装向导**：Client / Server MSI 接入 WixUI_InstallDir——欢迎页（品牌左幅）→ 中文许可协议 → 安装目录（默认 Program Files，可选）→ 安装进度 → 完成页；此前双击 MSI 只有 Windows Installer 默认裸进度窗。
- **品牌资产**：新增 generate-installer-branding.ps1 生成 493×312 欢迎 / 完成页背景与 493×58 内页横幅（主蓝 + L 标，与应用图标同体系）；generate-license-rtf.py 生成中文许可 RTF。
- **修复隐患**：接入 WixUI 后 ExecuteAction 固定 1300，原卸载清数据对话框（1350）会晚于执行阶段弹出导致勾选失效——移至 1290；运行时缺失提示提前至向导之前（1150）。清理动作改为 `[INSTALLFOLDER]` 目录无关（支持自定义安装目录）+ 尽力语义（缺失文件不卡卸载）。
- **其他**：控制面板元数据（产品图标 / 帮助链接）补全；Server 自定义完成小窗由品牌化 ExitDialog 取代；Client 保留「立即打开」TopMost 完成弹窗；新增 verify-msi-ui.ps1 结构校验（COM 校验对话框表 / 序列 / 品牌二进制，关键断言）；release.yml 安装 WixToolset.UI.wixext。
- **视觉验收反馈修正**：① 许可页标题混入「微软雅黑」——RTF 字体表不解析 Unicode 转义序列，字体名改用 ASCII 英文名 Microsoft YaHei（fcharset134 声明中文字符集）重新生成；② 「立即打开」独立 TopMost 弹窗取消——并入向导完成页可选复选框（默认勾选，Finish 按钮按条件 DoAction 异步启动客户端），应用侧 --install-finished 模式与 InstallFinishedPrompt 一并移除。
- **复选框色差修正**：MSI 的 CheckBox 控件不支持透明、以系统按钮面色（240,240,240）绘制自身背景——位图底色由近白（250,251,253）改为精确按钮面色，完成页复选框与周围无缝融合。
- **向导全量中文化**：构建加 `-culture zh-cn`（WixUI 扩展内置中文资源）——欢迎 / 许可 / 目录 / 进度 / 完成各页按钮与引导文字全部中文（下一步(&N)、上一步(&B)、取消……）。
- **开机自启选项（Client）**：新增「安装选项」页（目录页与确认页之间）——「开机自动启动 LabelFrame」复选框默认勾选；勾选时写 HKLM Run 键，以 `--autostart` 参数启动（托盘常驻、不自动打开浏览器，WinHost 新增该参数支持）；未勾选则不写。Server 无需选项（Windows 服务本身开机自启）。
- 本地构建 Client / Server 0.21.0 MSI 断言全部通过；视觉验收（真实安装走一遍向导）待执行（ACCEPTANCE-BACKLOG §6）。

## 迭代 30 注释与文档清理（代码去过程化 / DESIGN 重构）· 2026-08-25

- **代码注释去过程化**（60+ 文件约 165 处）：迭代号 / 决策号 / 章节引用等过程标记与历史叙述移除，注释只保留「是什么 / 为什么」；面向用户的错误消息去掉内部代号；标点残渣校验为零。
- **DESIGN.md 重构**：章节顺序修正（4→6→5 → 1-6）；原型十轮过程笔记归档 docs/archive/DESIGN-PROTOTYPE-NOTES.md；API 契约刷新为现状（Server / WinHost 完整端点分组 + 错误响应约定）；「风险与未决问题」重写为仅现存项（已解决条目删除），全文 212→201 行。
- DEPLOY / README / ACCEPTANCE-BACKLOG 过程引用清理。
- **收尾体检修复**：移除误提交的本地覆盖率报告（TestResults 入 gitignore）；README Rendering 行去掉已删除的 GDI 预览表述；src/LabelFrame.Studio 磁盘残渣删除；web/README 重写（去 Vite 模板样板与历史叙述）；DESIGN 决策表 3 处单元格内未转义管道符修复（表格渲染破坏）。
- dotnet build 0 警告 0 错误；dotnet test 298 全绿。

## 迭代 29 程序优化（SQLite WAL / 数据层基建 / 分析器门禁 / Server 集成测试 / 覆盖率收集）· 2026-08-25

- **SQLite WAL 全库启用**（决策 #80）：读写并发不再互相阻塞（Server 长轮询 / 心跳 / 提交并发受益）；四存储（jobs / templates / logs / server）基建收拢公共 LabelFrame.Core.Data.SqliteSupport，Server 不再复制 provider 初始化；设备心跳合并单条 UPDATE。
- **C# 分析器门禁**：AnalysisLevel=latest-recommended + 警告即错误（AndroidHost 实验性除外）；清零真修——ZPL 输出与端口参数固定 InvariantCulture（区域无关）、LabelJobQueue / ServerService / TransportManager 实现 IDisposable、LoggerMessage 源生成、Serilog formatProvider、传输插件接口参数名对齐；豁免（CA2007 / CA1031 / CA1848 / CA1873 / 测试目录 CA1707 / CA1861）在 .editorconfig 注明理由。
- **死代码清理**：LabelPreviewRenderer（GDI 预览，迭代 27 后零生产消费者）与其测试移除；Rendering 双 TFM 收敛单 net10.0、移除 System.Drawing.Common。
- **Server 端点集成测试**（WebApplicationFactory 全链路：注册 → 提交 → 幂等 → 领取 → 回报 → 403/404），**发现并修复两个真实缺陷**：① 无参 UseExceptionHandler() 缺 AddProblemDetails() 配套——宿主启动即崩（单元测试不拉管道未暴露）；② Results.Forbid() 依赖未注册的认证设施——运行时 500 而非 403（两宿主 4 处改显式 403）。
- **覆盖率收集**：coverlet.collector + CI artifact（不设门禁）；首份基线 Server 88% / Api 63% / WinHost 59% / Core 49% / Rendering 31%（类级均值）。
- dotnet build 0 警告 0 错误（分析器全开）；dotnet test 298 全绿。

## 迭代 28 工程治理 P1/P2（文档归档 / 死重移除 / 数据层并发 / 异常契约 / Program 拆分 / 集成测试 / 安全边界）· 2026-08-25

- **P1 文档**：16 份历史迭代文档归档 docs/archive（核心四件套 + DEPLOY + ACCEPTANCE-BACKLOG）；ROADMAP 章节与总览表按迭代号排序、陈旧状态对齐（12 处标题）；验收清欠清单落地（大部分验收 2026-08-17 已通过，欠账仅 5 项，见 docs/ACCEPTANCE-BACKLOG.md）。
- **P1 移除死重**：Studio（WPF，冻结存档可找回）与其测试、prototypes/web-designer 移除；AndroidHost README 实验性警示；ClientPackages / PluginPackages 抽共享 FilePackageService 基类。
- **P2 数据层（决策 #77）**：服务端领取原子化（UPDATE...RETURNING，多实例不重复领取）；列表 / 回报同连接加载消除 N+1；ServerService 全局信号量收窄到提交路径（多设备并发不再排队）；打印 Worker 空转 EXISTS 轻量探测（空闲不再每 200ms 全量加载作业）；TransportManager 读写加锁。
- **P2 异常契约（决策 #78）**：两宿主共享全局异常处理（未捕获异常 500 + LF_INTERNAL_001，不透堆栈 / 路径）；上传端点确定性错误 400、意外故障 500（原一律 400 且透出 ex.Message）；render 端点 base64 非法 400（原裸 500）。
- **P2 结构**：Program.cs 拆分——WinHost Main 888→372 行、Server 344→141 行，端点按域分组入 Api/ 目录。
- **P2 测试**：新增 LabelFrame.Api.Tests（TestServer HTTP 集成，10 用例）覆盖共享端点层，含预览 DPI / 错误码 / base64 / Excel 回环回归锚点。
- **P2 安全（决策 #79）**：局域网信任模型显式化（攻击面 / 缓解 / 升级触发条件：插件包签名优先于 API 鉴权）；插件包不加下载哈希（三层校验已覆盖损坏，同信道哈希对主动篡改无意义）。
- dotnet build 0 错误 0 警告；dotnet test 300 全绿（Studio 移除后基线 315→300）。

## 迭代 27 工程治理 P0（日常 CI + API 契约与端点去重 + 文档重组）· 2026-08-25

- **日常 CI（ci.yml）**：push master / PR 触发 dotnet 构建 / 测试 + 前端 lint / 双模式测试 / 构建（命令与发版流水线一致），主干回归提交即发现；同分支新推送自动取消旧运行；不改动发布流水线。
- **新增共享库 LabelFrame.Api**：Server / WinHost 重复的 9 个 DTO 与模板 / 调试出图 / Excel / 日志端点（约 250 行）收敛为共享实现（端点经 Options 传入各自错误码，对外错误码不变）；xlsx 文本解析下沉 Core（ExcelTableReader）；ApiErrorCodes 统一错误码注册表（替换 WinHost 端点魔法字符串）。
- **修复两宿主漂移缺陷**：WinHost 模板预览 DPI 硬编码 203 → 取宿主配置（原非 203 DPI 时预览与打印不一致）；预览统一 Skia 与打印同源（原 GDI 不同源）、无请求数据回退模板 testData；模板不存在错误码 LF_JOB_001（误用）→ LF_TPL_001（Server 保持 LF_SRV_006）；ErrorView 统一 Code / Message / FieldKey；render-image(s) 图片资源解析统一（base64 附带优先、按名回退模板库）。
- **文档重组**：README 重写（三类角色快速开始：文员单机 / 管理员多机 / 业务系统开发者；部署形态对照；216 → 约 110 行）；新增 docs/DEPLOY.md（MSI / Docker / Ubuntu / 管理界面插件 / 分发 / 签名 / 配置）。
- dotnet build 0 错误；dotnet test 315 全绿（Core 108 / Server 45 / Studio 25 / WinHost 137）。

## v0.20.2 验证发布 — 2026-08-18

- **流水线验证**：推送 0.20.2 tag 再次验证 GitHub Actions 发布流水线稳定（内容与 v0.20.1 相同，无功能变更）；全流程（测试 / 打包 / ghcr 镜像 / Release）通过，产物同上；ServerOptions 版本号同步 0.20.2。

## v0.20.1 验证发布 — 2026-08-18

- **流水线验证**：推送 0.20.1 tag 验证 GitHub Actions 发布流水线稳定（内容与 v0.20.0 相同，无功能变更）；全流程（测试 / 打包 / ghcr 镜像 / Release）通过，产物同上；ServerOptions 版本号同步 0.20.1。

## v0.20.0 发布（迭代 24：客户端批次作业 Batch Print）— 2026-08-18

- **自动发布**：推送 0.20.0 tag 触发 GitHub Actions——dotnet 全量测试 + 前端双模式测试/构建通过后，Server Docker 镜像推 ghcr.io（ghcr.io/marci-labs/labelframe-server:0.20.0 / latest），Server / Client MSI + 服务端 webui 插件 zip + Linux 归档上传 GitHub Release；ServerOptions 版本号同步 0.20.0（/api/server/info 与 tag 一致）。

## 迭代 24 完成（客户端批次作业 Batch Print）— 2026-08-18

- **迭代状态 ✅ 已完成**：前后端合入 master（67214c3）→ 端到端联调通过（附五）→ Serilog 日志命名待修项修复（pp-20260818.log）；dotnet build / dotnet test 315 全绿、web pnpm test 双模式 219×2 全绿。服务端零改动（跨端契约不变）；下一轮迭代 26（Niimbot 蓝牙打印机插件，顺延自本迭代）。

## 迭代 24 联调通过 + Serilog 日志命名修正 — 2026-08-18

- **端到端联调通过（附五）**：前后端合入 master（67214c3）后联调——API 契约（GET/POST /api/host/print-settings：默认值 / 保存即生效 / 400 校验 / Normalize）/ 设置页「打印批次」卡片（渲染 / 开关联动禁用 / 保存提示 / 旧 WinHost 404 降级）/ E2E（Server 100 张 → 批次 10/500ms → Serilog 100 条逐张日志 + 节流 9 次、批界间隔 ≈ 500ms → 终态 Completed）全部通过，前端零缺陷。
- **后端待修项已修复**：Serilog 日志文件名含字面 {Date}（产物 pp-{Date}20260818.log）——实证 Serilog.Sinks.File 5.0.0 下 {Date} 为字面量（「去掉 rollingInterval 保留 {Date}」同样不替换），已改为 pp-.log + RollingInterval.Day，实际产物 pp-20260818.log 按天滚动正常；§5.6 同步更正。

## 迭代 24 后端实施完成（WinHost）— 2026-08-18

- **PrintSettings（选项模型）与 PrintSettingsStore**：新增 PrintSettings（默认 关闭 / batchSize 10 / batchIntervalMs 500；读取 Normalize：缺失 / 损坏 / 越界统一回默认值——batchSize<1→10、batchIntervalMs<0→500、batchEnabled 非 bool→false；保存校验返回中文原因）+ PrintSettingsStore（%LOCALAPPDATA%\LabelFrame\print-settings.json，原子写：先写临时文件再替换，与 HostConfigStore 同模式）。
- **API**：新增 GET/POST /api/host/print-settings——POST 校验 batchSize≥1 / batchIntervalMs≥0（非法 400 + 中文原因）、仅回环可写（非回环 403）、保存即生效（更新内存单例，无需重启）；PrintSettings 单例注入 JobPrintWorker，读写 lock 保证跨线程可见性（评审 #8）。
- **JobPrintWorker 批次节流**：「发送前暂停（claim-then-delay）」——ClaimNextItemAsync 领取下一张后、SendAsync 前，若 nabled && sendsSinceBatch>0 && sendsSinceBatch%batchSize==0 先 wait Task.Delay(batchIntervalMs, stoppingToken)；发送成功、CompleteItemAsync 后计数 +1（内存态、跨作业全局累计、不持久化，重启清零）；判定抽为 BatchPrintPolicy.ShouldPauseBeforeSend 纯函数保证可测；本机与服务端作业统一生效；测试页直发不计入计数；队列 / 幂等 / 挂起恢复 / 重打语义零改动（LabelJobQueue / JobSubmissionService / ServerRoutingWorker / ServerService / RoutingJson 均未改）。
- **Serilog 文件日志**：WinHost 引入 Serilog.AspNetCore（传递依赖 Serilog.Sinks.File，无需单独引用）→ %LOCALAPPDATA%\LabelFrame\logs\app-20260818.log（app-.log + RollingInterval.Day、时间戳 + 级别），JobPrintWorker 逐张 ILogger 日志落盘带时间戳，供端到端冒烟断言批间间隔；host.log（hostLogWriter）通道不动，两套日志分开文件。
- **测试**：WinHost 新增 50 个（PrintSettings Normalize / 校验、PrintSettingsStore 兜底 / 原子写、API GET 兜底含越界 Normalize / POST 400 / 非回环 403、BatchPrintPolicy、Worker 节流集成 FakeTransport 时间序列——25 张/批 5 → 第 6/11/16/21 张前各停一次共 4 次、跨作业 5+5 → 第 5 张后 B 首张前等待一次、不足一批不等待、禁用无间隔）；dotnet build 0 错误、dotnet test 315 全绿（Core 108 / Server 45 / Studio 25 / WinHost 137）。
- **前端**（web 设置页「打印批次」卡片 + API client + 测试）由并行会话实施；端到端冒烟（Server 100 张 → 批次 10 / 500ms → Serilog 时间戳断言 ≈500ms → 终态 Completed）待两会话合并后执行。

## 迭代 24 设计定稿：客户端批次作业（Batch Print）— 2026-08-18

- **迭代主题调整（用户提出）**：迭代 24 改为「客户端批次作业（Batch Print）」（🔄 进行中）；Niimbot 蓝牙插件顺延至迭代 26；Android PDA 排期以 ROADMAP「延后至迭代 25」为准（DESIGN.md 两处已同步修正）。
- **设计方案定稿**：docs/archive/ITERATION-24-BATCH-DESIGN.md 经 hermes 两轮评审「可定稿」（第一轮 10 条意见 + 第二轮复核；附四落实两处 💡 建议：app-{Date}.log 滚动命名、§4.1 措辞）。
- **关键设计决策**：不拆作业、只在 JobPrintWorker 发送层节流；「发送前暂停（claim-then-delay）」统一语义（100 张/批 10/500ms = 9 次停顿 ≈ 4.5s，末批后不等待）；批次计数全局累计、内存态（重启清零）；设置读取 Normalize（缺失/损坏/越界回默认值）；WinHost 引入 Serilog 文件日志（app-{Date}.log，RollingInterval.Day）解决 ILogger 逐张日志不可见；测试页直发不计入批次计数；服务端零改动。
## 迭代 23 收尾：前端重做完成 + loadError 补充 + 0.19.0 打包 — 2026-08-17

- **前端重做完成（hermes，4dcdbce）**：按任务书 docs/archive/ITERATION-23-FRONTEND-TASK.md（含 hermes 评审附一 + 主 Agent 拍板附二）实施——`types.ts` / `client.ts` 插件包 API（serverApi.list/upload/delete/downloadPluginPackage + localApi.listInstalledPlugins/installPlugin/uninstallPlugin + pluginPackageDownloadUrl）；`pluginLimits.ts` 64MB 预检纯函数；Server UI「插件管理」页（invalid 红标+原因）；客户端设置「插件管理」卡片（可用插件四态 + 覆盖安装确认 + 已安装四态徽标 + manual 只读 + 旧版本 404 区分）；TabId 增 plugin-packages + puzzle 图标；web 212 用例全绿（双模式）。
- **loadError 结构化补充（前端评审三.1 拍板）**：`PluginDirectoryLoader.LoadWithErrors` 透出逐 DLL 失败原因 → `PluginInstaller.ListInstalled` 合并输出 `loadError`（package 按目录 / manual 按路径匹配）；端到端实证坏 DLL 包 loaded=false + loadError=Bad IL format.；dotnet 265 全绿。
- **0.19.0 本地测试包**：Client / Server MSI + `labelframe-server-webui-0.19.0.zip` + 示例 `.lfplugin`（合法 `labelframe-transport-sample-1.0.0.lfplugin` / 坏 DLL 演示包）打包验收；ServerOptions 版本号同步 0.19.0。
- **迭代 24 主题调整（用户提出）**：下一轮改为「Niimbot 蓝牙打印机传输插件实现 + 真机测试」（补需求 P1 蓝牙缺口）；精成打印机顺延。

## 迭代 23 客户端插件分发实施完成 — 2026-08-17

- **范围定稿（2026-08-17 用户拍板决策 1A-7A，规格 [docs/archive/ITERATION-23-SPEC.md](docs/archive/ITERATION-23-SPEC.md)）**：插件包上传服务端（zip + 根 manifest.json + 插件 DLL，后缀 `.lfplugin`；独立 `plugin-packages` 目录 + `/api/plugin-packages`）；客户端安装（设置页「插件管理」卡片浏览服务端可用插件 → 下载 → 三层校验 → 解压到 `plugins/<pluginId>/` → 重启生效）；客户端卸载（删目录 → 重启生效，热卸载仍不做）；包大小上限 64MB；不做签名（局域网无鉴权模型）；与「更新与安装包」UI 并列。
- **Core（插件包 + 加载演进）**：`Transport/Plugins/Package/`——`PluginPackageManifest`（pluginId/name/version 必填 + 可选 description/author/minHostVersion）+ `PluginPackageReader`（zip 读取：根 manifest 校验 / zip-slip 防护 / 安全解压）+ `PluginPackageLimits`（64MB）；`PluginDirectoryLoader` 演进——平铺 + 子目录扫描（`plugins/<pluginId>/` 安装包，决策 3A）+ **字节加载（LoadFromStream，不锁 Windows DLL）**；注册表 `RegisterExternal`（外部插件禁止覆盖内置插件 ID，决策 6A）；`PluginProbe`（安装预检：临时 collectible ALC 字节加载发现插件并核对 id）；`SafeFileName`（由 ClientPackagesService 提取共享的文件名规范化，防路径穿越）。
- **WinHost（插件安装 / 卸载 API）**：`PluginInstaller` 服务（安装三层校验 / 覆盖安装 / 卸载 / 已安装列表合并注册表 loaded 状态）+ `GET /api/plugins/installed`、`POST /api/plugins/install`（multipart）、`POST /api/plugins/uninstall`（失败统一 400 ErrorView）；Kestrel `MaxRequestBodySize` 64MB；卸载当前连接引用插件后重启回退默认连接（复用迭代 22 附五兜底）。
- **Server（插件包 API）**：`PluginPackagesService` + `GET/POST/GET/DELETE /api/plugin-packages`（列表含元数据与 valid/invalid 状态、上传即校验、路径穿越防护）；`ServerOptions.PluginPackagesPath` + `LABELFRAME_SERVER_PLUGIN_PACKAGES`；Kestrel 64MB；`docker-compose.yml` 挂载 `./plugin-packages`。
- **前端（待 hermes 独立实施）**：任务书 docs/archive/ITERATION-23-FRONTEND-TASK.md（契约 / 清单 / 待评估设计点）；主 Agent 代跑提交 9faae3a 已回滚（2026-08-17，协作方式按用户指定：前端由 hermes 独立评估与实施）。
- **测试**：dotnet 259 全绿（Core 104 / Server 45 / WinHost 85 / Studio 25，新增插件包读取 / zip-slip / 安全解压 / SafeFileName / 注册表防覆盖 / 加载器子目录 / PluginInstaller 安装卸载覆盖 / plugin-packages 上传删除路径穿越大小上限）；web 207 全绿（22 文件，+28 用例）。
- **端到端联调冒烟通过（16 步）**：上传 .lfplugin → 服务端列表元数据 → 客户端安装（重启前 loaded=false）→ 重启后装配 / loaded=true → 配置启用（SAMPLE(SMOKE)）→ 卸载成功（**字节加载修复 Windows 文件锁**，决策 #73）→ 重启后消失 → 卸载当前连接插件后重启回退 log（附五兜底）→ Server 删除插件包。
- **关键决策（DESIGN #72/#73）**：插件包分发闭环（格式 / 目录 / 校验 / 覆盖 / UI 并列）；外部插件字节加载（`LoadFromStream` 不锁文件，「卸载 = 删除文件 + 重启生效」在 Windows 下真正可用；插件 `Assembly.Location` 为空，自定位资源改用上下文数据目录）。
- **loadError 结构化补充（2026-08-17 前端评审三.1 拍板）**：`PluginDirectoryLoader.LoadWithErrors` 透出逐 DLL 失败原因 → `PluginInstaller` 合并到 `/api/plugins/installed.loadError`；前端「加载失败 err + 原因」真实可触发（端到端：坏 DLL 包 loaded=false + loadError=Bad IL format.）；dotnet 265 全绿（Core 108 / Server 45 / WinHost 87 / Studio 25）。
## 迭代 22 后端实施完成 — 2026-08-17

- **Core（传输插件机制，决策 #67-69）**：`LabelFrame.Core.Transport.Plugins`——统一接口 `ITransportPlugin`（Id / DisplayName / Description / Parameters / Describe / Create）+ 传输实例继续用 `IPrintTransport` / `IPrinterStatusProvider` / 新增 `ITestableTransport`（连接测试）；参数模型 `TransportParameterSpec` / `TransportPluginParameters` / `ITransportPluginContext`；注册表 `ITransportPluginRegistry`（内置 log / tcp9100 + WinHost 内置 winspool / zebra + 外部 DLL 目录扫描）；`PluginDirectoryLoader`（collectible ALC，单插件失败只记日志，卸载 = 删文件 + 重启生效）。
- **Core（Excel 模板生成，决策 4A）**：`LabelFrame.Core.Excel.ExcelTemplateWriter`（复用 `TemplateFrame.Excel.Simple` 写能力）→ `POST /api/import/excel-template`（Server 与 WinHost 都实现，契约字段 + testData 生成 xlsx）。
- **WinHost（传输插件化）**：`connection.json` 新格式 `{ pluginId, params }` + 旧格式自动迁移（`Mode` / `TcpHost` 等 → pluginId + params）；`TransportManager` 改走注册表装配（校验 / 测试 / 切换 / 持久化）；`GET/POST /api/transport` 扩展（pluginId / displayText / availablePlugins spec，旧字段 mode / availableModes 保留兼容）+ 新增 `GET /api/transport/plugins`；`LABELFRAME_PLUGINS` 插件目录（默认 `%ProgramData%\LabelFrame\Client\plugins`）。
- **Server（权限边界 + 分发）**：`GET /api/jobs?deviceId=` 过滤（客户端只看自己的作业、服务端看全部）；`client-packages` 目录 + 列表 / 上传 / 下载 / 删除 API（文件名路径穿越防护，`LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）；`docker-compose.yml` 挂载 `./client-packages`。
- **测试**：214 全绿（Core 78 / Server 37 / WinHost 74 / Studio 25，新增插件注册表 / 参数模型 / 目录加载器（示例插件 DLL 项目）/ Excel 生成 / TransportConfig 迁移 / 插件切换 / deviceId 过滤 / client-packages 用例）；web 178 全绿。
- **端到端冒烟通过**：上传 / 列表 / 下载 / 删除 client-packages、路径穿越 404、Server/WinHost excel-template（xlsx 200）、jobs 按 deviceId 过滤、/api/transport（availablePlugins + displayText + 外部 sample 插件可装配可测试）、切换失败保留当前连接、旧格式 mode 兼容。
## 迭代 22 联调收尾：后端缺陷修复 — 2026-08-17

- **修复（附四 §三）外部插件删除后宿主启动崩溃**：connection.json 仍引用已删除的外部插件 DLL（决策 2A「卸载 = 删除文件 + 重启生效」）时，`TransportManager.LoadPersisted` 检测插件不在注册表 → 回退默认连接（log）+ host.log 警告，宿主正常启动、不再抛「传输插件不存在」退出；新增回归用例（Startup_should_fall_back_to_log_when_persisted_plugin_missing）。
- **healthz 透出插件信息**：响应新增 `pluginId` / `displayText`（精确反映当前插件；旧字段 `transport` 保留兼容），前端联调观察项落实。
- 前端联调修复（hermes，54e77c9）：Excel 列映射按显示名自动匹配、插件参数全量字符串序列化（修复 HTTP 400）、null 默认值不显示字面量——web 179 用例全绿。
- 测试：dotnet 215 全绿（Core 78 / Server 37 / WinHost 75 / Studio 25）；web 179 全绿。
## 迭代 22 完成 + 迭代 23 计划调整 — 2026-08-17

- **迭代 22 完成（用户确认迭代结束）**：打印测试体验 + 传输插件化 + 客户端下载分发全部交付——前后端实现、联调 8 项场景全过、后端缺陷修复、前端 4 处修复（含剪贴板降级）；dotnet 215 / web 179 全绿；本地 0.18.0 测试包（`LabelFrame-Client-0.18.0.msi` / `LabelFrame-Server-0.18.0.msi` / `labelframe-server-webui-0.18.0.zip`）打包验收通过；ROADMAP 状态更新为已完成。
- **迭代 23 主题调整（2026-08-17 用户提出）**：下一轮改为「客户端插件分发——传输插件包上传服务端 + 客户端选择安装 / 卸载已安装插件」（范围会话中细化）；原「精成打印机传输插件实现 + 真机测试」顺延为迭代 24；Android PDA 宿主延后至迭代 25。
- **版本号**：ServerOptions.ProductVersion 同步 0.18.0（/api/server/info 与 MSI 一致）。
- **打包修复（040e0db，迭代 22 收尾）**：build:server 未设 VITE_UI_MODE 导致 Server UI 插件被打成 client 模式——脚本内联 cross-env + release.yml 产物自检；本地 0.18.0 包已按修复重建（webui 插件为真正 server 模式）。
- **前端 UI 修复（23789ba）**：连接方式插件参数表单必填星号与标签分行（并入同一 flex 子项）；web 187 用例全绿，已纳入重建的 Client 0.18.0 包。
## 迭代 22 规划：范围定稿 + 后端 / 前端分工 — 2026-08-17

- **范围定稿（用户拍板）**：打印测试体验（下载 Excel 模板、客户端仅本机打印测试、显示本机设备名、作业历史按设备可见）+ 传输插件化（统一接口 / 参数模型 / 注册表按需装配）+ 客户端下载分发（服务端 `client-packages` 目录 + 上传下载 API + 管理界面 + 客户端设置默认从服务端获取）。
- **四项决策**：① 客户端本机打印测试 = 在线走服务端路由、未注册 / 离线降级本机直连并提示；② 插件卸载 = 删除文件 + 重启生效（运行时热卸载记未决）；③ 安装包 = 页面上传 + 目录直放都支持，**Ubuntu / Docker 允许挂载 `client-packages`**；④ Excel 模板生成放 Core 共享（复用 `TemplateFrame.Excel.Simple` 写能力）。
- **分工**：后端由主 Agent 负责、前端由 hermes 负责；新增规格文档 `docs/archive/ITERATION-22-SPEC.md`（§5 契约 / §6 后端拆分 / §7 前端拆分 / §8 验收）；后端等待前端评估后并行开工。
- **DESIGN.md 决策 #67-71**：传输插件统一接口与参数模型 / 加载卸载使用 / connection.json 兼容演进 / 打印测试体验与权限边界 / 客户端下载分发；未决新增「传输插件运行时热卸载（ALC）」。
- 不推 tag；仓库内容无公司 / 业务线品牌字样。
## 迭代 21 收尾 + 状态盘点 — 2026-08-17

- **SQLitePCLRaw 升级 2.1.6 → 2.1.13**：修复 GHSA-2m69-gcr7-jv3q（SQLite 原生库漏洞）；Core / Server / AndroidHost 三项目同步升级，移除 NU1903 抑制；构建 0 警告 0 错误，测试 176 全绿。
- **验收状态确认（用户）**：真实打印机验收通过（迭代 2 / 3 / 4、13、14、15）；Studio 界面验收通过（迭代 7 / 8 / 8B / 8C / 8D）；双包 / 无头服务端联调完成（迭代 16 / 18 / 19 / 20）；试点验收通过（扫码枪 50 张 + 连续 100 张压力验证，含重启 / 断网）——ROADMAP 状态表已更新。
- **迭代 12 关闭**：前端 renderLabelImage 按用户决定取消（不再实施）；后端 previewValue / PrintMode 图片打印能力保留。
- **契约字段 Pattern 校验**：列为未来事项（现阶段不处理）。
- **迭代计划调整（2026-08-17 晚）**：用户提出新需求（打印测试体验 / 传输插件化 / 客户端下载分发），迭代 22 主题调整为本轮需求；精成打印机插件排入迭代 23（下一轮）；Android PDA 延后至迭代 24。
- **MSI 签名 Secret**：暂无证书，保持可选（有则自动签名、无则跳过）。

## 迭代 21（自动化发布）— 2026-08-12

- **首次自动发布成功（v0.17.0）**：推送 `v0.17.0` tag 后 CI 全流程通过——dotnet + 前端双模式测试 → 打包 `LabelFrame-Server-0.17.0.msi` / `LabelFrame-Client-0.17.0.msi` / `labelframe-server-webui-0.17.0.zip` / `labelframe-server-0.17.0-linux-x64.tar.gz` → 推送 `ghcr.io/marci-labs/labelframe-server:0.17.0` 与 `:latest` → 创建 GitHub Release（附件齐全）。
- 仓库已转移至组织 `marci-labs` 并转为公开；本地 remote 同步更新。
- CI 稳定性修复：TCP 状态测试改为服务端就绪同步 + 放宽超时；Skia 渲染测试阈值放宽（CJK 字体环境差异）；设备列表测试日期断言时区无关化；SQLitePCLRaw 在测试进程用 `[ModuleInitializer]` 确定性初始化（Server / WinHost / Core 测试）+ `SqliteLogStore` 构造时自行 `SqliteSupport.EnsureInitialized()`（不再依赖宿主先初始化）。
- 打包链修复：WiX 用 dotnet tool 版并安装 NetFx 扩展 + 先接受 OSMF EULA（WIX7015）；artifact 下载路径对齐 `web/dist` 与 `web/dist-server`；插件打包步骤改用 `$?` 判断（脚本内无原生命令时 `LASTEXITCODE` 为空）。
- 待办（2026-08-15 更新）：ghcr 包已设为 Public 并通过匿名 `docker pull ghcr.io/marci-labs/labelframe-server:0.17.0` 验证（注意：组织包可见性不支持 REST API 修改，须先由组织管理员在「组织设置 → Packages」允许公开包，再在包设置页改为 Public）；MSI 签名 Secret 可随时补充（有则自动签名，可选）。

## 迭代 22 后端实施完成 — 2026-08-17

- **Core（传输插件机制，决策 #67-69）**：`LabelFrame.Core.Transport.Plugins`——统一接口 `ITransportPlugin`（Id / DisplayName / Description / Parameters / Describe / Create）+ 传输实例继续用 `IPrintTransport` / `IPrinterStatusProvider` / 新增 `ITestableTransport`（连接测试）；参数模型 `TransportParameterSpec` / `TransportPluginParameters` / `ITransportPluginContext`；注册表 `ITransportPluginRegistry`（内置 log / tcp9100 + WinHost 内置 winspool / zebra + 外部 DLL 目录扫描）；`PluginDirectoryLoader`（collectible ALC，单插件失败只记日志，卸载 = 删文件 + 重启生效）。
- **Core（Excel 模板生成，决策 4A）**：`LabelFrame.Core.Excel.ExcelTemplateWriter`（复用 `TemplateFrame.Excel.Simple` 写能力）→ `POST /api/import/excel-template`（Server 与 WinHost 都实现，契约字段 + testData 生成 xlsx）。
- **WinHost（传输插件化）**：`connection.json` 新格式 `{ pluginId, params }` + 旧格式自动迁移（`Mode` / `TcpHost` 等 → pluginId + params）；`TransportManager` 改走注册表装配（校验 / 测试 / 切换 / 持久化）；`GET/POST /api/transport` 扩展（pluginId / displayText / availablePlugins spec，旧字段 mode / availableModes 保留兼容）+ 新增 `GET /api/transport/plugins`；`LABELFRAME_PLUGINS` 插件目录（默认 `%ProgramData%\LabelFrame\Client\plugins`）。
- **Server（权限边界 + 分发）**：`GET /api/jobs?deviceId=` 过滤（客户端只看自己的作业、服务端看全部）；`client-packages` 目录 + 列表 / 上传 / 下载 / 删除 API（文件名路径穿越防护，`LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）；`docker-compose.yml` 挂载 `./client-packages`。
- **测试**：214 全绿（Core 78 / Server 37 / WinHost 74 / Studio 25，新增插件注册表 / 参数模型 / 目录加载器（示例插件 DLL 项目）/ Excel 生成 / TransportConfig 迁移 / 插件切换 / deviceId 过滤 / client-packages 用例）；web 178 全绿。
- **端到端冒烟通过**：上传 / 列表 / 下载 / 删除 client-packages、路径穿越 404、Server/WinHost excel-template（xlsx 200）、jobs 按 deviceId 过滤、/api/transport（availablePlugins + displayText + 外部 sample 插件可装配可测试）、切换失败保留当前连接、旧格式 mode 兼容。
## 迭代 22 联调收尾：后端缺陷修复 — 2026-08-17

- **修复（附四 §三）外部插件删除后宿主启动崩溃**：connection.json 仍引用已删除的外部插件 DLL（决策 2A「卸载 = 删除文件 + 重启生效」）时，`TransportManager.LoadPersisted` 检测插件不在注册表 → 回退默认连接（log）+ host.log 警告，宿主正常启动、不再抛「传输插件不存在」退出；新增回归用例（Startup_should_fall_back_to_log_when_persisted_plugin_missing）。
- **healthz 透出插件信息**：响应新增 `pluginId` / `displayText`（精确反映当前插件；旧字段 `transport` 保留兼容），前端联调观察项落实。
- 前端联调修复（hermes，54e77c9）：Excel 列映射按显示名自动匹配、插件参数全量字符串序列化（修复 HTTP 400）、null 默认值不显示字面量——web 179 用例全绿。
- 测试：dotnet 215 全绿（Core 78 / Server 37 / WinHost 75 / Studio 25）；web 179 全绿。
## 迭代 22 完成 + 迭代 23 计划调整 — 2026-08-17

- **迭代 22 完成（用户确认迭代结束）**：打印测试体验 + 传输插件化 + 客户端下载分发全部交付——前后端实现、联调 8 项场景全过、后端缺陷修复、前端 4 处修复（含剪贴板降级）；dotnet 215 / web 179 全绿；本地 0.18.0 测试包（`LabelFrame-Client-0.18.0.msi` / `LabelFrame-Server-0.18.0.msi` / `labelframe-server-webui-0.18.0.zip`）打包验收通过；ROADMAP 状态更新为已完成。
- **迭代 23 主题调整（2026-08-17 用户提出）**：下一轮改为「客户端插件分发——传输插件包上传服务端 + 客户端选择安装 / 卸载已安装插件」（范围会话中细化）；原「精成打印机传输插件实现 + 真机测试」顺延为迭代 24；Android PDA 宿主延后至迭代 25。
- **版本号**：ServerOptions.ProductVersion 同步 0.18.0（/api/server/info 与 MSI 一致）。
- **打包修复（040e0db，迭代 22 收尾）**：build:server 未设 VITE_UI_MODE 导致 Server UI 插件被打成 client 模式——脚本内联 cross-env + release.yml 产物自检；本地 0.18.0 包已按修复重建（webui 插件为真正 server 模式）。
- **前端 UI 修复（23789ba）**：连接方式插件参数表单必填星号与标签分行（并入同一 flex 子项）；web 187 用例全绿，已纳入重建的 Client 0.18.0 包。
## 迭代 22 规划：范围定稿 + 后端 / 前端分工 — 2026-08-17

- **范围定稿（用户拍板）**：打印测试体验（下载 Excel 模板、客户端仅本机打印测试、显示本机设备名、作业历史按设备可见）+ 传输插件化（统一接口 / 参数模型 / 注册表按需装配）+ 客户端下载分发（服务端 `client-packages` 目录 + 上传下载 API + 管理界面 + 客户端设置默认从服务端获取）。
- **四项决策**：① 客户端本机打印测试 = 在线走服务端路由、未注册 / 离线降级本机直连并提示；② 插件卸载 = 删除文件 + 重启生效（运行时热卸载记未决）；③ 安装包 = 页面上传 + 目录直放都支持，**Ubuntu / Docker 允许挂载 `client-packages`**；④ Excel 模板生成放 Core 共享（复用 `TemplateFrame.Excel.Simple` 写能力）。
- **分工**：后端由主 Agent 负责、前端由 hermes 负责；新增规格文档 `docs/archive/ITERATION-22-SPEC.md`（§5 契约 / §6 后端拆分 / §7 前端拆分 / §8 验收）；后端等待前端评估后并行开工。
- **DESIGN.md 决策 #67-71**：传输插件统一接口与参数模型 / 加载卸载使用 / connection.json 兼容演进 / 打印测试体验与权限边界 / 客户端下载分发；未决新增「传输插件运行时热卸载（ALC）」。
- 不推 tag；仓库内容无公司 / 业务线品牌字样。
## 迭代 21 收尾 + 状态盘点 — 2026-08-17

- **SQLitePCLRaw 升级 2.1.6 → 2.1.13**：修复 GHSA-2m69-gcr7-jv3q（SQLite 原生库漏洞）；Core / Server / AndroidHost 三项目同步升级，移除 NU1903 抑制；构建 0 警告 0 错误，测试 176 全绿。
- **验收状态确认（用户）**：真实打印机验收通过（迭代 2 / 3 / 4、13、14、15）；Studio 界面验收通过（迭代 7 / 8 / 8B / 8C / 8D）；双包 / 无头服务端联调完成（迭代 16 / 18 / 19 / 20）；试点验收通过（扫码枪 50 张 + 连续 100 张压力验证，含重启 / 断网）——ROADMAP 状态表已更新。
- **迭代 12 关闭**：前端 renderLabelImage 按用户决定取消（不再实施）；后端 previewValue / PrintMode 图片打印能力保留。
- **契约字段 Pattern 校验**：列为未来事项（现阶段不处理）。
- **迭代计划调整（2026-08-17 晚）**：用户提出新需求（打印测试体验 / 传输插件化 / 客户端下载分发），迭代 22 主题调整为本轮需求；精成打印机插件排入迭代 23（下一轮）；Android PDA 延后至迭代 24。
- **MSI 签名 Secret**：暂无证书，保持可选（有则自动签名、无则跳过）。

## 迭代 21（自动化发布）— 2026-08-12（进行中）

- 新增 GitHub Actions 发布流水线 `.github/workflows/release.yml`：推送 `v*` tag 自动测试、打包（Server / Client MSI、管理界面插件 zip、linux-x64 归档）、构建并推送 ghcr.io Docker 镜像、创建 GitHub Release。
- 版本号单一来源：git tag（`v0.17.0` → `0.17.0`），另支持手动触发。
- 打包脚本：新增 `-WixPath` 参数（CI 用 dotnet tool 版 WiX 7.0.0）；MSI 签名证书改走 GitHub Secret（`MSI_SIGN_CERT_BASE64` / `MSI_SIGN_PASSWORD`），有则签名、无则跳过；移除脚本内明文默认密码。
- compose 默认拉取 `ghcr.io/marci-labs/labelframe-server`（`LABELFRAME_VERSION` / `LABELFRAME_IMAGE` 可覆盖）。
- 组织 `marci-labs` 已创建；仓库公开化 / 转移组织待执行。

## 迭代 0（奠基）— 2026-08-08

- 建立文档体系：README（愿景）、AGENTS、DESIGN、REQUIREMENTS、ROADMAP、CHANGELOG。
- 建立解决方案骨架：`LabelFrame.Core` / `LabelFrame.Server` / `LabelFrame.WinHost`（占位），`LabelFrame.AndroidHost` 目录占位。
- 初始化 git 仓库并推送至 GitHub。
## 迭代 1（契约与 ZPL）— 2026-08-09

- `LabelFrame.Core`：契约 / 版式模型（LabelContract、LabelLayout：文本 / 条码 / 二维码 / 图片 / 线，毫米坐标）、LabelDocument。
- 数据校验：必填字段缺失（含空白）拒绝，返回问题码 `LF_VAL_001`。
- ZPL 编码器：文本、Code128（^BC）、图片占位（^FX），毫米 → 点换算（默认 203 dpi）；二维码 / 线元素显式报错待迭代 2。
- 日志传输（模拟打印机）：`LogPrintTransport`。
- 单元测试：库位码 golden test、校验用例、编码器用例、传输用例（14 个，`dotnet test` 全绿）。
- 新增测试项目 `test/LabelFrame.Core.Tests` 并加入解决方案。
## 迭代 2（WinHost 打印闭环）— 2026-08-09

- 全项目升级 .NET 10；WinHost 目标 `net10.0-windows10.0.26100`。
- `LabelFrame.Core`：作业模型 + SQLite 持久化队列（requestId 幂等、逐张状态、挂起 / 恢复 / 取消、批内顺序、重启不丢作业并把在途 Item 重置续打）；LabelBitmap（1bpp）+ ZPL ^GF 位图编码；TCP 9100 传输；版式元素 JSON 转换器（type 判别）。
- `LabelFrame.WinHost`：本地 HTTP API（POST/GET /api/jobs、suspend/resume/cancel、healthz；模板自包含提交）；打印 Worker 串行打印；GDI 中文栅格化（内嵌 / 本地字体优先，回退微软雅黑）；传输：Log / TCP9100 / Windows 驱动（winspool raw）/ Zebra 官方 SDK（TCP / USB 自动发现 / 驱动）。
- 配置：appsettings.json（WinHost 节）+ LABELFRAME_* 环境变量。
- 测试 53 个全绿（队列 / ^GF / TCP / JSON / 栅格化 / raw / Zebra / 提交服务）；端到端冒烟验证通过。
## 迭代 3（Server 路由）— 2026-08-09

- `LabelFrame.Server`：设备注册 / 心跳 / 目录（在线状态）、作业定向投递（requestId 幂等，SQLite 持久化）、宿主轮询领取、结果回报、作业集中查询；测试入口页面；配置 appsettings（Server 节）+ LABELFRAME_SERVER_*。
- `LabelFrame.WinHost`：Server 路由客户端 + 路由 Worker（领取 → 本地队列打印 → 终态回报）。
- 设备离线语义：作业暂存 Pending，上线轮询即领取（不丢作业）。
- 默认端口：WinHost 53960 / Server 53961。
- 测试 65 个全绿；端到端冒烟：提交 → WinHost 领取打印 → 回报 Completed。
## 迭代 4（模板管理 + 预览）— 2026-08-09

- `LabelFrame.Core.Templates`：模板包模型 + zip 导入导出（manifest.json + images/）+ SQLite 模板存储（CRUD / 分组 / 图片资源）。
- `LabelFrame.WinHost`：模板 API（保存 / 列表 / 详情 / 删除 / 导出 / 导入 / 预览）；预览 PNG（GDI 文本与线 + ZXing 条码 / 二维码 + 图片渲染）；ZXing.Net 0.16.11。
- 测试 79 个全绿；冒烟验证：保存 → 预览 PNG → 导出 zip。

## 迭代 5（PDA 宿主）— 2026-08-09

- `LabelFrame.AndroidHost`（net10.0-android）：前台服务 + 开机自启广播、本地 HTTP（127.0.0.1:53970）、IP 9100 传输、Server 注册 / 轮询 / 回报、Android.Graphics 中文栅格化（^GF）、SQLite 作业队列。
- 编译打包成功（Signed APK 约 11MB）；`scripts/build-androidhost.ps1` 一键构建。
- 真机验收（PDA 网页 → Server → 宿主 → IP 打印机、开机自启）待执行；蓝牙在迭代 6。

## 迭代 6（P1 收尾）— 2026-08-09

- 失败项单独重打：`RetryItemAsync`（Failed → Pending，Failed 作业自动恢复）+ API `POST /api/jobs/{jobId}/items/{itemIndex}/retry`。
- 打印机测试页 / 在线状态：`GET /api/printer/status`、`POST /api/printer/test`；TCP `~HS` 基础解析、Zebra 连接即在线、驱动模式不可读回、Log 模拟在线。
- 蓝牙传输随迭代 5 受阻；真实设备字段联调待执行。
## 迭代 7（Studio 模板工具 V1）— 2026-08-09

- `LabelFrame.Studio`（WPF，net10.0-windows）：WinHost 客户端。
  - 连接管理：地址配置、一键启动 / 停止 WinHost、传输模式显示（healthz 新增 transport）。
  - 模板管理：按分组列表、详情（契约字段 + 版式元素）、删除、导出 `.lfpkg`。
  - 模板导入：文件选择 `.lfpkg` → 导入 WinHost 模板库。
  - 测试打印：选模板 → 按契约字段自动生成数据表单 → 预览 PNG → 提交打印作业 → 轮询状态与失败原因。
- 复用 WinHost API，无重复打印逻辑；`StudioClient` 支持注入 HttpClient（可测试）。
- 测试 85 个全绿；界面验收待执行；版式可视化编辑（拖拽画布）为 V2。
## 迭代 8（Studio 版式编排 V2）— 2026-08-09

- `LabelFrame.Studio` 新增版式编辑窗口（EditorWindow）：
  - 画布按 mm 渲染（缩放 50%–250%），元素拖拽移动 / 选中 / 删除。
  - 工具箱添加文本 / 条码 / 二维码 / 图片 / 线元素。
  - 属性面板编辑坐标、尺寸、SourceKey、字体高宽、线宽。
  - 契约字段增删、必填、类型、显示名编辑。
  - 保存（POST /api/templates）+ 刷新预览（WinHost preview PNG）。
- 条码数据仍为纯文本传递，模板元素类型决定条码 / 二维码渲染（无契约变更）。
- 测试 90 个全绿。
## 迭代 8B（Studio 版式增强：字段编辑 / 元素样式 / 区域布局）— 2026-08-09

- 字段编辑：键 Key / 显示名 / 必填 / 类型可编辑；重命名自动同步引用该字段的元素 SourceKey。
- 画布：显眼显示标签尺寸（不随窗口变化）；新元素默认排在上一个下方（上下结构为主）。
- 元素样式（模板包契约扩展，向后兼容）：文本 WidthMm（块宽）/ TextAlign（左/中/右）/ PaddingMm / BorderMm；条码 / 二维码 / 图片 BorderMm。
- 区域（格子）布局：新增 LabelRegionElement 容器；元素可锚定 RegionId + 区域内 H/V 对齐（默认居中）；区域移动元素跟随。
- ZPL 编码：区域边框 ^GB、文本块对齐 ^FB、二维码 ^BQ、线 ^GB（L）；预览渲染同步（共用 LabelLayoutResolver）。
- 测试 99 个全绿。
## 迭代 8C（Studio 界面重构：工作台 + 设计器）— 2026-08-09

- 共享渲染库 `LabelFrame.Rendering`（GDI + ZXing）：预览渲染从 WinHost 抽出，WinHost 与 Studio 共用；Studio 画布 / 预览本地实时渲染。
- 契约扩展：文本 / 条码 / 二维码支持 `Literal` 固定值或 `SourceKey` 字段填充（向后兼容）。
- 作业工作台（主窗口重写）：菜单栏、模板列表、本地预览、数据表单、打印、底部状态栏 + 日志栏。
- 模板设计器（独立窗口）：控件栏点击 / 拖入、画布毫米网格、选择移动、画区域（拖矩形）、元素拖入区域自动锚定居中 / 移出解除锚定、属性分组（位置尺寸 / 文本字体 / 填充 / 内边距边框 / 区域锚定）、测试数据、实时打印预览（节流）、打印测试、底部状态 + 日志。
- 待办（迭代 8D）：拖角缩放、标尺 / 对齐线。
- 测试 105 个全绿。
## 迭代 8D（设计器交互重做）— 2026-08-09

- 设计器重做（`DesignerWindow`）：
  - 设计 / 测试用 Tab 分离：测试 Tab 放测试数据（字段由版式自动推导）、实时打印预览、打印测试。
  - 控件栏改为可拖拽项（文本 / 条码 / 二维码 / 图片 / 线 / 容器），点击添加一次、拖入画布定位，修复“拖拽一次建两个元素”问题。
  - 画布：毫米标尺 + 网格；左键选中、8 手柄拖角缩放、Shift/Ctrl 点击与拖框多选、Delete 删除、中键平移、Ctrl+滚轮缩放（以鼠标为中心）；移动时边缘 / 中心自动吸附到画布与其它元素；右键对齐菜单（左 / 水平居中 / 右 / 上 / 垂直居中 / 下）。
  - 容器控件替代“画区域”：控件栏拖「容器」矩形；元素拖入容器自动锚定居中；属性面板移除 RegionId / 锚定 UI（后台能力保留，模板格式不变）。
  - 属性面板仅在选中元素时显示（默认收起）：单选显示元素属性，多选显示对齐工具。
  - 底部状态 + 日志栏横跨全窗口，日志自动滚动到底、可一键清空。
  - 固定值 / 字段 / 样式修改实时重绘画布并节流刷新打印预览。
- 契约字段后台自动推导：字段集合 = 版式「字段填充」元素 SourceKey 去重（保留旧契约字段顺序与元数据）；移除字段增删 / 重命名 / 显示名 UI；工作台与测试表单统一用 Key 作标签。
- `MainWindow`：数据表单标签改用字段 Key；日志自动滚底 + 清空按钮。
- 测试 109 个全绿（新增字段推导 / 多选删除 / 对齐 / 吸附用例）。
## 迭代 9（Excel 数据导入）— 2026-08-09

- 新增 `ExcelImportService`（Studio 服务层，UI 栈无关）：读取 .xlsx（标题行 + 数据行）、列 → 字段映射建议（Key 忽略大小写匹配）、按行生成标签数据字典；基于 `TemplateFrame.Excel.Simple` 1.0.5。
- 主窗口「导入数据(Excel)…」：选模板 → 选 .xlsx → 映射确认窗口（列 → 字段 Key 可手工调整）→ 批量打印（一次提交多张，复用 `/api/jobs`）→ 轮询作业状态；首行数据自动刷新预览，状态栏显示文件名与行数。
- Web 设计器原型 `prototypes/web-designer/`：Konva 画布（控件栏 / 容器 / 手柄缩放 / 多选对齐 / 中键平移 / Ctrl+滚轮缩放 / 标尺网格）+ WinHost API（连接 / 加载 / 保存 / 预览），用于 UI 技术选型评估（决策 #39）。
- 测试 112 个全绿（新增 Excel 读取 / 映射建议 / 行数据生成用例）。

## 迭代 8E（Web 设计器原型 v2）— 2026-08-09

- 视口缩放模型：画布容器自动铺满视口（随窗口自适应）；Ctrl+滚轮只缩放画布内容（以鼠标为中心）；「适应窗口」/「实际大小」按钮，设计态与真实尺寸预览分离。
- 条码 / 二维码实时渲染：值变化立即渲染真实条码 / 二维码（JsBarcode / qrcode-generator 本地化）；属性面板预留条码（码制 / 底部文字 / 模块宽）与二维码（纠错级别 / 边距）参数分组。
- 智能参考线：拖动时吸附画布边缘 / 中心与其它元素边缘 / 中心并显示参考线（参考 Figma / Konva snapping 做法）。
- 边框修正：边框为矩形元素外框描边，不再描文字。
- 控件栏精简为文本 / 条码 / 二维码（图片 / 线 / 容器移除入口，已有模板仍可加载显示）。
- 文本溢出模式：每元素可配置「自动换行 / 超长截断 / 缩小字体」（参考 BarTender Auto-Fit / Cleverence Label）。
- 原型经 headless 浏览器自测通过（元素添加 / 条码二维码渲染 / 初始化无异常）。

## 迭代 8F（Web 设计器原型 v3）— 2026-08-09

- 画布留白 + 标尺跟随：画布实际大小 = 输入尺寸 + 四周 10mm 留白；标尺以 mm 覆盖整个画布并随画布移动 / 缩放；内容区边缘标蓝刻度。
- 画布平移不越界：中键拖拽 clamp 到可视边界。
- 容器不再手动缩放：默认画布铺满视口；「实际大小」= 1mm=8 点（203dpi 打印比例），可滚动 / 平移查看。
- 文本溢出新增「不限制高度」模式（按内容实际高度显示全部文字）。
- 修复控件拖入不可见：drop 坐标改为基于 clientX/Y 几何换算（原依赖 Konva 指针状态，HTML5 拖拽期间无指针事件导致元素被放到错误 / 越界位置）。
- 核心修复：stage 尺寸 = 逻辑尺寸 × 比例尺（原实现 stage.scale 只缩放绘制内容，canvas 容器仍是逻辑尺寸，放大时内容被 canvas 裁剪 → 网格范围变化 / 拖入元素在裁剪区外不可见 / 标尺错位）。
- 标尺与网格对齐：标尺 0 点与画布左缘对齐（左上角空块布局）；网格覆盖整个画布（含 10mm 留白）并与标尺同比例缩放。
- 适应窗口 / 实际大小统一为「比例尺预设」：设计时按点处理，需要真实比例时再换算点与 mm。
- 第三轮修复：
  - 控件不可见根因：Konva 9.3 的 Text 无 clipFunc，文本渲染抛异常中断 render（网格先画好所以可见）。改为 Group clip 裁剪；条码 / 二维码未绑定显示「未绑定」虚线占位。
  - 标尺画进 Konva（与内容同坐标系），放大 + 平移后标尺 / 网格不再错位；HTML 标尺移除。
  - 中键平移改用原生 DOM + document 级 mouseup 复位（修复松开后仍拖拽的粘滞）。
- 第四轮修复：
  - 吸附 / 位置换算统一用 `getClientRect({ relativeTo: layer })` 逻辑坐标（原用绝对视觉坐标，比例尺放大后吸附与定位偏差 total 倍）。
  - 二维码同步 canvas 渲染（模块遍历手绘，去掉异步 Image 加载）。
  - 属性面板下拉 / 勾选补 commit（修复条码底部文字、码制、纠错等切换不刷新）。
  - 边框 / 内边距通用化（文本 / 条码 / 二维码一致）；文本模式收敛为「缩小适应 / 溢出显示」（文本框 = 遮罩区域，抛弃自动换行 / 不限制高度）。
- 第五轮改进：拉伸文本框不再改变字高（字高独立，遮罩区域变化）；溢出模式文案改「隐藏」；内边距拆上下 / 左右；填充默认固定值，字段填充 = 键名称 + 填充值（预览）；新增 Ctrl+C / Ctrl+V 复制粘贴（偏移 5mm）。
- 第六轮改进：Ctrl+Z / Ctrl+Y 撤销恢复（上限 100 步）；字高调大才撑高文本框；吸附强化（边完全重合 + 参考线醒目）；导出 / 导入设计到剪贴板（labelframe-web-design JSON，Ctrl+Shift+C/V）；控件栏新增矩形控件（边框 + 可选填充，保存映射 region）；文本框基础属性新增高度字段（遮罩高度，与条码 / 二维码一致）。
- 第七轮改进：矩形镂空（仅边框，移除填充色）；新增图层面板（控件列表 / 点击选中同步画布 / 置顶上移下移置底 / 列表 Delete 删除）。
- 第八轮修复：网格吸附兜底（无参考目标时贴最近 1mm 网格，消除 0.2 小数偏移）；字段填充提示明确「打印以外界数据为准，预览值被忽略」。
- 第九轮改进：移除适应窗口 / 实际大小按钮，改为 DPI 选择框（203/300）+「预览打印效果」按钮——按所选 DPI 以真实打印比例显示（203→1mm≈8点、300→1mm≈12点），再点退出回到适应窗口。
- 第十轮改进：文本框自动换行（超过显示区域换行，仍超出整体缩小字体至最小 1.5mm）+ 行间距（默认 1.2）+ 字体选择（雅黑 / 宋体 / 黑体 / 楷体 / Arial / Consolas）；单行保留缩小 / 隐藏。
- 补充：文本垂直对齐（顶端 / 居中 / 底部），配合换行使用。
- 纯前端编辑器化：移除连接 / 模板 / 保存 / 预览 / 导出导入按钮与 IP 等后端元素；导出 / 导入设计保留快捷键（Ctrl+Shift+C / Ctrl+Shift+V）并写入操作提示；顶部仅保留纸张 / DPI / 预览 / 缩放 / 网格；修复中键误触发控件选中（仅左键响应点击）。

## 迭代 11（单机模式，后端部分）— 2026-08-09

- 契约扩展：模板 testData（Core / SQLite / 模板包 / API 全链路，旧库自动迁移）。
- WinHost 演进：Web UI 静态托管（web/dist + SPA fallback）、Excel 导入 API、PDA 日志端点、宽松 CORS。
- AndroidHost 演进：PDA 测试模式（pc_host 配置 / 拉模板列表 / 点击模板用 testData 本地打印 / 终态日志回传 PC / 内置测试页）。
- 前端规格 docs/archive/FRONTEND-SPEC.md 定稿（hermes 两轮审阅全部落定），前端并行开发中。
- 测试 118 个全绿；AndroidHost 编译通过。

## 迭代 10（MSI 安装包）— 2026-08-09

- WinHost 单机 UX：WinExe（无控制台）、启动自动打开浏览器、Log 写 host.log、本机优雅关闭端点。
- 一键打包脚本：publish-winhost.ps1（self-contained + web/dist）+ build-msi.ps1（WiX v7）。
- MSI：桌面 / 开始菜单快捷方式、默认配置、卸载清理；产物 LabelFrame-0.11.0.msi（约 47MB）。
- 发布版冒烟通过；MSI 数据库验证 443 文件 + 2 快捷方式（Target=#WinHostExe）；沙箱无法实际安装 / 签名（Windows Installer 服务与 CryptoAPI 受限），真机验收与签名待执行。
- 名称统一为 LabelFrame（安装目录 / 快捷方式 / 卸载显示）；新增应用图标（蓝底白色 L 型，嵌入 exe 与快捷方式）。
- 新增脚本：generate-icon / create-signing-cert（openssl 自签名 + .NET 重封装）/ cleanup-residue（管理员清理历史残留）。
- 发布改 framework-dependent：MSI 56.5MB → 9.7MB（目标机需 .NET 10 Desktop Runtime）；系统托盘改原生 P/Invoke 实现（无 WinForms 依赖）。
- 安装结构修复：web/dist 与 assets 子目录正确（解决白屏 / JS 404）；安装目录 Program Files\LabelFrame。
- MSI 增加 .NET Desktop Runtime（x64）检测：缺失时全 UI 安装显示带可点击官方下载链接的对话框（MSI Hyperlink 控件，点击直达下载页）；静默 / 基础 UI 由 LaunchCondition 拦截并提示链接；不自动安装（2026-08-10 用户确认放弃 Burn 自动引导方案）。
- 修复运行时误报缺失（2026-08-10）：检测从注册表搜索改为 WiX NetFx 扩展 DotNetCompatibilityCheck（内置官方 NetCoreCheck 自检，检查 x64 Microsoft.WindowsDesktop.App >= 10.0.0、RollForward=latestMajor）。原注册表搜索读 sharedfx 键默认值，而运行时版本号是命名值，且 32 位 MSI 读 32 位视图，导致已装 Desktop Runtime 仍提示未安装；现改为实时自检，装完运行时**无需重启**即可识别。
- 修复托盘 P/Invoke 崩溃（0.11.1，2026-08-10）：`GetCurrentThreadId` / `GetModuleHandle` 被错误声明为从 `user32.dll` 导入（实际在 `kernel32.dll`），托盘线程启动即抛 `EntryPointNotFoundException`，未处理异常直接杀死宿主进程——这是「装完啥也不显示 / 页面打不开」的根因；已改为正确 DLL，并给托盘循环加异常保护：托盘出问题只记日志，不再让宿主退出。
- MSI 改为 **x64 包**（0.11.1，2026-08-10）：此前 MSI 是 32 位包，`ProgramFiles64Folder` 不生效导致装到 `Program Files (x86)`；现 `wix build -arch x64`，安装到 `C:\Program Files\LabelFrame`；版本 0.11.1 支持直接覆盖已装的 0.11.0。
- ZPL 编码器显式输出 `^PW` / `^LL`（0.11.2，2026-08-10）：按模板宽高换算点数（70×50 @203dpi → `^PW559` / `^LL400`），避免打印机沿用旧标签长度导致一张作业走多张纸；新增对应单元测试。
## 迭代 12（模板预览值 + 图片打印，后端部分，2026-08-10）

- 元素 JSON 新增 `previewValue`（字段填充模式预览值持久化，text/barcode/qrcode 非空时输出，旧模板向后兼容）。
- `TemplateStore.SaveAsync` testData 读-改-写：数据库现有值 → 并入显式传入 → 被元素预览值派生覆盖；旧模板显式测试数据不再因前端不传而被清空。
- 新增 `PrintMode`（Vector 默认 / Image）：Image 模式整版渲染 1bpp 位图经 `^GF` 直传打印机，与画布预览同源；`SubmitJobRequest` 支持 `printMode` 覆盖、`template.name` 取模板图片；`/healthz` 返回 `printMode`。
- 修复：预览渲染器文本无显式块宽时被裁成 1px 导致图片打印空白（改为按文本实际宽度绘制）。
- 修复：Log 传输重复打开同一 host.log 触发文件锁、ZPL 被静默丢弃（复用宿主日志写入器）。
- 测试 127 个全绿（新增 previewValue 往返、testData 派生/保留/覆盖、EncodeImage、RenderLabelBitmap、Image 打印模式用例）。
- 产物 `LabelFrame-0.11.3.msi`（2026-08-10）：含迭代 12 前后端合并版（预览值持久化 + 测试默认值 + 打印方式选择 + 图片打印），可覆盖 0.11.x 安装。
- 图片打印调试与清晰度优化（0.11.4，2026-08-10）：打印位图改为单比特网格对齐（去抗锯齿灰度，避免 1bpp 阈值切字发虚）；新增 `POST /api/print/render-image` 与前端「调试：不打印，保存实际打印图片（PNG）」复选框（图片打印方式下显示），用于排查文字清晰度 / 定位是渲染问题还是打印机问题。产物 `LabelFrame-0.11.4.msi`。
- 后端渲染器改为 SkiaSharp（0.11.5，2026-08-10，方案 2）：新增 `SkiaLabelRenderer`（canvas 类 2D 渲染，与前端同源规则：文本超出框宽缩小适应、左中右对齐、内边距/边框、线条/区域、ZXing 条码二维码、模板图片），图片打印与「保存打印图片」均切换；修复 GDI 渲染的 CJK / 右对齐 / 长文本缺失问题（含生僻字开头只匹配小字体、行高为负导致裁剪空矩形）。你的 70×50 模板四个字段（MaterialName / CompanyName / Specification / WarehouseName）端到端验证全部渲染。产物 `LabelFrame-0.11.5.msi`。
- 文本垂直对齐契约与前后端同源（0.11.6，2026-08-10）：文本元素新增 `heightMm` + `verticalAlign`（Top/Middle/Bottom），前端保存时写入元素高度与垂直对齐；Skia / GDI 渲染器按框高垂直对齐绘制，修复「打印比前端预览整体偏上」（此前前端在元素框内垂直居中、后端顶部对齐，且高度未持久化）。端到端验证：MaterialName / CompanyName / WarehouseName 文字均落在框中部。产物 `LabelFrame-0.11.6.msi`。
- 修复 0.11.6 回归（0.11.7，2026-08-10）：无 `heightMm` 的旧模板 + 1mm 内边距时，内框高（字高−2×内边距）被算成负数，裁剪区塌成 1px 导致文字几乎全部消失；现裁剪高度至少一行，旧模板恢复顶部对齐可见，新模板保持居中。产物 `LabelFrame-0.11.7.msi`。

## 迭代 13（文本排版与二维码参数持久化，后端部分，2026-08-10）

- 元素契约补齐第二批字段：文本 `wrap / lineHeight / fitMode / fontFamily`（默认 Microsoft YaHei）、二维码 `qrEcc / qrMargin`（默认 M / 2）、条码 `displayValue`（默认 true）、通用双边内边距 `paddingH / paddingV`（`PaddingHMm / PaddingVMm`，0=未设，缺失时回退 `paddingMm`，`paddingMm` 保留兼容）。
- 决策 A：`VerticalAlign` 默认由 Top 改为 Middle（与前端一致）；`LabelElementJsonConverter` 写规则改「非 Middle 才写」；旧模板无 `heightMm` 时 Skia 渲染器框高兜底 = `max(字高 + 2×最大双边内边距, 10mm)`（与前端读回兜底一致）。
- `LabelElementJsonConverter` 读写：非默认才写（wrap=true、displayValue=false、fitMode 非 shrink、lineHeight 非 1.2、fontFamily 非默认、qrEcc 非 M、qrMargin 非 2、paddingH/V >0），旧模板无新字段读回默认，无数据库迁移（layout 整块 JSON）。
- `SkiaLabelRenderer` 渲染支持：wrap 自动换行 + 行距（lineHeight 倍数）+ 超高整体缩小（最小 1.5mm）、overflow 隐藏裁剪不缩小、fontFamily 字体族（含 CJK 系统回退）、qrEcc / qrMargin 传 ZXing、条码 displayValue 底部数值文字（条码占剩余高度）、文本 / 条码 / 二维码双边内边距内容区（= 元素框减 padding）。
- ZPL 矢量路径不变量：新排版字段不参与 ZPL 编码，矢量输出与现状一致（新增不变量测试）。
- 测试 152 个全绿（新增字段往返 / 省略规则 / paddingMm 兜底 / wrap 换行与超高缩小 / overflow 不缩小 / 字体族 / QR 纠错与静区 / 条码文字 / 双边内边距 / 旧模板默认 Middle / ZPL 不变量）。
## 迭代 13（文本排版与二维码参数持久化，前后端已完成，2026-08-10）

- 前端（hermes）：`convert.ts` 的 `BackendElement` 补齐 `paddingH/paddingV/fontFamily/wrap/lineHeight/fitMode/qrEcc/qrMargin/displayValue`；写方向按契约非默认才写（wrap=true、displayValue=false、verticalAlign 非 Middle、fitMode 非 shrink 等）；读回 `?? 默认`（paddingH/V ?? paddingMm 旧模板兜底）；`ElementNode.tsx` TextContent wrap=true 超高由裁剪改为整体缩小（最小 1.5mm），与后端 Skia 渲染语义一致；convert.test.ts 64 用例全绿（+7 新增）。
- 复现验证：100×60 方案导入 → 保存 → 重开，关键差异清零（wrap / lineHeight / qrEcc / paddingV 均保留；剩余仅默认值显式化，显示一致）。
- 文档归档：`docs/archive/ITERATION-13-SPEC.md` / `docs/archive/ITERATION-13-CONTRACT.md` 标记已完成；ROADMAP 迭代 13 状态更新为「已完成（用户验收待执行）」；DESIGN 决策 #47 更新为前后端完成。
- 产物 `LabelFrame-0.12.0.msi`（2026-08-10）：含迭代 13 前后端合并版（元素契约第二批字段 + Skia 图片打印渲染 + 前端字段映射与 wrap 超高缩小），可覆盖 0.11.x 安装；用户测试验收待执行。
## 迭代 13 前端修复（0.12.1，2026-08-10）

- 修复画布中文长文本字高失真（commit abf58a0）：Konva `wrap='word'` 按空格分词，中文无空格永不换行 → 长文本单行溢出被 shrink 缩小；改为含 CJK 文本用 `wrap='char'` 逐字换行（与 Skia 打印语义一致），纯 ASCII 保持 `word`；shrink 缩小循环按「单行宽 ÷ 内容宽 = 行数」估算换行后总高，只对超高整体缩小（最小 1.5mm），并补 `lineHeight` 依赖。
- 实测：70×50 方案 MaterialName（fontH 3, wrap）修复前单行 1.59mm，修复后两行 2.85mm；64 单测 + build + lint 全绿。
- 产物 `LabelFrame-0.12.1.msi`（2026-08-10）：含该前端修复，可覆盖 0.12.0 / 0.11.x 安装。
## 打包优化（0.12.2，2026-08-10）

- MSI 升级不再覆盖用户配置：`appsettings.json` 从自动文件清单（AppFiles）中剔除，改为 `main.wxs` 中 GUID 固定的独立组件，标记 `NeverOverwrite="yes"`（升级 / 修复不覆盖）+ `Permanent="yes"`（卸载不删除）。新装仍写入默认配置；已改过的配置在后续更新中保留。
- 说明：`appsettings.json` 属于用户数据，卸载时也会保留（与 %LOCALAPPDATA%\LabelFrame 下的数据库一致）；需要全新默认配置时可手动删除该文件后重装。
- 产物 `LabelFrame-0.12.2.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装。
## 迭代 14（字体加粗 bold 契约，后端部分，2026-08-10）

- 前端（hermes，commit ae16d0d）：属性面板「加粗（打印更清晰）」复选框 + 画布 `fontStyle:'bold'`；convert.ts 契约字段 `bold`（true 才写 / 读回 ?? false）+ 单测；属性面板数字输入受控同步、右侧面板滚动条两项修复。
- 后端：`LabelTextElement.Bold`（bool，默认 false）+ `LabelElementJsonConverter` 写 `bold: true`（true 才写）、读回默认 false，旧模板兼容。
- ZPL（Vector）：新增 `ZplBoldMode`（默认 `FontVariant` 方案 A：粗体字体变体映射 `"0"→"1"`；`WidthScale` 方案 B：宽度 ×1.15 放大兜底）；`ZplEncoder` 构造函数可注入模式与映射表；WinHost `HostOptions.BoldMode` + `LABELFRAME_BOLD_MODE` 环境变量可配置。
- Skia（Image）：测量与绘制字体统一 `SKFont.Embolden = text.Bold`，换行 / shrink 度量按加粗字体计算，与前端预览一致。
- 测试 156 全绿（新增 bold 往返/省略、ZPL 方案 A/B、Skia 加粗墨迹对比）。
## 迭代 14 打包（0.12.3，2026-08-10）

- 含迭代 14 前后端合并版：文本加粗（`bold` 契约 + ZPL 方案 A/B + Skia Embolden）+ 前端加粗设置与属性面板两项修复。
- `appsettings.json` 保留用户配置机制沿用（独立组件 NeverOverwrite + Permanent）。
- 产物 `LabelFrame-0.12.3.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装。
## 迭代 15（打印设置与会话保留 + 连接管理 + 删除 ZPL，后端部分，2026-08-10）

- 彻底删除矢量 ZPL：移除 `IZplEncoder` / `ZplEncoder.Encode` / `ZplBoldMode` / `PrintMode`（配置 + `LABELFRAME_PRINT_MODE`）/ `SubmitJobRequest.printMode` / `/healthz.printMode` / `ITextRasterizer` / `GdiTextRasterizer` 及对应测试；`^GF` 位图编码重构为 `ZplImageEncoder`；作业项内容统一为整版位图指令（沿用历史列名，无迁移）；README / demo 脚本同步清理。
- 连接管理：`ITransportManager` + `TransportConfig`；`GET /api/transport`、`POST /api/transport`（单一连接、先测试后生效、失败自动回滚、400 沿用 ErrorView、响应统一 `config`=当前生效连接）；持久化 `%LOCALAPPDATA%\LabelFrame\connection.json`（启动优先级 connection.json > appsettings > 默认 Log）；Tcp / Windows 驱动 / Zebra 增加连接测试能力；打印 Worker / 打印机状态 / 测试页统一从管理器取当前连接；测试页改为 Skia 渲染整版位图 ^GF。
- 调试出图：新增 `POST /api/print/render-images`（批量渲染全部行返回 zip，`label-{n}.png`）；保留 `POST /api/print/render-image`（单张 PNG）；调试不建作业、不发驱动、不改作业模型 / SQLite。
- Log 模拟打印：`LogPrintTransport` 只记录摘要（不再写大段指令）；作业层渲染 PNG 保存到 `%LOCALAPPDATA%\LabelFrame\print\{jobId}\` 并写 host.log 摘要。
- AndroidHost：新增 `AndroidLabelRenderer`（Android.Graphics + ZXing）整版位图渲染 → `ZplImageEncoder`，替换 ZplEncoder（真机验收待 PDA 联调）。
- 测试 143 全绿（Core 60 / Server 8 / Studio 25 / WinHost 50）；AndroidHost 编译通过；前端（hermes）已实施会话保留 / 连接切换 UI / 调试开关（见下条）。


## 迭代 15（打印设置会话保留 + 连接管理 + 删除 ZPL，前端部分，2026-08-10）

- DataPrint 会话保留（§6.1）：草稿提升全局 AppContext（`printDraft`：selectedName / valuesByTemplate + dirtyKeysByTemplate / debugMode / jobId），sessionStorage 持久化（刷新保留、标签页天然隔离；**禁 localStorage**）；values 按 **key 存在性**合并（用户主动清空的字段不被 testData 顶回）；Excel 数据与列映射不保留。
- 连接管理 UI（§6.2）：AppContext 增 `transportConfig`（GET /api/transport），切换成功后立即用响应 config 更新全局状态；设置页「连接方式」分组（模式单选 Log / TCP / Windows 驱动 / Zebra，只显示当前模式参数，「测试连接」testOnly、「保存并应用」先测试后生效失败回滚）；DataPrint 顶部连接徽标 + 快速切换；状态栏 / 导航徽标显示 mode + 关键参数（TCP 192.168.1.50:9100 等）。
- 调试独立（§6.3）：独立开关（默认关）——开：「调试出图（单张）」走 render-image 下载 PNG、「下载调试图片 zip（N 张）」走 render-images（全部行），不建作业不发驱动，作业进度区提示；关：「打印测试 / 批量打印」正常作业 +「出图预览」即时预览。
- 删除（§3.2）：DataPrint / Settings 的 printMode 下拉与旧调试复选框、`Healthz.printMode` / `SubmitJobRequest.printMode` 类型。
- api client：新增 `getTransport` / `setTransport` / `testTransport` / `renderImages`；下载型端点统一 `fetchBlob`（Content-Disposition 文件名 + ErrorView 错误解析）。
- 测试 91 全绿（新增 27 个：draft 纯逻辑 / 连接切换交互 / 保留与调试按钮行为）；`pnpm build` / `pnpm lint` 通过。

## 迭代 15 打包（0.13.0，2026-08-10）

- 含迭代 15 前后端合并版：彻底删除矢量 ZPL（打印统一 Skia / Android 整版位图 ^GF）；连接管理 `GET/POST /api/transport`（单一连接、先测试后生效、失败回滚、持久化 connection.json）+ Web 设置页 / 数据与打印页连接切换 UI；调试独立（单张 PNG / 批量 zip，不建作业不发驱动）；DataPrint 会话保留（同标签页切视图不丢设置、标签页间不互通）；Log 模拟打印保存 PNG；AndroidHost 图片打印。
- 联调反馈修复：TCP 连接测试加固（IP 直连 + 本地监听开/关回归测试）；前端测试环境 storage 垫片（Node 26 下 jsdom 兼容，91 用例全绿）。
- `appsettings.json` 保留用户配置机制沿用；`%LOCALAPPDATA%\LabelFrame\connection.json` 保存用户连接配置。
- 产物 `LabelFrame-0.13.0.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装。
## 迭代 15 实测修复（连接测试严格化 + Log 输出可见，2026-08-10）

- 连接测试升级：Tcp / Zebra 均改为「连接 + `~HS` 主机状态探测」，无打印机响应判定失败（能连端口 ≠ 打印机），失败不切换、不持久化。
- Log 模拟打印：PNG 保存到 `%LOCALAPPDATA%\LabelFrame\print\{jobId}\`，作业视图新增 `printImageDir` / `printImageCount`，前端作业进度区显示目录与张数（此前用户找不到输出）。
- 新增回归测试：本地监听响应 `~HS` 判定成功、无响应判定失败（两向稳定）。

## 迭代 15 增强（设计器快捷操作说明，2026-08-10）

- 设计器画布顶部常驻核心快捷键提示条（`Ctrl+Z 撤销 · Ctrl+C/V 复制粘贴 · Delete 删除 · 中键平移 · Ctrl+滚轮缩放`），编辑模式随时可见（与预览模式提示同款视觉；预览时自动切换为预览提示）。
- 设计器工具栏新增「快捷键」按钮，弹出完整清单：编辑（撤销重做 / 删除 / 取消放置）、剪贴板（复制粘贴 / 导出导入设计 JSON）、画布（中键平移 / Ctrl+滚轮缩放 / Shift+Ctrl 多选 / 拖拽吸附 / 手柄缩放）三组。
- 测试 95 全绿（新增快捷键清单结构测试 4 个）。

## 迭代 15 打包（0.13.1，2026-08-10）

- 含迭代 15 前后端合并版 + 实测修复：连接测试升级为 `~HS` 打印机探测（能连端口≠打印机，无响应不切换）；Log 模拟打印目录随作业视图显示（`printImageDir` / `printImageCount`，前端进度区展示）；设计器快捷键提示条与「快捷键」弹窗（95 前端用例全绿）。
- 产物 `LabelFrame-0.13.1.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装；`appsettings.json` 保留机制沿用，连接配置持久化到 connection.json。
## 修复：本地 UI 打开地址规范化（2026-08-10）

- 当 `ListenUrl` 配置为 `0.0.0.0`（局域网访问）时，启动自动开浏览器与托盘「打开主界面」会跳到 `http://0.0.0.0:53960`；新增 `ToLocalUiUrl` 把通配监听地址（`0.0.0.0` / `*` / `+` / `[::]`）规范化为 `127.0.0.1`，本地界面始终可打开。
## 迭代 15 打包（0.13.2，2026-08-11）

- 前端 baseUrl 修复（附九定稿实施）：`getBaseUrl()` 无存储值时默认返回页面自身来源（`window.location.origin`）——PDA 远程访问不再发往自身回环 127.0.0.1；方案 B 自动纠正旧版保存的默认地址残留；新增 `settings.test.ts` 5 用例；`vite.config.ts` dev proxy（`/api` + `/healthz`）配套联调。前端 100 用例全绿。
- 后端（此前已合入）：本地 UI 打开地址规范化（`0.0.0.0` 监听时浏览器/托盘跳 127.0.0.1）、连接测试 `~HS` 探测、Log 模拟打印目录展示。
- 产物 `LabelFrame-0.13.2.msi`（2026-08-11）：可覆盖 0.12.x / 0.11.x 安装；`appsettings.json` 保留机制沿用。
## 迭代 16（服务端 / 客户端拆分，后端骨架，2026-08-11）

- 服务端（LabelFrame.Server）迁入集中能力：模板库（CRUD / 导入导出 / 预览）、作业提交支持 `templateName` 引用（pending 载荷附带模板 + 图片 base64）、调试出图（render-image / render-images，Skia）、设备日志接收与查询、Excel 导入、Web UI 静态托管（SPA fallback）；Server TFM 改 net10.0-windows（引用 Skia 渲染）。
- 客户端（WinHost）配合：`TemplateDto` 增 `Images`（base64），提交服务优先用内联图片否则按 Name 本地加载；路由 Worker 透传 Server 附带模板；`SqliteLogStore` 移至 Core.Logs 供两端共用；单机模式保留。
- 测试 147 全绿（Core 60 / Server 10 / Studio 25 / WinHost 52）；Server 新增 templateName 解析与模板不存在用例。
## 迭代 16/17（服务端 / 客户端拆分，0.14.0，2026-08-11）

- 前端（hermes，e161d81）：移除打印机连接 UI（连接方式 / 打印机分组、连接徽标与快速切换、transport API 与类型）；数据与打印新增目标设备选择（listDevices + targetDeviceId + templateName 提交），404/失败自动降级单机模式；JobView 适配 Server 作业视图；前端 105 用例全绿。
- 双 MSI 打包：`LabelFrame-Server-0.14.0.msi`（→ Program Files\LabelFrame\Server，服务端，默认 0.0.0.0:53961）与 `LabelFrame-Client-0.14.0.msi`（→ Program Files\LabelFrame\Client，打印客户端，默认 ServerUrl=127.0.0.1:53961）；两包 appsettings 保留机制沿用；打包脚本 `build-msi.ps1`（Client）与 `build-server-msi.ps1`（Server），文件清单 GUID 按包加盐避免冲突。
## 打包增强：卸载询问清除用户数据（0.14.0，2026-08-11）

- 两个 MSI 卸载时弹出确认对话框「清除用户数据（默认不勾选）」；勾选则删除本程序产生的数据：
  - Client：`%LOCALAPPDATA%\LabelFrame\` 下 jobs.db / templates.db / logs.db / host.log / connection.json / print 目录 + 安装目录 appsettings.json；
  - Server：`%LOCALAPPDATA%\LabelFrame\server\` 目录 + server.db + 安装目录 appsettings.json。
- 仅手动卸载触发（条件 `REMOVE=ALL AND NOT UPGRADINGPRODUCTCODE`），覆盖升级不会清数据；静默卸载不弹窗、默认保留。

## 迭代 18（决策与规格，进行中，2026-08-11）

- 架构修订（0.15.0）：服务端默认不提供界面（移除 web/dist 托管），客户端（WinHost 127.0.0.1:53960）托管完整 Web UI；模板 / 作业 / 设备投递仍以服务端为中心，作业走服务端队列。
- 服务端 Windows 服务部署（`LabelFrameServer`，LocalSystem）；数据目录默认改 `%ProgramData%\LabelFrame\server`；历史数据定期清理（作业默认保留 30 天、日志默认保留 90 天，可配置，非终态作业不删）。
- 客户端机器级 ServerUrl（WinHost `GET/POST /api/host/config` → `%ProgramData%\LabelFrame\Client\settings.json`）。
- 双 MSI 安装完成弹窗：Server（开机自启 / 立即运行，默认勾选）、Client（立即打开，默认勾选）；升级不触发。
- 规格与任务单：docs/archive/ITERATION-18-SPEC.md；决策登记 docs/DESIGN.md #53-58；架构修订 docs/archive/ARCHITECTURE-SPLIT.md。


## 迭代 18 后端实施（0.15.0，2026-08-11）

- Server 无头化：移除 Web UI 静态托管与测试页（/、/devices、/jobs），仅保留 /healthz 与 API；`GET /api/jobs` 支持 `?limit`（默认 100，上限 500）。
- Server Windows 服务：`builder.Host.UseWindowsService`（服务名 LabelFrameServer，LocalSystem）；控制台模式保留供开发；exe 图标改用 labelframe.ico。
- 数据目录改 `%ProgramData%\LabelFrame\server`（server.db / templates.db / logs.db；服务账户下 LOCALAPPDATA 不可靠）。
- 历史数据定期清理：`DataCleanupService`（启动 60s 后按周期执行，默认 24h）删除终态作业超 `JobRetentionDays`（默认 30 天）与日志超 `LogRetentionDays`（默认 90 天）；非终态作业不删；`ServerDb.DeleteTerminalJobsBeforeAsync` + `SqliteLogStore.DeleteBeforeAsync`。
- WinHost 机器级配置：`GET/POST /api/host/config`（serverUrl + deviceId/deviceName，仅回环可写），持久化 `%ProgramData%\LabelFrame\Client\settings.json`（缺失 / 损坏返回默认值；启动加载覆盖 ServerUrl）。
- WinHost 作业列表：`GET /api/jobs`（limit 默认 100 上限 500），JobView 扩展 CreatedAt / FailedItems / ErrorMessage / TargetDeviceId（本机作业为 null）。
- 双 MSI 0.15.0：Server 安装注册 Windows 服务 + 安装完成弹窗（开机自启 / 立即运行，默认勾选；按勾选 `sc config start= auto` / `net start`，升级不触发）；Client 安装完成弹窗（立即打开，默认勾选；启动客户端开界面）；Server 不再打包 web/dist；卸载清理路径含 ProgramData（Server server 目录 / Client settings.json）。
- 测试 156 全绿（Core 60 / Server 13 / WinHost 58 / Studio 25）；产物 `LabelFrame-Server-0.15.0.msi`（7.6MB）、`LabelFrame-Client-0.15.0.msi`（14.2MB）。


## 修复：Server 安装未注册 Windows 服务（0.15.1，2026-08-11）

- 根因：`ServiceInstall` 放在只有注册表 KeyPath 的独立组件，Windows Installer 拿不到服务二进制路径，服务创建被跳过（其余文件正常安装）。
- 修复：`ServiceInstall / ServiceControl` 移入 `LabelFrame.Server.exe` 所在组件（服务二进制 = 组件 KeyPath）；`generate-files.ps1` 新增 `-ServerServiceName / -ServerServiceDisplayName` 参数。
- 版本升至 0.15.1（MajorUpgrade 可覆盖已装的 0.15.0）；产物 `LabelFrame-Server-0.15.1.msi`（7.6MB）、`LabelFrame-Client-0.15.1.msi`（14.2MB）。
- 覆盖升级（0.15.0 → 0.15.1）不弹完成弹窗、不自动启动，服务注册为手动启动，可 `net start LabelFrameServer` 或服务管理器启动；全新安装仍弹完成弹窗（默认自启 + 立即运行）。


## 修复：安装完成弹窗的「自启 / 立即运行 / 立即打开」动作未触发（0.15.2，2026-08-11）

- 现象：0.15.1 全新安装后服务已注册，但 StartType=Manual 且从未启动（弹窗点确认后后续 InstallUISequence 动作不执行——最后一个对话框 EndDialog 后序列结束）。
- 修复：改为弹窗「确认」按钮点击时通过 `DoAction` 直接触发：Server（SetAutoStart → sc config start= auto；StartServiceNow → net start）、Client（LaunchClient）；移除 InstallUISequence 中的尾部 Custom 动作。
- 版本升至 0.15.2；产物 `LabelFrame-Server-0.15.2.msi`（7.6MB）、`LabelFrame-Client-0.15.2.msi`（14.1MB）。


## 简化：Server 服务安装改为“注册即自动 + 安装时启动”，完成弹窗仅提示（0.15.3，2026-08-11）

- 按用户反馈简化：移除 Server 完成弹窗的「开机自启 / 立即运行」勾选项与 `sc config / net start` 自定义动作；`ServiceInstall Start=auto` + `ServiceControl Start=install`（安装即自动 + 启动），完成弹窗仅提示“服务已注册并自动启动”。
- 实装验证：静默安装 0.15.3 后 `START_TYPE=AUTO_START`、服务 RUNNING、healthz OK。
- 双包版本对齐 0.15.3：`LabelFrame-Server-0.15.3.msi`（7.6MB）、`LabelFrame-Client-0.15.3.msi`（14.1MB，Client 交互不变：完成弹窗仍含「立即打开」勾选）。


## 迭代 18 前端合入与联调（0.15.3 客户端，2026-08-11）

- 前端（hermes，0668d03）：F1-F7 全部完成——双 base（serverApi / localApi）、机器级配置（/api/host/config 启动加载 + 保存即生效）、恢复连接方式（TransportPanel）与打印机分组、数据与打印本机设备默认选中 + 顶部连接徽标、作业历史页（limit=100，空态按模式）、Workbench/Designer/PdaLogs 跟随 serverMode 降级；前端测试 125 全绿、build/lint 通过。
- 后端复核：契约对齐（JobView.createdAt / HostConfig.serverUrl-deviceId-deviceName / TransportConfig / PrinterStatus 字段与后端一致）。
- 客户端 MSI 0.15.3 重新打包（含新前端 dist index-DfhhEvBH.js）；端到端冒烟通过：Client 注册在线 → 提交作业 Pending → Completed 2/2 → Log 模拟 PNG 落盘；页面引用新 bundle。


## 0.15.4：推送等效 + 客户端弹窗关闭修复 + 弹窗文字简化（2026-08-11）

- 服务端推送（长轮询通知）：`GET /api/devices/{deviceId}/jobs/notify?timeout=N`——作业入队立即返回 hasPending=true（等效推送），同时刷新设备心跳；客户端 `ServerRoutingWorker` 改为长轮询等待 → 立即领取，打印等待从最多 5s 轮询降为 <1s；网络异常回退间隔重试。
- 客户端安装完成弹窗无法关闭：`LaunchClient` 改为 `cmd /c start` 非阻塞启动（msiexec 不再等待 GUI 进程退出）。
- 弹窗文字去掉「（默认勾选）」「（默认不勾选）」括号说明。
- 测试 162 全绿（Server 17 / WinHost 60 / Core 60 / Studio 25）；产物 `LabelFrame-Server-0.15.4.msi`（7.6MB）、`LabelFrame-Client-0.15.4.msi`（14.1MB）。


## 迭代 19：Ubuntu 服务端部署 + 跨机验证（2026-08-11，进行中）

- Rendering / Server 多目标框架 `net10.0;net10.0-windows`：GDI 预览（LabelPreviewRenderer）仅 Windows（#if WINDOWS）；Server `UseWindowsService` / 应用图标 / WindowsServices 包仅 Windows；Linux 用 systemd。
- SkiaSharp Linux 原生库：新增 `SkiaSharp.NativeAssets.Linux`（net10.0）；发布产物含 `libSkiaSharp.so` / `libe_sqlite3.so`。
- Server 数据目录按平台默认：Windows `%ProgramData%\LabelFrame\server` / Linux `/var/lib/labelframe/server`；`LABELFRAME_SERVER_*` 覆盖。
- 交付：`scripts/publish-server-linux.ps1`（framework-dependent / self-contained，tar.gz 归档）、`scripts/deploy-server-ubuntu.sh`（用户/目录/systemd 自启/防火墙提示）、`packaging/ubuntu/labelframe-server.service`、`packaging/ubuntu/Dockerfile`。
- 测试 162 全绿；Windows Server MSI 打包回归正常；linux-x64 产物 6.7MB（归档）。
- 跨机验证（服务端 Linux + 客户端 Windows）待真机 / 容器执行：验证清单见 docs/archive/ITERATION-19-SPEC.md §5。


## 迭代 19 增补：Docker 镜像交付 + 容器跨机验证（2026-08-11）

- 构建 `labelframe-server:0.15.4`（aspnet:10.0 基础镜像 + libfontconfig/libfreetype/libharfbuzz Skia 依赖），导出离线包 `artifacts\labelframe-server-0.15.4.docker.tar`（106MB）；新增 `packaging/ubuntu/docker-compose.yml`（端口 + 数据卷 + 重启策略）。
- 容器内验证（Linux）：healthz、SQLite（数据落 /var/lib/labelframe/server）、Skia 单张/批量出图全部通过（修复 Skia 缺 fontconfig 依赖）。
- 跨机闭环验证：Windows Client 指向 Linux 容器服务端 → 设备注册 Online → 提交作业 Pending→Claimed 131ms（推送通知）。
- 已知行为：客户端「服务端地址」修改后需重启 Client（打印 Worker 连接使用启动时地址），文档已注明。


## 迭代 19 增补：服务端文本日志 + 日志目录挂载（2026-08-11）

- 新增 `LABELFRAME_SERVER_LOG_FILE` 环境变量：设置后服务端把 ILogger 输出追加写入 UTF-8 文本文件（极简 FileLoggerProvider，含时间/级别/分类）。
- Docker compose / systemd 默认挂载日志目录并启用：容器 `/var/lib/labelframe/logs` → 宿主机目录（compose 默认 `./logs`，生产建议 `/opt/store/labelframe/logs`），`tail -f server.log` 直接查看。
- 已重建 `labelframe-server:0.15.4` 镜像与离线包 `labelframe-server-0.15.4.docker.tar`（容器内验证：日志落盘宿主机挂载点）。

## 迭代 19 增补：安装包先停运行程序 + 作业完成回报不再等长轮询（2026-08-11）

- 安装 / 覆盖升级 / 卸载前先停止运行中的程序：
  - Server MSI 新增 `StopServerService` 自定义动作（`sc.exe stop LabelFrameServer`，安装 / 卸载均先停服务再执行 MSI 自带 StopServices / DeleteServices），避免卸载后服务仍显示运行；
  - Server 停机超时从默认 30s 缩短为 5s（`HostOptions.ShutdownTimeout`），避免客户端长轮询请求拖慢服务停止 / 卸载 / 升级；
  - Client MSI 新增 `KillWinHost` 自定义动作（`taskkill /F /IM LabelFrame.WinHost.exe`，位于安装序列最前），覆盖安装 / 卸载前强制结束托盘程序，避免 exe 占用导致覆盖失败或卸载后残留进程。双 MSI 显式声明 `Codepage="65001"`，保证任意系统区域设置下中文打包。
- 作业完成回报改为独立循环（`ServerRoutingWorker.ReportFinishedLoopAsync`，1s 周期）：本地作业终态后约 1s 内回报 Server，不再被 20s 长轮询阻塞——「已领取 → 已完成」不再延迟；新增回归测试 `Worker_should_report_finished_job_without_waiting_for_long_poll`。

## 迭代 20 文档：服务端管理界面（插件式 UI）+ 设备 IP（2026-08-11）

- 起草 `docs/archive/ITERATION-20-SPEC.md`：客户端状态栏显示本机 IP；服务端可选管理界面以插件形式提供（静态前端包放入 `plugins/web-ui` 即生效，无需重启，默认仍无头）；Server UI 去除打印机相关内容，保留工作台 / 设计器，新增在线设备页，数据与打印复用并改为“在线设备选择器”发送打印测试。
- 契约：`DeviceView.lastIp`、`GET /api/devices/by-ip/{ip}`、`POST /api/jobs` 可选 `targetIp`、`GET /api/host/config` 增加 `ips`、新增 `GET /api/server/info`、前端 `VITE_UI_MODE=client|server` 双构建。
- DESIGN 决策 #61/#62/#63；ROADMAP 新增迭代 20 条目。


## 迭代 20 后端实施（0.16.0，2026-08-11）

- 设备 IP 记录与查找：`devices` 表新增 `last_ip`（旧库 ALTER TABLE 迁移，已存在列静默跳过）；设备注册 / 心跳（notify / pending）从 `RemoteIpAddress` 记录 / 刷新来源 IP，IPv4-mapped IPv6（`::ffff:a.b.c.d`）统一为 IPv4 文本（MapToIPv4）；`DeviceView.lastIp`；新增 `GET /api/devices/by-ip/{ip}`（忽略大小写，未找到 404）；`POST /api/jobs` 支持可选 `targetIp`（与 `targetDeviceId` 二选一，同时提供时 `targetDeviceId` 优先，按 IP 未找到 404）。
- 服务端管理界面插件（默认仍无头，不推翻决策 #53）：`Server.WebUiPath`（默认 Windows `%ProgramData%\LabelFrame\server\plugins\web-ui` / Linux `/var/lib/labelframe/server/plugins/web-ui`，`LABELFRAME_SERVER_WEB_UI` 覆盖、为空显式禁用）；静态托管中间件启动确保目录存在（创建失败降级无头不崩），每次请求运行时检测——放入 `index.html` 即托管（UseDefaultFiles + UseStaticFiles + SPA fallback），移除即恢复无头；fallback 不拦截 `/api/*` 与 `/healthz`；新增 `GET /api/server/info`（`listenUrl / uiEnabled / version`）。
- 插件交付：`scripts/package-server-webui.ps1` 把 `web/dist-server` 打包为 `artifacts/labelframe-server-webui-<version>.zip`；Docker compose 增加可选卷挂载示例（`./plugins/web-ui`）。
- WinHost：`GET /api/host/config` 响应增加 `ips`（本机启用网卡的 IPv4 列表，过滤回环 / 隧道，去重；状态栏展示用）。
- 测试 176 全绿（Core 60 / Server 29 / Studio 25 / WinHost 62，新增迁移 / by-ip / targetIp / IP 规范化 / 插件开关 / ips 枚举用例）；端到端冒烟通过（lastIp 记录、by-ip、targetIp 解析、插件放入即托管、移除恢复无头、插件启用时 API 不受影响）。


## 迭代 20 前端合入与打包（0.16.0，2026-08-11）

- 前端（hermes，be87548）：双构建 `VITE_UI_MODE=client|server`——`build`（web/dist，Client 包）/ `build:server`（web/dist-server，插件包），`uiMode.ts` + `vite-env.d.ts` ImportMetaEnv 声明 + dev proxy 按模式分支 + vitest 显式注入；K1 `getServerBaseUrl()` server 构建恒返回同源相对路径 `''`（不读 localStorage / 机器级配置）；K2 server 构建跳过 localApi 探测、`serverMode` 恒 server 无 standalone；Server UI 菜单裁剪（移除设置与打印机相关内容，新增「在线设备」页，日志页更名「设备日志」）；在线设备页（每 5s 轮询、点击设默认目标 localStorage `labelframe.defaultTargetDeviceId` 跨页联动、离线拦截）；数据与打印在线设备选择器（仅在线可选、离线置灰显示上次心跳、默认目标优先级 用户点选 > 本机设备 > 第一台在线、提交时现拉校验在线（K3 掉线禁止提交不排队）、隐藏逐张重试表格）；客户端状态栏连接后显示本机 IP（/api/host/config.ips，多 IP 逗号分隔、过长省略 title 给全量）。前端测试 151 全绿（client / server 双分支）。
- 打包 0.16.0：`artifacts/LabelFrame-Server-0.16.0.msi`（7.6MB）、`artifacts/LabelFrame-Client-0.16.0.msi`（14.1MB）、`artifacts/labelframe-server-webui-0.16.0.zip`（0.2MB，由 `scripts/package-server-webui.ps1` 打包 web/dist-server）；插件端到端验证：解压到插件目录 → 服务端根路径返回管理界面、静态资源与 /api 正常、`/api/server/info.uiEnabled=true`。


## 迭代 20 打包修复（0.16.0，2026-08-11）

- 修复 Client 安装报错 2732（Directory Manager not initialized）：`KillWinHost` 自定义动作引用 `SystemFolder` 目录属性，却排在 `Before=FindRelatedProducts`（CostInitialize 之前，目录管理器未初始化）；改为 `After=InstallInitialize`，与 Server 包 `StopServerService` 一致。已用详细安装日志复现（2732 于 KillWinHost 处触发）并验证修复（升级安装 Client 0.16.0 成功，KillWinHost 正常执行）。
- 修复包内版本漂移：`main.wxs` / `main-server.wxs` 的 Package Version 硬编码 0.15.5（此前产物文件名 0.16.0 但安装后显示 0.15.5）；改为 `$(var.Version)`，打包脚本 `-d Version=$Version` 传入，避免今后再次漂移。
- 已验证：本机 Client / Server 0.15.5 → 0.16.0 提权升级安装成功，DisplayVersion=0.16.0，Server 服务 Running；产物 `artifacts/LabelFrame-Client-0.16.0.msi`（14.1MB）、`artifacts/LabelFrame-Server-0.16.0.msi`（7.6MB）。


## 迭代 20 收尾：Docker 0.16.0 + 安装完成弹窗优化 + 未来规划（2026-08-12）

- Docker 交付 0.16.0：镜像 `labelframe-server:0.16.0`（393MB，基础镜像 aspnet:10.0 + Skia 依赖）、离线包 `artifacts/labelframe-server-0.16.0.docker.tar`（105.9MB）；`docker-compose.yml` 升级镜像标签并默认挂载插件目录 `./plugins/web-ui:/var/lib/labelframe/server/plugins/web-ui`（README 已写明挂载目录与操作：解压 `labelframe-server-webui-0.16.0.zip` 到该目录即生效、移除即无头）；容器内验证 healthz / `/api/server/info`（uiEnabled）/ 插件页面 200 全部通过。
- Client 安装完成弹窗优化：MSI 原生完成弹窗会被 Windows 焦点策略挡到后台 / 任务栏闪烁——改为安装完成后拉起 WinHost 的 `--install-finished` TopMost 弹窗（默认置前，勾选「立即打开」重启宿主并打开界面）；修复弹窗「确认」点击后不关闭、进程挂起的问题（Application.Run 非模态下 DialogResult 不自动关闭，改为 ShowDialog + 显式 Close，重启宿主用 CreateProcess）；弹窗仅全新安装且非静默（/qn）时出现；已实测全新安装弹窗 TopMost 出现、安装成功。
- 未来规划（仅文档，不实施）：打印机连接方式插件化——把 Log / TCP9100 / Windows 驱动 / Zebra SDK 抽象为传输插件（接口 + 注册表按需装配），第三方厂商可自研接入；见 ROADMAP 待需求 / DESIGN 未决问题。
