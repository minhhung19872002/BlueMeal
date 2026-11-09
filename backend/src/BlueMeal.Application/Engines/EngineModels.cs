using BlueMeal.Domain.Enums;

namespace BlueMeal.Application.Engines;

/// <summary>Standard nutrient codes used by the engines (SPEC §5.3 seed).</summary>
public static class NutrientCodes
{
    public const string Energy = "ENERGY";
    public const string Protein = "PROTEIN";
    public const string Lipid = "LIPID";
    public const string Glucid = "GLUCID";
    public const string Fiber = "FIBER";
    public const string Sodium = "NA";
}

/// <summary>Food composition data: values per 100 g edible portion.</summary>
public sealed record FoodData(Guid Id, string Code, decimal RefuseRate, bool IsAnimalSource, IReadOnlyDictionary<string, decimal> Per100g, string? GroupCode = null, string? GroupName = null);

/// <summary>A purchasable ingredient, optionally mapped to a food. UnitPrice is per purchase unit (already resolved by price mode).</summary>
public sealed record IngredientData(Guid Id, string Code, string Name, FoodData? Food, decimal GramsPerUnit, decimal? UnitPrice, string? Allergens = null);

public sealed record RecipeLine(IngredientData Ingredient, decimal GrossG, decimal? NetGOverride = null);

/// <summary>A dish with its (current or locked) recipe. Quantities are for <see cref="BaseServings"/> servings.</summary>
public sealed record DishData(Guid DishId, string Code, string Name, DishType Type, CookingMethod Method, decimal BaseServings, IReadOnlyList<RecipeLine> Lines,
    string? Allergens = null, Guid? RecipeId = null, string? NameEn = null);

public sealed record MenuDish(DishData Dish, decimal ServingFactor = 1m);

/// <summary>Retention factor lookup (nutrient code, cooking method) → factor (default 1).</summary>
public interface IRetentionFactors
{
    decimal Get(string nutrientCode, CookingMethod method);
}

public sealed class RetentionTable(IReadOnlyDictionary<(string Code, CookingMethod Method), decimal>? factors = null) : IRetentionFactors
{
    public static readonly RetentionTable None = new();
    private readonly IReadOnlyDictionary<(string, CookingMethod), decimal> _f = factors ?? new Dictionary<(string, CookingMethod), decimal>();
    public decimal Get(string nutrientCode, CookingMethod method) => _f.TryGetValue((nutrientCode, method), out var v) ? v : 1m;
}

// ---------------------------------------------------------------- nutrition results

public sealed record LineNutrition(Guid IngredientId, string IngredientCode, string IngredientName, decimal GrossG, decimal NetG, bool MissingFood,
    IReadOnlyDictionary<string, decimal> Nutrients, decimal AnimalProteinG);

public sealed record DishNutrition(Guid DishId, string DishCode, string DishName, DishType Type, decimal ServingFactor, decimal NetWeightG, decimal CookedWeightG,
    IReadOnlyDictionary<string, decimal> Nutrients, decimal AnimalProteinG, IReadOnlyList<LineNutrition> Lines, IReadOnlyList<string> MissingFoodIngredients);

/// <summary>Aggregated nutrition of a menu (or a single dish) per serving.</summary>
public sealed record NutritionSummary(
    IReadOnlyDictionary<string, decimal> Totals,
    decimal EnergyTableKcal,
    decimal EnergyFromMacrosKcal,
    decimal ProteinPct,
    decimal LipidPct,
    decimal GlucidPct,
    decimal AnimalProteinG,
    decimal AnimalProteinPct,
    IReadOnlyList<DishNutrition> Dishes,
    IReadOnlyList<string> MissingFoodIngredients);

// ---------------------------------------------------------------- targets

public sealed record NutrientTarget(string Code, decimal? Min, decimal? Max, WarningLevel Level);

/// <summary>Per-meal target derived from a recommendation (SPEC §6.1): energy = daily × meal share.</summary>
public sealed record MealTarget(
    decimal EnergyKcal,
    decimal ProteinPctMin, decimal ProteinPctMax,
    decimal LipidPctMin, decimal LipidPctMax,
    decimal GlucidPctMin, decimal GlucidPctMax,
    decimal AnimalProteinPctMin, decimal AnimalProteinPctMax,
    IReadOnlyList<NutrientTarget> Nutrients,
    decimal? BudgetPerServing)
{
    public static MealTarget FromDaily(decimal dailyKcal, decimal mealSharePct, decimal pMin, decimal pMax, decimal lMin, decimal lMax, decimal gMin, decimal gMax,
        decimal animalMin, decimal animalMax, IReadOnlyList<NutrientTarget> nutrients, decimal? budget)
        => new(dailyKcal * mealSharePct / 100m, pMin, pMax, lMin, lMax, gMin, gMax, animalMin, animalMax, nutrients, budget);
}

/// <summary>% achievement of a target (SPEC §6.1). Pct is null when no target exists.</summary>
public sealed record ComplianceItem(string Code, decimal Actual, decimal? Min, decimal? Max, decimal? Target, decimal? Pct, bool BelowMin, bool AboveMax);

// ---------------------------------------------------------------- cost

public sealed record OverheadRule(string Code, string NameVi, string NameEn, OverheadMode Mode, decimal Value);

public sealed record IngredientCost(Guid IngredientId, string Code, string Name, decimal GrossG, decimal? UnitPrice, decimal Cost, bool MissingPrice, string? GroupCode, string? GroupName);
public sealed record DishCost(Guid DishId, string Code, string Name, decimal ServingFactor, decimal Cost, IReadOnlyList<IngredientCost> Ingredients);
public sealed record OverheadCost(string Code, string NameVi, string NameEn, decimal Amount);

public sealed record CostSummary(
    decimal FoodCostPerServing,
    decimal OverheadPerServing,
    decimal CostPerServing,
    decimal Servings,
    decimal TotalCost,
    IReadOnlyList<DishCost> Dishes,
    IReadOnlyList<OverheadCost> Overheads,
    IReadOnlyDictionary<string, decimal> ByFoodGroup,
    IReadOnlyList<string> MissingPriceIngredients,
    decimal? Budget,
    decimal? BudgetVariance,
    decimal? AgreedPrice,
    decimal? ExpectedMarginPerServing);

// ---------------------------------------------------------------- warnings

public sealed record Warning(string Code, WarningLevel Level, string MessageVi, string MessageEn, string? Subject = null, decimal? Value = null, decimal? Limit = null);

public static class WarningCodes
{
    public const string EnergyLow = "W-ENERGY-LOW";
    public const string EnergyHigh = "W-ENERGY-HIGH";
    public const string PlgRange = "W-PLG-RANGE";
    public const string AnimalProtein = "W-ANIMAL-PROTEIN";
    public const string NutrientMin = "W-NUTRIENT-MIN";
    public const string NutrientMax = "W-NUTRIENT-MAX";
    public const string CostOver = "W-COST-OVER";
    public const string RepeatDish = "W-REPEAT-DISH";
    public const string Structure = "W-STRUCTURE";
    public const string MissingData = "W-MISSING-DATA";
    public const string Allergen = "W-ALLERGEN";
    public const string StockShort = "W-STOCK-SHORT";
    public const string CertExpiry = "W-CERT-EXPIRY";
    public const string StockExpiry = "W-STOCK-EXPIRY";
    public const string StockMin = "W-STOCK-MIN";
    public const string SampleDispose = "W-SAMPLE-DISPOSE";
}

/// <summary>Required/optional slot of a meal structure (e.g. 1 × MainProtein, required).</summary>
public sealed record StructureSlot(DishType Type, int Quantity, bool IsRequired);

public sealed record StockShortage(string IngredientCode, string IngredientName, decimal Required, decimal Available);

/// <summary>Everything the warning engine needs about the context of a menu.</summary>
public sealed record WarningContext(
    MealTarget? Target,
    IReadOnlyList<StructureSlot> Structure,
    IReadOnlySet<Guid> RecentDishIds,
    IReadOnlyCollection<string> AllergensToAvoid,
    IReadOnlyList<StockShortage> Shortages,
    IReadOnlyDictionary<string, string>? NutrientNames = null);
