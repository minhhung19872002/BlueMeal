using System.Security.Cryptography;
using System.Text;
using BlueMeal.Api.Auth;
using BlueMeal.Api.Authorization;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Identity;
using BlueMeal.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Admin;

public sealed record UserListItem(Guid Id, string UserName, string FullName, string? Email, bool IsActive, bool Locked, DateTimeOffset? LockoutEnd, bool MfaEnabled,
    bool MfaRequired, bool MustChangePassword, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt, string[] Roles, Guid? EmployeeId, int ActiveSessions);
public sealed record UserCreate(string UserName, string FullName, string? Email, Guid[] RoleIds, Guid? EmployeeId, bool MfaRequired = false, string? Password = null, string Language = "vi");
public sealed record UserUpdate(string FullName, string? Email, Guid[] RoleIds, Guid? EmployeeId, bool MfaRequired, string Language = "vi");
public sealed record DataScopeItem(DataScopeType Type, Guid Id);
public sealed record DataScopeUpdate(DataScopeItem[] Scopes);

public sealed class UserCreateValidator : AbstractValidator<UserCreate>
{
    public UserCreateValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(64).Matches(@"^[A-Za-z0-9._@\-]+$").WithMessage("validation.userName");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Language).Must(l => l is "vi" or "en");
    }
}

public sealed class UserUpdateValidator : AbstractValidator<UserUpdate>
{
    public UserUpdateValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
    }
}

/// <summary>User accounts: add/edit/lock/unlock, reset password, force first-login change, roles, data scope, MFA, sessions (SPEC §7.1.6).</summary>
[Route("api/v1/users")]
public sealed class UsersController(AppDbContext db, UserManager<AppUser> users, AuthService auth, PermissionService permissions, IAuditWriter audit, TimeProvider clock) : ApiControllerBase
{
    private DateTimeOffset Now => clock.GetUtcNow();

    [HttpGet, HasPermission(Permissions.User.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] Guid? roleId, [FromQuery] string? status, CancellationToken ct)
    {
        var query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = "%" + q.Q.Trim() + "%";
            query = query.Where(u => EF.Functions.ILike(u.UserName!, term) || EF.Functions.ILike(u.FullName, term) || EF.Functions.ILike(u.Email!, term)
                                     || EF.Functions.ILike(EF.Functions.Unaccent(u.FullName), EF.Functions.Unaccent(term)));
        }
        if (roleId is not null) query = query.Where(u => u.UserRoles.Any(r => r.RoleId == roleId));
        if (status == "active") query = query.Where(u => u.IsActive);
        if (status == "disabled") query = query.Where(u => !u.IsActive);
        var nowL = Now;
        if (status == "locked") query = query.Where(u => u.LockoutEnd != null && u.LockoutEnd > nowL);
        var total = await query.LongCountAsync(ct);
        query = q.Sort switch
        {
            "userName" => query.OrderBy(u => u.UserName), "-userName" => query.OrderByDescending(u => u.UserName),
            "fullName" => query.OrderBy(u => u.FullName), "-fullName" => query.OrderByDescending(u => u.FullName),
            "lastLoginAt" => query.OrderBy(u => u.LastLoginAt), "-lastLoginAt" => query.OrderByDescending(u => u.LastLoginAt),
            _ => query.OrderBy(u => u.UserName),
        };
        var now = Now;
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(u => new UserListItem(u.Id, u.UserName!, u.FullName, u.Email, u.IsActive, u.LockoutEnd != null && u.LockoutEnd > now, u.LockoutEnd, u.TwoFactorEnabled, u.MfaRequired,
                u.MustChangePassword, u.LastLoginAt, u.CreatedAt, u.UserRoles.Select(r => r.Role!.Name!).ToArray(), u.EmployeeId,
                db.Set<UserSession>().Count(s => s.UserId == u.Id && s.RevokedAt == null && s.ExpiresAt > now)))
            .ToListAsync(ct);
        return Ok(new PagedResult<UserListItem>(items, q.Page, q.PageSize, total));
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.User.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var u = await users.Users.AsNoTracking().Include(x => x.UserRoles).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("user");
        var scopes = await db.Set<UserDataScope>().AsNoTracking().Where(s => s.UserId == id).Select(s => new DataScopeItem(s.ScopeType, s.ScopeId)).ToListAsync(ct);
        return Ok(new
        {
            u.Id, u.UserName, u.FullName, u.Email, u.IsActive, locked = u.LockoutEnd != null && u.LockoutEnd > Now, u.LockoutEnd, mfaEnabled = u.TwoFactorEnabled, u.MfaRequired,
            mfaMandatory = await auth.IsMfaMandatoryAsync(u, ct), u.MustChangePassword, u.LastLoginAt, u.CreatedAt, u.EmployeeId, u.Language,
            roleIds = u.UserRoles.Select(r => r.RoleId).ToArray(), scopes,
        });
    }

    [HttpPost, HasPermission(Permissions.User.Create)]
    public async Task<IActionResult> Create([FromBody] UserCreate req, CancellationToken ct)
    {
        await ValidateAsync(req, ct);
        var name = req.UserName.Trim();
        if (await users.FindByNameAsync(name) is not null) throw new ConflictException("error.duplicateCode", name);
        var password = string.IsNullOrEmpty(req.Password) ? GenerateTempPassword() : req.Password;
        var user = new AppUser
        {
            UserName = name, FullName = req.FullName.Trim(), Email = req.Email?.Trim(), EmailConfirmed = !string.IsNullOrEmpty(req.Email), EmployeeId = req.EmployeeId,
            IsActive = true, MustChangePassword = true, MfaRequired = req.MfaRequired, Language = req.Language, CreatedAt = Now, CreatedBy = CurrentUser.UserId,
            PasswordChangedAt = Now, LockoutEnabled = true,
        };
        await auth.ValidatePasswordPolicyAsync(user, password, ct);
        var r = await users.CreateAsync(user, password);
        if (!r.Succeeded) throw new AppException("error.validation", 400) { Errors = new Dictionary<string, string[]> { ["userName"] = r.Errors.Select(e => e.Description).ToArray() } };
        await SetRoles(user.Id, req.RoleIds, ct);
        if (req.EmployeeId is { } eid) await db.Set<Employee>().Where(e => e.Id == eid).ExecuteUpdateAsync(x => x.SetProperty(e => e.UserId, user.Id), ct);
        await audit.WriteAsync(AuditAction.Create, nameof(AppUser), user.Id.ToString(), newValues: new { user.UserName, user.FullName, roles = req.RoleIds }, ct: ct);
        return Ok(new { user.Id, temporaryPassword = string.IsNullOrEmpty(req.Password) ? password : null });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.User.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UserUpdate req, CancellationToken ct)
    {
        await ValidateAsync(req, ct);
        var u = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("user");
        var old = new { u.FullName, u.Email, u.MfaRequired, u.EmployeeId };
        u.FullName = req.FullName.Trim();
        u.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        u.NormalizedEmail = u.Email?.ToUpperInvariant();
        u.MfaRequired = req.MfaRequired;
        u.EmployeeId = req.EmployeeId;
        u.Language = req.Language is "en" ? "en" : "vi";
        u.UpdatedAt = Now;
        await users.UpdateAsync(u);
        await SetRoles(id, req.RoleIds, ct);
        await audit.WriteAsync(AuditAction.Update, nameof(AppUser), id.ToString(), old, new { u.FullName, u.Email, u.MfaRequired, u.EmployeeId, roles = req.RoleIds }, ct: ct);
        return NoContent();
    }

    private async Task SetRoles(Guid userId, Guid[] roleIds, CancellationToken ct)
    {
        var current = await db.Set<UserRole>().Where(r => r.UserId == userId).ToListAsync(ct);
        var wanted = roleIds.Distinct().ToHashSet();
        // the last active SYS_ADMIN cannot lose the role
        var adminRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Name == DefaultRoles.SysAdmin, ct);
        if (current.Any(c => c.RoleId == adminRole.Id) && !wanted.Contains(adminRole.Id)
            && await db.Set<UserRole>().CountAsync(r => r.RoleId == adminRole.Id && r.User!.IsActive, ct) <= 1)
            throw new BusinessRuleException("validation.lastAdmin");
        db.Set<UserRole>().RemoveRange(current.Where(c => !wanted.Contains(c.RoleId)));
        foreach (var r in wanted.Where(w => current.All(c => c.RoleId != w))) db.Set<UserRole>().Add(new UserRole { UserId = userId, RoleId = r });
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAllAsync(ct);
        if (current.Select(c => c.RoleId).ToHashSet().SetEquals(wanted) == false)
            await audit.WriteAsync(AuditAction.PermissionChange, nameof(AppUser), userId.ToString(), new { roles = current.Select(c => c.RoleId) }, new { roles = wanted }, ct: ct);
    }

    /// <summary>Disable account: revokes all sessions/refresh tokens (SPEC §10.1).</summary>
    [HttpPost("{id:guid}/lock"), HasPermission(Permissions.User.Lock)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken ct)
    {
        if (id == UserId) throw new BusinessRuleException("validation.selfLock");
        var u = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("user");
        u.IsActive = false;
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        await users.UpdateAsync(u);
        var revoked = await auth.RevokeUserSessionsAsync(id, "account_locked", null, ct);
        await audit.WriteAsync(AuditAction.Lock, nameof(AppUser), id.ToString(), message: $"revoked {revoked} sessions", ct: ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unlock"), HasPermission(Permissions.User.Lock)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct)
    {
        var u = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("user");
        u.IsActive = true;
        u.LockoutEnd = null;
        u.AccessFailedCount = 0;
        await users.UpdateAsync(u);
        await audit.WriteAsync(AuditAction.Unlock, nameof(AppUser), id.ToString(), ct: ct);
        return NoContent();
    }

    /// <summary>Admin password reset: temporary password, must be changed at next login, all sessions revoked.</summary>
    [HttpPost("{id:guid}/reset-password"), HasPermission(Permissions.User.ResetPassword)]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct)
    {
        var u = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("user");
        var temp = GenerateTempPassword();
        if (u.PasswordHash is not null) db.Set<PasswordHistory>().Add(new PasswordHistory { UserId = u.Id, PasswordHash = u.PasswordHash, CreatedAt = Now });
        u.PasswordHash = users.PasswordHasher.HashPassword(u, temp);
        u.MustChangePassword = true;
        u.PasswordChangedAt = Now;
        u.LockoutEnd = null;
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        await users.UpdateAsync(u);
        await auth.RevokeUserSessionsAsync(id, "admin_reset_password", null, ct);
        await audit.WriteAsync(AuditAction.PasswordChange, nameof(AppUser), id.ToString(), message: "reset by admin", ct: ct);
        return Ok(new { temporaryPassword = temp });
    }

    [HttpPost("{id:guid}/require-password-change"), HasPermission(Permissions.User.Edit)]
    public async Task<IActionResult> RequirePasswordChange(Guid id, CancellationToken ct)
    {
        var u = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("user");
        u.MustChangePassword = true;
        await users.UpdateAsync(u);
        return NoContent();
    }

    /// <summary>Reset MFA (lost device): user must enroll again at next login if MFA is mandatory.</summary>
    [HttpPost("{id:guid}/reset-mfa"), HasPermission(Permissions.User.ResetPassword)]
    public async Task<IActionResult> ResetMfa(Guid id, CancellationToken ct)
    {
        await auth.DisableMfaAsync(id, byAdmin: true, ct);
        await auth.RevokeUserSessionsAsync(id, "mfa_reset", null, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/scopes"), HasPermission(Permissions.User.Edit)]
    public async Task<IActionResult> SetScopes(Guid id, [FromBody] DataScopeUpdate req, CancellationToken ct)
    {
        _ = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("user");
        var old = await db.Set<UserDataScope>().Where(s => s.UserId == id).ToListAsync(ct);
        db.Set<UserDataScope>().RemoveRange(old);
        foreach (var s in req.Scopes.DistinctBy(s => (s.Type, s.Id))) db.Set<UserDataScope>().Add(new UserDataScope { UserId = id, ScopeType = s.Type, ScopeId = s.Id });
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAllAsync(ct);
        await audit.WriteAsync(AuditAction.PermissionChange, nameof(UserDataScope), id.ToString(), old.Select(o => new { o.ScopeType, o.ScopeId }), req.Scopes, ct: ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/sessions"), HasPermission(Permissions.Session.View)]
    public async Task<IActionResult> Sessions(Guid id, CancellationToken ct)
    {
        var now = Now;
        var items = await db.Set<UserSession>().AsNoTracking().Where(s => s.UserId == id).OrderByDescending(s => s.LastSeenAt).Take(100)
            .Select(s => new { s.Id, s.Ip, s.Device, s.UserAgent, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.RevokedAt, s.RevokedReason, active = s.RevokedAt == null && s.ExpiresAt > now })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}/login-history"), HasPermission(Permissions.User.View)]
    public async Task<IActionResult> LoginHistory(Guid id, [FromQuery(Name = "")] PageQuery q, CancellationToken ct)
    {
        var query = db.Set<LoginHistory>().AsNoTracking().Where(h => h.UserId == id).OrderByDescending(h => h.At);
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(h => new { h.Id, h.At, result = h.Result.ToString(), h.Ip, h.UserAgent, h.Reason }).ToListAsync(ct);
        return Ok(new PagedResult<object>(items, q.Page, q.PageSize, total));
    }

    /// <summary>Random temporary password satisfying the default policy (≥ 10 chars, upper, lower, digit, special).</summary>
    public static string GenerateTempPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnopqrstuvwxyz", digits = "23456789", special = "!@#$%^&*-_+=?";
        var all = upper + lower + digits + special;
        var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits), Pick(special) };
        while (chars.Count < 14) chars.Add(Pick(all));
        return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(1000)).ToArray());
        static char Pick(string s) => s[RandomNumberGenerator.GetInt32(s.Length)];
    }
}

/// <summary>Active sessions across all users; force logout per session or per user (SPEC §7.1.7).</summary>
[Route("api/v1/sessions")]
public sealed class SessionsController(AppDbContext db, AuthService auth, TimeProvider clock) : ApiControllerBase
{
    [HttpGet, HasPermission(Permissions.Session.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = from s in db.Set<UserSession>().AsNoTracking()
                    join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                    where s.RevokedAt == null && s.ExpiresAt > now
                    select new { s.Id, s.UserId, u.UserName, u.FullName, s.Ip, s.Device, s.UserAgent, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.IsMfaSetupOnly };
        if (!string.IsNullOrWhiteSpace(q.Q)) { var t = "%" + q.Q.Trim() + "%"; query = query.Where(x => EF.Functions.ILike(x.UserName!, t) || EF.Functions.ILike(x.FullName, t) || EF.Functions.ILike(x.Ip!, t)); }
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(x => x.LastSeenAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        var sid = SessionId;
        return Ok(new PagedResult<object>(items.Select(x => (object)new { x.Id, x.UserId, x.UserName, x.FullName, x.Ip, x.Device, x.UserAgent, x.CreatedAt, x.LastSeenAt, x.ExpiresAt, x.IsMfaSetupOnly, current = x.Id == sid }).ToList(), q.Page, q.PageSize, total));
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.Session.Revoke)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await auth.RevokeSessionAsync(id, "admin_force_logout", ct);
        return NoContent();
    }

    [HttpDelete("user/{userId:guid}"), HasPermission(Permissions.Session.Revoke)]
    public async Task<IActionResult> RevokeAll(Guid userId, CancellationToken ct)
    {
        var n = await auth.RevokeUserSessionsAsync(userId, "admin_force_logout_all", null, ct);
        return Ok(new { revoked = n });
    }
}
