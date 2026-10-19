/* zcode-workflow
description: LabelFrame 注释卫生核查工作流：机器扫描（过程标记＋文档同步启发式）先行，再按改动模块并行派核查子代理（#189
  尺度＋docs/design 断言核对），汇总分级报告。报告型，不修复。
whenToUse: "阶段性或发版前核查注释合规与设计文档同步（决策 #187/#189/#189④）——多模块并行语义核查提速；日常单模块自查直接跑
  scripts/check-comment-hygiene.py 更快。报告型不修复。"
args:
  base:
    type: string
    description: 核查基线 git ref（上次修复 squash 提交 / 发版 tag / 分支对 master），只查 base..HEAD 新增注释
    required: true
*/
interface ModuleAudit {
  /** 尺度存疑条目（文件:行——问题与处理倾向） */
  scaleIssues: string[];
  /** 文档同步缺口（docs/design/ 哪篇哪节与代码不符或缺失） */
  docGaps: string[];
  /** 一句话结论 */
  note: string;
}

function moduleKey(path: string): string {
  if (path.startsWith("src/LabelFrame.")) {
    return path.slice("src/LabelFrame.".length).split("/")[0];
  }
  if (path.startsWith("test/LabelFrame.")) {
    return path.slice("test/LabelFrame.".length).split("/")[0];
  }
  if (path.startsWith("web/")) {
    return "Web";
  }
  return "";
}

const base = String(args.base ?? "");
if (!base) {
  log("缺少 base 参数（核查基线 ref）");
}

phase("机器扫描：过程标记与文档同步启发式");
const scan = await world.run("python", ["scripts/check-comment-hygiene.py", base, "--full"], { timeoutMs: 120_000 });
const machineFindings = scan.stdout.trim();
log(`机器扫描完成（退出码 ${scan.exitCode}）`);

phase("按模块并行语义核查（#189 尺度＋docs/design 同步）");
const changed = await git.changedFiles(base);
const groups = new Map<string, string[]>();
for (const f of changed) {
  const key = moduleKey(f);
  if (!key) {
    continue;
  }
  if (!groups.has(key)) {
    groups.set(key, []);
  }
  groups.get(key)!.push(f);
}
const docMap: Record<string, string> = {
  Core: "core.md",
  Api: "api.md",
  Server: "server.md",
  WinHost: "winhost.md",
  AndroidHost: "androidhost.md",
  "TransportPlugin.Zebra": "transport-plugin-zebra.md",
  "TransportPlugin.Tspl": "transport-plugin-tspl.md（若不存在则如实报缺独立篇章）",
  Rendering: "rendering.md",
  "Bootstrapper.Ba": "bootstrapper.md",
  "Bootstrapper.PayloadTool": "bootstrapper.md",
  Web: "web-frontend.md",
};
log(`改动模块 ${groups.size} 个，并行核查`);

const audits = await Promise.all(
  [...groups.entries()].map(async ([name, files]) => {
    const dirs = [...new Set(files.map((f) => f.split("/").slice(0, 2).join("/")))];
    return {
      name,
      result: await agent(`语义核查·${name}`, {
        system:
          "你是 LabelFrame 注释规范核查员（只读，不修改任何文件）。判定判据＝决策 #187/#189；" +
          "拿不准的列入 scaleIssues 并给倾向，不擅自下结论；没有问题就返回空数组——不为了交差编问题。",
      }).ask<ModuleAudit>(
        [
          `核查模块 ${name}（改动文件 ${files.length} 个，涉及目录：${dirs.join("、")}）。`,
          `基线 git ref：${base}（对比 ${base}..HEAD）。步骤：`,
          `1. 用 git diff ${base} -- <该模块涉及路径> 查看改动，收集全部新增注释行（///、//、/* */、TSX JSX、csproj XML 全形态）；`,
          "2. 逐条判定 #189 尺度：一句话「对象用途与功能」summary＋白名单四类（不变量 / 平台坑 / 兼容义务 / 防回归警示）之外，是否有机制 / 契约 / 取舍长叙述堆在代码注释里（这类应进 docs/design/）——列入 scaleIssues（含文件:行 与处理倾向：删 / 精简 / 转文档）；",
          `3. 文档同步：该模块若出现结构 / 接口契约 / 关键机制实质变化（新增类型与接口、新机制、行为契约变更），核对 docs/design/ 对应篇（${name} → ${docMap[name] ?? "（无对应篇则如实报缺）"}）是否有相应描述——缺失或过时列入 docGaps（写明篇名 / 节与差异点）；`,
          "4. 过程标记（迭代号 / 决策号 / AC 编号等）机器已另扫，你不必重复报——专注尺度与文档。",
          "结果用中文，按 schema 返回。",
        ].join("\n"),
      ),
    };
  }),
);

phase("汇总核查报告");
const scaleIssues = audits.flatMap((a) => a.result.scaleIssues.map((s) => `[${a.name}] ${s}`));
const docGaps = audits.flatMap((a) => a.result.docGaps.map((s) => `[${a.name}] ${s}`));
const reportLines = [
  `# 注释卫生核查报告（基线 ${base}..HEAD）`,
  "",
  "## 一、机器扫描：过程标记（#187，确定级违规）",
  "",
  "```",
  machineFindings || "（无命中）",
  "```",
  "",
  "## 二、语义核查：#189 尺度存疑（需人工裁决）",
  ...(scaleIssues.length ? scaleIssues.map((s) => `- ${s}`) : ["- 无"]),
  "",
  "## 三、文档同步缺口（#189 ④）",
  ...(docGaps.length ? docGaps.map((s) => `- ${s}`) : ["- 无"]),
  "",
  "## 四、各模块一句话结论",
  ...audits.map((a) => `- ${a.name}：${a.result.note}`),
];
try {
  await artifact.markdown("report", reportLines.join("\n"), {
    title: `注释核查报告（${base}..HEAD）`,
    description: "机器过程标记命中＋各模块语义尺度核查＋docs/design 同步缺口",
    primary: true,
  });
} catch {
  log("报告 artifact 发布失败，内容随 return 交付");
}

interface Finding {
  where: string;
  what: string;
  evidence: string;
  status: "verified" | "unconfirmed";
  severity: "low" | "medium" | "high";
}
interface WorkflowReport {
  conclusion: string;
  findings: Finding[];
  verified: string[];
  notCovered: string[];
}
const markerCount = machineFindings.split("\n").filter((l) => l.trim().startsWith("[")).length;
const report: WorkflowReport = {
  conclusion: `机器扫描命中 ${markerCount} 处过程标记（确定违规）；语义核查 ${scaleIssues.length} 条尺度存疑、${docGaps.length} 条文档同步缺口（覆盖 ${audits.length} 个模块）。完整报告见 artifact。`,
  findings: [
    ...scaleIssues.map<Finding>((s) => ({
      where: s.slice(0, 80),
      what: "注释尺度存疑（#189）",
      evidence: s,
      status: "unconfirmed",
      severity: "low",
    })),
    ...docGaps.map<Finding>((s) => ({
      where: s.slice(0, 80),
      what: "docs/design/ 同步缺口（#189 ④）",
      evidence: s,
      status: "unconfirmed",
      severity: "medium",
    })),
  ],
  verified: [
    "机器扫描 scripts/check-comment-hygiene.py（退出码即判据）",
    "各模块语义核查由独立子代理回读 diff 完成",
  ],
  notCovered: ["字符串域（describe / 断言消息 / Usage 文本）按既定口径不查", "修复不在本工作流范围（报告型）"],
};
return report;