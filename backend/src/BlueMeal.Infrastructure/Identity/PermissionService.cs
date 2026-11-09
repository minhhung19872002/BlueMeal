using BlueMeal.Application.Abstractions;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Infrastructure.Identity;

public sealed record UserAccess(string[] Permissions, string[] Roles, Guid[]? Warehouses, Guid[]? Customers);

/// <summary>Loads effective permissions and data scopes per user, cached in Redis (region "access").</summary>
public sealed class PermissionService(AppDbContext db, ICacheService cache)
{
    public const string Region = "access";

    public async Task<UserAccess> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var key = await cache.RegionKeyAsync(Region, userId.ToString("N"), ct);
        return await cache.GetOrCreateAsync(key, () => LoadAsync(userId, ct), TimeSpan.FromMinutes(15), ct);
    }

    public async Task<UserAccess> LoadAsync(Guid userId, CancellationToken ct = default)
    {
        var roles = await db.Set<UserRole>().AsNoTracking().Where(ur => ur.UserId == userId && ur.Role!.IsActive)
            .Select(ur => new { ur.RoleId, ur.Role!.Name }).ToListAsync(ct);
        var roleIds = roles.Select(r => r.RoleId).ToList();
        var perms = await db.Set<RolePermission>().AsNoTracking().Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission!.Code).Distinct().ToArrayAsync(ct);
        var scopes = await db.Set<UserDataScope>().AsNoTracking().Where(s => s.UserId == userId).ToListAsync(ct);
        var wh = scopes.Where(s => s.ScopeType == DataScopeType.Warehouse).Select(s => s.ScopeId).ToArray();
        var cu = scopes.Where(s => s.ScopeType == DataScopeType.Customer).Select(s => s.ScopeId).ToArray();
        var roleNames = roles.Select(r => r.Name!).ToArray();
        // Admin-like users with no explicit scope assignment see everything; CUSTOMER_REP with no customers sees nothing.
        var isCustomerRep = roleNames.Contains("CUSTOMER_REP") && !perms.Contains("customer.view");
        return new UserAccess(perms, roleNames, wh.Length == 0 ? null : wh, cu.Length == 0 ? (isCustomerRep ? [] : null) : cu);
    }

    /// <summary>Invalidate cached access for all users (role/permission change).</summary>
    public Task InvalidateAllAsync(CancellationToken ct = default) => cache.InvalidateRegionAsync(Region, ct);
}
