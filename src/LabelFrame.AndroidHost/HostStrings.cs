using System.Globalization;

namespace LabelFrame.AndroidHost;

/// <summary>资源文案取值辅助（GetString 包装）；带参模板用 {0} 位置占位、InvariantCulture——
/// 参数为地址 / 计数 / 设备号等技术值，不随语言变形态。</summary>
internal static class HostStrings
{
    public static string L(Android.Content.Context context, int resId) => context.GetString(resId)!;

    public static string L(Android.Content.Context context, int resId, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, context.GetString(resId)!, args);
}
