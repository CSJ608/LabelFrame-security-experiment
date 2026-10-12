using LabelFrame.Bootstrapper.Prerequisites;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>
/// Authenticode 发布者验签测试：发布者策略（CN 提取 / 比对）纯函数矩阵 +
/// WinVerifyTrust 互操作冒烟（系统自带微软签名文件——验签器真实链路锚点）。
/// </summary>
public sealed class AuthenticodeVerifierTests
{
    // ---- 发布者策略（纯函数） ----

    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US")]
    [InlineData("CN=microsoft corporation")]
    [InlineData("CN=Microsoft Corporation")]
    public void Allowed_publisher_matches_by_common_name_ignoring_case(string subject)
    {
        Assert.True(AuthenticodePublisherPolicy.IsAllowedPublisher(subject, AuthenticodePublisherPolicy.MicrosoftCorporation));
    }

    [Theory]
    [InlineData("CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US")] // 有效签名但发布者实体不同
    [InlineData("CN=Contoso Ltd, O=Contoso, C=US")]
    [InlineData("O=Microsoft Corporation, C=US")] // 无 CN 段
    [InlineData("CN=")] // 空 CN
    [InlineData("")]
    public void Disallowed_publisher_is_rejected(string subject)
    {
        Assert.False(AuthenticodePublisherPolicy.IsAllowedPublisher(subject, AuthenticodePublisherPolicy.MicrosoftCorporation));
    }

    [Fact]
    public void Null_subject_is_rejected()
    {
        Assert.False(AuthenticodePublisherPolicy.IsAllowedPublisher(null, AuthenticodePublisherPolicy.MicrosoftCorporation));
    }

    [Fact]
    public void Common_name_extraction_tolerates_quoted_values_and_inner_commas()
    {
        Assert.Equal("Contoso, Ltd", AuthenticodePublisherPolicy.TryExtractCommonName("CN=\"Contoso, Ltd\", O=Contoso, C=US"));
        Assert.Equal("Microsoft Corporation", AuthenticodePublisherPolicy.TryExtractCommonName(" O = X, CN = Microsoft Corporation "));
        Assert.Null(AuthenticodePublisherPolicy.TryExtractCommonName(null));
        Assert.Null(AuthenticodePublisherPolicy.TryExtractCommonName("O=No Common Name"));
    }

    // ---- WinVerifyTrust 互操作冒烟（真实系统文件；文件不在场的极端镜像跳过） ----

    [Fact]
    public void Embedded_signed_binary_reports_valid_signature_and_subject()
    {
        // 候选 = 携带内嵌 Authenticode 签名的微软产物（dotnet.exe 为 CN=.NET 签名——注意 Windows 系统文件多为
        // catalog 签名，WTD_CHOICE_FILE 只验内嵌签名，故不能用 cmd.exe 一类）；候选全不在场的精简环境跳过
        var candidate = ResolveEmbeddedSignedCandidate();
        if (candidate is null)
        {
            return;
        }

        var result = new WinTrustAuthenticodeVerifier().Verify(candidate);

        Assert.True(result.SignatureValid, $"WinVerifyTrust 状态 0x{result.TrustStatus:X8}：{result.FailureReason}；主体 = {result.SignerSubject}");
        Assert.NotNull(result.SignerSubject);
        Assert.Contains("CN=", result.SignerSubject); // WTHelper 链读到签名者主体（互操作成功锚点）
        Assert.Contains("Microsoft", result.SignerSubject, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveEmbeddedSignedCandidate()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(programFiles, "dotnet", "dotnet.exe"),
            Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    [Fact]
    public void Tampered_content_of_signed_binary_fails_hash_verification()
    {
        // 复制内嵌签名文件并翻转一字节：Authenticode 哈希不符 → 签名无效（防投毒核心路径）
        var signedFile = ResolveEmbeddedSignedCandidate();
        if (signedFile is null)
        {
            return;
        }

        var tampered = Path.Combine(Path.GetTempPath(), "lf-tampered-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            var bytes = File.ReadAllBytes(signedFile);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(tampered, bytes);

            var result = new WinTrustAuthenticodeVerifier().Verify(tampered);

            Assert.False(result.SignatureValid);
            Assert.NotNull(result.FailureReason);
        }
        finally
        {
            if (File.Exists(tampered))
            {
                File.Delete(tampered);
            }
        }
    }

    [Fact]
    public void Unsigned_content_reports_invalid()
    {
        var unsigned = Path.Combine(Path.GetTempPath(), "lf-unsigned-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllText(unsigned, "not-a-signed-pe");
            var result = new WinTrustAuthenticodeVerifier().Verify(unsigned);

            Assert.False(result.SignatureValid);
        }
        finally
        {
            if (File.Exists(unsigned))
            {
                File.Delete(unsigned);
            }
        }
    }

    [Fact]
    public void Missing_file_reports_invalid_without_throwing()
    {
        var result = new WinTrustAuthenticodeVerifier().Verify(Path.Combine(Path.GetTempPath(), "lf-absent-" + Guid.NewGuid().ToString("N")));

        Assert.False(result.SignatureValid);
        Assert.NotNull(result.FailureReason);
    }
}
