namespace LabelFrame.Bootstrapper.Ba.Ui;

/// <summary>向导分页接口：进入时刷新内容；离开前校验是否可继续；不适用的问卷页自动跳过（两层问卷）。</summary>
internal interface IWizardPage
{
    /// <summary>进入该页时刷新（如确认页按最新问卷答案重建组件清单）。</summary>
    void OnEnter();

    /// <summary>是否允许进入下一页（false 时 <paramref name="reason"/> 给出中文提示）。</summary>
    bool CanProceed(out string? reason);

    /// <summary>当前问卷状态下本页是否不适用（向导壳在前进 / 后退导航时自动越过；如基础模式的角色页、服务端角色的打印机页）。</summary>
    bool ShouldSkip { get; }
}
