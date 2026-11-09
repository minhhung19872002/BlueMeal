using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Engines;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Infrastructure.Engines;

/// <summary>Input line for ad-hoc computations (recipe editor, manual menu builder).</summary>
public sealed record AdHocLine(Guid IngredientId, decimal GrossG, decimal? NetGOverride = null);

/// <summary>
/// Loads engine inputs from the database: foods (per 100 g), ingredients with unit prices resolved by the configured
/// price mode (SPEC §6.3 a–d), dishes with current or locked recipes, retention factors and meal targets.
/// Food/nutrient data are cached per request; catalog-level caching uses the "catalog:food" region.
/// </summary>
public sealed class EngineDataService(AppDbContext db, ISettingsService settings, TimeProvider clock)
{
    private Dictionary<Guid, IngredientData>? _ingredients;
    private CostPriceMode? _mode;
    private RetentionTable? _retention;

    public async Task<CostPriceMode> PriceModeAsync(CancellationToken ct)
        => _mode ??= (await settings.GetAsync<CostSettings>(SettingKeys.Cost, ct)).PriceMode;

    public async Task<IReadOnlyList<OverheadRule>> OverheadsAsync(CancellationToken ct)
        => (await settings.GetAsync<CostSettings>(SettingKeys.Cost, ct)).Overheads.Select(o => new OverheadRule(o.Code, o.NameVi, o.NameEn, o.Mode, o.Value)).ToList();

    public async Task<RetentionTable> RetentionAsync(CancellationToken ct)
    {
        if (_retention is not null) return _retention;
        var rows = await db.Set<NutrientRetentionFactor>().AsNoTracking().Select(r => new { r.NutrientId, r.CookingMethod, r.Factor }).ToListAsync(ct);
        var codes = await db.Set<Nutrient>().AsNoTracking().ToDictionaryAsync(n => n.Id, n => n.Code, ct);
        return _retention = new RetentionTable(rows.Where(r => codes.ContainsKey(r.NutrientId)).ToDictionary(r => (codes[r.NutrientId], r.CookingMethod), r => r.Factor));
    }

    public async Task<NutritionEngine> NutritionEngineAsync(CancellationToken ct) => new(await RetentionAsync(ct));

    /// <summary>Loads ingredient data (with food and resolved unit price) for the given ids.</summary>
    public async Task<IReadOnlyDictionary<Guid, IngredientData>> IngredientsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        _ingredients ??= new();
        var missing = ids.Distinct().Where(i => !_ingredients.ContainsKey(i)).ToList();
        if (missing.Count > 0)
        {
            var ings = await db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters().Where(i => missing.Contains(i.Id))
                .Select(i => new { i.Id, i.Code, i.NameVi, i.FoodId, i.GramsPerUnit, i.ReferencePrice, i.PreferredPartnerId, i.Allergens }).ToListAsync(ct);
            var foodIds = ings.Where(i => i.FoodId != null).Select(i => i.FoodId!.Value).Distinct().ToList();
            var foods = await db.Set<Food>().AsNoTracking().IgnoreQueryFilters().Where(f => foodIds.Contains(f.Id))
                .Select(f => new { f.Id, f.Code, f.RefuseRate, Animal = f.IsAnimalSourceOverride ?? f.FoodGroup!.IsAnimalSource, GroupCode = f.FoodGroup!.Code, GroupName = f.FoodGroup.NameVi }).ToListAsync(ct);
            var values = await db.Set<FoodNutrient>().AsNoTracking().Where(v => foodIds.Contains(v.FoodId))
                .Select(v => new { v.FoodId, v.Nutrient!.Code, v.ValuePer100gEdible }).ToListAsync(ct);
            var foodData = foods.ToDictionary(f => f.Id, f => new FoodData(f.Id, f.Code, f.RefuseRate, f.Animal,
                values.Where(v => v.FoodId == f.Id).ToDictionary(v => v.Code, v => v.ValuePer100gEdible), f.GroupCode, f.GroupName));
            var prices = await ResolvePricesAsync(ings.Select(i => (i.Id, i.ReferencePrice, i.PreferredPartnerId)).ToList(), ct);
            foreach (var i in ings)
                _ingredients[i.Id] = new IngredientData(i.Id, i.Code, i.NameVi, i.FoodId is { } fid && foodData.TryGetValue(fid, out var fd) ? fd : null,
                    i.GramsPerUnit, prices.GetValueOrDefault(i.Id), i.Allergens);
        }
        return _ingredients;
    }

    /// <summary>Unit price per purchase unit according to the configured mode, falling back to the reference price.</summary>
    private async Task<Dictionary<Guid, decimal?>> ResolvePricesAsync(IReadOnlyList<(Guid Id, decimal? Reference, Guid? Partner)> items, CancellationToken ct)
    {
        var mode = await PriceModeAsync(ct);
        var ids = items.Select(i => i.Id).ToList();
        var result = items.ToDictionary(i => i.Id, i => i.Reference);
        switch (mode)
        {
            case CostPriceMode.LastReceipt:
            {
                var last = await (from l in db.Set<StockReceiptLine>().AsNoTracking()
                                  join r in db.Set<StockReceipt>().AsNoTracking() on l.ReceiptId equals r.Id
                                  where ids.Contains(l.IngredientId) && r.Status == StockDocStatus.Posted
                                  group new { l.UnitPrice, r.DocDate, r.PostedAt } by l.IngredientId into g
                                  select new { Id = g.Key, Price = g.OrderByDescending(x => x.DocDate).ThenByDescending(x => x.PostedAt).Select(x => x.UnitPrice).First() }).ToListAsync(ct);
                foreach (var x in last) result[x.Id] = x.Price;
                break;
            }
            case CostPriceMode.WeightedAverage:
            {
                var avg = await db.Set<StockBalance>().AsNoTracking().Where(b => ids.Contains(b.IngredientId) && b.Quantity > 0)
                    .GroupBy(b => b.IngredientId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.Quantity), Value = g.Sum(x => x.Value) }).ToListAsync(ct);
                foreach (var x in avg.Where(a => a.Qty > 0)) result[x.Id] = x.Value / x.Qty;
                break;
            }
            case CostPriceMode.Contract:
            {
                var today = DateOnly.FromDateTime(clock.GetUtcNow().AddHours(7).DateTime);
                var contracts = await (from p in db.Set<PartnerContractPrice>().AsNoTracking()
                                       join c in db.Set<PartnerContract>().AsNoTracking() on p.ContractId equals c.Id
                                       where ids.Contains(p.IngredientId) && c.Status == ContractStatus.Active && c.ValidFrom <= today && (c.ValidTo == null || c.ValidTo >= today)
                                       select new { p.IngredientId, c.PartnerId, c.ValidFrom, p.UnitPrice }).ToListAsync(ct);
                foreach (var i in items)
                {
                    var match = contracts.Where(c => c.IngredientId == i.Id).OrderByDescending(c => c.PartnerId == i.Partner).ThenByDescending(c => c.ValidFrom).FirstOrDefault();
                    if (match is not null) result[i.Id] = match.UnitPrice;
                }
                break;
            }
        }
        return result;
    }

    /// <summary>Loads dishes with their recipes. When <paramref name="recipeOverrides"/> maps a dish to a recipe id (locked at approval), that version is used.</summary>
    public async Task<IReadOnlyDictionary<Guid, (DishData Dish, decimal CookingLoss)>> DishesAsync(IEnumerable<Guid> dishIds, IReadOnlyDictionary<Guid, Guid?>? recipeOverrides, CancellationToken ct)
    {
        var ids = dishIds.Distinct().ToList();
        var dishes = await db.Set<Dish>().AsNoTracking().IgnoreQueryFilters().Where(d => ids.Contains(d.Id)).ToListAsync(ct);
        var lockedIds = recipeOverrides?.Values.Where(v => v != null).Select(v => v!.Value).ToList() ?? [];
        var recipes = await db.Set<Recipe>().AsNoTracking().IgnoreQueryFilters().Include(r => r.Items)
            .Where(r => (ids.Contains(r.DishId) && r.IsCurrent && !r.IsDeleted) || lockedIds.Contains(r.Id)).ToListAsync(ct);
        var ingIds = recipes.SelectMany(r => r.Items.Select(i => i.IngredientId));
        var ings = await IngredientsAsync(ingIds, ct);
        var result = new Dictionary<Guid, (DishData, decimal)>();
        foreach (var d in dishes)
        {
            Recipe? recipe = null;
            if (recipeOverrides is not null && recipeOverrides.TryGetValue(d.Id, out var rid) && rid is not null) recipe = recipes.FirstOrDefault(r => r.Id == rid);
            recipe ??= recipes.FirstOrDefault(r => r.DishId == d.Id && r.IsCurrent);
            var lines = recipe?.Items.OrderBy(i => i.SortOrder).Where(i => ings.ContainsKey(i.IngredientId))
                .Select(i => new RecipeLine(ings[i.IngredientId], i.GrossG, i.NetGOverride)).ToList() ?? [];
            result[d.Id] = (new DishData(d.Id, d.Code, d.NameVi, d.DishType, d.CookingMethod, recipe?.BaseServings ?? 1, lines, d.Allergens, recipe?.Id, d.NameEn), recipe?.CookingLossRate ?? 0);
        }
        return result;
    }

    /// <summary>Builds dish data from editor lines (not saved yet).</summary>
    public async Task<DishData> AdHocDishAsync(Guid? dishId, string code, string name, DishType type, CookingMethod method, decimal baseServings, IReadOnlyList<AdHocLine> lines, CancellationToken ct)
    {
        var ings = await IngredientsAsync(lines.Select(l => l.IngredientId), ct);
        return new DishData(dishId ?? Guid.Empty, code, name, type, method, baseServings <= 0 ? 1 : baseServings,
            lines.Where(l => ings.ContainsKey(l.IngredientId)).Select(l => new RecipeLine(ings[l.IngredientId], l.GrossG, l.NetGOverride)).ToList());
    }

    /// <summary>Per-meal target from a recommendation (SPEC §6.1). Budget falls back to the recommendation budget.</summary>
    public async Task<MealTarget?> TargetAsync(Guid? recommendationId, decimal? budgetOverride, CancellationToken ct)
    {
        if (recommendationId is null) return null;
        var r = await db.Set<Recommendation>().AsNoTracking().IgnoreQueryFilters().Include(x => x.Nutrients).ThenInclude(x => x.Nutrient)
            .FirstOrDefaultAsync(x => x.Id == recommendationId, ct);
        if (r is null) return null;
        return MealTarget.FromDaily(r.EnergyPerDayKcal, r.MealSharePercent, r.ProteinPctMin, r.ProteinPctMax, r.LipidPctMin, r.LipidPctMax, r.GlucidPctMin, r.GlucidPctMax,
            r.AnimalProteinPctMin, r.AnimalProteinPctMax,
            r.Nutrients.Where(n => n.Nutrient != null).Select(n => new NutrientTarget(n.Nutrient!.Code, n.MinPerMeal, n.MaxPerMeal, n.Level)).ToList(),
            budgetOverride ?? r.BudgetPerServing);
    }

    public async Task<IReadOnlyDictionary<string, string>> NutrientNamesAsync(bool english, CancellationToken ct)
        => await db.Set<Nutrient>().AsNoTracking().ToDictionaryAsync(n => n.Code, n => english && n.NameEn != null ? n.NameEn : n.NameVi, ct);

    public async Task<IReadOnlyList<StructureSlot>> StructureAsync(Guid? templateId, CancellationToken ct)
        => templateId is null ? [] : await db.Set<MealStructureSlot>().AsNoTracking().Where(s => s.TemplateId == templateId)
            .Select(s => new StructureSlot(s.DishType, s.Quantity, s.IsRequired)).ToListAsync(ct);
}
