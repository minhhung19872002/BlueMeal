namespace BlueMeal.Application.Engines;

/// <summary>
/// Nutrition calculation (SPEC §6.1). Pure, deterministic, decimal arithmetic; no rounding except via <see cref="Round"/>.
/// <code>
/// net_g        = override_net_g ?? gross_g × (1 − refuse_rate/100)
/// nutrient_val = net_g / 100 × value_per_100g_edible × retention_factor(nutrient, cooking_method)
/// dish         = Σ lines / base_servings × serving_factor
/// menu         = Σ dishes
/// %P = protein×4/E, %L = lipid×9/E, %G = glucid×4/E   (E = 4P + 9L + 4G)
/// </code>
/// </summary>
public sealed class NutritionEngine(IRetentionFactors? retention = null)
{
    private readonly IRetentionFactors _retention = retention ?? RetentionTable.None;

    public static decimal NetGrams(RecipeLine line)
    {
        if (line.NetGOverride is { } o) return o;
        var refuse = line.Ingredient.Food?.RefuseRate ?? 0m;
        return line.GrossG * (1m - refuse / 100m);
    }

    public LineNutrition ComputeLine(RecipeLine line, Domain.Enums.CookingMethod method)
    {
        var net = NetGrams(line);
        var food = line.Ingredient.Food;
        var values = new Dictionary<string, decimal>();
        decimal animal = 0;
        if (food is not null)
        {
            foreach (var (code, per100) in food.Per100g)
                values[code] = net / 100m * per100 * _retention.Get(code, method);
            if (food.IsAnimalSource && values.TryGetValue(NutrientCodes.Protein, out var p)) animal = p;
        }
        return new LineNutrition(line.Ingredient.Id, line.Ingredient.Code, line.Ingredient.Name, line.GrossG, net, food is null, values, animal);
    }

    /// <summary>Nutrition of ONE serving of a dish, multiplied by the serving factor.</summary>
    public DishNutrition ComputeDish(DishData dish, decimal servingFactor = 1m, decimal cookingLossRate = 0m)
    {
        if (dish.BaseServings <= 0) throw new ArgumentException("BaseServings must be > 0", nameof(dish));
        var scale = servingFactor / dish.BaseServings;
        var lines = dish.Lines.Select(l => ComputeLine(l, dish.Method)).ToList();
        var totals = new Dictionary<string, decimal>();
        foreach (var l in lines)
            foreach (var (code, v) in l.Nutrients)
                totals[code] = totals.GetValueOrDefault(code) + v * scale;
        var net = lines.Sum(l => l.NetG) * scale;
        return new DishNutrition(dish.DishId, dish.Code, dish.Name, dish.Type, servingFactor, net, net * (1m - cookingLossRate / 100m), totals,
            lines.Sum(l => l.AnimalProteinG) * scale, lines, lines.Where(l => l.MissingFood).Select(l => l.IngredientCode).Distinct().ToList());
    }

    public NutritionSummary ComputeMenu(IEnumerable<MenuDish> dishes)
        => Summarize(dishes.Select(d => ComputeDish(d.Dish, d.ServingFactor)).ToList());

    public static NutritionSummary Summarize(IReadOnlyList<DishNutrition> dishes)
    {
        var totals = new Dictionary<string, decimal>();
        foreach (var d in dishes)
            foreach (var (code, v) in d.Nutrients)
                totals[code] = totals.GetValueOrDefault(code) + v;
        var p = totals.GetValueOrDefault(NutrientCodes.Protein);
        var l = totals.GetValueOrDefault(NutrientCodes.Lipid);
        var g = totals.GetValueOrDefault(NutrientCodes.Glucid);
        var eMacro = p * 4m + l * 9m + g * 4m;
        var animal = dishes.Sum(d => d.AnimalProteinG);
        return new NutritionSummary(
            totals,
            totals.GetValueOrDefault(NutrientCodes.Energy),
            eMacro,
            eMacro == 0 ? 0 : p * 4m / eMacro * 100m,
            eMacro == 0 ? 0 : l * 9m / eMacro * 100m,
            eMacro == 0 ? 0 : g * 4m / eMacro * 100m,
            animal,
            p == 0 ? 0 : animal / p * 100m,
            dishes,
            dishes.SelectMany(d => d.MissingFoodIngredients).Distinct().ToList());
    }

    /// <summary>
    /// % achievement against the meal target (SPEC §6.1): energy vs daily × meal share; other nutrients vs
    /// min_per_meal (or max_per_meal when only a maximum is declared).
    /// </summary>
    public static IReadOnlyList<ComplianceItem> Compliance(NutritionSummary s, MealTarget target)
    {
        var items = new List<ComplianceItem>();
        var energy = EffectiveEnergy(s);
        items.Add(new ComplianceItem(NutrientCodes.Energy, energy, null, null, target.EnergyKcal,
            target.EnergyKcal == 0 ? null : energy / target.EnergyKcal * 100m, false, false));
        foreach (var t in target.Nutrients)
        {
            var actual = s.Totals.GetValueOrDefault(t.Code);
            var reference = t.Min ?? t.Max;
            items.Add(new ComplianceItem(t.Code, actual, t.Min, t.Max, reference,
                reference is null or 0 ? null : actual / reference.Value * 100m,
                t.Min is { } mn && actual < mn, t.Max is { } mx && actual > mx));
        }
        return items;
    }

    /// <summary>Energy from the composition table when present, otherwise from macronutrients.</summary>
    public static decimal EffectiveEnergy(NutritionSummary s) => s.EnergyTableKcal > 0 ? s.EnergyTableKcal : s.EnergyFromMacrosKcal;

    /// <summary>Display rounding (SPEC §6.1): energy 0, macro 1, micro 2 decimals.</summary>
    public static decimal Round(string code, decimal value) => Math.Round(value, DecimalsFor(code), MidpointRounding.AwayFromZero);

    public static int DecimalsFor(string code) => code switch
    {
        NutrientCodes.Energy => 0,
        NutrientCodes.Protein or NutrientCodes.Lipid or NutrientCodes.Glucid or NutrientCodes.Fiber or "WATER" or "ASH" or "SUGAR" or "SFA" => 1,
        _ => 2,
    };

    /// <summary>
    /// Nutrition score 0–100 (higher is better): 100 − energy deviation − P:L:G deviation − animal protein deviation − micro violations.
    /// </summary>
    public static decimal Score(NutritionSummary s, MealTarget target)
    {
        var energy = EffectiveEnergy(s);
        var penalty = 0m;
        if (target.EnergyKcal > 0) penalty += Math.Abs(energy - target.EnergyKcal) / target.EnergyKcal * 100m * 1.5m;
        penalty += Deviation(s.ProteinPct, target.ProteinPctMin, target.ProteinPctMax);
        penalty += Deviation(s.LipidPct, target.LipidPctMin, target.LipidPctMax);
        penalty += Deviation(s.GlucidPct, target.GlucidPctMin, target.GlucidPctMax);
        penalty += Deviation(s.AnimalProteinPct, target.AnimalProteinPctMin, target.AnimalProteinPctMax) * 0.5m;
        foreach (var c in Compliance(s, target).Skip(1)) if (c.BelowMin || c.AboveMax) penalty += 5m;
        return Math.Clamp(Math.Round(100m - penalty, 1), 0m, 100m);
    }

    /// <summary>Distance (in percentage points) of a value outside [min, max]; 0 when inside.</summary>
    public static decimal Deviation(decimal value, decimal min, decimal max) => value < min ? min - value : value > max ? value - max : 0m;
}
