using System.Globalization;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Enums;

namespace BlueMeal.Application.Engines;

/// <summary>
/// Menu/recipe warning rules (SPEC §6.2). Thresholds come from <see cref="WarningSettings"/> and the recommendation
/// targets. Messages are produced in Vietnamese and English so they can be stored in the menu snapshot.
/// Daily-job rules (W-CERT-EXPIRY, W-STOCK-EXPIRY, W-STOCK-MIN, W-SAMPLE-DISPOSE) live in the alert scanner.
/// </summary>
public static class WarningEngine
{
    private static readonly CultureInfo Vi = new("vi-VN");
    private static readonly CultureInfo En = new("en-US");

    private static string N(decimal v, int d, CultureInfo c) => Math.Round(v, d, MidpointRounding.AwayFromZero).ToString("N" + d, c);

    public static IReadOnlyList<Warning> Evaluate(NutritionSummary nutrition, CostSummary? cost, IReadOnlyList<MenuDish> dishes, WarningContext ctx, WarningSettings s)
    {
        var list = new List<Warning>();
        if (ctx.Target is { } t)
        {
            Energy(nutrition, t, s, list);
            if (s.PlgEnabled) Plg(nutrition, t, s, list);
            if (s.AnimalProteinEnabled) Animal(nutrition, t, s, list);
            Micro(nutrition, t, ctx, list);
        }
        if (cost is not null) Cost(cost, s, list);
        Structure(dishes, ctx.Structure, list);
        Repeat(dishes, ctx.RecentDishIds, list);
        MissingData(nutrition, cost, s, list);
        Allergens(dishes, ctx.AllergensToAvoid, s, list);
        foreach (var sh in ctx.Shortages)
            list.Add(new Warning(WarningCodes.StockShort, WarningLevel.Info,
                $"Thiếu tồn kho {sh.IngredientName}: cần {N(sh.Required, 2, Vi)}, khả dụng {N(sh.Available, 2, Vi)}.",
                $"Stock shortage for {sh.IngredientName}: required {N(sh.Required, 2, En)}, available {N(sh.Available, 2, En)}.",
                sh.IngredientCode, sh.Required, sh.Available));
        return list;
    }

    // W-ENERGY-LOW / W-ENERGY-HIGH: < 90% / > 110% Warning; < 80% / > 120% Critical (configurable)
    internal static void Energy(NutritionSummary n, MealTarget t, WarningSettings s, List<Warning> list)
    {
        if (t.EnergyKcal <= 0) return;
        var e = NutritionEngine.EffectiveEnergy(n);
        var pct = e / t.EnergyKcal * 100m;
        if (pct < s.EnergyWarnLowPct)
        {
            var lvl = pct < s.EnergyCriticalLowPct ? WarningLevel.Critical : WarningLevel.Warning;
            list.Add(new Warning(WarningCodes.EnergyLow, lvl,
                $"Năng lượng {N(e, 0, Vi)} kcal chỉ đạt {N(pct, 0, Vi)}% mục tiêu bữa ({N(t.EnergyKcal, 0, Vi)} kcal).",
                $"Energy {N(e, 0, En)} kcal is only {N(pct, 0, En)}% of the meal target ({N(t.EnergyKcal, 0, En)} kcal).", NutrientCodes.Energy, pct, s.EnergyWarnLowPct));
        }
        else if (pct > s.EnergyWarnHighPct)
        {
            var lvl = pct > s.EnergyCriticalHighPct ? WarningLevel.Critical : WarningLevel.Warning;
            list.Add(new Warning(WarningCodes.EnergyHigh, lvl,
                $"Năng lượng {N(e, 0, Vi)} kcal vượt {N(pct, 0, Vi)}% mục tiêu bữa ({N(t.EnergyKcal, 0, Vi)} kcal).",
                $"Energy {N(e, 0, En)} kcal is {N(pct, 0, En)}% of the meal target ({N(t.EnergyKcal, 0, En)} kcal).", NutrientCodes.Energy, pct, s.EnergyWarnHighPct));
        }
    }

    // W-PLG-RANGE: %P, %L, %G outside recommended ranges
    internal static void Plg(NutritionSummary n, MealTarget t, WarningSettings s, List<Warning> list)
    {
        if (n.EnergyFromMacrosKcal <= 0) return;
        void Check(string code, string vi, string en, decimal v, decimal min, decimal max)
        {
            if (v >= min && v <= max) return;
            list.Add(new Warning(WarningCodes.PlgRange, s.PlgLevel,
                $"Tỷ lệ năng lượng từ {vi} {N(v, 1, Vi)}% ngoài khoảng khuyến nghị {N(min, 0, Vi)}–{N(max, 0, Vi)}%.",
                $"Energy from {en} {N(v, 1, En)}% is outside the recommended {N(min, 0, En)}–{N(max, 0, En)}%.", code, v, v < min ? min : max));
        }
        Check(NutrientCodes.Protein, "protein", "protein", n.ProteinPct, t.ProteinPctMin, t.ProteinPctMax);
        Check(NutrientCodes.Lipid, "lipid", "fat", n.LipidPct, t.LipidPctMin, t.LipidPctMax);
        Check(NutrientCodes.Glucid, "glucid", "carbohydrate", n.GlucidPct, t.GlucidPctMin, t.GlucidPctMax);
    }

    // W-ANIMAL-PROTEIN
    internal static void Animal(NutritionSummary n, MealTarget t, WarningSettings s, List<Warning> list)
    {
        if (n.Totals.GetValueOrDefault(NutrientCodes.Protein) <= 0) return;
        var v = n.AnimalProteinPct;
        if (v >= t.AnimalProteinPctMin && v <= t.AnimalProteinPctMax) return;
        list.Add(new Warning(WarningCodes.AnimalProtein, s.AnimalProteinLevel,
            $"Tỷ lệ đạm động vật/tổng đạm {N(v, 1, Vi)}% ngoài khoảng {N(t.AnimalProteinPctMin, 0, Vi)}–{N(t.AnimalProteinPctMax, 0, Vi)}%.",
            $"Animal/total protein {N(v, 1, En)}% is outside {N(t.AnimalProteinPctMin, 0, En)}–{N(t.AnimalProteinPctMax, 0, En)}%.", NutrientCodes.Protein, v,
            v < t.AnimalProteinPctMin ? t.AnimalProteinPctMin : t.AnimalProteinPctMax));
    }

    // W-NUTRIENT-MIN / W-NUTRIENT-MAX (level per recommendation_nutrient)
    internal static void Micro(NutritionSummary n, MealTarget t, WarningContext ctx, List<Warning> list)
    {
        foreach (var target in t.Nutrients)
        {
            var actual = n.Totals.GetValueOrDefault(target.Code);
            var name = ctx.NutrientNames?.GetValueOrDefault(target.Code) ?? target.Code;
            var d = NutritionEngine.DecimalsFor(target.Code);
            if (target.Min is { } min && actual < min)
                list.Add(new Warning(WarningCodes.NutrientMin, target.Level,
                    $"{name} {N(actual, d, Vi)} thấp hơn mức tối thiểu {N(min, d, Vi)}/bữa.",
                    $"{name} {N(actual, d, En)} is below the minimum {N(min, d, En)} per meal.", target.Code, actual, min));
            if (target.Max is { } max && actual > max)
                list.Add(new Warning(WarningCodes.NutrientMax, target.Level,
                    $"{name} {N(actual, d, Vi)} vượt mức tối đa {N(max, d, Vi)}/bữa.",
                    $"{name} {N(actual, d, En)} exceeds the maximum {N(max, d, En)} per meal.", target.Code, actual, max));
        }
    }

    // W-COST-OVER: > budget Warning; > budget × (1 + x%) Critical
    internal static void Cost(CostSummary c, WarningSettings s, List<Warning> list)
    {
        if (c.Budget is not { } budget || budget <= 0 || c.CostPerServing <= budget) return;
        var overPct = (c.CostPerServing - budget) / budget * 100m;
        var lvl = overPct > s.CostCriticalOverPct ? WarningLevel.Critical : WarningLevel.Warning;
        list.Add(new Warning(WarningCodes.CostOver, lvl,
            $"Chi phí {N(c.CostPerServing, 0, Vi)} ₫/suất vượt ngân sách {N(budget, 0, Vi)} ₫ ({N(overPct, 1, Vi)}%).",
            $"Cost {N(c.CostPerServing, 0, En)} VND/serving exceeds the budget {N(budget, 0, En)} VND ({N(overPct, 1, En)}%).", null, c.CostPerServing, budget));
    }

    // W-STRUCTURE: missing required slot → Critical
    internal static void Structure(IReadOnlyList<MenuDish> dishes, IReadOnlyList<StructureSlot> structure, List<Warning> list)
    {
        foreach (var slot in structure.Where(s => s.IsRequired))
        {
            var have = dishes.Count(d => d.Dish.Type == slot.Type);
            if (have >= slot.Quantity) continue;
            list.Add(new Warning(WarningCodes.Structure, WarningLevel.Critical,
                $"Thiếu món bắt buộc theo cơ cấu bữa: {DishTypeVi(slot.Type)} (cần {slot.Quantity}, có {have}).",
                $"Missing required dish for the meal structure: {slot.Type} (need {slot.Quantity}, have {have}).", slot.Type.ToString(), have, slot.Quantity));
        }
    }

    // W-REPEAT-DISH: dish used in the last N days for the same subject → Info
    internal static void Repeat(IReadOnlyList<MenuDish> dishes, IReadOnlySet<Guid> recent, List<Warning> list)
    {
        foreach (var d in dishes.Where(d => recent.Contains(d.Dish.DishId)).DistinctBy(d => d.Dish.DishId))
            list.Add(new Warning(WarningCodes.RepeatDish, WarningLevel.Info,
                $"Món \"{d.Dish.Name}\" đã có trong thực đơn những ngày gần đây.",
                $"Dish \"{d.Dish.NameEn ?? d.Dish.Name}\" was served recently.", d.Dish.Code));
    }

    // W-MISSING-DATA: ingredient without nutrition data or without price
    internal static void MissingData(NutritionSummary n, CostSummary? c, WarningSettings s, List<Warning> list)
    {
        foreach (var code in n.MissingFoodIngredients)
            list.Add(new Warning(WarningCodes.MissingData, s.MissingDataLevel,
                $"Nguyên liệu {code} chưa ánh xạ thực phẩm — thiếu dữ liệu dinh dưỡng.",
                $"Ingredient {code} is not mapped to a food — nutrition data missing.", code));
        if (c is null) return;
        foreach (var code in c.MissingPriceIngredients)
            list.Add(new Warning(WarningCodes.MissingData, s.MissingDataLevel,
                $"Nguyên liệu {code} chưa có đơn giá.", $"Ingredient {code} has no unit price.", code));
    }

    // W-ALLERGEN
    internal static void Allergens(IReadOnlyList<MenuDish> dishes, IReadOnlyCollection<string> avoid, WarningSettings s, List<Warning> list)
    {
        if (avoid.Count == 0) return;
        var set = avoid.Select(a => a.Trim().ToUpperInvariant()).Where(a => a.Length > 0).ToHashSet();
        foreach (var d in dishes.DistinctBy(x => x.Dish.DishId))
        {
            var dishAllergens = Split(d.Dish.Allergens).Concat(d.Dish.Lines.SelectMany(l => Split(l.Ingredient.Allergens))).ToHashSet();
            var hit = dishAllergens.Intersect(set).ToList();
            if (hit.Count == 0) continue;
            list.Add(new Warning(WarningCodes.Allergen, s.AllergenLevel,
                $"Món \"{d.Dish.Name}\" chứa chất gây dị ứng cần tránh: {string.Join(", ", hit)}.",
                $"Dish \"{d.Dish.NameEn ?? d.Dish.Name}\" contains allergens to avoid: {string.Join(", ", hit)}.", d.Dish.Code));
        }
    }

    public static IEnumerable<string> Split(string? csv) => (csv ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToUpperInvariant());

    public static string DishTypeVi(DishType t) => t switch
    {
        DishType.MainProtein => "Món mặn", DishType.StirFry => "Món xào", DishType.Soup => "Canh", DishType.Vegetable => "Rau",
        DishType.Staple => "Cơm/tinh bột", DishType.Dessert => "Tráng miệng", DishType.Drink => "Đồ uống", _ => "Khác",
    };

    public static int CriticalCount(IEnumerable<Warning> w) => w.Count(x => x.Level == WarningLevel.Critical);
}
