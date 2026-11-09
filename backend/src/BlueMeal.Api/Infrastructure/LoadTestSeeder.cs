using BlueMeal.Application.Authorization;
using BlueMeal.Domain.Entities;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Infrastructure;

/// <summary>
/// Creates the accounts used by the k6 load test (tools/k6/load-test.js): role LOAD_TEST (read + small report export, no MFA,
/// not a system role) and users <c>loadtest0001…</c>. Only runs from the command line:
/// <c>dotnet BlueMeal.Api.dll --seed-loadtest=1000</c> with <c>BLUEMEAL_LOADTEST_PASSWORD</c> set; never at normal start-up.
/// <c>--seed-loadtest=0</c> removes the accounts again.
/// </summary>
public static class LoadTestSeeder
{
    public const string RoleCode = "LOAD_TEST";
    public const string Prefix = "loadtest";

    private static readonly string[] Grants =
    [
        Permissions.Dashboard.View, Permissions.Menu.View, Permissions.MenuPlan.View, Permissions.Recipe.View, Permissions.Dish.View, Permissions.Food.View,
        Permissions.Nutrition.View, Permissions.Report.View, Permissions.Report.Export, Permissions.Stock.View, Permissions.Alert.View,
    ];

    public static async Task RunAsync(IServiceProvider root, int count, string? password, ILogger log)
    {
        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (count <= 0)
        {
            var existing = await db.Users.Where(u => u.UserName!.StartsWith(Prefix)).ToListAsync();
            var ids = existing.Select(u => u.Id).ToList();
            // user_session has no FK to app_user: remove the test sessions explicitly (login history stays as audit evidence)
            await db.Set<UserSession>().Where(s => ids.Contains(s.UserId)).ExecuteDeleteAsync();
            foreach (var u in existing) await users.DeleteAsync(u);
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == RoleCode);
            if (role is not null) { db.Remove(role); await db.SaveChangesAsync(); }
            log.LogInformation("Removed {Count} load-test accounts", existing.Count);
            return;
        }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12) throw new InvalidOperationException("Set BLUEMEAL_LOADTEST_PASSWORD (≥ 12 characters).");

        var r = await db.Roles.Include(x => x.RolePermissions).FirstOrDefaultAsync(x => x.Name == RoleCode);
        if (r is null)
        {
            r = new AppRole { Name = RoleCode, NormalizedName = RoleCode, NameVi = "Kiểm thử tải (tạm thời)", NameEn = "Load test (temporary)", Description = "k6 — xóa sau khi kiểm thử", IsSystem = false };
            db.Roles.Add(r);
        }
        var permIds = await db.Set<Permission>().Where(p => Grants.Contains(p.Code)).Select(p => p.Id).ToListAsync();
        foreach (var id in permIds.Where(id => r.RolePermissions.All(x => x.PermissionId != id))) r.RolePermissions.Add(new RolePermission { PermissionId = id });
        await db.SaveChangesAsync();

        var have = (await db.Users.Where(u => u.UserName!.StartsWith(Prefix)).Select(u => u.UserName!).ToListAsync()).ToHashSet();
        if (have.Count > 0)
        {
            // accounts left from an earlier run get this run's password — otherwise their failed logins from the single k6 IP
            // trip the per-IP CAPTCHA threshold and every later login is rejected (seen in a run with stale accounts)
            var hash = users.PasswordHasher.HashPassword(new AppUser { FullName = "x" }, password);
            await db.Users.Where(u => u.UserName!.StartsWith(Prefix))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, hash).SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null).SetProperty(u => u.AccessFailedCount, 0));
        }
        var created = 0;
        for (var i = 1; i <= count; i++)
        {
            var name = $"{Prefix}{i:0000}";
            if (have.Contains(name)) continue;
            var u = new AppUser
            {
                UserName = name, FullName = $"Kiểm thử tải {i:0000}", Email = $"{name}@loadtest.local", EmailConfirmed = true, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow, PasswordChangedAt = DateTimeOffset.UtcNow, LockoutEnabled = true,
            };
            var res = await users.CreateAsync(u, password);
            if (!res.Succeeded) throw new InvalidOperationException(string.Join("; ", res.Errors.Select(e => e.Description)));
            db.Set<UserRole>().Add(new UserRole { UserId = u.Id, RoleId = r.Id });
            created++;
            if (created % 100 == 0) { await db.SaveChangesAsync(); log.LogInformation("{Count} load-test accounts created", created); }
        }
        await db.SaveChangesAsync();
        log.LogInformation("Load-test accounts ready: {Total} ({Created} new)", count, created);
    }
}
