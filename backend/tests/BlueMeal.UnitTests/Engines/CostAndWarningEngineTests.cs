using BlueMeal.Application.Engines;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Enums;

namespace BlueMeal.UnitTests.Engines;

/// <summary>SPEC §6.3 — expected values computed by hand.</summary>
public class CostEngineTests
{
    private static readonly OverheadRule[] Overheads =
    [
        new("FUEL", "Chất đốt", "Fuel", OverheadMode.PercentOfFood, 3),
        new("LABOR", "Nhân công", "Labor", OverheadMode.FixedPerServing, 3000),
    ];

    [Fact] // 130 g / 1000 × 18 000 = 2 340
    public void Ingredient_cost_is_gross_over_grams_per_unit_times_price()
        => CostEngine.IngredientCost(new RecipeLine(F.IRice, 130), 1).Cost.Should().Be(2340m);

    [Fact] // oil: 3 g / 920 × 45 000 = 146.7391304…
    public void Ingredient_cost_with_non_1000_gram_unit()
        => CostEngine.IngredientCost(new RecipeLine(F.IOil, 3), 1).Cost.Should().BeApproximately(146.7391m, 0.0001m);

    [Fact] // gross weight, not net: chicken 160 g / 1000 × 120 000 = 19 200
    public void Cost_uses_gross_weight()
        => CostEngine.IngredientCost(new RecipeLine(F.IChicken, 160), 1).Cost.Should().Be(19200m);

    [Fact]
    public void Missing_price_costs_zero_and_is_flagged()
    {
        var c = CostEngine.IngredientCost(new RecipeLine(F.INoPrice, 100), 1);
        c.Cost.Should().Be(0); c.MissingPrice.Should().BeTrue();
    }

    [Fact] // recipe for 10 servings, 1 300 g rice = 23 400 ₫ → 2 340/serving, × factor 1.2 = 2 808
    public void Dish_cost_per_serving_with_factor()
    {
        var dish = F.Dish("COM10", DishType.Staple, CookingMethod.Boil, 10, (F.IRice, 1300));
        CostEngine.DishCost(dish, 1.2m).Cost.Should().Be(2808m);
    }

    [Fact] // food 2 340 + 13 000 = 15 340; fuel 3 % = 460.2; labor 3 000 → overhead 3 460.2; per serving 18 800.2
    public void Overheads_percent_and_fixed()
    {
        var c = CostEngine.ComputeMenu([new MenuDish(F.RiceDish), new MenuDish(F.PorkDish)], Overheads);
        c.FoodCostPerServing.Should().Be(15340m);
        c.Overheads.Single(o => o.Code == "FUEL").Amount.Should().Be(460.2m);
        c.OverheadPerServing.Should().Be(3460.2m);
        c.CostPerServing.Should().Be(18800.2m);
    }

    [Fact] // 350 servings × 18 800.2 = 6 580 070; budget 18 000 → +800.2; agreed price 20 000 → margin 1 199.8
    public void Totals_budget_variance_and_margin()
    {
        var c = CostEngine.ComputeMenu([new MenuDish(F.RiceDish), new MenuDish(F.PorkDish)], Overheads, 350, 18000, 20000);
        c.TotalCost.Should().Be(6580070m);
        c.BudgetVariance.Should().Be(800.2m);
        c.ExpectedMarginPerServing.Should().Be(1199.8m);
    }

    [Fact]
    public void Cost_by_food_group()
    {
        var c = CostEngine.ComputeMenu([new MenuDish(F.RiceDish), new MenuDish(F.PorkDish)], []);
        c.ByFoodGroup["THIT"].Should().Be(13000m);
        c.ByFoodGroup["RAU"].Should().Be(2340m); // fixture group of non-animal foods
    }

    [Fact] // 350 servings × 130 g = 45 500 g = 45.5 kg
    public void Demand_for_servings()
    {
        var d = CostEngine.Demand([new MenuDish(F.RiceDish)], 350).Single();
        d.GrossG.Should().Be(45500m);
        d.PurchaseUnits.Should().Be(45.5m);
    }

    [Fact]
    public void Demand_merges_same_ingredient_across_dishes()
    {
        var porkSoup = F.Dish("CANH", DishType.Soup, CookingMethod.Boil, 1, (F.IPork, 20));
        CostEngine.Demand([new MenuDish(F.PorkDish), new MenuDish(porkSoup)], 100).Single().GrossG.Should().Be(12000m);
    }
}

/// <summary>SPEC §6.2 warning rules.</summary>
public class WarningEngineTests
{
    private static readonly WarningSettings S = new();

    private static IReadOnlyList<Warning> Eval(NutritionSummary n, MealTarget? t = null, CostSummary? c = null, IReadOnlyList<MenuDish>? dishes = null,
        IReadOnlyList<StructureSlot>? structure = null, IReadOnlySet<Guid>? recent = null, string[]? allergens = null, IReadOnlyList<StockShortage>? shortages = null)
        => WarningEngine.Evaluate(n, c, dishes ?? [], new WarningContext(t, structure ?? [], recent ?? new HashSet<Guid>(), allergens ?? [], shortages ?? []), S);

    // A summary whose P:L:G and animal ratio are inside the default ranges, so only the energy rule can fire.
    private static NutritionSummary Balanced(decimal energy) => F.Summary(energy, energy * 0.15m / 4, energy * 0.22m / 9, energy * 0.63m / 4, energy * 0.15m / 4 * 0.4m);

    [Theory] // target 910 kcal: 85 % → Warning, 75 % → Critical, 100 % → none, 115 % → Warning, 125 % → Critical
    [InlineData(773.5, "W-ENERGY-LOW", WarningLevel.Warning)]
    [InlineData(682.5, "W-ENERGY-LOW", WarningLevel.Critical)]
    [InlineData(1046.5, "W-ENERGY-HIGH", WarningLevel.Warning)]
    [InlineData(1137.5, "W-ENERGY-HIGH", WarningLevel.Critical)]
    public void Energy_thresholds(double energy, string code, WarningLevel level)
    {
        var w = Eval(Balanced((decimal)energy), F.Target()).Single();
        w.Code.Should().Be(code);
        w.Level.Should().Be(level);
        w.MessageVi.Should().Contain("kcal");
        w.MessageEn.Should().Contain("kcal");
    }

    [Fact]
    public void Energy_in_range_has_no_warning() => Eval(Balanced(910), F.Target()).Should().BeEmpty();

    [Fact] // %P 10, %L 35, %G 55 → protein and lipid outside, glucid inside
    public void Plg_out_of_range()
    {
        var n = F.Summary(910, 910 * 0.10m / 4, 910 * 0.35m / 9, 910 * 0.55m / 4, 910 * 0.10m / 4 * 0.4m);
        var w = Eval(n, F.Target()).Where(x => x.Code == WarningCodes.PlgRange).ToList();
        w.Select(x => x.Subject).Should().BeEquivalentTo(["PROTEIN", "LIPID"]);
    }

    [Fact] // animal protein 10 % < 30 %
    public void Animal_protein_out_of_range()
    {
        var n = F.Summary(910, 34.125m, 22.2444m, 143.325m, 3.4125m);
        Eval(n, F.Target()).Should().ContainSingle(x => x.Code == WarningCodes.AnimalProtein);
    }

    [Fact]
    public void Micro_nutrient_min_and_max_use_configured_level()
    {
        var t = F.Target(nutrients: [new NutrientTarget("FIBER", 7, null, WarningLevel.Info), new NutrientTarget("NA", null, 800, WarningLevel.Critical)]);
        var n = F.Summary(910, 34.125m, 22.2444m, 143.325m, 13.65m, ("FIBER", 3), ("NA", 1200));
        var w = Eval(n, t);
        w.Should().ContainSingle(x => x.Code == WarningCodes.NutrientMin && x.Level == WarningLevel.Info);
        w.Should().ContainSingle(x => x.Code == WarningCodes.NutrientMax && x.Level == WarningLevel.Critical);
    }

    [Theory] // budget 35 000: 36 750 (+5 %) Warning; 40 250 (+15 %) Critical; 34 000 none
    [InlineData(36750, WarningLevel.Warning)]
    [InlineData(40250, WarningLevel.Critical)]
    public void Cost_over_budget(double perServing, WarningLevel level)
    {
        var cost = new CostSummary((decimal)perServing, 0, (decimal)perServing, 1, (decimal)perServing, [], [], new Dictionary<string, decimal>(), [], 35000, (decimal)perServing - 35000, null, null);
        Eval(Balanced(910), F.Target(), cost).Single().Level.Should().Be(level);
    }

    [Fact]
    public void Cost_under_budget_no_warning()
    {
        var cost = new CostSummary(34000, 0, 34000, 1, 34000, [], [], new Dictionary<string, decimal>(), [], 35000, -1000, null, null);
        Eval(Balanced(910), F.Target(), cost).Should().BeEmpty();
    }

    [Fact] // required soup missing → Critical; optional dessert missing → nothing
    public void Missing_required_slot_is_critical()
    {
        var structure = new[] { new StructureSlot(DishType.Staple, 1, true), new StructureSlot(DishType.Soup, 1, true), new StructureSlot(DishType.Dessert, 1, false) };
        var w = Eval(Balanced(910), null, null, [new MenuDish(F.RiceDish)], structure);
        w.Should().ContainSingle(x => x.Code == WarningCodes.Structure && x.Level == WarningLevel.Critical && x.Subject == "Soup");
        WarningEngine.CriticalCount(w).Should().Be(1);
    }

    [Fact]
    public void Repeated_dish_is_info()
    {
        var w = Eval(Balanced(910), null, null, [new MenuDish(F.PorkDish)], recent: new HashSet<Guid> { F.PorkDish.DishId });
        w.Single().Should().Match<Warning>(x => x.Code == WarningCodes.RepeatDish && x.Level == WarningLevel.Info);
    }

    [Fact]
    public void Missing_nutrition_data_and_price()
    {
        var dish = F.Dish("X", DishType.Other, CookingMethod.Boil, 1, (F.IUnmapped, 50), (F.INoPrice, 50));
        var n = new NutritionEngine().ComputeMenu([new MenuDish(dish)]);
        var c = CostEngine.ComputeMenu([new MenuDish(dish)], []);
        var w = Eval(n, null, c, [new MenuDish(dish)]).Where(x => x.Code == WarningCodes.MissingData).ToList();
        w.Select(x => x.Subject).Should().BeEquivalentTo(["NL-LA", "NL-FREE"]);
    }

    [Fact] // shrimp ingredient carries SHELLFISH; customer avoids shellfish
    public void Allergen_is_detected_from_ingredients()
    {
        var dish = F.Dish("TOM", DishType.MainProtein, CookingMethod.StirFry, 1, (F.IShrimp, 100));
        var w = Eval(Balanced(910), null, null, [new MenuDish(dish)], allergens: ["shellfish", " "]);
        w.Should().ContainSingle(x => x.Code == WarningCodes.Allergen && x.MessageVi.Contains("SHELLFISH"));
    }

    [Fact]
    public void Stock_shortage_is_info()
    {
        var w = Eval(Balanced(910), shortages: [new StockShortage("NL-GAO", "Gạo", 45.5m, 30m)]);
        w.Single().Should().Match<Warning>(x => x.Code == WarningCodes.StockShort && x.Level == WarningLevel.Info);
    }

    [Fact]
    public void Disabled_rules_do_not_fire()
    {
        var s = new WarningSettings { PlgEnabled = false, AnimalProteinEnabled = false };
        var n = F.Summary(910, 910 * 0.10m / 4, 910 * 0.35m / 9, 910 * 0.55m / 4, 0);
        WarningEngine.Evaluate(n, null, [], new WarningContext(F.Target(), [], new HashSet<Guid>(), [], []), s).Should().BeEmpty();
    }
}
