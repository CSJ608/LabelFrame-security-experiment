# LabelFrame Server Docker 离线部署包（__LABELFRAME_VERSION__，linux-x64）

本包由**有网准备机**上的 LabelFrame Offline Builder 生成，用于**无外网目标机**部署或升级 LabelFrame Server。目标机侧继续使用包内既有 `install.sh`；包结构、校验、资源分发、健康检查与固定目录升级语义保持不变。

## 在有网准备机生成本包

从对应版本的 GitHub Release 下载并解压 `labelframe-offline-builder-<版本>.zip`，在已安装 Docker 的准备机运行：

```bash
bash build-docker-offline.sh --output-dir ./dist
# 可选：复用下载缓存
bash build-docker-offline.sh --output-dir ./dist --cache-dir ./cache
```

Builder 会按 Release 记录的不可变 digest 拉取并复核 Server 镜像，下载 Client MSI、PDA APK、Web UI 与官方插件并校验 SHA256，最后生成 `labelframe-offline-__LABELFRAME_VERSION__-linux-x64.tar.gz` 与同名 `.sha256` 旁车。任一资源哈希或镜像 RepoDigest 不符即拒绝产包；包内 `offline-build-info.json` 记录实际镜像与资源哈希。

## 目标机前置条件

- Linux x64 + Docker Engine（含 `docker compose` v2 子命令）；
- `sha256sum`（coreutils，一般发行版自带）；
- **不需要外网**，但本包不含 Docker 本体。

## 解压到固定部署目录

部署与以后升级始终使用**同一目录**，因为数据卷实际名包含部署目录名：

```bash
sha256sum -c labelframe-offline-__LABELFRAME_VERSION__-linux-x64.tar.gz.sha256
mkdir -p labelframe-offline
tar -xzf labelframe-offline-__LABELFRAME_VERSION__-linux-x64.tar.gz -C labelframe-offline --strip-components=1
cd labelframe-offline
```

先验证归档旁车，再执行包内脚本；包内 `SHA256SUMS` 会二次校验除账本自身外的全部部署文件（含 `install.sh`、Compose、环境文件、Web UI、镜像与分发资源）。

## 一键部署

```bash
bash install.sh
curl http://127.0.0.1:53961/healthz
```

`install.sh` 会强制执行 `SHA256SUMS` 校验，加载 Server 镜像，预建分发目录，把 Client MSI、PDA APK 与官方插件复制到对应挂载目录，启动 Compose，等待 `/healthz` 就绪并输出访问地址。脚本幂等，可安全重跑。

管理界面 `http://<目标机 IP>:53961/` 已随包预装。客户端连接地址使用 `http://<目标机 IP>:53961`。

## 手动等价步骤

```bash
sha256sum -c SHA256SUMS
docker load -i images/labelframe-server-__LABELFRAME_VERSION__.image.tar.gz
mkdir -p client-packages pda-packages plugin-packages
docker compose up -d
curl http://127.0.0.1:53961/healthz
```

手动步骤不会自动把 `packages/` 内容复制到分发目录，需自行拷贝；优先运行 `install.sh`。

## 升级

拿新版本 Builder 生成的新包，覆盖解压到**当初部署的同一目录**后继续运行：

```bash
bash install.sh
```

数据卷、模板、数据库与日志保留。不要换新目录升级：换目录会创建新的空数据卷，且固定容器名可能与旧部署冲突。可选删除 `images/` 下旧版本镜像归档释放磁盘，不影响运行数据。

曾按带版本号目录部署的存量环境，也应继续在原目录覆盖解压并运行 `install.sh`，无需迁移数据。

## 可选：接入一个现有 Docker 网络

默认服务加入自身 Compose 网络并发布宿主端口 53961。同机其他容器需要通过容器 DNS 访问时，在部署目录创建：

```bash
docker network create shared-print
printf 'LABELFRAME_EXTERNAL_NETWORK=shared-print
' > network.env
bash install.sh
```

也可单次运行 `LABELFRAME_EXTERNAL_NETWORK=shared-print bash install.sh`。脚本会验证该网络是本地用户自定义 bridge；同目录升级会保留 `network.env`。调用方容器也须加入该网络，使用 `http://labelframe-server:53961`。服务端 API 无鉴权，只接入受信任网络。

## 分发资源

| 包内文件 | Installer 落位目录 | 使用方式 |
|---|---|---|
| `packages/client/*.msi` | `./client-packages/` | 客户端设置页与服务端下载中心 |
| `packages/pda/*.apk` | `./pda-packages/` | PDA 扫下载中心二维码安装 |
| `packages/plugin/*.lfplugin` | `./plugin-packages/` | 客户端 / PDA 插件管理 |
| `plugins/web-ui/` | Compose Web UI 挂载目录 | 服务端管理界面 |

后续可直接向挂载目录增删文件，或经管理界面上传。若目录曾被 Docker 以 root 创建而不可写，执行：

```bash
sudo chown -R "$(id -un):$(id -gn)" client-packages pda-packages plugin-packages
bash install.sh
```

## 常见排障

- 服务状态：`docker compose ps`；
- 容器日志：`docker compose logs --tail=100` 或 `docker compose logs -f`；
- 文本日志：`./logs/server-<yyyyMMdd>.log`；
- 防火墙：按需执行 `sudo ufw allow 53961/tcp`；
- 数据卷：`docker volume ls`，默认名为 `<部署目录名>_labelframe-data`；
- 离线原理：镜像由 `docker load` 以 compose 所需完整 tag 导入，本地命中后不会联网拉取。

## 包内容一览

| 文件 / 目录 | 说明 |
|---|---|
| `images/labelframe-server-__LABELFRAME_VERSION__.image.tar.gz` | 按 Release digest 校验后导出的 Server 镜像 |
| `compose.yml` + `.env` | 部署描述与版本钉定配置 |
| `install.sh` / `README.md` | 离线目标机 Installer / 本文档 |
| 同名 `.tar.gz.sha256` 旁车 | 执行包内脚本前验证整个归档 |
| `SHA256SUMS` | 除账本自身外全部部署文件的包内完整性账本 |
| `offline-build-info.json` | Builder 的镜像 RepoDigest 与资源审计记录 |
| `packages/` | Client MSI、PDA APK、官方传输插件 |
| `plugins/web-ui/` | 已预解压的服务端管理界面 |
