namespace LabelFrame.Api;

/// <summary>
/// API 问题码注册表（Server / WinHost 共用端点的语义化错误码；LF_API_* / LF_JOB_* / LF_SRV_* 等在各自工程注册表）。
/// LF_INTERNAL_001 与 LF_API_BAD_BODY 全仓仅本注册表一处字面量，其余代码一律引用常量。
/// </summary>
public static class ApiErrorCodes
{
    /// <summary>模板不存在（宿主侧模板库查询失败；服务端沿用 LF_SRV_006）。</summary>
    public const string TemplateNotFound = "LF_TPL_001";

    /// <summary>模板图片无效：保存请求 images 的 base64 / 格式（仅 PNG / JPG / BMP）/ 单图 2MB / 单模板 10 张共 10MB 上限校验失败，或渲染引用的图片资源缺失。</summary>
    public const string TemplateImageInvalid = "LF_TPL_002";

    /// <summary>连接配置无效（pluginId / 参数校验失败）。</summary>
    public const string TransportInvalid = "LF_TRANSPORT_INVALID";

    /// <summary>测试页发送失败（打印机连接不可达 / 超时等传输故障；消息含目标地址与原因）。</summary>
    public const string TransportTestFailed = "LF_TRANSPORT_TEST_FAILED";

    /// <summary>插件包无效（zip / manifest / 预检校验失败）。</summary>
    public const string PluginInvalid = "LF_PLUGIN_INVALID";

    /// <summary>插件文件被占用（卸载 / 覆盖需重启客户端）。</summary>
    public const string PluginBusy = "LF_PLUGIN_BUSY";

    /// <summary>插件安装失败（解压 / 写入等 IO 异常）。</summary>
    public const string PluginInstallFailed = "LF_PLUGIN_INSTALL_FAILED";

    /// <summary>请求体反序列化失败（非法 JSON / 非 UTF-8 编码 / 字段类型不匹配 → 400；属调用方错误）。</summary>
    public const string BadBody = "LF_API_BAD_BODY";

    /// <summary>未捕获的服务器内部错误（全局异常处理器 500 兜底；全仓唯一字面量定义处）。</summary>
    public const string InternalError = "LF_INTERNAL_001";
}
