using System.Text.Json;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlueMeal.Api.Infrastructure;

/// <summary>
/// Honors the <c>Idempotency-Key</c> header on document create/post endpoints (SPEC §8): the first response (2xx) is stored for
/// 24 h per user + key + route and replayed for retries; a concurrent duplicate gets 409. Requests without the header run normally.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private sealed record Stored(int Status, JsonElement? Body);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(key)) { await next(); return; }
        if (key.Length > 100) throw new AppException("error.validation");
        var cache = http.RequestServices.GetRequiredService<ICacheService>();
        var user = http.RequestServices.GetRequiredService<ICurrentUser>().UserId?.ToString() ?? "anon";
        var cacheKey = $"idem:{user}:{http.Request.Path.Value?.ToLowerInvariant()}:{key}";
        var ct = http.RequestAborted;

        var stored = await cache.GetAsync<Stored>(cacheKey, ct);
        if (stored is not null)
        {
            http.Response.Headers["Idempotent-Replayed"] = "true";
            context.Result = stored.Body is { } b ? new ObjectResult(b) { StatusCode = stored.Status } : new StatusCodeResult(stored.Status);
            return;
        }
        if (!await cache.TrySetAsync(cacheKey + ":lock", "1", TimeSpan.FromMinutes(2), ct)) throw new ConflictException("stock.idempotencyConflict");
        try
        {
            var executed = await next();
            if (executed.Exception is null || executed.ExceptionHandled)
            {
                var (status, body) = executed.Result switch
                {
                    ObjectResult o => (o.StatusCode ?? 200, (JsonElement?)JsonSerializer.SerializeToElement(o.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
                    StatusCodeResult s => (s.StatusCode, null),
                    _ => (0, null),
                };
                if (status is >= 200 and < 300) await cache.SetAsync(cacheKey, new Stored(status, body), Ttl, ct);
            }
        }
        finally
        {
            await cache.RemoveAsync(cacheKey + ":lock", CancellationToken.None);
        }
    }
}
