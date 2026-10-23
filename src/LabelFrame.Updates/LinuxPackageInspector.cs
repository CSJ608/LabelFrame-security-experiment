using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LabelFrame.Updates;

/// <summary>使用应用固定证书与 Linux MSI 工具验证客户端安装包。</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxPackageInspector(string certificatePath) : IClientUpdatePackageInspector
{
    public async Task<ClientUpdatePackage> InspectAsync(string path, string fileName, CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > ClientUpdateContract.MaxPackageBytes) throw new InvalidDataException("安装包大小超出自动升级限制。");
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        var signature = await InspectionProcess.RunAsync("/usr/bin/msiinfo", ["extract", path, "\u0005DigitalSignature"], cancellationToken);
        var (publisher, _) = PackageSignature.Verify(signature, certificatePath);
        var temporary = Path.Combine(Path.GetTempPath(), "labelframe-inspect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var pemPath = Path.Combine(temporary, "publisher.pem");
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
            await File.WriteAllTextAsync(pemPath, certificate.ExportCertificatePem(), cancellationToken);
            await InspectionProcess.RunAsync("/usr/bin/osslsigncode", ["verify", "-CAfile", pemPath, "-ignore-timestamp", "-ignore-cdp",
                "-require-leaf-hash", "sha256:" + publisher, "-in", path], cancellationToken);
            var exported = Encoding.UTF8.GetString(await InspectionProcess.RunAsync("/usr/bin/msiinfo", ["export", path, "Property"], cancellationToken));
            var properties = exported.Split('\n').Skip(3).Where(line => line.Contains('\t'))
                .Select(line => line.TrimEnd('\r').Split('\t', 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
            var summary = Encoding.UTF8.GetString(await InspectionProcess.RunAsync("/usr/bin/msiinfo", ["suminfo", path], cancellationToken));
            var template = summary.Split('\n').FirstOrDefault(line => line.StartsWith("Template: ", StringComparison.Ordinal))
                ?? throw new InvalidDataException("安装包缺少架构元数据。");
            return ClientPackageMetadata.Validate(properties, template[10..].Trim().Split(';')[0], fileName, publisher, sha256, file.Length);
        }
        finally
        {
            File.Delete(pemPath);
            Directory.Delete(temporary);
        }
    }
}

/// <summary>按服务端运行平台选择安装包验证器。</summary>
public static class ClientPackageInspector
{
    public static IClientUpdatePackageInspector Create(string? certificatePath = null)
    {
        var path = certificatePath ?? ClientUpdateContract.CertificatePath;
        if (OperatingSystem.IsWindows()) return new WindowsPackageInspector(path);
        if (OperatingSystem.IsLinux()) return new LinuxPackageInspector(path);
        throw new PlatformNotSupportedException("当前系统不支持客户端更新包验证。");
    }
}
