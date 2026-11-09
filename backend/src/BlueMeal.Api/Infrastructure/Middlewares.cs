using BlueMeal.Api.Auth;
using BlueMeal.Api.Errors;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Settings;
using BlueMeal.Infrastructure.Identity;

namespace BlueMeal.Api.Infrastructure;

/// <summary>Validates the session (revocation, idle timeout) and loads permissions/data scopes for the request.</summary>
public sealed class SessionValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, AuthService auth, PermissionService permissions)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var sid = ctx.User.FindFirst("sid")?.Value;
            var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? ctx.User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(sid, out var sessionId) || !Guid.TryParse(sub, out var userId) || !await auth.ValidateSessionAsync(sessionId, ctx.RequestAborted))
            {
                await ProblemWriter.WriteAsync(ctx, StatusCodes.Status401Unauthorized, "auth.sessionRevoked", code: "session_revoked");
                return;
            }
            ctx.Items[HttpCurrentUser.AccessItemKey] = await permissions.GetAsync(userId, ctx.RequestAborted);
        }
        await next(ctx);
    }
}

/// <summary>Security headers (SPEC §10.5). Nginx sets the same for static files; API sets its own for defence in depth.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(), payment=(), usb=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "same-origin";
        if (!ctx.Request.Path.StartsWithSegments("/swagger"))
            h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        if (ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
        return next(ctx);
    }
}

/// <summary>Maintenance mode: only admins (system.setting_edit) and auth/health endpoints pass.</summary>
public sealed class MaintenanceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ISettingsService settings, ICurrentUser user)
    {
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/v1/auth") && !path.StartsWithSegments("/api/v1/system/status"))
        {
            var m = await settings.GetAsync<MaintenanceSettings>(SettingKeys.Maintenance, ctx.RequestAborted);
            if (m.Enabled && !user.Has(Application.Authorization.Permissions.SystemInfo.SettingEdit))
            {
                await ProblemWriter.WriteAsync(ctx, StatusCodes.Status503ServiceUnavailable, "error.maintenance", code: "maintenance");
                return;
            }
        }
        await next(ctx);
    }
}
