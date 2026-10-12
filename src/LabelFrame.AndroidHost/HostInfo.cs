namespace LabelFrame.AndroidHost;

/// <summary>应用版本（android:versionName）读取，供配置页与本地 HTTP 展示（升级确认与排障）。</summary>
public static class HostInfo
{
    /// <summary>取当前安装版本；进程生命周期内不变。</summary>
    public static string GetVersion(Android.Content.Context context)
    {
        try
        {
            return context.PackageManager?.GetPackageInfo(context.PackageName ?? string.Empty, 0)?.VersionName
                ?? "未知";
        }
        catch (Android.Content.PM.PackageManager.NameNotFoundException)
        {
            return "未知";
        }
    }
}
