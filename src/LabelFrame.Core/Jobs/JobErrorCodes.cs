namespace LabelFrame.Core.Jobs;

/// <summary>作业 / API / IO / 编码问题码常量。约定：LF_JOB_xxx / LF_IO_xxx / LF_API_xxx / LF_ENC_xxx。</summary>
public static class JobErrorCodes
{
    /// <summary>作业不存在。</summary>
    public const string JobNotFound = "LF_JOB_001";

    /// <summary>当前状态不允许该操作。</summary>
    public const string InvalidTransition = "LF_JOB_002";

    /// <summary>作业没有该序号的标签（条目级重打越界；从 LF_JOB_002 拆出以区分语义）。</summary>
    public const string ItemNotFound = "LF_JOB_003";

    /// <summary>该序号标签状态不允许重打（仅 Failed 可重打；从 LF_JOB_002 拆出以区分语义）。</summary>
    public const string ItemNotRetriable = "LF_JOB_004";

    /// <summary>发送到打印机失败。</summary>
    public const string TransportSendFailed = "LF_IO_001";

    /// <summary>请求格式错误（JSON / 必填字段缺失）。</summary>
    public const string InvalidRequest = "LF_API_001";

    /// <summary>ZPL 编码失败（模板不支持的元素等）。</summary>
    public const string EncodeFailed = "LF_ENC_001";

    /// <summary>指令编译失败：插件编译器无法原生表示元素 / 字符等（DESIGN ，显式失败不回退图片）。</summary>
    public const string CommandCompileFailed = "LF_ENC_002";

    /// <summary>打印方式配置异常：printMode=native 但连接的插件无文档编译能力（存量异常配置提交兜底）。</summary>
    public const string PrintModeNotSupported = "LF_ENC_003";
}