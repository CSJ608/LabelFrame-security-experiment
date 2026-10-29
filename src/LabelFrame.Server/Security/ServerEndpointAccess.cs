using Microsoft.AspNetCore.Routing;

namespace LabelFrame.Server.Security;

/// <summary>定义 Server 端点的安全域。</summary>
public enum ServerAccess { Admin, Public, Authentication, Device, Integration }

/// <summary>按实际匹配的路由和方法分类 Server 访问权限。</summary>
public static class ServerEndpointAccess
{
    private static readonly Dictionary<string, ServerAccess> Exceptions = CreateExceptions();

    public static ServerAccess Classify(string method, string? route)
    {
        var key = $"{method} {route?.TrimEnd('/')}";
        return Exceptions.GetValueOrDefault(key, ServerAccess.Admin);
    }

    public static ServerAccess Classify(HttpContext context) =>
        Classify(context.Request.Method, (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText);

    private static Dictionary<string, ServerAccess> CreateExceptions()
    {
        var entries = new Dictionary<string, ServerAccess>(StringComparer.OrdinalIgnoreCase);
        void Add(ServerAccess access, string method, params string[] routes)
        {
            foreach (var route in routes) entries.Add($"{method} {route}", access);
        }

        Add(ServerAccess.Public, "GET", "/healthz", "/api/server/info",
            "/api/client-packages", "/api/client-packages/{fileName}",
            "/api/pda-packages", "/api/pda-packages/{fileName}");
        Add(ServerAccess.Authentication, "GET", "/api/auth/session");
        Add(ServerAccess.Authentication, "POST", "/api/auth/login");
        Add(ServerAccess.Device, "POST", "/api/devices", "/api/logs",
            "/api/devices/{deviceId}/jobs/{jobId}/result", "/api/devices/{deviceId}/jobs/{jobId}/progress");
        Add(ServerAccess.Device, "GET", "/api/devices/{deviceId}/jobs/notify",
            "/api/devices/{deviceId}/jobs/pending", "/api/plugin-packages", "/api/plugin-packages/{fileName}");
        Add(ServerAccess.Integration, "POST", "/api/jobs");
        Add(ServerAccess.Integration, "GET", "/api/jobs/{jobId}", "/api/devices/by-ip/{ip}");
        foreach (var platform in new[] { "client-updates", "android-updates" })
        {
            Add(ServerAccess.Device, "GET", $"/api/{platform}/candidate", $"/api/{platform}/{{id}}/package");
            Add(ServerAccess.Device, "POST", $"/api/{platform}/status");
        }
        return entries;
    }
}
