namespace LabelFrame.Server.Security;

/// <summary>筛选可向匿名下载者公开的连接地址。</summary>
public static class ServerPublicInfo
{
    public static string? SafeBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/') : null;
}
