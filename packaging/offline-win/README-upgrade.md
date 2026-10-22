# LabelFrame Windows 离线部署包·升级（__LABELFRAME_VERSION__，win-x64）

本包用于**离线 / 内网环境**已装 LabelFrame 的 Windows 机器**版本升级**：解压后双击包内引导程序即可，
全程零外网。本包**不含运行时安装器**（体积约为全量包一半）——升级机在首次安装时已装 .NET 10 / WebView2
运行时，引导程序检测到已装即跳过，不重复安装。

- 新机**首次安装**请用全量包 `labelframe-offline-__LABELFRAME_VERSION__-win-x64.zip`（含运行时安装器）；
- 在线环境不必用本包：客户端「设置 → 检查更新」或直接运行新版引导 EXE 联网升级即可。

## 使用（三步）

1. 把本包 zip 拷到目标机（U 盘 / 内网共享），解压到任意目录；
2. 双击解压目录中的 `LabelFrame-Bootstrapper-__LABELFRAME_VERSION__.exe`；
3. 引导程序自动进入**升级模式**（列出「现版本 → 新版本」）→ 确认安装计划 → 完成。

用户配置（服务端地址、打印机设置、模板与作业数据）全部保留；服务端升级完成后自动恢复运行。

## 完整性校验（拷贝后建议）

在解压目录用 PowerShell 校验载荷（引导 EXE + 全部组件）：

```powershell
Get-Content SHA256SUMS | ForEach-Object {
  $hash, $name = $_ -split '  ', 2
  $ok = (Get-FileHash $name -Algorithm SHA256).Hash.ToLower() -eq $hash
  "{0}  {1}" -f ($(if ($ok) { 'OK  ' } else { 'FAIL' }), $name)
}
```

## 升级后验证

- 服务端：`http://127.0.0.1:53961/healthz` 返回 `{"service":"LabelFrame.Server","status":"ok"}`，且版本号为 __LABELFRAME_VERSION__；
- 客户端：界面「设置 → 更新与安装包」可见新版本；打印链路照常。

## 何时该改用全量包

升级过程中若提示**缺运行时 / 运行时安装失败**（本机运行时中途被卸载、损坏，或新版本抬升了 .NET 大版本要求），
离线场景下本包无法补装运行时——改用同版本**全量包**重跑引导 EXE 即可（其余步骤相同）。

## 常见问题

- **想顺便装没装过的组件**（如首次开启管理界面 / 新增插件）：引导向导改选组件即可，本包内组件齐全（运行时除外）；
- **升级中断 / 失败**：向导失败页给出中文失败分类，可整包重试；Burn 日志在 `%TEMP%\LabelFrame*.log`；
- **SmartScreen「未知发布者」**：点「更多信息 → 仍要运行」（同全量包口径）。

## 包内容一览

| 文件 | 说明 |
|---|---|
| `LabelFrame-Bootstrapper-__LABELFRAME_VERSION__.exe` | 安装引导程序（离线入口，自动进入升级模式） |
| `LabelFrame-Server-__LABELFRAME_VERSION__.msi` | 服务端（Windows 服务 `LabelFrameServer`） |
| `LabelFrame-Client-__LABELFRAME_VERSION__.msi` | 打印客户端（界面壳 + 托盘） |
| `labelframe-server-webui-__LABELFRAME_VERSION__.zip` | 服务端管理界面插件（向导「管理界面」开关组件） |
| `labelframe-transport-zebra-__LABELFRAME_VERSION__.lfplugin` | Zebra 品牌传输官方插件 |
| `labelframe-transport-tspl-__LABELFRAME_VERSION__.lfplugin` | TSPL 指令集传输官方插件 |
| `LabelFrame-AndroidHost-__LABELFRAME_VERSION__.apk` | PDA 宿主安装包（拷入服务端下载中心分发） |
| `install-manifest.json` / `latest.json` | 安装清单与版本指针（引导程序离线消费） |
| `SHA256SUMS` / `README.md` | 载荷哈希（不含 manifest / README）与本文件 |
