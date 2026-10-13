using System.Text.Json.Serialization;
using LabelFrame.Core.Jobs;

namespace LabelFrame.WinHost.Api;

// 通用契约（SubmitJobRequest / TemplateDto / LabelDto / ErrorView 等）在 LabelFrame.Api 共享库，
// 本文件只保留宿主专属类型。

/// <summary>作业视图（API 响应；CreatedAt / FailedItems / ErrorMessage / TargetDeviceId 为作业历史列表列）。</summary>
public sealed record JobView(
    string JobId,
    string RequestId,
    string Status,
    int TotalItems,
    int CompletedItems,
    IReadOnlyList<JobItemView> Items,
    string? PrintImageDir = null,
    int? PrintImageCount = null,
    DateTimeOffset? CreatedAt = null,
    int? FailedItems = null,
    string? ErrorMessage = null,
    string? TargetDeviceId = null);

/// <summary>单张标签视图。</summary>
public sealed record JobItemView(int Index, string Status, string? ErrorCode, string? ErrorMessage);

/// <summary>提交结果：成功带作业（含是否新建），失败带问题码。</summary>
public sealed record SubmitJobResult(LabelJob? Job, bool Created, string? ErrorCode, string? ErrorMessage, string? FieldKey)
{
    /// <summary>成功。</summary>
    public static SubmitJobResult Success(LabelJob job, bool created) => new(job, created, null, null, null);

    /// <summary>失败。</summary>
    public static SubmitJobResult Failure(string code, string message, string? fieldKey = null)
        => new(null, false, code, message, fieldKey);
}

/// <summary>作业与视图映射。</summary>
public static class JobViews
{
    /// <summary>把作业映射为视图（可附带 Log 模拟打印图片目录）。</summary>
    public static JobView From(LabelJob job, string? printImageDir = null, int? printImageCount = null) => new(
        job.Id,
        job.RequestId,
        job.Status.ToString(),
        job.Items.Count,
        job.Items.Count(i => i.Status == LabelJobItemStatus.Completed),
        job.Items.Select(i => new JobItemView(i.Index, i.Status.ToString(), i.ErrorCode, i.ErrorMessage)).ToList(),
        printImageDir,
        printImageCount,
        job.CreatedAt,
        job.Items.Count(i => i.Status == LabelJobItemStatus.Failed),
        job.Items.FirstOrDefault(i => i.Status == LabelJobItemStatus.Failed)?.ErrorMessage,
        null);
}

/// <summary>传输插件参数规格（前端动态表单渲染用；visibleWhenKey + visibleWhenValues 为条件可见性声明——字段集按连接类型切换，缺省 null = 恒可见；labelKey / hintKey 为展示端翻译键（决策 #192），缺省不出现在 JSON——无键插件响应与键化前逐字节等价）。</summary>
public sealed record TransportPluginParameterDto(
    string Key,
    string Label,
    string Type,
    bool Required,
    string? DefaultValue,
    IReadOnlyList<TransportParameterOptionDto>? Options,
    string? Hint,
    string? VisibleWhenKey = null,
    IReadOnlyList<string>? VisibleWhenValues = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LabelKey = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HintKey = null);

/// <summary>Select 参数枚举项。</summary>
public sealed record TransportParameterOptionDto(
    string Value,
    string? Label,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LabelKey = null);

/// <summary>已装配传输插件描述（GET /api/transport.availablePlugins 与 /api/transport/plugins；supportsDocumentCompile 为文档编译能力位；displayNameKey / descriptionKey 为插件级展示端翻译键（决策 #192，接口 DIM），缺省不出现在 JSON）。</summary>
public sealed record TransportPluginDescriptorDto(
    string Id,
    string DisplayName,
    string? Description,
    IReadOnlyList<TransportPluginParameterDto> Parameters,
    bool IsExternal = false,
    string? AssemblyPath = null,
    bool SupportsDocumentCompile = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayNameKey = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DescriptionKey = null);

/// <summary>连接状态（GET /api/transport 与 POST 响应共用；旧字段 mode / availableModes 保留兼容旧前端）。</summary>
public sealed record TransportConfigDto(
    string PluginId,
    string DisplayName,
    string DisplayText,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyList<TransportPluginDescriptorDto> AvailablePlugins,
    string Mode,
    IReadOnlyList<string> AvailableModes);

/// <summary>连接切换 / 测试请求（POST /api/transport；pluginId + params 字典优先，旧字段 mode + 平铺参数兼容）。</summary>
public sealed record TransportApplyRequest(
    string? PluginId,
    IReadOnlyDictionary<string, string>? Params,
    bool? TestOnly,
    string? Mode = null,
    string? TcpHost = null,
    int? TcpPort = null,
    string? PrinterName = null,
    string? ZebraKind = null,
    string? ZebraUsbName = null);

/// <summary>连接切换 / 测试响应：ok + 中文消息 + 当前生效连接（失败时 config = 未变前的连接）。</summary>
public sealed record TransportApplyResponse(bool Ok, string Message, TransportConfigDto Config);

/// <summary>机器级配置响应（GET /api/host/config；Ips 为本机枚举 IPv4 状态栏展示用；Version 为只读产品版本——「检查更新」比较口径，POST 忽略）。</summary>
public sealed record HostConfigDto(string ServerUrl, string DeviceId, string DeviceName, IReadOnlyList<string>? Ips = null, string? Version = null);

/// <summary>机器级配置请求（POST /api/host/config；仅 serverUrl 可写）。</summary>
public sealed record HostConfigRequest(string? ServerUrl);

/// <summary>卸载插件请求（POST /api/plugins/uninstall）。</summary>
public sealed record UninstallPluginRequest(string? PluginId);
