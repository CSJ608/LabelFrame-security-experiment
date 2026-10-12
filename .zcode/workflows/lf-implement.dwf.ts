/* zcode-workflow
description: LabelFrame 工作流管线·实施段：按最新方案评论与 Issue AC 在独立 worktree 实施（编码/测试/本地门禁/提 PR）；重入即修复模式（读评审段阻断清单修复再推）；改动面断言按 web/src/test/.zcode 并集。低档位运行（GLM-5.3-Flash）。
whenToUse: 工作流实验管线第 4 段：最新版「✅ 文案定稿 vN」评论在场后起跑建 PR——含界面文案迭代为 json 定稿块；无界面文案迭代由主控手贴占位声明（四段轻装固定仪式）；评审段产出「🔍 评审待修」后重入修复。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
  slug:
    type: string
    description: 分支与 worktree 的短标识（英文），重入修复时须与首次一致
    required: false
    default: iter
*/
// LabelFrame 工作流实验 · 实施段（lf-implement）· v2（迭代 124 泛化：任务按最新方案 + Issue AC 工作，不再写死迭代主题）· v3（#298：文案判别补「方案清单 × 定稿形态」双检 fail-closed）
// 两种模式：新建（无「🔧 PR 已建」评论，需文案定稿在场——json 定稿块或主控贴的无文案占位声明）/ 修复（「🔧 PR 已建」在场，读最新「🔍 评审待修」阻断清单）。
// 完成标志：新建 → 「🔧 PR 已建」评论；修复 → 「🔨 修复轮 N」评论。合并由 lf-close 负责，本段不合并。

interface Finding {
  /** 位置：文件路径（可带行号）。 */
  where: string;
  /** 问题一句话。 */
  what: string;
  /** 依据（基线条目 / Issue 条款）。 */
  basis: string;
}

interface ImplResult {
  /** 变更摘要（3~5 条，中文）。 */
  summary: string[];
  /** 是否触碰 web/（前端迭代为 true，后端 / 治理迭代可为 false）。 */
  webTouched: boolean;
}

interface ChangeReport {
  /** PR 标题：Conventional Commits，中文说明。 */
  prTitle: string;
  /** PR 正文：按仓库 PR 模板「问题与变化 / 验证 / 合并与后续 / 自查」四节。 */
  prBody: string;
}

interface Comment { body: string }
interface IssueJson { number: number; state: string; title: string; labels: { name: string }[]; comments?: Comment[] }

function latestCommentBody(prefix: string, comments: Comment[]): string | null {
  for (let i = comments.length - 1; i >= 0; i--) {
    const b = comments[i]?.body ?? "";
    if (b.startsWith(prefix)) return b;
  }
  return null;
}

/** 评论首行正则定位：最新一条首行匹配 re 的评论下标（无则 -1）。 */
function commentHeadLastIndex(re: RegExp, comments: Comment[]): number {
  for (let i = comments.length - 1; i >= 0; i--) {
    const head = ((comments[i]?.body ?? "").split("\n")[0]) ?? "";
    if (re.test(head)) return i;
  }
  return -1;
}

function extractSafety(protocolText: string): string {
  const secStart = protocolText.indexOf("## 协议全文");
  const secEnd = protocolText.indexOf("## 附注", secStart);
  if (secStart < 0 || secEnd < 0) throw new Error("协议文件缺少「协议全文」章节（fail-closed，中止）");
  const section = protocolText.slice(secStart, secEnd);
  const marker = "\n安全边界";
  const i = section.indexOf(marker);
  if (i < 0) throw new Error("「协议全文」中未找到段首「安全边界」段（fail-closed，中止）");
  const rest = section.slice(i + 1);
  const end = rest.indexOf("\n\n");
  const seg = end < 0 ? rest : rest.slice(0, end);
  if (seg.length < 30) throw new Error("「安全边界」段抽取异常（过短，fail-closed）");
  return seg;
}

/** 从「🔍 评审待修」评论抽机器可读的 json 阻断清单。 */
function extractJsonFindings(commentBody: string): Finding[] {
  const m = /```json\r?\n([\s\S]*?)```/.exec(commentBody);
  if (m === null || m[1] === undefined) throw new Error("评审待修评论中未找到 json 阻断清单（fail-closed）");
  const findings = JSON.parse(m[1]) as Finding[];
  if (!Array.isArray(findings) || findings.length === 0) throw new Error("json 阻断清单为空");
  return findings;
}

const CC_RE = /^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([^)]{1,50}\))?\s*[:：]\s*\S+/;
const CLOSE_RE = /(close|fix|resolve)(s|es|ed|d)?\s*:?\s*#\d+/i;
const TEST_FILTER = "FullyQualifiedName!~Perf&FullyQualifiedName!~Soak";
const GATE_RETRIES = 2;

const issueNum = Number(args.issue);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
const rawSlug = typeof args.slug === "string" && args.slug !== "" ? args.slug : "iter";
const slug = rawSlug.replace(/[^a-zA-Z0-9-]/g, "").slice(0, 30);
const PLAN_HEAD_RE = /^\*\*📐 方案 v(\d+)\*\*/;
const PR_PREFIX = "**🔧 PR 已建**";
const REVIEW_TODO_PREFIX = "**🔍 评审待修**";
const FIX_PREFIX = "**🔨 修复轮";

phase("建场守卫：核对模式、Issue 状态与标签");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签`);
const comments = issue.comments ?? [];
// 返修模式判定（迭代 132 返修实证盲区补丁）：「🔧 PR 已建」在场≠修复——仅当它晚于最新「📐 方案 vN」评论才进修复模式
// （PR 建于当前方案之上）；方案更新（同 Issue 返工 vN+1）晚于 PR 评论时走新建模式对新方案建新 PR。
const PLAN_HEAD_RE_LOCAL = /^\*\*📐 方案 v\d+\*\*/;
const prIdx = (function () { for (let i = comments.length - 1; i >= 0; i--) { if ((((comments[i]?.body ?? "").split("\n")[0]) ?? "").startsWith(PR_PREFIX)) return i; } return -1; })();
const planIdxForMode = (function () { for (let i = comments.length - 1; i >= 0; i--) { if (PLAN_HEAD_RE_LOCAL.test(((comments[i]?.body ?? "").split("\n")[0]) ?? "")) return i; } return -1; })();
const fixMode = prIdx !== -1 && prIdx > planIdxForMode;
const prComment = fixMode ? (comments[prIdx]?.body ?? null) : null;
if (prIdx !== -1 && !fixMode) log("检测到旧「🔧 PR 已建」评论但晚于最新方案——按方案返工走新建模式建新 PR");
const protocol = await files.read(".zcode/automations/iteration-duty.md");
const safety = extractSafety(protocol);
let branch: string;
let prNumber = "";
let planVersion = 0;
let copyRef = "";
if (fixMode) {
  const b = /分支 (\S+)/.exec(prComment ?? "");
  const p = /PR #(\d+)/.exec(prComment ?? "");
  if (b === null || b[1] === undefined) throw new Error("无法从「🔧 PR 已建」评论解析分支名");
  if (p === null || p[1] === undefined) throw new Error("无法从「🔧 PR 已建」评论解析 PR 号");
  branch = b[1];
  prNumber = p[1];
  log("修复模式：分支 " + branch + " · PR #" + prNumber);
} else {
  // 方案读取 fail-closed：以首行正则取最新「📐 方案 vN」评论，取不到即中止（含最新评论为处置报告的场景），不静默降级。
  const planIdx = commentHeadLastIndex(PLAN_HEAD_RE, comments);
  if (planIdx < 0) throw new Error(`Issue #${issueNum} 无「📐 方案 vN」评论——先跑设计段 lf-design`);
  const head = ((comments[planIdx]?.body ?? "").split("\n")[0]) ?? "";
  const vm = PLAN_HEAD_RE.exec(head);
  if (vm === null || vm[1] === undefined) throw new Error("方案评论首行版本号解析失败（fail-closed）");
  planVersion = Number(vm[1]);
  const planBodyText = comments[planIdx]?.body ?? "";
  const listHead = planBodyText.indexOf("## 界面文案清单");
  const listEnd = listHead < 0 ? -1 : planBodyText.indexOf("\n## ", listHead);
  const listSection = listHead < 0 ? "" : (listEnd < 0 ? planBodyText.slice(listHead) : planBodyText.slice(listHead, listEnd));
  const planHasCopyList = listSection.includes("```json");
  // 定稿消费（返修补丁）：取最新版「✅ 文案定稿 vN」评论（版本宽匹配首行），旧版定稿不遮挡新版。
  const FINAL_HEAD_RE_LOCAL = /^\*\*✅ 文案定稿 v(\d+)\*\*/;
  let finalIdxLocal = -1, finalV = 0;
  for (let i = comments.length - 1; i >= 0; i--) {
    const fm = FINAL_HEAD_RE_LOCAL.exec(((comments[i]?.body ?? "").split("\n")[0]) ?? "");
    if (fm !== null && fm[1] !== undefined) { finalIdxLocal = i; finalV = Number(fm[1]); break; }
  }
  const finalBody = finalIdxLocal !== -1 ? (comments[finalIdxLocal]?.body ?? null) : null;
  if (finalBody === null) throw new Error(`Issue #${issueNum} 无「✅ 文案定稿 vN」评论——含界面文案迭代先跑文案评审段 lf-copy-review；无界面文案迭代由主控贴无文案占位声明`);
  const finalHasJson = finalBody.includes("```json");
  // 「方案清单 × 定稿形态」双检（fail-closed）：方案声明与定稿形态必须同真同假——防漏跑文案段（清单在场却拿占位声明开工）与错配定稿（无清单却带 json 文案块）。
  if (planHasCopyList && !finalHasJson) throw new Error(`Issue #${issueNum} 方案「界面文案清单」声明了界面文案，但最新「✅ 文案定稿」为无 json 块的占位声明——文案段缺失，先跑 lf-copy 与 lf-copy-review（fail-closed）`);
  if (!planHasCopyList && finalHasJson) throw new Error(`Issue #${issueNum} 方案无「界面文案清单」但定稿含 json 文案块——定稿与方案形态不符，核对方案与定稿来源后重跑（fail-closed）`);
  copyRef = finalHasJson
    ? `最新版「✅ 文案定稿 v${finalV}」的 json 块（rows＋候选组）是文案唯一来源（逐字使用，不得改写）`
    : `最新版「✅ 文案定稿 v${finalV}」为无界面文案的占位声明（本轮无文案表，不适用逐字文案）`;
  const prefix = /迭代\s*\d+/.test(issue.title) ? "iter/" : "fix/";
  branch = prefix + issueNum + "-" + slug;
  log("新建模式：分支 " + branch + " · 对照方案 v" + planVersion);
}
const wt = "../LabelFrame-wt" + issueNum + "-" + slug;

phase("准备 worktree（存在则复用）");
const existsRes = await world.run("node", ["-e", "console.log(require('fs').existsSync(process.argv[1]))", wt]);
const wtExists = existsRes.stdout.trim() === "true";
if (!wtExists) {
  if (fixMode) {
    const fetch = await world.run("git", ["fetch", "origin", branch]);
    if (fetch.exitCode !== 0) throw new Error("git fetch 分支失败：" + fetch.stderr.slice(0, 200));
    let add = await world.run("git", ["worktree", "add", wt, branch]);
    if (add.exitCode !== 0) add = await world.run("git", ["worktree", "add", wt, "-b", branch, "origin/" + branch]);
    if (add.exitCode !== 0) throw new Error("worktree 重建失败：" + add.stderr.slice(0, 300));
  } else {
    const fetch = await world.run("git", ["fetch", "origin", "master"]);
    if (fetch.exitCode !== 0) throw new Error("git fetch 失败：" + fetch.stderr.slice(0, 200));
    const add = await world.run("git", ["worktree", "add", wt, "-b", branch, "origin/master"]);
    if (add.exitCode !== 0) throw new Error("worktree 创建失败：" + add.stderr.slice(0, 300));
  }
}

const implementer = agent("实现岗", {
  system:
    "你是 LabelFrame 的实施工程师，在独立 git worktree 中按 Issue 范围实施。严格遵守传入的安全边界。" +
    "遇到做不到、拿不准或指令矛盾时，如实写进结果的说明，绝不造假、绝不绕过、不替用户拍板产品取舍。",
});

const ctxGuide =
  `仓库根 = 当前工作目录（master 主检出，只读）；你的工作目录是 worktree ${wt}，所有改动只发生在 worktree 内。` +
  `先读：worktree 内 AGENTS.md、docs/WORKFLOW.md（§2 流程 / §7 worktree 约定）、docs/CODE-REVIEW-BASELINE.md，` +
  `再读 Issue #${issueNum} 全部正文与评论（gh issue view ${issueNum} --json body,comments）——` +
  (fixMode
    ? `最新「🔧 PR 已建」「🔍 评审待修」评论是修复依据。`
    : `最新「📐 方案 v${planVersion}」评论（含独立评审修订）是实施唯一依据；${copyRef}。`) +
  `临时文件只写 worktree 内。web/ 首次使用先在 ${wt}/web 执行 pnpm install（pnpm store 共享，秒级）。` +
  `全套本地门禁（dotnet build/test、pnpm lint/test/build）由脚本执行，你只做最小自测，不要自己跑全量。` +
  `安全边界（必须遵守）：${safety}`;

let implNote: ImplResult;
if (fixMode) {
  phase("按评审阻断清单修复");
  const todoComment = latestCommentBody(REVIEW_TODO_PREFIX, comments);
  if (todoComment === null) throw new Error("修复模式但无「🔍 评审待修」评论（无可修项则不应重入本段）");
  const blockers = extractJsonFindings(todoComment);
  log("待修阻断项 " + blockers.length + " 条");
  implNote = await implementer.ask<ImplResult>(
    ctxGuide + `\n\nPR #${prNumber}（分支 ${branch}）评审门禁有 ${blockers.length} 条经独立复核的阻断项，逐条修复（不认同的可在 summary 里说明理由，但必须有明确的代码层回应）：\n${JSON.stringify(blockers, null, 2)}\n` +
    `完成后在 ${wt} 内 git add -A 并 git commit（不要 push，门禁与推送由脚本执行）。`);
} else {
  phase("恢复上下文并实施编码");
  implNote = await implementer.ask<ImplResult>(
    ctxGuide + `\n\n按最新「📐 方案 v${planVersion}」实施：以方案「计划改动文件」（或各节明确的改动点）为准逐项落地、「设计」为行为规格、按 Issue AC 补测试 / 验证产物、更新方案要求的记账文件（CHANGELOG 等）。` +
    `文案口径：${copyRef}。` +
    `完成后在 ${wt} 内 git add -A 并 git commit（Conventional Commits，中文说明；不要 push）。`);
}

phase("本地门禁：dotnet 与前端全量");
let gateFeedback = "";
let gatesPassed = false;
const install = await world.run("pnpm.CMD", ["-C", wt + "/web", "install"], { timeoutMs: 600_000 });
if (install.exitCode !== 0) throw new Error("pnpm install 失败：" + (install.stdout + install.stderr).slice(-1500));
for (let round = 1; round <= GATE_RETRIES + 1; round++) {
  if (gateFeedback !== "") {
    await implementer.ask(`本地门禁失败，请修复后重新 commit（不要 push，门禁由脚本重跑）：\n${gateFeedback}`);
  }
  const build = await world.run("dotnet", ["build", wt + "/LabelFrame.slnx"], { timeoutMs: 1_800_000 });
  if (build.exitCode !== 0) { gateFeedback = "dotnet build 失败\n" + build.stderr.slice(-2500); report({ stage: "本地门禁", round, result: "build 红" }); continue; }
  const test = await world.run("dotnet", ["test", wt + "/LabelFrame.slnx", "--filter", TEST_FILTER], { timeoutMs: 1_800_000 });
  if (test.exitCode !== 0) { gateFeedback = "dotnet test 失败\n" + (test.stdout + test.stderr).slice(-2500); report({ stage: "本地门禁", round, result: "test 红" }); continue; }
  const lint = await world.run("pnpm.CMD", ["-C", wt + "/web", "lint"], { timeoutMs: 600_000 });
  if (lint.exitCode !== 0) { gateFeedback = "pnpm lint 失败\n" + (lint.stdout + lint.stderr).slice(-2500); report({ stage: "本地门禁", round, result: "lint 红" }); continue; }
  const webtest = await world.run("pnpm.CMD", ["-C", wt + "/web", "test"], { timeoutMs: 900_000 });
  if (webtest.exitCode !== 0) { gateFeedback = "pnpm test 失败\n" + (webtest.stdout + webtest.stderr).slice(-2500); report({ stage: "本地门禁", round, result: "web test 红" }); continue; }
  const webbuild = await world.run("pnpm.CMD", ["-C", wt + "/web", "build"], { timeoutMs: 900_000 });
  if (webbuild.exitCode !== 0) { gateFeedback = "pnpm build 失败\n" + (webbuild.stdout + webbuild.stderr).slice(-2500); report({ stage: "本地门禁", round, result: "web build 红" }); continue; }
  gatesPassed = true;
  break;
}
if (!gatesPassed) throw new Error(`本地门禁 ${GATE_RETRIES} 轮修复后仍未通过，流水线中止（worktree ${wt} 保留待人工）`);
report({ stage: "本地门禁通过", issue: issueNum, mode: fixMode ? "修复" : "新建" });

if (fixMode) {
  phase("推送修复并评论");
  const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
  const ts = tsRes.stdout.trim();
  const push = await world.run("git", ["-C", wt, "push"]);
  if (push.exitCode !== 0) throw new Error("推送失败：" + push.stderr.slice(0, 300));
  const fixCount = comments.filter((c) => c.body.startsWith(FIX_PREFIX)).length + 1;
  await world.run("gh", ["issue", "comment", String(issueNum), "--body",
    FIX_PREFIX + " " + fixCount + "**（工作流·实施段·修复 · " + ts + "）\n分支 " + branch + " · PR #" + prNumber + "\n本轮处置：\n" +
    implNote.summary.map((s) => "- " + s).join("\n") + "\n\n本地门禁（dotnet build/test + pnpm lint/test/build）全绿。等待评审段复审。"]);
  await artifact.markdown("impl-report", `# 修复轮 ${fixCount}（Issue #${issueNum}）\n\n${implNote.summary.map((s) => "- " + s).join("\n")}`, { title: "修复轮 " + fixCount + " 汇报（Issue #" + issueNum + "）", primary: true });
  return {
    conclusion: `Issue #${issueNum} 修复轮 ${fixCount} 完成：${implNote.summary.length} 条处置推送至 PR #${prNumber}，本地门禁全绿。下一步：重跑评审段 lf-review 复审。`,
    findings: [], verified: ["dotnet build + test（排除 Perf/Soak）", "pnpm lint / test / build", "阻断清单逐条由实现岗处置"], notCovered: ["CI 三检查（lf-close 段等待）"],
  };
}

phase("提 PR 并落评论");
const change = await implementer.ask<ChangeReport>(
  `生成 PR 标题与正文：标题 Conventional Commits（中文说明）；正文按 worktree 内 .github/PULL_REQUEST_TEMPLATE.md 结构如实填写（若该文件不存在则用「问题与变化 / 验证 / 合并与后续 / 自查」四节）；` +
  `关联 #${issueNum} 用普通引用，严禁 Closes/Fixes 等关闭关键词。只返回结构化结果；不要 push、不要创建 PR（由脚本执行）。`);
const push = await world.run("git", ["-C", wt, "push", "-u", "origin", branch]);
if (push.exitCode !== 0 && !/Everything up-to-date|already exists/i.test(push.stderr)) throw new Error("分支推送失败：" + push.stderr.slice(0, 300));
const prCreate = await world.run("gh", ["pr", "create", "--base", "master", "--head", branch, "--title", change.prTitle, "--body", change.prBody]);
let prUrl = prCreate.stdout.trim();
if (prCreate.exitCode !== 0) {
  // 幂等：run 重放 / AmendWorkflow 续跑时 PR 可能已存在（#301 实证）——回查该分支既有 PR 继续，其余失败照旧抛出
  if (!/already exists/i.test(prCreate.stderr)) throw new Error("PR 创建失败：" + prCreate.stderr.slice(0, 300));
  const existing = await world.run("gh", ["pr", "list", "--head", branch, "--state", "open", "--json", "url", "--jq", ".[0].url"]);
  if (existing.exitCode === 0) prUrl = existing.stdout.trim();
}
const parsed = /\/pull\/(\d+)/.exec(prUrl);
if (parsed === null || parsed[1] === undefined) throw new Error("无法解析 PR 号：" + prUrl);
const newPr = parsed[1];

const assertProblems: string[] = [];
if (!CC_RE.test(change.prTitle)) assertProblems.push("PR 标题不符合 Conventional Commits");
if (CLOSE_RE.test(change.prBody)) assertProblems.push("PR 正文含 Closes/Fixes 关闭关键词");
const prFilesRes = await world.run("gh", ["pr", "view", newPr, "--json", "files"]);
const prPaths = (JSON.parse(prFilesRes.stdout) as { files: { path: string }[] }).files.map((f) => f.path);
if (!prPaths.includes("CHANGELOG.md")) assertProblems.push("代码改动未更新 CHANGELOG.md");
// 改动面并集断言（fail-closed 显式信号）：前端 / 后端 / 治理 / 产品面（packaging 分发 + 用户文档）迭代
// 分别落在 web/ / src/+test/ / .zcode/ / packaging+docs+scripts+README 前缀（#301 实证：产品面迭代会全落后者）。
if (!prPaths.some((p) => p.startsWith("web/") || p.startsWith("src/") || p.startsWith("test/") || p.startsWith(".zcode/") || p.startsWith("packaging/") || p.startsWith("docs/") || p.startsWith("scripts/") || p === "README.md")) assertProblems.push("改动未落在 web/ / src/ / test/ / .zcode/ / packaging/ / docs/ / scripts/ 任一前缀（意外改动面，请人工核查）");
if (assertProblems.length > 0) throw new Error("PR 元数据断言未过：" + assertProblems.join("；"));

await world.run("gh", ["issue", "edit", String(issueNum), "--add-label", "进行中"]);
const prCommentTs = await tsIso();
await world.run("gh", ["issue", "comment", String(issueNum), "--body",
  "**🔧 PR 已建**（工作流·实施段 · " + prCommentTs + "）\n分支 " + branch + " · PR #" + newPr + " · " + prUrl + "\n变更摘要：\n" +
  implNote.summary.map((s) => "- " + s).join("\n") +
  "\n\n本地门禁（dotnet build/test + pnpm lint/test/build）全绿；PR 元数据断言通过。等待评审段 lf-review。"]);
report({ stage: "PR 已建", issue: issueNum, pr: Number(newPr), title: change.prTitle });
await artifact.markdown("impl-report",
  `# 实施完成（Issue #${issueNum}）\n\n分支 ${branch} · PR #${newPr}（${prUrl}）\n\n## 变更摘要\n${implNote.summary.map((s) => "- " + s).join("\n")}\n\n## 已过门禁\n- dotnet build + dotnet test（排除 Perf/Soak）\n- pnpm lint / test / build\n- PR 元数据断言（Conventional Commits / 无关闭关键词 / CHANGELOG 在场）`,
  { title: "实施汇报（Issue #" + issueNum + "）", description: "低档位实施 + 确定性门禁，PR 已建待评审。", primary: true });
return {
  conclusion: `Issue #${issueNum} 实施完成：PR #${newPr} 已建（${change.prTitle}），本地门禁全绿、元数据断言通过。下一步：起评审段 lf-review。`,
  findings: [], verified: ["dotnet build + test（排除 Perf/Soak）", "pnpm lint / test / build", "PR 元数据确定性断言"], notCovered: ["代码评审（lf-review 段职责）", "CI 三检查（lf-close 段等待）"],
};

async function tsIso(): Promise<string> {
  const r = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
  return r.stdout.trim();
}