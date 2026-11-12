using BlueMeal.Api.Auth;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Services;

/// <summary>
/// Disables accounts that have not signed in for <see cref="PasswordPolicySettings.InactiveDisableDays"/> days
/// (TCVN 11930:2017 cấp độ 2 — quản lý tài khoản không sử dụng). The inactivity clock starts at the latest of
/// last login, creation and administrator re-activation. The last active system administrator is never disabled.
/// </summary>
public sealed class InactiveAccountsJob(AppDbContext db, ISettingsService settings, AuthService auth, IAuditWriter audit,
    TimeProvider clock, ILogger<InactiveAccountsJob> log) : IAppJob
{
    public string Name => JobNames.InactiveAccounts;

    public async Task ExecuteAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        var policy = await settings.GetAsync<PasswordPolicySettings>(SettingKeys.PasswordPolicy, ct);
        if (policy.InactiveDisableDays <= 0) return;
        var cutoff = clock.GetUtcNow().AddDays(-policy.InactiveDisableDays);

        var stale = await db.Users.Where(u => u.IsActive && !u.IsDeleted
                && (u.LastLoginAt ?? u.CreatedAt) < cutoff
                && (u.ReactivatedAt == null || u.ReactivatedAt < cutoff))
            .Select(u => u.Id).ToListAsync(ct);
        if (stale.Count == 0) return;

        var adminRoleId = await db.Roles.Where(r => r.Name == DefaultRoles.SysAdmin).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var activeAdmins = await db.Set<UserRole>().Where(r => r.RoleId == adminRoleId && r.User!.IsActive && !r.User.IsDeleted)
            .Select(r => r.UserId).Distinct().ToListAsync(ct);

        var disabled = 0;
        foreach (var id in SelectToDisable(stale, activeAdmins))
        {
            var u = await db.Users.FirstAsync(x => x.Id == id, ct);
            u.IsActive = false;
            u.SecurityStamp = Guid.NewGuid().ToString("N");
            u.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            var revoked = await auth.RevokeUserSessionsAsync(id, "account_inactive", null, ct);
            await audit.WriteAsync(AuditAction.Lock, nameof(AppUser), id.ToString(),
                message: $"inactive_{policy.InactiveDisableDays}d; revoked {revoked} sessions", ct: ct);
            disabled++;
        }
        if (disabled < stale.Count) log.LogWarning("{Count} inactive administrator account(s) kept active: last active administrator", stale.Count - disabled);
        log.LogInformation("Disabled {Count} account(s) inactive for more than {Days} days", disabled, policy.InactiveDisableDays);
    }

    /// <summary>Stale accounts to disable, keeping at least one active system administrator.</summary>
    public static IReadOnlyList<Guid> SelectToDisable(IEnumerable<Guid> stale, IEnumerable<Guid> activeAdmins)
    {
        var remainingAdmins = activeAdmins.ToHashSet();
        var result = new List<Guid>();
        foreach (var id in stale)
        {
            if (remainingAdmins.Contains(id))
            {
                if (remainingAdmins.Count <= 1) continue;
                remainingAdmins.Remove(id);
            }
            result.Add(id);
        }
        return result;
    }
}
