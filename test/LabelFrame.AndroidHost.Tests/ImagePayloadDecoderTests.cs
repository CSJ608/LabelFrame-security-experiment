using LabelFrame.AndroidHost.Api;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

/// <summary>ImagePayloadDecoder（纯托管，按文件链接编译进本工程——AndroidHost 整工程
/// net10.0-android 不可被 net10.0 测试工程引用）的 base64 解码行为测试；
/// 渲染传参链路依赖 Android Graphics，由 CI Android 构建 + 真机冒烟覆盖（本工程不可挂载）。</summary>
public class ImagePayloadDecoderTests
{
    [Fact]
    public void Decode_should_map_base64_values_to_bytes()
    {
        var decoded = ImagePayloadDecoder.Decode(new Dictionary<string, string>
        {
            ["a.png"] = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            ["b.jpg"] = Convert.ToBase64String(new byte[] { 255 }),
        });

        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded["a.png"]);
        Assert.Equal(new byte[] { 255 }, decoded["b.jpg"]);
    }

    [Fact]
    public void Decode_null_should_pass_through_null()
    {
        Assert.Null(ImagePayloadDecoder.Decode(null));
    }

    [Fact]
    public void Decode_invalid_base64_should_throw_for_submission_failure_channel()
    {
        // SubmissionService catch FormatException → 提交失败「模板图片 base64 无效。」
        Assert.Throws<FormatException>(() => ImagePayloadDecoder.Decode(new Dictionary<string, string> { ["bad.png"] = "!!!not-base64!!!" }));
    }
}
