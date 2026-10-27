using LabelFrame.Updates;

namespace LabelFrame.Server.Tests;

/// <summary>使用外部实际 APK 和官方工具链验证签名完整性。</summary>
public sealed class AndroidApkVerificationTests
{
    [RealApkFact]
    public async Task Real_non_debug_host_apk_is_verified_and_wrong_trust_or_modified_bytes_are_rejected()
    {
        var apk = Environment.GetEnvironmentVariable("LABELFRAME_TEST_ANDROID_APK")!;
        var fingerprint = Environment.GetEnvironmentVariable("LABELFRAME_ANDROID_PUBLISHER_SHA256")!;
        var inspector = Create(fingerprint);
        var package = await inspector.InspectAsync(apk);
        Assert.Equal(AndroidUpdateContract.PackageName, package.PackageName);
        Assert.True(package.VersionCode > 0);
        Assert.Contains("arm64-v8a", package.Abis);
        Assert.Equal(1, package.Compatibility.Protocol);
        Assert.Equal(new FileInfo(apk).Length, package.SizeBytes);
        Assert.Equal(fingerprint.ToUpperInvariant(), package.PublisherSha256);
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(new string('0', 64)).InspectAsync(apk));
        var directory = Path.Combine(Path.GetTempPath(), "lfapkverify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var damaged = Path.Combine(directory, "damaged.apk");
            File.Copy(apk, damaged);
            using (var file = File.Open(damaged, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                file.Position = 100;
                var original = file.ReadByte();
                file.Position = 100;
                file.WriteByte((byte)(original ^ 0x80));
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => inspector.InspectAsync(damaged));
            using (var file = File.OpenWrite(damaged)) file.SetLength(1024);
            await Assert.ThrowsAsync<InvalidDataException>(() => inspector.InspectAsync(damaged));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static AndroidPackageInspector Create(string fingerprint) => new(
        Environment.GetEnvironmentVariable("LABELFRAME_ANDROID_JAVA"),
        Environment.GetEnvironmentVariable("LABELFRAME_ANDROID_APKSIGNER_JAR"),
        Environment.GetEnvironmentVariable("LABELFRAME_ANDROID_AAPT2"), fingerprint);
}

/// <summary>仅在显式提供真实 APK 和官方工具链时启用验签集成测试。</summary>
public sealed class RealApkFactAttribute : FactAttribute
{
    public RealApkFactAttribute()
    {
        if (new[] { "LABELFRAME_TEST_ANDROID_APK", "LABELFRAME_ANDROID_JAVA", "LABELFRAME_ANDROID_APKSIGNER_JAR",
                "LABELFRAME_ANDROID_AAPT2", "LABELFRAME_ANDROID_PUBLISHER_SHA256" }.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "需要显式提供真实非调试宿主 APK、可信指纹和 Android 官方工具链；普通测试不替代此验收。";
    }
}
