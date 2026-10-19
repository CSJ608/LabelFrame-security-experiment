#!/usr/bin/env python3
"""注释卫生与设计文档同步核查（决策 #187 / #189）。

用法：
  python scripts/check-comment-hygiene.py <base> [--full]

  base    对比基线（ref 或提交号，如 v1.0.0、HEAD~10）
  --full  附加启发式 B：逐提交检查「结构变化（增删源文件）而未同步 docs/design/」

检查项：
  A. 过程标记扫描：base..HEAD 新增注释行中的迭代号 / 决策号 / Issue / PR / AC 编号 /
     返修轮次 / 评审拍板决议等（#187 禁令；字符串域按既定口径不扫）。
  B. 结构变化-文档同步启发式（--full）：新增或删除 .cs / .ts / .tsx 源文件的提交，
     若该提交未同时修改 docs/design/，列出供人工确认是否属 #189 ④ 的「实质变化」。

退出码：0 = 干净；1 = 有命中（A 命中为确定违规；B 命中为提示级，需人工判定）。
"""
import re
import subprocess
import sys

MARKER_PATTERNS = [
    (r"迭代\s?\d+", "迭代号"),
    (r"决策\s?#?\d+", "决策号"),
    (r"\bAC-\d", "AC 编号"),
    (r"PR\s*#\d+", "PR 引用"),
    (r"返修|返工|salvage", "返修轮次"),
    (r"方案\s*[vV]\d", "方案轮次"),
    (r"(?:^|[^\w#])#\d{2,}(?!\w)", "裸 Issue 号"),
    (r"评审\s*[A-Z]-?\d|拍板|决议\s*\d", "评审拍板决议"),
]
CODE_EXTS = (".cs", ".ts", ".tsx", ".js", ".jsx")


def run_git(*args):
    return subprocess.run(
        ["git", *args], capture_output=True, text=True, encoding="utf-8", errors="replace"
    ).stdout


def looks_like_comment(body: str) -> bool:
    return body.startswith(("//", "///", "/*", "*", "<!--", "#")) or "//" in body


def scan_markers(base: str):
    diff = run_git("diff", f"{base}..HEAD", "--", "src/", "test/", "web/src/")
    cur, hits = None, []
    for line in diff.splitlines():
        if line.startswith("+++ b/"):
            cur = line[6:]
            continue
        if not line.startswith("+") or line.startswith("+++"):
            continue
        body = line[1:].strip()
        if not body or not looks_like_comment(body):
            continue
        for pat, name in MARKER_PATTERNS:
            if re.search(pat, body):
                hits.append(f"[{name}] {cur}: {body[:110]}")
                break
    return hits


def scan_doc_sync(base: str):
    """启发式：增删源文件的提交未同步 docs/design/。"""
    commits = [
        c for c in run_git("log", "--format=%H", f"{base}..HEAD", "--", "src/", "web/src/").splitlines() if c
    ]
    warnings = []
    for c in commits:
        files = run_git("show", "--name-status", "--format=", c).splitlines()
        added_or_deleted = [
            f for f in files
            if f[:1] in ("A", "D") and any(f.endswith(ext) for ext in CODE_EXTS)
        ]
        if not added_or_deleted:
            continue
        if not any("/docs/design/" in f or f.startswith("docs/design/") for f in files):
            subject = run_git("log", "-1", "--format=%h %s", c).strip()
            warnings.append(f"{subject}：增删源文件 {len(added_or_deleted)} 个（如 {added_or_deleted[0][2:].strip()}）但未动 docs/design/ —— 需人工确认是否属实质变化")
    return warnings


def main():
    args = sys.argv[1:]
    full = "--full" in args
    args = [a for a in args if a != "--full"]
    if not args:
        print(__doc__)
        sys.exit(2)
    base = args[0]
    if run_git("rev-parse", "--verify", base).strip() == "":
        print(f"基线 {base} 不存在")
        sys.exit(2)

    marker_hits = scan_markers(base)
    print(f"A. 过程标记扫描（{base}..HEAD 新增注释行）：命中 {len(marker_hits)}")
    for h in marker_hits:
        print("  " + h)

    sync_warnings = scan_doc_sync(base) if full else []
    if full:
        print(f"\nB. 结构变化-文档同步启发式：提示 {len(sync_warnings)}")
        for w in sync_warnings:
            print("  " + w)

    if marker_hits:
        print("\n结论：A 项为确定违规（#187），需剥离过程标记、保留约束文本。")
        sys.exit(1)
    if sync_warnings:
        print("\n结论：A 项干净；B 项提示需人工判定（#189 ④：实质变化须同步 docs/design/）。")
        sys.exit(1)
    print("\n结论：干净。")
    sys.exit(0)


if __name__ == "__main__":
    main()
