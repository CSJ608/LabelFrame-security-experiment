using System.Text.RegularExpressions;

namespace LabelFrame.Core.Errors;

/// <summary>
/// 错误消息模板插值：占位符 <c>{key}</c>，参数为扁平字符串键值对象（随 ErrorView 可选 params 透出，展示端按码表翻译）。
/// 规则：单遍替换（参数值内嵌 <c>{x}</c> 不级联替换）；参数缺失时占位符原样保留——消息兜底不抛。
/// </summary>
public static partial class ErrorMessageTemplates
{
    /// <summary>渲染模板：按参数替换 <c>{key}</c> 占位符（缺 key 保留原文；parameters null / 空直接返回模板）。</summary>
    public static string Format(string template, IReadOnlyDictionary<string, string>? parameters)
    {
        if (string.IsNullOrEmpty(template) || parameters is null || parameters.Count == 0)
        {
            return template;
        }

        return PlaceholderRegex().Replace(
            template,
            match => parameters.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    /// <summary>占位符形态：<c>{key}</c>，key 限字母 / 数字 / 下划线（与 web 端 i18next <c>{{key}}</c> 键名约束一致）。</summary>
    [GeneratedRegex("""\{([A-Za-z0-9_]+)\}""")]
    private static partial Regex PlaceholderRegex();
}
