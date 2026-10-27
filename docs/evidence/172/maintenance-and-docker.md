# 人工恢复与 Docker 部署简化验证

日期：2026-10-27；任务 [#474](https://github.com/marci-labs/LabelFrame/issues/474)。这是主机测试与隔离容器证据，不是 PDA ART、安装回执或实升验收。

## 用户批准与实现边界

可信局域网下优先简单部署、可维护：不增加领取围栏、设备认证或配套服务。人工恢复使用既有服务端管理口令；保存原始日志快照和原因，明确风险接受而非系统核验成功。已知整批、挂起、编辑、领取在途、待回报及活动安装会话仍不能跳过。新的领取失败重新阻断；安装人工终态拒绝迟到回执覆盖。

Docker 内置 JDK17、Google Maven apksig 8.9.2 与 aapt2 8.9.2-12782657，工具下载及 SHA256 校验仅发生在镜像构建时。正式证书指纹使用已核验的 v1.5.0/v1.4.0 发布身份；自签部署可显式覆盖。既有镜像＋资源包、数据卷和管理口令流程保持，不增加工具挂载或现场 SDK 安装。既有 HTTPS/回环管理要求未改变。

## 主机回归

| 检查 | 结果 |
|---|---|
| `dotnet build LabelFrame.slnx` | 0 警告、0 错误 |
| 日常全量测试，排除 Perf/Soak | 1590 通过、1 显式外部 APK 测试跳过；Core329 / Api36 / Android119 / Client3 / Bootstrapper282 / Server221 / WinHost600 |
| 新增人工恢复回归 | 19 项；覆盖审计保留、重启、新领取失败、忙态/编辑保护、签名/版本/事务不符、活动会话、审计写入失败、迟到回执、管理口令与重定向拒绝 |
| Android Release 1.5.2 / 10502 | 0 警告、0 错误；非调试，尚未安装到设备 |
| 显式最新 APK＋官方工具专项 | 19/19 通过、零跳过；含完整验签、错信任、篡改和截断 |
| 前端 | 两种模式各 66 文件 / 776 测试通过；lint 退出 0（既有 React 警告保留）；双构建通过（既有大 chunk 提示） |

APK 大小 23,906,366 字节；SHA256 `3D16E7F42B55DD34CD527F3AF9582BFCE791DB468CB9EDEC8D5DCF537E5D0B27`。签名为本机 Android Debug 证书 `BB1B172202460CE9726290813CEF475E1B99B8E5D58E98145BB527D3CEA40AC2`，不是正式发行签名。APK 非 debuggable 与使用测试证书是两回事。

构建中发现的 Android API28 平台告警已加系统版本检查并重新构建清零。首次 Linux publish 未指定 TFM、首次前端 server 测试调用不存在脚本均失败，已分别改为 `-f net10.0` 和 `pnpm exec cross-env VITE_UI_MODE=server vitest run` 成功重跑；不将失败命令计作通过。pnpm 自动追加的锁文件工具条目已撤回，没有升级依赖。

## 真实 Docker 断网验证

由当前 Server 发布目录和修改后的 `packaging/ubuntu/Dockerfile` 实际构建 `labelframe-android-update-474:validation`，镜像 ID `sha256:1bc4347216ef2e27759ac934eaacce9cb3bf0e40fc395fa73109c03d7fceae84`。运行使用 `--network none`、无端口映射、无生产数据卷；仅上述 APK 单文件只读挂载。请求从容器内 `127.0.0.1:53961` 发出。

| 操作 | 实测结果 |
|---|---|
| 默认正式指纹＋本地测试签名 APK | `/healthz` 正常；发布 HTTP400，明确“APK 发布证书与预配置可信指纹不一致” |
| 仅覆盖隔离测试指纹，其余使用镜像默认工具路径 | 发布 HTTP200；真实包名、版本10502、SDK23/36、arm64-v8a、证书、摘要和签名协议资产全部解析正确 |
| 发布目标 | 仅虚构 `container-validation-only`，批次 `aa637cbeb9dd4383b635468977b473f4`，不是授权真机 |
| 候选 APK 下载 | 下载文件 SHA256 与输入 APK 完全一致 |
| 暂停发布后查询候选 | 发布返回 `paused:true`，候选返回 `{"release":null}` |
| 外网与暴露面 | 容器检查为 `NetworkMode=none`、`PortBindings={}`；证明运行时无需下载工具或访问外网 |

隔离发布快照已复制保留在本机临时目录 `LabelFrame-android-container-474-20261027/android-updates`；测试容器用 `--rm`，停止后删除其临时运行层，不涉及业务数据。验证镜像保留供复现，未推送镜像或发布 tag。

## 尚未验收

新增原生双语人工恢复界面、实际管理员输入/取消体验、ART 验签、系统确认/取消/后台/进程重建、MY_PACKAGE_REPLACED 健康链和整批/挂起/编辑负向场景仍需唯一授权 DT50 真机验证。本阶段没有设备操作，也未用测试冒充出纸或正式签名实升。原 APK/配置/数据备份和同签名恢复核查仍是下一阶段前置。
