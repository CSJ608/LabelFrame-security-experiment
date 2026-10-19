namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 传输插件参数规格：前端按此动态渲染参数表单（文本 / 数字 / 开关 / 下拉），
/// 后端据此做必填与类型校验。
/// 声明 <see cref="VisibleWhenKey"/> 的参数仅在其条件键取值命中 <see cref="VisibleWhenValues"/>
/// 时可见（字段集按连接类型切换）；缺省 null = 恒可见。
/// </summary>
public sealed record TransportParameterSpec(
    string Key,
    string Label,
    TransportParameterType Type,
    bool Required = false,
    string? DefaultValue = null,
    IReadOnlyList<TransportParameterOption>? Options = null,
    string? Hint = null,
    string? VisibleWhenKey = null,
    IReadOnlyList<string>? VisibleWhenValues = null)
{
    /// <summary>参数标签的展示端翻译键（键空间约定见 DESIGN §6.8「自描述文案多语言」；缺省 null = 展示端直接用 <see cref="Label"/> 原文）。</summary>
    public string? LabelKey { get; init; }

    /// <summary>输入提示的展示端翻译键（同 <see cref="LabelKey"/> 命名约定加 .hint 后缀；缺省 null = 用 <see cref="Hint"/> 原文）。</summary>
    public string? HintKey { get; init; }

    /// <summary>
    /// 同行分组键（展示端布局提示）：同值参数渲染于同一行（行内按声明序），行序 = 首个成员的声明序——
    /// 纯分组等价键而非排序键；缺省 null = 各自成行（历史行为，未声明的插件表单零变化）。
    /// init 属性而非构造参数：构造器签名零变化，旧插件二进制兼容（同 LabelKey / HintKey 模式）。
    /// </summary>
    public int? Row { get; init; }
    /// <summary>
    /// 历史构造器（VisibleWhen 字段引入前的 7 参形态）：外部插件 DLL 按旧签名编译，
    /// 宿主升级而插件未随发（服务端分发窗口期）时仍须可加载——可选参数是编译期语法糖，
    /// 不显式保留此构造器，旧二进制将因 MissingMethodException 在装配期崩溃。不可删除。
    /// </summary>
    public TransportParameterSpec(
        string Key,
        string Label,
        TransportParameterType Type,
        bool Required,
        string? DefaultValue,
        IReadOnlyList<TransportParameterOption>? Options,
        string? Hint)
        : this(Key, Label, Type, Required, DefaultValue, Options, Hint, null, null)
    {
    }

    /// <summary>
    /// 当前连接参数下该字段是否可见：无 VisibleWhenKey 声明恒可见；
    /// 有声明时条件键取值（忽略大小写）命中集合即可见，键缺失 / 空值视为不可见
    /// （必填校验随之跳过，由条件键自身的必填校验兜底）。
    /// </summary>
    public bool IsVisible(IReadOnlyDictionary<string, string> parameters)
    {
        if (VisibleWhenKey is null)
        {
            return true;
        }

        if (!parameters.TryGetValue(VisibleWhenKey, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        return VisibleWhenValues is not null && VisibleWhenValues.Any(v =>
            string.Equals(v, rawValue.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
