# LabelFrame 部署指南

面向管理员 / IT：安装包、Docker、Ubuntu、管理界面插件、分发通道与常见配置。
快速上手见 [README](../README.md)；架构与决策见 [DESIGN](DESIGN.md)。

## 1. 部署形态对照

| 形态 | 服务端 | 打印电脑 | 适用 |
|---|---|---|---|
| 单机 | Server MSI + Client MSI 同机安装 | 同一台 | 一台电脑一台打印机 |
| Windows 服务器 | Server MSI（Windows 服务 `LabelFrameServer`，默认 0.0.0.0:53961） | 每台装 Client MSI | 局域网多机 |
| Linux 服务器 | Docker（推荐）或 systemd | 每台装 Client MSI | 局域网多机 |
| Linux 自动化测试 | Server Docker + Linux Client Docker | `log` 模拟输出 | 无打印机的发布制品 E2E |

默认端口：Server `53961`、Client 本机界面 `53960`、PDA 宿主 `53970`。

## 2. 安装引导程序（推荐安装入口）

面向全新机器或测试环境的首装用户：从 [GitHub Releases](https://github.com/marci-labs/LabelFrame/releases) 下载 **`LabelFrame-Bootstrapper-x.x.x.exe`**（安装引导程序），双击运行即可走完「七页向导」完成安装——引导程序按 install manifest 自动获取当版组件与前置运行时，无需先懂组件清单。

- **七页向导流程**：欢迎（新装 / 升级检测，含离线安装指引）→ 拓扑预设（单机一体 / 服务端 / 打印客户端等，选错可重跑改选）→ 品牌多选（按已装打印机驱动名预选：检测到 ZDesigner 预选 Zebra、检测到 TSC 预选「TSC 及 TSPL 兼容机」；选项来自清单插件条目）→ 管理界面开关（服务端网页管理界面，可选）→ 确认安装计划（组件清单 + 本机已装版本对照）→ 安装进度（逐组件下载 + 校验 + 安装，失败自动回滚并给出中文失败分类）→ 完成。
- **前置说明**：引导程序自带 .NET 10 Desktop Runtime / ASP.NET Core Runtime / WebView2 运行时**前置链**——本机缺失时自动按官方直链下载补装（已装则跳过），无需手工预装运行时；这正是它优于 MSI 直装的主要场景（MSI 直装缺失运行时会拦截并给下载链接，需手动补装）。
- **升级**：本机已装 LabelFrame 时重跑引导程序即进入升级模式（列出可升级组件「现版本 → 新版本」，复用同一安装链完成覆盖升级，用户配置保留）；客户端设置页「检查更新」发现新版本时也会指向此入口。
- **离线 / 内网**：统一采用**两层模型：有网 Builder / 离线 Installer**。在有网准备机从 Release 下载 `labelframe-offline-builder-<版本>.zip` 并生成 Windows Full / Upgrade；目标服务器解压后运行 `install-server.cmd`，由包内 Installer 零外网安装 / 升级 Server、更新管理界面并分发 Client / PDA / 官方插件。在线 Burn 引导程序与 `--layout` 能力保持不变，但不再是新离线 Server 包的目标机入口。
- **SmartScreen「未知发布者」提示**：引导 EXE 当前使用自签证书签名（与 MSI 同通道，见「自动化发布与签名」一节）——公网下载首次运行 Windows 可能提示「未知发布者」，点「更多信息 → 仍要运行」即可；下载完整性由 install manifest 的 sha256 强制校验保障。内网可把自签根证书加入受信任根消除提示。
- **安装日志（排障）**：Burn 引擎日志在 `%TEMP%\LabelFrame*.log`（失败报告页可直接打开本次日志；按通配查找）。
- 本地手工构建（发版链之外）：`scripts\build-bundle.ps1 -Version x.y.z`（需 WiX v7；Secrets 在场时 `-Sign` 复用 MSI 证书签名）。

## 3. 安装包（MSI，高级路径）

下载：[GitHub Releases](https://github.com/marci-labs/LabelFrame/releases)。

- **LabelFrame-Server-x.x.x.msi** → `C:\Program Files\LabelFrame\Server`：无头服务端（模板库 / 作业中心 / 设备投递 / 调试出图 / 日志 / Excel），不接打印机、不提供 Web UI；安装为 Windows 服务 `LabelFrameServer`，数据在 `%ProgramData%\LabelFrame\server`。
- **LabelFrame-Client-x.x.x.msi** → `C:\Program Files\LabelFrame\Client`：打印客户端，以应用窗口（WebView2 嵌入界面壳）托管完整界面（模板设计 / 数据与打印 / 连接配置 / 作业历史；不提供「PDA 日志」页——设备日志回传能力暂未包含），本机地址 `http://127.0.0.1:53960`；窗口关闭后服务驻留系统托盘（真正退出走托盘「退出」）。

要点：

- 前置：.NET 10 Desktop Runtime（x64）+ Microsoft Edge WebView2 运行时（Evergreen，Win10/11 多数已随 Edge 预装）。MSI 内置检测：.NET 缺失时 NetCoreCheck 自检弹出可点击的官方下载链接（不自动安装）；WebView2 缺失时全 UI 安装显示带官方下载链接的中文对话框（装完点「重新检测」即可继续，无需重启安装程序），静默 / 基础 UI 由 LaunchCondition 拦截提示。客户端启动时若 WebView2 初始化失败，自动回退默认浏览器打开界面并在 host.log 记录原因。
- 单机使用 = 同机安装两个包；多台打印电脑 = 每台装 Client，设置页把服务端地址指向服务端 IP。
- 两个包的 appsettings.json 均为独立用户配置组件：覆盖安装 / 修复不覆盖、卸载保留。卸载时可选是否清除用户数据（默认不清除）。
- 公开下载的 MSI 若未用受信任商业证书签名，Windows 可能提示「未知发布者」，点「仍要运行」即可；内网可把自签根证书加入受信任根消除提示（见「自动化发布与签名」一节）。
- 打印默认整版位图（Skia 渲染，与画布预览同源；图片指令按连接分派——默认 ZPL `^GF`，TSPL 连接输出 `BITMAP`）；具备原生指令能力的官方插件（Zebra / TSPL）可将文本、条码、二维码等直出打印机指令（连接的「打印方式」参数可选）；连接方式在客户端「设置」页配置（先测试后生效）。
- **修改服务端地址后需重启 Client**（打印 Worker 使用启动时的地址）。
- 清理历史安装残留：管理员运行 `scripts\cleanup-residue.ps1`。

### 3.1 Windows 离线 / 内网部署（两层模型：Builder / Installer）

Windows 离线部署分为两步：**有网准备机运行 Builder 组包；无外网目标服务器运行 Installer 安装**。Release 提供小型 `labelframe-offline-builder-<版本>.zip`，Builder 已固定对应 Release 版本，无需另填版本号。

#### 第一步：有网准备机生成 Full / Upgrade

准备机需 Windows PowerShell 5.1 或 PowerShell 7，并能访问 GitHub Releases、微软 .NET 与 WebView2 下载源。解压 Builder 后运行：

```powershell
.\build-windows-offline.ps1 -OutputDir D:\LabelFrameOffline
# 可选：复用下载缓存
.\build-windows-offline.ps1 -OutputDir D:\LabelFrameOffline -CacheDir D:\LabelFrameCache
```

生成两个包：

| 输出 | 适用 | runtime 载荷 |
|---|---|---|
| `labelframe-offline-<版本>-win-x64.zip` **Full** | 新服务器首装；也可作为升级缺前置时的兜底 | .NET Desktop、ASP.NET Core、WebView2 x64 **Standalone** 三类完整离线安装器 |
| `labelframe-upgrade-<版本>-win-x64.zip` **Upgrade** | 已装服务器升级 | **零 runtime**；目标机缺基础 .NET 10 或 ASP.NET Core 10+ 时拒绝执行并提示改用 Full |

Builder 对 LabelFrame 原子资源与固定版 .NET runtime 做 SHA256 / 大小校验；WebView2 Full 使用 `MicrosoftEdgeWebView2RuntimeInstallerX64.exe`（x64 Standalone，不是联网 Bootstrapper），校验固定文件名、体积大于 100 MB 与 Microsoft Authenticode 发布者；任一来源、哈希或签名异常均非零退出，不留下最终命名的残包。缓存一致时复用，不一致时删除重取。每个输出包含 `offline-build-info.json` 记录实际来源、哈希、大小与生成时间。

#### 第二步：离线目标服务器安装

把 Full 或 Upgrade 拷到目标服务器并解压，双击：

```cmd
install-server.cmd
```

也可在管理员 PowerShell 中运行：

```powershell
.\install-server.ps1
```

Installer 只部署 **Server 与服务端分发资源**，不会自动安装打印客户端。它会：

1. 校验 `SHA256SUMS`、包版本、Full / Upgrade runtime 集合与受管资源清单；
2. Full 缺基础 .NET 10 host/runtime 时先静默补装 Desktop Runtime，再按需补装 ASP.NET Core Runtime；WebView2 作为客户端后续人工安装的离线介质保留，不自动安装；Upgrade 不安装任何 runtime，任一 Server 前置缺失时提示改用 Full；
3. 静默安装 / 升级 Server MSI，并输出 MSI 与 Installer 日志路径；
4. 整体替换管理界面，失败时恢复旧 Web UI；`LABELFRAME_SERVER_WEB_UI` 已明确设为空白时保持无头模式；
5. 把 Client MSI、PDA APK、Zebra / TSPL 官方插件分发到服务端实际配置目录；
6. 验证 Windows 服务 Running、`/healthz`、服务版本、管理界面模式及下载中心文件哈希；
7. 全链成功后仅按受管账本清理旧官方资源；第三方插件、用户上传件和被用户改写的文件保留。

同版本重跑幂等；升级保留 `%ProgramData%\LabelFrame` 下的数据与配置。Full / Upgrade 均可完成 Server 升级，差别只在是否携带 runtime。

> 在线安装引导程序仍使用 WebView2 Evergreen 小型 Bootstrapper；只有离线 Full 使用 x64 Standalone。`scripts\make-offline-layout.ps1` / 引导程序 `--layout` 仍可用于在线 Burn 的自定义组件布局，不等同于这里的 Server Installer 包。


## 4. Docker（推荐的服务端部署方式）

**快速启动（Release compose 分发）**：从 [GitHub Releases](https://github.com/marci-labs/LabelFrame/releases) 下载 `compose.yml` 与 `.env` 放同一目录，`docker compose up -d` 即可——`.env` 已把 `LABELFRAME_VERSION` 钉定为本 Release 版本（想跟随最新版可改回 `latest`，镜像源覆盖见文件内注释）。两者与仓库 `packaging/ubuntu/docker-compose.yml` 同源（发版流水线直接复制生成）。把 LabelFrame **并入你自己管理多服务的 compose** 见「整合进已有 compose」一节；**离线 / 内网环境**从同 Release 下载离线部署 Builder，按「离线 / 内网部署（两层模型）」一节生成 Docker 离线包。

镜像 `ghcr.io/marci-labs/labelframe-server`（`latest` 指向最新版）：

```bash
docker pull ghcr.io/marci-labs/labelframe-server:latest
docker run -d --name labelframe-server -p 53961:53961 \
  -v labelframe-data:/var/lib/labelframe/server \
  --restart unless-stopped ghcr.io/marci-labs/labelframe-server:latest
# 或 docker compose -f packaging/ubuntu/docker-compose.yml up -d
curl http://127.0.0.1:53961/healthz   # {"service":"LabelFrame.Server","status":"ok"}
```

- 数据（server.db / templates.db / logs.db）在数据卷 `/var/lib/labelframe/server`；文本日志按日轮转写入挂载目录 `./logs/server-<yyyyMMdd>.log`（`LABELFRAME_SERVER_LOG_FILE` 为基准路径，`tail -f ./logs/server-$(date +%Y%m%d).log` 即可；默认保留 31 个文件，超期自动清理，`LABELFRAME_SERVER_LOG_FILE_RETENTION_DAYS` 可调）。单文件超过大小上限（默认 50MB，`LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB`，0 = 不限）时轮转为当日序号文件 `server-<yyyyMMdd>.N.log` 继续写——总占用上界 ≈ 31 文件 × 50MB ≈ 1.55GB。`logs.db` 另有按量闸（默认 256MB，超阈删最旧并 `VACUUM` 真实回收磁盘，见「配置与环境变量」一节）。日志路径无效时服务不再启动失败——跳过文件通道、控制台输出中文告警，服务继续运行。
- Server 镜像已内置 `fonts-wqy-microhei`，服务端管理界面的模板预览 / 出图预览默认使用 `WenQuanYi Micro Hei` 渲染中文文本。
- compose 已默认挂载 `./plugins/web-ui`（管理界面插件）与 `./client-packages`（客户端安装包分发），见下文「服务端管理界面（可选插件）」与「分发通道」两节。
- 自行构建：`docker build -f packaging/ubuntu/Dockerfile -t labelframe-server artifacts/server-linux/linux-x64`。
- 本地构建镜像调试：`LABELFRAME_IMAGE=labelframe-server LABELFRAME_VERSION=1.0.0 docker compose up -d`。

### 4.1 整合进已有 compose（在线）

把 LabelFrame 服务端并入你自己管理多服务的 docker-compose（同一个 compose 文件 / 同一个默认网络），两个动作：**① 服务块并入你的 compose（单根挂载）；② 跑一次资源落位脚本**。

① 服务块（整合形态——与独立部署 compose 的差异：无 `container_name`（固定名在共享 compose 是冲突源，同 compose 内服务名即 DNS 名）；无命名卷（单根 bind mount 路径自控，不受 compose 项目名 / 目录影响）；`expose` 默认仅网络内可达）：

```yaml
services:
  labelframe-server:            # 服务名即网络内 DNS 名，你的其他服务用 http://labelframe-server:53961 访问；重名可改
    image: ghcr.io/marci-labs/labelframe-server:${LABELFRAME_VERSION:-latest}
    restart: unless-stopped
    expose:
      - "53961"
    # 宿主机 / 局域网浏览器直访才改用 ports（二选一）：
    # ports:
    #   - "53961:53961"
    volumes:
      # 单根挂载：数据库 / 管理界面网页 / 三类分发目录 / 文本日志全部在这一棵树内
      # （子目录缺失 = 对应能力不启用，服务照常运行）
      - ./labelframe:/var/lib/labelframe
    environment:
      - TZ=Asia/Shanghai
      - LABELFRAME_SERVER_LOG_FILE=/var/lib/labelframe/logs/server.log
```

在 compose 同目录 `.env` 钉 `LABELFRAME_VERSION=1.0.0`（镜像 tag 与下面的资源脚本共用同一版本变量，防镜像 / 资源错配）。

② 资源在线落位（管理界面网页 zip + 客户端 MSI / PDA APK / 官方传输插件包（Zebra / TSPL）——一条命令按挂载布局放好）：

```bash
curl -fsSL https://raw.githubusercontent.com/marci-labs/LabelFrame/master/scripts/fetch-server-resources.sh \
  | bash -s -- ./labelframe
```

- 脚本消费发版流水线的 `install-manifest.json`（**sha256 逐源强制校验，fail-closed**），五类资源落位到 `./labelframe/server/` 下的 `plugins/web-ui/`（解压展开、原子换名）、`client-packages/`、`pda-packages/`、`plugin-packages/`（Zebra 与 TSPL 两个 `.lfplugin`；TSPL 自 1.1.0 起收录，旧版清单无此条目时自动跳过）；
- **版本对齐**：默认读同目录 `.env` 的 `LABELFRAME_VERSION`（与镜像同版）；`--version 1.0.0` 显式指定；两者皆无走官方 latest 通道（运行时提示所用版本）；
- 幂等重跑：已就位且校验相符的包文件跳过下载；旧版本包保留（下载中心允许多版本共存）；
- 前置：`curl`（或 `wget`）+ `unzip` + `sha256sum`；纯用户态，无需 root；`--manifest <文件 / URL>` 可指定清单（测试或镜像源）；
- 也可直接使用仓库脚本：`bash scripts/fetch-server-resources.sh ./labelframe`（`--help` 查看全部参数）。

落位后的布局（`docker compose up -d` 后访问 `http://<宿主机>:53961/healthz` 自检，管理界面即开箱可用）：

```
./labelframe/
├── server/
│   ├── plugins/web-ui/        管理界面（经服务端 :53961 访问）
│   ├── client-packages/       客户端 MSI（客户端「更新与安装包」可下载）
│   ├── pda-packages/          PDA APK（管理界面「下载中心」扫码下载）
│   ├── plugin-packages/       传输插件 .lfplugin（客户端「插件管理」可安装）
│   └── server.db 等           服务端启动时自动生成
└── logs/                      文本日志（按日轮转）
```

升级：改 `.env` 版本 → `docker compose pull labelframe-server && docker compose up -d` → 重跑落位脚本（网页与包刷新到新版，数据不动）。

注意（信任与网络边界）：

- **服务端 API 无鉴权**（局域网信任模型）：并入多服务网络后，该网络内所有容器都能访问其 API——仅接入受信任网络；需要对外时请前置带鉴权的反向代理。
- 反向代理：**不支持路径前缀形态**（如 `https://host/labelframe/`）——用独立子域名或独立端口转发 `:53961`。
- 数据库为 SQLite：`./labelframe` 请落在本地文件系统，勿挂 NFS / 网络卷（文件锁不可靠）。
- ghcr.io 拉取不畅：`image` 覆盖为可达镜像源；完全离线环境改用离线部署包（下一节）。

### 4.2 Server + Linux Log Client 本地 E2E

正式镜像 `ghcr.io/marci-labs/labelframe-client` 是仅用于无打印机自动化测试的 Linux 无头 Client。它固定使用 `log` 模拟打印，不包含 TCP 9100、USB、Windows 驱动、Zebra SDK、官方传输插件（Zebra / TSPL）、第三方插件或客户端 Web UI，不能替代物理打印验收。

验证已发布的同版本 Server / Client（Compose 不含 `build`）：

```powershell
$env:LABELFRAME_VERSION = "1.0.0"
powershell -ExecutionPolicy Bypass -File .\scripts\test-linux-client-e2e.ps1 `
  -ComposeFile packaging/e2e/compose.release.yaml -SkipBuild
docker compose -f .\packaging\e2e\compose.release.yaml down
```

验证当前源码候选：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-linux-client-e2e.ps1
# 通过后环境保持运行：http://127.0.0.1:53910
docker compose -f .\packaging\e2e\compose.yaml down
```

脚本验证 Linux 能力边界、模板 / 预览 / 包导入导出、Excel / 日志公共端点、设备注册、幂等、单张 / 多张、离线暂存、Skia 渲染、PNG 数量与条码内容、Server 终态回报、Client 重启持久化及重启后继续领取；数据保存在 Compose 命名卷。端口冲突时传 `-ServerPort <端口>`。完整测试大纲与排障方式见 [LINUX-CLIENT-E2E.md](LINUX-CLIENT-E2E.md)。

### 4.3 离线 / 内网部署（两层模型：Builder / `install.sh`）

Docker 离线部署同样采用**有网 Builder / 离线 Installer**。在有网且已安装 Docker 的准备机解压 `labelframe-offline-builder-<版本>.zip`，运行：

```bash
bash build-docker-offline.sh --output-dir ./dist
# 可选：复用下载缓存
bash build-docker-offline.sh --output-dir ./dist --cache-dir ./cache
```

Builder 按 Release 记录的不可变 digest 拉取并复核 Server 镜像，下载 Client MSI、PDA APK、Web UI 与官方插件并按 SHA256 校验，然后生成：

```text
labelframe-offline-<版本>-linux-x64.tar.gz
labelframe-offline-<版本>-linux-x64.tar.gz.sha256
```

目标机前置不变：Linux x64、Docker Engine（含 Compose v2）与 `sha256sum`。把归档与旁车一并拷到无外网目标机，先验证整个归档，再解压到固定部署目录并运行包内既有 `install.sh`：

```bash
sha256sum -c labelframe-offline-<版本>-linux-x64.tar.gz.sha256
mkdir -p labelframe-offline
tar -xzf labelframe-offline-<版本>-linux-x64.tar.gz -C labelframe-offline --strip-components=1
cd labelframe-offline
bash install.sh
curl http://127.0.0.1:53961/healthz
```

生成包的结构与既有 Docker 离线包兼容；`install.sh` 的校验、`docker load`、Compose 启动、管理界面与 Client / PDA / 插件分发、健康检查、外部网络选项、固定目录升级和幂等重跑语义均不变。升级仍须把新包解压到**当初部署的同一目录**后重跑 `bash install.sh`，否则会使用新的数据卷。

- 完整性：归档旁车在执行包内脚本前校验整个 tar.gz；`install.sh` 再强制校验除 `SHA256SUMS` 自身外的全部部署文件；Builder 任一哈希或镜像 digest 不符即拒绝产包；
- 审计：包内 `offline-build-info.json` 记录镜像 RepoDigest 与实际资源哈希；
- 分发闭环：`install.sh` 把 `packages/` 下 Client MSI、PDA APK、官方插件复制到挂载目录，管理界面已预解压；
- 可选共享网络：继续使用部署目录的 `network.env` 或 `LABELFRAME_EXTERNAL_NETWORK=<网络名> bash install.sh`；
- 存量目录：曾按版本号目录部署的，仍在原目录覆盖解压并运行 `install.sh`，无需迁移数据。


## 5. Ubuntu（systemd 裸机部署）

**一键安装（推荐）**：`install-server-linux.sh` 是 Linux 服务端的「安装程序」——一条命令完成下载校验、解包、systemd 服务与管理界面落位（契约见 [DESIGN.md](DESIGN.md)）：

```bash
# 在线安装（官方稳定通道，默认含管理界面；目标 Ubuntu 22.04 / 24.04 需 curl + unzip）
curl -fsSL -o install-server-linux.sh \
  https://raw.githubusercontent.com/marci-labs/LabelFrame/master/scripts/install-server-linux.sh
sudo bash install-server-linux.sh

# 离线 / 内网：先在有网机器生成布局目录（make-offline-layout.ps1），拷到服务器后
sudo bash install-server-linux.sh --manifest /path/to/布局目录        # 本地文件优先，零外网请求

# 常用变体
sudo bash install-server-linux.sh --no-webui                          # 不装管理界面
sudo bash install-server-linux.sh --manifest <清单 URL>               # 安装指定版本
```

- 脚本消费发版流水线生成的 `install-manifest.json`：sha256 逐源强制校验、多源回退（fail-closed，不装不明文件）；安装完成自检 `/healthz` 并核对版本。
- Release 归档默认 **self-contained**（免装 .NET）；framework-dependent 归档（本地构建或旧版附件）需目标机先装 [.NET 10 ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)，缺失时脚本明确报错并给出该官方链接，不自动安装。
- 幂等：升级 = 重跑同一命令（覆盖升级；`appsettings.json` 用户配置与 `/var/lib/labelframe` 数据、日志目录不动）。
- 高级路径（归档已在手 / 自定义构建）：
  1. Windows 上发布 linux-x64 包：
     ```powershell
     .\scripts\publish-server-linux.ps1                 # self-contained（默认，免运行时）
     .\scripts\publish-server-linux.ps1 -FrameworkDependent   # 需目标机自备 runtime
     ```
  2. 上传归档后部署：`sudo bash scripts/deploy-server-ubuntu.sh labelframe-server-x.x.x-linux-x64.tar.gz`
     脚本会：建 `labelframe` 用户 → 解压 `/opt/labelframe/server` → 数据目录 `/var/lib/labelframe/server` → 安装并启动 systemd 服务（自启 + 崩溃重启）。
  3. 防火墙放行：`sudo ufw allow 53961/tcp`；Windows Client 设置页填 `http://<Ubuntu-IP>:53961`。

## 6. 升级与迁移

**下表的程序回滚说明以数据库格式兼容及安装器允许为前提，不代表本版支持客户端降级。** 本轮只支持本地作业库正向升级，不提供旧宿主降级流程；旧程序不能直接读取分表库，MSI 仍禁止降级。ServerDb、模板库和日志库不属于本次分表迁移范围。

总原则：**模板、作业历史、日志与配置在各部署形态下都与程序分离存放，正常升级不清数据**；升级前建议对数据目录做一次备份（Windows：`%ProgramData%\LabelFrame`；Linux：`/var/lib/labelframe`）。各版本的变化与升级注意事项见[更新日志](../CHANGELOG.md)。

| 形态 | 升级方法 | 数据与回滚 |
|---|---|---|
| Windows（引导程序 / MSI） | 本机已装 LabelFrame 时**重跑新版安装引导程序**即进入升级模式（列出可升级组件「现版本 → 新版本」，复用同一安装链覆盖升级）；或直接运行新版 MSI 覆盖安装——客户端升级会自动安全退出正在运行的程序再安装。客户端「检查更新」发现新版本时同样指向引导程序入口 | 数据在 `%ProgramData%\LabelFrame`（模板 / 作业 / 日志），`appsettings.json` 与用户配置在覆盖安装 / 卸载时均默认保留（卸载时可选清除，默认不清除）；回滚＝运行旧版本引导程序或 MSI 覆盖安装 |
| Linux Docker（compose 分发） | 编辑 `.env` 把 `LABELFRAME_VERSION` 改为新版本（或 `latest`）→ `docker compose pull && docker compose up -d` | 数据在数据卷（默认 `labelframe-data`），重建容器不丢失；回滚＝改回旧版本号后重建 |
| Ubuntu（systemd） | 幂等设计：**重跑 `sudo bash install-server-linux.sh`** 即覆盖升级（也可 `--manifest` 指定版本） | `/var/lib/labelframe` 数据与 `appsettings.json` 不动；回滚＝`--manifest` 指定旧版本重跑 |
| Windows Server 离线包 | 有网准备机用 Builder 的 `build-windows-offline.ps1` 生成 Full / Upgrade；目标机解压后运行 `install-server.cmd`。Upgrade 零 runtime，缺 ASP.NET Core 10+ 时改用 Full | `%ProgramData%\LabelFrame` 数据与配置保留；官方分发资源按账本收敛，第三方与自定义文件保留；回滚＝用旧版 Builder 生成对应包后安装旧版 |
| Docker 离线部署包 | 有网准备机用 `build-docker-offline.sh` 生成归档；新版归档解压到**当初部署的同一目录**（`--strip-components=1`）后继续运行 `bash install.sh`；**勿换目录升级** | 卷数据保留；旧版本镜像文件可删 `images/` 下旧 `.image.tar.gz` 释放磁盘；回滚＝旧版包同目录重跑 |
| PDA（Android 宿主） | 服务端「下载中心」扫码或直接下载新版 APK 覆盖安装（GitHub Release 与下载中心分发的都是同一签名的 Release 构建） | 配置（服务器地址 / 打印机 / 设备名）保留；**换签名来源的 APK 须先卸载重装**——配置会被清空且设备号变化（Server 设备目录出现新条目），详见「自动化发布与签名」一节 |

补充要点：

- **升级顺序**：建议先升级服务端，再升级各打印客户端与 PDA；跨版本升级前先读[更新日志](../CHANGELOG.md)当版的「升级注意」。
- **模板兼容**：新版本可继续打开既有模板；但**旧版本程序可能不认识新版本模板中的新元素**（如图片元素）——把模板回传给旧版本使用前请先确认。
- **插件随行**：Zebra / TSPL 官方传输插件随客户端 MSI 附带并在启动时自动安装 / 升级（见「分发通道」一节），升级客户端无需单独处理插件。
- **服务端管理界面（可选插件）**：经安装引导程序安装的随升级自动处理；手动放置 `web-ui` 插件的，升级后用新版 `labelframe-server-webui-*.zip` 覆盖目录即可（放入即生效）。

### 本地作业库分表与离线降级

Windows/Linux/Android 使用 Core 的本地打印作业库受影响，服务端投递数据库 `ServerDb` 不受影响；不要仅凭文件叫 jobs.db 就判断库用途，须核对宿主配置的实际路径。本轮只支持升级，不提供降级旧宿主的受支持流程、维护工具发行附件或 PDA 维护入口。

升级前结束打印作业及结果回报，停止使用同一数据库的全部旧宿主，保留配置与数据并预留维护时间及磁盘空间；正常安装升级不清空数据。首次启动会先建立一致性备份再迁移，迁移成功后才启动队列服务。失败时不要反复强制重启、卸载清数据或换回旧程序；保留原库、备份及日志，排除空间不足、文件占用或未知结构后重试。共享内核的桌面回归不能替代 PDA 实际升级验证，平台证据由本轮发版 Issue 记录。

空间预检针对数据库及备份实际路径，不使用系统根分区的空闲量代替；查询失败也阻断。PDA 先建立前台服务通知，再后台初始化；未就绪期间本地 HTTP 与 Worker 不开放。停止或快速重启会取消旧代并等待后台排空，不在主线程等待迁移，也不启动重叠 Worker。初始化失败记录本地日志（含已创建备份的路径与校验标志）并停止服务；APK 构建与托管测试不代表真机升级验收完成。

新空库原子创建 `user_version=1`、`jobs/job_items/job_payloads`；旧版 `user_version=0` 的已知结构首次初始化时在无池独占连接中备份并迁移。未知版本/结构、缺失载荷、孤立关系或空间不足会拒绝启动，迁移异常不再吞掉。已有分表库重复初始化只做快照下结构/关系检查，不每次独占迁移。其他宿主/连接池仍占用库时可能取锁失败：停止全部宿主后重试，不杀进程、不清全局连接池。

以下为**开发者离线验证参考，不是本轮产品降级操作指南**。维护工具仅保留源码，未加入 Release/MSI/Linux 归档或 Android APK；正常升级无需使用它。在有 .NET 10 SDK 的 Windows/Linux 隔离验证机，从仓库根目录构建；执行 DLL 的机器需要 .NET 10 运行时及对应原生库（保留完整输出目录）：

```text
dotnet build tools/LabelFrame.JobStorageTool -c Release
dotnet tools/LabelFrame.JobStorageTool/bin/Release/net10.0/LabelFrame.JobStorageTool.dll to-legacy <当前本地作业库绝对路径> --hosts-stopped
```

也支持 `to-split` 显式正向维护。工具不创建缺失库；参数缺失退出码 2、转换失败 1、取消 130、成功或格式已匹配 0。`--hosts-stopped` 是操作者确认而非自动停机机制；SQLite 独占锁进一步阻止并发数据库访问，但不能替代关闭宿主（空闲宿主随后仍可能重新打开库）。

反向迁移源码验证要求：停止所有使用此库的宿主并禁用自动重启 → 对**当前库**运行 `to-legacy`（工具先做当前一致性备份）→ 确认退出码 0 → 在隔离环境验证旧存储读取当前库。这不等于提供旧版安装流程。不要在反向迁移后先启动新版，它会再次正向迁移。反向迁移保留新作业、幂等键、最新状态和全部载荷，不恢复迁移前陈旧备份；失败保持原格式。

同目录备份命名为 `.before-split-<唯一标识>.db` 或 `.before-legacy-<唯一标识>.db`，通过 SQLite 备份 API 包含 WAL 已提交内容，不生拷在线主文件。备份不覆盖、不自动清理；失败时输出路径及是否完成验证，**未验证文件不能当作可恢复副本**。空间预检为逻辑页大小四倍加 64MiB 余量，不是磁盘峰值硬保证；实际文件系统故障仍可能发生。大历史库应预留维护窗口与额外空间。Ctrl+C 在下一可取消边界响应，原生备份/单条 SQL 期间不承诺即时取消。

Windows/Linux 可在停机后的原数据路径维护，须具备目录写权限；无需启动 UI 或打印传输。Android 应用私有库通常不能由普通桌面工具直接访问，当前没有 APK 内维护入口或受支持的通用导出/回灌流程，因此**不能承诺普通 PDA 可直接完成离线降级**。需由管理员先建立保留数据库及 WAL 一致性的、可验证的设备维护通道，或继续使用新版；不要卸载应用清数据或恢复旧备份作为绕过方案。Android 工具集成与发行包分发不在本次本地验证完成范围。

## 7. 服务端管理界面（可选插件）

服务端默认无头（仅 `/healthz` + API）。需要管理界面时，把前端 server 构建产物（`web/dist-server`，或 Release 里的 `labelframe-server-webui-x.x.x.zip`）放进插件目录即可——**放入即生效、移除即无头，无需重启**：

- Windows：`%ProgramData%\LabelFrame\server\plugins\web-ui`
- Linux / Docker：`/var/lib/labelframe/server/plugins/web-ui`（compose 默认挂载 `./plugins/web-ui`）

打包脚本：`scripts/package-server-webui.ps1`。管理界面与客户端界面是同一前端的两种构建（工作台 / 设计器 / 在线设备 / 作业历史 / 下载中心 / 插件管理），无打印机相关内容。

## 8. 分发通道

### 客户端安装包分发（服务端集中下载）

- 目录 `client-packages`（Windows `%ProgramData%\LabelFrame\server\client-packages`；Linux `/var/lib/labelframe/server/client-packages`；环境变量 `LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）。
- 目录直放文件或经管理界面「下载中心」页上传；客户端设置页「更新与安装包」列出手工下载包。上传草稿不会自动触发安装；自动升级另需下方的明确发布步骤。
- API：`GET/POST /api/client-packages`、`GET/DELETE /api/client-packages/{file}`（路径穿越防护）。

### Windows 客户端受控自动升级

1. **先部署更新服务端**。配置 `Server.UpdateAdminToken` 或环境变量 `LABELFRAME_SERVER_UPDATE_ADMIN_TOKEN`，使用至少 32 字符的随机高熵口令。管理发布仅接受 HTTPS 或服务端本机回环请求，并核对浏览器同源；未配置口令时不能发布。TLS 在 Kestrel 终止，或管理员在服务端本机打开管理界面；现有转发头只处理来源 IP，不把任意 `X-Forwarded-Proto` 当作 HTTPS。口令仅在本页内存使用，不写浏览器存储。
2. **客户端首次由管理员手工安装一次新版 MSI**。该 MSI 部署 `LabelFrameClientUpdater` Windows 服务；客户端继续用原普通用户运行。安装于受管理员保护的 Program Files 目录才支持自动安装；自定义用户可写目录保留手工升级方式。可用 `Get-Service LabelFrameClientUpdater` 检查服务存在和运行状态。
3. **上传后明确发布**。服务端下载中心 → Windows 包管理 → 输入升级管理口令 → 选择草稿 → 指定设备 ID（留空为全量）→「校验并发布升级」。发布校验真实 Client 产品/版本/x64 架构、内容摘要、固定发布者和兼容范围；发布后内容和范围不能覆盖，可暂停/恢复。建议先指定少量终端验证，再将后续版本分批发布。
4. **客户端选择方式**。「设置 → 更新与安装包」默认「自动下载，确认安装」，下载好后全局提示可一键「安装并重启客户端」。专用常驻终端可显式保存「维护时间自动安装」，默认本机 02:00–04:00；跨午夜窗口支持，相同起止无效。只有界面已关闭到托盘且 10 分钟未操作、整批任务与结果回报完成、没有未保存编辑和在途操作时才安装；错过窗口等待下次，不中断作业，不自动重启操作系统。
5. **失败与恢复**。更新后先检查真实安装/运行版本、插件加载、连接未回退到模拟打印、本机 API 和路由注册。失败保留打印保护并显示状态；安装失败但旧版完整恢复则恢复旧版业务并禁止该版本自动反复重试。管理员修复后可用设置/托盘一键重启重新检查。日志位于安装目录同级 `ClientUpdateState/<事务 ID>/install.log`，服务拒绝原因在 Windows 应用程序事件日志；用户更新设置和恢复记录在作业数据库目录下 `updates/`（默认 `%LOCALAPPDATA%\LabelFrame\updates`）。原用户等待器 20 分钟未等到安装结果会显示错误并留 `update-relaunch-error.txt`，不强杀安装器。

发布者公开证书随程序放置为 `update-publisher.cer`，来自当前可信 Release；应用自行验签，不导入系统根证书。旧版 1.4.0 安装包没有自动升级兼容元数据，只能手工分发。未来包必须用同一可信发布者签名并包含 `LABELFRAME_MIN_SERVER_VERSION` / `LABELFRAME_MAX_SERVER_VERSION` / `LABELFRAME_RUNTIME_MAJOR`；证书过期/换签或运行时主版本变化须管理员部署，不能跳过验证。Linux 原生服务端需安装 `msitools`、`osslsigncode`、`ca-certificates`；Docker 镜像已包含。不可变发布目录为 `Server.ClientUpdatesPath`（环境变量 `LABELFRAME_SERVER_CLIENT_UPDATES`，默认服务器数据目录下 `client-updates`），应纳入服务端数据持久化和备份。

此功能不负责 Server、PDA 或 Linux 客户端自动升级。Windows Installer 自身失败回滚与自动降级是两件事；现有 MSI 禁止降级，不提供自动安装旧版本的承诺。完整协议与安装事务见 [设计](design/client-updates.md)。

### PDA（Android 宿主）安装包分发与扫码下载

Android 受控升级采用独立的 `/api/android-updates` 发布面；草稿上传不等于发布。当前实现阶段仅系统确认安装，DT50 厂商静默安装因安全文件交接未通过而禁用，不能用共享 Download 目录或放宽权限绕过。完整客户端交付与验收状态见 [#474](https://github.com/marci-labs/LabelFrame/issues/474)。

Docker 部署继续沿用原来的镜像＋资源包更新方式：新镜像内置 APK 校验所需工具和 LabelFrame 既有正式 Android 发布证书指纹，不需要现场安装 Java/Android SDK、挂载工具目录或新增容器。资源包仍将 APK 放入 `pda-packages`；管理员在下载中心选择 APK 和设备批次发布，PDA 后台下载后由用户系统确认安装。单纯替换资源包不自动向所有设备安装。发布快照使用现有数据卷，无额外挂载；已有更新管理口令与 HTTPS 反代继续复用。

以下配置用于非容器部署或自定义工具/签名覆盖，普通官方 Docker 部署不必逐项设置。缺失或配置错误时拒绝受控发布，原手工分发仍可用：

| 配置 / 环境变量 | 用途 |
|---|---|
| `Server.AndroidJavaPath` / `LABELFRAME_ANDROID_JAVA` | JDK 17+ 的 `java` / `java.exe` 绝对路径 |
| `Server.AndroidApksignerJarPath` / `LABELFRAME_ANDROID_APKSIGNER_JAR` | 官方 Android SDK Build Tools `lib/apksigner.jar` 绝对路径 |
| `Server.AndroidAapt2Path` / `LABELFRAME_ANDROID_AAPT2` | 同套 Build Tools `aapt2` / `aapt2.exe` 绝对路径 |
| `Server.AndroidPublisherSha256` / `LABELFRAME_ANDROID_PUBLISHER_SHA256` | 管理员从可信发行证书预先核实的 64 位十六进制 SHA256，不从上传包自动认领 |
| `Server.AndroidUpdatesPath` / `LABELFRAME_SERVER_ANDROID_UPDATES` | 独立快照目录，默认服务器数据目录下 `android-updates`，须持久化与备份 |

管理权限复用 `LABELFRAME_SERVER_UPDATE_ADMIN_TOKEN` 和 HTTPS/回环＋Origin 校验；口令不放 URL 或浏览器持久存储。非容器 Windows/Linux 需要 JDK（仅 JRE 不满足源文件启动器）及适合本平台的工具。Docker 构建时内置 JDK17、Google Maven apksig 8.9.2 与 aapt2 8.9.2-12782657（固定 SHA256），运行时不下载工具。内置可信指纹来自已核验的 v1.5.0/v1.4.0 正式 APK，证据见 [发布核验](https://github.com/marci-labs/LabelFrame/issues/468)；自签环境可覆盖，不会自动信任上传包。程序随附 `ApkVerifierBridge.java` 检查完整签名；只接受非调试、单一签名、无轮转且具有更新元数据的宿主包。旧版无协议元数据 APK 继续手工分发，不能以修改文件名使其成为受控更新。

PDA 历史路由未知或安装恢复异常可在「应用更新 → 管理员人工恢复」处理：输入原服务端更新管理口令及核对原因，阅读风险后确认。仍有整批/挂起作业、待回报或活动安装会话时不能跳过；原异常留在应用私有 `recovery-audit`，结果标记为人工恢复而不是升级成功。此入口不自动重打、不删除数据；服务端不可达时不能验证管理员口令，需恢复连接后重试。

最多 100 个发布批次、每包最多 64MiB；同版同内容可分批发布，同版不同内容禁止。全量设备须明确确认；暂停影响候选和下载资格，但不能保证撤销已提交系统的安装。状态是带服务端接收时间的设备自报，不是设备身份认证或实时健康证明。暂不提供自动降级、自动信任新签名或卸载清数据换签名路径。

- 目录 `pda-packages`（Windows `%ProgramData%\LabelFrame\server\pda-packages`；Linux `/var/lib/labelframe/server/pda-packages`；环境变量 `LABELFRAME_SERVER_PDA_PACKAGES` 可覆盖；Docker compose 默认挂载 `./pda-packages`）——与 `client-packages` 模式对称。
- 目录直放 APK 或经管理界面「下载中心」页「PDA 下载」区上传（上传仅接受 `.apk`；目录直放不限制扩展名）。
- API：`GET/POST /api/pda-packages`、`GET/DELETE /api/pda-packages/{file}`（路径穿越防护；不存在 404 + `LF_SRV_010`）。**APK 下载响应 MIME 固定 `application/vnd.android.package-archive`**——Android 浏览器据此识别为安装包直接拉起安装。
- **PDA 扫码装机（推荐路径）**：PDA 与服务器连同一局域网 → 打开服务端管理界面「下载中心」页 → PDA 相机 / 扫码工具扫条目旁二维码 → 浏览器下载 APK → 按页面提示完成「未知来源 / 安装未知应用」一次性授权后安装。二维码内容 = 管理员浏览器正在访问的局域网地址 + 该条目下载路径。
- 升级安装请使用同一签名来源的 APK（GitHub Release 与本目录分发的都是同一 keystore 签名的 Release 构建，可直接覆盖安装）；换签名的影响见「自动化发布与签名」一节。

### 传输插件分发（`.lfplugin`）

- 插件包 = zip（根 `manifest.json`：pluginId / name / version 必填 + 插件 DLL）；服务端独立 `plugin-packages` 目录 + `/api/plugin-packages` 上传 / 列表 / 下载（64MB 上限、上传即校验）。
- 客户端设置页「插件管理」：浏览 → 安装（下载 → 三层校验 → 解压到 `%ProgramData%\LabelFrame\Client\plugins\<pluginId>\`）→ **重启客户端生效**；卸载 = 删目录 + 重启。外部插件字节加载，不锁文件。
- 外部插件 DLL 也可手动放入 `%ProgramData%\LabelFrame\Client\plugins`（`LABELFRAME_PLUGINS` 可覆盖），单个加载失败只记日志不影响宿主。
- 连接配置：`%LOCALAPPDATA%\LabelFrame\connection.json`，格式 `{ "pluginId": "tcp9100", "params": { "host": "...", "port": "9100" } }`；旧格式自动迁移。
- 内置传输插件：`log`（模拟打印）、`tcp9100`、`winspool`（Windows 驱动）。插件接口见 DESIGN「传输插件」相关决策记录。
- **Zebra 品牌传输已外置为官方插件**：插件 id `labelframe-transport-zebra`，`.lfplugin` 包随 GitHub Release 发布（`labelframe-transport-zebra-<版本>.lfplugin`）并经客户端 MSI 附带（安装目录 `plugin-packages\`）；连接配置引用 Zebra 时插件未装则客户端启动自动从附带包安装（升级无断裂），也可在「插件管理」手动安装 / 升级（官方插件覆盖安装带版本比较：新版本覆盖、同版本幂等、降级需先卸载）。未安装该插件时 Zebra 品牌不可用（连接引用则回退默认连接并在 host.log 留痕）。
- **TSPL 指令集传输已外置为官方插件**：插件 id `labelframe-transport-tspl`，服务 TSC 及 TSPL 兼容品牌（指令集维度单插件）；TCP 9100 网口与 USB 免驱动直连双通道，原生指令模式（文本 / Code 128 条码 / 二维码 / 线 / 矩形框直出指令）与图片模式（整版 `BITMAP` 位图，带「反色处理」开关应对位极性相反的兼容机型）双能力；打印机状态（`ESC ! ?`：缺纸 / 卡纸 / 打印头开 / 色带尽 / 暂停等）实时查询。分发与安装语义同 Zebra：`.lfplugin` 包随 GitHub Release 发布并经客户端 MSI 附带、连接引用未装时启动自动安装、覆盖安装带版本比较。

## 9. 自动化发布与签名

- 发版两步：① 更新 `docs/ROADMAP.md` 与 `CHANGELOG.md` 提交推送；② 例如 `git tag v1.0.0 && git push origin v1.0.0`。
- CI 自动：构建测试 → 双 MSI（可签名）→ 管理界面插件 zip → 官方传输插件包（Zebra / TSPL `.lfplugin`）→ Linux 归档 → **Android APK（PDA 宿主）** → 同一次构建的 Server / Linux Client 候选镜像通过 Compose E2E → 原镜像推 ghcr.io（版本号 + `latest`）并锚定 Server 镜像 digest → 生成 `labelframe-offline-builder-<版本>.zip` → GitHub Release（正文自动取 `CHANGELOG.md` 当版小节——用户视角更新说明在前、GitHub 自动提交列表附后；小节缺失即发版失败，发版顺序保证记账 PR 先合、tag 后推）。
- **安装引导 EXE**：发版链含独立 `bundle` job——按当版 install manifest（分阶段生成，runtime 哈希跨 job 一致性断言 fail-closed）构建 `LabelFrame-Bootstrapper-<版本>.exe` 并随 Release 附件发布（「安装引导程序（推荐安装入口）」一节的推荐入口即此产物）；下载多源与哈希校验语义见 DESIGN.md。
- **Linux 归档与 compose 分发**：Linux 归档默认 self-contained（`install-server-linux.sh` 一键安装免 .NET 前置，见「Ubuntu（systemd 裸机部署）」一节）；Release 附件含 `compose.yml` + `.env`（与 `packaging/ubuntu/docker-compose.yml` 同源复制、`.env` 钉定当版版本，「Docker（推荐的服务端部署方式）」一节的快速启动即此产物；两者不进 install manifest——部署描述文件而非可安装产物）。
- **离线部署 Builder**：Release 保留全部原子资源、Server 镜像、安装清单、compose 附件与小型 Builder，不再重复上传 Windows Full、Windows Upgrade、Docker Full 三类大包。管理员按需用 Builder 生成相同命名和选择语义的离线包；来源清单钉定当版资源哈希与 Server 镜像 release-time digest。
- MSI 签名：配置 Secret `MSI_SIGN_CERT_BASE64` / `MSI_SIGN_PASSWORD` 时自动签名，否则跳过。当前为自签证书过渡方案（公开下载仍可能 SmartScreen 提示），正式对外分发建议购买 OV 代码签名证书。本地签名：`scripts\create-signing-cert.ps1` 生成证书，`scripts\build-msi.ps1 -Version x.y.z -Sign` 使用。
- **引导 EXE 签名**：与 MSI 复用同一对 Secrets（`MSI_SIGN_CERT_BASE64` / `MSI_SIGN_PASSWORD`，`build-bundle.ps1 -Sign`）；未配置时跳过签名，完整性由 manifest sha256 校验保障。
- Android APK 签名：必须配置全部四个 Secrets——`ANDROID_KEYSTORE_BASE64` / `ANDROID_KEYSTORE_PASSWORD` / `ANDROID_KEY_ALIAS` / `ANDROID_KEY_PASSWORD`——用专用自签 keystore 签名；**任一缺失即构建失败（`::error::` 后 exit 1），绝不回退 debug 签名**（此前「缺失回退 debug 签名并告警」的过渡路径已移除：回退路径存在 = 两次构建可能签名不一致，用户无法覆盖升级且换签名会重置 ANDROID_ID 致设备号漂移）。日常 CI 的 Android 构建检查（ci.yml 第三必需检查）仍用 debug 签名验证可构建，不对外分发。
- **keystore 证书管理（重要）**：
  - 一次性生成并写入 Secrets：`scripts\create-android-keystore.ps1 -Password '<强密码>' -SetGithubSecrets`（生成 `labelframe-release.keystore` 并把四个 Secret 写入仓库）。
  - **托管位置**：keystore 文件与密码只存两处——GitHub 仓库 Secrets（发版流水线使用）+ 生成方离线备份（密码管理器 / 加密盘）。**不得提交进仓库**。
  - **备份要求**：keystore 或密码丢失 = 无法再发同签名升级包（只能换签名，见下），务必在生成后立即离线备份并记录别名与两个密码；GitHub Secrets 可随时重写（keystore 文件还在即可恢复）。
  - **换签名影响**：换签名后已装设备**需先卸载旧版再安装**——卸载会清空配置（服务器地址 / 打印机 IP / 设备名称需重填）；且 Android 8+ 的设备号（ANDROID_ID）绑定签名密钥，**换签名后设备号会变**，Server 设备目录出现新条目（旧条目停留显示离线，可忽略）。PDA 安装 / 覆盖升级详见 [AndroidHost README](../src/LabelFrame.AndroidHost/README.md)。

## 10. 配置与环境变量

- 服务端监听 / 数据库路径 / 历史清理保留期（作业默认 30 天、日志默认 90 天）/ 日志大小上限（文本单文件默认 50MB、logs.db 按量闸默认 256MB）均可用 `LABELFRAME_SERVER_*` 环境变量覆盖（systemd 单元已设默认值）。
- 服务端暂存作业过期：设备离线期间暂存的 Pending 作业默认 **12 小时**未投递即放弃（终态 Expired，作业历史可见、不重新投递，业务系统重打需用新 requestId 重发）；TTL 只对 Pending 计龄，作业被设备领取后不再计龄。配置 `Server.PendingJobTtlHours`（`LABELFRAME_SERVER_PENDING_TTL_HOURS`），设为 0 或负值 = 关闭过期（行为与现状一致）；过期扫描周期 `Server.ExpirationScanIntervalMinutes`（默认 5 分钟，`LABELFRAME_SERVER_EXPIRATION_SCAN_MINUTES`）。例：
  ```bash
  LABELFRAME_SERVER_PENDING_TTL_HOURS=4      # 暂存 4 小时未投递即放弃
  LABELFRAME_SERVER_PENDING_TTL_HOURS=0      # 关闭过期（长期离线设备需人工处理）
  ```
- 服务端 Claimed 作业超时回收：作业被设备领取后默认 **30 分钟**未回报终态（宿主崩溃 / 重启后映射丢失），服务端按「宿主失联超时」回收为终态 **Failed**（原因含错误码 `LF_SRV_009` 与「结果未知，可能已实际打印；需重打请用新 requestId 重发」），不再停留 Claimed 直至历史清理；不自动重新投递（防重复打印），迟到的真实回报按幂等重放返回既有终态。判定只以领取时间 + 服务端时钟为准、与设备在线状态无关。配置 `Server.ClaimedJobTimeoutMinutes`（`LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES`），0 或负值 = 关闭回收（沿用现状）。例：
  ```bash
  LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES=30   # 默认：领取后 30 分钟未回报即回收为 Failed
  LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES=720  # 长挂起场景（打印机离线数小时续打）调大余量
  LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES=0    # 关闭回收（Claimed 永不超时，需人工处理）
  ```
- WinHost：`appsettings.json` 的 `WinHost` 节 + `LABELFRAME_*` 环境变量覆盖；常用传输变量示例：
  ```powershell
  $env:LABELFRAME_TRANSPORT = "Zebra"        # Zebra SDK；或 Tcp / WindowsDriver / Log
  $env:LABELFRAME_TCP_HOST = "192.168.1.50"
  $env:LABELFRAME_PRINTER = "ZDesigner ZD421-203dpi ZPL"
  dotnet run --project src\LabelFrame.WinHost
  ```
- 日志轮转与保留（含服务端单文件大小上限）：三处文件日志统一**按日轮转、默认保留 31 个（天）**——服务端 `server-<yyyyMMdd>.log`（`LABELFRAME_SERVER_LOG_FILE_RETENTION_DAYS`）、客户端宿主 `host-<yyyyMMdd>.log`（`LABELFRAME_HOST_LOG_RETENTION_DAYS`）、客户端 Serilog `app-*.log`（`LABELFRAME_APP_LOG_RETENTION_DAYS`）；均设 0 或负值 = 不清理（不设上限）。`LABELFRAME_SERVER_LOG_FILE` / `LABELFRAME_HOST_LOG` 给的是基准路径，实际文件名带日期后缀；历史单名 `server.log` / `host.log` 不迁移不删除。**服务端单文件大小上限**：`LABELFRAME_SERVER_LOG_FILE_MAX_SIZE_MB`（默认 50，0 或负值 = 不限）——单文件超限轮转为当日序号文件 `server-<yyyyMMdd>.N.log`（跨天回新日期基名），清理按（日期, 序号）序删最旧；大小上限启用后 `LABELFRAME_SERVER_LOG_FILE_RETENTION_DAYS` 的实际语义为**文件个数上限**（按日单文件时代即保留天数，既有部署无感），总占用上界 ≈ 31 文件 × 单文件上限。
- **logs.db 按量闸与空间回收**：`LABELFRAME_SERVER_LOGS_DB_MAX_SIZE_MB`（默认 256，0 或负值 = 不限量闸、按期时间闸仍生效）——每 24h 清理周期内，库文件（logs.db + logs.db-wal 合计）超过阈值时按大小删最旧日志直至回到阈值内，删除后执行 `VACUUM` + `wal_checkpoint(TRUNCATE)` **真实回收磁盘**（主文件显著收缩，非仅空闲页复用）。⚠️ **量闸删除不受 90 天保留期下限约束——这是有意行为，不是缺陷**（磁盘防护优先于保留期，避免被当作缺陷上报）；执行期代价：临时磁盘最多约 2× 库大小、期间日志写入短暂阻塞（低频后台窗口可接受，失败自动下个周期重试）。
- 模拟 PNG 保存与清理：Windows 默认 `%LOCALAPPDATA%\LabelFrame\print`，Linux 官方容器默认 `/var/lib/labelframe/client/print`，可由 `LABELFRAME_PRINT_OUTPUT` 指定专用目录。保存默认开启，管理员可在程序配置文件 `appsettings.json` 的 `WinHost` 节设 `SaveLogPrintImages=false`，或用环境变量覆盖；重启生效，不提供 UI 开关。关闭只停止新 PNG，Log 作业、数据日志及已有受管图片清理继续；不影响正式打印、预览、主动调试下载。
- 新出图作业目录带 `.labelframe-print-v1` 归属标记；**旧无标记目录、混合内容、子目录及链接不自动清理或接管**，需要另行确认后维护。默认31天＋512MiB容量目标，启动及每小时后台扫描，不依赖继续打印；提交后顺带扫描至少间隔5分钟。判龄为目录 UTC LastWriteTime，容量超限最旧优先，可早于31天淘汰。**容量是周期清理目标而非硬配额**，批次写入、扫描节流或删除失败期间可能超限，不包含数据库、旧无标记目录、浏览器缓存及用户下载。遇占用/权限失败留痕重试。删除不影响作业恢复和历史，只使模拟 PNG 不再可查看，历史出图数量变为0。时间与容量规则独立禁用，兼容配置中原 `RETENTION_DAYS=0` 现在只禁用时间规则；需要完全关闭清理时须同时把容量设为0。例：
  ```bash
  LABELFRAME_SAVE_LOG_PRINT_IMAGES=false   # 默认true；仅关闭新模拟PNG
  LABELFRAME_PRINT_IMAGE_RETENTION_DAYS=31 # <=0仅关闭时间清理
  LABELFRAME_PRINT_IMAGE_MAX_SIZE_MB=512   # MiB；<=0仅关闭容量清理
  ```
  对应配置文件示例：`{"WinHost":{"SaveLogPrintImages":false,"PrintImageRetentionDays":31,"PrintImageMaxSizeMB":512}}`。布尔环境变量接受 true/false（不区分大小写），非法值保持配置文件值；整数非法值同样保持原配置。进程内单例串行协调保存与清理，不支持多宿主共享出图目录；不要指向模板、下载或混合用途目录。失败写入的部分 PNG 后续仍可按规则清理，相同 requestId 重放不会补图。
- 模拟打印作业数据留痕（排障说明）：Log 模拟打印提交时，客户端宿主日志（`host-<yyyyMMdd>.log`）同时记录**作业原始数据**——作业 ID / 模板名 / 标签张数 / 数据集合（字段键值紧凑形式，如 `locationCode=A-01-02-03; zone=A-01`）；多张标签按数据集合**去重**（相同记一条 + 重复张数），单条约 2KB 上限、超限截断并标注完整长度。排障时在当日 host 日志按「作业数据」检索即可对照「这个作业带了什么数据进来」（PNG 出图只含渲染结果，看不到作业输入）；真实传输（tcp9100 / winspool / zebra）不产生该记录。
- 服务端业务事件日志（作业创建 / 设备认领 / 回报终态，作业粒度）默认 **INFO**；高流量需要降噪时按标准 Logging 配置降级，例如环境变量：
  ```bash
  Logging__LogLevel__LabelFrame.Server.ServerService=Warning   # 隐藏业务事件 INFO 行
  ```

## 11. 辅助脚本

- Release 的 `labelframe-offline-builder-<版本>.zip`：有网准备机按需生成 Windows / Docker 离线包；Windows 用 `build-windows-offline.ps1`，Docker 用 `build-docker-offline.sh`。
- `scripts\install-server-linux.sh`：Linux 服务端一键安装（manifest 校验 + systemd + 管理界面落位；在线 / 离线布局目录 / 指定版本，见「Ubuntu（systemd 裸机部署）」一节）。
- `scripts\demo-winhost.ps1`：无打印机验证打印闭环（构建 → 启动 WinHost → 提交含中文作业 → 展示 ZPL）。
- 品牌资产（图标 / 安装位图 / Android 资源 / 社交预览）不再本地生成——v0.33.0 起由品牌 v02 母版库（`assets/brand/`）统一供给，旧生成脚本已退役。
- `scripts\cleanup-residue.ps1`：清理历史安装残留（管理员运行）。
