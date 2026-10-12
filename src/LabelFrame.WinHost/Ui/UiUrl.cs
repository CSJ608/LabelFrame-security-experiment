namespace LabelFrame.WinHost.Ui;

/// <summary>界面地址规则：监听地址 → 本机 UI 地址、窗口内导航边界判定。</summary>
public static class UiUrl
{
    /// <summary>本地 UI 打开地址：通配监听（0.0.0.0 / * / + / [::] / :）规范化为 127.0.0.1，避免界面跳到 0.0.0.0。</summary>
    public static string ToLocalUiUrl(string listenUrl)
    {
        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var uri))
        {
            return listenUrl;
        }

        if (uri.Host is "0.0.0.0" or "*" or "+" or "::" or "[::]")
        {
            var builder = new UriBuilder(uri) { Host = "127.0.0.1" };
            return builder.Uri.ToString();
        }

        return listenUrl;
    }

    /// <summary>窗口内导航边界：本机地址（127.0.0.1 / localhost / [::1]）留在窗口内，其余（如帮助 / 下载外链）交给系统浏览器。</summary>
    public static bool IsLocalNavigation(Uri uri)
    {
        return uri.IsLoopback;
    }
}
