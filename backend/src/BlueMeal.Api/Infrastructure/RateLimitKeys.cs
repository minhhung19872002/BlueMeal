using System.Security.Claims;

namespace BlueMeal.Api.Infrastructure;

/// <summary>Partition key of the global API rate limiter (SPEC §10.5): per user when authenticated, otherwise per IP.</summary>
public static class RateLimitKeys
{
    public static string ForRequest(HttpContext ctx)
    {
        // the JWT handler maps "sub" to ClaimTypes.NameIdentifier — read both, otherwise every user would share one bucket
        var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? ctx.User.FindFirst("sub")?.Value;
        return ctx.User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(userId) ? "u:" + userId : "ip:" + ctx.Connection.RemoteIpAddress;
    }
}
