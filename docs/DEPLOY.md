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

- **七页向导流程**：欢迎（新装 / 升级检测，含离线安装指引）→ 拓扑预设（单机一体 / 服务端 / 打印客户端等，选错可重跑改选）→ 品牌多选（检测到本机 ZDesigner 驱动时预选 Zebra，对应官方外置插件）→ 管理界面开关（服务端网页管理界面，可选）→ 确认安装计划（组件清单 + 本机已装版本对照）→ 安装进度（逐组件下载 + 校验 + 安装，失败自动回滚并给出中文失败分类）→ 完成。
- **前置说明**：引导程序自带 .NET 10 Desktop Runtime / ASP.NET Core Runtime / WebView2 运行时**前置链**——本机缺失时自动按官方直链下载补装（已装则跳过），无需手工预装运行时；这正是它优于 MSI 直装的主要场景（MSI 直装缺失运行时会拦截并给下载链接，需手动补装）。
- **升级**：本机已装 LabelFrame 时重跑引导程序即进入升级模式（列出可升级组件「现版本 → 新版本」，复用同一安装链完成覆盖升级，用户配置保留）；客户端设置页「检查更新」发现新版本时也会指向此入口。
- **离线 / 内网**：欢迎页提供离线安装指引；完全离线的预下载布局目录形态见相邻迭代排期（当前内网可先在有网机器装好后整机克隆或用 MSI + 手动运行时部署）。
- **SmartScreen「未知发布者」提示**：引导 EXE 当前使用自签证书签名（与 MSI 同通道，见「自动化发布与签名」一节）——公网下载首次运行 Windows 可能提示「未知发布者」，点「更多信息 → 仍要运行」即可；下载完整性由 install manifest 的 sha256 强制校验保障。内网可把自签根证书加入受信任根消除提示。
- **安装日志（排障）**：Burn 引擎日志在 `%TEMP%\LabelFrame*.log`（失败报告页可直接打开本次日志；按通配查找）。
- 本地手工构建（发版链之外）：`scripts\build-bundle.ps1 -Version x.y.z`（需 WiX v7；Secrets 在场时 `-Sign` 复用 MSI 证书签名）。

## 3. 安装包（MSI，高级路径）

下载：[GitHub Releases](https://github.com/marci-labs/LabelFrame/releases)。

- **LabelFrame-Server-x.x.x.msi** → `C:\Program Files\LabelFrame\Server`：无头服务端（模板库 / 作业中心 / 设备投递 / 调试出图 / 日志 / Excel），不接打印机、不提供 Web UI；安装为 Windows 服务 `LabelFrameServer`，数据在 `%ProgramData%\LabelFrame\server`。
- **LabelFrame-Client-x.x.x.msi** → `C:\Program Files\LabelFrame\Client`：打印客户端，以应用窗口（WebView2 嵌入界面壳）托管完整界面（模板设计 / 数据与打印 / 连接配置 / 作业历史；不提供「PDA 日志」页——设备日志回传能力立项前不包含），本机地址 `http://127.0.0.1:53960`；窗口关闭后服务驻留系统托盘（真正退出走托盘「退出」）。

要点：

- 前置：.NET 10 Desktop Runtime（x64）+ Microsoft Edge WebView2 运行时（Evergreen，Win10/11 多数已随 Edge 预装）。MSI 内置检测：.NET 缺失时 NetCoreCheck 自检弹出可点击的官方下载链接（不自动安装）；WebView2 缺失时全 UI 安装显示带官方下载链接的中文对话框（装完点「重新检测」即可继续，无需重启安装程序），静默 / 基础 UI 由 LaunchCondition 拦截提示。客户端启动时若 WebView2 初始化失败，自动回退默认浏览器打开界面并在 host.log 记录原因。
- 单机使用 = 同机安装两个包；多台打印电脑 = 每台装 Client，设置页把服务端地址指向服务端 IP。
- 两个包的 appsettings.json 均为独立用户配置组件：覆盖安装 / 修复不覆盖、卸载保留。卸载时可选是否清除用户数据（默认不清除）。
- 公开下载的 MSI 若未用受信任商业证书签名，Windows 可能提示「未知发布者」，点「仍要运行」即可；内网可把自签根证书加入受信任根消除提示（见「自动化发布与签名」一节）。
- 打印统一为整版位图（Skia 渲染 → `^GF` 直传打印机），与画布预览同源；连接方式在客户端「设置」页配置（先测试后生效）。
- **修改服务端地址后需重启 Client**（打印 Worker 使用启动时的地址）。
- 清理历史安装残留：管理员运行 `scripts\cleanup-residue.ps1`。

## 4. Docker（推荐的服务端部署方式）

**快速启动（Release compose 分发）**：从 [GitHub Releases](https://github.com/marci-labs/LabelFrame/releases) 下载 `compose.yml` 与 `.env` 放同一目录，`docker compose up -d` 即可——`.env` 已把 `LABELFRAME_VERSION` 钉定为本 Release 版本（想跟随最新版可改回 `latest`，镜像源覆盖见文件内注释）。两者与仓库 `packaging/ubuntu/docker-compose.yml` 同源（发版流水线直接复制生成）。把 LabelFrame **并入你自己管理多服务的 compose** 见「整合进已有 compose」一节；**离线 / 内网环境**改用同 Release 的离线部署包（`labelframe-offline-<版本>-linux-x64.tar.gz`，见「离线 / 内网部署（offline bundle）」一节）。

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
- 本地构建镜像调试：`LABELFRAME_IMAGE=labelframe-server LABELFRAME_VERSION=0.22.2 docker compose up -d`。

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

在 compose 同目录 `.env` 钉 `LABELFRAME_VERSION=0.36.0`（镜像 tag 与下面的资源脚本共用同一版本变量，防镜像 / 资源错配）。

② 资源在线落位（管理界面网页 zip + 客户端 MSI / PDA APK / 传输插件包——一条命令按挂载布局放好）：

```bash
curl -fsSL https://raw.githubusercontent.com/marci-labs/LabelFrame/master/scripts/fetch-server-resources.sh \
  | bash -s -- ./labelframe
```

- 脚本消费发版流水线的 `install-manifest.json`（**sha256 逐源强制校验，fail-closed**），四类资源落位到 `./labelframe/server/` 下的 `plugins/web-ui/`（解压展开、原子换名）、`client-packages/`、`pda-packages/`、`plugin-packages/`；
- **版本对齐**：默认读同目录 `.env` 的 `LABELFRAME_VERSION`（与镜像同版）；`--version 0.36.0` 显式指定；两者皆无走官方 latest 通道（运行时提示所用版本）；
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

正式镜像 `ghcr.io/marci-labs/labelframe-client` 是仅用于无打印机自动化测试的 Linux 无头 Client。它固定使用 `log` 模拟打印，不包含 TCP 9100、USB、Windows 驱动、Zebra SDK、第三方插件或客户端 Web UI，不能替代物理打印验收。

验证已发布的同版本 Server / Client（Compose 不含 `build`）：

```powershell
$env:LABELFRAME_VERSION = "0.22.2"
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

### 4.3 离线 / 内网部署（offline bundle）

从 [GitHub Releases](https://github.com/marci-labs/LabelFrame/releases) 下载 `labelframe-offline-<版本>-linux-x64.tar.gz`，拷到目标机（U 盘 / 内网共享），解压到**固定部署目录**（`--strip-components=1` 剥掉包内版本号顶层目录，命令对任意版本通用），包内自带全部所需（镜像 tar + 与在线版逐字节同源的 `compose.yml` + 版本钉定 `.env` + 管理界面预解压 + 客户端 / PDA / 插件安装包），部署全程零外网：

```bash
mkdir -p labelframe-offline
tar -xzf labelframe-offline-<版本>-linux-x64.tar.gz -C labelframe-offline --strip-components=1
cd labelframe-offline
bash install.sh          # 或按 README 三步手动：sha256sum -c → docker load → docker compose up -d
curl http://127.0.0.1:53961/healthz
```

- **前置**：目标机已装 Docker Engine（含 compose v2）与 `sha256sum`——包不含 Docker 本体；
- **完整性**：`install.sh` 强制 `SHA256SUMS` 全件校验（镜像 + 分发产物），不符即拒装（fail-closed）；
- **离线原理**：镜像以 ghcr 全名 tag `docker load` 进本机，compose 默认 pull=missing 本地命中即不再联网拉取；
- **管理界面开箱可用**（`plugins/web-ui/` 已预解压，即 compose 默认挂载的服务端 WebUiPath）；
- **分发闭环**：`install.sh` 在启动服务前自动把 `packages/` 下 Client MSI / PDA APK / `.lfplugin` 拷入对应挂载目录（`./client-packages/` 等，`cp -f` 幂等、包内原件保留），下载中心开箱可用；后续增删安装包可直接向挂载目录拷入 / 删除文件或经管理界面「下载中心」上传（即时生效）——离线环境客户端 / PDA / 插件安装包不用再出网；
- **幂等 / 升级**：`install.sh` 重跑无害；升级 = **同一部署目录**解压新版本包重跑（`mkdir -p` + `tar -xzf 新包 -C 同一目录 --strip-components=1` 覆盖解压后 `bash install.sh`——`SHA256SUMS` 按当版清单校验，旧版本残留文件不影响；勿换目录升级：compose 未钉定卷名，数据卷实际名 = `<部署目录名>_labelframe-data`（如 `labelframe-offline` → `labelframe-offline_labelframe-data`），换目录会新建**空卷**、模板 / 数据库 / 日志不跟随，且 `container_name` 固定 `labelframe-server`，新目录起服务前须先在旧目录 `docker compose down`）；
- **可选共享网络（仅离线包 `install.sh`）**：部署者在同一目录建 `network.env`，写 `LABELFRAME_EXTERNAL_NETWORK=<现有用户自定义 bridge 网络名>`（最多一个）；或运行 `LABELFRAME_EXTERNAL_NETWORK=<网络名> bash install.sh`，进程变量优先。脚本启动前校验网络并生成 Compose 覆盖文件，Server 保留默认网络及宿主端口，同时以别名 `labelframe-server` 接入外部网络。调用方容器自行加入该网络后使用 `http://labelframe-server:53961`；宿主机进程用 `http://127.0.0.1:53961`，远端用 `http://<宿主机可达 IP>:53961`。`network.env` 不随解压覆盖，升级 / 重建重跑 `bash install.sh`；直接运行 `docker compose up -d` 不应用该选项。空值仍为独立部署，网络不存在或无效则报错。API 无鉴权，仅接入受信任网络；在线版 Compose 分发入口暂不支持该选项（参见包内 README）。
- **存量版本号目录迁移**（曾按「解压得同名目录」旧口径部署，如 `labelframe-offline-0.30.0-linux-x64/`）：新版本包解压到**当初部署的同一目录**重跑即可（`tar --strip-components=1` 剥掉顶层版本号目录后布局一致，数据沿用该目录对应的既有卷，无需搬迁）；旧版本镜像 tar 可选清理——删除目录内 `images/` 下旧版本 `.image.tar.gz` 释放磁盘（约 150MB+/ 版本，不影响运行与数据），`docker image rm ghcr.io/marci-labs/labelframe-server:<旧版本>` 同为可选；
- 发版流水线对该包做**真离线自验**（offline-bundle job 在无镜像的 runner 上以包内 `install.sh` 走用户全链），不可用即发版失败；组包脚本 `scripts/make-offline-bundle.sh` 可本地复用。决策记录见 [DESIGN.md](DESIGN.md)。

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

总原则：**模板、作业历史、日志与配置在各部署形态下都与程序分离存放，正常升级不清数据**；升级前建议对数据目录做一次备份（Windows：`%ProgramData%\LabelFrame`；Linux：`/var/lib/labelframe`）。各版本的变化与升级注意事项见[更新日志](../CHANGELOG.md)。

| 形态 | 升级方法 | 数据与回滚 |
|---|---|---|
| Windows（引导程序 / MSI） | 本机已装 LabelFrame 时**重跑新版安装引导程序**即进入升级模式（列出可升级组件「现版本 → 新版本」，复用同一安装链覆盖升级）；或直接运行新版 MSI 覆盖安装——客户端升级会自动安全退出正在运行的程序再安装。客户端「检查更新」发现新版本时同样指向引导程序入口 | 数据在 `%ProgramData%\LabelFrame`（模板 / 作业 / 日志），`appsettings.json` 与用户配置在覆盖安装 / 卸载时均默认保留（卸载时可选清除，默认不清除）；回滚＝运行旧版本引导程序或 MSI 覆盖安装 |
| Linux Docker（compose 分发） | 编辑 `.env` 把 `LABELFRAME_VERSION` 改为新版本（或 `latest`）→ `docker compose pull && docker compose up -d` | 数据在数据卷（默认 `labelframe-data`），重建容器不丢失；回滚＝改回旧版本号后重建 |
| Ubuntu（systemd） | 幂等设计：**重跑 `sudo bash install-server-linux.sh`** 即覆盖升级（也可 `--manifest` 指定版本） | `/var/lib/labelframe` 数据与 `appsettings.json` 不动；回滚＝`--manifest` 指定旧版本重跑 |
| Linux 离线部署包 | 新版离线包解压到**当初部署的同一目录**（`--strip-components=1`）后重跑 `bash install.sh`；**勿换目录升级**——数据卷名绑定部署目录名，换目录会新建空卷（模板 / 数据库不跟随） | 卷数据保留；旧版本镜像文件可删 `images/` 下旧 `.image.tar.gz` 释放磁盘；回滚＝旧版包同目录重跑 |
| PDA（Android 宿主） | 服务端「下载中心」扫码或直接下载新版 APK 覆盖安装（GitHub Release 与下载中心分发的都是同一签名的 Release 构建） | 配置（服务器地址 / 打印机 / 设备名）保留；**换签名来源的 APK 须先卸载重装**——配置会被清空且设备号变化（Server 设备目录出现新条目），详见「自动化发布与签名」一节 |

补充要点：

- **升级顺序**：建议先升级服务端，再升级各打印客户端与 PDA；跨版本升级前先读[更新日志](../CHANGELOG.md)当版的「升级注意」。
- **模板兼容**：新版本可继续打开既有模板；但**旧版本程序可能不认识新版本模板中的新元素**（如图片元素）——把模板回传给旧版本使用前请先确认。
- **插件随行**：Zebra 官方传输插件随客户端 MSI 附带并在启动时自动安装 / 升级（见「分发通道」一节），升级客户端无需单独处理插件。
- **服务端管理界面（可选插件）**：经安装引导程序安装的随升级自动处理；手动放置 `web-ui` 插件的，升级后用新版 `labelframe-server-webui-*.zip` 覆盖目录即可（放入即生效）。

## 7. 服务端管理界面（可选插件）

服务端默认无头（仅 `/healthz` + API）。需要管理界面时，把前端 server 构建产物（`web/dist-server`，或 Release 里的 `labelframe-server-webui-x.x.x.zip`）放进插件目录即可——**放入即生效、移除即无头，无需重启**：

- Windows：`%ProgramData%\LabelFrame\server\plugins\web-ui`
- Linux / Docker：`/var/lib/labelframe/server/plugins/web-ui`（compose 默认挂载 `./plugins/web-ui`）

打包脚本：`scripts/package-server-webui.ps1`。管理界面与客户端界面是同一前端的两种构建（工作台 / 设计器 / 在线设备 / 作业历史 / 下载中心 / 插件管理），无打印机相关内容。

## 8. 分发通道

### 客户端安装包分发（服务端集中下载）

- 目录 `client-packages`（Windows `%ProgramData%\LabelFrame\server\client-packages`；Linux `/var/lib/labelframe/server/client-packages`；环境变量 `LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）。
- 目录直放文件或经管理界面「下载中心」页上传；客户端设置页「更新与安装包」列出并可下载（不自动升级，下载后自行运行安装）。
- API：`GET/POST /api/client-packages`、`GET/DELETE /api/client-packages/{file}`（路径穿越防护）。

### PDA（Android 宿主）安装包分发与扫码下载

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

## 9. 自动化发布与签名

- 发版两步：① 更新 `docs/ROADMAP.md` 与 `CHANGELOG.md` 提交推送；② 例如 `git tag v0.22.2 && git push origin v0.22.2`。
- CI 自动：构建测试 → 双 MSI（可签名）→ 管理界面插件 zip → Linux 归档 → **Android APK（PDA 宿主）** → 同一次构建的 Server / Linux Client 候选镜像通过 Compose E2E → 原镜像推 ghcr.io（版本号 + `latest`）→ GitHub Release。
- **安装引导 EXE**：发版链含独立 `bundle` job——按当版 install manifest（分阶段生成，runtime 哈希跨 job 一致性断言 fail-closed）构建 `LabelFrame-Bootstrapper-<版本>.exe` 并随 Release 附件发布（「安装引导程序（推荐安装入口）」一节的推荐入口即此产物）；下载多源与哈希校验语义见 DESIGN.md。
- **Linux 归档与 compose 分发**：Linux 归档默认 self-contained（`install-server-linux.sh` 一键安装免 .NET 前置，见「Ubuntu（systemd 裸机部署）」一节）；Release 附件含 `compose.yml` + `.env`（与 `packaging/ubuntu/docker-compose.yml` 同源复制、`.env` 钉定当版版本，「Docker（推荐的服务端部署方式）」一节的快速启动即此产物；两者不进 install manifest——部署描述文件而非可安装产物）。
- MSI 签名：配置 Secret `MSI_SIGN_CERT_BASE64` / `MSI_SIGN_PASSWORD` 时自动签名，否则跳过。当前为自签证书过渡方案（公开下载仍可能 SmartScreen 提示），正式对外分发建议购买 OV 代码签名证书。本地签名：`scripts\create-signing-cert.ps1` 生成证书，`scripts\build-msi.ps1 -Sign` 使用。
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
- 出图目录保留清理：Log 模拟打印出图目录（`print\<jobId>`，默认 `%LOCALAPPDATA%\LabelFrame\print`，Windows 窗口与 Linux 无头客户端同路径）**按天保留、默认 31 天**——超期作业子目录（含内部 PNG）在客户端启动与每次模拟打印落盘后自动删除；判龄 = 作业目录 LastWriteTime（与 jobs.db 作业历史无关，删除只影响 Log 模式「查看出图」）；遇占用 / 权限失败降级留痕、不影响打印与出图。`LABELFRAME_PRINT_IMAGE_RETENTION_DAYS` 可调，0 或负值 = 不清理。例：
  ```bash
  LABELFRAME_PRINT_IMAGE_RETENTION_DAYS=31   # 默认：保留 31 天
  LABELFRAME_PRINT_IMAGE_RETENTION_DAYS=0    # 关闭清理（出图目录无限累积，恢复旧现状）
  ```
- 模拟打印作业数据留痕（排障说明）：Log 模拟打印提交时，客户端宿主日志（`host-<yyyyMMdd>.log`）同时记录**作业原始数据**——作业 ID / 模板名 / 标签张数 / 数据集合（字段键值紧凑形式，如 `locationCode=A-01-02-03; zone=A-01`）；多张标签按数据集合**去重**（相同记一条 + 重复张数），单条约 2KB 上限、超限截断并标注完整长度。排障时在当日 host 日志按「作业数据」检索即可对照「这个作业带了什么数据进来」（PNG 出图只含渲染结果，看不到作业输入）；真实传输（tcp9100 / winspool / zebra）不产生该记录。
- 服务端业务事件日志（作业创建 / 设备认领 / 回报终态，作业粒度）默认 **INFO**；高流量需要降噪时按标准 Logging 配置降级，例如环境变量：
  ```bash
  Logging__LogLevel__LabelFrame.Server.ServerService=Warning   # 隐藏业务事件 INFO 行
  ```

## 11. 辅助脚本

- `scripts\install-server-linux.sh`：Linux 服务端一键安装（manifest 校验 + systemd + 管理界面落位；在线 / 离线布局目录 / 指定版本，见「Ubuntu（systemd 裸机部署）」一节）。
- `scripts\demo-winhost.ps1`：无打印机验证打印闭环（构建 → 启动 WinHost → 提交含中文作业 → 展示 ZPL）。
- 品牌资产（图标 / 安装位图 / Android 资源 / 社交预览）不再本地生成——v0.33.0 起由品牌 v02 母版库（`assets/brand/`）统一供给，旧生成脚本已退役。
- `scripts\cleanup-residue.ps1`：清理历史安装残留（管理员运行）。
