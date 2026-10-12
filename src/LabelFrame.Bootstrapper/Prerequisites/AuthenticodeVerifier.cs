namespace LabelFrame.Bootstrapper.Prerequisites;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

/// <summary>Authenticode 验签结果。</summary>
/// <param name="SignatureValid">签名与证书链是否完整有效（WinVerifyTrust 通过；文件被改动 / 签名缺失 / 链不受信 = false）。</param>
/// <param name="SignerSubject">签名者证书主体（完整 RDN，如 <c>CN=Microsoft Corporation, O=…, C=US</c>）；签名无效时为 null。</param>
/// <param name="TrustStatus">WinVerifyTrust 返回的 HRESULT（0 = 有效）。</param>
/// <param name="FailureReason">中文失败原因（签名无效时非空）。</param>
public sealed record AuthenticodeVerificationResult(
    bool SignatureValid,
    string? SignerSubject,
    int TrustStatus,
    string? FailureReason)
{
    /// <summary>构造「签名无效」结果。</summary>
    public static AuthenticodeVerificationResult Invalid(int trustStatus, string reason) =>
        new(false, null, trustStatus, reason);
}

/// <summary>Authenticode 文件验签接口（实现 = WinVerifyTrust；测试注入假验签器）。</summary>
public interface IAuthenticodeVerifier
{
    /// <summary>验证文件的 Authenticode 签名（只读，无系统改动）。</summary>
    AuthenticodeVerificationResult Verify(string filePath);
}

/// <summary>
/// 发布者策略（纯函数）：evergreen 载荷防投毒 = 签名链完整（WinVerifyTrust）+ 签名者主体
/// CN 等于期望发布者双条件——伪造微软签名非可行攻击，对厂商轮换免疫。
/// </summary>
public static class AuthenticodePublisherPolicy
{
    /// <summary>微软发布者主体 CN（WebView2 evergreen 安装器的验签目标）。</summary>
    public const string MicrosoftCorporation = "Microsoft Corporation";

    /// <summary>签名者主体是否为期望发布者（按 RDN 的 CN 段忽略大小写比较）。</summary>
    public static bool IsAllowedPublisher(string? signerSubject, string expectedPublisher)
    {
        var commonName = TryExtractCommonName(signerSubject);
        return commonName is not null
            && string.Equals(commonName, expectedPublisher, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从证书主体 RDN（如 <c>CN=Microsoft Corporation, O=…, C=US</c>）提取 CN 段值；无 CN / 主体为空返回 null。</summary>
    /// <remarks>容忍段内空格与成对引号（RFC 4514 转义值罕见，不展开处理——提取失败即拒绝，fail-closed）。</remarks>
    public static string? TryExtractCommonName(string? subject)
    {
        // net48 腿 IsNullOrWhiteSpace 无 NotNullWhen 流注解——显式判空保持空值流分析
        if (subject is null || subject.Trim().Length == 0)
        {
            return null;
        }

        // 按顶层逗号分段（跟踪引号配对，容忍值内逗号）
        var segments = new List<string>();
        var builder = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in subject)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (ch == ',' && !quoted)
            {
                segments.Add(builder.ToString());
                builder.Length = 0;
            }
            else
            {
                builder.Append(ch);
            }
        }

        segments.Add(builder.ToString());

        foreach (var segment in segments)
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = segment.Substring(0, separatorIndex).Trim();
            if (!string.Equals(key, "CN", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = segment.Substring(separatorIndex + 1).Trim().Trim('"');
            return value.Length == 0 ? null : value;
        }

        return null;
    }
}

/// <summary>
/// WinVerifyTrust 实现：GENERIC_VERIFY_V2 验证签名与链完整（刻意不做联网吊销检查——安装期离线友好，不可加）；
/// 以 <c>WTD_STATEACTION_VERIFY</c> 取状态数据读签名者证书主体，收尾以 <c>WTD_STATEACTION_CLOSE</c> 释放。
/// </summary>
public sealed class WinTrustAuthenticodeVerifier : IAuthenticodeVerifier
{
    /// <summary>验证文件的 Authenticode 签名（只读；文件不存在 / 无法打开按签名无效处理）。</summary>
    public AuthenticodeVerificationResult Verify(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return AuthenticodeVerificationResult.Invalid(unchecked((int)0x80070002), "文件不存在，无法验证签名。");
        }

        var actionId = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
        var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
        };

        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>());
        AuthenticodeVerificationResult result;
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);
            var data = new NativeMethods.WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_DATA>(),
                dwUIChoice = NativeMethods.WTD_UI_NONE,
                fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE,
                dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                pUnion = fileInfoPtr,
                dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY,
            };

            var status = NativeMethods.WinVerifyTrust(NativeMethods.InvalidHwnd, ref actionId, ref data);
            try
            {
                if (status == 0)
                {
                    var signerSubject = TryReadSignerSubject(data.hWVTStateData);
                    result = signerSubject is null
                        ? AuthenticodeVerificationResult.Invalid(status, "签名有效但无法读取签名者证书主体（拒绝，fail-closed）。")
                        : new AuthenticodeVerificationResult(true, signerSubject, 0, null);
                }
                else
                {
                    result = AuthenticodeVerificationResult.Invalid(status, DescribeStatus(status));
                }
            }
            finally
            {
                // 释放状态数据（同结构体改 CLOSE 再调一次——官方样例「Verifying the Signature of a PE File」形态；
                // 失败路径状态数据可能未建立（句柄为 0），此时无须 CLOSE）
                if (data.hWVTStateData != IntPtr.Zero)
                {
                    data.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
                    _ = NativeMethods.WinVerifyTrust(NativeMethods.InvalidHwnd, ref actionId, ref data);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }

        return result;
    }

    /// <summary>从状态数据读签名者证书主体（WTHelper 链：ProvData → Signer[0] → Cert[0] → 证书编码字节 → X509）。</summary>
    private static string? TryReadSignerSubject(IntPtr stateData)
    {
        if (stateData == IntPtr.Zero)
        {
            return null;
        }

        var providerData = NativeMethods.WTHelperProvDataFromStateData(stateData);
        if (providerData == IntPtr.Zero)
        {
            return null;
        }

        var signer = NativeMethods.WTHelperGetProvSignerFromChain(providerData, 0, fCounterSigner: false, 0);
        if (signer == IntPtr.Zero)
        {
            return null;
        }

        var providerCert = NativeMethods.WTHelperGetProvCertFromChain(signer, 0);
        if (providerCert == IntPtr.Zero)
        {
            return null;
        }

        var providerCertStruct = Marshal.PtrToStructure<NativeMethods.CRYPT_PROVIDER_CERT>(providerCert);
        if (providerCertStruct.pCert == IntPtr.Zero)
        {
            return null;
        }

        var context = Marshal.PtrToStructure<NativeMethods.CERT_CONTEXT>(providerCertStruct.pCert);
        if (context.pbCertEncoded == IntPtr.Zero || context.cbCertEncoded == 0
            || context.cbCertEncoded > int.MaxValue)
        {
            return null;
        }

        var encoded = new byte[context.cbCertEncoded];
        Marshal.Copy(context.pbCertEncoded, encoded, 0, (int)context.cbCertEncoded);

        try
        {
#if NET10_0_OR_GREATER
            // .NET 9+：证书构造器过时（SYSLIB0057），改 Loader
            using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(encoded);
            return certificate.Subject;
#else
            // net48 腿：构造器为标准形态
            using var certificate = new X509Certificate2(encoded);
            return certificate.Subject;
#endif
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>常见 WinVerifyTrust 错误的中文归因（其余按十六进制状态码呈现）。</summary>
    private static string DescribeStatus(int status) => status switch
    {
        unchecked((int)0x800B0100) => "文件没有 Authenticode 签名。",
        unchecked((int)0x800B0101) => "签名证书已过期。",
        unchecked((int)0x800B0104) => "签名者证书不满足使用条件。",
        unchecked((int)0x800B0109) => "签名证书链不受本机信任。",
        unchecked((int)0x80091007) => "文件哈希与签名不符（文件可能被改动）。",
        unchecked((int)0x800B0111) => "签名未通过时间戳与有效期校验。",
        _ => $"WinVerifyTrust 未通过（0x{status:X8}）。",
    };

    private static class NativeMethods
    {
        public const uint WTD_UI_NONE = 2;
        public const uint WTD_REVOKE_NONE = 0;
        public const uint WTD_CHOICE_FILE = 1;
        public const uint WTD_STATEACTION_VERIFY = 1;
        public const uint WTD_STATEACTION_CLOSE = 2;

        /// <summary>WinVerifyTrust 的 hwnd 入参（无 UI 场景传无效句柄 0）。</summary>
        public static readonly IntPtr InvalidHwnd = IntPtr.Zero;

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        public static extern int WinVerifyTrust(IntPtr hWnd, ref Guid pgActionId, ref WINTRUST_DATA data);

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        public static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        public static extern IntPtr WTHelperGetProvSignerFromChain(
            IntPtr providerData,
            uint indexSigner,
            [MarshalAs(UnmanagedType.Bool)] bool fCounterSigner,
            uint indexCounterSigner);

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        public static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint indexCert);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WINTRUST_DATA
        {
            // 字段序 = Wintrust.h _WINTRUST_DATA（无 hWndParent / pgActionID 成员——偏移错位会引发引擎读野指针崩溃）
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pUnion;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        /// <summary>CRYPT_PROVIDER_CERT（只声明读到的首两个字段：cbStruct + pCert——签名者证书上下文指针）。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CRYPT_PROVIDER_CERT
        {
            public uint cbStruct;
            public IntPtr pCert;
        }

        /// <summary>CERT_CONTEXT（只声明读证书编码字节所需字段）。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CERT_CONTEXT
        {
            public uint dwCertEncodingType;
            public IntPtr pbCertEncoded;
            public uint cbCertEncoded;
            public IntPtr pCertInfo;
            public IntPtr hCertStore;
        }
    }
}
