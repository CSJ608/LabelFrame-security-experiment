namespace LabelFrame.Bootstrapper.Downloads;

/// <summary>下载侧失败分类（差异化提示口径）。</summary>
public enum DownloadFailureCategory
{
    /// <summary>无失败（成功态）。</summary>
    None,

    /// <summary>源不可达：URL 失效 / 文件不存在（404 等）/ 域名无法解析 / 无法建立连接。</summary>
    SourceUnreachable,

    /// <summary>网络中断：连接被重置 / 响应超时 / 传输中断。</summary>
    NetworkInterrupted,

    /// <summary>校验失败：下载内容与构建期锁定的 SHA-256 摘要不符（传输损坏或源内容被篡改）。</summary>
    VerificationFailed,

    /// <summary>未分类（其余错误码——以安装日志为准）。</summary>
    Other,
}

/// <summary>
/// 下载侧失败分类器：引擎 HRESULT → <see cref="DownloadFailureCategory"/> + 差异化中文提示（含可操作建议）。
/// wininet 错误码经 HRESULT_FROM_WIN32 映射为 0x8007xxxx / INET_E_* 为 0x800Cxxxx。
/// </summary>
public static class DownloadFailureClassifier
{
    /// <summary>CRYPT_E_HASH_VALUE——Burn 载荷校验（哈希不符）的典型错误码。</summary>
    public const int HashMismatchHResult = unchecked((int)0x80091007);

    /// <summary>源不可达：INET_E_RESOURCE_NOT_FOUND（404 / 资源不存在）、INET_E_INVALID_URL、文件未找到、域名未解析、无法连接。</summary>
    private static readonly HashSet<int> SourceUnreachableCodes =
    [
        unchecked((int)0x800C0005), // INET_E_RESOURCE_NOT_FOUND
        unchecked((int)0x800C0002), // INET_E_INVALID_URL
        unchecked((int)0x80070002), // ERROR_FILE_NOT_FOUND（本地源解析失败）
        unchecked((int)0x80070003), // ERROR_PATH_NOT_FOUND
        unchecked((int)0x80072EE7), // ERROR_INTERNET_NAME_NOT_RESOLVED
        unchecked((int)0x80072EFD), // ERROR_INTERNET_CANNOT_CONNECT
    ];

    /// <summary>网络中断：超时（12002）、连接中止 / 重置（12029 / 12030）、INET_E_DOWNLOAD_FAILURE、TCP 层中断。</summary>
    private static readonly HashSet<int> NetworkInterruptedCodes =
    [
        unchecked((int)0x80072EE2), // ERROR_INTERNET_TIMEOUT
        unchecked((int)0x80072EFE), // ERROR_INTERNET_CONNECTION_ABORTED
        unchecked((int)0x80072EFF), // ERROR_INTERNET_CONNECTION_RESET
        unchecked((int)0x800C0008), // INET_E_DOWNLOAD_FAILURE（会话中断）
        unchecked((int)0x80072746), // WSAECONNRESET
        unchecked((int)0x8007274C), // WSAETIMEDOUT
        unchecked((int)0x8007274D), // WSAECONNREFUSED（端口未监听——常为源服务未就绪）
    ];

    /// <summary>HRESULT → 分类（线性查表；未知码归 <see cref="DownloadFailureCategory.Other"/>）。</summary>
    public static DownloadFailureCategory Classify(int hresult)
    {
        if (hresult == 0)
        {
            return DownloadFailureCategory.None;
        }

        if (hresult == HashMismatchHResult)
        {
            return DownloadFailureCategory.VerificationFailed;
        }

        if (SourceUnreachableCodes.Contains(hresult))
        {
            return DownloadFailureCategory.SourceUnreachable;
        }

        if (NetworkInterruptedCodes.Contains(hresult))
        {
            return DownloadFailureCategory.NetworkInterrupted;
        }

        return DownloadFailureCategory.Other;
    }

    /// <summary>分类 → 差异化中文提示（含可操作建议；源耗尽 / 重试语境由调用方拼接）。</summary>
    public static string Describe(DownloadFailureCategory category) => category switch
    {
        DownloadFailureCategory.SourceUnreachable =>
            "下载源不可达（源地址失效、文件不存在或无法建立连接）。引导程序已按清单顺序自动尝试备用源；"
            + "若全部源均失败，请检查网络与代理设置，或由管理员部署镜像源（安装清单 urls 数组支持多源，镜像位已预留）。",
        DownloadFailureCategory.NetworkInterrupted =>
            "网络传输中断或超时（连接被重置 / 响应超时）。引导程序已自动换源或重试；"
            + "请检查网络稳定性、代理与防火墙设置后点击「重试」——已成功缓存的组件不会重复下载。",
        DownloadFailureCategory.VerificationFailed =>
            "文件完整性校验失败：下载内容与构建时锁定的 SHA-256 摘要不符（传输损坏或源内容异常）。"
            + "引导程序已自动换源重新获取；若持续失败请停止安装并携带日志反馈，勿尝试绕过校验安装不明文件。",
        DownloadFailureCategory.Other =>
            "下载或校验出现未分类错误，请查看安装日志中的错误码定位原因。",
        _ => string.Empty,
    };
}
