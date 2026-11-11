using BlueMeal.Api.Authorization;
using BlueMeal.Api.Catalog;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Application.Engines;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Engines;
using BlueMeal.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Catalogs;

// ================================================================== Dishes — SPEC §7.3.6

public sealed class DishDto : CatalogDto
{
    public DishType DishType { get; set; }
    public CookingMethod CookingMethod { get; set; }
    public decimal? ServingWeightG { get; set; }
    public Guid? ImageFileId { get; set; }
    public string? Tags { get; set; }
    public string? Allergens { get; set; }
    public string? Description { get; set; }
    public Guid? CurrentRecipeId { get; set; }
    public int? CurrentRecipeVersion { get; set; }
}

public sealed class DishUpsert : CatalogUpsert
{
    [ExcelColumn("Loại món", "Dish type", Required = true, Order = 4)] public DishType DishType { get; set; }
    [ExcelColumn("Phương pháp chế biến", "Cooking method", Order = 5)] public CookingMethod CookingMethod { get; set; }
    [ExcelColumn("Khối lượng 1 suất (g)", "Serving weight (g)", Order = 6)] public decimal? ServingWeightG { get; set; }
    [ExcelIgnore] public Guid? ImageFileId { get; set; }
    [ExcelColumn("Nhãn", "Tags", Order = 7)] public string? Tags { get; set; }
    [ExcelColumn("Chất gây dị ứng", "Allergens", Order = 8)] public string? Allergens { get; set; }
    [ExcelColumn("Mô tả", "Description", Order = 9, Width = 40)] public string? Description { get; set; }
}

public sealed class DishUpsertValidator : CatalogUpsertValidator<DishUpsert>
{
    public DishUpsertValidator()
    {
        RuleFor(x => x.DishType).IsInEnum();
        RuleFor(x => x.CookingMethod).IsInEnum();
        RuleFor(x => x.ServingWeightG).InclusiveBetween(0, 5000).When(x => x.ServingWeightG is not null);
    }
}

[Route("api/v1/dishes"), PermissionModule("dish")]
public sealed class DishesController(EngineDataService engine) : CatalogControllerBase<Dish, DishDto, DishUpsert>
{
    protected override string ExportTitle => L.Pick("Danh muc mon an", "Dishes");
    protected override string CacheRegion => "catalog:Dish";

    protected override IQueryable<Dish> ApplyFilters(IQueryable<Dish> q, IQueryCollection query)
    {
        if (Enum.TryParse<DishType>(query["dishType"], true, out var t)) q = q.Where(d => d.DishType == t);
        if (Enum.TryParse<CookingMethod>(query["cookingMethod"], true, out var m)) q = q.Where(d => d.CookingMethod == m);
        if (query["hasRecipe"] == "false") q = q.Where(d => !d.Recipes.Any(r => r.IsCurrent && !r.IsDeleted));
        return q;
    }

    protected override IQueryable<DishDto> Project(IQueryable<Dish> q) => q.Select(d => new DishDto
    {
        Id = d.Id, Code = d.Code, NameVi = d.NameVi, NameEn = d.NameEn, IsActive = d.IsActive, SortOrder = d.SortOrder, Version = d.Version, CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt,
        DishType = d.DishType, CookingMethod = d.CookingMethod, ServingWeightG = d.ServingWeightG, ImageFileId = d.ImageFileId, Tags = d.Tags, Allergens = d.Allergens, Description = d.Description,
        CurrentRecipeId = d.Recipes.Where(r => r.IsCurrent && !r.IsDeleted).Select(r => (Guid?)r.Id).FirstOrDefault(),
        CurrentRecipeVersion = d.Recipes.Where(r => r.IsCurrent && !r.IsDeleted).Select(r => (int?)r.VersionNo).FirstOrDefault(),
    });

    protected override async Task BeforeDeleteAsync(Dish e, CancellationToken ct)
    {
        if (await Db.Set<MenuItem>().AnyAsync(m => m.DishId == e.Id, ct)) throw new ConflictException("error.inUse");
    }

    /// <summary>Quick view: nutrition and cost of one serving of the current recipe (SPEC §7.3.6).</summary>
    [HttpGet("{id:guid}/quick"), HasPermission(Permissions.Dish.View)]
    public async Task<IActionResult> Quick(Guid id, CancellationToken ct)
    {
        var dishes = await engine.DishesAsync([id], null, ct);
        if (!dishes.TryGetValue(id, out var d)) throw new NotFoundException("dish");
        var nutrition = (await engine.NutritionEngineAsync(ct)).ComputeDish(d.Dish, 1, d.CookingLoss);
        var cost = CostEngine.ComputeMenu([new MenuDish(d.Dish)], []);
        return Ok(new { recipeId = d.Dish.RecipeId, nutrition = NutritionEngine.Summarize([nutrition]).ToView(), cost = CurrentUser.Has(Permissions.Menu.ViewCost) || CurrentUser.Has(Permissions.Cost.View) ? cost.ToView() : null });
    }
}

// ================================================================== Recipes (versioned) — SPEC §7.3.7

public sealed record RecipeItemInput(Guid IngredientId, decimal GrossG, decimal? NetGOverride, string? PrepNote);
public sealed record RecipeInput(Guid DishId, decimal BaseServings, string? Instructions, int? PrepTimeMinutes, decimal CookingLossRate, string? Note, IReadOnlyList<RecipeItemInput> Items, uint? Version);
public sealed record RecipeComputeInput(Guid? DishId, CookingMethod? CookingMethod, DishType? DishType, decimal BaseServings, decimal CookingLossRate, IReadOnlyList<RecipeItemInput> Items, decimal? Servings);

public sealed class RecipeInputValidator : AbstractValidator<RecipeInput>
{
    public RecipeInputValidator()
    {
        RuleFor(x => x.DishId).NotEmpty();
        RuleFor(x => x.BaseServings).GreaterThan(0).LessThanOrEqualTo(10000);
        RuleFor(x => x.CookingLossRate).InclusiveBetween(0, 90);
        RuleFor(x => x.PrepTimeMinutes).InclusiveBetween(0, 1440).When(x => x.PrepTimeMinutes is not null);
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.IngredientId).NotEmpty();
            i.RuleFor(x => x.GrossG).GreaterThan(0).LessThan(1_000_000);
            i.RuleFor(x => x.NetGOverride).GreaterThanOrEqualTo(0).When(x => x.NetGOverride is not null);
        });
        RuleFor(x => x.Instructions).MaximumLength(8000);
    }
}

[Route("api/v1/recipes")]
public sealed class RecipesController(AppDbContext db, EngineDataService engine, IAuditWriter audit, ICacheService cache, ISettingsService settings) : ApiControllerBase
{
    private bool CanSeeCost => CurrentUser.Has(Permissions.Menu.ViewCost) || CurrentUser.Has(Permissions.Cost.View) || CurrentUser.Has(Permissions.Recipe.Edit);

    [HttpGet, HasPermission(Permissions.Recipe.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] Guid? dishId, [FromQuery] RecipeStatus? status, [FromQuery] bool currentOnly = false, CancellationToken ct = default)
    {
        var query = db.Set<Recipe>().AsNoTracking().Where(r => !r.Dish!.IsDeleted);
        if (dishId is not null) query = query.Where(r => r.DishId == dishId);
        if (status is not null) query = query.Where(r => r.Status == status);
        if (currentOnly) query = query.Where(r => r.IsCurrent);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var norm = "%" + BlueMeal.Domain.Common.TextNormalizer.Normalize(q.Q) + "%";
            query = query.Where(r => EF.Functions.ILike(r.Dish!.SearchText!, norm));
        }
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderBy(r => r.Dish!.Code).ThenByDescending(r => r.VersionNo).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(r => new
            {
                r.Id, r.DishId, dishCode = r.Dish!.Code, dishNameVi = r.Dish.NameVi, dishNameEn = r.Dish.NameEn, dishType = r.Dish.DishType.ToString(),
                cookingMethod = r.Dish.CookingMethod.ToString(), r.VersionNo, r.IsCurrent, status = r.Status.ToString(), r.BaseServings, r.CookingLossRate,
                itemCount = r.Items.Count, r.UpdatedAt, r.CreatedAt, r.Version,
            }).ToListAsync(ct);

        // kcal and food cost of one serving per listed version (same engines as the dish quick view, SPEC §7.3.6).
        // DishesAsync takes one recipe per dish, so versions of the same dish are computed in successive rounds.
        var nutritionEngine = await engine.NutritionEngineAsync(ct);
        var perServing = new Dictionary<Guid, (decimal Kcal, decimal Cost)>();
        var pending = items.Select(i => (i.Id, i.DishId)).ToList();
        while (pending.Count > 0)
        {
            var round = pending.GroupBy(p => p.DishId).Select(g => g.First()).ToList();
            var dishes = await engine.DishesAsync(round.Select(r => r.DishId), round.ToDictionary(r => r.DishId, r => (Guid?)r.Id), ct);
            foreach (var (recipeId, rDish) in round)
                if (dishes.TryGetValue(rDish, out var d))
                    perServing[recipeId] = (NutritionEngine.Summarize([nutritionEngine.ComputeDish(d.Dish, 1, d.CookingLoss)]).ToView().EnergyKcal,
                        CostEngine.ComputeMenu([new MenuDish(d.Dish)], []).ToView().FoodCostPerServing);
            pending = pending.Except(round).ToList();
        }
        var canCost = CanSeeCost;
        var rows = items.Select(i => new
        {
            i.Id, i.DishId, i.dishCode, i.dishNameVi, i.dishNameEn, i.dishType, i.cookingMethod, i.VersionNo, i.IsCurrent, i.status, i.BaseServings, i.CookingLossRate,
            i.itemCount, i.UpdatedAt, i.CreatedAt, i.Version,
            energyKcal = perServing.TryGetValue(i.Id, out var v) ? Math.Round(v.Kcal, 0) : (decimal?)null,
            costPerServing = canCost && perServing.TryGetValue(i.Id, out var c) ? Math.Round(c.Cost, 0) : (decimal?)null,
        }).ToList();
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.Recipe.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var r = await db.Set<Recipe>().AsNoTracking().Include(x => x.Dish).Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("recipe");
        var ings = await engine.IngredientsAsync(r.Items.Select(i => i.IngredientId), ct);
        var result = await ComputeAsync(new RecipeComputeInput(r.DishId, r.Dish!.CookingMethod, r.Dish.DishType, r.BaseServings, r.CookingLossRate,
            r.Items.OrderBy(i => i.SortOrder).Select(i => new RecipeItemInput(i.IngredientId, i.GrossG, i.NetGOverride, i.PrepNote)).ToList(), null), ct);
        return Ok(new
        {
            r.Id, r.DishId, dish = new { r.Dish.Code, r.Dish.NameVi, r.Dish.NameEn, dishType = r.Dish.DishType.ToString(), cookingMethod = r.Dish.CookingMethod.ToString() },
            r.VersionNo, r.IsCurrent, status = r.Status.ToString(), r.BaseServings, r.Instructions, r.PrepTimeMinutes, r.CookingLossRate, r.Note, r.Version, r.AuthorUserId, r.UpdatedAt,
            items = r.Items.OrderBy(i => i.SortOrder).Select(i => new
            {
                i.Id, i.IngredientId, ingredientCode = ings.GetValueOrDefault(i.IngredientId)?.Code, ingredientName = ings.GetValueOrDefault(i.IngredientId)?.Name,
                i.GrossG, i.NetGOverride, netG = ings.TryGetValue(i.IngredientId, out var ing) ? NutritionEngine.NetGrams(new RecipeLine(ing, i.GrossG, i.NetGOverride)) : i.GrossG,
                refuseRate = ing?.Food?.RefuseRate, hasFood = ing?.Food is not null, i.PrepNote,
            }),
            computed = result,
        });
    }

    /// <summary>Realtime computation for the recipe editor (nutrition, P:L:G, cost, missing data) — SPEC §7.3.7.</summary>
    [HttpPost("compute"), HasPermission(Permissions.Recipe.View)]
    public async Task<IActionResult> Compute([FromBody] RecipeComputeInput input, CancellationToken ct) => Ok(await ComputeAsync(input, ct));

    private async Task<object> ComputeAsync(RecipeComputeInput input, CancellationToken ct)
    {
        var dish = await engine.AdHocDishAsync(input.DishId, "", "", input.DishType ?? DishType.Other, input.CookingMethod ?? CookingMethod.None, input.BaseServings,
            input.Items.Select(i => new AdHocLine(i.IngredientId, i.GrossG, i.NetGOverride)).ToList(), ct);
        var ne = await engine.NutritionEngineAsync(ct);
        var dn = ne.ComputeDish(dish, 1, input.CookingLossRate);
        var summary = NutritionEngine.Summarize([dn]);
        var cost = CostEngine.ComputeMenu([new MenuDish(dish)], await engine.OverheadsAsync(ct), input.Servings ?? 1);
        var warnings = WarningEngine.Evaluate(summary, cost, [new MenuDish(dish)], new WarningContext(null, [], new HashSet<Guid>(), [], []),
            await settings.GetAsync<WarningSettings>(SettingKeys.Warning, ct));
        var servings = input.Servings is > 0 ? input.Servings.Value : (decimal?)null;
        return new
        {
            nutrition = summary.ToView(),
            cost = CanSeeCost ? cost.ToView() : null,
            warnings,
            scaled = servings is null ? null : dish.Lines.Select(l => new
            {
                ingredientId = l.Ingredient.Id, l.Ingredient.Code, l.Ingredient.Name,
                grossG = l.GrossG / dish.BaseServings * servings.Value,
                netG = NutritionEngine.NetGrams(l) / dish.BaseServings * servings.Value,
                purchaseUnits = l.Ingredient.GramsPerUnit > 0 ? l.GrossG / dish.BaseServings * servings.Value / l.Ingredient.GramsPerUnit : 0,
            }),
        };
    }

    [HttpPost, HasPermission(Permissions.Recipe.Create)]
    public async Task<IActionResult> Create([FromBody] RecipeInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var dish = await db.Set<Dish>().FirstOrDefaultAsync(d => d.Id == input.DishId, ct) ?? throw new NotFoundException("dish");
        var next = (await db.Set<Recipe>().IgnoreQueryFilters().Where(r => r.DishId == dish.Id).MaxAsync(r => (int?)r.VersionNo, ct) ?? 0) + 1;
        var recipe = new Recipe { DishId = dish.Id, VersionNo = next, Status = RecipeStatus.Draft, AuthorUserId = CurrentUser.UserId };
        Apply(recipe, input);
        // the very first recipe of a dish becomes current immediately (nothing to compare against)
        if (!await db.Set<Recipe>().AnyAsync(r => r.DishId == dish.Id && r.IsCurrent, ct)) { recipe.IsCurrent = true; recipe.Status = RecipeStatus.Active; }
        db.Add(recipe);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateRegionAsync("catalog:Dish", ct);
        return Ok(new { recipe.Id, recipe.VersionNo });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.Recipe.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] RecipeInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var recipe = await db.Set<Recipe>().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("recipe");
        if (recipe.Status != RecipeStatus.Draft) throw new BusinessRuleException("recipe.notDraft");
        if (input.Version is { } v) db.Entry(recipe).Property(x => x.Version).OriginalValue = v;
        db.Set<RecipeItem>().RemoveRange(recipe.Items);
        recipe.Items.Clear();
        Apply(recipe, input);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(Recipe r, RecipeInput input)
    {
        r.BaseServings = input.BaseServings; r.Instructions = input.Instructions; r.PrepTimeMinutes = input.PrepTimeMinutes;
        r.CookingLossRate = input.CookingLossRate; r.Note = input.Note;
        var i = 0;
        foreach (var it in input.Items)
            r.Items.Add(new RecipeItem { RecipeId = r.Id, IngredientId = it.IngredientId, GrossG = it.GrossG, NetGOverride = it.NetGOverride, PrepNote = it.PrepNote, SortOrder = ++i });
    }

    /// <summary>Makes this version current; the previous current version is archived.</summary>
    [HttpPost("{id:guid}/activate"), HasPermission(Permissions.Recipe.Approve)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var recipe = await db.Set<Recipe>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("recipe");
        var current = await db.Set<Recipe>().Where(r => r.DishId == recipe.DishId && r.IsCurrent && r.Id != id).ToListAsync(ct);
        foreach (var c in current) { c.IsCurrent = false; c.Status = RecipeStatus.Archived; }
        recipe.IsCurrent = true;
        recipe.Status = RecipeStatus.Active;
        await db.SaveChangesAsync(ct);
        await cache.InvalidateRegionAsync("catalog:Dish", ct);
        await audit.WriteAsync(AuditAction.Approve, nameof(Recipe), id.ToString(), message: $"v{recipe.VersionNo} activated", ct: ct);
        return NoContent();
    }

    public sealed record CloneRequest(Guid? TargetDishId);

    /// <summary>Copies a recipe as a new Draft version (same dish) or as the first recipe of another dish.</summary>
    [HttpPost("{id:guid}/clone"), HasPermission(Permissions.Recipe.Create)]
    public async Task<IActionResult> Clone(Guid id, [FromBody] CloneRequest req, CancellationToken ct)
    {
        var src = await db.Set<Recipe>().AsNoTracking().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("recipe");
        var dishId = req.TargetDishId ?? src.DishId;
        _ = await db.Set<Dish>().FirstOrDefaultAsync(d => d.Id == dishId, ct) ?? throw new NotFoundException("dish");
        var input = new RecipeInput(dishId, src.BaseServings, src.Instructions, src.PrepTimeMinutes, src.CookingLossRate, src.Note,
            src.Items.OrderBy(i => i.SortOrder).Select(i => new RecipeItemInput(i.IngredientId, i.GrossG, i.NetGOverride, i.PrepNote)).ToList(), null);
        return await Create(input, ct);
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.Recipe.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var recipe = await db.Set<Recipe>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("recipe");
        if (recipe.IsCurrent) throw new BusinessRuleException("recipe.notDraft");
        if (await db.Set<MenuItem>().AnyAsync(m => m.RecipeId == id, ct)) throw new ConflictException("error.inUse");
        db.Remove(recipe);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Side-by-side comparison of two versions: per-ingredient quantities and nutrition/cost totals.</summary>
    [HttpGet("compare"), HasPermission(Permissions.Recipe.View)]
    public async Task<IActionResult> Compare([FromQuery] Guid a, [FromQuery] Guid b, CancellationToken ct)
    {
        var recipes = await db.Set<Recipe>().AsNoTracking().Include(r => r.Items).Include(r => r.Dish).Where(r => r.Id == a || r.Id == b).ToListAsync(ct);
        var ra = recipes.FirstOrDefault(r => r.Id == a) ?? throw new NotFoundException("recipe");
        var rb = recipes.FirstOrDefault(r => r.Id == b) ?? throw new NotFoundException("recipe");
        var ings = await engine.IngredientsAsync(ra.Items.Concat(rb.Items).Select(i => i.IngredientId), ct);
        decimal PerServing(Recipe r, Guid ing) => r.Items.Where(i => i.IngredientId == ing).Sum(i => i.GrossG) / r.BaseServings;
        var rows = ra.Items.Concat(rb.Items).Select(i => i.IngredientId).Distinct().Select(ing => new
        {
            ingredientId = ing, code = ings.GetValueOrDefault(ing)?.Code, name = ings.GetValueOrDefault(ing)?.Name,
            a = PerServing(ra, ing), b = PerServing(rb, ing), diff = PerServing(rb, ing) - PerServing(ra, ing),
        }).ToList();
        async Task<object> Calc(Recipe r) => await ComputeAsync(new RecipeComputeInput(r.DishId, r.Dish!.CookingMethod, r.Dish.DishType, r.BaseServings, r.CookingLossRate,
            r.Items.Select(i => new RecipeItemInput(i.IngredientId, i.GrossG, i.NetGOverride, i.PrepNote)).ToList(), null), ct);
        return Ok(new { a = new { ra.Id, ra.VersionNo, status = ra.Status.ToString(), computed = await Calc(ra) }, b = new { rb.Id, rb.VersionNo, status = rb.Status.ToString(), computed = await Calc(rb) }, rows });
    }

    // ---------------------------------------------------------------- Excel import of recipes (one row = one ingredient line)

    private static readonly LineImportColumn ColDish = new("dishCode", "Mã món", "Dish code", Required: true, Width: 14);
    private static readonly LineImportColumn ColServings = new("baseServings", "Số suất chuẩn", "Base servings", Width: 14,
        HintVi: "Số suất mà khối lượng nguyên liệu tính cho; ghi ở một dòng bất kỳ của món, bỏ trống = 1", HintEn: "Servings the quantities are for; on any row of the dish, empty = 1");
    private static readonly LineImportColumn ColLoss = new("cookingLossRate", "Hao hụt chế biến (%)", "Cooking loss (%)", Width: 16,
        HintVi: "0–90; ghi ở một dòng bất kỳ của món, bỏ trống = 0", HintEn: "0–90; on any row of the dish, empty = 0");
    private static readonly LineImportColumn ColIngredient = new("ingredientCode", "Mã nguyên liệu", "Ingredient code", Required: true, Width: 16,
        HintVi: "Mã trong danh mục nguyên liệu", HintEn: "Code from the ingredient catalog");
    private static readonly LineImportColumn ColGross = new("grossG", "Khối lượng sống (g)", "Gross weight (g)", Required: true, Width: 16,
        HintVi: "Khối lượng mua vào (gram) cho số suất chuẩn", HintEn: "Purchased weight (grams) for the base servings");
    private static readonly LineImportColumn ColNet = new("netG", "Khối lượng sạch (g)", "Net weight (g)", Width: 16,
        HintVi: "Bỏ trống = tính theo tỷ lệ thải bỏ của thực phẩm", HintEn: "Empty = computed from the food's refuse rate");
    private static readonly LineImportColumn ColPrep = new("prepNote", "Ghi chú sơ chế", "Prep note", Width: 28);
    private static readonly LineImportColumn ColInstructions = new("instructions", "Cách chế biến", "Instructions", Width: 40,
        HintVi: "Ghi ở một dòng bất kỳ của món", HintEn: "On any row of the dish");
    private static readonly LineImportColumn[] RecipeImportColumns = [ColDish, ColServings, ColLoss, ColIngredient, ColGross, ColNet, ColPrep, ColInstructions];
    private const string RecipeImportKind = nameof(Recipe);

    private sealed record RecipeImportDoc(Guid DishId, bool HasCurrent, RecipeInput Input);

    [HttpGet("import/template"), HasPermission(Permissions.Recipe.Import)]
    public IActionResult ImportTemplate([FromServices] IExcelService excel)
    {
        var bytes = LineImport.Template(excel, RecipeImportColumns,
        [
            L.Pick("Mỗi dòng là một nguyên liệu của món; các dòng cùng mã món tạo thành một công thức.", "Each row is one ingredient of a dish; rows with the same dish code form one recipe."),
            L.Pick("Món chưa có công thức: công thức nhập vào được áp dụng ngay. Món đã có công thức: tạo phiên bản mới ở trạng thái Nháp để duyệt.",
                "Dish without a recipe: the imported recipe applies at once. Dish with a recipe: a new Draft version is created for approval."),
            L.Pick("Một món có dòng lỗi sẽ không được nhập (cả công thức).", "A dish with any invalid row is not imported (the whole recipe)."),
        ]);
        return File(bytes, LineImport.XlsxMime, "recipes_template.xlsx");
    }

    [HttpPost("import/preview"), HasPermission(Permissions.Recipe.Import), RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<ImportPreview>> ImportPreview(IFormFile file, [FromServices] IExcelService excel, CancellationToken ct)
    {
        var bytes = await LineImport.ReadUploadAsync(file, ct);
        var (results, _) = await ParseRecipeImportAsync(excel, bytes, ct);
        var token = await LineImport.StashAsync(cache, UserId, RecipeImportKind, bytes, null, ct);
        return LineImport.Preview(token, results, results.Count(r => r.Errors.Count == 0 && r.Action == ImportAction.Create), results.Count(r => r.Errors.Count == 0 && r.Action == ImportAction.Update));
    }

    [HttpPost("import/commit"), HasPermission(Permissions.Recipe.Import)]
    public async Task<ActionResult<ImportCommitResult>> ImportCommit([FromBody] LineImportCommitRequest req, [FromServices] IExcelService excel, CancellationToken ct)
    {
        var (bytes, _) = await LineImport.TakeAsync(cache, UserId, RecipeImportKind, req.Token, ct);
        var (results, docs) = await ParseRecipeImportAsync(excel, bytes, ct);
        var errors = results.Where(r => r.Errors.Count > 0).ToList();
        if (errors.Count > 0 && !req.SkipErrors) return new ImportCommitResult(0, 0, errors.Count, errors);

        var dishIds = docs.Select(d => d.DishId).ToList();
        var lastVersion = await db.Set<Recipe>().IgnoreQueryFilters().Where(r => dishIds.Contains(r.DishId))
            .GroupBy(r => r.DishId).Select(g => new { g.Key, Max = g.Max(r => r.VersionNo) }).ToDictionaryAsync(x => x.Key, x => x.Max, ct);
        foreach (var doc in docs)
        {
            var recipe = new Recipe { DishId = doc.DishId, VersionNo = lastVersion.GetValueOrDefault(doc.DishId) + 1, Status = RecipeStatus.Draft, AuthorUserId = CurrentUser.UserId };
            Apply(recipe, doc.Input);
            // same rule as Create: the first recipe of a dish is current at once, otherwise the new version waits for approval
            if (!doc.HasCurrent) { recipe.IsCurrent = true; recipe.Status = RecipeStatus.Active; }
            db.Add(recipe);
        }
        await db.SaveChangesAsync(ct);
        await LineImport.ForgetAsync(cache, req.Token, ct);
        await cache.InvalidateRegionAsync("catalog:Dish", ct);
        int created = docs.Count(d => !d.HasCurrent), versions = docs.Count - created;
        await audit.WriteAsync(AuditAction.Import, nameof(Recipe), null, message: $"created {created}, new versions {versions}, skipped rows {errors.Count}", ct: ct);
        return new ImportCommitResult(created, versions, errors.Count, errors);
    }

    /// <summary>Validates every row and groups valid dishes into recipes; a dish with any invalid row is left out entirely.</summary>
    private async Task<(List<ImportRowResult> Results, List<RecipeImportDoc> Docs)> ParseRecipeImportAsync(IExcelService excel, byte[] bytes, CancellationToken ct)
    {
        var rows = LineImport.Read(excel, bytes, RecipeImportColumns);
        static string? Key(string? code) => code?.ToUpperInvariant();
        var dishCodes = rows.Select(r => Key(r.Text(ColDish.Key))).OfType<string>().Distinct().ToList();
        var ingCodes = rows.Select(r => Key(r.Text(ColIngredient.Key))).OfType<string>().Distinct().ToList();
        var dishes = await db.Set<Dish>().AsNoTracking().Where(d => dishCodes.Contains(d.Code.ToUpper()))
            .Select(d => new { d.Id, d.Code, HasCurrent = db.Set<Recipe>().Any(r => r.DishId == d.Id && r.IsCurrent) })
            .ToDictionaryAsync(d => d.Code.ToUpperInvariant(), ct);
        var ingredients = await db.Set<Ingredient>().AsNoTracking().Where(i => ingCodes.Contains(i.Code.ToUpper()))
            .Select(i => new { i.Id, i.Code }).ToDictionaryAsync(i => i.Code.ToUpperInvariant(), i => i.Id, ct);

        var lines = new List<(LineImportRow Row, string Doc, string? Code, decimal? Servings, decimal? Loss, RecipeItemInput? Item)>();
        foreach (var row in rows)
        {
            var dishCode = row.Text(ColDish.Key);
            var ingCode = row.Text(ColIngredient.Key);
            if (dishCode is not null && !dishes.ContainsKey(Key(dishCode)!)) row.Errors.Add(L.T("import.notFoundRef", ColDish.Header, dishCode));
            Guid? ingredientId = ingCode is not null && ingredients.TryGetValue(Key(ingCode)!, out var iid) ? iid : null;
            if (ingCode is not null && ingredientId is null) row.Errors.Add(L.T("import.notFoundRef", ColIngredient.Header, ingCode));
            var gross = row.Number(ColGross, v => v is > 0 and < 1_000_000);
            var net = row.Number(ColNet, v => v >= 0);
            var servings = row.Number(ColServings, v => v is > 0 and <= 10_000);
            var loss = row.Number(ColLoss, v => v is >= 0 and <= 90);
            if (row.Text(ColInstructions.Key) is { Length: > 8000 } ins) row.Errors.Add(L.T("import.invalid", ColInstructions.Header, ins[..40] + "…"));
            var item = ingredientId is { } id && gross is { } g ? new RecipeItemInput(id, g, net, row.Text(ColPrep.Key)) : null;
            // rows without a dish code cannot be grouped: each one is its own (failed) document
            lines.Add((row, Key(dishCode) ?? $"#{row.RowNumber}", dishCode is null ? ingCode : $"{dishCode} · {ingCode}", servings, loss, item));
        }

        var results = LineImport.Results(lines.Select(l => (l.Row, l.Doc, l.Code)),
            doc => dishes.TryGetValue(doc, out var d) && d.HasCurrent ? ImportAction.Update : ImportAction.Create);
        var docs = lines.GroupBy(l => l.Doc)
            .Where(g => g.All(l => l.Row.Errors.Count == 0 && l.Item is not null))
            .Select(g =>
            {
                var dish = dishes[g.Key];
                var input = new RecipeInput(dish.Id, g.Select(l => l.Servings).FirstOrDefault(v => v is not null) ?? 1,
                    g.Select(l => l.Row.Text(ColInstructions.Key)).FirstOrDefault(v => v is not null), null,
                    g.Select(l => l.Loss).FirstOrDefault(v => v is not null) ?? 0, L.Pick("Nhập từ Excel", "Imported from Excel"),
                    g.Select(l => l.Item!).ToList(), null);
                return new RecipeImportDoc(dish.Id, dish.HasCurrent, input);
            }).ToList();
        return (results, docs);
    }
}

// ================================================================== Recommendations — SPEC §7.3.8

public sealed record RecommendationNutrientDto(Guid NutrientId, string? NutrientCode, decimal? MinPerMeal, decimal? MaxPerMeal, WarningLevel Level);

public sealed class RecommendationDto : CatalogDto
{
    public Guid? SubjectGroupId { get; set; }
    public string? SubjectGroupNameVi { get; set; }
    public string? Reference { get; set; }
    public int? ReferenceYear { get; set; }
    public decimal EnergyPerDayKcal { get; set; }
    public decimal MealSharePercent { get; set; }
    public decimal ProteinPctMin { get; set; }
    public decimal ProteinPctMax { get; set; }
    public decimal LipidPctMin { get; set; }
    public decimal LipidPctMax { get; set; }
    public decimal GlucidPctMin { get; set; }
    public decimal GlucidPctMax { get; set; }
    public decimal AnimalProteinPctMin { get; set; }
    public decimal AnimalProteinPctMax { get; set; }
    public decimal? BudgetPerServing { get; set; }
    public string? Note { get; set; }
    public decimal MealEnergyKcal { get; set; }
    public List<RecommendationNutrientDto> Nutrients { get; set; } = [];
}

public sealed class RecommendationUpsert : CatalogUpsert
{
    [ExcelColumn("Mã đối tượng", "Subject group code", Order = 4), ExcelRef(typeof(SubjectGroup))] public Guid? SubjectGroupId { get; set; }
    [ExcelColumn("Nguồn tham chiếu", "Reference", Order = 5, Width = 36)] public string? Reference { get; set; }
    [ExcelColumn("Năm", "Year", Order = 6)] public int? ReferenceYear { get; set; }
    [ExcelColumn("Nhu cầu năng lượng/ngày (kcal)", "Energy per day (kcal)", Required = true, Order = 7)] public decimal EnergyPerDayKcal { get; set; }
    [ExcelColumn("Tỷ lệ bữa ca (%)", "Meal share (%)", Required = true, Order = 8)] public decimal MealSharePercent { get; set; } = 35;
    [ExcelColumn("Protein % min", "Protein % min", Order = 9)] public decimal ProteinPctMin { get; set; } = 13;
    [ExcelColumn("Protein % max", "Protein % max", Order = 10)] public decimal ProteinPctMax { get; set; } = 20;
    [ExcelColumn("Lipid % min", "Fat % min", Order = 11)] public decimal LipidPctMin { get; set; } = 20;
    [ExcelColumn("Lipid % max", "Fat % max", Order = 12)] public decimal LipidPctMax { get; set; } = 25;
    [ExcelColumn("Glucid % min", "Carbohydrate % min", Order = 13)] public decimal GlucidPctMin { get; set; } = 55;
    [ExcelColumn("Glucid % max", "Carbohydrate % max", Order = 14)] public decimal GlucidPctMax { get; set; } = 65;
    [ExcelColumn("Đạm động vật % min", "Animal protein % min", Order = 15)] public decimal AnimalProteinPctMin { get; set; } = 30;
    [ExcelColumn("Đạm động vật % max", "Animal protein % max", Order = 16)] public decimal AnimalProteinPctMax { get; set; } = 50;
    [ExcelColumn("Ngân sách/suất (₫)", "Budget per serving (VND)", Order = 17, Format = "#,##0")] public decimal? BudgetPerServing { get; set; }
    [ExcelColumn("Ghi chú", "Note", Order = 18, Width = 30)] public string? Note { get; set; }
    [ExcelIgnore] public List<RecommendationNutrientDto>? Nutrients { get; set; }
}

public sealed class RecommendationUpsertValidator : CatalogUpsertValidator<RecommendationUpsert>
{
    public RecommendationUpsertValidator()
    {
        RuleFor(x => x.EnergyPerDayKcal).InclusiveBetween(500, 6000);
        RuleFor(x => x.MealSharePercent).InclusiveBetween(5, 100);
        RuleFor(x => x).Must(x => x.ProteinPctMin <= x.ProteinPctMax && x.LipidPctMin <= x.LipidPctMax && x.GlucidPctMin <= x.GlucidPctMax && x.AnimalProteinPctMin <= x.AnimalProteinPctMax)
            .WithMessage("error.validation");
        RuleFor(x => x.BudgetPerServing).GreaterThanOrEqualTo(0).When(x => x.BudgetPerServing is not null);
        RuleForEach(x => x.Nutrients).Must(n => n.MinPerMeal is null || n.MaxPerMeal is null || n.MinPerMeal <= n.MaxPerMeal).When(x => x.Nutrients is not null).WithMessage("error.validation");
    }
}

[Route("api/v1/recommendations"), PermissionModule("recommendation")]
public sealed class RecommendationsController : CatalogControllerBase<Recommendation, RecommendationDto, RecommendationUpsert>
{
    protected override string ExportTitle => L.Pick("Danh muc khuyen nghi", "Recommendations");
    protected override string CacheRegion => "catalog:Recommendation";

    protected override IQueryable<Recommendation> ApplyFilters(IQueryable<Recommendation> q, IQueryCollection query)
        => Guid.TryParse(query["subjectGroupId"], out var s) ? q.Where(r => r.SubjectGroupId == s) : q;

    protected override IQueryable<RecommendationDto> Project(IQueryable<Recommendation> q) => q.Select(r => new RecommendationDto
    {
        Id = r.Id, Code = r.Code, NameVi = r.NameVi, NameEn = r.NameEn, IsActive = r.IsActive, SortOrder = r.SortOrder, Version = r.Version, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt,
        SubjectGroupId = r.SubjectGroupId, SubjectGroupNameVi = r.SubjectGroup!.NameVi, Reference = r.Reference, ReferenceYear = r.ReferenceYear, EnergyPerDayKcal = r.EnergyPerDayKcal,
        MealSharePercent = r.MealSharePercent, ProteinPctMin = r.ProteinPctMin, ProteinPctMax = r.ProteinPctMax, LipidPctMin = r.LipidPctMin, LipidPctMax = r.LipidPctMax,
        GlucidPctMin = r.GlucidPctMin, GlucidPctMax = r.GlucidPctMax, AnimalProteinPctMin = r.AnimalProteinPctMin, AnimalProteinPctMax = r.AnimalProteinPctMax,
        BudgetPerServing = r.BudgetPerServing, Note = r.Note, MealEnergyKcal = r.EnergyPerDayKcal * r.MealSharePercent / 100,
        Nutrients = r.Nutrients.Select(n => new RecommendationNutrientDto(n.NutrientId, n.Nutrient!.Code, n.MinPerMeal, n.MaxPerMeal, n.Level)).ToList(),
    });

    protected override IQueryable<Recommendation> LoadForImport(IQueryable<Recommendation> q) => q.Include(r => r.Nutrients);

    protected override void Apply(RecommendationUpsert input, Recommendation e)
    {
        var nutrients = input.Nutrients;
        input.Nutrients = null;
        Mapster.TypeAdapter.Adapt(input, e);
        input.Nutrients = nutrients;
    }

    protected override async Task BeforeSaveAsync(Recommendation e, RecommendationUpsert input, bool isNew, CancellationToken ct)
    {
        if (!isNew && e.Source == DataSource.VddApi) e.LocallyModified = true;
        if (input.Nutrients is null) return;
        if (!isNew) await Db.Entry(e).Collection(x => x.Nutrients).LoadAsync(ct);
        Db.Set<RecommendationNutrient>().RemoveRange(e.Nutrients);
        e.Nutrients.Clear();
        foreach (var n in input.Nutrients.DistinctBy(n => n.NutrientId))
            e.Nutrients.Add(new RecommendationNutrient { RecommendationId = e.Id, NutrientId = n.NutrientId, MinPerMeal = n.MinPerMeal, MaxPerMeal = n.MaxPerMeal, Level = n.Level });
    }
}

// ================================================================== Report groups — SPEC §7.3.9

public sealed class ReportGroupDto : CatalogDto
{
    public string? Description { get; set; }
    public string? RequiredPermission { get; set; }
    public List<string> ReportCodes { get; set; } = [];
}

public sealed class ReportGroupUpsert : CatalogUpsert
{
    [ExcelColumn("Mô tả", "Description", Order = 4, Width = 40)] public string? Description { get; set; }
    [ExcelColumn("Quyền xem bổ sung", "Required permission", Order = 5)] public string? RequiredPermission { get; set; }
}

public sealed class ReportGroupUpsertValidator : CatalogUpsertValidator<ReportGroupUpsert>
{
    public ReportGroupUpsertValidator()
        => RuleFor(x => x.RequiredPermission).Must(p => p is null || p.Length == 0 || Permissions.All.Any(a => a.Code == p)).WithMessage("error.validation");
}

[Route("api/v1/report-groups"), PermissionModule("report_group")]
public sealed class ReportGroupsController : CatalogControllerBase<ReportGroup, ReportGroupDto, ReportGroupUpsert>
{
    protected override string ExportTitle => L.Pick("Nhom bao cao", "Report groups");

    protected override IQueryable<ReportGroupDto> Project(IQueryable<ReportGroup> q) => q.Select(g => new ReportGroupDto
    {
        Id = g.Id, Code = g.Code, NameVi = g.NameVi, NameEn = g.NameEn, IsActive = g.IsActive, SortOrder = g.SortOrder, Version = g.Version, CreatedAt = g.CreatedAt, UpdatedAt = g.UpdatedAt,
        Description = g.Description, RequiredPermission = g.RequiredPermission, ReportCodes = g.Reports.OrderBy(r => r.SortOrder).Select(r => r.Code).ToList(),
    });

    protected override async Task BeforeDeleteAsync(ReportGroup e, CancellationToken ct)
        => await Db.Set<ReportDefinition>().Where(r => r.ReportGroupId == e.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.ReportGroupId, (Guid?)null), ct);

    /// <summary>All report definitions with their current group (for assignment).</summary>
    [HttpGet("definitions"), HasPermission(Permissions.ReportGroup.View)]
    public async Task<IActionResult> Definitions(CancellationToken ct)
        => Ok(await Db.Set<ReportDefinition>().AsNoTracking().OrderBy(r => r.SortOrder)
            .Select(r => new { r.Id, r.Code, r.NameVi, r.NameEn, r.ReportGroupId, group = r.ReportGroup!.NameVi, r.IsActive }).ToListAsync(ct));

    public sealed record AssignRequest(string[] ReportCodes);

    [HttpPut("{id:guid}/reports"), HasPermission(Permissions.ReportGroup.Edit)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignRequest req, CancellationToken ct)
    {
        _ = await Db.Set<ReportGroup>().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new NotFoundException("report group");
        var defs = await Db.Set<ReportDefinition>().Where(r => req.ReportCodes.Contains(r.Code)).ToListAsync(ct);
        foreach (var d in defs) d.ReportGroupId = id;
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }
}
