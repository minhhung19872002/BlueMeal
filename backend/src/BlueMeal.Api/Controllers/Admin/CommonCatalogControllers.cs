using BlueMeal.Api.Authorization;
using BlueMeal.Api.Catalog;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Admin;

// ------------------------------------------------------------------ Departments (tree, drag & drop) — SPEC §7.1.2

public sealed class DepartmentDto : CatalogDto
{
    public Guid? ParentId { get; set; }
    public string? ParentNameVi { get; set; }
    public string? Description { get; set; }
}

public sealed class DepartmentUpsert : CatalogUpsert
{
    [ExcelColumn("Mã đơn vị cha", "Parent code", Order = 4), ExcelRef(typeof(Department))]
    public Guid? ParentId { get; set; }
    [ExcelColumn("Mô tả", "Description", Order = 5, Width = 40)]
    public string? Description { get; set; }
}

public sealed class DepartmentUpsertValidator : CatalogUpsertValidator<DepartmentUpsert>;

[Route("api/v1/departments"), PermissionModule("department")]
public sealed class DepartmentsController : CatalogControllerBase<Department, DepartmentDto, DepartmentUpsert>
{
    protected override string ExportTitle => L.Pick("Co cau to chuc", "Departments");

    protected override async Task BeforeSaveAsync(Department e, DepartmentUpsert input, bool isNew, CancellationToken ct)
    {
        if (e.ParentId is null) return;
        // prevent cycles
        var cursor = e.ParentId;
        var guard = 0;
        while (cursor is not null && guard++ < 100)
        {
            if (cursor == e.Id) throw new BusinessRuleException("error.validation");
            cursor = await Db.Set<Department>().AsNoTracking().Where(d => d.Id == cursor).Select(d => d.ParentId).FirstOrDefaultAsync(ct);
        }
    }

    protected override async Task BeforeDeleteAsync(Department e, CancellationToken ct)
    {
        if (await Db.Set<Department>().AnyAsync(d => d.ParentId == e.Id, ct) || await Db.Set<Employee>().AnyAsync(x => x.DepartmentId == e.Id, ct))
            throw new ConflictException("error.inUse");
    }

    public sealed record TreeNode(Guid Id, string Code, string NameVi, string? NameEn, bool IsActive, int SortOrder, Guid? ParentId, List<TreeNode> Children);

    [HttpGet("tree"), HasPermission(Permissions.Department.View)]
    public async Task<IActionResult> Tree(CancellationToken ct)
    {
        var all = await Db.Set<Department>().AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Code).ToListAsync(ct);
        var nodes = all.ToDictionary(d => d.Id, d => new TreeNode(d.Id, d.Code, d.NameVi, d.NameEn, d.IsActive, d.SortOrder, d.ParentId, []));
        var roots = new List<TreeNode>();
        foreach (var n in nodes.Values)
            if (n.ParentId is { } p && nodes.TryGetValue(p, out var parent)) parent.Children.Add(n); else roots.Add(n);
        return Ok(roots);
    }

    public sealed record MoveRequest(Guid? ParentId, int Index);

    /// <summary>Drag and drop: move a node under a new parent at a given position; siblings are renumbered.</summary>
    [HttpPut("{id:guid}/move"), HasPermission(Permissions.Department.Edit)]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveRequest req, CancellationToken ct)
    {
        var node = await Db.Set<Department>().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("department");
        var cursor = req.ParentId;
        while (cursor is not null)
        {
            if (cursor == id) throw new BusinessRuleException("error.validation");
            cursor = await Db.Set<Department>().AsNoTracking().Where(d => d.Id == cursor).Select(d => d.ParentId).FirstOrDefaultAsync(ct);
        }
        node.ParentId = req.ParentId;
        var siblings = await Db.Set<Department>().Where(d => d.ParentId == req.ParentId && d.Id != id).OrderBy(d => d.SortOrder).ToListAsync(ct);
        siblings.Insert(Math.Clamp(req.Index, 0, siblings.Count), node);
        for (var i = 0; i < siblings.Count; i++) siblings[i].SortOrder = (i + 1) * 10;
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }
}

// ------------------------------------------------------------------ Country / Province / Ward — SPEC §5.1 (2-level administrative units)

public sealed class CountryDto : CatalogDto { public string? Iso2 { get; set; } }
public sealed class CountryUpsert : CatalogUpsert
{
    [ExcelColumn("Mã ISO-2", "ISO-2 code", Order = 4, Width = 10)] public string? Iso2 { get; set; }
}
public sealed class CountryUpsertValidator : CatalogUpsertValidator<CountryUpsert> { public CountryUpsertValidator() { RuleFor(x => x.Iso2).MaximumLength(2); } }

[Route("api/v1/countries"), PermissionModule("common_catalog")]
public sealed class CountriesController : CatalogControllerBase<Country, CountryDto, CountryUpsert>
{
    protected override string ExportTitle => L.Pick("Quoc gia", "Countries");
    protected override async Task BeforeDeleteAsync(Country e, CancellationToken ct)
    {
        if (await Db.Set<Province>().AnyAsync(p => p.CountryId == e.Id, ct)) throw new ConflictException("error.inUse");
    }
}

public sealed class ProvinceDto : CatalogDto
{
    public Guid CountryId { get; set; }
    public string? CountryNameVi { get; set; }
    public string? Kind { get; set; }
}
public sealed class ProvinceUpsert : CatalogUpsert
{
    [ExcelColumn("Mã quốc gia", "Country code", Required = true, Order = 4), ExcelRef(typeof(Country))] public Guid CountryId { get; set; }
    [ExcelColumn("Loại (Tỉnh/Thành phố)", "Kind", Order = 5)] public string? Kind { get; set; }
}
public sealed class ProvinceUpsertValidator : CatalogUpsertValidator<ProvinceUpsert> { public ProvinceUpsertValidator() { RuleFor(x => x.CountryId).NotEmpty(); } }

[Route("api/v1/provinces"), PermissionModule("common_catalog")]
public sealed class ProvincesController : CatalogControllerBase<Province, ProvinceDto, ProvinceUpsert>
{
    protected override string ExportTitle => L.Pick("Tinh thanh pho", "Provinces");
    protected override IQueryable<Province> ApplyFilters(IQueryable<Province> q, IQueryCollection query)
        => Guid.TryParse(query["countryId"], out var c) ? q.Where(x => x.CountryId == c) : q;
    protected override async Task BeforeDeleteAsync(Province e, CancellationToken ct)
    {
        if (await Db.Set<Ward>().AnyAsync(w => w.ProvinceId == e.Id, ct)) throw new ConflictException("error.inUse");
    }
}

public sealed class WardDto : CatalogDto
{
    public Guid ProvinceId { get; set; }
    public string? ProvinceNameVi { get; set; }
    public string? Kind { get; set; }
}
public sealed class WardUpsert : CatalogUpsert
{
    [ExcelColumn("Mã tỉnh/thành", "Province code", Required = true, Order = 4), ExcelRef(typeof(Province))] public Guid ProvinceId { get; set; }
    [ExcelColumn("Loại (Xã/Phường/Đặc khu)", "Kind", Order = 5)] public string? Kind { get; set; }
}
public sealed class WardUpsertValidator : CatalogUpsertValidator<WardUpsert> { public WardUpsertValidator() { RuleFor(x => x.ProvinceId).NotEmpty(); } }

/// <summary>Communes/wards are imported from Excel (ASSUMPTIONS A-13).</summary>
[Route("api/v1/wards"), PermissionModule("common_catalog")]
public sealed class WardsController : CatalogControllerBase<Ward, WardDto, WardUpsert>
{
    protected override string ExportTitle => L.Pick("Xa phuong", "Wards");
    protected override IQueryable<Ward> ApplyFilters(IQueryable<Ward> q, IQueryCollection query)
        => Guid.TryParse(query["provinceId"], out var p) ? q.Where(x => x.ProvinceId == p) : q;
}

// ------------------------------------------------------------------ Units of measure + converter — SPEC §7.1.3

public sealed class UnitDto : CatalogDto
{
    public UomType Type { get; set; }
    public decimal FactorToBase { get; set; }
    public string? Symbol { get; set; }
}
public sealed class UnitUpsert : CatalogUpsert
{
    [ExcelColumn("Loại (Mass/Volume/Count/Energy)", "Type", Required = true, Order = 4)] public UomType Type { get; set; }
    [ExcelColumn("Hệ số quy đổi về đơn vị cơ sở", "Factor to base unit", Required = true, Order = 5, Format = "0.######")] public decimal FactorToBase { get; set; } = 1;
    [ExcelColumn("Ký hiệu", "Symbol", Order = 6, Width = 10)] public string? Symbol { get; set; }
}
public sealed class UnitUpsertValidator : CatalogUpsertValidator<UnitUpsert>
{
    public UnitUpsertValidator() { RuleFor(x => x.FactorToBase).GreaterThan(0); RuleFor(x => x.Type).IsInEnum(); RuleFor(x => x.Symbol).MaximumLength(20); }
}

[Route("api/v1/units"), PermissionModule("common_catalog")]
public sealed class UnitsController : CatalogControllerBase<UnitOfMeasure, UnitDto, UnitUpsert>
{
    protected override string ExportTitle => L.Pick("Don vi do luong", "Units of measure");
    protected override IQueryable<UnitOfMeasure> ApplyFilters(IQueryable<UnitOfMeasure> q, IQueryCollection query)
        => Enum.TryParse<UomType>(query["type"], true, out var t) ? q.Where(x => x.Type == t) : q;

    /// <summary>Unit conversion tool: value × from.factor / to.factor (same type only).</summary>
    [HttpGet("convert"), HasPermission(Permissions.CommonCatalog.View)]
    public async Task<IActionResult> Convert([FromQuery] decimal value, [FromQuery] Guid from, [FromQuery] Guid to, CancellationToken ct)
    {
        var units = await Db.Set<UnitOfMeasure>().AsNoTracking().Where(u => u.Id == from || u.Id == to).ToListAsync(ct);
        var f = units.FirstOrDefault(u => u.Id == from) ?? throw new NotFoundException("unit");
        var t = units.FirstOrDefault(u => u.Id == to) ?? throw new NotFoundException("unit");
        if (f.Type != t.Type) throw new BusinessRuleException("error.validation");
        return Ok(new { value, from = f.Code, to = t.Code, result = Math.Round(value * f.FactorToBase / t.FactorToBase, 6) });
    }
}
