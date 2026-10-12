/* zcode-workflow
description: LabelFrame 工作流管线·文案评审段（清单驱动）：提炼产品文案风格基准，主编按方案「界面文案清单」契约校对草稿（重复/啰嗦/术语/中英对齐，含候选组），再经独立「新用户视角」评审，产出定稿落 Issue 评论「✅ 文案定稿 vN」（消费最新草稿、定稿晚于草稿才幂等——同 Issue 返工不阻断）。高档位运行（GLM-5.3）。
whenToUse: 工作流实验管线第 3 段（方案「界面文案清单」节含 json 契约的迭代使用）：「✍️ 文案草稿 vN」评论在场后起跑，产出文案定稿供用户过目与实施段使用；无界面文案迭代由主控手贴「✅ 文案定稿 vN」占位声明跳过本段（四段轻装固定仪式）。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
*/
// LabelFrame 工作流实验 · 文案评审段（lf-copy-review）· v3（#298 泛化：契约与断言改方案「界面文案清单」驱动，不再写死导览形态与 15~45 行/候选恰 3 组）
// 职责：解析方案契约 → 提炼风格基准 → 主编逐条校对草稿（含候选组） → 独立「新用户视角」评审 → 终稿契约断言 → 落「✅ 文案定稿 vN」（消费最新草稿、版本递增）。
// 前置：草稿评论在场；幂等：定稿评论已存在则直接退出。

interface CopySlotSpec {
  /** 槽位名（英文 camelCase），zh 与 en 成对产出。 */
  name: string;
  /** 中文上限（字符数；0 = 不限）。 */
  capZh: number;
  /** 英文上限（字符数；0 = 不限）。 */
  capEn: number;
}

interface CopyItemSpec {
  key: string;
  page: string;
  scenario: string;
  slots: CopySlotSpec[];
}

interface CopyCandidateSpec {
  name: string;
  desc: string;
  slots: CopySlotSpec[];
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
  key: string;
  page: string;
  slots: Record<string, CopySlotText>;
}

interface CandidateGroupText {
  values: Record<string, CopySlotText>;
  note: string;
}

interface CandidateSetText {
  name: string;
  groups: CandidateGroupText[];
}

interface StyleBaseline {
  /** 从现有 i18n 资源提炼的风格规则，每条一句（语气/称谓/标点/按钮动词/句长）。 */
  rules: string[];
}

interface CopyEdit {
  /** 行下标（对应草稿 rows；候选组问题用 index = -1 并在 problem 注明组名）。 */
  index: number;
  /** 问题一句话（重复/啰嗦/术语不一致/中英不对齐/与基准冲突）。 */
  problem: string;
}

interface CopyRevision {
  /** 修订后的完整行数组（未改的行原样保留，key 与行集不变）。 */
  revisedRows: CopyRow[];
  /** 修订后的候选组（与契约声明一致，第 1 组仍为推荐组）。 */
  revisedCandidateSets: CandidateSetText[];
  /** 每处修订的记录（index + problem 一句话）。 */
  edits: CopyEdit[];
}

interface RowsResult {
  /** 完整行数组。 */
  rows: CopyRow[];
  /** 候选组（可仅在候选被修订时变化）。 */
  candidateSets: CandidateSetText[];
}

interface ReaderFeedback {
  /** 第一次用的人看不懂的行：index + 原因。 */
  unclear: string[];
  /** 与其他行重复表达的行：index + 与谁重复。 */
  redundant: string[];
  /** 可整句删掉的废话：index + 句子。 */
  waste: string[];
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

function commentHeadLastIndex(re: RegExp, comments: Comment[]): number {
  for (let i = comments.length - 1; i >= 0; i--) {
    const head = ((comments[i]?.body ?? "").split("\n")[0]) ?? "";
    if (re.test(head)) return i;
  }
  return -1;
}

/** 从草稿/定稿评论里抽机器可读的 ```json 行块（第 1 块 = rows，第 2 块 = 候选组）。 */
function extractJsonBlocks(commentBody: string): string[] {
  const out: string[] = [];
  const re = /```json\r?\n([\s\S]*?)```/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(commentBody)) !== null) if (m[1] !== undefined) out.push(m[1]);
  if (out.length === 0) throw new Error("评论中未找到 json 行块（fail-closed）");
  return out;
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

/** 从方案评论解析「界面文案清单」json 契约（fail-closed）。 */
function parseCopyContract(planBody: string): CopyContract {
  const head = planBody.indexOf("## 界面文案清单");
  if (head < 0) throw new Error("方案评论不含「## 界面文案清单」节——含界面文案迭代的方案必须声明该契约节（fail-closed）");
  const next = planBody.indexOf("\n## ", head);
  const section = next < 0 ? planBody.slice(head) : planBody.slice(head, next);
  const m = /```json\r?\n([\s\S]*?)```/.exec(section);
  if (m === null || m[1] === undefined) throw new Error("「界面文案清单」节缺少 json 契约块（fail-closed）");
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

/** 定量断言（清单驱动）：行集与清单恰好一致、槽位声明齐全且逐槽非空与上限、候选组数按声明。 */
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
    out.push("### 候选组：" + spec.name + "（" + spec.desc + "；第 1 组为推荐组——待用户过目拍板后实施段采用）");
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
/** 版本宽匹配（迭代 132 返修实证盲区补丁）：消费最新版草稿/定稿，幂等条件改为「定稿晚于草稿」——
 *  同 Issue 方案返工产新草稿（vN+1）时旧定稿不阻断，新定稿按 maxV+1 递增。 */
const DRAFT_HEAD_RE = /^\*\*✍️ 文案草稿 v(\d+)\*\*/;
const FINAL_HEAD_RE = /^\*\*✅ 文案定稿 v(\d+)\*\*/;
function maxHeadVersion(re: RegExp, comments: Comment[]): number {
  let max = 0;
  for (const c of comments) {
    const m = re.exec(((c?.body ?? "").split("\n")[0]) ?? "");
    if (m !== null && m[1] !== undefined) max = Math.max(max, Number(m[1]));
  }
  return max;
}
const PLAN_HEAD_RE = /^\*\*📐 方案 v(\d+)\*\*/;

phase("建场守卫：核对草稿在场与定稿缺席");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,url,body,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签`);
const comments = issue.comments ?? [];
const draftIdx = (function () { for (let i = comments.length - 1; i >= 0; i--) { if (DRAFT_HEAD_RE.test(((comments[i]?.body ?? "").split("\n")[0]) ?? "")) return i; } return -1; })();
const finalIdx = (function () { for (let i = comments.length - 1; i >= 0; i--) { if (FINAL_HEAD_RE.test(((comments[i]?.body ?? "").split("\n")[0]) ?? "")) return i; } return -1; })();
if (draftIdx < 0) throw new Error(`Issue #${issueNum} 无「✍️ 文案草稿 vN」评论——先跑文案段 lf-copy`);
if (finalIdx !== -1 && finalIdx > draftIdx) {
  log("最新定稿评论晚于最新草稿评论，幂等退出");
  return { conclusion: `Issue #${issueNum} 最新「✅ 文案定稿 vN」晚于最新「✍️ 文案草稿」，本段幂等退出；新草稿落地后重跑即产新定稿。`, findings: [], verified: ["建场守卫：定稿晚于草稿"], notCovered: [] };
}
const draftBody = comments[draftIdx]?.body ?? "";
const nextFinalV = maxHeadVersion(FINAL_HEAD_RE, comments) + 1;
const FINAL_PREFIX = "**✅ 文案定稿 v" + nextFinalV + "**";
const planIdx = commentHeadLastIndex(PLAN_HEAD_RE, comments);
if (planIdx < 0) throw new Error(`Issue #${issueNum} 无「📐 方案 vN」评论——契约来源缺失（fail-closed）`);
const contract = parseCopyContract(comments[planIdx]?.body ?? "");
const draftBlocks = extractJsonBlocks(draftBody);
const draftRows = JSON.parse(draftBlocks[0] ?? "[]") as CopyRow[];
if (!Array.isArray(draftRows) || draftRows.length === 0) throw new Error("草稿 rows json 块解析结果为空");
const draftSets = draftBlocks[1] !== undefined ? JSON.parse(draftBlocks[1]) as CandidateSetText[] : [];
log("契约 " + contract.items.length + " 条＋草稿 " + draftRows.length + " 行＋候选 " + draftSets.length + " 组已解析，开始提炼风格基准");

phase("提炼本产品文案风格基准");
const editor = agent("主编", {
  system:
    "你是 LabelFrame 的文案主编，负责让界面文案像同一个人写的。你只读文件、只产出结构化结果，不改代码。" +
    "判断必须有依据（基准规则或既有资源例句）；说不清依据的不要写。",
});
const baseline = await editor.ask<StyleBaseline>(
  "提炼「本产品文案风格基准」：用你的文件工具读 web/src/i18n/ 下 zh 与 en 资源各至少三个文件，" +
  "提炼 5~10 条可操作的规则（语气、称谓、标点、按钮动词习惯、句长节奏），每条一句，供校对本轮文案时对照。");

phase("主编按契约逐条校对草稿（含候选组）");
const copyRevision = await editor.ask<CopyRevision>(
  `【风格基准】\n${baseline.rules.map((r, i) => (i + 1) + ". " + r).join("\n")}\n\n` +
  `【文案清单契约（行集/槽位/上限/候选组数以此为准，修订不得破坏）】\n${JSON.stringify(contract, null, 2)}\n\n` +
  `【草稿 rows（Issue #${issueNum}）】\n${JSON.stringify(draftRows, null, 2)}\n\n` +
  `【候选组】\n${JSON.stringify(draftSets, null, 2)}\n\n` +
  `逐条校对并直接产出修订结果：① 相邻或全局重复表达同一意思的，合并或删；② 啰嗦句砍到一句动作导向；` +
  `③ 术语与既有资源不一致的（读资源核对）改为一致；④ 中英文语义不对齐的改 en（候选组 en 措辞重点核：忌直译感、按英文 UI 习惯）；⑤ 与基准冲突的改。` +
  `edits 记录每处修订（index + problem 一句话；候选组问题 index 用 -1 并注明组名），revisedRows / revisedCandidateSets 给修订后的完整结果` +
  `（未改的原样保留，key 与行集不变；候选组保持契约声明的组数且第 1 组为推荐组）。`);
let currentRows = copyRevision.revisedRows;
let currentSets = copyRevision.revisedCandidateSets;

phase("请第一次使用的人读一遍");
const readerFeedback = await agent("新用户视角", {
  system: "你扮演一个第一次使用标签打印软件的仓库员工，中文母语。你只根据拿到的文字判断，不查任何文件、不看代码。",
}).ask<ReaderFeedback>(
  `下面是一款标签打印软件的界面文案表（JSON，每行：key / page / slots——槽位名→{zh,en} 中文与英文文案）。` +
  `以第一次使用的人的身份读一遍，只回答三件事：哪些行看不懂或术语陌生（unclear，注明行内 key）；哪些行和别的行说的是同一件事（redundant，注明与哪个 key 重复）；` +
  `哪些中文正文是删掉也不影响理解的废话（waste，原样引用该句）。没有就给空数组，不要硬凑。\n${JSON.stringify(currentRows)}`);
const readerIssueCount = readerFeedback.unclear.length + readerFeedback.redundant.length + readerFeedback.waste.length;

phase("吸收反馈并出终稿");
let finalRows = currentRows;
let finalSets = currentSets;
if (readerIssueCount > 0) {
  const absorbed = await editor.ask<RowsResult>(
    `新用户视角反馈如下，吸收可取项修订（不合理的可忽略但在结果里附一句理由），重新给出完整行数组与候选组（key 与行集不变，候选保持契约组数）：\n${JSON.stringify(readerFeedback)}`);
  finalRows = absorbed.rows;
  finalSets = absorbed.candidateSets;
}
const violations = checkContract(finalRows, finalSets, contract);
if (violations.length > 0) {
  const fixed = await editor.ask<RowsResult>(
    `终稿清单契约断言未通过，修订后重新给完整行数组与候选组（契约：行集恰好一致、槽位齐全、逐槽上限、候选组数按声明）：\n${JSON.stringify(violations)}`);
  finalRows = fixed.rows;
  finalSets = fixed.candidateSets;
  const still = checkContract(finalRows, finalSets, contract);
  if (still.length > 0) throw new Error("终稿仍有契约违规：" + JSON.stringify(still));
}
report({ stage: "文案定稿", issue: issueNum, rows: finalRows.length, candidateSets: finalSets.length, edits: copyRevision.edits.length, readerIssues: readerIssueCount });

phase("落定稿评论与交付");
const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
const ts = tsRes.stdout.trim();
const cols = slotColumns(contract);
const finalMd = [
  FINAL_PREFIX + "（工作流·文案评审段 · " + ts + "）",
  "",
  "## 风格基准（主编提炼）",
  ...baseline.rules.map((r, i) => "- " + (i + 1) + ". " + r),
  "",
  "## 主编修订记录（" + copyRevision.edits.length + " 处）",
  ...copyRevision.edits.map((e) => "- 第 " + (e.index + 1) + " 行：" + e.problem),
  "",
  "## 新用户视角反馈（" + readerIssueCount + " 条，已吸收）",
  ...readerFeedback.unclear.map((u) => "- 看不懂：" + u),
  ...readerFeedback.redundant.map((r) => "- 重复：" + r),
  ...readerFeedback.waste.map((w) => "- 废话：" + w),
  "",
  "## 定稿文案表（实施段以此为准 · 槽位列按清单声明）",
  "",
  ...rowsTable(finalRows, cols),
  ...(finalSets.length > 0 ? ["", ...candidatesTables(finalSets, contract)] : []),
  "",
  "```json",
  JSON.stringify(finalRows, null, 2),
  "```",
  "",
  "```json",
  JSON.stringify(finalSets, null, 2),
  "```",
].join("\n");
await postIssueCommentByFile(issueNum, finalMd);
await artifact.markdown("copy-final", finalMd, { title: "文案定稿 v" + nextFinalV + "（Issue #" + issueNum + "）", description: "主编校对 + 新用户视角评审后的定稿（文案表＋候选组，契约断言通过），含修订记录。", primary: true });
log("定稿已落 Issue #" + issueNum);
return {
  conclusion: `Issue #${issueNum} 文案定稿完成：${finalRows.length} 行（契约 ${contract.items.length} 条全覆盖）＋候选组 ${finalSets.length} 组，主编修订 ${copyRevision.edits.length} 处，新用户视角反馈 ${readerIssueCount} 条已吸收，清单契约断言通过。下一步：主会话交用户过目（含候选组选择，可改可跳过）后起实施段 lf-implement。`,
  findings: [],
  verified: ["风格基准从现有 i18n 资源实读提炼", "主编与撰写者不同上下文（独立复审）", "新用户视角只拿文案不看代码", "契约断言：行集与清单恰好一致、槽位齐全、逐槽上限、候选组数按声明"],
  notCovered: ["文案最终观感属用户主观判断（B 类），待用户过目或陪验", "候选组最终取舍待用户拍板"],
};
