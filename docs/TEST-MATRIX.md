# LabelFrame 功能与测试矩阵

本文是发布测试大纲与证据索引。功能定义以 [REQUIREMENTS](REQUIREMENTS.md) 为准，设计边界以 [DESIGN](DESIGN.md) 为准，未闭环真机项以打 `待验收` 标签的 Issue 为准。

## 1. 分级与门禁

| 标记 | 含义 | 发布要求 |
|---|---|---|
| P0 | 打印底线、数据不丢、服务可启动、核心发布制品 | 每次发布必须有自动化证据；涉及物理设备的部分保留最近一次真机验收记录 |
| P1 | 模板工具、批量操作、运维与扩展能力 | 单元 / 组件 / HTTP 集成至少一层；关键用户主链在大版本做浏览器或安装冒烟 |
| P2 | 性能、兼容扩展与低频运维 | nightly、基准或按需人工验收，不阻断日常 CI |

证据类型：`单元/组件` 在进程内验证规则；`HTTP 集成` 使用与生产一致的宿主装配；`制品 E2E` 运行真实 Docker 镜像；`浏览器/真机` 验证用户交互或物理世界结果。不能用较低层证据替代更高层边界。

## 2. 核心打印与数据链路

| 优先级 | 功能 | 单元 / 组件证据 | HTTP 集成证据 | 制品 / 浏览器 / 真机证据 | 当前结论 |
|---|---|---|---|---|---|
| P0 | 契约必填校验、元素 JSON 兼容 | Core `Validation` / `Json` | 作业端点 400 契约 | Compose 作业使用真实模板 | 自动门禁完整 |
| P0 | Skia 整版渲染、中文、文本 / 条码 / 二维码 / 图片 / 线 / 区域 | WinHost `Rendering` | 预览 / render-image 返回 PNG | Compose 逐张复制 PNG，检查非空白并解码 Code128 | 自动门禁完整；真实扫码见试点记录 |
| P0 | `^GF` 编码与 DPI / 尺寸 | Core `ZplImageEncoderTests` | JobSubmissionService 全链 | Windows 真机已验收；Linux Log 不发送打印机 | 自动 + 既有真机证据 |
| P0 | 作业持久化、批内顺序、挂起 / 恢复 / 取消 / 失败项重打 | Core `LabelJobQueueTests`、WinHost Jobs | WinHost 作业全生命周期 | Windows UI 主链既有验收；Linux E2E 验证持久化 | 自动门禁完整 |
| P0 | requestId 幂等、不重打 | ServerService / Server endpoints | Server 完整宿主幂等重放 | Compose 对同一请求重放并断言同一 jobId | 自动门禁完整 |
| P0 | 设备注册、心跳、定向投递、领取、终态回报 | ServerService / Routing Worker | Server 注册→提交→领取→回报 | Compose 真实双进程闭环 | 自动门禁完整 |
| P1 | 反代下设备来源 IP 的转发头信任三态（关闭＝TCP 对端直取、行为不变 / 受信网段＝按 X-Forwarded-For 改写真实 IP / 不受信对端＝头不采信＋告警） | ServerOptions 受信代理解析（IP / CIDR / *） | 完整宿主集成（注册→by-ip→targetIp 链路，X-Forwarded-For 三态断言） | 不依赖真机（纯 HTTP 头语义，反代形态以配置等效覆盖） | 自动门禁完整 |
| P0 | 设备离线作业暂存、恢复后继续 | Routing 单元 / Server 持久层 | Server 路由集成 | Compose 停 Client 后提交，启动后完成 | 自动门禁完整 |
| P0 | Pending 暂存 TTL 过期（Expired 终态 / 领取过滤兜底 / requestId 重放不重新投递） | PendingJobTtlTests（FakeTimeProvider） | 完整宿主集成（含 TTL=0 回归锚点、notify 积压立即返回） | 不依赖真机（纯服务端时钟语义） | 自动门禁完整 |
| P0 | Client 重启后本地历史保留并继续领取 | 队列持久层 / Worker | WinHost HTTP 作业列表 | Compose 重启前后作业与新作业双断言 | 自动门禁完整 |
| P0 | 批次节流＋打印 DPI 设置（203/300/600 白名单、GET 回生效值、保存即生效） | BatchPrintPolicy + FakeTimeProvider Worker；`PrintSettingsApiTests` / `PrintSettingsStoreTests`（DPI 白名单与持久化） | print-settings HTTP；100 张 / 10 张一批既有联调记录；DPI 切换真机实证 | 自动 + 既有联调证据 |
| P0 | 错误响应与未捕获异常不泄露 | Api / 两宿主异常测试 | 统一 ErrorView / 500 集成 | 浏览器展示人话错误按主链抽查 | 自动门禁完整 |

## 3. 模板、Excel 与前端

| 优先级 | 功能 | 单元 / 组件证据 | HTTP 集成证据 | 制品 / 浏览器证据 | 当前结论 |
|---|---|---|---|---|---|
| P1 | 模板 CRUD、分组、testData | TemplateStore / Template JSON | 共享模板端点 | Release E2E 保存、列表、详情、删除 | 自动门禁完整 |
| P1 | 模板包导入 / 导出与图片资源 | TemplatePackageTests | 共享模板端点 | Release E2E 导出→删除→导入 | 自动门禁完整 |
| P1 | 模板预览与打印同源 | Skia renderer | 模板 preview DPI 回归 | Release E2E 预览 PNG 解码 | 自动门禁完整 |
| P1 | Web 设计器元素属性、图层、快捷键、历史、吸附 | web design 单元 + PropsPanel / SidePanel | 模板 API | 浏览器实际新建 / 保存 / 重开 / 打印 | 组件充分；浏览器为大版本冒烟 |
| P1 | 前端 client / server 双模式导航与权限边界 | App 双模式组件测试 | 两宿主端点装配 | Server UI 与 Windows Client 分别冒烟 | 自动 + 浏览器抽查 |
| P1 | 测试数据表单、调试出图、单张 / 批量提交 | DataPrint 双模式组件测试 | jobs / render-image(s) | Compose 浏览器定向打印 | 自动 + 浏览器主链 |
| P1 | Excel 模板生成、导入解析 | Core Excel + web mapping | 真实 xlsx 生成→上传回读 | 浏览器下载后续填 A3:A5，命名区域仍止于 A2，导入完整识别 4 行 | 自动门禁完整；续填欠账已关闭 |
| P1 | DataPrint 会话草稿与切页恢复 | AppContext / draft / DataPrint 组件 | 不适用 | 浏览器按需抽查 | 组件门禁完整 |
| P1 | 作业历史、在线设备 | 页面组件测试 | jobs / devices | Server UI 浏览器抽查 | 自动 + 浏览器抽查（「PDA 日志」页已下线，仅保留 /api/logs 端点） |

## 4. 传输、插件与分发

| 优先级 | 功能 | 自动化证据 | 制品 / 真机证据 | 当前结论 |
|---|---|---|---|---|
| P0 | Log 传输与 PNG 输出 | LogPrintTransport / JobSubmissionService | Linux 镜像逐张 PNG 解码 | 发布门禁完整 |
| P0 | TCP 9100 发送、连接失败与状态近似 | Tcp9100 transport tests | Windows 真实设备打印既有验收 | 自动 + 真机证据（状态口径经真机实证：缺纸位静态不报、走纸后才置位） |
| P0 | Windows 驱动传输 | RawPrinterTransport tests | Windows 真机既有验收 | Linux 明确不包含 |
| P0 | Zebra SDK 发送与状态读取 | Zebra transport tests + PrinterStatusTests | 真机三场景（正常 / 人工缺纸 / 人工暂停）× 双路径（SDK `GetCurrentStatus` / TCP `~HS`）实证 | 已真机确认（字段语义与映射口径见 DESIGN 决策表） |
| P0 | TSPL 插件：原生指令编译（TEXT / BARCODE / QRCODE / BAR / BOX）＋图片 BITMAP 编码（反色选项）＋参数声明 | `TsplLabelCompilerTests` / `TsplImageEncoderTests` / `TsplImageDispatchTests`（分派字节不变 + 二进制载荷）/ `TsplTransportPluginParameterTests` | TSC TT065-60 真机验收（编译出纸效果 / 位极性取反 / USB 设备路径 / `ESC ! ?` 状态语义 / 反色两态） | 自动 + 真机证据 |
| P1 | TSPL USB 免驱动设备发现（CM 在位枚举＋自动发现逐台「打开＋应答探测」取第一台可通讯打印机） | `UsbPrinterDeviceDiscoveryTests`（路径解析与 GUID 尾巴保留回归）/ `TsplPrinterTransportTests`（探测跳过、失败清单与显式指定提示语义） | 多 USB 打印机真机（挂驱动非目标机在场时选中目标 TSC、已拔出设备不入列） | 自动 + 真机证据 |
| P1 | 官方插件存量懒激活迁移（zebra / tspl 旧配置自动安装附带包） | `OfficialPluginMigrationTests` | 升级走查既有记录 | 自动门禁完整 |
| P1 | 连接参数校验、先测试后生效、旧配置迁移 | TransportManager / config / plugin registry / OfficialPluginMigrationTests | WinHost HTTP + 设置页组件 | 自动门禁完整 |
| P1 | 插件发现、隔离加载、包校验、安装 / 卸载 | Core package / loader + PluginInstaller | WinHost HTTP；既有 16 步联调 | 自动门禁完整；无签名边界已记录 |
| P1 | Client / 插件包服务端分发 | Server service + 页面组件 | 上传 / 列表 / 下载 / 删除集成 | 自动门禁完整 |
| P1 | Linux Client 能力裁剪 | ClientHost tests | 镜像 health / 传输列表，UI 与插件接口 404 | 发布门禁完整 |
| P1 | Niimbot 蓝牙 | 已放弃（不再承接） | 不适用 | 蓝牙需求降级待需求（REQUIREMENTS §6） |

## 5. 部署、可靠性与非功能

| 优先级 | 功能 | 自动化证据 | 制品 / 人工证据 | 当前结论 |
|---|---|---|---|---|
| P0 | Release 构建、双 MSI、Linux Server 归档 | release workflow | GitHub Release 附件 | v0.22.0 已验证 |
| P0 | Server / Linux Client 版本镜像 | 候选镜像 Compose E2E 后原镜像推送 | 发布后 pull 双稳定镜像复验 | v0.22.0 已验证 |
| P0 | SQLite WAL、并发领取、清理保留 | 数据层 / Server tests | soak 监控 WAL / 堆 / 错误率 | 日常 + nightly 分层 |
| P0 | CI 测试口径稳定 | CI 排除 Perf / Soak | Release 同口径，nightly 单独执行 | 已对齐 |
| P1 | MSI 向导结构、中文、目录、自启契约 | CI `verify-msi-ui.ps1` | 视觉 / 净机 / 升级保留待人工清单 | 结构自动，视觉仍欠账 |
| P1 | Ubuntu systemd 部署 | 脚本与发布归档构建 | 2026-08-17 跨机验收 | 已有真机证据 |
| P1 | 服务端可选管理界面 | ServerPluginUi tests + web server build | Docker 测试 Compose 显式启用 | 默认无头边界保持 |
| P2 | 延迟 / 并发门槛 | Perf Trait | nightly-perf | 不进日常 / Release 测试 |
| P2 | 15 分钟稳态与微基准 | Soak Trait / BenchmarkDotNet | nightly artifact / 日志 | 周期门禁 |
| P2 | PDA Android 宿主 | AndroidHost.Tests ＋ CI Android 构建检查 | 2026-09-08 UROVO DT50 真机主体验收（真实 IP 打印机物理出纸）；APK 随发版发布 | 主线已验收；蓝牙 / USB 传输真机欠账见 [#407](https://github.com/marci-labs/LabelFrame/issues/407)，16KB 运行时验证待 Android 15+ 16KB 内核设备（DESIGN「风险与未决问题」在案） |

## 6. 标准执行口径

```powershell
dotnet build LabelFrame.slnx -c Release
dotnet test LabelFrame.slnx -c Release --no-build --filter "FullyQualifiedName!~Perf&FullyQualifiedName!~Soak"

Push-Location web
pnpm lint
pnpm test
$env:VITE_UI_MODE = "server"; pnpm test
Remove-Item Env:VITE_UI_MODE
pnpm build
pnpm build:server
Pop-Location

# 源码候选组合
.\scripts\test-linux-client-e2e.ps1

# 只引用同版本正式镜像的组合
$env:LABELFRAME_VERSION = "1.0.0"
.\scripts\test-linux-client-e2e.ps1 -ComposeFile packaging/e2e/compose.release.yaml -SkipBuild
```

发布判定必须同时记录命令结果、CI / Release run、Compose 作业与 PNG 证据、浏览器冒烟结论。物理打印能力只能引用真实打印机 / 扫码枪验收，Linux Log 镜像不能替代。
