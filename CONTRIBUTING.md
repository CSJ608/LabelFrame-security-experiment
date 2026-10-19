# 贡献指南

感谢你考虑为 LabelFrame 贡献！

## 开发环境

- .NET 10 SDK
- Node.js 24 + pnpm 11
- Windows（WinHost / MSI 打包）；Server 可在 Linux 构建

```bash
dotnet build LabelFrame.slnx        # 构建
dotnet test LabelFrame.slnx --filter "FullyQualifiedName!~Perf&FullyQualifiedName!~Soak"  # 日常测试
cd web && pnpm install && pnpm dev  # 前端开发
```

## 提交规范

- 使用 [Conventional Commits](https://www.conventionalcommits.org/)：`feat:` / `fix:` / `test:` / `docs:` / `chore:`，中文说明为主
- `master` 受门禁保护：变更走**短分支 + PR**，三项必需检查（构建与测试（dotnet + 前端）/ MSI 结构断言 / Android 构建）全绿后 squash 合并（流程见 [docs/WORKFLOW.md](docs/WORKFLOW.md)）
- 每次 push 自动跑 CI（构建 + 测试（dotnet + 前端双模式）+ MSI 结构断言 + Android 构建）
- 发版 = 更新 `docs/ROADMAP.md` 与 `CHANGELOG.md` 后推送 `v*` tag

## 文档

改代码前请先读仓库根目录的 `AGENTS.md`（协作约束与完成定义）和 `docs/DESIGN.md`（架构决策记录）。

关键约定：
- 文档与注释使用中文；代码标识符使用英文
- 涉及跨迭代的公共契约变更（模板包格式、打印 API、作业模型），先讨论并更新文档再改代码
- `dotnet build` 零警告（分析器 latest-recommended + 警告即错误）

## 性能 / 稳定性测试

日常测试不含 Perf/Soak（由 nightly 流水线跑）——本地手动执行：

```powershell
powershell -File scripts\run-perf.ps1 -Mode perf    # 延迟/并发
powershell -File scripts\run-perf.ps1 -Mode soak    # 稳定性
powershell -File scripts\run-perf.ps1 -Mode bench   # 微基准
```

## 报问题

提 [Issue](https://github.com/marci-labs/LabelFrame/issues)，按模板填写（版本号、复现步骤、期望行为）。
