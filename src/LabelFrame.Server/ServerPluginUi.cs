namespace LabelFrame.Server;

/// <summary>服务端管理界面插件：静态前端包目录。中间件每次请求运行时检测目录存在——放进去即托管、移除即恢复无头，无需重启。</summary>
public static class ServerPluginUi
{
    /// <summary>插件是否启用：WebUiPath 非空、目录存在且包含 index.html（空目录仍视为无头）。</summary>
    public static bool IsEnabled(string? webUiPath)
        => !string.IsNullOrWhiteSpace(webUiPath)
           && Directory.Exists(webUiPath)
           && File.Exists(Path.Combine(webUiPath!, "index.html"));

    /// <summary>
    /// 解析 SPA fallback 的 index.html：仅非 /api/* 与 /healthz 的路径回退；
    /// 插件未启用或 index.html 不存在返回 null（保持 404）。
    /// </summary>
    public static string? ResolveIndexFile(string? webUiPath, string path)
    {
        if (!IsEnabled(webUiPath) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.Equals("/healthz", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.Combine(webUiPath!, "index.html");
    }
}