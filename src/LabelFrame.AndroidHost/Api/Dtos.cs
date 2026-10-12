using LabelFrame.Core.Contracts;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelFrame.AndroidHost.Api;

/// <summary>提交作业请求（与 WinHost / Server 同构，模板自包含）。</summary>
public sealed record SubmitJobRequest(
    string? RequestId,
    TemplateDto? Template,
    IReadOnlyList<LabelDto>? Labels);

/// <summary>自包含模板；Images（键 → base64，可选）为模板图片资源，渲染前经 ImagePayloadDecoder 解码传入。</summary>
public sealed record TemplateDto(
    LabelContract? Contract,
    LabelLayout? Layout,
    IReadOnlyDictionary<string, string>? Images = null);

/// <summary>单张标签数据。</summary>
public sealed record LabelDto(IReadOnlyDictionary<string, string>? Data);

/// <summary>作业视图。</summary>
public sealed record JobView(
    string JobId,
    string RequestId,
    string Status,
    int TotalItems,
    int CompletedItems,
    IReadOnlyList<JobItemView> Items);

/// <summary>单张标签视图。</summary>
public sealed record JobItemView(int Index, string Status, string? ErrorCode, string? ErrorMessage);

/// <summary>错误响应：问题码 + 中文兜底消息（可选字段键、可选模板参数）。
/// params 键名与展示端码表模板占位符 {key} 同名（按码翻译插值用）；
/// 缺省 / null 时序列化须与旧形态逐字节等价（WhenWritingNull）。</summary>
public sealed record ErrorView(
    string Code,
    string Message,
    string? FieldKey = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Params = null);

/// <summary>提交结果。</summary>
public sealed record SubmitResult(LabelJob? Job, bool Created, string? ErrorCode, string? ErrorMessage, string? FieldKey)
{
    public static SubmitResult Success(LabelJob job, bool created) => new(job, created, null, null, null);

    public static SubmitResult Failure(string code, string message, string? fieldKey = null)
        => new(null, false, code, message, fieldKey);
}

/// <summary>JSON 选项（版式元素转换器 + 枚举字符串）。</summary>
public static class HostJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new LabelElementJsonConverter(),
        },
    };
}

/// <summary>作业视图映射。</summary>
public static class JobViews
{
    public static JobView From(LabelJob job) => new(
        job.Id,
        job.RequestId,
        job.Status.ToString(),
        job.Items.Count,
        job.Items.Count(i => i.Status == LabelJobItemStatus.Completed),
        job.Items.Select(i => new JobItemView(i.Index, i.Status.ToString(), i.ErrorCode, i.ErrorMessage)).ToList());
}