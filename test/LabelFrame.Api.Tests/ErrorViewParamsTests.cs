using System.Text.Json;
using LabelFrame.Api;
using LabelFrame.Core.Errors;
using Xunit;

namespace LabelFrame.Api.Tests;

/// <summary>
/// ErrorView 可选 params 契约测试：
/// params 缺省 / null 时序列化与旧形态逐字节等价（加法兼容，旧调用方无感）；
/// params 非空时以 JSON 对象形态追加在字段尾部；模板插值单遍替换、参数缺失占位符原样保留。
/// </summary>
public sealed class ErrorViewParamsTests
{
    /// <summary>之前的 ErrorView 形态快照（现状锚点）：三字段位置记录。</summary>
    private sealed record LegacyErrorView(string Code, string Message, string? FieldKey = null);

    // minimal API 默认 JSON 选项（Microsoft.AspNetCore.Http.Json → JsonSerializerDefaults.Web：camelCase、null 输出）
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    /// <summary>解析 JSON 顶层字段名序列（JsonDocument 保持文档顺序，可断言字段顺序）。</summary>
    private static string[] FieldNames(string json)
        => JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public void Params_absent_should_serialize_byte_equal_to_legacy_shape()
    {
        // 现状锚点：与前的三字段形态逐字节一致（minimal API Web 默认：camelCase、null 输出、非 ASCII 转义）
        var legacy = JsonSerializer.Serialize(
            new LegacyErrorView("LF_SRV_002", "请求体不能为空。"), WebDefaults);
        var current = JsonSerializer.Serialize(
            new ErrorView("LF_SRV_002", "请求体不能为空。"), WebDefaults);

        Assert.Equal(legacy, current);
        // 字段集与顺序锚定（code / message / fieldKey，无 params）
        Assert.Equal(["code", "message", "fieldKey"], FieldNames(current));
    }

    [Fact]
    public void Params_explicit_null_should_serialize_byte_equal_to_legacy_shape()
    {
        var legacy = JsonSerializer.Serialize(
            new LegacyErrorView("LF_TPL_001", "模板不存在:标签A。", "name"), WebDefaults);
        var current = JsonSerializer.Serialize(
            new ErrorView("LF_TPL_001", "模板不存在:标签A。", "name", Params: null), WebDefaults);

        Assert.Equal(legacy, current);
    }

    [Fact]
    public void Params_present_should_append_after_fieldKey()
    {
        var current = JsonSerializer.Serialize(
            new ErrorView(
                "LF_SRV_001",
                "按 IP 未找到设备：192.168.1.5。",
                Params: new Dictionary<string, string> { ["ip"] = "192.168.1.5" }),
            WebDefaults);

        // params 以扁平对象追加在字段尾部；字段顺序 code / message / fieldKey / params
        Assert.Equal(["code", "message", "fieldKey", "params"], FieldNames(current));
        using var doc = JsonDocument.Parse(current);
        var parameters = doc.RootElement.GetProperty("params");
        Assert.Equal(JsonValueKind.Object, parameters.ValueKind);
        Assert.Equal("192.168.1.5", parameters.GetProperty("ip").GetString());
        // 与缺省形态的公共前缀一致（纯加法：新字段只在尾部出现，闭合花括号前逐字节相同）
        var withoutParams = JsonSerializer.Serialize(
            new ErrorView("LF_SRV_001", "按 IP 未找到设备：192.168.1.5。"), WebDefaults);
        Assert.StartsWith(withoutParams[..^1], current, StringComparison.Ordinal);
    }

    [Fact]
    public void Params_deserialization_roundtrip_should_tolerate_absence()
    {
        // 旧后端 / 旧调用方向：JSON 无 params 字段 → 反序列化 Params 为 null（加法兼容）
        var fromLegacy = JsonSerializer.Deserialize<ErrorView>("""{"code":"X","message":"m","fieldKey":null}""", WebDefaults);
        Assert.Null(fromLegacy!.Params);

        var fromCurrent = JsonSerializer.Deserialize<ErrorView>(
            """{"code":"X","message":"m","fieldKey":null,"params":{"ip":"1.2.3.4"}}""", WebDefaults);
        Assert.NotNull(fromCurrent!.Params);
        Assert.Equal("1.2.3.4", fromCurrent.Params!["ip"]);
    }

    // ---- ErrorMessageTemplates（模板 + 参数构造，渲染结果与旧内插文案等价）----

    [Fact]
    public void Format_should_render_placeholders_from_parameters()
    {
        var rendered = ErrorMessageTemplates.Format(
            "按 IP 未找到设备：{ip}。",
            new Dictionary<string, string> { ["ip"] = "10.0.0.9" });
        Assert.Equal("按 IP 未找到设备：10.0.0.9。", rendered);
    }

    [Fact]
    public void Format_should_keep_placeholder_when_parameter_missing()
    {
        // 消息兜底不抛：缺参数时占位符原样保留（后端中文 message 仍可读）
        Assert.Equal(
            "设备 {deviceId} 不是作业 j-1 的领取者。",
            ErrorMessageTemplates.Format(
                "设备 {deviceId} 不是作业 {jobId} 的领取者。",
                new Dictionary<string, string> { ["jobId"] = "j-1" }));
    }

    [Fact]
    public void Format_should_not_cascade_replace_placeholder_shaped_values()
    {
        // 单遍替换：参数值本身含 {x} 形态时不被二次替换（防注入式文案变形）
        Assert.Equal(
            "不支持的连接方式：{mode}。",
            ErrorMessageTemplates.Format(
                "不支持的连接方式：{mode}。",
                new Dictionary<string, string> { ["mode"] = "{mode}" }));
    }

    [Fact]
    public void Format_should_return_template_when_parameters_null_or_empty()
    {
        Assert.Equal("请求体不能为空。", ErrorMessageTemplates.Format("请求体不能为空。", null));
        Assert.Equal("请求体不能为空。", ErrorMessageTemplates.Format("请求体不能为空。", new Dictionary<string, string>()));
    }
}
