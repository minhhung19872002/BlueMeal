using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using BlueMeal.Application.Abstractions;
using BlueMeal.Infrastructure.Identity;

namespace BlueMeal.Api.Infrastructure;

/// <summary>Request-scoped user context. Permissions/scopes are filled by <see cref="SessionValidationMiddleware"/>.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    internal const string AccessItemKey = "bm.access";
    private HttpContext? Ctx => accessor.HttpContext;
    private UserAccess? Access => Ctx?.Items[AccessItemKey] as UserAccess;

    /// <summary>Set by background jobs / seeding to act as a system user.</summary>
    public Guid? OverrideUserId { get; set; }

    public bool IsAuthenticated => Ctx?.User.Identity?.IsAuthenticated == true;
    public Guid? UserId => OverrideUserId ?? (Guid.TryParse(Ctx?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Ctx?.User.FindFirstValue("sub"), out var id) ? id : null);
    public string? UserName => Ctx?.User.FindFirstValue(ClaimTypes.Name) ?? Ctx?.User.FindFirstValue("name") ?? (OverrideUserId is null ? null : "system");
    public string? FullName => Ctx?.User.FindFirstValue("full_name");
    public string? Ip => Ctx?.Connection.RemoteIpAddress?.ToString();
    public string? UserAgent => Ctx?.Request.Headers.UserAgent.ToString();
    public string? TraceId => Activity.Current?.Id ?? Ctx?.TraceIdentifier;
    public string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "vi";

    private HashSet<string>? _perms;
    public IReadOnlySet<string> Permissions => _perms ??= Access is null ? [] : new HashSet<string>(Access.Permissions, StringComparer.Ordinal);
    public bool Has(string permission) => Permissions.Contains(permission);

    public IReadOnlySet<Guid>? WarehouseScope => Access?.Warehouses is null ? null : new HashSet<Guid>(Access.Warehouses);
    public IReadOnlySet<Guid>? CustomerScope => Access?.Customers is null ? null : new HashSet<Guid>(Access.Customers);
    public IReadOnlyList<string> Roles => Access?.Roles ?? [];
}
