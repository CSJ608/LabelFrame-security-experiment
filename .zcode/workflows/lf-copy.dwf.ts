/* zcode-workflow
description: LabelFrame 工作流管线·文案草拟段（清单驱动）：按最新方案「界面文案清单」json 契约（槽位×zh/en 上限由方案声明）撰写迭代全部界面文案草稿（文案表＋候选组），落 Issue 评论「✍️ 文案草稿 vN」（与最新方案比新旧、版本递增——同 Issue 方案返工不阻断）。低档位运行（GLM-5.3-Flash）。
whenToUse: 工作流实验管线第 2 段（方案「界面文案清单」节含 json 契约的迭代使用）：最新「📐 方案 vN」评论在场后起跑，产出文案草稿供评审段校对。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
*/
// LabelFrame 工作流实验 · 文案草拟段（lf-copy）· v3（#298 泛化：从导览/帮助专用改「界面文案清单」契约驱动——任务面、槽位集、字数上限、候选组数全部来自方案，不再写死任务清单 A–H 与 15~45 行等硬编码）
// 职责：按最新「📐 方案 vN」的「界面文案清单」json 契约撰写迭代全部界面文案草稿（zh+en），落 Issue 评论「✍️ 文案草稿 vN」（与最新方案比新旧、版本递增——同 Issue 方案返工不阻断）。
// 前置：最新方案评论在场且其清单节含 json 契约块（fail-closed）；幂等：草稿评论已存在则直接退出。

interface CopySlotSpec {
  /** 槽位名（英文 camelCase：title / body / btn / label / placeholder …），zh 与 en 成对产出。 */
  name: string;
  /** 中文上限（字符数；0 = 不限）。 */
  capZh: number;
  /** 英文上限（字符数；0 = 不限）。 */
  capEn: number;
}

interface CopyItemSpec {
  /** i18n 语义 key 建议（如 help.article.designer.intro），供实施段落资源。 */
  key: string;
  /** 所属页面或组件位置（如 Help / Designer / Settings）。 */
  page: string;
  /** 文案场景：出现在哪、何时出现、干什么用（措辞由本段撰写，场景由方案定）。 */
  scenario: string;
  /** 文案槽位声明（至少一个；上限 0 = 不限）。 */
  slots: CopySlotSpec[];
}

interface CopyCandidateSpec {
  /** 候选组名（英文 camelCase，如 demoEntry）。 */
  name: string;
  /** 这组候选在选什么（一句话）。 */
  desc: string;
  /** 每组的槽位声明。 */
  slots: CopySlotSpec[];
  /** 组数（第 1 组为推荐组）。 */
  count: number;
}

interface CopyContract {
  items: CopyItemSpec[];
  candidates: CopyCandidateSpec[];
}

interface CopySlotText {
  zh: string;
  en: string;
}

interface CopyRow {
  /** 与清单 item.key 一一对应，不得增删。 */
  key: string;
  page: string;
  /** 槽位名 → zh/en 文案（与该条声明的槽位集恰好一致）。 */
  slots: Record<string, CopySlotText>;
}

interface CandidateGroupText {
  /** 各槽位 zh/en 文案。 */
  values: Record<string, CopySlotText>;
  /** 一句话说明该组的语感与适用性。 */
  note: string;
}

interface CandidateSetText {
  name: string;
  groups: CandidateGroupText[];
}

interface CopyDraft {
  rows: CopyRow[];
  /** 候选组（与契约 candidates 一一对应；无声明给空数组）。 */
  candidateSets: CandidateSetText[];
  /** 从现有资源观察到的风格要点（语气/称谓/标点/动词习惯），每条一句。 */
  styleNotes: string[];
}

/**
 * 形状防御（#329 管线修复登记）：文案员返回值三字段（rows / candidateSets / styleNotes）齐全才采信，
 * 缺省回退旧值 prev（prev 为 null 时返回 null）——修订轮模型偶发只回传局部字段（如仅 rows 缺
 * candidateSets / styleNotes），未防御时驱动层 `draft.styleNotes.map` / `checkContract(rows.map)`
 * 直接崩（dwfrun-c185e182 崩溃实证）。初稿与修订轮统一过防御；调用方以引用相等（coerceDraft
 * 返回 === prev）判别「形状不合格已回退」，修订轮据此按完整 schema 重问一次。
 */
function coerceDraft(v: unknown, prev: CopyDraft | null): CopyDraft | null {
  if (v === null || typeof v !== "object") return prev;
  const o = v as Partial<CopyDraft>;
  if (!Array.isArray(o.rows) || !Array.isArray(o.candidateSets) || !Array.isArray(o.styleNotes)) return prev;
  if (o.styleNotes.some((s) => typeof s !== "string")) return prev;
  return { rows: o.rows, candidateSets: o.candidateSets, styleNotes: o.styleNotes };
}

interface Comment { body: string }
interface IssueJson { number: number; state: string; labels: { name: string }[]; comments?: Comment[] }

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

function asSlotSpecs(v: unknown, owner: string): CopySlotSpec[] {
  if (!Array.isArray(v) || v.length === 0) throw new Error(owner + " 未声明 slots（fail-closed）");
  return v.map((s, i) => {
    const o = s as Partial<CopySlotSpec>;
    if (typeof o.name !== "string" || o.name === "") throw new Error(owner + " 第 " + (i + 1) + " 个槽位缺 name（fail-closed）");
    if (typeof o.capZh !== "number" || !Number.isFinite(o.capZh) || o.capZh < 0) throw new Error(owner + " 槽位 " + o.name + " 缺 capZh 数值上限（0=不限；fail-closed）");
    if (typeof o.capEn !== "number" || !Number.isFinite(o.capEn) || o.capEn < 0) throw new Error(owner + " 槽位 " + o.name + " 缺 capEn 数值上限（0=不限；fail-closed）");
    return { name: o.name, capZh: o.capZh, capEn: o.capEn };
  });
}

/** 从方案评论解析「界面文案清单」json 契约（fail-closed：节缺失 / json 缺失 / items 为空即中止）。 */
function parseCopyContract(planBody: string): CopyContract {
  const head = planBody.indexOf("## 界面文案清单");
  if (head < 0) throw new Error("方案评论不含「## 界面文案清单」节——含界面文案迭代的方案必须声明该契约节（fail-closed）");
  const next = planBody.indexOf("\n## ", head);
  const section = next < 0 ? planBody.slice(head) : planBody.slice(head, next);
  const m = /```json\r?\n([\s\S]*?)```/.exec(section);
  if (m === null || m[1] === undefined) throw new Error("「界面文案清单」节缺少 json 契约块（fail-closed）——设计段 lf-design 产出异常");
  const raw = JSON.parse(m[1]) as { items?: unknown; candidates?: unknown };
  if (!Array.isArray(raw.items) || raw.items.length === 0) throw new Error("界面文案清单 items 为空（fail-closed）");
  const items = raw.items.map((it, i) => {
    const o = it as Partial<CopyItemSpec>;
    if (typeof o.key !== "string" || o.key === "") throw new Error("清单第 " + (i + 1) + " 条缺 key（fail-closed）");
    return {
      key: o.key,
      page: typeof o.page === "string" ? o.page : "",
      scenario: typeof o.scenario === "string" ? o.scenario : "",
      slots: asSlotSpecs(o.slots, "清单 " + o.key),
    };
  });
  const candidates = Array.isArray(raw.candidates)
    ? raw.candidates.map((c) => {
        const o = c as Partial<CopyCandidateSpec>;
        if (typeof o.name !== "string" || o.name === "") throw new Error("候选组缺 name（fail-closed）");
        if (typeof o.count !== "number" || !Number.isFinite(o.count) || o.count < 2) throw new Error("候选组 " + o.name + " count 须为 ≥2 的数字（fail-closed）");
        return { name: o.name, desc: typeof o.desc === "string" ? o.desc : "", slots: asSlotSpecs(o.slots, "候选组 " + o.name), count: Math.floor(o.count) };
      })
    : [];
  return { items, candidates };
}

interface CopyViolation { index: number; problem: string }

/** 定量断言（清单驱动）：行集与清单恰好一致、槽位声明齐全且逐槽非空与上限、候选组数按声明（命令能判定的不花模型）。 */
function checkContract(rows: CopyRow[], sets: CandidateSetText[], contract: CopyContract): CopyViolation[] {
  const out: CopyViolation[] = [];
  const rowKeys = rows.map((r) => r.key);
  contract.items.forEach((spec) => {
    if (!rowKeys.includes(spec.key)) out.push({ index: -1, problem: "清单项缺行：" + spec.key });
  });
  const seen = new Set<string>();
  rows.forEach((r, i) => {
    if (seen.has(r.key)) out.push({ index: i, problem: "key 重复：" + r.key });
    seen.add(r.key);
    const spec = contract.items.find((s) => s.key === r.key);
    if (spec === undefined) {
      out.push({ index: i, problem: "行不在清单内：" + r.key });
      return;
    }
    const names = spec.slots.map((s) => s.name);
    Object.keys(r.slots).forEach((n) => {
      if (!names.includes(n)) out.push({ index: i, problem: "未声明槽位：" + r.key + "/" + n });
    });
    spec.slots.forEach((sl) => {
      const t = r.slots[sl.name];
      if (t === undefined || typeof t.zh !== "string" || typeof t.en !== "string") {
        out.push({ index: i, problem: "槽位缺失：" + r.key + "/" + sl.name });
        return;
      }
      if (t.zh === "" || t.en === "") out.push({ index: i, problem: "槽位空值：" + r.key + "/" + sl.name });
      if (sl.capZh > 0 && t.zh.length > sl.capZh) out.push({ index: i, problem: "zh 超上限：" + r.key + "/" + sl.name + "（" + t.zh.length + ">" + sl.capZh + "）" });
      if (sl.capEn > 0 && t.en.length > sl.capEn) out.push({ index: i, problem: "en 超上限：" + r.key + "/" + sl.name + "（" + t.en.length + ">" + sl.capEn + "）" });
    });
  });
  contract.candidates.forEach((spec) => {
    const set = sets.find((s) => s.name === spec.name);
    if (set === undefined) {
      out.push({ index: -1, problem: "候选组缺失：" + spec.name });
      return;
    }
    if (set.groups.length !== spec.count) out.push({ index: -1, problem: "候选组 " + spec.name + " 组数须 " + spec.count + "（当前 " + set.groups.length + "）" });
    set.groups.forEach((g, gi) => {
      if (typeof g.note !== "string" || g.note === "") out.push({ index: -1, problem: "候选组 " + spec.name + " 第 " + (gi + 1) + " 组缺说明" });
      spec.slots.forEach((sl) => {
        const t = g.values[sl.name];
        if (t === undefined || typeof t.zh !== "string" || typeof t.en !== "string") {
          out.push({ index: -1, problem: "候选组 " + spec.name + " 第 " + (gi + 1) + " 组槽位缺失：" + sl.name });
          return;
        }
        if (t.zh === "" || t.en === "") out.push({ index: -1, problem: "候选组 " + spec.name + " 第 " + (gi + 1) + " 组槽位空值：" + sl.name });
        if (sl.capZh > 0 && t.zh.length > sl.capZh) out.push({ index: -1, problem: "候选组 " + spec.name + " 第 " + (gi + 1) + " 组 zh 超上限：" + sl.name + "（" + t.zh.length + "）" });
        if (sl.capEn > 0 && t.en.length > sl.capEn) out.push({ index: -1, problem: "候选组 " + spec.name + " 第 " + (gi + 1) + " 组 en 超上限：" + sl.name + "（" + t.en.length + "）" });
      });
    });
  });
  sets.forEach((s) => {
    if (!contract.candidates.some((c) => c.name === s.name)) out.push({ index: -1, problem: "未声明的候选组：" + s.name });
  });
  return out;
}

/** 表格列 = 契约槽位并集（清单与候选组按首次出现序）。 */
function slotColumns(contract: CopyContract): string[] {
  const names: string[] = [];
  contract.items.forEach((it) => it.slots.forEach((s) => { if (!names.includes(s.name)) names.push(s.name); }));
  contract.candidates.forEach((c) => c.slots.forEach((s) => { if (!names.includes(s.name)) names.push(s.name); }));
  return names;
}

function mdEscape(s: string): string {
  return s.replace(/\|/g, "\\|");
}

function rowsTable(rows: CopyRow[], cols: string[]): string[] {
  const head = ["#", "key", "页面"];
  cols.forEach((c) => { head.push(c + " zh"); head.push(c + " en"); });
  const lines = ["|" + head.map((h) => " " + h + " ").join("|") + "|", "|" + head.map(() => "---|").join("")];
  rows.forEach((r, i) => {
    const cells = [String(i + 1), r.key, r.page];
    cols.forEach((c) => {
      const t = r.slots[c];
      cells.push(t === undefined ? "—" : mdEscape(t.zh));
      cells.push(t === undefined ? "—" : mdEscape(t.en));
    });
    lines.push("|" + cells.map((x) => " " + x + " ").join("|") + "|");
  });
  return lines;
}

function candidatesTables(sets: CandidateSetText[], contract: CopyContract): string[] {
  const out: string[] = [];
  contract.candidates.forEach((spec) => {
    const set = sets.find((s) => s.name === spec.name);
    if (set === undefined) return;
    const head = ["组"];
    spec.slots.forEach((s) => { head.push(s.name + " zh"); head.push(s.name + " en"); });
    head.push("说明");
    out.push("### 候选组：" + spec.name + "（" + spec.desc + "；第 1 组为推荐组——随定稿交用户过目拍板）");
    out.push("");
    out.push("|" + head.map((h) => " " + h + " ").join("|") + "|");
    out.push("|" + head.map(() => "---|").join(""));
    set.groups.forEach((g, gi) => {
      const cells = [String(gi + 1)];
      spec.slots.forEach((s) => {
        const t = g.values[s.name];
        cells.push(t === undefined ? "—" : mdEscape(t.zh));
        cells.push(t === undefined ? "—" : mdEscape(t.en));
      });
      cells.push(mdEscape(g.note));
      out.push("|" + cells.map((x) => " " + x + " ").join("|") + "|");
    });
    out.push("");
  });
  return out;
}

/** 长正文评论经真实临时文件走 --body-file：Windows spawn 命令行 32K 上限，长正文直传 --body 会 ENAMETOOLONG（#308 实证）——分块追加写入（每块 12K，写到仓库外）后以文件提交。 */
async function postIssueCommentByFile(num: number, body: string): Promise<void> {
  const tmpPath = "../.lf-comment-" + num + ".md";
  const CHUNK = 12000;
  for (let i = 0; i < body.length; i += CHUNK) {
    const w = await world.run("node", ["-e", "const fs=require('fs');fs.writeFileSync(process.argv[1],process.argv[2],{flag:process.argv[3]});", tmpPath, body.slice(i, i + CHUNK), i === 0 ? "w" : "a"]);
    if (w.exitCode !== 0) throw new Error("评论临时文件写入失败：" + w.stderr.slice(0, 200));
  }
  const post = await world.run("gh", ["issue", "comment", String(num), "--body-file", tmpPath]);
  if (post.exitCode !== 0) throw new Error("Issue 评论失败：" + post.stderr.slice(0, 300));
  await world.run("node", ["-e", "const fs=require('fs');try{fs.unlinkSync(process.argv[1]);}catch{}", tmpPath]);
}

const issueNum = Number(args.issue);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
const PLAN_HEAD_RE = /^\*\*📐 方案 v(\d+)\*\*/;
/** 草稿版本宽匹配（迭代 132 返修实证盲区补丁）：草稿评论须与最新方案比新旧，而非「存在即退出」——
 *  同 Issue 方案返工（vN 递增）时旧草稿在场不阻断，新草稿按 maxV+1 递增落评论。 */
const DRAFT_HEAD_RE = /^\*\*✍️ 文案草稿 v(\d+)\*\*/;
function maxDraftVersion(comments: Comment[]): number {
  let max = 0;
  for (const c of comments) {
    const m = DRAFT_HEAD_RE.exec(((c?.body ?? "").split("\n")[0]) ?? "");
    if (m !== null && m[1] !== undefined) max = Math.max(max, Number(m[1]));
  }
  return max;
}
const MAX_REWRITE = 2;

phase("建场守卫：核对方案在场与草稿缺席");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,url,body,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签`);
const comments = issue.comments ?? [];
// 最新方案 vN 定位（fail-closed）：首行正则取不到（含最新评论为处置报告）即中止。
const planIdx = commentHeadLastIndex(PLAN_HEAD_RE, comments);
if (planIdx < 0) throw new Error(`Issue #${issueNum} 无「📐 方案 vN」评论——先跑设计段 lf-design`);
const planBody = comments[planIdx]?.body ?? "";
const draftIdx = commentHeadLastIndex(DRAFT_HEAD_RE, comments);
if (draftIdx !== -1 && draftIdx > planIdx) {
  log("最新草稿评论晚于最新方案评论，幂等退出");
  return { conclusion: `Issue #${issueNum} 最新「✍️ 文案草稿 vN」晚于最新「📐 方案」评论，本段幂等退出；方案更新后重跑即产新版本草稿。`, findings: [], verified: ["建场守卫：草稿晚于方案"], notCovered: [] };
}
const nextDraftV = maxDraftVersion(comments) + 1;
const DRAFT_PREFIX = "**✍️ 文案草稿 v" + nextDraftV + "**";
const contract = parseCopyContract(planBody);
log("清单契约解析：" + contract.items.length + " 条文案项＋" + contract.candidates.length + " 组候选，开始撰写");

phase("依清单契约与现有文案风格撰写草稿");
const copywriter = agent("文案员", {
  system:
    "你是 LabelFrame 的界面文案撰写者。你的产出是给人看的 UI 文案：简洁、动作导向、不重复、与产品既有文案风格一致。" +
    "只读文件不修改。宁可留白也不要编造产品没有的功能。",
});
// 初稿 prompt 单独成变量（勿改字节）：修订轮形状回退时的「按完整 schema 重问」复用同一全文 + 追加说明
const draftPrompt =
  `任务：为本迭代（Issue #${issueNum}）按「界面文案清单」契约撰写全部界面文案（zh + en）。产出 CopyDraft：rows 文案表 + candidateSets 候选组 + styleNotes 风格勘察笔记。\n\n` +
  `【已定稿方案（Issue 评论原文）】\n${planBody}\n\n` +
  `【文案清单契约——唯一任务面，行集与 items 键集须恰好一致，不得增删】\n${JSON.stringify(contract, null, 2)}\n\n` +
  `逐条产出：key / page 照抄清单；该条声明的每个槽位给 zh 与 en（语义对齐、en 按英文 UI 习惯，忌逐字直译感）。` +
  `scenario 描述场景与用途——措辞以此为准；涉及产品行为的事实（如打印生效边界）先用文件工具实读相关源码与 docs/DESIGN.md 核实，宁可不写也不编造。\n` +
  (contract.candidates.length > 0
    ? `候选组按 candidates 声明逐组产出：恰好 count 组、每组全部槽位 zh/en＋一句话说明 note（语感与适用性），第 1 组为推荐组。\n`
    : ``) +
  `【风格要求】先用你的文件工具读 web/src/i18n/ 下的现有资源（zh 与 en 各至少三个文件），观察并模仿其语气、称谓、标点、按钮动词习惯（观察写进 styleNotes，每条一句）；` +
  `文案必须与既有界面文案是同一个「口音」。正文动作导向（「点这里做 X」而非「本页提供 X 功能」）；相邻行不重复说同一件事。\n\n` +
  `【硬限】每槽位字数以契约 capZh / capEn 为准（0 = 不限）；行集与清单 items 键集恰好一致。`;
let draft = coerceDraft(await copywriter.ask<CopyDraft>(draftPrompt), null);
if (draft === null) {
  // 初稿形状不齐（三字段缺一）：按完整 schema 重问一次（追加说明段避免命中上一问缓存）
  log("初稿返回缺 rows / candidateSets / styleNotes 之一，按完整 schema 重问一次");
  draft = coerceDraft(
    await copywriter.ask<CopyDraft>(
      draftPrompt + `\n\n【重问】上一轮返回缺少 rows / candidateSets / styleNotes 之一，请按完整 CopyDraft schema 重新给出全部三个字段。`,
    ),
    null,
  );
  if (draft === null) throw new Error("文案草稿初稿两问后仍缺 rows / candidateSets / styleNotes 字段（形状防御）");
}

phase("定量自查并退回重写违约文案");
for (let round = 1; round <= MAX_REWRITE; round++) {
  const violations = checkContract(draft.rows, draft.candidateSets, contract);
  if (violations.length === 0) break;
  log("第 " + round + " 轮定量自查发现 " + violations.length + " 处违约，退回重写");
  const revised = coerceDraft(
    await copywriter.ask(`以下清单契约定量断言未通过，请修订并重新给出完整草稿（只改有问题的行/组，其余保持）：\n${JSON.stringify(violations)}`),
    draft,
  );
  if (revised === null || revised === draft) {
    // 引用相等判别（#329 管线修复）：修订轮返回缺字段（形状回退到 prev）——按完整 schema 重问一次
    log("修订轮返回缺 rows / candidateSets / styleNotes 之一，按完整 schema 重问一次");
    const again = coerceDraft(
      await copywriter.ask<CopyDraft>(
        draftPrompt +
          `\n\n【重问】上一轮修订返回缺少 rows / candidateSets / styleNotes 之一；以下定量断言未通过，请按完整 CopyDraft schema 给出全部三个字段：\n${JSON.stringify(violations)}`,
      ),
      draft,
    );
    if (again === null || again === draft) throw new Error("文案草稿修订轮重问后仍缺 rows / candidateSets / styleNotes 字段（形状防御）");
    draft = again;
  } else {
    draft = revised;
  }
  if (round === MAX_REWRITE) {
    const still = checkContract(draft.rows, draft.candidateSets, contract);
    if (still.length > 0) throw new Error("文案草稿 " + MAX_REWRITE + " 轮后仍有清单契约违规：" + JSON.stringify(still));
  }
}
report({ stage: "草稿完成", issue: issueNum, rows: draft.rows.length, candidateSets: draft.candidateSets.length, styleNotes: draft.styleNotes.length });

phase("落稿 Issue 供评审段校对");
const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
const ts = tsRes.stdout.trim();
const cols = slotColumns(contract);
const draftMd = [
  DRAFT_PREFIX + "（工作流·文案段 · " + ts + "）",
  "",
  "## 风格勘察笔记",
  ...draft.styleNotes.map((s) => "- " + s),
  "",
  "## 文案表（" + draft.rows.length + " 行 · 槽位列按清单声明）",
  "",
  ...rowsTable(draft.rows, cols),
  ...(draft.candidateSets.length > 0 ? ["", "## 候选组", "", ...candidatesTables(draft.candidateSets, contract)] : []),
  "",
  "```json",
  JSON.stringify(draft.rows, null, 2),
  "```",
  "",
  "```json",
  JSON.stringify(draft.candidateSets, null, 2),
  "```",
].join("\n");
await postIssueCommentByFile(issueNum, draftMd);
await artifact.markdown("copy-draft", draftMd, { title: "文案草稿 v" + nextDraftV + "（Issue #" + issueNum + "）", description: "按方案「界面文案清单」契约产出的文案草稿（文案表＋候选组），待评审段校对。", primary: true });
log("草稿已落 Issue #" + issueNum);
return {
  conclusion: `Issue #${issueNum} 文案草稿完成：${draft.rows.length} 行 zh+en（按清单 ${contract.items.length} 条）＋候选组 ${draft.candidateSets.length} 组，风格模仿自现有 i18n 资源，清单契约定量断言全部通过。下一步：起文案评审段 lf-copy-review。`,
  findings: [],
  verified: ["清单契约断言：行集与清单恰好一致、槽位齐全、逐槽非空与上限、候选组数按声明", "文案员实读了 web/src/i18n 现有资源（styleNotes 留痕）"],
  notCovered: ["文案未经主编校对与新用户视角评审（评审段职责）", "候选组最终取舍待用户过目（推荐组仅标注）"],
};
