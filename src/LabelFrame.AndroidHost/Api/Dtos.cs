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

/// <summary>自包含模板。Images（键 → base64，可选）为模板图片资源——迭代 132（决策 #184）补入：
/// 路由模式 Server 投递链本就附带图片 base64，此前 PDA DTO 丢弃致嵌入图无法渲染（契约把 PDA 锁死）；
/// PDA 选图 UI 仍不在该迭代范围。</summary>
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

/// <summary>错误响应：问题码 + 中文消息（可选字段键、可选模板参数）。
/// params（决策 #164 ③ / #166，迭代 109 定契约、迭代 110 PDA 副本对齐）：扁平字符串键值对象，
/// 键名与展示端码表模板占位符 <c>{key}</c> 同名，供 PDA / web 按码翻译插值；后端 message 永远为渲染后中文兜底。
/// 缺省 / null 时序列化与旧形态逐字节等价（<see cref="JsonIgnoreAttribute"/> WhenWritingNull）。</summary>
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