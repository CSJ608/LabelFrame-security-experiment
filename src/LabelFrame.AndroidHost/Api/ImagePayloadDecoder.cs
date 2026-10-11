namespace LabelFrame.AndroidHost.Api;

/// <summary>
/// 作业载荷图片 base64 解码（迭代 132，决策 #184）：纯托管（无 Android 依赖，可被 net10.0 测试工程
/// 按文件链接编译——AndroidHost 整工程为 net10.0-android 不可被引用）。坏 base64 抛
/// <see cref="FormatException"/>，由 SubmissionService 转「模板图片 base64 无效」提交失败。
/// </summary>
public static class ImagePayloadDecoder
{
    /// <summary>解码键 → base64 字典为键 → 原始字节字典；null 透传 null（无附带图）。</summary>
    public static IReadOnlyDictionary<string, byte[]>? Decode(IReadOnlyDictionary<string, string>? images)
        => images is null
            ? null
            : images.ToDictionary(kv => kv.Key, kv => Convert.FromBase64String(kv.Value));
}
