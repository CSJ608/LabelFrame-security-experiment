using LabelFrame.Bootstrapper.Downloads;
using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>
/// 下载侧失败分类测试（/ ；DESIGN）：引擎 HRESULT →
/// 源不可达 / 网络中断 / 校验失败三分类 + 差异化中文提示（含可操作建议）。
/// </summary>
public sealed class DownloadFailureClassifierTests
{
    [Theory]
    [InlineData(unchecked((int)0x800C0005), DownloadFailureCategory.SourceUnreachable)] // INET_E_RESOURCE_NOT_FOUND（404）
    [InlineData(unchecked((int)0x800C0002), DownloadFailureCategory.SourceUnreachable)] // INET_E_INVALID_URL
    [InlineData(unchecked((int)0x80070002), DownloadFailureCategory.SourceUnreachable)] // ERROR_FILE_NOT_FOUND
    [InlineData(unchecked((int)0x80072EE7), DownloadFailureCategory.SourceUnreachable)] // 域名未解析
    [InlineData(unchecked((int)0x80072EFD), DownloadFailureCategory.SourceUnreachable)] // 无法连接
    [InlineData(unchecked((int)0x80072EE2), DownloadFailureCategory.NetworkInterrupted)] // 响应超时
    [InlineData(unchecked((int)0x80072EFE), DownloadFailureCategory.NetworkInterrupted)] // 连接中止
    [InlineData(unchecked((int)0x80072EFF), DownloadFailureCategory.NetworkInterrupted)] // 连接重置
    [InlineData(unchecked((int)0x800C0008), DownloadFailureCategory.NetworkInterrupted)] // INET_E_DOWNLOAD_FAILURE
    [InlineData(unchecked((int)0x80072746), DownloadFailureCategory.NetworkInterrupted)] // WSAECONNRESET
    [InlineData(unchecked((int)0x8007274D), DownloadFailureCategory.NetworkInterrupted)] // WSAECONNREFUSED
    [InlineData(unchecked((int)0x80091007), DownloadFailureCategory.VerificationFailed)] // CRYPT_E_HASH_VALUE（坏哈希）
    [InlineData(unchecked((int)0x80070005), DownloadFailureCategory.Other)]              // 拒绝访问（未单列）
    [InlineData(unchecked((int)0x80004005), DownloadFailureCategory.Other)]              // E_FAIL
    public void Classify_maps_engine_hresult_to_category(int hresult, DownloadFailureCategory expected)
    {
        Assert.Equal(expected, DownloadFailureClassifier.Classify(hresult));
    }

    [Fact]
    public void Zero_hresult_is_no_failure()
    {
        Assert.Equal(DownloadFailureCategory.None, DownloadFailureClassifier.Classify(0));
    }

    [Fact]
    public void Hash_mismatch_constant_matches_crypt_e_hash_value()
    {
        // Burn 载荷校验失败的典型错误码常量锚定（防手写位移漂移）
        Assert.Equal(unchecked((int)0x80091007), DownloadFailureClassifier.HashMismatchHResult);
    }

    [Theory]
    [InlineData(DownloadFailureCategory.SourceUnreachable, "下载源不可达")]
    [InlineData(DownloadFailureCategory.SourceUnreachable, "镜像源")]
    [InlineData(DownloadFailureCategory.NetworkInterrupted, "网络传输中断或超时")]
    [InlineData(DownloadFailureCategory.NetworkInterrupted, "重试")]
    [InlineData(DownloadFailureCategory.VerificationFailed, "校验失败")]
    [InlineData(DownloadFailureCategory.VerificationFailed, "SHA-256")]
    [InlineData(DownloadFailureCategory.Other, "安装日志")]
    public void Describe_provides_differentiated_actionable_hint(DownloadFailureCategory category, string expectedPhrase)
    {
        var description = DownloadFailureClassifier.Describe(category);

        Assert.Contains(expectedPhrase, description, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_none_is_empty()
    {
        Assert.Equal(string.Empty, DownloadFailureClassifier.Describe(DownloadFailureCategory.None));
    }

    [Fact]
    public void Descriptions_are_distinct_across_categories()
    {
        // 差异化提示：三类核心文案互不相同（不共用模板导致提示无差别）
        var source = DownloadFailureClassifier.Describe(DownloadFailureCategory.SourceUnreachable);
        var network = DownloadFailureClassifier.Describe(DownloadFailureCategory.NetworkInterrupted);
        var verify = DownloadFailureClassifier.Describe(DownloadFailureCategory.VerificationFailed);

        Assert.NotEqual(source, network);
        Assert.NotEqual(network, verify);
        Assert.NotEqual(source, verify);
    }
}
