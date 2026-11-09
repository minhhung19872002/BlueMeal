using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using BlueMeal.Api.Authorization;
using BlueMeal.Api.Controllers;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Catalog;

public interface IHasVersion { uint? Version { get; set; } }

/// <summary>
/// Generic catalog endpoints (SPEC §7 common features): server-side paging, accent-insensitive search, filter,
/// sort, create/update/soft-delete, lock/unlock, change history, Excel import (preview → commit) and export.
/// Permissions are relative to the derived controller's [PermissionModule].
/// </summary>
/// <typeparam name="TEntity">Entity (soft-deletable, versioned).</typeparam>
/// <typeparam name="TDto">Read DTO (must have Id and Version).</typeparam>
/// <typeparam name="TUpsert">Write DTO — only whitelisted fields (prevents mass assignment).</typeparam>
public abstract class CatalogControllerBase<TEntity, TDto, TUpsert> : ApiControllerBase
    where TEntity : AuditableEntity, new()
    where TDto : class
    where TUpsert : class, IHasVersion, new()
{
    protected IAppDbContext Db => HttpContext.RequestServices.GetRequiredService<IAppDbContext>();
    protected ICacheService Cache => HttpContext.RequestServices.GetRequiredService<ICacheService>();
    protected IExcelService Excel => HttpContext.RequestServices.GetRequiredService<IExcelService>();
    protected IAuditWriter Audit => HttpContext.RequestServices.GetRequiredService<IAuditWriter>();

    private static readonly bool HasCode = typeof(TEntity).GetProperty("Code") is not null;
    private static readonly bool HasSearch = typeof(TEntity).GetProperty("SearchText") is not null;
    private static readonly bool HasActive = typeof(TEntity).GetProperty("IsActive") is not null;
    private static readonly bool HasSort = typeof(TEntity).GetProperty("SortOrder") is not null;

    /// <summary>Cache region invalidated on writes (catalog lookups are cached — SPEC §11).</summary>
    protected virtual string CacheRegion => "catalog:" + typeof(TEntity).Name;

    /// <summary>Bilingual sheet title for exports.</summary>
    protected virtual string ExportTitle => typeof(TEntity).Name;

    /// <summary>Includes / base filters / data scope (IDOR protection — SPEC §10.2).</summary>
    protected virtual IQueryable<TEntity> BaseQuery(IQueryable<TEntity> q) => q;

    /// <summary>Additional filters from query string (?partnerTypeId=…).</summary>
    protected virtual IQueryable<TEntity> ApplyFilters(IQueryable<TEntity> q, IQueryCollection query) => q;

    /// <summary>Default ordering when no ?sort is given.</summary>
    protected virtual IQueryable<TEntity> DefaultOrder(IQueryable<TEntity> q)
        => HasSort ? q.OrderBy(e => EF.Property<int>(e, "SortOrder")).ThenBy(e => EF.Property<string>(e, HasCode ? "Code" : "Id"))
                   : q.OrderByDescending(e => e.CreatedAt);

    /// <summary>Hook before insert/update (derived rules, cross-field validation). Throw AppException to reject.</summary>
    protected virtual Task BeforeSaveAsync(TEntity entity, TUpsert input, bool isNew, CancellationToken ct) => Task.CompletedTask;

    protected virtual Task AfterSaveAsync(TEntity entity, bool isNew, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Hook before soft delete (e.g. check references). Throw ConflictException("error.inUse") to reject.</summary>
    protected virtual Task BeforeDeleteAsync(TEntity entity, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Maps an entity query to DTOs (override when Mapster projection is not enough).</summary>
    protected virtual IQueryable<TDto> Project(IQueryable<TEntity> q) => q.ProjectToType<TDto>();

    /// <summary>Applies upsert values to the entity (override for child collections).</summary>
    protected virtual void Apply(TUpsert input, TEntity entity) => input.Adapt(entity);

    // ------------------------------------------------------------------ endpoints

    [HttpGet, HasPermission(PermissionAction.View)]
    public virtual async Task<ActionResult<PagedResult<TDto>>> List([FromQuery] PageQuery q, CancellationToken ct)
    {
        var query = Filtered(q);
        var total = await query.LongCountAsync(ct);
        query = Sort(query, q.Sort);
        var items = await Project(query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)).ToListAsync(ct);
        return new PagedResult<TDto>(items, q.Page, q.PageSize, total);
    }

    /// <summary>Lightweight active lookup for comboboxes: id, code, name (cached).</summary>
    [HttpGet("lookup"), HasPermission(PermissionAction.View)]
    public virtual async Task<IActionResult> Lookup([FromQuery] string? q, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var query = BaseQuery(Db.Set<TEntity>().AsNoTracking());
        if (HasActive) query = query.Where(e => EF.Property<bool>(e, "IsActive"));
        if (!string.IsNullOrWhiteSpace(q) && HasSearch)
        {
            var norm = "%" + TextNormalizer.Normalize(q) + "%";
            query = query.Where(e => EF.Functions.ILike(EF.Property<string>(e, "SearchText"), norm));
        }
        query = DefaultOrder(query);
        var items = await query.Take(Math.Clamp(take, 1, 500)).Select(LookupSelector()).ToListAsync(ct);
        return Ok(items);
    }

    protected virtual Expression<Func<TEntity, LookupItem>> LookupSelector()
    {
        if (typeof(ICatalog).IsAssignableFrom(typeof(TEntity)))
            return e => new LookupItem(e.Id, EF.Property<string>(e, "Code"), EF.Property<string>(e, "NameVi"), EF.Property<string?>(e, "NameEn"));
        return e => new LookupItem(e.Id, HasCode ? EF.Property<string>(e, "Code") : e.Id.ToString(), e.Id.ToString(), null);
    }

    [HttpGet("{id:guid}"), HasPermission(PermissionAction.View)]
    public virtual async Task<ActionResult<TDto>> Get(Guid id, CancellationToken ct)
    {
        var dto = await Project(BaseQuery(Db.Set<TEntity>().AsNoTracking()).Where(e => e.Id == id)).FirstOrDefaultAsync(ct);
        return dto is null ? throw new NotFoundException(typeof(TEntity).Name) : dto;
    }

    [HttpPost, HasPermission(PermissionAction.Create)]
    public virtual async Task<ActionResult<TDto>> Create([FromBody] TUpsert input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var entity = new TEntity();
        await UpsertAsync(entity, input, true, ct);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, await LoadDto(entity.Id, ct));
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionAction.Edit)]
    public virtual async Task<ActionResult<TDto>> Update(Guid id, [FromBody] TUpsert input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var entity = await LoadForUpdate(id, ct);
        if (input.Version is { } v) Db.ChangeTracker.Entries<TEntity>().First(e => e.Entity == entity).Property(e => e.Version).OriginalValue = v;
        await UpsertAsync(entity, input, false, ct);
        return await LoadDto(entity.Id, ct);
    }

    [HttpDelete("{id:guid}"), HasPermission(PermissionAction.Delete)]
    public virtual async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await LoadForUpdate(id, ct);
        await BeforeDeleteAsync(entity, ct);
        Db.Set<TEntity>().Remove(entity); // converted to soft delete by the interceptor
        await Db.SaveChangesAsync(ct);
        await Cache.InvalidateRegionAsync(CacheRegion, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/lock"), HasPermission(PermissionAction.Edit)]
    public virtual async Task<IActionResult> Lock(Guid id, CancellationToken ct) { await SetActiveCore(id, false, ct); return NoContent(); }

    [HttpPost("{id:guid}/unlock"), HasPermission(PermissionAction.Edit)]
    public virtual async Task<IActionResult> Unlock(Guid id, CancellationToken ct) { await SetActiveCore(id, true, ct); return NoContent(); }

    [HttpGet("{id:guid}/history"), HasPermission(PermissionAction.View)]
    public virtual async Task<IActionResult> History(Guid id, CancellationToken ct)
    {
        _ = await LoadForUpdate(id, ct, tracking: false); // scope check
        var name = typeof(TEntity).Name;
        var key = id.ToString();
        var items = await Db.Set<AuditLog>().AsNoTracking().Where(a => a.EntityType == name && a.EntityId == key)
            .OrderByDescending(a => a.At).Take(200)
            .Select(a => new { a.At, a.UserName, action = a.Action.ToString(), a.OldValues, a.NewValues }).ToListAsync(ct);
        return Ok(items);
    }

    // ------------------------------------------------------------------ Excel

    [HttpGet("export"), HasPermission(PermissionAction.Export)]
    public virtual async Task<IActionResult> Export([FromQuery] PageQuery q, CancellationToken ct)
    {
        var entities = await Sort(Filtered(q), q.Sort).Take(100_000).ToListAsync(ct);
        var cols = ExcelColumns();
        var refMaps = await LoadRefMaps(cols, toCode: true, ct);
        var rows = entities.Select(e => ToRow(e.Adapt<TUpsert>(), e, cols, refMaps)).ToList();
        var bytes = Excel.Write(new ExcelSheet
        {
            Name = ExportTitle,
            Header = await ReportHeader(ExportTitle, null, ct),
            Columns = cols.Select(c => new ExcelColumn(c.Prop.Name, c.Header, c.Attr?.Width ?? 18, c.Attr?.Format)).ToList(),
            Rows = rows,
        });
        await Audit.WriteAsync(AuditAction.Export, typeof(TEntity).Name, null, message: $"{rows.Count} rows", ct: ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{ExportTitle}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpGet("import/template"), HasPermission(PermissionAction.Import)]
    public virtual IActionResult ImportTemplate()
    {
        var cols = ExcelColumns();
        var notes = new List<string> { L.IsEnglish ? "Columns marked * are required. Rows with an existing code are updated." : "Cột có dấu * là bắt buộc. Dòng có mã đã tồn tại sẽ được cập nhật." };
        foreach (var c in cols)
        {
            var t = Nullable.GetUnderlyingType(c.Prop.PropertyType) ?? c.Prop.PropertyType;
            var hint = L.IsEnglish ? c.Attr?.HintEn : c.Attr?.HintVi;
            if (t.IsEnum) hint = (hint is null ? "" : hint + " — ") + string.Join(", ", Enum.GetNames(t));
            if (t == typeof(bool)) hint ??= L.IsEnglish ? "x = yes, empty = no" : "x = có, để trống = không";
            if (c.Ref is not null) hint ??= L.IsEnglish ? "Code of the referenced catalog" : "Nhập mã của danh mục liên quan";
            if (hint is not null) notes.Add($"• {c.Header}: {hint}");
        }
        var bytes = Excel.Write(
            new ExcelSheet { Name = L.IsEnglish ? "Data" : "Du lieu", Columns = cols.Select(c => new ExcelColumn(c.Prop.Name, c.Header, c.Attr?.Width ?? 18, c.Attr?.Format, c.Required)).ToList() },
            new ExcelSheet { Name = L.IsEnglish ? "Guide" : "Huong dan", Notes = notes });
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{ExportTitle}_template.xlsx");
    }

    [HttpPost("import/preview"), HasPermission(PermissionAction.Import), RequestSizeLimit(20 * 1024 * 1024)]
    public virtual async Task<ActionResult<ImportPreview>> ImportPreview(IFormFile file, CancellationToken ct)
    {
        var bytes = await ReadUpload(file, ct);
        var (results, _) = await ParseImport(bytes, ct);
        var token = Guid.NewGuid().ToString("N");
        await Cache.SetAsync("import:" + token, new ImportCacheEntry(UserId, typeof(TEntity).Name, Convert.ToBase64String(bytes)), TimeSpan.FromMinutes(30), ct);
        var valid = results.Count(r => r.Errors.Count == 0);
        return new ImportPreview(token, results.Count, valid, results.Count - valid,
            results.Count(r => r.Errors.Count == 0 && r.Action == ImportAction.Create), results.Count(r => r.Errors.Count == 0 && r.Action == ImportAction.Update), results);
    }

    public sealed record ImportCommitRequest(string Token, bool SkipErrors = true);
    private sealed record ImportCacheEntry(Guid UserId, string Entity, string Content);

    [HttpPost("import/commit"), HasPermission(PermissionAction.Import)]
    public virtual async Task<ActionResult<ImportCommitResult>> ImportCommit([FromBody] ImportCommitRequest req, CancellationToken ct)
    {
        var entry = await Cache.GetAsync<ImportCacheEntry>("import:" + req.Token, ct);
        if (entry is null || entry.UserId != UserId || entry.Entity != typeof(TEntity).Name) throw new AppException("error.notFound", 404, "import");
        var (results, parsed) = await ParseImport(Convert.FromBase64String(entry.Content), ct);
        var errors = results.Where(r => r.Errors.Count > 0).ToList();
        if (errors.Count > 0 && !req.SkipErrors) return new ImportCommitResult(0, 0, results.Count, errors);

        int created = 0, updated = 0;
        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        foreach (var (row, input) in parsed)
        {
            if (row.Errors.Count > 0) continue;
            TEntity entity;
            if (row.Action == ImportAction.Update)
            {
                entity = await Db.Set<TEntity>().FirstAsync(e => EF.Property<string>(e, "Code") == row.Code, ct);
                await UpsertAsync(entity, input, false, ct, save: false);
                updated++;
            }
            else
            {
                entity = new TEntity();
                await UpsertAsync(entity, input, true, ct, save: false);
                created++;
            }
        }
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await Cache.RemoveAsync("import:" + req.Token, ct);
        await Cache.InvalidateRegionAsync(CacheRegion, ct);
        await Audit.WriteAsync(AuditAction.Import, typeof(TEntity).Name, null, message: $"created {created}, updated {updated}, skipped {errors.Count}", ct: ct);
        return new ImportCommitResult(created, updated, errors.Count, errors);
    }

    // ------------------------------------------------------------------ internals

    protected IQueryable<TEntity> Filtered(PageQuery q)
    {
        var query = Db.Set<TEntity>().AsNoTracking();
        if (q.IncludeDeleted && CurrentUser.Has(Application.Authorization.Permissions.Audit.View)) query = query.IgnoreQueryFilters();
        query = BaseQuery(query);
        if (!string.IsNullOrWhiteSpace(q.Q) && HasSearch)
        {
            var norm = "%" + TextNormalizer.Normalize(q.Q).Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(e => EF.Functions.ILike(EF.Property<string>(e, "SearchText"), norm));
        }
        if (q.IsActive is { } active && HasActive) query = query.Where(e => EF.Property<bool>(e, "IsActive") == active);
        return ApplyFilters(query, Request.Query);
    }

    private IQueryable<TEntity> Sort(IQueryable<TEntity> q, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return DefaultOrder(q);
        IOrderedQueryable<TEntity>? ordered = null;
        foreach (var part in sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var desc = part.StartsWith('-');
            var name = part.TrimStart('-', '+');
            var prop = typeof(TEntity).GetProperty(name, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || !IsSortable(prop.PropertyType)) continue; // whitelist: scalar entity properties only
            var param = Expression.Parameter(typeof(TEntity), "e");
            var body = Expression.Convert(Expression.Property(param, prop), typeof(object));
            var lambda = Expression.Lambda<Func<TEntity, object>>(body, param);
            ordered = ordered is null ? (desc ? q.OrderByDescending(lambda) : q.OrderBy(lambda)) : (desc ? ordered.ThenByDescending(lambda) : ordered.ThenBy(lambda));
        }
        return ordered ?? DefaultOrder(q);
    }

    private static bool IsSortable(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateOnly) || t == typeof(DateTimeOffset) || t == typeof(DateTime) || t == typeof(TimeOnly) || t == typeof(Guid);
    }

    private async Task<TEntity> LoadForUpdate(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = BaseQuery(tracking ? Db.Set<TEntity>() : Db.Set<TEntity>().AsNoTracking());
        return await q.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException(typeof(TEntity).Name);
    }

    private async Task<TDto> LoadDto(Guid id, CancellationToken ct)
        => await Project(Db.Set<TEntity>().AsNoTracking().Where(e => e.Id == id)).FirstAsync(ct);

    private async Task SetActiveCore(Guid id, bool active, CancellationToken ct)
    {
        if (!HasActive) throw new AppException("error.validation");
        var e = await LoadForUpdate(id, ct);
        typeof(TEntity).GetProperty("IsActive")!.SetValue(e, active);
        await Db.SaveChangesAsync(ct);
        await Cache.InvalidateRegionAsync(CacheRegion, ct);
        await Audit.WriteAsync(active ? AuditAction.Unlock : AuditAction.Lock, typeof(TEntity).Name, id.ToString(), ct: ct);
    }

    protected async Task UpsertAsync(TEntity entity, TUpsert input, bool isNew, CancellationToken ct, bool save = true)
    {
        if (HasCode)
        {
            var code = (string?)typeof(TUpsert).GetProperty("Code")?.GetValue(input);
            if (!string.IsNullOrWhiteSpace(code))
            {
                code = code.Trim();
                typeof(TUpsert).GetProperty("Code")!.SetValue(input, code);
                var id = entity.Id;
                var dup = await Db.Set<TEntity>().AnyAsync(e => EF.Property<string>(e, "Code") == code && e.Id != id, ct)
                          || Db.ChangeTracker.Entries<TEntity>().Any(x => x.State == EntityState.Added && x.Entity.Id != id && (string?)typeof(TEntity).GetProperty("Code")!.GetValue(x.Entity) == code);
                if (dup) throw new AppException("error.duplicateCode", 409, code) { Errors = new Dictionary<string, string[]> { ["code"] = [L.T("error.duplicateCode", code)] } };
            }
        }
        Apply(input, entity);
        await BeforeSaveAsync(entity, input, isNew, ct);
        if (isNew) Db.Set<TEntity>().Add(entity);
        if (!save) return;
        await Db.SaveChangesAsync(ct);
        await Cache.InvalidateRegionAsync(CacheRegion, ct);
        await AfterSaveAsync(entity, isNew, ct);
    }

    // ---- Excel helpers

    protected sealed record ColumnInfo(PropertyInfo Prop, ExcelColumnAttribute? Attr, Type? Ref, string Header, bool Required);

    protected virtual IReadOnlyList<ColumnInfo> ExcelColumns()
    {
        var props = typeof(TUpsert).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.Name != nameof(IHasVersion.Version) && p.GetCustomAttribute<ExcelIgnoreAttribute>() is null
                        && (IsSortable(p.PropertyType)))
            .Select((p, i) => (p, a: p.GetCustomAttribute<ExcelColumnAttribute>(), i))
            .OrderBy(x => x.a?.Order ?? 1000 + x.i)
            .Select(x => new ColumnInfo(x.p, x.a, x.p.GetCustomAttribute<ExcelRefAttribute>()?.Entity,
                x.a is null ? x.p.Name : L.Pick(x.a.HeaderVi, x.a.HeaderEn), x.a?.Required ?? false))
            .ToList();
        return props;
    }

    /// <summary>Loads id↔code maps for [ExcelRef] columns.</summary>
    private async Task<Dictionary<Type, Dictionary<string, string>>> LoadRefMaps(IReadOnlyList<ColumnInfo> cols, bool toCode, CancellationToken ct)
    {
        var maps = new Dictionary<Type, Dictionary<string, string>>();
        foreach (var t in cols.Where(c => c.Ref is not null).Select(c => c.Ref!).Distinct())
        {
            var method = typeof(CatalogControllerBase<TEntity, TDto, TUpsert>).GetMethod(nameof(LoadCodes), BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(t);
            var pairs = await (Task<List<(Guid Id, string Code)>>)method.Invoke(this, [ct])!;
            maps[t] = toCode ? pairs.ToDictionary(p => p.Id.ToString(), p => p.Code) : pairs.GroupBy(p => p.Code.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Id.ToString());
        }
        return maps;
    }

    private async Task<List<(Guid Id, string Code)>> LoadCodes<TRef>(CancellationToken ct) where TRef : Entity
    {
        var list = await Db.Set<TRef>().AsNoTracking().Select(e => new { e.Id, Code = EF.Property<string>(e, "Code") }).ToListAsync(ct);
        return list.Select(x => (x.Id, x.Code)).ToList();
    }

    private static Dictionary<string, object?> ToRow(TUpsert u, TEntity e, IReadOnlyList<ColumnInfo> cols, Dictionary<Type, Dictionary<string, string>> refMaps)
    {
        var d = new Dictionary<string, object?>();
        foreach (var c in cols)
        {
            var v = c.Prop.GetValue(u);
            if (c.Ref is not null && v is Guid g) v = refMaps[c.Ref].TryGetValue(g.ToString(), out var code) ? code : null;
            else if (v is Enum en) v = en.ToString();
            d[c.Prop.Name] = v;
        }
        return d;
    }

    private async Task<(List<ImportRowResult> Results, List<(ImportRowResult Row, TUpsert Input)> Parsed)> ParseImport(byte[] bytes, CancellationToken ct)
    {
        var cols = ExcelColumns();
        var headerMap = new Dictionary<string, ColumnInfo>();
        foreach (var c in cols)
        {
            headerMap[BlueMeal.Infrastructure.Excel.ExcelService.Norm(c.Prop.Name)] = c;
            if (c.Attr is not null)
            {
                headerMap[BlueMeal.Infrastructure.Excel.ExcelService.Norm(c.Attr.HeaderVi)] = c;
                headerMap[BlueMeal.Infrastructure.Excel.ExcelService.Norm(c.Attr.HeaderEn)] = c;
            }
        }
        using var ms = new MemoryStream(bytes);
        var read = Excel.Read(ms, headerMap.Keys.ToList());
        if (read.Rows.Count == 0) throw new AppException("import.noSheet");
        var refMaps = await LoadRefMaps(cols, toCode: false, ct);
        var existingCodes = HasCode
            ? (await Db.Set<TEntity>().AsNoTracking().Select(e => EF.Property<string>(e, "Code")).ToListAsync(ct)).Select(c => c.ToUpperInvariant()).ToHashSet()
            : new HashSet<string>();
        var validator = HttpContext.RequestServices.GetService<FluentValidation.IValidator<TUpsert>>();
        var seen = new HashSet<string>();
        var results = new List<ImportRowResult>();
        var parsed = new List<(ImportRowResult, TUpsert)>();

        foreach (var row in read.Rows)
        {
            var errors = new List<string>();
            var input = new TUpsert();
            foreach (var (header, raw) in row.Values)
            {
                if (!headerMap.TryGetValue(BlueMeal.Infrastructure.Excel.ExcelService.Norm(header), out var col)) continue;
                if (raw is null) continue;
                try
                {
                    if (col.Ref is not null)
                    {
                        if (refMaps[col.Ref].TryGetValue(raw.Trim().ToUpperInvariant(), out var refId)) col.Prop.SetValue(input, Guid.Parse(refId));
                        else errors.Add(L.T("import.notFoundRef", col.Header, raw));
                    }
                    else col.Prop.SetValue(input, ImportValueParser.Parse(raw, col.Prop.PropertyType));
                }
                catch (FormatException) { errors.Add(L.T("import.invalid", col.Header, raw)); }
            }
            foreach (var c in cols.Where(c => c.Required))
            {
                var v = c.Prop.GetValue(input);
                if (v is null || (v is string s && string.IsNullOrWhiteSpace(s))) errors.Add(L.T("import.required", c.Header));
            }
            if (errors.Count == 0 && validator is not null)
            {
                var vr = await validator.ValidateAsync(input, ct);
                errors.AddRange(vr.Errors.Select(e => L.Has(e.ErrorMessage) ? L.T(e.ErrorMessage) : e.ErrorMessage));
            }
            string? code = HasCode ? (string?)typeof(TUpsert).GetProperty("Code")?.GetValue(input) : null;
            code = code?.Trim();
            if (code is not null && !seen.Add(code.ToUpperInvariant())) errors.Add(L.T("error.duplicateCode", code));
            var action = code is not null && existingCodes.Contains(code.ToUpperInvariant()) ? ImportAction.Update : ImportAction.Create;
            var result = new ImportRowResult(row.RowNumber, code, action, errors);
            results.Add(result);
            parsed.Add((result, input));
        }
        return (results, parsed);
    }

    private static async Task<byte[]> ReadUpload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new AppException("import.noSheet");
        if (file.Length > 20 * 1024 * 1024) throw new AppException("error.fileSize", 413, 20);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx") throw new AppException("error.fileType", 415);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        // xlsx = zip: "PK\x03\x04" magic number check (do not trust the client MIME type)
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B) throw new AppException("error.fileType", 415);
        return bytes;
    }

    protected async Task<ExcelReportHeader> ReportHeader(string title, string? period, CancellationToken ct)
    {
        var org = await Db.Set<Organization>().AsNoTracking().FirstOrDefaultAsync(ct);
        return new ExcelReportHeader(L.Pick(org?.Name ?? "BlueMeal", org?.NameEn), title, period, CurrentUser.FullName ?? CurrentUser.UserName ?? "", DateTimeOffset.UtcNow, Signatures: false);
    }
}

public sealed record LookupItem(Guid Id, string Code, string NameVi, string? NameEn);

/// <summary>Parses Excel cell text into typed values (Vietnamese and invariant number formats).</summary>
public static class ImportValueParser
{
    public static object? Parse(string raw, Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        raw = raw.Trim();
        if (t == typeof(string)) return raw;
        if (t == typeof(bool)) return raw.ToLowerInvariant() switch { "x" or "1" or "true" or "có" or "co" or "yes" or "y" => true, "0" or "false" or "không" or "khong" or "no" or "n" or "" => false, _ => throw new FormatException() };
        if (t == typeof(decimal)) return ParseDecimal(raw);
        if (t == typeof(double)) return (double)ParseDecimal(raw);
        if (t == typeof(int)) return (int)Math.Round(ParseDecimal(raw));
        if (t == typeof(long)) return (long)Math.Round(ParseDecimal(raw));
        if (t == typeof(DateOnly))
        {
            if (DateOnly.TryParseExact(raw, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return d;
            if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var oa)) return DateOnly.FromDateTime(DateTime.FromOADate(oa));
            throw new FormatException();
        }
        if (t == typeof(TimeOnly))
        {
            if (TimeOnly.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var tm)) return tm;
            if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var frac)) return TimeOnly.FromTimeSpan(TimeSpan.FromDays(frac % 1));
            throw new FormatException();
        }
        if (t == typeof(Guid)) return Guid.Parse(raw);
        if (t.IsEnum)
        {
            if (Enum.TryParse(t, raw, true, out var e) && Enum.IsDefined(t, e!)) return e;
            throw new FormatException();
        }
        throw new FormatException();
    }

    public static decimal ParseDecimal(string raw)
    {
        raw = raw.Replace(" ", "").Replace("₫", "");
        // Invariant first (ClosedXML returns numbers as invariant strings), then Vietnamese "1.234,5"
        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) && !(raw.Contains(',') && raw.Contains('.') && raw.LastIndexOf(',') > raw.LastIndexOf('.')))
            return d;
        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Number, new System.Globalization.CultureInfo("vi-VN"), out d)) return d;
        throw new FormatException();
    }
}
