namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>执行边界契约的 UI 明示文案：确认页可见行「确认前只读」，测试断言锚点（语义不可弱化）。</summary>
public static class ExecuteBoundaryNotice
{
    /// <summary>确认页可见行：明示点击「安装」前不下载、不安装、不改动系统；点击后按明细执行（需要管理员权限）。</summary>
    public const string Banner = "点击「安装」前不会下载、不安装、不改动系统；点击后按明细下载并安装（需要管理员权限）。";
}
