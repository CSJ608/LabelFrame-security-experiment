namespace LabelFrame.Server;

/// <summary>Server 领域异常：携带问题码。</summary>
public sealed class ServerException : Exception
{
    /// <summary>创建异常。</summary>
    public ServerException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>创建异常（模板化消息：message 为渲染后中文文案，parameters 随异常透传到 ErrorView.params—— ③ /）。</summary>
    public ServerException(string code, string message, IReadOnlyDictionary<string, string>? parameters)
        : base(message)
    {
        Code = code;
        Parameters = parameters;
    }

    /// <summary>问题码（见 <see cref="ServerErrorCodes"/>）。</summary>
    public string Code { get; }

    /// <summary>消息模板参数（扁平字符串键值对象，键名与模板占位符一致；无参消息为 null）。</summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; }
}
