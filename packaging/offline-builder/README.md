# LabelFrame 离线部署 Builder

本 Builder 在**有网准备机**下载并校验当前 Release 的原子资源，再生成可拷到无外网目标机的部署包。Builder 版本已固定在 `offline-source-manifest.json`，无需手工输入版本号。

## Windows 包

准备机要求：Windows PowerShell 5.1 或 PowerShell 7，可访问 GitHub Releases、微软 .NET 下载源和 WebView2 下载源。

```powershell
.\build-windows-offline.ps1 -OutputDir D:\LabelFrame
```

可选缓存目录：

```powershell
.\build-windows-offline.ps1 -OutputDir D:\LabelFrame -CacheDir D:\LabelFrameCache
```

输出：

- `labelframe-offline-<版本>-win-x64.zip`：Full，含 Desktop Runtime、ASP.NET Core Runtime 和 WebView2 Standalone；
- `labelframe-upgrade-<版本>-win-x64.zip`：Upgrade，不含运行时。

目标 Windows 服务器解压后，以管理员身份运行 `install-server.cmd`。脚本只安装或升级 Server 并分发 Client MSI、PDA APK 和官方插件，不自动安装客户端。Full 会按需补齐 Desktop Runtime 与 ASP.NET Core Runtime；Upgrade 若缺少任一 Server 运行时前置会拒绝执行并提示改用 Full。

## Docker 包

准备机要求：bash、Docker、Python 3、curl、sha256sum 和 gzip，可访问 GitHub Releases 与镜像仓库。

```bash
bash build-docker-offline.sh --output-dir ./dist
```

可选缓存目录：

```bash
bash build-docker-offline.sh --output-dir ./dist --cache-dir ./cache
```

输出 `labelframe-offline-<版本>-linux-x64.tar.gz` 与同名 `.sha256` 旁车。把两者一并拷到目标机，先执行 `sha256sum -c <归档名>.sha256`，再解压并运行包内既有 `install.sh`。

## 完整性与缓存

- LabelFrame 原子资源和固定版 .NET Runtime 按 `install-manifest.json` 的 SHA256 与大小校验；
- WebView2 Full 使用 x64 Standalone，校验固定文件名、体积大于 100 MB、Microsoft Authenticode 发布者；
- Docker 镜像按 Release 记录的不可变 digest 拉取并复核 RepoDigest；
- 缓存一致时复用；缓存不符先删除再重取；任一来源、哈希、签名或 digest 异常时非零退出，不保留最终命名的残包；
- 每个输出包包含 `offline-build-info.json`，记录实际来源、哈希、大小和生成时间。WebView2 Evergreen 可能随时间轮转，因此不承诺两次 Builder 输出逐字节相同。
