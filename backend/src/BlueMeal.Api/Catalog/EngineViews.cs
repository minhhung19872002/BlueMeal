using BlueMeal.Application.Engines;

namespace BlueMeal.Api.Catalog;

/// <summary>JSON-friendly projections of engine results (values unrounded; the UI rounds per SPEC §6.1).</summary>
public sealed record NutritionView(
    decimal EnergyKcal, decimal EnergyTableKcal, decimal EnergyFromMacrosKcal,
    decimal ProteinG, decimal LipidG, decimal GlucidG,
    decimal ProteinPct, decimal LipidPct, decimal GlucidPct,
    decimal AnimalProteinG, decimal AnimalProteinPct,
    IReadOnlyDictionary<string, decimal> Totals,
    IReadOnlyList<DishNutritionView> Dishes,
    IReadOnlyList<string> MissingFoodIngredients,
    IReadOnlyList<ComplianceItem>? Compliance,
    decimal? Score,
    decimal? TargetEnergyKcal);

public sealed record DishNutritionView(Guid DishId, string Code, string Name, string Type, decimal ServingFactor, decimal NetWeightG, decimal CookedWeightG, decimal EnergyKcal,
    IReadOnlyDictionary<string, decimal> Nutrients, IReadOnlyList<LineView> Lines);

public sealed record LineView(Guid IngredientId, string Code, string Name, decimal GrossG, decimal NetG, bool MissingFood, IReadOnlyDictionary<string, decimal> Nutrients);

public sealed record CostView(decimal FoodCostPerServing, decimal OverheadPerServing, decimal CostPerServing, decimal Servings, decimal TotalCost,
    IReadOnlyList<DishCostView> Dishes, IReadOnlyList<OverheadCost> Overheads, IReadOnlyDictionary<string, decimal> ByFoodGroup,
    IReadOnlyList<string> MissingPriceIngredients, decimal? Budget, decimal? BudgetVariance, decimal? AgreedPrice, decimal? ExpectedMarginPerServing);

public sealed record DishCostView(Guid DishId, string Code, string Name, decimal ServingFactor, decimal Cost, IReadOnlyList<IngredientCost> Ingredients);

public static class EngineViews
{
    public static NutritionView ToView(this NutritionSummary s, MealTarget? target = null)
        => new(NutritionEngine.EffectiveEnergy(s), s.EnergyTableKcal, s.EnergyFromMacrosKcal,
            s.Totals.GetValueOrDefault(NutrientCodes.Protein), s.Totals.GetValueOrDefault(NutrientCodes.Lipid), s.Totals.GetValueOrDefault(NutrientCodes.Glucid),
            s.ProteinPct, s.LipidPct, s.GlucidPct, s.AnimalProteinG, s.AnimalProteinPct, s.Totals,
            s.Dishes.Select(d => new DishNutritionView(d.DishId, d.DishCode, d.DishName, d.Type.ToString(), d.ServingFactor, d.NetWeightG, d.CookedWeightG,
                d.Nutrients.GetValueOrDefault(NutrientCodes.Energy), d.Nutrients,
                d.Lines.Select(l => new LineView(l.IngredientId, l.IngredientCode, l.IngredientName, l.GrossG, l.NetG, l.MissingFood, l.Nutrients)).ToList())).ToList(),
            s.MissingFoodIngredients,
            target is null ? null : NutritionEngine.Compliance(s, target),
            target is null ? null : NutritionEngine.Score(s, target),
            target?.EnergyKcal);

    public static CostView ToView(this CostSummary c)
        => new(c.FoodCostPerServing, c.OverheadPerServing, c.CostPerServing, c.Servings, c.TotalCost,
            c.Dishes.Select(d => new DishCostView(d.DishId, d.Code, d.Name, d.ServingFactor, d.Cost, d.Ingredients)).ToList(),
            c.Overheads, c.ByFoodGroup, c.MissingPriceIngredients, c.Budget, c.BudgetVariance, c.AgreedPrice, c.ExpectedMarginPerServing);
}
