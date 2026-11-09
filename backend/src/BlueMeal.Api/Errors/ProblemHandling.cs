using System.Diagnostics;
using BlueMeal.Application.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueMeal.Api.Errors;

/// <summary>Writes RFC 7807 problem+json with traceId, a stable error code and localized detail (SPEC §8).</summary>
public static class ProblemWriter
{
    public static ProblemDetails Create(HttpContext ctx, int status, string key, object[]? args = null, IDictionary<string, string[]>? errors = null, string? code = null)
    {
        var traceId = Activity.Current?.Id ?? ctx.TraceIdentifier;
        var pd = errors is null ? new ProblemDetails() : new ValidationProblemDetails(errors);
        pd.Status = status;
        pd.Title = L.T(status switch
        {
            400 or 422 => "error.validation",
            401 => "error.unauthenticated",
            403 => "error.forbidden",
            404 => "error.notFound",
            409 => "error.concurrency",
            429 => "error.rateLimited",
            _ => "error.internal",
        }, args ?? [traceId]);
        pd.Detail = L.T(key, args ?? []);
        pd.Type = $"https://bluemeal.local/errors/{code ?? key}";
        pd.Instance = ctx.Request.Path;
        pd.Extensions["traceId"] = traceId;
        pd.Extensions["code"] = code ?? key;
        return pd;
    }

    public static async Task WriteAsync(HttpContext ctx, int status, string key, object[]? args = null, IDictionary<string, string[]>? errors = null, string? code = null)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        var pd = Create(ctx, status, key, args, errors, code);
        await ctx.Response.WriteAsJsonAsync(pd, pd.GetType(), options: null, contentType: "application/problem+json");
    }
}

/// <summary>
/// Converts expected business/validation exceptions to problem responses before they reach the framework
/// exception handler (which would log them as unhandled errors). Unexpected exceptions are re-thrown.
/// </summary>
public sealed class ExpectedExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (Exception ex) when (GlobalExceptionHandler.IsExpected(ex) && !ctx.Response.HasStarted)
        {
            await GlobalExceptionHandler.HandleKnownAsync(ctx, ex);
        }
    }
}

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log) : IExceptionHandler
{
    public static bool IsExpected(Exception ex) => ex is ValidationException or AppException or DbUpdateConcurrencyException or BadHttpRequestException
        || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation } };

    public static Task HandleKnownAsync(HttpContext ctx, Exception ex) => new GlobalExceptionHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(ctx, ex, ctx.RequestAborted).AsTask();

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        switch (ex)
        {
            case ValidationException ve:
                var errors = ve.Errors.GroupBy(e => ToCamel(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => L.Has(e.ErrorMessage) ? L.T(e.ErrorMessage) : e.ErrorMessage).ToArray());
                await ProblemWriter.WriteAsync(ctx, 400, "error.validation", errors: errors);
                return true;
            case AppException ae:
                await ProblemWriter.WriteAsync(ctx, ae.StatusCode, ae.Key, ae.Args, ae.Errors);
                return true;
            case DbUpdateConcurrencyException:
                await ProblemWriter.WriteAsync(ctx, 409, "error.concurrency");
                return true;
            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg }:
                await ProblemWriter.WriteAsync(ctx, 409, "error.duplicateCode", [pg.ConstraintName ?? ""]);
                return true;
            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } }:
                await ProblemWriter.WriteAsync(ctx, 409, "error.inUse");
                return true;
            case BadHttpRequestException bad:
                await ProblemWriter.WriteAsync(ctx, bad.StatusCode, "error.validation");
                return true;
            case OperationCanceledException when ctx.RequestAborted.IsCancellationRequested:
                return true;
            default:
                log.LogError(ex, "Unhandled exception for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                var traceId = Activity.Current?.Id ?? ctx.TraceIdentifier;
                await ProblemWriter.WriteAsync(ctx, 500, "error.internal", [traceId]);
                return true;
        }
    }

    private static string ToCamel(string s)
        => string.Join('.', s.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
