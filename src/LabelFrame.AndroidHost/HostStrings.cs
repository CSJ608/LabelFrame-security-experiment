using System.Globalization;

namespace LabelFrame.AndroidHost;

/// <summary>
/// 资源文案取值辅助（/ ， ⑤）：Activity / Service / 无 UI 上下文共用的
/// <c>GetString</c> 包装；带参模板用 <c>{0}</c> 位置占位（<see cref="string.Format(string,object[])"/>，
/// InvariantCulture——参数为地址 / 计数 / 设备号等技术值，不随语言变复数形态）。
/// </summary>
internal static class HostStrings
{
    public static string L(Android.Content.Context context, int resId) => context.GetString(resId)!;

    public static string L(Android.Content.Context context, int resId, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, context.GetString(resId)!, args);
}
