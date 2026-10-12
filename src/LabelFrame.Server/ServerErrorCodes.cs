namespace LabelFrame.Server;

/// <summary>Server 问题码。约定：LF_SRV_xxx。</summary>
/// <remarks>新增码须同步 web 端码表（`web/src/i18n/locales/*/errorCodes.json`，覆盖断言测试挡漏译）。</remarks>
public static class ServerErrorCodes
{
    /// <summary>设备未注册。</summary>
    public const string DeviceNotFound = "LF_SRV_001";

    /// <summary>请求格式错误。</summary>
    public const string InvalidRequest = "LF_SRV_002";

    /// <summary>作业不存在。</summary>
    public const string JobNotFound = "LF_SRV_003";

    /// <summary>设备不是该作业的领取者。</summary>
    public const string NotJobOwner = "LF_SRV_004";

    /// <summary>作业状态不允许该操作。</summary>
    public const string InvalidTransition = "LF_SRV_005";

    /// <summary>模板不存在。</summary>
    public const string TemplateNotFound = "LF_SRV_006";

    /// <summary>客户端安装包不存在。</summary>
    public const string ClientPackageNotFound = "LF_SRV_007";

    /// <summary>传输插件包不存在。</summary>
    public const string PluginPackageNotFound = "LF_SRV_008";

    /// <summary>宿主失联超时（Claimed 作业超时回收的原因码，嵌入回收后的 ErrorMessage 文案；非 HTTP 错误响应）。</summary>
    public const string HostLostTimeout = "LF_SRV_009";

    /// <summary>PDA（Android 宿主）安装包不存在。</summary>
    public const string PdaPackageNotFound = "LF_SRV_010";

    /// <summary>Pending 暂存超期放弃（超 TTL 未投递置 Expired 终态的原因码，落 server_jobs.error_code）。</summary>
    public const string PendingExpired = "LF_SRV_011";

    /// <summary>按 IP 未找到设备（提交 targetIp 解析 / GET by-ip 查询；与 LF_SRV_001 设备未注册语义不同）。</summary>
    public const string DeviceNotFoundByIp = "LF_SRV_012";

    /// <summary>callbackUrl 无效（scheme 白名单仅 http/https，提交即拒）。</summary>
    public const string InvalidCallbackUrl = "LF_SRV_013";
}
