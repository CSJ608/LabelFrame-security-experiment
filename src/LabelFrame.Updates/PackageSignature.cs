using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace LabelFrame.Updates;

/// <summary>固定客户端发布者的 CMS 签名验证。</summary>
internal static class PackageSignature
{
    internal static (string Publisher, byte[] Content) Verify(byte[] signature, string certificatePath)
    {
        using var trusted = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
        if (DateTime.UtcNow < trusted.NotBefore.ToUniversalTime() || DateTime.UtcNow > trusted.NotAfter.ToUniversalTime())
            throw new InvalidDataException("客户端更新发布者证书已过期或尚未生效，请由管理员更新信任配置。");
        var usages = trusted.Extensions.OfType<X509EnhancedKeyUsageExtension>().SelectMany(e => e.EnhancedKeyUsages.Cast<Oid>());
        if (!usages.Any(o => o.Value == "1.3.6.1.5.5.7.3.3")) throw new InvalidDataException("更新发布者证书没有代码签名用途。");
        var publisher = trusted.GetCertHashString(HashAlgorithmName.SHA256);
        try
        {
            var cms = new SignedCms();
            cms.Decode(signature);
            if (cms.ContentInfo.ContentType.Value != "1.3.6.1.4.1.311.2.1.4" || cms.SignerInfos.Count != 1)
                throw new InvalidDataException("安装包签名格式不受支持。");
            var signer = cms.SignerInfos[0];
            if (signer.Certificate?.GetCertHashString(HashAlgorithmName.SHA256) != publisher || signer.DigestAlgorithm.Value != "2.16.840.1.101.3.4.2.1")
                throw new InvalidDataException("安装包不是受信任的 LabelFrame 更新发布者，或签名算法不受支持。");
            cms.CheckSignature(verifySignatureOnly: true);
            return (publisher, cms.ContentInfo.Content);
        }
        catch (CryptographicException ex) { throw new InvalidDataException("安装包签名校验失败。", ex); }
    }
}
