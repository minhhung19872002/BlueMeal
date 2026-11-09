using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BlueMeal.Api.Authorization;

/// <summary>Relative permission actions resolved against the controller's <see cref="PermissionModuleAttribute"/>.</summary>
public enum PermissionAction { View, Create, Edit, Delete, Import, Export }

/// <summary>
/// Requires a permission code. Either absolute (<c>[HasPermission("menu.approve")]</c>) or relative to the
/// controller module (<c>[HasPermission(PermissionAction.View)]</c> + <c>[PermissionModule("food")]</c>).
/// Every endpoint must carry this attribute or [AllowAnonymous] — otherwise it is denied (SPEC §8, A-06).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class HasPermissionAttribute : Attribute
{
    public string? Code { get; }
    public PermissionAction? Action { get; }
    public HasPermissionAttribute(string code) { Code = code; }
    public HasPermissionAttribute(PermissionAction action) { Action = action; }
}

/// <summary>Module prefix for relative permissions on generic catalog controllers.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class PermissionModuleAttribute(string module) : Attribute
{
    public string Module { get; } = module;
}

/// <summary>Endpoint usable with a restricted token (password change / MFA setup pending).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowRestrictedAttribute : Attribute;

public static class PermissionResolver
{
    public static string Resolve(HasPermissionAttribute attr, Type? controllerType)
    {
        if (attr.Code is not null) return attr.Code;
        var module = controllerType?.GetCustomAttributes(typeof(PermissionModuleAttribute), true).OfType<PermissionModuleAttribute>().FirstOrDefault()?.Module
                     ?? throw new InvalidOperationException($"{controllerType?.Name} uses relative permissions without [PermissionModule]");
        return $"{module}.{attr.Action!.Value.ToString().ToLowerInvariant()}";
    }
}

/// <summary>
/// Runs after routing + authentication. Enforces deny-by-default and permission checks using <see cref="ICurrentUser"/>.
/// </summary>
public sealed class PermissionEnforcementMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ICurrentUser user)
    {
        var endpoint = ctx.GetEndpoint();
        if (endpoint is null || ctx.Request.Path.StartsWithSegments("/health") || ctx.Request.Path.StartsWithSegments("/swagger") || !ctx.Request.Path.StartsWithSegments("/api"))
        {
            await next(ctx);
            return;
        }

        var md = endpoint.Metadata;
        if (md.GetMetadata<IAllowAnonymous>() is not null) { await next(ctx); return; }

        var perms = md.GetOrderedMetadata<HasPermissionAttribute>();
        var controllerType = md.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()?.ControllerTypeInfo.AsType();
        if (perms.Count == 0)
        {
            // Deny by default: endpoint without explicit permission metadata
            await Problem(ctx, StatusCodes.Status403Forbidden, "error.forbidden");
            return;
        }
        if (!user.IsAuthenticated) { await Problem(ctx, StatusCodes.Status401Unauthorized, "error.unauthorized"); return; }

        if (ctx.User.FindFirst("scope")?.Value == "restricted" && md.GetMetadata<AllowRestrictedAttribute>() is null)
        {
            await Problem(ctx, StatusCodes.Status403Forbidden, "auth.mustChangePassword", "restricted_session");
            return;
        }

        // Method-level attributes take precedence over class-level ones; all method-level must pass.
        var methodLevel = perms.Where(p => p.Action is not null || p.Code is not null).ToList();
        foreach (var p in methodLevel)
        {
            var code = PermissionResolver.Resolve(p, controllerType);
            if (code == Permissions.Self) continue;
            if (!user.Has(code)) { await Problem(ctx, StatusCodes.Status403Forbidden, "error.forbidden"); return; }
        }
        await next(ctx);
    }

    private static Task Problem(HttpContext ctx, int status, string key, string? code = null)
        => Errors.ProblemWriter.WriteAsync(ctx, status, key, code: code);
}

/// <summary>Satisfies [Authorize] semantics without policies — actual checks happen in <see cref="PermissionEnforcementMiddleware"/>.</summary>
public static class AuthorizationSetup
{
    public static IServiceCollection AddBlueMealAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        return services;
    }
}
