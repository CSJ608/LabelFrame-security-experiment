namespace LabelFrame.Core.Jobs;

/// <summary>作业领域异常：携带问题码，供 API 层映射 HTTP 状态。</summary>
public sealed class LabelJobException : Exception
{
    /// <summary>创建异常。</summary>
    public LabelJobException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>创建异常（模板化消息：message 为渲染后中文文案，parameters 随异常透传到 ErrorView.params—— ③ /）。</summary>
    public LabelJobException(string code, string message, IReadOnlyDictionary<string, string>? parameters)
        : base(message)
    {
        Code = code;
        Parameters = parameters;
    }

    /// <summary>问题码（见 <see cref="JobErrorCodes"/>）。</summary>
    public string Code { get; }

    /// <summary>消息模板参数（扁平字符串键值对象，键名与模板占位符一致；无参消息为 null）。</summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; }
}
