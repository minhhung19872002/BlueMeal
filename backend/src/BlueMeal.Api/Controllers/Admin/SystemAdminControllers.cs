using System.Text.Json;
using BlueMeal.Api.Authorization;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Admin;

public sealed record OrganizationDto(Guid Id, string Name, string? NameEn, string? TaxCode, string? Address, Guid? LogoFileId, string? Phone, string? Email,
    string? Representative, string? RepresentativeTitle, string? Website, uint Version);
public sealed record OrganizationUpdate(string Name, string? NameEn, string? TaxCode, string? Address, string? Phone, string? Email, string? Representative,
    string? RepresentativeTitle, string? Website, uint? Version);

public sealed class OrganizationUpdateValidator : AbstractValidator<OrganizationUpdate>
{
    public OrganizationUpdateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.TaxCode).Matches(@"^\d{10}(-\d{3})?$").When(x => !string.IsNullOrEmpty(x.TaxCode));
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Address).MaximumLength(500);
    }
}

/// <summary>Organization info and logo (SPEC §7.1.1). Logo is shown on the header and reports.</summary>
[Route("api/v1/organization")]
public sealed class OrganizationController(AppDbContext db, FileService files) : ApiControllerBase
{
    [HttpGet, HasPermission(Permissions.Organization.View)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var o = await db.Set<Organization>().AsNoTracking().FirstAsync(ct);
        return Ok(new OrganizationDto(o.Id, o.Name, o.NameEn, o.TaxCode, o.Address, o.LogoFileId, o.Phone, o.Email, o.Representative, o.RepresentativeTitle, o.Website, o.Version));
    }

    [HttpPut, HasPermission(Permissions.Organization.Edit)]
    public async Task<IActionResult> Update([FromBody] OrganizationUpdate req, CancellationToken ct)
    {
        await ValidateAsync(req, ct);
        var o = await db.Set<Organization>().FirstAsync(ct);
        if (req.Version is { } v) db.Entry(o).Property(x => x.Version).OriginalValue = v;
        o.Name = req.Name; o.NameEn = req.NameEn; o.TaxCode = req.TaxCode; o.Address = req.Address; o.Phone = req.Phone; o.Email = req.Email;
        o.Representative = req.Representative; o.RepresentativeTitle = req.RepresentativeTitle; o.Website = req.Website;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("logo"), HasPermission(Permissions.Organization.Edit), RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile file, CancellationToken ct)
    {
        var o = await db.Set<Organization>().FirstAsync(ct);
        var f = await files.SaveAsync(file, FileService.ImageTypes, 2, nameof(Organization), o.Id, ct);
        o.LogoFileId = f.Id;
        await db.SaveChangesAsync(ct);
        return Ok(new { fileId = f.Id });
    }

    /// <summary>Public logo (login page, published menu) — only the organization logo is served anonymously.</summary>
    [HttpGet("logo"), AllowAnonymous, ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Logo(CancellationToken ct)
    {
        var id = await db.Set<Organization>().AsNoTracking().Select(o => o.LogoFileId).FirstOrDefaultAsync(ct);
        if (id is null) return NotFound();
        var (stream, f) = await files.OpenAsync(id.Value, ct);
        return File(stream, f.ContentType);
    }
}

/// <summary>Attachment storage service: random names, MIME sniffing, size limit, owner-based access.</summary>
public sealed class FileService(AppDbContext db, IFileStorage storage)
{
    public static readonly Dictionary<string, string> ImageTypes = new() { [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".webp"] = "image/webp" };
    public static readonly Dictionary<string, string> DocumentTypes = new(ImageTypes) { [".pdf"] = "application/pdf" };

    public async Task<FileAttachment> SaveAsync(IFormFile? file, Dictionary<string, string> allowed, int maxMb, string ownerType, Guid? ownerId, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new AppException("error.fileType", 400);
        if (file.Length > maxMb * 1024L * 1024L) throw new AppException("error.fileSize", 413, maxMb);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.TryGetValue(ext, out var mime)) throw new AppException("error.fileType", 415);
        await using var s = file.OpenReadStream();
        var head = new byte[12];
        var n = await s.ReadAsync(head.AsMemory(0, 12), ct);
        if (!MagicMatches(mime, head.AsSpan(0, n))) throw new AppException("error.fileType", 415);
        s.Position = 0;
        var (stored, size, sha) = await storage.SaveAsync(s, ext, ct);
        var att = new FileAttachment
        {
            OriginalName = Path.GetFileName(file.FileName).Length > 200 ? Path.GetFileName(file.FileName)[..200] : Path.GetFileName(file.FileName),
            StoredName = stored, ContentType = mime, Size = size, Sha256 = sha, OwnerType = ownerType, OwnerId = ownerId,
        };
        db.Add(att);
        await db.SaveChangesAsync(ct);
        return att;
    }

    public async Task<(Stream Stream, FileAttachment File)> OpenAsync(Guid id, CancellationToken ct)
    {
        var f = await db.Set<FileAttachment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("file");
        return (storage.OpenRead(f.StoredName), f);
    }

    private static bool MagicMatches(string mime, ReadOnlySpan<byte> h) => mime switch
    {
        "image/png" => h.Length >= 4 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47,
        "image/jpeg" => h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF,
        "image/webp" => h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P',
        "application/pdf" => h.Length >= 4 && h[0] == '%' && h[1] == 'P' && h[2] == 'D' && h[3] == 'F',
        _ => false,
    };
}

/// <summary>Generic attachment endpoints. Download permission is derived from the owner type (SPEC §10.4).</summary>
[Route("api/v1/files")]
public sealed class FilesController(FileService files, AppDbContext db) : ApiControllerBase
{
    private static readonly Dictionary<string, string> OwnerPermissions = new()
    {
        [nameof(Organization)] = Permissions.Organization.View,
        [nameof(PartnerCertificate)] = Permissions.Partner.View,
        [nameof(PartnerContract)] = Permissions.Partner.View,
        [nameof(Dish)] = Permissions.Dish.View,
        [nameof(StockReceiptLine)] = Permissions.StockReceipt.View,
        ["Inspection"] = Permissions.Inspection.View,
    };
    private static readonly Dictionary<string, string> OwnerEditPermissions = new()
    {
        [nameof(PartnerCertificate)] = Permissions.Partner.Edit,
        [nameof(PartnerContract)] = Permissions.Partner.Edit,
        [nameof(Dish)] = Permissions.Dish.Edit,
        [nameof(StockReceiptLine)] = Permissions.StockReceipt.Edit,
        ["Inspection"] = Permissions.Inspection.Edit,
    };

    [HttpPost, HasPermission(Permissions.Self), RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string ownerType, CancellationToken ct)
    {
        if (!OwnerEditPermissions.TryGetValue(ownerType, out var perm) || !CurrentUser.Has(perm)) throw new ForbiddenException();
        var allowed = ownerType == nameof(Dish) ? FileService.ImageTypes : FileService.DocumentTypes;
        var f = await files.SaveAsync(file, allowed, 10, ownerType, null, ct);
        return Ok(new { f.Id, f.OriginalName, f.ContentType, f.Size });
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.Self)]
    public async Task<IActionResult> Download(Guid id, [FromQuery] bool inline = false, CancellationToken ct = default)
    {
        var meta = await db.Set<FileAttachment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("file");
        if (meta.OwnerType is null || !OwnerPermissions.TryGetValue(meta.OwnerType, out var perm) || !CurrentUser.Has(perm)) throw new ForbiddenException();
        var (stream, f) = await files.OpenAsync(id, ct);
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        return inline ? File(stream, f.ContentType) : File(stream, f.ContentType, f.OriginalName);
    }
}

/// <summary>System configuration (SPEC §7.1.13): password policy, thresholds, inventory cost method, overhead, SMTP, backup schedule, sample retention, generator weights…</summary>
[Route("api/v1/settings")]
public sealed class SettingsController(ISettingsService settings, IAuditWriter audit, IEmailSender email, TimeProvider clock) : ApiControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [HttpGet, HasPermission(Permissions.SystemInfo.Setting)]
    public async Task<IActionResult> All(CancellationToken ct)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, type) in SettingKeys.Types)
        {
            var value = await GetTyped(key, type, ct);
            if (value is SmtpSettings smtp) smtp.Password = string.IsNullOrEmpty(smtp.Password) ? null : "********";
            result[key] = value;
        }
        return Ok(result);
    }

    [HttpGet("{key}"), HasPermission(Permissions.SystemInfo.Setting)]
    public async Task<IActionResult> Get(string key, CancellationToken ct)
    {
        if (!SettingKeys.Types.TryGetValue(key, out var type)) throw new NotFoundException("setting");
        var v = await GetTyped(key, type, ct);
        if (v is SmtpSettings smtp) smtp.Password = string.IsNullOrEmpty(smtp.Password) ? null : "********";
        return Ok(v);
    }

    [HttpPut("{key}"), HasPermission(Permissions.SystemInfo.SettingEdit)]
    public async Task<IActionResult> Put(string key, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (!SettingKeys.Types.TryGetValue(key, out var type)) throw new NotFoundException("setting");
        var value = body.Deserialize(type, Json) ?? throw new AppException("error.validation");
        var old = await GetTyped(key, type, ct);
        if (value is SmtpSettings smtp && (smtp.Password == "********" || smtp.Password is null)) smtp.Password = (old as SmtpSettings)?.Password;
        if (value is PrivacyNoticeSettings notice && old is PrivacyNoticeSettings prev)
        {
            // The version is server-managed: a changed text is a new notice that every user must accept again.
            var changed = notice.ContentVi.Trim() != prev.ContentVi.Trim() || notice.ContentEn.Trim() != prev.ContentEn.Trim();
            notice.Version = changed ? prev.Version + 1 : prev.Version;
            notice.PublishedAt = changed ? clock.GetUtcNow() : prev.PublishedAt;
        }
        ValidateSetting(value);
        var method = typeof(ISettingsService).GetMethod(nameof(ISettingsService.SetAsync))!.MakeGenericMethod(type);
        await (Task)method.Invoke(settings, [key, value, ct])!;
        if (old is SmtpSettings os) os.Password = os.Password is null ? null : "***";
        if (value is SmtpSettings ns) ns.Password = ns.Password is null ? null : "***";
        await audit.WriteAsync(AuditAction.ConfigChange, nameof(AppSetting), key, old, value, ct: ct);
        return NoContent();
    }

    public sealed record TestEmailRequest(string To);

    [HttpPost("smtp/test"), HasPermission(Permissions.SystemInfo.SettingEdit)]
    public async Task<IActionResult> TestEmail([FromBody] TestEmailRequest req, CancellationToken ct)
    {
        await email.SendAsync(req.To, "BlueMeal — Kiểm tra email / Test email", "<p>Cấu hình SMTP hoạt động. / SMTP configuration works.</p>", ct);
        return NoContent();
    }

    private async Task<object> GetTyped(string key, Type type, CancellationToken ct)
    {
        var method = typeof(ISettingsService).GetMethod(nameof(ISettingsService.GetAsync))!.MakeGenericMethod(type);
        var task = (Task)method.Invoke(settings, [key, ct])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static void ValidateSetting(object v)
    {
        var errors = new Dictionary<string, string[]>();
        switch (v)
        {
            case PasswordPolicySettings p when p.MinLength is < 8 or > 128 || p.HistoryCount is < 0 or > 24 || p.MaxFailedAttempts is < 3 or > 20 || p.LockoutMinutes is < 1 or > 1440
                                               || p.InactiveDisableDays is < 0 or > 365:
                errors["policy"] = [L.T("error.validation")]; break;
            case PrivacyNoticeSettings n when string.IsNullOrWhiteSpace(n.ContentVi) || string.IsNullOrWhiteSpace(n.ContentEn) || n.ContentVi.Length > 20_000 || n.ContentEn.Length > 20_000:
                errors["content"] = [L.T("error.validation")]; break;
            case SessionPolicySettings s when s.IdleTimeoutMinutes is < 5 or > 480 || s.MaxConcurrentSessions is < 1 or > 20 || s.AccessTokenMinutes is < 5 or > 60:
                errors["session"] = [L.T("error.validation")]; break;
            case ApprovalSettings a when a.Levels is < 1 or > 2:
                errors["levels"] = [L.T("error.validation")]; break;
            case SampleSettings s when s.RetentionHours < 24:
                errors["retentionHours"] = [L.T("error.validation")]; break;
            case AuditSettings a when a.RetentionMonths < 12:
                errors["retentionMonths"] = [L.T("error.validation")]; break;
        }
        if (errors.Count > 0) throw new AppException("error.validation", 400) { Errors = errors };
    }
}

/// <summary>Centralized audit log lookup and export (SPEC §10.3, RPT-16). Read-only by design.</summary>
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController(AppDbContext db, IExcelService excel, IAuditWriter audit) : ApiControllerBase
{
    public sealed class AuditQuery : PageQuery
    {
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }
        public string? UserName { get; set; }
        public AuditAction? Action { get; set; }
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public string? Ip { get; set; }
        public bool? Success { get; set; }
    }

    private IQueryable<AuditLog> Query(AuditQuery q)
    {
        var query = db.Set<AuditLog>().AsNoTracking();
        if (q.From is { } f) query = query.Where(a => a.At >= f);
        if (q.To is { } t) query = query.Where(a => a.At <= t);
        if (!string.IsNullOrWhiteSpace(q.UserName)) query = query.Where(a => EF.Functions.ILike(a.UserName!, "%" + q.UserName.Trim() + "%"));
        if (q.Action is { } ac) query = query.Where(a => a.Action == ac);
        if (!string.IsNullOrWhiteSpace(q.EntityType)) query = query.Where(a => a.EntityType == q.EntityType);
        if (!string.IsNullOrWhiteSpace(q.EntityId)) query = query.Where(a => a.EntityId == q.EntityId);
        if (!string.IsNullOrWhiteSpace(q.Ip)) query = query.Where(a => a.Ip == q.Ip);
        if (q.Success is { } s) query = query.Where(a => a.Success == s);
        if (!string.IsNullOrWhiteSpace(q.Q)) query = query.Where(a => EF.Functions.ILike(a.Message!, "%" + q.Q + "%") || EF.Functions.ILike(a.EntityType!, "%" + q.Q + "%"));
        return query.OrderByDescending(a => a.At);
    }

    [HttpGet, HasPermission(Permissions.Audit.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] AuditQuery q, CancellationToken ct)
    {
        var query = Query(q);
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(a => new { a.Id, a.At, a.UserId, a.UserName, a.Ip, a.UserAgent, action = a.Action.ToString(), a.EntityType, a.EntityId, a.OldValues, a.NewValues, a.Success, a.Message, a.TraceId })
            .ToListAsync(ct);
        return Ok(new PagedResult<object>(items, q.Page, q.PageSize, total));
    }

    [HttpGet("entity-types"), HasPermission(Permissions.Audit.View)]
    public async Task<IActionResult> EntityTypes(CancellationToken ct)
        => Ok(await db.Set<AuditLog>().AsNoTracking().Where(a => a.EntityType != null).Select(a => a.EntityType!).Distinct().OrderBy(x => x).Take(500).ToListAsync(ct));

    [HttpGet("export"), HasPermission(Permissions.Audit.Export)]
    public async Task<IActionResult> Export([FromQuery(Name = "")] AuditQuery q, [FromQuery] string? format, CancellationToken ct)
    {
        var rows = await Query(q).Take(50_000).ToListAsync(ct);
        var org = await db.Set<Organization>().AsNoTracking().FirstOrDefaultAsync(ct);
        var period = q.From is null && q.To is null ? null : $"{q.From?.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy} - {q.To?.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy}";
        var sheet = new ExcelSheet
        {
            Name = "RPT-16",
            Header = new ExcelReportHeader(L.Pick(org?.Name ?? "BlueMeal", org?.NameEn), L.Pick("Nhật ký truy cập / thao tác hệ thống", "System access / activity log"), period, CurrentUser.FullName ?? CurrentUser.UserName ?? "", DateTimeOffset.UtcNow),
            Columns =
            [
                new("at", L.Pick("Thời điểm", "Time"), 18), new("user", L.Pick("Người dùng", "User"), 16), new("ip", "IP", 15), new("action", L.Pick("Hành động", "Action"), 14),
                new("entity", L.Pick("Đối tượng", "Object"), 18), new("entityId", "ID", 38), new("success", L.Pick("Thành công", "Success"), 10), new("message", L.Pick("Nội dung", "Details"), 40),
                new("changes", L.Pick("Giá trị thay đổi", "Changes"), 60),
            ],
            Rows = rows.Select(a => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["at"] = a.At, ["user"] = a.UserName, ["ip"] = a.Ip, ["action"] = a.Action.ToString(), ["entity"] = a.EntityType, ["entityId"] = a.EntityId,
                ["success"] = a.Success, ["message"] = a.Message, ["changes"] = a.NewValues is { Length: > 2000 } nv ? nv[..2000] : a.NewValues,
            }).ToList(),
        };
        var name = $"RPT-16_NhatKyHeThong_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        var csv = IsCsv(format);
        var ods = IsOds(format);
        await audit.WriteAsync(AuditAction.Export, nameof(AuditLog), null, message: $"{rows.Count} rows" + (csv ? " (csv)" : ods ? " (ods)" : ""), ct: ct);
        if (csv) return File(CsvWriter.Write(sheet), CsvWriter.ContentType, CsvWriter.FileName(name));
        if (ods) return File(OdsWriter.Write(sheet), OdsWriter.ContentType, OdsWriter.FileName(name));
        return File(excel.Write(sheet), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }
}
