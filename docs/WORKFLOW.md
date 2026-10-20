# LabelFrame 工作流（GitHub）

> 版本：2 · 落地：2026-09-09（迭代 42，决策 #97）；2026-09-10 增补 §7 多会话 worktree 并行试行（流程治理 #17）；2026-09-10 增第三必需检查「Android 构建（PDA 宿主）」（迭代 49，决策 #104）。
> 参考外部通用「产品迭代工作流」资料 v1.1，已按本仓库事实实例化；仓库运行不依赖外部资料。
> 修改本文件、门禁规则或流程模板属于**流程治理迭代**，普通迭代不做。

## 1. 平台与仓库事实

| 项 | 事实 |
|---|---|
| 权威仓库 | `github.com/marci-labs/LabelFrame`（公开；远端 `origin` 为唯一权威） |
| 默认分支 | `master` |
| 任务载体 | GitHub Issue（[迭代任务模板](../.github/ISSUE_TEMPLATE/iteration.md)立项；缺陷 / 功能沿用各自模板） |
| 变更载体 | 短主题分支 + PR |
| 合并方式 | squash；PR 标题 = Conventional Commits（中文说明为主） |
| 分支清理 | 仓库开启合并后自动删除远端分支；本地分支合并后手动清理 |
| 门禁 | ruleset「master 门禁」：必须 PR + 三项必需检查（strict，要求目标分支新鲜度）+ 禁强推；**管理员无豁免名单** |
| 必需检查 | 「构建与测试（dotnet + 前端）」「MSI 结构断言（安装包 UI 契约）」「Android 构建（PDA 宿主）」（均来自 [ci.yml](../.github/workflows/ci.yml)；第三项为迭代 49 引入——先合入 job 的 PR 验证全绿、合并后立即补进门禁，避免 job 未入库时必需检查自锁）。改名 / 增删必须**先改门禁再改 ci.yml**（删除方向），避免出现无保护窗口 |
| 日常 CI | push master / 全部 PR / 手动：dotnet 构建 + 测试（排除 Perf/Soak）+ 前端 lint / 双模式测试 / 双构建 + MSI 结构断言 + Android 构建（Release APK）；Perf/Soak 由 [nightly-perf.yml](../.github/workflows/nightly-perf.yml) 周一 04:00 跑 |
| 发布 | 记账 PR 合并后推 `v*` tag → [release.yml](../.github/workflows/release.yml) 自动构建发布（ghcr 镜像 + MSI / 插件 zip / Linux 归档 / Android APK 上传 Release）；除发版 tag 外不推其他 tag |

## 2. 日常迭代流程

1. **立项**：从「迭代任务」模板建 Issue，写清目标与依据 / 范围 / 不在范围 / 验收标准（AC-xx）/ 启动命令。可用工作区 skill `/new-iteration` 辅助起草与创建（`.zcode/skills/new-iteration/`，随仓库版本化）。
2. **恢复上下文**：新会话读 `AGENTS.md` → 本文件 → Issue 全文（含评论）→ 相关文档（DESIGN / REQUIREMENTS / ROADMAP 状态总览）。不重新初始化流程。
3. **实施**：从最新 `master` 切短主题分支（建议 `iter/<N>-<slug>`、`fix/<slug>`、`feat/<slug>`）；严格按 Issue 范围，新想法开新 Issue 或记入 DESIGN「未决问题」。
4. **本地检查**：`dotnet build LabelFrame.slnx` + `dotnet test`（排除 Perf/Soak）；前端改动加 `pnpm lint / test / build`。不用历史结果充当本轮结果。
5. **提 PR**：正文按 PR 模板；关联 Issue 用 `#N` 普通引用——**验收未完成时禁用 `Closes` / `Fixes` 等关闭关键词**（避免代码一合并就提前结项）。
6. **CI 与复审**：三项必需检查必须针对**最新提交**全绿；旧提交的绿灯不覆盖新提交。自查清单按 PR 模板如实勾选。
7. **合并**：squash；合并后回读 master 提交与 PR 状态确认落地；清理本地分支。
8. **结项或待验收**：
   - 验收完成 → 验收证据（AC-xx 逐条：通过 / 失败 / 跳过 + 证据链接）回写 Issue → 关闭 Issue；ROADMAP 状态总览更新一行（✅ + Issue 链接），CHANGELOG 随 PR 更新。
   - 验收滞后（真机 / 用户 / 长测）→ Issue 打 `待验收` 标签**保持开放**，欠账项与恢复条件写进 Issue；结项时关闭。

发版记账（版本号 / ROADMAP / CHANGELOG）同样走 PR；tag 只推 `v*`。

## 3. 事实源唯一

| 事实 | 权威位置 |
|---|---|
| 本轮需求 / 范围 / 决议 / 进度 / 验收 | Issue（正文 + 评论） |
| 变更内容与检查证据 | PR + Actions 运行记录 |
| 迭代索引与状态总览 | [ROADMAP.md](ROADMAP.md)（结项时更新一行；迭代 0-41 历史详情在 [archive/ROADMAP-ITERATIONS.md](archive/ROADMAP-ITERATIONS.md)） |
| 版本正文 | [CHANGELOG.md](../CHANGELOG.md) + GitHub Release |
| 架构与关键技术决策 | [DESIGN.md](DESIGN.md) 决策表 |
| 验收欠账 | 打 `待验收` 标签的 Issue |
| 契约行为 | 代码 + DESIGN.md「5. API 概览」等章节 |

同一事实只在一处维护，其他位置只留链接；Issue / PR 已能接续时不另建本地交接文件。

## 4. 风险裁剪

- **轻量**（文案 / 文档 / 局部小修）：Issue 与 PR 描述可精简（问题 / 验证 / 剩余三段）；CI 门禁不变。
- **标准**（默认）：模板全字段；UI 变更附真实界面验收证据；回归测试随风险补充。
- **强化**（跨端公共契约：模板包格式 / 打印 API / 作业模型；权限 / 数据迁移）：先更新 DESIGN 文档再改代码（AGENTS 既有约束）；PR 附兼容影响与回退方式；合并后增加真机 / 集成复验。

## 5. 门禁演练记录（迭代 42 落地证据）

| 演练 | 结果 | 证据 |
|---|---|---|
| 失败阻断：故意失败 PR 应被禁止合并 | ✅ 通过——两项必需检查失败（注入 `DrillGateFail.cs` 语法错误，dotnet build 报 `CS1525`，因果确认），`mergeStateStatus=BLOCKED`，平台禁止合并；PR 关闭、分支删除 | [PR #2](https://github.com/marci-labs/LabelFrame/pull/2)（job 日志见 Actions run 34326168208） |
| 正常合并：合法变更全绿后可 squash 合并 | ✅ 通过——本表所在的 PR（首个正式 PR）两项必需检查针对最新提交全绿后 squash 合并入 master | [PR #3](https://github.com/marci-labs/LabelFrame/pull/3) |

## 6. 选配未启用

Issue 自动巡检、合并队列、GitHub Projects 看板、规格驱动（Gherkin / TDD / 变异测试）——启用任一项需开流程治理迭代评估收益与代价。

## 7. 多会话 worktree 并行（试行，2026-09-10 · #17）

> 状态：**试行**——默认仍单迭代串行，并行按需启用；首次并行演练后按 §7.1 记录实际效果，再定转正 / 调整 / 回退。

- **适用条件**：迭代间代码重叠低（不同目录 / 层，如纯前端 vs 服务端 + 宿主）；同时并行 ≤ 2 个；公共契约或同文件重叠高的迭代（如都要改 `client.ts` / DESIGN 同一节）串行执行，先立项者先行。
- **工作区与会话约定**：`git worktree add ../LabelFrame-wt<N>-<slug> -b <迭代分支> origin/master`——一个执行会话绑定一个 worktree（工作目录即 worktree 根）；主 checkout 只保留 `master` 与日常维护，不承载迭代分支；每个 worktree 首次使用需在 `web/` 执行 `pnpm install`（pnpm store 全局共享，秒级）；.NET 还原随首次构建自动完成。
- **流程不变**：worktree 内同样走短主题分支 → PR → 双必需检查 → squash；每个 PR 的检查必须针对**各自最新提交**全绿。
- **汇合规则**：master 门禁为 strict 目标分支新鲜度——**后合并方负责 rebase 到新 master 并重跑检查**；结项记账（CHANGELOG / ROADMAP 状态总览）双方都会改同一片区域，冲突由后合并方解决（squash 后按时间序重排新条目即可）。
- **运行时限制**：真机 / 客户端验证**串行**（WinHost 全局单实例互斥锁 + 端口 53960 唯一）——两个 worktree 不得同时起真实客户端；界面联调各自走沙箱 mock（独立端口）互不干扰。

### 7.1 演练记录

| 演练 | 结果 | 证据 |
|---|---|---|
| 首次并行：迭代 46（缩略图预览 #15）+ 迭代 47（作业可观测性 #16） | ✅ 完成并**转正**（约定不变）——两迭代在独立 worktree 双会话并行开发交付（PR #19 / #20），汇合按本节约定执行：46 先合并、47 rebase 到新 master 重跑检查后合并，无冲突返工；后续验收修订（#21~#23）在主 checkout 串行承接。暴露的两条教训均非并行机制本身所致，已各自形成对策：① 主 checkout 承接 UI 验收前未重建 `web/dist`（#15 首验扑空）→ 纳入验收前自查；② preview 请求形态与旧版 Server 不兼容（裸 POST 404）由真机验收方暴露（mock 测试盲区）→ 增补请求形态回归测试（#22） | [#17 结项评论](https://github.com/marci-labs/LabelFrame/issues/17)；两 Issue 验收记录：#15 / #16 |
