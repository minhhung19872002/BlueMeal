using BlueMeal.Api.Authorization;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Identity;
using BlueMeal.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Admin;

public sealed record RoleDto(Guid Id, string Code, string NameVi, string? NameEn, string? Description, bool IsSystem, bool RequiresMfa, bool IsActive, int UserCount, int PermissionCount);
public sealed record RoleUpsert(string Code, string NameVi, string? NameEn, string? Description, bool RequiresMfa, bool IsActive = true);
public sealed record PermissionDto(Guid Id, string Code, string Module, string Action, string NameVi, string NameEn, string ModuleNameVi, string ModuleNameEn, string ActionNameVi, string ActionNameEn);
public sealed record RolePermissionsUpdate(string[] Permissions);

public sealed class RoleUpsertValidator : AbstractValidator<RoleUpsert>
{
    public RoleUpsertValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[A-Z0-9_]+$").WithMessage("validation.roleCode");
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

/// <summary>Role (permission group) management and the module × action permission matrix (SPEC §7.1.5, §7.1.9).</summary>
[Route("api/v1/roles")]
public sealed class RolesController(AppDbContext db, PermissionService permissions, IAuditWriter audit) : ApiControllerBase
{
    [HttpGet, HasPermission(Permissions.Role.View)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.IsSystem ? 0 : 1).ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name!, r.NameVi, r.NameEn, r.Description, r.IsSystem, r.RequiresMfa, r.IsActive,
                db.Set<UserRole>().Count(ur => ur.RoleId == r.Id), r.RolePermissions.Count)).ToListAsync(ct);
        return Ok(roles);
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.Role.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var r = await db.Roles.AsNoTracking().Include(x => x.RolePermissions).ThenInclude(x => x.Permission).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("role");
        return Ok(new
        {
            role = new RoleDto(r.Id, r.Name!, r.NameVi, r.NameEn, r.Description, r.IsSystem, r.RequiresMfa, r.IsActive, 0, r.RolePermissions.Count),
            permissions = r.RolePermissions.Select(p => p.Permission!.Code).OrderBy(c => c).ToArray(),
        });
    }

    [HttpPost, HasPermission(Permissions.Role.Create)]
    public async Task<IActionResult> Create([FromBody] RoleUpsert req, CancellationToken ct)
    {
        await ValidateAsync(req, ct);
        var code = req.Code.Trim().ToUpperInvariant();
        if (await db.Roles.AnyAsync(r => r.NormalizedName == code, ct)) throw new ConflictException("error.duplicateCode", code);
        var role = new AppRole { Name = code, NormalizedName = code, NameVi = req.NameVi, NameEn = req.NameEn, Description = req.Description, RequiresMfa = req.RequiresMfa, IsActive = req.IsActive, ConcurrencyStamp = Guid.NewGuid().ToString() };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.PermissionChange, nameof(AppRole), role.Id.ToString(), newValues: req, ct: ct);
        return Ok(new { role.Id });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.Role.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] RoleUpsert req, CancellationToken ct)
    {
        await ValidateAsync(req, ct);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("role");
        var old = new { role.NameVi, role.NameEn, role.Description, role.RequiresMfa, role.IsActive };
        role.NameVi = req.NameVi; role.NameEn = req.NameEn; role.Description = req.Description; role.RequiresMfa = req.RequiresMfa || DefaultRoles.MfaMandatory.Contains(role.Name);
        if (role.Name != DefaultRoles.SysAdmin) role.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAllAsync(ct);
        await audit.WriteAsync(AuditAction.PermissionChange, nameof(AppRole), id.ToString(), old, req, ct: ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.Role.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("role");
        if (role.IsSystem) throw new BusinessRuleException("validation.systemRole");
        if (await db.Set<UserRole>().AnyAsync(ur => ur.RoleId == id, ct)) throw new ConflictException("error.inUse");
        await db.Set<RolePermission>().Where(rp => rp.RoleId == id).ExecuteDeleteAsync(ct);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.PermissionChange, nameof(AppRole), id.ToString(), oldValues: new { role.Name }, message: "deleted", ct: ct);
        return NoContent();
    }

    /// <summary>Replaces the permission set of a role (matrix save).</summary>
    [HttpPut("{id:guid}/permissions"), HasPermission(Permissions.Role.Edit)]
    public async Task<IActionResult> SetPermissions(Guid id, [FromBody] RolePermissionsUpdate req, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("role");
        if (role.Name == DefaultRoles.SysAdmin) throw new BusinessRuleException("validation.systemRole");
        var wanted = req.Permissions.Distinct().ToHashSet();
        var all = await db.Set<Permission>().ToListAsync(ct);
        var unknown = wanted.Except(all.Select(p => p.Code)).ToList();
        if (unknown.Count > 0) throw new BusinessRuleException("error.validation");
        var before = role.RolePermissions.Select(rp => rp.Permission!.Code).OrderBy(c => c).ToArray();
        role.RolePermissions.Clear();
        foreach (var p in all.Where(p => wanted.Contains(p.Code))) role.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = p.Id });
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAllAsync(ct);
        await audit.WriteAsync(AuditAction.PermissionChange, nameof(AppRole), id.ToString(),
            new { permissions = before }, new { permissions = wanted.OrderBy(c => c).ToArray(), added = wanted.Except(before), removed = before.Except(wanted) }, ct: ct);
        return NoContent();
    }
}

/// <summary>Read-only function list generated from code (SPEC §7.1.8).</summary>
[Route("api/v1/permissions")]
public sealed class PermissionsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet, HasPermission(Permissions.Permission.View)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.Set<Permission>().AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);
        var roleCounts = await db.Set<RolePermission>().AsNoTracking().GroupBy(rp => rp.PermissionId).Select(g => new { g.Key, c = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.c, ct);
        return Ok(rows.Select(p =>
        {
            var m = Permissions.ModuleName(p.Module);
            var a = Permissions.ActionName(p.Action);
            return new { dto = new PermissionDto(p.Id, p.Code, p.Module, p.Action, p.NameVi, p.NameEn, m.Vi, m.En, a.Vi, a.En), roles = roleCounts.GetValueOrDefault(p.Id) };
        }).Select(x => new { x.dto.Id, x.dto.Code, x.dto.Module, x.dto.Action, x.dto.NameVi, x.dto.NameEn, x.dto.ModuleNameVi, x.dto.ModuleNameEn, x.dto.ActionNameVi, x.dto.ActionNameEn, roleCount = x.roles }));
    }
}
