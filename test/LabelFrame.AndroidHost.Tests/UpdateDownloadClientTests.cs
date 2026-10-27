using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LabelFrame.AndroidHost.Updates;
using LabelFrame.Updates;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public sealed class UpdateDownloadClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LabelFrame-download-test-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Bytes = [1, 2, 3, 4];
    private static readonly string Signer = new('A', 64);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static AndroidUpdateQuery Identity => new("pda", 100, 30, "arm64-v8a", Signer);
    private static AndroidUpdateRelease Release => new(new string('a', 32), new(AndroidUpdateContract.PackageName, 101, "1.5.2", 23, 36,
        ["arm64-v8a"], Signer, Convert.ToHexString(SHA256.HashData(Bytes)), Bytes.Length, new(1, "1.5.1", "2.0.0")), ["pda"], false, DateTimeOffset.UtcNow);
    private static HttpResponseMessage Response(byte[]? bytes = null) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes ?? Bytes) };
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public async Task Verified_download_uses_fixed_origin_and_subpath_and_atomic_private_file()
    {
        var release = Release;
        var inspector = new Inspector(release.Package);
        using var client = new UpdateDownloadClient("https://example.test/prefix", Identity, new Handler(request =>
        {
            Assert.Equal("example.test", request.RequestUri!.Host);
            Assert.Equal("/prefix/api/android-updates/" + release.Id + "/package", request.RequestUri.AbsolutePath);
            return Response();
        }));
        var file = await client.DownloadAsync(release, _directory, inspector, default);
        Assert.Equal(Bytes, File.ReadAllBytes(file));
        Assert.False(File.Exists(Path.Combine(_directory, "candidate.partial")));
        Assert.Equal(1, inspector.Calls);
    }

    [Fact]
    public async Task Administrator_check_reuses_original_management_endpoint_without_persisting_token()
    {
        using var client = new UpdateDownloadClient("https://example.test/prefix", Identity, new Handler(request =>
        {
            Assert.Equal("/prefix/api/android-updates", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }));
        await client.VerifyAdministratorAsync("test-token", default);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task Administrator_failure_or_redirect_never_authorizes_recovery(HttpStatusCode code)
    {
        using var client = new UpdateDownloadClient("https://example.test", Identity, new Handler(_ => new(code)));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.VerifyAdministratorAsync("test-token", default));
    }

    [Fact]
    public async Task Administrator_token_never_sent_over_remote_plaintext()
    {
        using var client = new UpdateDownloadClient("http://example.test", Identity, new Handler(_ => throw new InvalidOperationException("不得发送")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VerifyAdministratorAsync("test-token", default));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public async Task Short_or_oversized_body_never_reaches_inspector(int size)
    {
        var release = Release;
        var inspector = new Inspector(release.Package);
        using var client = new UpdateDownloadClient("https://example.test", Identity, new Handler(_ =>
        {
            var response = Response(new byte[size]);
            response.Content.Headers.ContentLength = release.Package.SizeBytes;
            return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, _directory, inspector, default));
        Assert.Equal(0, inspector.Calls);
        Assert.False(File.Exists(Path.Combine(_directory, "candidate.partial")));
        Assert.False(File.Exists(Path.Combine(_directory, "candidate.apk")));
    }

    [Fact]
    public async Task Wrong_digest_rejected_before_signature_inspector()
    {
        var inspector = new Inspector(Release.Package);
        using var client = new UpdateDownloadClient("https://example.test", Identity, new Handler(_ => Response([4, 3, 2, 1])));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release, _directory, inspector, default));
        Assert.Equal(0, inspector.Calls);
    }

    [Fact]
    public async Task Real_metadata_mismatch_rejected_after_inspection()
    {
        var inspector = new Inspector(Release.Package with { VersionCode = 999 });
        using var client = new UpdateDownloadClient("https://example.test", Identity, new Handler(_ => Response()));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release, _directory, inspector, default));
        Assert.Equal(1, inspector.Calls);
        Assert.False(File.Exists(Path.Combine(_directory, "candidate.apk")));
    }

    [Fact]
    public async Task Signature_inspection_failure_never_publishes_cache()
    {
        var inspector = new Inspector(Release.Package, true);
        using var client = new UpdateDownloadClient("https://example.test", Identity, new Handler(_ => Response()));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release, _directory, inspector, default));
        Assert.False(File.Exists(Path.Combine(_directory, "candidate.apk")));
    }

    [Fact]
    public async Task Redirect_is_not_accepted_as_package()
    {
        using var client = new UpdateDownloadClient("https://example.test", Identity, new Handler(_ => new(HttpStatusCode.Redirect)));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(Release, _directory, new Inspector(Release.Package), default));
    }

    [Theory]
    [InlineData("downgrade")]
    [InlineData("signature")]
    [InlineData("abi")]
    [InlineData("device")]
    [InlineData("paused")]
    [InlineData("path")]
    [InlineData("silent")]
    public async Task Ineligible_candidate_rejected_without_network(string fault)
    {
        var release = Release;
        release = fault switch
        {
            "downgrade" => release with { Package = release.Package with { VersionCode = 100 } },
            "signature" => release with { Package = release.Package with { PublisherSha256 = new string('B', 64) } },
            "abi" => release with { Package = release.Package with { Abis = ["x86"] } },
            "device" => release with { Devices = ["other"] },
            "paused" => release with { Paused = true },
            "path" => release with { Id = "../untrusted" },
            _ => release with { Mode = "silent" }
        };
        using var client = new UpdateDownloadClient("https://example.test", Identity, new Handler(_ => throw new InvalidOperationException("网络不应被调用")));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, _directory, new Inspector(release.Package), default));
    }

    [Theory]
    [InlineData("1.5.1", true)]
    [InlineData("2.0.0", false)]
    [InlineData("1.0.0", false)]
    public async Task Candidate_checks_actual_server_version(string version, bool accepted)
    {
        using var client = new UpdateDownloadClient("https://example.test/prefix", Identity, new Handler(request =>
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("/server/info", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { version }) : JsonSerializer.Serialize(new { release = Release }, Json);
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }));
        if (accepted) Assert.NotNull(await client.CandidateAsync(default));
        else await Assert.ThrowsAsync<InvalidDataException>(() => client.CandidateAsync(default));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
    private sealed class Inspector(AndroidUpdatePackage package, bool fail = false) : IAndroidUpdatePackageInspector
    {
        public int Calls { get; private set; }
        public Task<AndroidUpdatePackage> InspectAsync(string path, CancellationToken ct = default)
        {
            Calls++;
            if (fail) throw new InvalidDataException("APK 内容验签失败");
            return Task.FromResult(package);
        }
    }
}
