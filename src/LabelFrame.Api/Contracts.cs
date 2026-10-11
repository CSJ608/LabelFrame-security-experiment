using System.Text.Json.Serialization;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.Api;

/// <summary>提交作业请求（模板自包含：契约 + 版式 + 标签数据；路由模式附加目标设备 / 模板名）。</summary>
/// <param name="CallbackUrl">终态回调地址（可选可空，决策 #154）：仅路由模式 Server 消费——作业到达终态（Completed / Failed / Expired）后异步 POST 通知；仅允许 http/https 绝对地址，提交即校验拒绝。直连模式（WinHost / AndroidHost）接受但忽略。</param>
public sealed record SubmitJobRequest(
    string? RequestId,
    TemplateDto? Template,
    IReadOnlyList<LabelDto>? Labels,
    string? TargetDeviceId = null,
    string? TemplateName = null,
    string? TargetIp = null,
    string? CallbackUrl = null);

/// <summary>自包含模板（Contract + Layout 必带；Name / Images 可选）。</summary>
public sealed record TemplateDto(
    LabelContract? Contract,
    LabelLayout? Layout,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Images = null);

/// <summary>单张标签数据。</summary>
public sealed record LabelDto(IReadOnlyDictionary<string, string>? Data);

/// <summary>模板提交 DTO（保存到模板库；图片资源可随请求携带（迭代 132，决策 #184），testData 可选）。</summary>
/// <param name="Images">图片资源（键 → base64）。三态语义（DESIGN §5.1，决策 #184）：
/// 不带字段或显式 null＝保留模板存量图片（record 可选参数下两者反序列化同为 null，契约合并为一态）、
/// 空字典＝清空全部、非空字典＝全量覆盖；服务端解码校验（PNG / JPG / BMP、单图 ≤2MB、≤10 张、总量 ≤10MB，
/// 按解码后原始字节）。缺省 / null 仅为旧调用方安全兜底，新调用方恒发显式字典。</param>
public sealed record TemplatePackageDto(
    string? Name,
    string? Group,
    LabelContract? Contract,
    LabelLayout? Layout,
    IReadOnlyDictionary<string, string>? TestData = null,
    IReadOnlyDictionary<string, string>? Images = null);

/// <summary>预览请求。</summary>
public sealed record PreviewRequest(IReadOnlyDictionary<string, string>? Data);

/// <summary>设备日志回传请求（PDA / 客户端 → 服务端；客户端本地查看用）。</summary>
public sealed record PushLogRequest(string? DeviceId, IReadOnlyList<string>? Lines);

/// <summary>Excel 模板生成请求（columns 顺序即表头，sampleRow 示例行）。</summary>
public sealed record ExcelTemplateRequest(IReadOnlyList<ExcelTemplateColumnDto>? Columns, IReadOnlyDictionary<string, string>? SampleRow);

/// <summary>Excel 模板列。</summary>
public sealed record ExcelTemplateColumnDto(string? Key, string? DisplayName);

/// <summary>
/// 错误响应：问题码 + 中文消息（可选字段键、可选模板参数）。
/// params（决策 #164 ③ / #166，迭代 109）：扁平字符串键值对象，键名与消息模板占位符 <c>{key}</c> 一致，
/// 供展示端按码表翻译插值；后端 message 永远为渲染后的中文兜底文案。缺省 / null 时序列化与旧形态逐字节等价
/// （<see cref="JsonIgnoreAttribute"/> WhenWritingNull——params 不出现在 JSON，旧调用方无感）。
/// </summary>
public sealed record ErrorView(
    string Code,
    string Message,
    string? FieldKey = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Params = null);
