using System.Net;
using System.Security.Claims;
using LabelFrame.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LabelFrame.Server.Security;

/// <summary>提供 Server 专属管理员认证、来源与防伪边界。</summary>
public static class AdminSecurity
{
    public const string Scheme = "LabelFrame.Admin";
    public const string CsrfHeader = "X-LabelFrame-CSRF";
    private static readonly object TransportKey = new();

    public static IServiceCollection AddAdminSecurity(this IServiceCollection services, string? passwordHash)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(new AdminCredential(passwordHash));
        services.AddSingleton<AdminTicketStore>();
        services.AddSingleton<AdminLoginLimiter>();
        services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();
        services.AddDataProtection();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeader;
            options.Cookie.Name = "LabelFrame.Admin.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "LabelFrame.Admin.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = c => RejectAsync(c.HttpContext, 401, "请先登录管理员。");
            options.Events.OnRedirectToAccessDenied = c => RejectAsync(c.HttpContext, 403, "无权执行此操作。");
        });
        services.AddOptions<CookieAuthenticationOptions>(Scheme)
            .Configure<AdminTicketStore, TimeProvider>((options, store, clock) =>
            {
                options.SessionStore = store;
                options.TimeProvider = clock;
            });
        return services;
    }

    public static IApplicationBuilder CaptureAdminTransport(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var peer = context.Connection.RemoteIpAddress;
        var forwarded = context.Request.Headers.Keys.Any(key => key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase));
        context.Items[TransportKey] = context.Request.IsHttps ||
            (!forwarded && peer is not null && IPAddress.IsLoopback(peer));
        await next(context);
    });

    public static IApplicationBuilder UseAdminBoundary(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        // 静态资源不含业务数据；所有选中 API 由路由模板分类，未知 API 默认保护。
        if (!context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }
        var access = ServerEndpointAccess.Classify(context);
        if (access is ServerAccess.Public or ServerAccess.Device or ServerAccess.Integration)
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        if (context.Items[TransportKey] is not true || !IsSameOrigin(context.Request))
        {
            await RejectAsync(context, 403, "管理访问需要可信 HTTPS（直连回环除外）及同源请求。");
            return;
        }
        if (access == ServerAccess.Admin && context.User.Identity?.IsAuthenticated != true)
        {
            await RejectAsync(context, 401, "请先登录管理员。");
            return;
        }
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                await RejectAsync(context, 403, "请求验证失败，请刷新后重试。");
                return;
            }
        }
        await next(context);
    });

    public static void MapAdminSecurity(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/session", (HttpContext context, IAntiforgery antiforgery, AdminCredential credential) =>
            Results.Ok(new
            {
                authenticated = context.User.Identity?.IsAuthenticated == true,
                configured = !string.IsNullOrWhiteSpace(credential.PasswordHash),
                csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken,
            }));
        app.MapPost("/api/auth/login", async (HttpContext context, AdminLoginRequest request,
            AdminCredential credential, IPasswordHasher<string> hasher, AdminLoginLimiter limiter) =>
        {
            if (!limiter.TryAcquire())
            {
                context.Response.Headers.RetryAfter = "60";
                return Error(429, "登录尝试过多，请稍后重试。");
            }
            if (request.Password is not { Length: > 0 and <= 1024 } || string.IsNullOrWhiteSpace(credential.PasswordHash))
                return Error(401, "管理员凭据无效或尚未配置。");
            PasswordVerificationResult verified;
            try { verified = hasher.VerifyHashedPassword("admin", credential.PasswordHash, request.Password); }
            catch (FormatException) { verified = PasswordVerificationResult.Failed; }
            if (verified == PasswordVerificationResult.Failed) return Error(401, "管理员凭据无效或尚未配置。");
            // 同一已登录会话不重新签入，避免框架复用正在撤销的票据键。
            if (context.User.Identity?.IsAuthenticated == true) return Error(409, "请先退出当前会话。");
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin"), new Claim(ClaimTypes.Name, "admin")], Scheme);
            try
            {
                await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });
            }
            catch (AdminSessionCapacityException)
            {
                context.Response.Headers.RetryAfter = "60";
                return Error(503, "管理员会话已满，请退出其他会话或等待会话过期后重试。");
            }
            return Results.Ok(new { authenticated = true });
        }).WithMetadata(new RequestSizeLimitAttribute(8192));
        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme);
            return Results.Ok(new { authenticated = false });
        });
    }

    public static bool IsSameOrigin(HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0 && site is not ("same-origin" or "none")) return false;
        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        return string.Equals(uri.GetLeftPart(UriPartial.Authority), $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult Error(int status, string message) => Results.Json(
        new ErrorView("LF_AUTH_DENIED", message), statusCode: status);
    private static Task RejectAsync(HttpContext context, int status, string message) => Error(status, message).ExecuteAsync(context);

    public sealed record AdminCredential(string? PasswordHash);
    public sealed record AdminLoginRequest(string? Password);
}
