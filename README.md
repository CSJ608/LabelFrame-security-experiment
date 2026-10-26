# LabelFrame

<!-- 头图 = 品牌 v02 彩色 lockup 母版（assets/brand/） -->
<p align="left"><img src="assets/brand/lockup-color.svg" width="280" alt="LabelFrame" /></p>

[![CI](https://github.com/marci-labs/LabelFrame/actions/workflows/ci.yml/badge.svg)](https://github.com/marci-labs/LabelFrame/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/marci-labs/LabelFrame?display_name=tag&sort=semver)](https://github.com/marci-labs/LabelFrame/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20%C2%B7%20Linux-blue)

面向仓库场景的标签打印框架：模板契约 + 打印服务 + 设备宿主（PC / PDA）。

## 愿景

**方便仓库完成标签打印，提高办公效率。**

- **方便** = 少步骤、就近打印、零学习成本、少故障、少求人；
- **效率** = 打印快、能批量、不重复劳动、不出错、融进业务动作。

## 它能做什么

- **可视化模板设计器**：画布拖拽编排（毫米网格 / 参考线吸附 / 撤销 / 图层），支持文本 / 条码 / 二维码 / 线 / 矩形 / 容器 / 图片元素，条码二维码实时渲染，打印与预览同源。
- **数据打印**：表单填数或 Excel 导入（列映射）批量打印，可下载按模板生成的 Excel 模板。
- **作业中心**：一次请求多张、异步 jobId、逐张状态、幂等不重打、断点续打、失败项单独重打、历史按设备可查。
- **设备路由**：业务系统提交作业 → 服务端定向投递到指定电脑 / PDA 就近出纸；设备离线暂存、上线即领。
- **传输插件**：TCP 9100 / Windows 驱动 / 日志模拟内置；Zebra 品牌与 TSPL 指令集（TSC 及 TSPL 兼容品牌；TCP 9100 / USB 免驱动直连，原生指令与图片双模式）为官方外置插件（`.lfplugin`，随客户端安装包附带，也可服务端分发安装）；厂商可自研插件接入（服务端集中分发、客户端一键安装）。
- **零打印机验证**：调试出图（PNG / zip 下载）与日志模拟打印，不耗纸排查问题。
- **应用内帮助**：客户端与服务端管理界面内置「帮助」页与就地「?」文档点（字段说明 / 常见问题 / 实时演示），无需翻手册。
- **中英双语**：Web 界面、错误消息与 PDA 宿主支持中文 / 英文，客户端托盘一键切换。

## 快速开始（按你的角色）

### 文员：一台电脑一台打印机（单机模式）

1. 从 [GitHub Releases](https://github.com/marci-labs/LabelFrame/releases) 下载 **`LabelFrame-Bootstrapper-x.x.x.exe`（安装引导程序，推荐）** 双击运行，选「单机一体」拓扑走完向导即可——运行时缺失会自动补装，无需手工准备 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) 与 [Microsoft Edge WebView2 运行时](https://go.microsoft.com/fwlink/p/?LinkId=2124703)。高级路径：分别安装 `LabelFrame-Server-x.x.x.msi` 与 `LabelFrame-Client-x.x.x.msi`（缺失运行时时安装包会给出官方下载链接，需手动补装；见[部署指南](docs/DEPLOY.md) 的「安装引导程序（推荐安装入口）」与「安装包（MSI，高级路径）」两节）。
2. Client 装完会自动打开 **LabelFrame 应用窗口**（自有窗口与任务栏图标，也可从桌面快捷方式打开）；窗口关闭后服务驻留系统托盘，打印不受影响。
3. 「设计器」页新建模板：画布拖入文本 / 条码 / 二维码，毫米级排版，实时预览。
4. 「数据与打印」页填数据（或「下载 Excel 模板」→ 填好 → 导入）。
5. 点打印。第一次使用先在「设置 → 连接方式」选连接（无打印机时选 `log` 模拟验证，出图在 `%LOCALAPPDATA%\LabelFrame\print`）。

### 管理员：一台服务器 + 多台打印电脑

1. 服务器安装 Server（[部署指南](docs/DEPLOY.md) 四种形态任选：MSI / Docker / Ubuntu / 单机同装；Windows 上推荐直接跑安装引导程序选「服务端」拓扑）。
2. 每台打印电脑安装客户端：推荐运行 `LabelFrame-Bootstrapper-x.x.x.exe` 选「打印客户端」拓扑（自动补装运行时），或直装 `LabelFrame-Client-x.x.x.msi`。
3. 每台 Client 的「设置」页把服务端地址改为 `http://<服务器IP>:53961` → 测试连接 → 保存并**重启 Client**。
4. 模板集中在服务端维护，各电脑打印与历史互不干扰；可选用管理界面插件（服务端网页管理）与「下载中心」页集中分发安装包（客户端 MSI + PDA APK，PDA 可扫码下载）。

### 业务系统开发者：HTTP 提交作业

```bash
curl -X POST http://<服务器>:53961/api/jobs \
  -H "Content-Type: application/json" \
  -d '{
    "requestId": "order-123-1",          # 幂等键：重放不重复打印
    "targetDeviceId": "pc-01",           # 目标设备（可按 IP 查设备后提交）
    "templateName": "库位标签",
    "labels": [ { "data": { "zone": "A", "code": "A-01-02-03" } } ]
  }'
# → 202 {"jobId": "...", "status": "Pending"}；GET /api/jobs/{jobId} 查进度与逐张状态
```

设备注册 / 心跳 / 领取 / 回报等完整契约见 [docs/DESIGN.md](docs/DESIGN.md) 的「API 概览」Server 一节。

## 部署形态对照

| 形态 | 服务端 | 适用 |
|---|---|---|
| 单机 | 安装引导程序选「单机一体」（或 Server + Client 两个 MSI 同机安装） | 一台电脑一台打印机 |
| Windows 服务器 | 引导程序「服务端」拓扑（或 Server MSI，Windows 服务，端口 53961） | 局域网多机 |
| Linux / Docker | `docker compose up -d` 或 systemd | 局域网多机 |
| Docker E2E | 稳定版 Server + 当前 Linux Log Client | 无打印机集成回归 |

安装入口与向导流程见[部署指南](docs/DEPLOY.md) 的「安装引导程序（推荐安装入口）」一节；Docker / Ubuntu 步骤、签名与配置项见 **[docs/DEPLOY.md](docs/DEPLOY.md)**。

无外网部署统一采用**两层模型**：在有网准备机从 Release 下载小型 `labelframe-offline-builder-<版本>.zip`，由 Builder 生成所需离线包；再把离线包拷到目标机，由包内 Installer 完成安装。Windows 准备机运行 `build-windows-offline.ps1`，目标服务器解压后运行 `install-server.cmd`；Docker 准备机运行 `build-docker-offline.sh`，目标机解压后继续运行既有 `install.sh`。

## 升级与版本

本地作业库存储采用状态/载荷分表：首次升级启动前需停止所有使用该库的旧宿主，并预留迁移时间及备份空间；新版会先创建一致性备份，再原子迁移旧库。迁移失败时不继续启动打印，应保留数据库及诊断信息排查重试。本轮只支持升级，不提供降级旧宿主的受支持流程或维护工具发行包；不能直接换旧程序、卸载清数据或恢复陈旧备份。详见[作业库升级](docs/DEPLOY.md#本地作业库分表与离线降级)。ServerDb 不受此格式变更影响。

各版本的用户可感知变化与升级注意事项见 [CHANGELOG](CHANGELOG.md)（0.x 历史版本有速览表）。自 1.0.0 起版本号遵循[语义化版本](https://semver.org/lang/zh-CN/)：模板包格式、打印 API、作业模型、插件接口等公共契约在 minor 版本内只做兼容性新增，不破坏既有集成。

| 部署形态 | 怎么升级 | 数据与回滚 |
|---|---|---|
| Windows（单机 / 服务器 / 打印客户端） | 重跑新版安装引导程序（自动进入升级模式，逐组件覆盖）或运行新版 MSI 覆盖安装；客户端运行中升级会自动安全退出 | 模板 / 作业 / 日志与配置默认保留；本地作业库升级后不支持直接降级，MSI 仍禁止降级安装 |
| Linux Docker | 更新 `.env` 中的版本号（或改 `latest`）后 `docker compose up -d` | 数据卷保留；回滚＝改回旧版本号重建 |
| Windows Server 离线包 | 有网准备机运行 Builder 的 `build-windows-offline.ps1` 生成 Full / Upgrade；目标机解压后运行 `install-server.cmd` | Full 含三类离线运行时（WebView2 为 x64 Standalone），Installer 只按需补 Server 所需 ASP.NET Core；Upgrade 零 runtime，缺前置时改用 Full；配置与数据保留 |
| Docker 离线部署包 | 有网准备机运行 `build-docker-offline.sh` 生成归档；在**当初部署的同一目录**解压新版后继续运行包内 `install.sh`（勿换目录） | 数据卷与配置不动；回滚＝旧版包同目录重跑 |
| PDA（Android） | 下载中心扫码或直接下载新版 APK 覆盖安装 | 配置保留（同签名来源）；换签名来源的 APK 须先卸载重装 |

升级顺序建议（先服务端后客户端）、模板兼容注意与各形态详细步骤见[部署指南](docs/DEPLOY.md) 的「升级与迁移」一节。

## 仓库结构

| 项目 | 说明 |
|---|---|
| `src/LabelFrame.Core` | 契约 / 版式模型、数据校验、ZPL 图片编码、作业队列、模板库、传输插件接口 |
| `src/LabelFrame.Rendering` | Skia 整版渲染（打印与预览同源） |
| `src/LabelFrame.Api` | Server / WinHost 共享的 HTTP 契约（DTO / 错误码）与端点实现 |
| `src/LabelFrame.Server` | 无头服务端：模板库 / 作业中心 / 设备投递 / 调试出图 / 日志 |
| `src/LabelFrame.WinHost` | Client Host 共享实现：Windows 完整客户端；Linux 无头 Log 测试客户端 |
| `src/LabelFrame.TransportPlugin.Zebra` / `src/LabelFrame.TransportPlugin.Tspl` | 官方外置传输插件工程（`.lfplugin` 包：Zebra 品牌指令集；TSPL 指令集，服务 TSC 及 TSPL 兼容品牌） |
| `src/LabelFrame.AndroidHost` | Android / PDA 打印宿主（真机验收通过；CI 构建并随发版出 APK，见其 README） |
| `web/` | Web 前端（Vite + React + TS + Konva）：客户端界面与服务端管理界面双构建 |

## 开发

前置：.NET 10 SDK；前端使用 Node.js 26.12.1（根目录 `.node-version`）和 pnpm 12.15.0（`web/package.json` 的 `packageManager`）。开发、CI 与 E2E 构建使用相同版本；安装依赖使用 `pnpm install --frozen-lockfile`。

```bash
dotnet build LabelFrame.slnx        # 构建
dotnet test LabelFrame.slnx --filter "FullyQualifiedName!~Perf&FullyQualifiedName!~Soak"  # 日常测试
cd web && pnpm install --frozen-lockfile && pnpm dev  # 前端开发（连本机 WinHost）
```

- 无打印机验证打印闭环：`powershell -ExecutionPolicy Bypass -File .\scripts\demo-winhost.ps1`。
- Docker 验证 Server / Linux Client 路由闭环：源码候选运行 `powershell -ExecutionPolicy Bypass -File .\scripts\test-linux-client-e2e.ps1`；正式同版本镜像运行 `powershell -ExecutionPolicy Bypass -File .\scripts\test-linux-client-e2e.ps1 -ComposeFile packaging/e2e/compose.release.yaml -SkipBuild`，详见 [docs/LINUX-CLIENT-E2E.md](docs/LINUX-CLIENT-E2E.md)。
- 提交即跑 CI（`.github/workflows/ci.yml`：dotnet 构建 / 测试 + 前端 lint / 双模式测试 / 构建 + MSI 结构断言 + Android 构建）；`master` 受门禁保护——变更走短分支 + PR，三项必需检查全绿后 squash 合并（流程见 [docs/WORKFLOW.md](docs/WORKFLOW.md)）。
- 发版：更新 ROADMAP / CHANGELOG 后推送 `v*` tag，由 `release.yml` 自动构建发布（双 MSI / 官方插件包 / Linux 归档 / Android APK / ghcr 镜像 / 离线部署 Builder，见 [docs/DEPLOY.md](docs/DEPLOY.md) 的「自动化发布与签名」一节）。Release 保留原子资源、镜像、安装清单与小型 Builder；Windows Full / Upgrade 与 Docker Full 由管理员按需生成，不再随每版重复上传。Release 构建要求 Android 签名 Secrets 齐备（缺失即失败、不回退 debug 签名）；keystore 证书管理与换签名影响（卸载重装 + 设备号变化）见 [docs/DEPLOY.md](docs/DEPLOY.md) 与 [AndroidHost README](src/LabelFrame.AndroidHost/README.md)。

## 文档

- [docs/DESIGN.md](docs/DESIGN.md) —— 架构设计与决策记录
- [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) —— 需求：场景、底线、能力、边界、成功衡量
- [docs/ROADMAP.md](docs/ROADMAP.md) —— 迭代状态总览（迭代详情在 GitHub Issue）
- [CHANGELOG.md](CHANGELOG.md) —— 更新日志：各版本用户可感知变化与升级注意（含 0.x 历史速览）
- [docs/WORKFLOW.md](docs/WORKFLOW.md) —— 工作流：Issue 立项、PR 门禁、事实源映射
- [docs/DEPLOY.md](docs/DEPLOY.md) —— 部署指南（MSI / Docker / Ubuntu / 分发 / 签名）
- [docs/TEST-MATRIX.md](docs/TEST-MATRIX.md) —— 功能清单、发布测试大纲与证据边界
- [AGENTS.md](AGENTS.md) —— 给 AI 协作的常驻约束
