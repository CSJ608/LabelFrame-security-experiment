# LabelFrame Windows Server 离线部署包·Upgrade（__LABELFRAME_VERSION__，win-x64）

本包由有网准备机上的 LabelFrame Offline Builder 生成，用于无外网且已安装 LabelFrame Server 的 Windows 服务器升级。目标机解压后运行 `install-server.cmd`，由离线 Installer 完成 Server 升级、Web UI 更新、分发资源落位与健康检查。

Upgrade 是**零 runtime 包**：不含 .NET Desktop、ASP.NET Core 或 WebView2 安装器，也不会重装任何 runtime。目标机缺少 ASP.NET Core 10+ 时会在修改系统前拒绝执行，并提示改用同版本 Full 包 `labelframe-offline-__LABELFRAME_VERSION__-win-x64.zip`。

## 使用

1. 把 zip 拷到目标服务器并解压到任意本地目录；
2. 双击 `install-server.cmd`，按 UAC 提示授权管理员权限；
3. 等待脚本完成校验、Server MSI 升级、Web UI 原子替换、分发资源更新与健康检查。

也可在管理员 PowerShell 中运行：

```powershell
.\install-server.ps1
```

用户配置、模板、作业与日志保留；脚本只部署 Server 和服务端分发资源，不自动安装打印客户端。

## 升级行为

- 安装前强制校验 `SHA256SUMS`、包版本、**零 runtime** 集合与 `managed-files.json`；
- ASP.NET Core 10+ 在场时直接升级 Server，不安装任何 runtime；
- Server MSI 以静默 MajorUpgrade 执行，详细日志路径会显示在控制台；
- Web UI 先解压到同卷临时目录，再整体替换；若服务启动或健康检查失败，恢复旧 Web UI；
- Client MSI、PDA APK、Zebra / TSPL 官方插件更新到服务端实际配置的下载目录；
- 服务恢复后验证 `/healthz`、服务版本、管理界面模式与下载中心文件哈希；
- 全链成功后仅清理由上次受管账本确认且内容未被用户改写的旧官方资源；第三方插件和自定义文件不动；
- 同版本重跑幂等。

## 何时改用 Full

- 目标机未安装 ASP.NET Core 10+；
- runtime 被卸载或损坏；
- 新版本抬升了 Server 所需 .NET 大版本；
- 需要额外携带 Desktop Runtime 或 WebView2 x64 Standalone 作为客户端人工安装介质。

改用 Full 后仍运行同一个 `install-server.cmd`，其余升级步骤和数据保留语义相同。

## 升级后验证

- `sc query LabelFrameServer` 显示服务 Running；
- `http://127.0.0.1:53961/healthz` 返回健康状态；
- 管理界面启用时可打开，旧散列文件不残留；
- 下载中心只新增 / 保留本次与用户自定义资源，第三方插件不被删除。

成功或失败都会显示 Installer 日志与 MSI 日志路径；正式日志位于 `%ProgramData%\LabelFrame\installer\logs`。

## 包内容一览

| 文件 | 说明 |
|---|---|
| `install-server.cmd` / `install-server.ps1` | 目标服务器离线 Installer 入口 |
| `LabelFrame-Server-__LABELFRAME_VERSION__.msi` | 服务端升级安装包 |
| `LabelFrame-Client-__LABELFRAME_VERSION__.msi` | 供下载中心分发、后续人工安装的打印客户端 |
| `labelframe-server-webui-__LABELFRAME_VERSION__.zip` | 服务端管理界面，由 Installer 整体替换 |
| `labelframe-transport-zebra-__LABELFRAME_VERSION__.lfplugin` | Zebra 官方插件 |
| `labelframe-transport-tspl-__LABELFRAME_VERSION__.lfplugin` | TSPL 官方插件 |
| `LabelFrame-AndroidHost-__LABELFRAME_VERSION__.apk` | PDA 宿主安装包 |
| `managed-files.json` | 本包允许分发的官方资源清单 |
| `offline-build-info.json` | Builder 实际来源、哈希、大小与生成时间 |
| `SHA256SUMS` | 包内载荷完整性账本 |
| `install-manifest.json` / `latest.json` | 原子资源版本与来源记录 |

本包不应出现任何 `windowsdesktop-runtime-*`、`aspnetcore-runtime-*` 或 WebView2 Runtime 安装器；出现即视为包结构错误并拒绝安装。
