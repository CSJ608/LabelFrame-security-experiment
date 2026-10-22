# LabelFrame Windows 离线部署包·全量（__LABELFRAME_VERSION__，win-x64）

本包用于**离线 / 内网环境**的 Windows 新机**首次安装**：解压后双击包内引导程序，全程零外网
（全部组件与 .NET 10 / WebView2 运行时安装器已随包携带，目标机无需预装任何运行时）。

- 已装机的**版本升级**请改用升级包 `labelframe-upgrade-__LABELFRAME_VERSION__-win-x64.zip`（体积约为本包一半，不含运行时安装器）；
- 在线环境不必用本包：直接下载 Release 的引导 EXE 联网安装即可（见仓库 `docs/DEPLOY.md`「安装引导程序」一节）。

## 使用（三步）

1. 把本包 zip 拷到目标机（U 盘 / 内网共享），解压到任意目录；
2. 双击解压目录中的 `LabelFrame-Bootstrapper-__LABELFRAME_VERSION__.exe`；
3. 按安装向导选择部署形态（单机一体 / 服务端 / 打印客户端）与组件 → 安装完成。

引导程序自动使用同目录的安装清单与组件文件，不访问网络；本机缺运行时时自动用包内安装器补装（已装则跳过）。
双 MSI 亦可从解压目录直接单独安装（服务端 `LabelFrame-Server-*.msi` / 客户端 `LabelFrame-Client-*.msi`），
但该路径缺运行时只提示不补装——离线机请优先走引导 EXE。

## 完整性校验（拷贝后建议）

在解压目录用 PowerShell 校验载荷（引导 EXE + 全部组件）：

```powershell
Get-Content SHA256SUMS | ForEach-Object {
  $hash, $name = $_ -split '  ', 2
  $ok = (Get-FileHash $name -Algorithm SHA256).Hash.ToLower() -eq $hash
  "{0}  {1}" -f ($(if ($ok) { 'OK  ' } else { 'FAIL' }), $name)
}
```

## 安装后验证

- 服务端：`http://127.0.0.1:53961/healthz` 返回 `{"service":"LabelFrame.Server","status":"ok"}`；服务名 `LabelFrameServer`（`sc query LabelFrameServer`）；
- 客户端：本机界面 `http://127.0.0.1:53960`；设置页把服务端地址指向 `http://<服务端 IP>:53961`；
- 防火墙：服务端机放行 53961/tcp。

## 分发 PDA 安装包（离线环境闭环）

包内 `LabelFrame-AndroidHost-__LABELFRAME_VERSION__.apk` 拷入服务端下载中心目录
`%ProgramData%\LabelFrame\server\pda-packages`（或经管理界面上传），PDA 扫下载中心二维码即可安装——无需外网。

## 常见问题

- **升级**：拿新版本**升级包**，同样解压后双击引导 EXE——自动进入升级模式（列出「现版本 → 新版本」），用户配置保留；
- **升级包提示缺运行时 / 运行时安装失败**：本机运行时中途被卸载或损坏、或新版本抬升了运行时大版本要求——改用本全量包重跑即可；
- **安装向导失败排查**：Burn 日志在 `%TEMP%\LabelFrame*.log`（失败页可直接打开本次日志）；
- **SmartScreen「未知发布者」**：点「更多信息 → 仍要运行」；内网可把仓库自签根证书加入受信任根消除提示（见 `docs/DEPLOY.md`）。

## 包内容一览

| 文件 | 说明 |
|---|---|
| `LabelFrame-Bootstrapper-__LABELFRAME_VERSION__.exe` | 安装引导程序（离线入口，读同目录清单与组件） |
| `LabelFrame-Server-__LABELFRAME_VERSION__.msi` | 服务端（Windows 服务 `LabelFrameServer`） |
| `LabelFrame-Client-__LABELFRAME_VERSION__.msi` | 打印客户端（界面壳 + 托盘） |
| `labelframe-server-webui-__LABELFRAME_VERSION__.zip` | 服务端管理界面插件（向导「管理界面」开关组件） |
| `labelframe-transport-zebra-__LABELFRAME_VERSION__.lfplugin` | Zebra 品牌传输官方插件 |
| `labelframe-transport-tspl-__LABELFRAME_VERSION__.lfplugin` | TSPL 指令集传输官方插件 |
| `LabelFrame-AndroidHost-__LABELFRAME_VERSION__.apk` | PDA 宿主安装包（经服务端下载中心分发） |
| `windowsdesktop-runtime-*-win-x64.exe` | .NET 10 Desktop Runtime 安装器（缺失补装，已装跳过） |
| `aspnetcore-runtime-*-win-x64.exe` | ASP.NET Core Runtime 安装器（缺失补装，已装跳过） |
| `MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe` | WebView2 运行时安装器（缺失补装，已装跳过） |
| `install-manifest.json` / `latest.json` | 安装清单与版本指针（引导程序离线消费） |
| `SHA256SUMS` / `README.md` | 载荷哈希（不含 manifest / README）与本文件 |
