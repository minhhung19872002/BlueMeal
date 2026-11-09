using System.Security.Cryptography;
using BlueMeal.Api.Authorization;
using BlueMeal.Api.Catalog;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Admin;

public sealed class EmployeeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public Guid? DepartmentId { get; set; }
    public string? DepartmentNameVi { get; set; }
    public string? Position { get; set; }
    public Gender Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public Guid? UserId { get; set; }
    public bool IsActive { get; set; }
    public bool IsAnonymized { get; set; }
    public uint Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>True when PII fields were masked for the current user (no pii.view).</summary>
    public bool PiiMasked { get; set; }
}

public sealed class EmployeeUpsert : IHasVersion
{
    [ExcelColumn("Mã nhân viên", "Employee code", Required = true, Order = 1, Width = 14)] public string Code { get; set; } = default!;
    [ExcelColumn("Họ và tên", "Full name", Required = true, Order = 2, Width = 28)] public string FullName { get; set; } = default!;
    [ExcelColumn("Mã phòng ban", "Department code", Order = 3), ExcelRef(typeof(Department))] public Guid? DepartmentId { get; set; }
    [ExcelColumn("Chức vụ", "Position", Order = 4)] public string? Position { get; set; }
    [ExcelColumn("Giới tính (Male/Female/Common)", "Gender", Order = 5)] public Gender Gender { get; set; }
    [ExcelColumn("Ngày sinh", "Date of birth", Order = 6, Format = "dd/mm/yyyy")] public DateOnly? DateOfBirth { get; set; }
    [ExcelColumn("Điện thoại", "Phone", Order = 7)] public string? Phone { get; set; }
    [ExcelColumn("Email", "Email", Order = 8, Width = 26)] public string? Email { get; set; }
    [ExcelColumn("Số CCCD", "National ID", Order = 9)] public string? NationalId { get; set; }
    [ExcelColumn("Đang làm việc", "Active", Order = 10)] public bool IsActive { get; set; } = true;
    public uint? Version { get; set; }
}

public sealed class EmployeeUpsertValidator : AbstractValidator<EmployeeUpsert>
{
    public EmployeeUpsertValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email)).MaximumLength(200);
        RuleFor(x => x.Phone).Matches(@"^[0-9+()\-\s.]{6,20}$").When(x => !string.IsNullOrEmpty(x.Phone));
        RuleFor(x => x.NationalId).Matches(@"^\d{9}(\d{3})?$").When(x => !string.IsNullOrEmpty(x.NationalId));
        RuleFor(x => x.DateOfBirth).Must(d => d is null || (d.Value.Year > 1900 && d.Value <= DateOnly.FromDateTime(DateTime.Today)));
        RuleFor(x => x.Gender).IsInEnum();
    }
}

/// <summary>Employee catalog with column-level encrypted PII, masking without pii.view, account creation, DSAR export and anonymization (SPEC §7.1.4, §10.6).</summary>
[Route("api/v1/employees"), PermissionModule("employee")]
public sealed class EmployeesController(UserManager<AppUser> users, IEncryptionService enc, IAuditWriter audit) : CatalogControllerBase<Employee, EmployeeDto, EmployeeUpsert>
{
    protected override string ExportTitle => L.Pick("Danh muc nhan vien", "Employees");

    protected override IQueryable<Employee> ApplyFilters(IQueryable<Employee> q, IQueryCollection query)
    {
        if (Guid.TryParse(query["departmentId"], out var d)) q = q.Where(e => e.DepartmentId == d);
        // exact-match search on encrypted columns via HMAC (SPEC §10.4)
        if (query["phone"].ToString() is { Length: > 0 } phone) { var h = enc.Hash(phone); q = q.Where(e => e.PhoneHash == h); }
        if (query["email"].ToString() is { Length: > 0 } email) { var h = enc.Hash(email); q = q.Where(e => e.EmailHash == h); }
        if (query["nationalId"].ToString() is { Length: > 0 } nid) { var h = enc.Hash(nid); q = q.Where(e => e.NationalIdHash == h); }
        if (query["hasAccount"] == "true") q = q.Where(e => e.UserId != null);
        if (query["hasAccount"] == "false") q = q.Where(e => e.UserId == null);
        return q;
    }

    protected override IQueryable<Employee> DefaultOrder(IQueryable<Employee> q) => q.OrderBy(e => e.Code);

    protected override async Task PostProcessAsync(IList<EmployeeDto> items, bool single, CancellationToken ct)
    {
        if (CurrentUser.Has(Permissions.Pii.View))
        {
            if (single && items.Count == 1) await audit.WriteAsync(AuditAction.PiiAccess, nameof(Employee), items[0].Id.ToString(), ct: ct);
            return;
        }
        foreach (var e in items)
        {
            e.Phone = Masking.Phone(e.Phone);
            e.Email = Masking.Email(e.Email);
            e.NationalId = Masking.Generic(e.NationalId);
            e.PiiMasked = true;
        }
    }

    protected override void PostProcessExportRow(IDictionary<string, object?> row)
    {
        if (CurrentUser.Has(Permissions.Pii.View)) return;
        row[nameof(EmployeeUpsert.Phone)] = Masking.Phone(row[nameof(EmployeeUpsert.Phone)] as string);
        row[nameof(EmployeeUpsert.Email)] = Masking.Email(row[nameof(EmployeeUpsert.Email)] as string);
        row[nameof(EmployeeUpsert.NationalId)] = Masking.Generic(row[nameof(EmployeeUpsert.NationalId)] as string);
    }

    protected override Task BeforeSaveAsync(Employee e, EmployeeUpsert input, bool isNew, CancellationToken ct)
    {
        if (!isNew && !CurrentUser.Has(Permissions.Pii.View))
        {
            // Users who only see masked values cannot overwrite PII with the masked text.
            var entry = Db.ChangeTracker.Entries<Employee>().First(x => x.Entity == e);
            foreach (var p in new[] { nameof(Employee.Phone), nameof(Employee.Email), nameof(Employee.NationalId) })
                if ((entry.Property(p).CurrentValue as string)?.Contains('*') == true) entry.Property(p).CurrentValue = entry.Property(p).OriginalValue;
        }
        return Task.CompletedTask;
    }

    public sealed record CreateAccountRequest(string UserName, Guid[] RoleIds, bool SendEmail = false);

    /// <summary>Creates a login account from an employee; returns a one-time temporary password (must change at first login).</summary>
    [HttpPost("{id:guid}/create-account"), HasPermission(Permissions.User.Create)]
    public async Task<IActionResult> CreateAccount(Guid id, [FromBody] CreateAccountRequest req, CancellationToken ct)
    {
        var emp = await Db.Set<Employee>().FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("employee");
        if (emp.UserId is not null) throw new ConflictException("error.duplicateCode", req.UserName);
        var temp = UsersController.GenerateTempPassword();
        var user = new AppUser
        {
            UserName = req.UserName.Trim(), FullName = emp.FullName, Email = emp.Email, EmailConfirmed = !string.IsNullOrEmpty(emp.Email),
            EmployeeId = emp.Id, IsActive = true, MustChangePassword = true, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = CurrentUser.UserId,
            PasswordChangedAt = DateTimeOffset.UtcNow, LockoutEnabled = true,
        };
        var r = await users.CreateAsync(user, temp);
        if (!r.Succeeded) throw new AppException("error.duplicateCode", 409, req.UserName);
        foreach (var roleId in req.RoleIds.Distinct()) Db.Set<UserRole>().Add(new UserRole { UserId = user.Id, RoleId = roleId });
        emp.UserId = user.Id;
        await Db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.Create, nameof(AppUser), user.Id.ToString(), newValues: new { user.UserName, employee = emp.Code, roles = req.RoleIds }, ct: ct);
        return Ok(new { userId = user.Id, userName = user.UserName, temporaryPassword = temp });
    }

    /// <summary>Data subject access: export all personal data held about an employee (SPEC §10.6).</summary>
    [HttpGet("{id:guid}/personal-data"), HasPermission(Permissions.Pii.Manage)]
    public async Task<IActionResult> PersonalData(Guid id, CancellationToken ct)
    {
        var e = await Db.Set<Employee>().AsNoTracking().Include(x => x.Department).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("employee");
        object? account = null;
        if (e.UserId is { } uid)
        {
            var u = await users.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid, ct);
            var logins = await Db.Set<LoginHistory>().AsNoTracking().Where(h => h.UserId == uid).OrderByDescending(h => h.At).Take(500)
                .Select(h => new { h.At, result = h.Result.ToString(), h.Ip, h.UserAgent }).ToListAsync(ct);
            account = new { u?.UserName, u?.Email, u?.FullName, u?.CreatedAt, u?.LastLoginAt, mfa = u?.TwoFactorEnabled, loginHistory = logins };
        }
        await audit.WriteAsync(AuditAction.PiiAccess, nameof(Employee), id.ToString(), message: "personal-data export", ct: ct);
        var payload = new
        {
            exportedAt = DateTimeOffset.UtcNow,
            employee = new { e.Code, e.FullName, department = e.Department?.NameVi, e.Position, gender = e.Gender.ToString(), e.DateOfBirth, e.Phone, e.Email, e.NationalId, e.IsActive },
            account,
        };
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return File(json, "application/json", $"personal-data-{e.Code}.json");
    }

    /// <summary>Right to erasure: irreversible anonymization of personal data (account is disabled).</summary>
    [HttpPost("{id:guid}/anonymize"), HasPermission(Permissions.Pii.Manage)]
    public async Task<IActionResult> Anonymize(Guid id, CancellationToken ct)
    {
        var e = await Db.Set<Employee>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("employee");
        e.FullName = $"[Đã ẩn danh / Anonymized] {e.Code}";
        e.Phone = null; e.Email = null; e.NationalId = null; e.DateOfBirth = null; e.Position = null;
        e.IsAnonymized = true; e.IsActive = false;
        if (e.UserId is { } uid)
        {
            var u = await users.FindByIdAsync(uid.ToString());
            if (u is not null)
            {
                u.FullName = e.FullName; u.Email = null; u.NormalizedEmail = null; u.PhoneNumber = null; u.IsActive = false;
                await users.UpdateAsync(u);
                await Db.Set<UserSession>().Where(s => s.UserId == uid && s.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow).SetProperty(s => s.RevokedReason, "anonymized"), ct);
            }
        }
        await Db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.Update, nameof(Employee), id.ToString(), message: "anonymized", ct: ct);
        return NoContent();
    }
}

/// <summary>PII masking helpers (SPEC §10.6).</summary>
public static class Masking
{
    public static string? Phone(string? v) => string.IsNullOrEmpty(v) ? v : v.Length <= 4 ? "****" : new string('*', v.Length - 3) + v[^3..];
    public static string? Email(string? v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        var at = v.IndexOf('@');
        return at <= 1 ? "***" + (at >= 0 ? v[at..] : "") : v[0] + new string('*', Math.Max(3, at - 1)) + v[at..];
    }
    public static string? Generic(string? v) => string.IsNullOrEmpty(v) ? v : v.Length <= 4 ? "****" : new string('*', v.Length - 4) + v[^4..];
}
