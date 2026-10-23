using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using LabelFrame.Updates;
using LabelFrame.WinHost.Updates;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>更新包元数据、发布者、管道边界与维护策略验证。</summary>
public sealed class ClientUpdateContractTests
{
    [Theory]
    [InlineData("02:00", "04:00", "03:00", true)]
    [InlineData("02:00", "04:00", "04:00", false)]
    [InlineData("23:00", "03:00", "01:00", true)]
    [InlineData("23:00", "03:00", "22:00", false)]
    [InlineData("02:00", "02:00", "02:00", false)]
    [InlineData("invalid", "03:00", "02:00", false)]
    public void Maintenance_window_supports_midnight_and_excludes_missed_windows(string start, string end, string now, bool expected)
        => Assert.Equal(expected, ClientUpdateContract.IsInWindow(TimeOnly.Parse(now, CultureInfo.InvariantCulture), start, end));

    [Fact]
    public void Automatic_install_requires_explicit_opt_in_hidden_ui_and_ten_minute_idle()
    {
        var now = DateTimeOffset.UtcNow;
        var idle = now.AddMinutes(-11);
        var time = new TimeOnly(3, 0);
        Assert.NotNull(ClientUpdatePolicy.PostponeReason(new(), time, false, idle, now));
        Assert.NotNull(ClientUpdatePolicy.PostponeReason(new("notify"), time, false, idle, now));
        Assert.NotNull(ClientUpdatePolicy.PostponeReason(new("maintenance"), time, true, idle, now));
        Assert.NotNull(ClientUpdatePolicy.PostponeReason(new("maintenance"), time, false, now.AddMinutes(-9), now));
        Assert.NotNull(ClientUpdatePolicy.PostponeReason(new("maintenance"), new(12, 0), false, idle, now));
        Assert.Null(ClientUpdatePolicy.PostponeReason(new("maintenance"), time, false, idle, now));
        Assert.Throws<InvalidDataException>(() => new ClientUpdateSettings("maintenance", "02:00", "02:00").Validate());
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("256.1.0")]
    [InlineData("1.0.65536")]
    [InlineData("1.0.0-beta")]
    public void Installer_versions_reject_ambiguous_or_out_of_range_values(string version)
        => Assert.Throws<InvalidDataException>(() => ClientUpdateContract.ParseVersion(version));

    [Fact]
    public void Package_metadata_requires_actual_client_identity_architecture_version_and_compatibility()
    {
        var properties = Metadata();
        Assert.Equal("1.5.0", Validate(properties).Version);
        foreach (var (key, value) in new[] { ("ProductName", "LabelFrame Server"), ("Manufacturer", "other"),
            ("UpgradeCode", Guid.NewGuid().ToString()), ("ProductVersion", "1.6.0"),
            ("LABELFRAME_MIN_SERVER_VERSION", "3.0.0"), ("LABELFRAME_RUNTIME_MAJOR", "invalid") })
        {
            var invalid = new Dictionary<string, string>(properties) { [key] = value };
            Assert.Throws<InvalidDataException>(() => Validate(invalid));
        }
        Assert.Throws<InvalidDataException>(() => ClientPackageMetadata.Validate(properties, "Intel", "LabelFrame-Client-1.5.0.msi", "pub", "hash", 1));
        properties.Remove("LABELFRAME_MIN_SERVER_VERSION");
        Assert.Throws<InvalidDataException>(() => Validate(properties));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(16385)]
    [InlineData(int.MaxValue)]
    public async Task Pipe_protocol_rejects_oversized_or_invalid_frames_before_allocation(int size)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, size);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePipeProtocol.ReadAsync<UpdateInstallRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Pipe_protocol_round_trips_and_rejects_truncation()
    {
        using var stream = new MemoryStream();
        var expected = new UpdateInstallResponse(true, Guid.NewGuid().ToString("N"));
        await UpdatePipeProtocol.WriteAsync(stream, expected, CancellationToken.None);
        stream.Position = 0;
        Assert.Equal(expected, await UpdatePipeProtocol.ReadAsync<UpdateInstallResponse>(stream, CancellationToken.None));
        using var truncated = new MemoryStream(stream.ToArray()[..^1]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => UpdatePipeProtocol.ReadAsync<UpdateInstallResponse>(truncated, CancellationToken.None));
    }

    [Fact]
    public void Cms_signature_requires_pinned_publisher_validity_eku_and_untampered_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-signature-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var certificatePath = Path.Combine(root, "publisher.cer");
        try
        {
            using var key = RSA.Create(2048);
            using var signer = Certificate(key, true, DateTimeOffset.UtcNow.AddDays(1));
            var signature = Sign(signer);
            File.WriteAllBytes(certificatePath, signer.Export(X509ContentType.Cert));
            Assert.Equal(signer.GetCertHashString(HashAlgorithmName.SHA256), PackageSignature.Verify(signature, certificatePath).Publisher);
            var damaged = signature.ToArray();
            damaged[^1] ^= 1;
            Assert.Throws<InvalidDataException>(() => PackageSignature.Verify(damaged, certificatePath));
            using var otherKey = RSA.Create(2048);
            using var other = Certificate(otherKey, true, DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(certificatePath, other.Export(X509ContentType.Cert));
            Assert.Throws<InvalidDataException>(() => PackageSignature.Verify(signature, certificatePath));
            using var expired = Certificate(key, true, DateTimeOffset.UtcNow.AddHours(-1));
            File.WriteAllBytes(certificatePath, expired.Export(X509ContentType.Cert));
            Assert.Throws<InvalidDataException>(() => PackageSignature.Verify(Sign(expired), certificatePath));
            using var wrongUsage = Certificate(key, false, DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(certificatePath, wrongUsage.Export(X509ContentType.Cert));
            Assert.Throws<InvalidDataException>(() => PackageSignature.Verify(Sign(wrongUsage), certificatePath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Executor_rejects_user_writable_deployment_and_quotes_windows_arguments()
    {
        Assert.Throws<InvalidDataException>(() => ProtectedUpdatePaths.Verify(Path.GetTempPath()));
        Assert.Equal("\"a b\"", UpdateSupervisor.QuoteArgument("a b"));
        Assert.Equal("\"a\\\"b\"", UpdateSupervisor.QuoteArgument("a\"b"));
        Assert.Equal("\"a\\\\\"", UpdateSupervisor.QuoteArgument("a\\"));
    }

    private static X509Certificate2 Certificate(RSA key, bool codeSigning, DateTimeOffset until)
    {
        var request = new CertificateRequest("CN=LabelFrame Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection {
            new(codeSigning ? "1.3.6.1.5.5.7.3.3" : "1.3.6.1.5.5.7.3.1") }, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), until);
    }
    private static byte[] Sign(X509Certificate2 certificate)
    {
        var cms = new SignedCms(new ContentInfo(new Oid("1.3.6.1.4.1.311.2.1.4"), [0x30, 0]), false);
        cms.ComputeSignature(new CmsSigner(certificate) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") });
        return cms.Encode();
    }
    private static Dictionary<string, string> Metadata() => new()
    {
        ["ProductName"] = "LabelFrame Client", ["Manufacturer"] = "LabelFrame", ["UpgradeCode"] = ClientUpdateContract.UpgradeCode,
        ["ProductCode"] = Guid.NewGuid().ToString(), ["ProductVersion"] = "1.5.0", ["LABELFRAME_MIN_SERVER_VERSION"] = "1.0.0",
        ["LABELFRAME_MAX_SERVER_VERSION"] = "2.0.0", ["LABELFRAME_RUNTIME_MAJOR"] = "10",
    };
    private static ClientUpdatePackage Validate(Dictionary<string, string> properties)
        => ClientPackageMetadata.Validate(properties, "x64", "LabelFrame-Client-1.5.0.msi", "pub", "hash", 1);
}
