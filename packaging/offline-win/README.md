# LabelFrame Windows Server 离线部署包·Full（__LABELFRAME_VERSION__，win-x64）

本包由有网准备机上的 LabelFrame Offline Builder 生成，用于无外网 Windows 服务器的首次安装，也可在升级机缺少运行时时作为兜底。目标机解压后运行 `install-server.cmd`，由离线 Installer 安装或升级 Server、更新管理界面并分发 Client / PDA / 官方插件。

Full 携带三类完整离线 runtime：.NET Desktop Runtime、ASP.NET Core Runtime、WebView2 x64 **Standalone**。Installer 会按需依次补齐 Server 所需的 Desktop Runtime（基础 `dotnet` host / .NET Runtime）与 ASP.NET Core Runtime；WebView2 保留为后续人工安装客户端的离线介质，不会自动安装，也不会复制进服务端下载中心。

已装服务器且运行时完整时，优先使用体积更小的 Upgrade 包 `labelframe-upgrade-__LABELFRAME_VERSION__-win-x64.zip`；Upgrade **不含任何 runtime**。

## 使用

1. 把 zip 拷到目标服务器并解压到任意本地目录；
2. 双击 `install-server.cmd`，按 UAC 提示授权管理员权限；
3. 等待脚本完成完整性校验、Server MSI 安装 / 升级、Web UI 原子替换、分发资源落位与健康检查。

也可在管理员 PowerShell 中运行：

```powershell
.\install-server.ps1
```

脚本只部署 Server 与服务端下载中心资源，**不会自动安装打印客户端**。

## Installer 会做什么

- 强制校验 `SHA256SUMS`、包版本、Full runtime 集合与 `managed-files.json`；
- 缺基础 .NET 10 host/runtime 时先从包内静默补装 Desktop Runtime，再按需补装 ASP.NET Core Runtime；不自动安装 WebView2；
- 以静默 MSI 安装 / MajorUpgrade Server，并输出 MSI 详细日志；
- 整体替换 Web UI，替换后任一健康检查失败都会恢复旧 Web UI 并重新启动服务；
- 把 Client MSI、PDA APK、Zebra / TSPL 官方插件分别放入服务端实际配置的分发目录；
- 尊重 Machine 级 `LABELFRAME_SERVER_*` 路径覆盖；`LABELFRAME_SERVER_WEB_UI` 已定义为空白时保持无头模式；
- 验证 Windows 服务 Running、`/healthz`、服务版本、`uiEnabled` 与下载中心文件哈希；
- 全链成功后，仅按上次受管账本清理旧官方资源；第三方插件、用户上传件和内容已被用户替换的文件保留。

同版本重跑幂等。模板、作业、日志与配置保留在 `%ProgramData%\LabelFrame`，正常升级不会清除。

## 安装后验证

- 服务端健康检查：`http://127.0.0.1:53961/healthz`；
- Windows 服务：`sc query LabelFrameServer`；
- 管理界面启用时：浏览器访问 `http://<服务器 IP>:53961/`；
- 客户端：从下载中心取得 Client MSI 后人工安装，并把服务端地址设为 `http://<服务器 IP>:53961`；
- 防火墙：按需放行 53961/tcp。

成功或失败都会显示 Installer 日志与 MSI 日志路径；正式日志位于 `%ProgramData%\LabelFrame\installer\logs`。

## Full 与 Upgrade 如何选择

| 场景 | 使用 |
|---|---|
| 新服务器首次部署 | Full |
| 已装服务器升级，基础 .NET 10 与 ASP.NET Core 10+ 均在场 | Upgrade |
| Upgrade 提示缺少任一 Server 运行时前置 | 改用同版本 Full |
| 需要给后续客户端人工安装准备 Desktop / WebView2 离线介质 | Full |

在线安装引导程序使用 WebView2 Evergreen 小型 Bootstrapper；本 Full 包携带的是 `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` x64 Standalone 完整安装器，可在断网环境使用。

## 包内容一览

| 文件 | 说明 |
|---|---|
| `install-server.cmd` / `install-server.ps1` | 目标服务器离线 Installer 入口 |
| `LabelFrame-Server-__LABELFRAME_VERSION__.msi` | 服务端 Windows 服务安装包 |
| `LabelFrame-Client-__LABELFRAME_VERSION__.msi` | 供下载中心分发、后续人工安装的打印客户端 |
| `labelframe-server-webui-__LABELFRAME_VERSION__.zip` | 服务端管理界面，由 Installer 整体替换 |
| `labelframe-transport-zebra-__LABELFRAME_VERSION__.lfplugin` | Zebra 官方插件，分发到插件下载目录 |
| `labelframe-transport-tspl-__LABELFRAME_VERSION__.lfplugin` | TSPL 官方插件，分发到插件下载目录 |
| `LabelFrame-AndroidHost-__LABELFRAME_VERSION__.apk` | PDA 宿主，分发到 PDA 下载目录 |
| `windowsdesktop-runtime-*-win-x64.exe` | .NET Desktop Runtime，净机缺基础 host/runtime 时先自动补装 |
| `aspnetcore-runtime-*-win-x64.exe` | ASP.NET Core Runtime，Server 缺失时自动补装 |
| `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` | WebView2 x64 Standalone 离线介质，不自动安装 |
| `managed-files.json` | 本包允许分发的官方资源清单 |
| `offline-build-info.json` | Builder 实际来源、哈希、大小与生成时间 |
| `SHA256SUMS` | 包内载荷完整性账本 |
| `install-manifest.json` / `latest.json` | 原子资源版本与来源记录；Installer 以本包账本为安装边界 |
