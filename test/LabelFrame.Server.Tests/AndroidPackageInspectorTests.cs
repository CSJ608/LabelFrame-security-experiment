using LabelFrame.Updates;

namespace LabelFrame.Server.Tests;

/// <summary>验证 APK 真实元数据解析与部署前置的拒绝边界。</summary>
public sealed class AndroidPackageInspectorTests
{
    private const string Badging = "package: name='com.labelframe.androidhost' versionCode='10502' versionName='1.5.2'\nminSdkVersion:'23'\ntargetSdkVersion:'36'\nnative-code: 'arm64-v8a'\n";
    private const string Tree = "    A: http://schemas.android.com/apk/res/android:versionCode(0x0101021b)=10502\n";

    [Fact]
    public void Long_version_code_is_composed_from_manifest_high_and_low_values()
    {
        var package = Parse(Badging, Tree + "    A: http://schemas.android.com/apk/res/android:versionCodeMajor(0x01010576)=2\n");
        Assert.Equal((2L << 32) | 10502, package.VersionCode);
        Assert.Equal("1.5.2", package.VersionName);
        Assert.Equal(new[] { "arm64-v8a" }, package.Abis);
    }

    [Theory]
    [InlineData("product")]
    [InlineData("debug")]
    [InlineData("split")]
    [InlineData("abi")]
    [InlineData("protocol")]
    [InlineData("range")]
    [InlineData("high")]
    public void Invalid_product_metadata_is_rejected(string kind)
    {
        var text = kind switch
        {
            "product" => Badging.Replace("com.labelframe.androidhost", "com.example.fake", StringComparison.Ordinal),
            "debug" => Badging + "application-debuggable\n",
            "split" => Badging.Replace("versionCode=", "split='config.arm64' versionCode=", StringComparison.Ordinal),
            "abi" => Badging.Replace("arm64-v8a", "unknown", StringComparison.Ordinal),
            _ => Badging
        };
        var metadata = kind switch { "protocol" => new(2, "1.0.0", "2.0.0"), "range" => new(1, "2.0.0", "1.0.0"), _ => new AndroidUpdateMetadata(1, "1.5.1", "2.0.0") };
        Assert.Throws<InvalidDataException>(() => AndroidPackageInspector.ParseMetadata(text,
            kind == "high" ? Tree + "    A: http://schemas.android.com/apk/res/android:versionCodeMajor(0x01010576)=-1\n" : Tree,
            metadata, new string('A', 64), new string('B', 64), 3));
    }

    [Fact]
    public async Task Missing_trust_and_missing_tools_fail_closed_before_inspection()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => new AndroidPackageInspector(null, null, null, null).InspectAsync("missing.apk"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AndroidPackageInspector(null, null, null, new string('A', 64)).InspectAsync("missing.apk"));
    }

    private static AndroidUpdatePackage Parse(string badging, string tree) => AndroidPackageInspector.ParseMetadata(badging, tree,
        new(1, "1.5.1", "2.0.0"), new string('A', 64), new string('B', 64), 3);
}
