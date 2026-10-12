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
