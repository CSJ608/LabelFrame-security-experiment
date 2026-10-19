# Linux Client 与容器 E2E

## 定位

Linux Client 是正式发布的无头测试宿主，复用 Windows Client 的作业队列、Server 轮询、Skia 渲染、`^GF` 编码与结果回报链路。首版仅注册 `log` 传输：不连接物理打印机，渲染结果保存为 PNG。

仓库提供两种 Compose：

- `packaging/e2e/compose.yaml`：稳定 Server 基线 + 当前 Server UI / Linux Client 源码候选，供开发使用；
- `packaging/e2e/compose.release.yaml`：只引用同版本 Server / Linux Client 镜像，不含 `build`，供发布门禁与稳定版复验使用。

发布流水线先各构建一次 Server / Client 候选镜像，再用第二种 Compose 验收；通过后直接给同一镜像追加 `latest` 并推送，不重新构建。

## 启动与冒烟

```powershell
# 一次完成镜像构建、启动、设备上线、单张 / 多张、PNG 解码与重启后继续领取验证
.\scripts\test-linux-client-e2e.ps1

# 拉取并验证同版本正式镜像
$env:LABELFRAME_VERSION = "1.0.0"
.\scripts\test-linux-client-e2e.ps1 `
  -ComposeFile packaging/e2e/compose.release.yaml -SkipBuild
```

脚本通过后保留环境，管理界面为 `http://127.0.0.1:53910`。端口被占用时传 `-ServerPort`；稳定版 Compose 停止命令中的文件名相应改为 `compose.release.yaml`。

```powershell
.\scripts\test-linux-client-e2e.ps1 -ServerPort 53963
```

只启动环境：

```powershell
docker compose -f packaging/e2e/compose.yaml up -d --build
```

查看 Client 健康状态与 Log 输出：

```powershell
docker compose -f packaging/e2e/compose.yaml exec labelframe-client curl -s http://127.0.0.1:53960/healthz
docker compose -f packaging/e2e/compose.yaml exec labelframe-client find /var/lib/labelframe/client/print -name '*.png'
docker compose -f packaging/e2e/compose.yaml logs -f labelframe-client
```

停止环境：

```powershell
docker compose -f packaging/e2e/compose.yaml down
```

默认不删除数据卷，便于复查作业和 PNG。确认不再需要数据后，显式执行 `docker compose -f packaging/e2e/compose.yaml down --volumes`。

## 测试大纲

| 层级 | 检查项 | 判定 |
|---|---|---|
| 镜像 | Linux 目标发布、进程启动、健康检查 | `/healthz` 返回 `platform=linux`、`headless=true` |
| 能力边界 | 传输列表、插件与 UI | 仅 `log`；插件安装端点与 Client 根页面为 404 |
| 路由 | 注册、心跳、领取、回报 | Server 设备在线；作业最终 `Completed` |
| 渲染 | 单张 / 多张、中文、条码 | 每个 Item 生成一张非空 PNG，数量一致；直接复制 Compose 数据卷产物并解码出预期 Code128 内容 |
| 可靠性 | 幂等、Client 重启、持久化 | 不重复建作业；重启后重新心跳，再提交新作业并完成领取、出图与终态回报 |
| 浏览器 E2E | 模板保存、数据填写、设备选择、打印 | Server UI 主链可完成，页面无阻断性错误 |
| 回归 | Windows Client 与全仓测试 | 既有构建 / 测试全绿，Windows 专属传输仍可编译 |

## 严格补证记录（2026-08-28）

- 浏览器在 Server 管理界面设计器中新建 `Linux UI E2E 20260828`，放置 Code128 条码、绑定 `code` 字段并保存；随后选择同一模板与在线设备 `linux-e2e-01`，填写 `LF-UI-COMPOSE-001` 并完成单张打印。Server 作业 `5363d5333855447ebc788c4a583fd5e3` 与 Client 本地作业 `d106ae755fb14e1fa34d8df76e250846` 均为 `Completed`，浏览器控制台无 warning / error。
- 自动化脚本在 Client 重启后先断言重启前的单张 / 多张本地作业仍存在，再提交新作业 `1c728cddb1644d5b866dc6bba70eed66`；设备恢复 `Online`，新作业完成领取、出图与终态回报。
- 脚本通过 `docker compose cp` 直接复制 Client 命名卷中的 PNG，由 `LabelFrame.PrintImageVerifier` 检查图片非空白并逐张解码 Code128。本轮自动化 5 张均通过；浏览器作业 PNG 另行直接解码为 `LF-UI-COMPOSE-001`。
- `0.22.0` 精确版本候选双镜像在只引用镜像的 Compose 中通过：公共模板 / 预览 / 模板包 / Excel / 日志端点、Linux 能力裁剪、幂等、单张 + 3 张、离线暂存、重启持久化与重启后新作业，共 6 张 PNG 均解码成功。
- Release `33133706115` 在 Ubuntu runner 上通过同一候选 E2E 后推送原镜像；发布后重新 pull GHCR 双镜像，以全新项目卷复验同一完整大纲并通过。Server 摘要 `sha256:73080dc76df8b14faa19e41cee9074dcc1067814587b7c61360fda455040abd9`，Client 摘要 `sha256:6bf9d318c9f94fca134d0ccbbf394c2bb7da84e4ac6ea10a33189f0d1ac7770d`；稳定复验作业为单张 `4852e406b9664c19961db831d97ad4ba`、3 张 `7b70ccb0277d464f83259075e0aead99`、离线暂存 `4851a04cf5a942bf8a4be45cb1dbc678`、重启后新作业 `0cca13b3193745b4a0eb3e94689e07fb`。

## 已知边界

- Linux Client 不提供图形界面，配置全部来自 `LABELFRAME_*` 环境变量。
- 首版不支持 TCP 9100、USB、Windows 驱动、Zebra SDK 或第三方传输插件。
- Log 成功只证明软件链路与渲染输出正确，不替代真实打印机的走纸、扫码、缺纸和状态回读验收。
