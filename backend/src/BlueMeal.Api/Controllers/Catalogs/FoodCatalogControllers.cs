using BlueMeal.Api.Authorization;
using BlueMeal.Api.Catalog;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using FluentValidation;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Catalogs;

// ================================================================== Warehouses (scoped per storekeeper) — SPEC §7.3.1

public sealed class WarehouseDto : CatalogDto
{
    public WarehouseType Type { get; set; }
    public string? Location { get; set; }
    public Guid? KeeperEmployeeId { get; set; }
    public string? KeeperName { get; set; }
    public decimal? TempMin { get; set; }
    public decimal? TempMax { get; set; }
    public string? Note { get; set; }
}

public sealed class WarehouseUpsert : CatalogUpsert
{
    [ExcelColumn("Loại kho (Dry/Chilled/Frozen/Other)", "Type", Required = true, Order = 4)] public WarehouseType Type { get; set; } = WarehouseType.Dry;
    [ExcelColumn("Vị trí", "Location", Order = 5, Width = 30)] public string? Location { get; set; }
    [ExcelColumn("Mã thủ kho (nhân viên)", "Storekeeper code", Order = 6), ExcelRef(typeof(Employee))] public Guid? KeeperEmployeeId { get; set; }
    [ExcelColumn("Nhiệt độ thấp nhất (°C)", "Min temp (°C)", Order = 7)] public decimal? TempMin { get; set; }
    [ExcelColumn("Nhiệt độ cao nhất (°C)", "Max temp (°C)", Order = 8)] public decimal? TempMax { get; set; }
    [ExcelColumn("Ghi chú", "Note", Order = 9, Width = 30)] public string? Note { get; set; }
}

public sealed class WarehouseUpsertValidator : CatalogUpsertValidator<WarehouseUpsert>
{
    public WarehouseUpsertValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x).Must(x => x.TempMin is null || x.TempMax is null || x.TempMin <= x.TempMax).WithMessage("error.validation");
    }
}

[Route("api/v1/warehouses"), PermissionModule("warehouse")]
public sealed class WarehousesController : CatalogControllerBase<Warehouse, WarehouseDto, WarehouseUpsert>
{
    protected override string ExportTitle => L.Pick("Danh muc kho", "Warehouses");

    /// <summary>Storekeepers only see warehouses assigned to them (SPEC §4).</summary>
    protected override IQueryable<Warehouse> BaseQuery(IQueryable<Warehouse> q)
    {
        var scope = CurrentUser.WarehouseScope;
        return scope is null ? q : q.Where(w => scope.Contains(w.Id));
    }

    protected override IQueryable<WarehouseDto> Project(IQueryable<Warehouse> q) => q.Select(w => new WarehouseDto
    {
        Id = w.Id, Code = w.Code, NameVi = w.NameVi, NameEn = w.NameEn, IsActive = w.IsActive, SortOrder = w.SortOrder, Version = w.Version, CreatedAt = w.CreatedAt, UpdatedAt = w.UpdatedAt,
        Type = w.Type, Location = w.Location, KeeperEmployeeId = w.KeeperEmployeeId, TempMin = w.TempMin, TempMax = w.TempMax, Note = w.Note,
        KeeperName = Db.Set<Employee>().Where(e => e.Id == w.KeeperEmployeeId).Select(e => e.FullName).FirstOrDefault(),
    });

    protected override async Task BeforeDeleteAsync(Warehouse e, CancellationToken ct)
    {
        if (await Db.Set<StockBalance>().AnyAsync(b => b.WarehouseId == e.Id && b.Quantity != 0, ct)) throw new ConflictException("error.inUse");
    }
}

// ================================================================== Food groups (tree) — SPEC §7.3.2

public sealed class FoodGroupDto : CatalogDto
{
    public Guid? ParentId { get; set; }
    public string? ParentNameVi { get; set; }
    public bool IsAnimalSource { get; set; }
}

public sealed class FoodGroupUpsert : CatalogUpsert
{
    [ExcelColumn("Mã nhóm cha", "Parent code", Order = 4), ExcelRef(typeof(FoodGroup))] public Guid? ParentId { get; set; }
    [ExcelColumn("Nguồn gốc động vật", "Animal source", Order = 5)] public bool IsAnimalSource { get; set; }
}

public sealed class FoodGroupUpsertValidator : CatalogUpsertValidator<FoodGroupUpsert>;

[Route("api/v1/food-groups"), PermissionModule("food_group")]
public sealed class FoodGroupsController : CatalogControllerBase<FoodGroup, FoodGroupDto, FoodGroupUpsert>
{
    protected override string ExportTitle => L.Pick("Phan nhom thuc pham", "Food groups");
    protected override string CacheRegion => "catalog:food";

    protected override async Task BeforeSaveAsync(FoodGroup e, FoodGroupUpsert input, bool isNew, CancellationToken ct)
    {
        var cursor = e.ParentId; var guard = 0;
        while (cursor is not null && guard++ < 50)
        {
            if (cursor == e.Id) throw new BusinessRuleException("error.validation");
            cursor = await Db.Set<FoodGroup>().AsNoTracking().Where(d => d.Id == cursor).Select(d => d.ParentId).FirstOrDefaultAsync(ct);
        }
    }

    protected override async Task BeforeDeleteAsync(FoodGroup e, CancellationToken ct)
    {
        if (await Db.Set<FoodGroup>().AnyAsync(g => g.ParentId == e.Id, ct) || await Db.Set<Food>().AnyAsync(f => f.FoodGroupId == e.Id, ct)) throw new ConflictException("error.inUse");
    }

    public sealed record GroupNode(Guid Id, string Code, string NameVi, string? NameEn, bool IsAnimalSource, bool IsActive, Guid? ParentId, int FoodCount, List<GroupNode> Children);

    [HttpGet("tree"), HasPermission(Permissions.FoodGroup.View)]
    public async Task<IActionResult> Tree(CancellationToken ct)
    {
        var counts = await Db.Set<Food>().AsNoTracking().GroupBy(f => f.FoodGroupId).Select(g => new { g.Key, n = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.n, ct);
        var all = await Db.Set<FoodGroup>().AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Code).ToListAsync(ct);
        var nodes = all.ToDictionary(d => d.Id, d => new GroupNode(d.Id, d.Code, d.NameVi, d.NameEn, d.IsAnimalSource, d.IsActive, d.ParentId, counts.GetValueOrDefault(d.Id), []));
        var roots = new List<GroupNode>();
        foreach (var n in nodes.Values) if (n.ParentId is { } p && nodes.TryGetValue(p, out var parent)) parent.Children.Add(n); else roots.Add(n);
        return Ok(roots);
    }
}

// ================================================================== Nutrients — SPEC §5.3

public sealed class NutrientDto : CatalogDto
{
    public string Unit { get; set; } = default!;
    public NutrientGroup Group { get; set; }
    public bool IsCore { get; set; }
    public int Decimals { get; set; }
}

public sealed class NutrientUpsert : CatalogUpsert
{
    [ExcelColumn("Đơn vị (kcal/g/mg/µg)", "Unit", Required = true, Order = 4)] public string Unit { get; set; } = "g";
    [ExcelColumn("Nhóm (Energy/Macro/Mineral/Vitamin/Other)", "Group", Required = true, Order = 5)] public NutrientGroup Group { get; set; }
    [ExcelColumn("Chỉ tiêu chính", "Core", Order = 6)] public bool IsCore { get; set; }
    [ExcelColumn("Số lẻ hiển thị", "Decimals", Order = 7)] public int Decimals { get; set; } = 1;
}

public sealed class NutrientUpsertValidator : CatalogUpsertValidator<NutrientUpsert>
{
    public NutrientUpsertValidator() { RuleFor(x => x.Unit).NotEmpty().MaximumLength(10); RuleFor(x => x.Decimals).InclusiveBetween(0, 4); RuleFor(x => x.Group).IsInEnum(); }
}

[Route("api/v1/nutrients"), PermissionModule("nutrient")]
public sealed class NutrientsController : CatalogControllerBase<Nutrient, NutrientDto, NutrientUpsert>
{
    protected override string ExportTitle => L.Pick("Chat dinh duong", "Nutrients");
    protected override string CacheRegion => "catalog:food";
    protected override IQueryable<Nutrient> ApplyFilters(IQueryable<Nutrient> q, IQueryCollection query)
    {
        if (Enum.TryParse<NutrientGroup>(query["group"], true, out var g)) q = q.Where(x => x.Group == g);
        if (query["core"] == "true") q = q.Where(x => x.IsCore);
        return q;
    }

    protected override async Task BeforeDeleteAsync(Nutrient e, CancellationToken ct)
    {
        if (e.Code is "ENERGY" or "PROTEIN" or "LIPID" or "GLUCID") throw new BusinessRuleException("validation.systemRole");
        if (await Db.Set<FoodNutrient>().AnyAsync(f => f.NutrientId == e.Id, ct)) throw new ConflictException("error.inUse");
    }
}

// ================================================================== Foods (food composition table) — SPEC §7.3.3

public sealed class FoodDto : CatalogDto
{
    public Guid FoodGroupId { get; set; }
    public string? FoodGroupNameVi { get; set; }
    public decimal RefuseRate { get; set; }
    public bool? IsAnimalSourceOverride { get; set; }
    public bool IsAnimalSource { get; set; }
    public DataSource Source { get; set; }
    public string? SourceVersion { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public bool LocallyModified { get; set; }
    public bool IsLocked { get; set; }
    public string? Note { get; set; }
    /// <summary>Nutrient code → value per 100 g edible portion.</summary>
    public Dictionary<string, decimal> Nutrients { get; set; } = new();
}

public sealed class FoodUpsert : CatalogUpsert
{
    [ExcelColumn("Mã nhóm thực phẩm", "Food group code", Required = true, Order = 4), ExcelRef(typeof(FoodGroup))] public Guid FoodGroupId { get; set; }
    [ExcelColumn("Tỷ lệ thải bỏ (%)", "Refuse rate (%)", Order = 5, Format = "0.0")] public decimal RefuseRate { get; set; }
    [ExcelColumn("Nguồn động vật (ghi đè)", "Animal source override", Order = 6)] public bool? IsAnimalSourceOverride { get; set; }
    [ExcelColumn("Ghi chú", "Note", Order = 7, Width = 30)] public string? Note { get; set; }
    /// <summary>Nutrient code → value per 100 g edible portion (Excel: one column per nutrient).</summary>
    [ExcelIgnore] public Dictionary<string, decimal?>? Nutrients { get; set; }
}

public sealed class FoodUpsertValidator : CatalogUpsertValidator<FoodUpsert>
{
    public FoodUpsertValidator()
    {
        RuleFor(x => x.FoodGroupId).NotEmpty();
        RuleFor(x => x.RefuseRate).InclusiveBetween(0, 99.9m);
        RuleForEach(x => x.Nutrients).Must(kv => kv.Value is null || (kv.Value >= 0 && kv.Value < 1_000_000)).When(x => x.Nutrients is not null).WithMessage("error.validation");
    }
}

[Route("api/v1/foods"), PermissionModule("food")]
public sealed class FoodsController : CatalogControllerBase<Food, FoodDto, FoodUpsert>
{
    protected override string ExportTitle => L.Pick("Bang thanh phan thuc pham", "Food composition");
    protected override string CacheRegion => "catalog:food";

    protected override IQueryable<Food> ApplyFilters(IQueryable<Food> q, IQueryCollection query)
    {
        if (Guid.TryParse(query["foodGroupId"], out var g))
        {
            // include sub-groups (one level is enough for the standard tree)
            var children = Db.Set<FoodGroup>().Where(x => x.ParentId == g).Select(x => x.Id);
            q = q.Where(f => f.FoodGroupId == g || children.Contains(f.FoodGroupId));
        }
        if (Enum.TryParse<DataSource>(query["source"], true, out var s)) q = q.Where(f => f.Source == s);
        if (query["sample"] == "true") q = q.Where(f => f.SourceVersion == "SAMPLE");
        return q;
    }

    protected override IQueryable<FoodDto> Project(IQueryable<Food> q) => q.Select(f => new FoodDto
    {
        Id = f.Id, Code = f.Code, NameVi = f.NameVi, NameEn = f.NameEn, IsActive = f.IsActive, SortOrder = f.SortOrder, Version = f.Version, CreatedAt = f.CreatedAt, UpdatedAt = f.UpdatedAt,
        FoodGroupId = f.FoodGroupId, FoodGroupNameVi = f.FoodGroup!.NameVi, RefuseRate = f.RefuseRate, IsAnimalSourceOverride = f.IsAnimalSourceOverride,
        IsAnimalSource = f.IsAnimalSourceOverride ?? f.FoodGroup.IsAnimalSource, Source = f.Source, SourceVersion = f.SourceVersion, LastSyncedAt = f.LastSyncedAt,
        LocallyModified = f.LocallyModified, IsLocked = f.IsLocked, Note = f.Note,
    });

    protected override async Task PostProcessAsync(IList<FoodDto> items, bool single, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        var values = await Db.Set<FoodNutrient>().AsNoTracking().Where(n => ids.Contains(n.FoodId)).Select(n => new { n.FoodId, n.Nutrient!.Code, n.ValuePer100gEdible }).ToListAsync(ct);
        foreach (var g in values.GroupBy(v => v.FoodId))
        {
            var dto = items.First(i => i.Id == g.Key);
            dto.Nutrients = g.ToDictionary(v => v.Code, v => v.ValuePer100gEdible);
        }
    }

    protected override IQueryable<Food> LoadForImport(IQueryable<Food> q) => q.Include(f => f.Nutrients);

    private Dictionary<string, Guid>? _nutrientIds;
    private async Task<Dictionary<string, Guid>> NutrientIds(CancellationToken ct)
        => _nutrientIds ??= await Db.Set<Nutrient>().AsNoTracking().ToDictionaryAsync(n => n.Code, n => n.Id, StringComparer.OrdinalIgnoreCase, ct);

    protected override void Apply(FoodUpsert input, Food entity)
    {
        entity.Code = input.Code; entity.NameVi = input.NameVi; entity.NameEn = input.NameEn; entity.IsActive = input.IsActive; entity.SortOrder = input.SortOrder;
        entity.FoodGroupId = input.FoodGroupId; entity.RefuseRate = input.RefuseRate; entity.IsAnimalSourceOverride = input.IsAnimalSourceOverride; entity.Note = input.Note;
    }

    protected override async Task BeforeSaveAsync(Food e, FoodUpsert input, bool isNew, CancellationToken ct)
    {
        if (!isNew && e.IsLocked && !CurrentUser.Has(Permissions.Food.Unlock)) throw new AppException("food.lockedApi", 423);
        if (!isNew && e.Source == DataSource.VddApi) e.LocallyModified = true;
        if (isNew && e.Source == 0) e.Source = DataSource.Manual;
        if (input.Nutrients is null) return;
        var ids = await NutrientIds(ct);
        if (!isNew && !Db.Entry(e).Collection(x => x.Nutrients).IsLoaded) await Db.Entry(e).Collection(x => x.Nutrients).LoadAsync(ct);
        foreach (var (code, value) in input.Nutrients)
        {
            if (!ids.TryGetValue(code, out var nid)) continue;
            var existing = e.Nutrients.FirstOrDefault(n => n.NutrientId == nid);
            if (value is null) { if (existing is not null) e.Nutrients.Remove(existing); }
            else if (existing is null) e.Nutrients.Add(new FoodNutrient { FoodId = e.Id, NutrientId = nid, ValuePer100gEdible = value.Value });
            else existing.ValuePer100gEdible = value.Value;
        }
    }

    protected override async Task BeforeDeleteAsync(Food e, CancellationToken ct)
    {
        if (await Db.Set<Ingredient>().AnyAsync(i => i.FoodId == e.Id, ct)) throw new ConflictException("error.inUse");
    }

    // ---- dynamic nutrient columns for Excel (one column per nutrient: "PROTEIN (g)")

    private List<(string Code, string Unit)>? _nutrientCols;
    private async Task<List<(string Code, string Unit)>> NutrientColumns(CancellationToken ct)
        => _nutrientCols ??= (await Db.Set<Nutrient>().AsNoTracking().Where(n => n.IsActive).OrderBy(n => n.SortOrder).Select(n => new { n.Code, n.Unit }).ToListAsync(ct))
            .Select(n => (n.Code, n.Unit)).ToList();

    protected override async Task<IReadOnlyList<ExcelColumn>> ExtraExcelColumnsAsync(CancellationToken ct)
        => (await NutrientColumns(ct)).Select(n => new ExcelColumn("n:" + n.Code, $"{n.Code} ({n.Unit})", 11, "0.##")).ToList();

    protected override async Task FillExtraExportAsync(IReadOnlyList<Food> entities, IReadOnlyList<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        var ids = entities.Select(e => e.Id).ToList();
        var values = await Db.Set<FoodNutrient>().AsNoTracking().Where(n => ids.Contains(n.FoodId)).Select(n => new { n.FoodId, n.Nutrient!.Code, n.ValuePer100gEdible }).ToListAsync(ct);
        var byFood = values.GroupBy(v => v.FoodId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Code, x => x.ValuePer100gEdible));
        for (var i = 0; i < entities.Count; i++)
            if (byFood.TryGetValue(entities[i].Id, out var m))
                foreach (var (code, v) in m) rows[i]["n:" + code] = v;
    }

    protected override void ReadExtraImport(IReadOnlyDictionary<string, string?> raw, IReadOnlyList<ExcelColumn> extra, FoodUpsert input, List<string> errors)
    {
        input.Nutrients = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in extra)
        {
            var header = raw.Keys.FirstOrDefault(k => string.Equals(k.Split(' ')[0], col.Key[2..], StringComparison.OrdinalIgnoreCase));
            if (header is null || raw[header] is not { } v) continue;
            try { input.Nutrients[col.Key[2..]] = ImportValueParser.ParseDecimal(v); }
            catch (FormatException) { errors.Add(L.T("import.invalid", col.Header, v)); }
        }
    }

    // ---- quick-edit nutrient grid

    public sealed record GridRow(Guid Id, string Code, string NameVi, string? NameEn, decimal RefuseRate, bool IsLocked, DataSource Source, uint Version, Dictionary<string, decimal> Values);

    /// <summary>Paged foods × selected nutrients grid for fast inline editing.</summary>
    [HttpGet("grid"), HasPermission(Permissions.Food.View)]
    public async Task<IActionResult> Grid([FromQuery(Name = "")] PageQuery q, [FromQuery] string? nutrients, CancellationToken ct)
    {
        var codes = (nutrients ?? "ENERGY,PROTEIN,LIPID,GLUCID,FIBER,CA,FE,NA,VIT_C").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(30).ToList();
        var query = Filtered(q).OrderBy(f => f.SortOrder).ThenBy(f => f.Code);
        var total = await query.LongCountAsync(ct);
        var foods = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(f => new { f.Id, f.Code, f.NameVi, f.NameEn, f.RefuseRate, f.IsLocked, f.Source, f.Version }).ToListAsync(ct);
        var ids = foods.Select(f => f.Id).ToList();
        var vals = await Db.Set<FoodNutrient>().AsNoTracking().Where(n => ids.Contains(n.FoodId) && codes.Contains(n.Nutrient!.Code))
            .Select(n => new { n.FoodId, n.Nutrient!.Code, n.ValuePer100gEdible }).ToListAsync(ct);
        var rows = foods.Select(f => new GridRow(f.Id, f.Code, f.NameVi, f.NameEn, f.RefuseRate, f.IsLocked, f.Source, f.Version,
            vals.Where(v => v.FoodId == f.Id).ToDictionary(v => v.Code, v => v.ValuePer100gEdible))).ToList();
        return Ok(new PagedResult<GridRow>(rows, q.Page, q.PageSize, total));
    }

    /// <summary>Inline update of nutrient values (null removes).</summary>
    [HttpPut("{id:guid}/nutrients"), HasPermission(Permissions.Food.Edit)]
    public async Task<IActionResult> SetNutrients(Guid id, [FromBody] Dictionary<string, decimal?> values, CancellationToken ct)
    {
        var food = await Db.Set<Food>().Include(f => f.Nutrients).FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("food");
        if (food.IsLocked && !CurrentUser.Has(Permissions.Food.Unlock)) throw new AppException("food.lockedApi", 423);
        if (values.Values.Any(v => v is < 0 or >= 1_000_000)) throw new AppException("error.validation");
        await BeforeSaveAsync(food, new FoodUpsert { Nutrients = values, Code = food.Code, NameVi = food.NameVi, FoodGroupId = food.FoodGroupId }, false, ct);
        food.UpdatedAt = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync(ct);
        await Cache.InvalidateRegionAsync(CacheRegion, ct);
        return NoContent();
    }

    /// <summary>Unlock/relock API-sourced data for local editing (requires food.unlock — SPEC §7.3.3).</summary>
    [HttpPost("{id:guid}/data-lock"), HasPermission(Permissions.Food.Unlock)]
    public async Task<IActionResult> SetDataLock(Guid id, [FromQuery] bool locked, CancellationToken ct)
    {
        var food = await Db.Set<Food>().FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("food");
        food.IsLocked = locked;
        await Db.SaveChangesAsync(ct);
        await Audit.WriteAsync(locked ? AuditAction.Lock : AuditAction.Unlock, nameof(Food), id.ToString(), message: "data-lock", ct: ct);
        return NoContent();
    }
}

// ================================================================== Food safety & quality specs — SPEC §5.3, §7.3.4

public sealed class FoodSafetySpecDto
{
    public Guid Id { get; set; }
    public Guid? FoodId { get; set; }
    public string? FoodNameVi { get; set; }
    public Guid? IngredientId { get; set; }
    public string? IngredientNameVi { get; set; }
    public string? SensoryColor { get; set; }
    public string? SensorySmell { get; set; }
    public string? SensoryState { get; set; }
    public decimal? StorageTempMin { get; set; }
    public decimal? StorageTempMax { get; set; }
    public int? MaxShelfDaysAfterReceipt { get; set; }
    public string? RequiredDocuments { get; set; }
    public string? Allergens { get; set; }
    public string? Note { get; set; }
    public uint Version { get; set; }
}

public sealed class FoodSafetySpecUpsert : IHasVersion
{
    [ExcelColumn("Mã thực phẩm", "Food code", Order = 1), ExcelRef(typeof(Food))] public Guid? FoodId { get; set; }
    [ExcelColumn("Mã nguyên liệu", "Ingredient code", Order = 2), ExcelRef(typeof(Ingredient))] public Guid? IngredientId { get; set; }
    [ExcelColumn("Màu sắc", "Color", Order = 3, Width = 24)] public string? SensoryColor { get; set; }
    [ExcelColumn("Mùi", "Smell", Order = 4, Width = 24)] public string? SensorySmell { get; set; }
    [ExcelColumn("Trạng thái", "State", Order = 5, Width = 24)] public string? SensoryState { get; set; }
    [ExcelColumn("Nhiệt độ bảo quản thấp nhất", "Storage temp min", Order = 6)] public decimal? StorageTempMin { get; set; }
    [ExcelColumn("Nhiệt độ bảo quản cao nhất", "Storage temp max", Order = 7)] public decimal? StorageTempMax { get; set; }
    [ExcelColumn("Hạn dùng tối đa sau nhập (ngày)", "Max shelf days after receipt", Order = 8)] public int? MaxShelfDaysAfterReceipt { get; set; }
    [ExcelColumn("Giấy tờ yêu cầu", "Required documents", Order = 9, Width = 30)] public string? RequiredDocuments { get; set; }
    [ExcelColumn("Chất gây dị ứng", "Allergens", Order = 10)] public string? Allergens { get; set; }
    [ExcelColumn("Ghi chú", "Note", Order = 11)] public string? Note { get; set; }
    public uint? Version { get; set; }
}

public sealed class FoodSafetySpecUpsertValidator : AbstractValidator<FoodSafetySpecUpsert>
{
    public FoodSafetySpecUpsertValidator()
    {
        RuleFor(x => x).Must(x => x.FoodId is not null || x.IngredientId is not null).WithMessage("error.validation");
        RuleFor(x => x).Must(x => x.StorageTempMin is null || x.StorageTempMax is null || x.StorageTempMin <= x.StorageTempMax).WithMessage("error.validation");
        RuleFor(x => x.MaxShelfDaysAfterReceipt).InclusiveBetween(0, 3650).When(x => x.MaxShelfDaysAfterReceipt is not null);
    }
}

[Route("api/v1/food-safety-specs"), PermissionModule("food_safety")]
public sealed class FoodSafetySpecsController : CatalogControllerBase<FoodSafetySpec, FoodSafetySpecDto, FoodSafetySpecUpsert>
{
    protected override string ExportTitle => L.Pick("Tieu chuan ATTP", "Food safety specs");
    protected override IQueryable<FoodSafetySpecDto> Project(IQueryable<FoodSafetySpec> q) => q.Select(s => new FoodSafetySpecDto
    {
        Id = s.Id, FoodId = s.FoodId, IngredientId = s.IngredientId, SensoryColor = s.SensoryColor, SensorySmell = s.SensorySmell, SensoryState = s.SensoryState,
        StorageTempMin = s.StorageTempMin, StorageTempMax = s.StorageTempMax, MaxShelfDaysAfterReceipt = s.MaxShelfDaysAfterReceipt, RequiredDocuments = s.RequiredDocuments,
        Allergens = s.Allergens, Note = s.Note, Version = s.Version,
        FoodNameVi = Db.Set<Food>().Where(f => f.Id == s.FoodId).Select(f => f.NameVi).FirstOrDefault(),
        IngredientNameVi = Db.Set<Ingredient>().Where(f => f.Id == s.IngredientId).Select(f => f.NameVi).FirstOrDefault(),
    });
    protected override IQueryable<FoodSafetySpec> ApplyFilters(IQueryable<FoodSafetySpec> q, IQueryCollection query)
    {
        if (Guid.TryParse(query["foodId"], out var f)) q = q.Where(x => x.FoodId == f);
        if (Guid.TryParse(query["ingredientId"], out var i)) q = q.Where(x => x.IngredientId == i);
        if (query["q"].ToString() is { Length: > 0 } term)
        {
            var norm = "%" + BlueMeal.Domain.Common.TextNormalizer.Normalize(term) + "%";
            q = q.Where(x => Db.Set<Food>().Any(f => f.Id == x.FoodId && EF.Functions.ILike(f.SearchText!, norm)) || Db.Set<Ingredient>().Any(g => g.Id == x.IngredientId && EF.Functions.ILike(g.SearchText!, norm)));
        }
        return q;
    }
}

// ================================================================== Ingredients (purchased items) — SPEC §7.3.5

public sealed class IngredientDto : CatalogDto
{
    public Guid? FoodId { get; set; }
    public string? FoodCode { get; set; }
    public string? FoodNameVi { get; set; }
    public Guid? PurchaseUnitId { get; set; }
    public string? PurchaseUnitSymbol { get; set; }
    public decimal GramsPerUnit { get; set; }
    public Guid? DefaultWarehouseId { get; set; }
    public decimal MinStock { get; set; }
    public int? ShelfLifeDays { get; set; }
    public string? StorageCondition { get; set; }
    public decimal? ReferencePrice { get; set; }
    public Guid? PreferredPartnerId { get; set; }
    public string? PreferredPartnerNameVi { get; set; }
    public string? Allergens { get; set; }
    public string? Barcode { get; set; }
    public string? Note { get; set; }
    public decimal StockOnHand { get; set; }
}

public sealed class IngredientUpsert : CatalogUpsert
{
    [ExcelColumn("Mã thực phẩm (bảng thành phần)", "Food code", Order = 4), ExcelRef(typeof(Food))] public Guid? FoodId { get; set; }
    [ExcelColumn("Mã đơn vị mua", "Purchase unit code", Order = 5), ExcelRef(typeof(UnitOfMeasure))] public Guid? PurchaseUnitId { get; set; }
    [ExcelColumn("Quy đổi ra gram / đơn vị mua", "Grams per unit", Required = true, Order = 6, Format = "#,##0.##")] public decimal GramsPerUnit { get; set; } = 1000m;
    [ExcelColumn("Mã kho mặc định", "Default warehouse code", Order = 7), ExcelRef(typeof(Warehouse))] public Guid? DefaultWarehouseId { get; set; }
    [ExcelColumn("Tồn tối thiểu", "Minimum stock", Order = 8, Format = "#,##0.##")] public decimal MinStock { get; set; }
    [ExcelColumn("Hạn dùng (ngày)", "Shelf life (days)", Order = 9)] public int? ShelfLifeDays { get; set; }
    [ExcelColumn("Điều kiện bảo quản", "Storage condition", Order = 10, Width = 26)] public string? StorageCondition { get; set; }
    [ExcelColumn("Giá tham chiếu (₫/đơn vị mua)", "Reference price (VND/unit)", Order = 11, Format = "#,##0")] public decimal? ReferencePrice { get; set; }
    [ExcelColumn("Mã nhà cung cấp ưu tiên", "Preferred supplier code", Order = 12), ExcelRef(typeof(Partner))] public Guid? PreferredPartnerId { get; set; }
    [ExcelColumn("Chất gây dị ứng", "Allergens", Order = 13)] public string? Allergens { get; set; }
    [ExcelColumn("Mã vạch", "Barcode", Order = 14)] public string? Barcode { get; set; }
    [ExcelColumn("Ghi chú", "Note", Order = 15, Width = 30)] public string? Note { get; set; }
}

public sealed class IngredientUpsertValidator : CatalogUpsertValidator<IngredientUpsert>
{
    public IngredientUpsertValidator()
    {
        RuleFor(x => x.GramsPerUnit).GreaterThan(0).LessThan(1_000_000);
        RuleFor(x => x.MinStock).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReferencePrice).GreaterThanOrEqualTo(0).When(x => x.ReferencePrice is not null);
        RuleFor(x => x.ShelfLifeDays).InclusiveBetween(0, 3650).When(x => x.ShelfLifeDays is not null);
    }
}

[Route("api/v1/ingredients"), PermissionModule("ingredient")]
public sealed class IngredientsController(TimeProvider clock) : CatalogControllerBase<Ingredient, IngredientDto, IngredientUpsert>
{
    protected override string ExportTitle => L.Pick("Danh muc nguyen lieu", "Ingredients");
    protected override string CacheRegion => "catalog:food";

    protected override IQueryable<Ingredient> ApplyFilters(IQueryable<Ingredient> q, IQueryCollection query)
    {
        if (Guid.TryParse(query["foodId"], out var f)) q = q.Where(x => x.FoodId == f);
        if (query["unmapped"] == "true") q = q.Where(x => x.FoodId == null);
        if (query["barcode"].ToString() is { Length: > 0 } bc) q = q.Where(x => x.Barcode == bc);
        if (Guid.TryParse(query["warehouseId"], out var w)) q = q.Where(x => x.DefaultWarehouseId == w);
        return q;
    }

    protected override IQueryable<IngredientDto> Project(IQueryable<Ingredient> q) => q.Select(i => new IngredientDto
    {
        Id = i.Id, Code = i.Code, NameVi = i.NameVi, NameEn = i.NameEn, IsActive = i.IsActive, SortOrder = i.SortOrder, Version = i.Version, CreatedAt = i.CreatedAt, UpdatedAt = i.UpdatedAt,
        FoodId = i.FoodId, FoodCode = i.Food!.Code, FoodNameVi = i.Food.NameVi, PurchaseUnitId = i.PurchaseUnitId, PurchaseUnitSymbol = i.PurchaseUnit!.Symbol,
        GramsPerUnit = i.GramsPerUnit, DefaultWarehouseId = i.DefaultWarehouseId, MinStock = i.MinStock, ShelfLifeDays = i.ShelfLifeDays, StorageCondition = i.StorageCondition,
        ReferencePrice = i.ReferencePrice, PreferredPartnerId = i.PreferredPartnerId, Allergens = i.Allergens, Barcode = i.Barcode, Note = i.Note,
        PreferredPartnerNameVi = Db.Set<Partner>().Where(p => p.Id == i.PreferredPartnerId).Select(p => p.NameVi).FirstOrDefault(),
        StockOnHand = Db.Set<StockBalance>().Where(b => b.IngredientId == i.Id).Sum(b => (decimal?)b.Quantity) ?? 0,
    });

    protected override async Task BeforeSaveAsync(Ingredient e, IngredientUpsert input, bool isNew, CancellationToken ct)
    {
        // record reference price changes in the price history (SPEC §5.3)
        var entry = Db.ChangeTracker.Entries<Ingredient>().FirstOrDefault(x => x.Entity == e);
        var oldPrice = isNew || entry is null ? null : (decimal?)entry.Property(x => x.ReferencePrice).OriginalValue;
        if (input.ReferencePrice is { } p && p != oldPrice)
            Db.Set<IngredientPriceHistory>().Add(new IngredientPriceHistory { IngredientId = e.Id, EffectiveDate = DateOnly.FromDateTime(clock.GetUtcNow().AddHours(7).DateTime), UnitPrice = p, Source = PriceSource.Manual, CreatedAt = clock.GetUtcNow(), CreatedBy = CurrentUser.UserId });
        await Task.CompletedTask;
    }

    protected override async Task BeforeDeleteAsync(Ingredient e, CancellationToken ct)
    {
        if (await Db.Set<RecipeItem>().AnyAsync(r => r.IngredientId == e.Id, ct) || await Db.Set<StockBalance>().AnyAsync(b => b.IngredientId == e.Id && b.Quantity != 0, ct))
            throw new ConflictException("error.inUse");
    }

    public sealed record PriceInput(DateOnly EffectiveDate, decimal UnitPrice, Guid? PartnerId);

    [HttpGet("{id:guid}/prices"), HasPermission(Permissions.Ingredient.View)]
    public async Task<IActionResult> Prices(Guid id, CancellationToken ct)
        => Ok(await (from h in Db.Set<IngredientPriceHistory>().AsNoTracking()
                     where h.IngredientId == id
                     join p in Db.Set<Partner>().AsNoTracking().IgnoreQueryFilters() on h.PartnerId equals p.Id into pj
                     from p in pj.DefaultIfEmpty()
                     orderby h.EffectiveDate descending, h.CreatedAt descending
                     select new { h.Id, h.EffectiveDate, h.UnitPrice, source = h.Source.ToString(), partner = p.NameVi, h.CreatedAt }).Take(500).ToListAsync(ct));

    [HttpPost("{id:guid}/prices"), HasPermission(Permissions.Ingredient.Price)]
    public async Task<IActionResult> AddPrice(Guid id, [FromBody] PriceInput req, CancellationToken ct)
    {
        var ing = await Db.Set<Ingredient>().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("ingredient");
        if (req.UnitPrice < 0) throw new AppException("error.validation");
        Db.Set<IngredientPriceHistory>().Add(new IngredientPriceHistory { IngredientId = id, EffectiveDate = req.EffectiveDate, UnitPrice = req.UnitPrice, Source = PriceSource.Manual, PartnerId = req.PartnerId, CreatedAt = clock.GetUtcNow(), CreatedBy = CurrentUser.UserId });
        var latest = await Db.Set<IngredientPriceHistory>().Where(h => h.IngredientId == id).MaxAsync(h => (DateOnly?)h.EffectiveDate, ct);
        if (latest is null || req.EffectiveDate >= latest) ing.ReferencePrice = req.UnitPrice;
        await Db.SaveChangesAsync(ct);
        await Cache.InvalidateRegionAsync(CacheRegion, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/stock"), HasPermission(Permissions.Ingredient.View)]
    public async Task<IActionResult> Stock(Guid id, CancellationToken ct)
    {
        var scope = CurrentUser.WarehouseScope;
        var q = Db.Set<StockBalance>().AsNoTracking().Where(b => b.IngredientId == id);
        if (scope is not null) q = q.Where(b => scope.Contains(b.WarehouseId));
        var balances = await (from b in q join w in Db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.Id
                              select new { b.WarehouseId, warehouse = w.NameVi, b.Quantity, b.AverageCost, b.Value }).ToListAsync(ct);
        var lots = await Db.Set<StockLot>().AsNoTracking().Where(l => l.IngredientId == id && l.Quantity > 0 && (scope == null || scope.Contains(l.WarehouseId)))
            .OrderBy(l => l.ExpiryDate).Select(l => new { l.Id, l.WarehouseId, l.LotNo, l.MfgDate, l.ExpiryDate, l.Quantity, l.UnitCost }).ToListAsync(ct);
        return Ok(new { balances, lots });
    }
}

// ================================================================== Meal shifts & meal structure templates — SPEC §5.4

public sealed class MealShiftDto : CatalogDto { public TimeOnly? ServeFrom { get; set; } public TimeOnly? ServeTo { get; set; } }
public sealed class MealShiftUpsert : CatalogUpsert
{
    [ExcelColumn("Phục vụ từ (HH:mm)", "Serve from", Order = 4)] public TimeOnly? ServeFrom { get; set; }
    [ExcelColumn("Đến (HH:mm)", "Serve to", Order = 5)] public TimeOnly? ServeTo { get; set; }
}
public sealed class MealShiftUpsertValidator : CatalogUpsertValidator<MealShiftUpsert>;

[Route("api/v1/meal-shifts"), PermissionModule("meal_shift")]
public sealed class MealShiftsController : CatalogControllerBase<MealShift, MealShiftDto, MealShiftUpsert>
{
    protected override string ExportTitle => L.Pick("Ca an", "Meal shifts");
    protected override async Task BeforeDeleteAsync(MealShift e, CancellationToken ct)
    { if (await Db.Set<Menu>().AnyAsync(m => m.MealShiftId == e.Id, ct)) throw new ConflictException("error.inUse"); }
}

public sealed record StructureSlotDto(DishType DishType, int Quantity, bool IsRequired, int SortOrder);
public sealed class MealStructureDto : CatalogDto { public string? Description { get; set; } public List<StructureSlotDto> Slots { get; set; } = []; }
public sealed class MealStructureUpsert : CatalogUpsert
{
    [ExcelColumn("Mô tả", "Description", Order = 4, Width = 40)] public string? Description { get; set; }
    [ExcelIgnore] public List<StructureSlotDto>? Slots { get; set; }
}
public sealed class MealStructureUpsertValidator : CatalogUpsertValidator<MealStructureUpsert>
{
    public MealStructureUpsertValidator() { RuleForEach(x => x.Slots).Must(s => s.Quantity is >= 1 and <= 5 && Enum.IsDefined(s.DishType)).When(x => x.Slots is not null).WithMessage("error.validation"); }
}

[Route("api/v1/meal-structures"), PermissionModule("meal_structure")]
public sealed class MealStructuresController : CatalogControllerBase<MealStructureTemplate, MealStructureDto, MealStructureUpsert>
{
    protected override string ExportTitle => L.Pick("Co cau bua an", "Meal structures");
    protected override IQueryable<MealStructureDto> Project(IQueryable<MealStructureTemplate> q) => q.Select(t => new MealStructureDto
    {
        Id = t.Id, Code = t.Code, NameVi = t.NameVi, NameEn = t.NameEn, IsActive = t.IsActive, SortOrder = t.SortOrder, Version = t.Version, CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt,
        Description = t.Description, Slots = t.Slots.OrderBy(s => s.SortOrder).Select(s => new StructureSlotDto(s.DishType, s.Quantity, s.IsRequired, s.SortOrder)).ToList(),
    });
    protected override IQueryable<MealStructureTemplate> LoadForImport(IQueryable<MealStructureTemplate> q) => q.Include(t => t.Slots);
    protected override void Apply(MealStructureUpsert input, MealStructureTemplate e)
    {
        e.Code = input.Code; e.NameVi = input.NameVi; e.NameEn = input.NameEn; e.IsActive = input.IsActive; e.SortOrder = input.SortOrder; e.Description = input.Description;
    }
    protected override async Task BeforeSaveAsync(MealStructureTemplate e, MealStructureUpsert input, bool isNew, CancellationToken ct)
    {
        if (input.Slots is null) return;
        if (!isNew) await Db.Entry(e).Collection(x => x.Slots).LoadAsync(ct);
        Db.Set<MealStructureSlot>().RemoveRange(e.Slots);
        e.Slots.Clear();
        var i = 0;
        foreach (var s in input.Slots) e.Slots.Add(new MealStructureSlot { TemplateId = e.Id, DishType = s.DishType, Quantity = s.Quantity, IsRequired = s.IsRequired, SortOrder = ++i });
    }
}
