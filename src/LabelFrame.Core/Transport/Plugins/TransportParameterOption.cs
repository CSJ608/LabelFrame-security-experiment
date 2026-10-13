namespace LabelFrame.Core.Transport.Plugins;

/// <summary>Select 类型参数的枚举项。</summary>
public sealed record TransportParameterOption(string Value, string? Label = null)
{
    /// <summary>选项标签的展示端翻译键（键空间约定见 DESIGN §6.8「自描述文案多语言」；缺省 null = 展示端直接用 <see cref="Label"/> 原文）。</summary>
    public string? LabelKey { get; init; }
}
