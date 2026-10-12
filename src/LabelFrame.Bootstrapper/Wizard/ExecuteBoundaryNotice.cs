namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>执行边界契约的 UI 明示文案（Issue 问卷只读锚点； 修订为「确认前只读」；/ 随两层问卷精简表述，语义不变）：确认页可见行，测试断言锚点。</summary>
/// <remarks>
/// 原文为「仅预览（dry-run）：绝不下载安装」； 起 Apply 在确认页「安装」后启用——问卷阶段（含确认页展示）
/// 仍保持零下载 / 零安装 / 零系统改动。 起欢迎页提示移除（自动加载后用户不再停留就绪页），仅保留确认页一行明示。
/// </remarks>
public static class ExecuteBoundaryNotice
{
    /// <summary>确认页可见行：明示点击「安装」前不下载、不安装、不改动系统；点击后按明细执行（需要管理员权限）。</summary>
    public const string Banner = "点击「安装」前不会下载、不安装、不改动系统；点击后按明细下载并安装（需要管理员权限）。";
}
