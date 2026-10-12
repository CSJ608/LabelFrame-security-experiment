# 模块设计文档（docs/design/）

> 承载：迭代 138（[#353](https://github.com/marci-labs/LabelFrame/issues/353)）· 结构模板：arc42 裁剪六节（业界调研结论见 Issue 正文）。
> 每篇描述一个模块的**现状设计**（逆向自代码），后续演进以代码与 [DESIGN.md](../DESIGN.md) 决策表为准。

## 与既有文档的分工

| 文档 | 回答的问题 | 更新时机 |
|---|---|---|
| [DESIGN.md](../DESIGN.md) | **Why**——愿景、总体架构、关键技术决策表（160+ 条决策原文） | 决策发生时 |
| [ARCHITECTURE-REVIEW.md](../ARCHITECTURE-REVIEW.md) | **When**——某时点的全仓架构评审快照（体量度量、热点、对标） | 评审时点 |
| docs/design/*.md（本目录） | **How**——各模块的机制、契约、取舍与坑（模块级现状设计） | 模块设计实质变化时 |

同一事实只在一处维护：决策原文只在 DESIGN.md，本目录只引决策号不重复叙述；全仓级视图只在 ARCHITECTURE-REVIEW.md。

## 模块清单

| 模块 | 文档 | 覆盖范围 | 状态 |
|---|---|---|---|
| LabelFrame.Rendering | [rendering.md](rendering.md) | Skia 整版位图渲染（5 文件） | ✅ 试点首篇 |
| LabelFrame.Core | [core.md](core.md) | 契约 / 版式模型 / 校验 / ZPL 编码 / 作业队列 / 模板库 / 插件接口（共享内核，72 文件） | ✅ |
| LabelFrame.Api | [api.md](api.md) | Server / WinHost 共享 HTTP 契约与端点 | ✅ |
| LabelFrame.Server | [server.md](server.md) | 无头服务端（模板库 / 作业中心 / 投递 / 出图 / 日志） | ✅ |
| LabelFrame.WinHost | [winhost.md](winhost.md) | Client Host（Windows 完整客户端 + Linux 无头 Log 客户端） | ✅ |
| LabelFrame.AndroidHost | [androidhost.md](androidhost.md) | Android / PDA 打印宿主 | ✅ |
| LabelFrame.TransportPlugin.Zebra | [transport-plugin-zebra.md](transport-plugin-zebra.md) | Zebra 官方外置传输插件（插件 ABI 面） | ✅ |
| LabelFrame.Bootstrapper（三件） | [bootstrapper.md](bootstrapper.md) | 安装引导（共享库 / BA / PayloadTool，三件合一） | ✅ |
| web 前端 | [web-frontend.md](web-frontend.md) | `web/src/`（pages / lib / components / i18n / state） | ✅ |

## 六节结构模板（每篇固定）

1. **职责与边界**——模块做什么、不做什么（边界外去向）
2. **内部结构**——构建块分组（类型 / 子系统）与关键机制
3. **关键流程**——2~5 个核心场景的调用链与数据流
4. **对外契约与依赖**——接口面、依赖了谁、被谁依赖
5. **设计取舍**——以决策号索引为主（指向 DESIGN.md），不重复叙述
6. **平台坑与不变量**——修改本模块前必须知道的约束与已踩过的坑

## 与源码注释的关系（决策 #189）

本目录承接源码注释中的设计知识（机制 / 契约 / 取舍叙述）。源码注释按「极简化」口径执行：

- **保留白名单（代码原位）**：不变量、平台坑、兼容义务、防回归警示——去掉会重踩踩过的坑的四类；
- **删除**：机制描述、契约说明、取舍叙述——由本目录文档承载（模块文档与注释删减同 PR 交付、文档在前）；
- 一句话「对象用途与功能」的 summary 保留（IDE 悬停与 API 速查本位）；过程标记全面禁止（决策 #187）。
