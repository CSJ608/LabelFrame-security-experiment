# AGENTS —— 协作约束

本文件是本仓库中 AI 协作的常驻规则，所有迭代执行前必须阅读。

## 项目身份

- 仓库名：LabelFrame；命名空间统一使用 `LabelFrame.*`。
- 仓库内容（文档、代码、提交信息、目录命名）不得出现任何公司 / 业务线品牌名称或既有仓储项目命名前缀；需要时使用本仓库自己的命名。
- 项目定位：独立、完整的标签打印解决方案，不依赖任何既有业务系统；业务系统只是调用方。

## 工作流（迭代 42 起）

- 流程细则与平台事实见 [docs/WORKFLOW.md](docs/WORKFLOW.md)：**迭代任务全面在 GitHub Issue 承载**（「迭代任务」模板立项），变更走短主题分支 + PR + squash 合并。
- 执行会话按此顺序恢复上下文：`AGENTS.md` → `docs/WORKFLOW.md` → 本轮 Issue 全文（含评论）→ 相关文档；已有流程在途时不重新初始化。
- 开始前必读：`README.md`、`docs/DESIGN.md`、`docs/REQUIREMENTS.md`、`docs/ROADMAP.md`（状态总览）。
- `master` 受 ruleset 门禁保护（必须 PR + 三项必需检查 + 禁强推），**不直推 master**；PR 标题用 Conventional Commits（中文说明为主）。
- PR 关联 Issue 用 `#N` 普通引用；**验收未完成时禁用 `Closes` / `Fixes` 等关闭关键词**——验收滞后（真机 / 用户 / 长测）给 Issue 打 `待验收` 标签保持开放。
- 严格按 Issue「范围 / 不在范围」执行；不擅自添加未规划内容；新想法开新 Issue 或记入 `docs/DESIGN.md` 的「未决问题」，与用户讨论后再排期。

## 完成定义（DoD）

一个迭代完成必须同时满足：

1. Issue 验收标准（AC-xx）全部满足（滞后的转 `待验收` 标签并记录恢复条件）；
2. `dotnet build` / `dotnet test`（排除 Perf/Soak）本地通过；前端改动含 `pnpm lint / test`；
3. PR 三项必需检查（「构建与测试（dotnet + 前端）」「MSI 结构断言（安装包 UI 契约）」「Android 构建（PDA 宿主）」）针对最新提交全绿并已 squash 合入 `master`；
4. `CHANGELOG.md` 更新（随 PR 提交）；`docs/ROADMAP.md` 状态总览更新一行（✅ + Issue 链接）；
5. 关键决策或风险变化已记入 `docs/DESIGN.md` 决策表；
6. 验收证据（AC-xx 逐条：通过 / 失败 / 跳过 + 证据链接）回写 Issue 后关闭，或转 `待验收`。

## 其他约束

- 发布流程（迭代 21 起，机制不变）：发版 = 记账 PR 合并后推送 `v*` tag（如 `v0.17.0`），由 GitHub Actions 自动构建发布——Server Docker 镜像推 ghcr.io、PC 安装包（Server / Client MSI、插件 zip、Linux 归档）与 PDA 宿主 APK 上传 GitHub Release；除发版 tag 外不推其他 tag。
- 修改 CI workflow（`.github/workflows/`）、门禁 ruleset、Issue / PR 模板或 `docs/WORKFLOW.md` 属于**流程治理迭代**；普通迭代不做。
- 文档与注释使用中文；代码标识符使用英文。
- **源码注释规范（决策 #187 / #189，所有新代码即刻生效）**：一句话「对象用途与功能」summary＋白名单四类（不变量 / 平台坑 / 兼容义务 / 防回归警示）保留；机制 / 契约 / 取舍叙述写入 `docs/design/` 对应篇而非堆在代码注释；注释中**禁止过程标记**（迭代号 / 决策号 / Issue 与 PR 引用 / AC 编号 / 返修轮次等——决策依据进文档）。迭代涉及模块结构或接口契约实质变化时，AC 须含 `docs/design/` 同步条目（#189 ④）。核查入口：`/comment-check` 技能或 `python scripts/check-comment-hygiene.py <base>`。
- 涉及跨迭代的公共契约（模板包格式、打印 API、作业模型）变更，必须先讨论并更新 `docs/DESIGN.md`，再改代码（强化路径）。
