# LabelFrame 代码评审基线

> 用途：自动化评审岗（契约规范员等）的判断锚点。
> **红线：本基线没有的条目不得作为「阻断」，只能列「建议」并注明「无基线可依」。**
> 状态：已随迭代 120（#278）入库生效（2026-10-08 重建，接替 2026-09-21 散失版本）。

## 放置路由

- Web 前端：页面 `web/src/pages/<Page>.tsx` + 同名 `.test.tsx`；跨页面复用组件 `web/src/components/`；全局状态 `web/src/state/`；文案资源 `web/src/i18n/`（语义 key，zh 源 + en 同步）。
- 后端按 `LabelFrame.*` 既有分层：契约/模型 Core、Server API、宿主逻辑 WinHost / AndroidHost；渲染 Rendering；官方传输插件 `src/LabelFrame.TransportPlugin.*`（Zebra / TSPL——外置 .lfplugin 工程，只依赖 Core，同构扩展见决策 #195）。
- 新建目录 / 项目须有 Issue 条款依据；无依据只给「建议」级提醒。

## 命名与品牌红线（阻断）

- 任何代码 / 文件 / 文档不得出现公司、业务线品牌名称或既有仓储项目命名前缀；统一 `LabelFrame.*` 命名。
- 代码标识符英文；文档与注释中文。

## 注释 rubric

- 注释只写「代码本身表达不了的约束」：为什么这么做、外部契约引用、踩坑规避；复述代码做了什么的注释应删。
- 导出类型 / 公共 API 保留中文文档注释。
- 删注释与加注释同等受审（防止顺手删掉有约束信息的注释）。

## i18n 纪律（阻断）

- 用户可见文案必须走 `web/src/i18n/` 资源（语义 key），禁止组件内硬编码用户可见的中英文字符串。
- zh 与 en 键集必须一致（缺失键 = 阻断）；术语与既有资源对齐（同一概念前后一致）。

## 测试规范

- 新页面 / 组件配 `.test.tsx`（渲染 + 关键交互断言）；纯函数配单测。
- 修 bug 附回归测试（能先红后绿更佳）。
- 测试不得依赖真实网络 / 打印机 / 真实客户端（mock 或沙箱端口）。

## 契约与流程（阻断）

- 公共契约（模板包格式 / 打印 API / 作业模型）变更必须先更新 `docs/DESIGN.md` 再改代码。
- PR：Conventional Commits 中文标题；Issue 用 `#N` 普通引用；验收未完成禁用 Closes / Fixes 等关闭关键词；代码改动必须更新 `CHANGELOG.md`。
- 不碰 `.github/workflows/`、门禁 ruleset、Issue / PR 模板、`docs/WORKFLOW.md`、`AGENTS.md`（流程治理迭代专属）。
