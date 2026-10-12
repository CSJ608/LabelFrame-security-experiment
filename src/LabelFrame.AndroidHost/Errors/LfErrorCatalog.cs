using System.Text.RegularExpressions;
using LabelFrame.Core.Errors;

namespace LabelFrame.AndroidHost.Errors;

/// <summary>LF_* 错误码 PDA 侧码表（与 web 端 errorCodes.json 逐码对齐，占位 {key} 单花括号）。
/// 展示规则：en 态已知码按码表模板插值（复用 <see cref="ErrorMessageTemplates.Format"/> 单遍替换），
/// 未知码或模板所需参数不全回退后端中文 message——任何输入都不出现裸占位符；zh 态直用后端 message
/// （中文权威在后端，zh 份是注册表镜像，供覆盖断言用）。</summary>
public static partial class LfErrorCatalog
{
    /// <summary>zh 码表（注册表镜像：覆盖断言用，zh 态展示不走本表）。</summary>
    public static readonly IReadOnlyDictionary<string, string> Zh = new Dictionary<string, string>
    {
        ["LF_API_001"] = "请求格式错误（缺少必填字段或参数无效）。",
        ["LF_API_BAD_BODY"] = "请求体或参数无效：不是合法的 JSON、编码不是 UTF-8 或参数类型不匹配。请检查请求内容与 Content-Type（application/json; charset=utf-8）后重试。",
        ["LF_JOB_001"] = "作业不存在：{jobId}。",
        ["LF_JOB_002"] = "作业当前状态 {status} 不允许该操作。",
        ["LF_JOB_003"] = "作业没有第 {itemIndex} 张标签。",
        ["LF_JOB_004"] = "第 {itemIndex} 张状态为 {itemStatus}，仅 Failed 可重打。",
        ["LF_IO_001"] = "发送失败：{reason}。",
        ["LF_ENC_001"] = "标签编码失败。",
        ["LF_ENC_002"] = "打印指令编译失败，请尝试切换打印方式后重试。",
        ["LF_ENC_003"] = "打印方式配置异常：当前连接的插件不支持原生打印。",
        ["LF_TPL_001"] = "模板不存在:{templateName}。",
        ["LF_TPL_002"] = "模板图片无效：base64、格式（仅 PNG / JPG / BMP）、单张 2MB、每模板 10 张共 10MB 上限，或渲染引用的图片资源缺失。",
        ["LF_TRANSPORT_INVALID"] = "连接配置无效（参数缺失或不受支持）。",
        ["LF_TRANSPORT_TEST_FAILED"] = "测试页发送失败：无法连接打印机「{target}」——{reason}。请检查打印机地址 / 网络 / 驱动后重试。",
        ["LF_PLUGIN_INVALID"] = "插件包无效：{detail}",
        ["LF_PLUGIN_BUSY"] = "插件文件被占用，需重启客户端后生效。",
        ["LF_PLUGIN_INSTALL_FAILED"] = "插件安装失败，请重试；问题持续请联系管理员。",
        ["LF_INTERNAL_001"] = "服务器内部错误，请查看服务端日志。",
        ["LF_SRV_001"] = "设备未注册：{deviceId}。",
        ["LF_SRV_002"] = "请求无效：缺少必填字段或参数不合法。",
        ["LF_SRV_003"] = "作业不存在：{jobId}。",
        ["LF_SRV_004"] = "设备 {deviceId} 不是作业 {jobId} 的领取者。",
        ["LF_SRV_005"] = "作业 {jobId} 当前状态 {status} 不允许该操作。",
        ["LF_SRV_006"] = "模板不存在：{templateName}。",
        ["LF_SRV_007"] = "安装包不存在。",
        ["LF_SRV_008"] = "插件包不存在。",
        ["LF_SRV_009"] = "宿主失联超时（LF_SRV_009）：领取后超过 {minutes} 分钟未回报终态，服务端已按失败回收（结果未知，可能已实际打印）；需重打请用新 requestId 重发，不会自动重新投递。",
        ["LF_SRV_010"] = "安装包不存在。",
        ["LF_SRV_011"] = "暂存超过 {hours} 小时未投递，服务端已放弃；需重打请用新 requestId 重发。",
        ["LF_SRV_012"] = "按 IP 未找到设备：{ip}。",
        ["LF_SRV_013"] = "callbackUrl 无效（仅支持 http/https 地址）：{callbackUrl}。",
    };

    /// <summary>en 码表（en 态已知码的本地翻译模板）。</summary>
    public static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["LF_API_001"] = "Invalid request (missing required fields or invalid parameters).",
        ["LF_API_BAD_BODY"] = "Invalid request body or parameters: not valid JSON, not UTF-8, or a parameter type mismatch. Check the request content and Content-Type (application/json; charset=utf-8), then retry.",
        ["LF_JOB_001"] = "Job not found: {jobId}.",
        ["LF_JOB_002"] = "The job status {status} does not allow this operation.",
        ["LF_JOB_003"] = "The job has no label #{itemIndex}.",
        ["LF_JOB_004"] = "Label #{itemIndex} has status {itemStatus}; only Failed labels can be reprinted.",
        ["LF_IO_001"] = "Send failed: {reason}.",
        ["LF_ENC_001"] = "Label encoding failed.",
        ["LF_ENC_002"] = "Print command compilation failed. Try switching the print mode and retry.",
        ["LF_ENC_003"] = "Print mode misconfigured: the connected plugin does not support native printing.",
        ["LF_TPL_001"] = "Template not found: {templateName}.",
        ["LF_TPL_002"] = "Template image invalid: base64, format (PNG / JPG / BMP only), 2 MB per image, 10 images / 10 MB per template, or a referenced image resource is missing.",
        ["LF_TRANSPORT_INVALID"] = "Invalid connection configuration (missing or unsupported parameters).",
        ["LF_TRANSPORT_TEST_FAILED"] = "Test page failed to send: cannot connect to printer \"{target}\" — {reason}. Check the printer address / network / driver and retry.",
        ["LF_PLUGIN_INVALID"] = "Invalid plugin package: {detail}",
        ["LF_PLUGIN_BUSY"] = "Plugin files are in use. Restart the client to apply changes.",
        ["LF_PLUGIN_INSTALL_FAILED"] = "Plugin installation failed. Please retry; if the problem persists, contact your administrator.",
        ["LF_INTERNAL_001"] = "Internal server error. Check the server logs.",
        ["LF_SRV_001"] = "Device not registered: {deviceId}.",
        ["LF_SRV_002"] = "Invalid request: missing required fields or invalid parameters.",
        ["LF_SRV_003"] = "Job not found: {jobId}.",
        ["LF_SRV_004"] = "Device {deviceId} is not the owner of job {jobId}.",
        ["LF_SRV_005"] = "Job {jobId} in status {status} does not allow this operation.",
        ["LF_SRV_006"] = "Template not found: {templateName}.",
        ["LF_SRV_007"] = "Installer package not found.",
        ["LF_SRV_008"] = "Plugin package not found.",
        ["LF_SRV_009"] = "Host lost timeout (LF_SRV_009): no terminal report for over {minutes} minutes after the job was claimed. The server recycled it as failed (result unknown; it may have printed). To reprint, resubmit with a new requestId; it will not be redelivered automatically.",
        ["LF_SRV_010"] = "Installer package (APK) not found.",
        ["LF_SRV_011"] = "The job expired after {hours} hours without delivery and was abandoned by the server. To reprint, resubmit with a new requestId.",
        ["LF_SRV_012"] = "No device found for IP: {ip}.",
        ["LF_SRV_013"] = "Invalid callbackUrl (only http/https URLs are allowed): {callbackUrl}.",
    };

    /// <summary>解析错误展示文案：en 态已知码按码表模板插值（模板所需参数不全或未知码回退后端中文 message）；
    /// 非 en 态（zh 缺省与回退 zh 的系统语言）直接采用后端 message。</summary>
    /// <param name="code">后端 ErrorView.code（null / 空走回退）。</param>
    /// <param name="parameters">后端 ErrorView.params（扁平字符串键值对象）。</param>
    /// <param name="backendMessage">后端中文 message（兜底展示）。</param>
    /// <param name="english">当前资源语言是否为 en（经语言哨兵资源判定）。</param>
    public static string Resolve(
        string? code,
        IReadOnlyDictionary<string, string>? parameters,
        string backendMessage,
        bool english)
    {
        if (!english || string.IsNullOrWhiteSpace(code))
        {
            return backendMessage;
        }

        if (!En.TryGetValue(code, out var template))
        {
            return backendMessage;
        }

        var needed = PlaceholderKeys(template);
        if (needed.Count > 0
            && (parameters is null || needed.Any(key => !parameters.ContainsKey(key))))
        {
            // 模板带参但后端未提供：回退后端原文，不输出插值残缺
            return backendMessage;
        }

        return ErrorMessageTemplates.Format(template, parameters);
    }

    /// <summary>提取模板的全部占位符键名（去重；<c>{key}</c> 单花括号形态）。</summary>
    public static IReadOnlyList<string> PlaceholderKeys(string template) =>
        PlaceholderRegex().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>占位符形态：与 Core <see cref="ErrorMessageTemplates"/> 同款（键名限字母 / 数字 / 下划线）。</summary>
    [GeneratedRegex("""\{([A-Za-z0-9_]+)\}""")]
    private static partial Regex PlaceholderRegex();
}
