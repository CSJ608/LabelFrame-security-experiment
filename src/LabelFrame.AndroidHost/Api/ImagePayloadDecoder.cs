namespace LabelFrame.AndroidHost.Api;

/// <summary>作业载荷图片 base64 解码。保持纯托管（无 Android 依赖）——须可被 net10.0 测试工程
/// 按文件链接编译（AndroidHost 整工程为 net10.0-android 不可被引用）；坏 base64 抛 FormatException。</summary>
public static class ImagePayloadDecoder
{
    /// <summary>解码键 → base64 字典为键 → 原始字节字典；null 透传 null（无附带图）。</summary>
    public static IReadOnlyDictionary<string, byte[]>? Decode(IReadOnlyDictionary<string, string>? images)
        => images is null
            ? null
            : images.ToDictionary(kv => kv.Key, kv => Convert.FromBase64String(kv.Value));
}
