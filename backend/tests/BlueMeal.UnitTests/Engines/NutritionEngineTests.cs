using BlueMeal.Application.Engines;
using BlueMeal.Domain.Enums;

namespace BlueMeal.UnitTests.Engines;

/// <summary>SPEC §6.1 — expected values computed by hand.</summary>
public class NutritionEngineTests
{
    private readonly NutritionEngine _engine = new();

    [Fact] // 130 g, refuse 0 → net 130
    public void Net_equals_gross_without_refuse() => NutritionEngine.NetGrams(new RecipeLine(F.IRice, 130)).Should().Be(130m);

    [Fact] // 160 × (1 − 52/100) = 76.8
    public void Net_applies_refuse_rate() => NutritionEngine.NetGrams(new RecipeLine(F.IChicken, 160)).Should().Be(76.8m);

    [Fact]
    public void Net_override_wins() => NutritionEngine.NetGrams(new RecipeLine(F.IChicken, 160, 90)).Should().Be(90m);

    [Fact] // rice 130 g: P 1.3×7.9 = 10.27; L 1.3; G 98.67; E 447.2
    public void Line_nutrients_are_net_over_100_times_value()
    {
        var l = _engine.ComputeLine(new RecipeLine(F.IRice, 130), CookingMethod.Boil);
        l.Nutrients["PROTEIN"].Should().Be(10.27m);
        l.Nutrients["LIPID"].Should().Be(1.3m);
        l.Nutrients["GLUCID"].Should().Be(98.67m);
        l.Nutrients["ENERGY"].Should().Be(447.2m);
        l.MissingFood.Should().BeFalse();
    }

    [Fact] // spinach 200 g, refuse 15 → 170 g; Vit C 1.7 × 23 = 39.1; boiled retention 0.55 → 21.505
    public void Retention_factor_is_applied_per_nutrient_and_method()
    {
        var engine = new NutritionEngine(new RetentionTable(new Dictionary<(string, CookingMethod), decimal> { [("VIT_C", CookingMethod.Boil)] = 0.55m }));
        var l = engine.ComputeLine(new RecipeLine(F.ISpinach, 200), CookingMethod.Boil);
        l.NetG.Should().Be(170m);
        l.Nutrients["VIT_C"].Should().Be(21.505m);
        l.Nutrients["CA"].Should().Be(170m); // default factor 1 → 1.7 × 100
    }

    [Fact]
    public void Retention_only_for_matching_method()
    {
        var engine = new NutritionEngine(new RetentionTable(new Dictionary<(string, CookingMethod), decimal> { [("VIT_C", CookingMethod.Boil)] = 0.55m }));
        engine.ComputeLine(new RecipeLine(F.ISpinach, 200), CookingMethod.Raw).Nutrients["VIT_C"].Should().Be(39.1m);
    }

    [Fact]
    public void Unmapped_ingredient_has_no_nutrients_and_is_flagged()
    {
        var dish = F.Dish("X", DishType.Other, CookingMethod.Boil, 1, (F.IUnmapped, 50), (F.IRice, 100));
        var r = _engine.ComputeDish(dish);
        r.MissingFoodIngredients.Should().Equal("NL-LA");
        r.Lines.Single(x => x.IngredientCode == "NL-LA").Nutrients.Should().BeEmpty();
        r.Nutrients["PROTEIN"].Should().Be(7.9m);
    }

    [Fact] // pork 100 g → 19 g animal protein; rice → 0
    public void Animal_protein_only_from_animal_foods()
    {
        var menu = _engine.ComputeMenu([new MenuDish(F.PorkDish), new MenuDish(F.RiceDish)]);
        menu.AnimalProteinG.Should().Be(19m);
        menu.Totals["PROTEIN"].Should().Be(29.27m); // 19 + 10.27
        menu.AnimalProteinPct.Should().BeApproximately(64.9129m, 0.0001m); // 19 / 29.27
    }

    [Fact] // recipe for 10 servings with 1300 g rice → 1 serving = 130 g
    public void Dish_divides_by_base_servings()
    {
        var dish = F.Dish("COM10", DishType.Staple, CookingMethod.Boil, 10, (F.IRice, 1300));
        var r = _engine.ComputeDish(dish);
        r.Nutrients["PROTEIN"].Should().Be(10.27m);
        r.NetWeightG.Should().Be(130m);
    }

    [Fact] // serving factor 1.5 → protein 15.405
    public void Serving_factor_multiplies_dish()
    {
        _engine.ComputeDish(F.RiceDish, 1.5m).Nutrients["PROTEIN"].Should().Be(15.405m);
    }

    [Fact] // net 130, cooking loss 20% → cooked 104 g
    public void Cooked_weight_applies_cooking_loss()
    {
        _engine.ComputeDish(F.RiceDish, 1m, 20m).CookedWeightG.Should().Be(104m);
    }

    [Fact]
    public void Invalid_base_servings_throws()
    {
        var bad = F.RiceDish with { BaseServings = 0 };
        var act = () => _engine.ComputeDish(bad);
        act.Should().Throw<ArgumentException>();
    }

    [Fact] // menu = Σ dishes: energy 447.2 + 139 = 586.2
    public void Menu_sums_dishes()
    {
        var m = _engine.ComputeMenu([new MenuDish(F.RiceDish), new MenuDish(F.PorkDish)]);
        m.Totals["ENERGY"].Should().Be(586.2m);
        m.Dishes.Should().HaveCount(2);
    }

    [Fact] // P 20, L 10, G 100 → E = 80 + 90 + 400 = 570; %P 14.0351, %L 15.7895, %G 70.1754
    public void Energy_structure_from_macros()
    {
        var s = F.Summary(560, 20, 10, 100);
        s.EnergyFromMacrosKcal.Should().Be(570m);
        s.ProteinPct.Should().BeApproximately(14.0351m, 0.0001m);
        s.LipidPct.Should().BeApproximately(15.7895m, 0.0001m);
        s.GlucidPct.Should().BeApproximately(70.1754m, 0.0001m);
        (s.ProteinPct + s.LipidPct + s.GlucidPct).Should().BeApproximately(100m, 0.0000001m);
    }

    [Fact]
    public void Energy_structure_is_zero_without_macros()
    {
        var s = F.Summary(0, 0, 0, 0);
        s.ProteinPct.Should().Be(0); s.LipidPct.Should().Be(0); s.GlucidPct.Should().Be(0); s.AnimalProteinPct.Should().Be(0);
    }

    [Fact]
    public void Effective_energy_prefers_composition_table() => NutritionEngine.EffectiveEnergy(F.Summary(560, 20, 10, 100)).Should().Be(560m);

    [Fact]
    public void Effective_energy_falls_back_to_macros() => NutritionEngine.EffectiveEnergy(F.Summary(0, 20, 10, 100)).Should().Be(570m);

    [Fact] // target 2600 × 35% = 910 kcal; actual 819 → 90 %
    public void Compliance_energy_is_against_daily_times_meal_share()
    {
        var target = MealTarget.FromDaily(2600, 35, 13, 20, 20, 25, 55, 65, 30, 50, [], null);
        target.EnergyKcal.Should().Be(910m);
        var c = NutritionEngine.Compliance(F.Summary(819, 30, 20, 120), target);
        c[0].Code.Should().Be("ENERGY");
        c[0].Pct.Should().Be(90m);
    }

    [Fact] // Ca 120 vs min 170 → 70.588 %, below min
    public void Compliance_uses_min_per_meal()
    {
        var target = F.Target(nutrients: [new NutrientTarget("CA", 170, null, WarningLevel.Warning)]);
        var c = NutritionEngine.Compliance(F.Summary(900, 30, 20, 120, 0, ("CA", 120)), target).Single(x => x.Code == "CA");
        c.BelowMin.Should().BeTrue();
        c.Pct.Should().BeApproximately(70.5882m, 0.0001m);
    }

    [Fact] // Na 900 vs max 800 → 112.5 %, above max
    public void Compliance_uses_max_when_only_max_declared()
    {
        var target = F.Target(nutrients: [new NutrientTarget("NA", null, 800, WarningLevel.Warning)]);
        var c = NutritionEngine.Compliance(F.Summary(900, 30, 20, 120, 0, ("NA", 900)), target).Single(x => x.Code == "NA");
        c.AboveMax.Should().BeTrue();
        c.Pct.Should().Be(112.5m);
    }

    [Theory] // display rounding only at the end: energy 0, macro 1, micro 2 (SPEC §6.1)
    [InlineData("ENERGY", 446.5, 447)]
    [InlineData("PROTEIN", 10.27, 10.3)]
    [InlineData("GLUCID", 98.65, 98.7)]
    [InlineData("VIT_C", 21.505, 21.51)]
    [InlineData("CA", 170.004, 170.00)]
    public void Display_rounding(string code, double value, double expected)
        => NutritionEngine.Round(code, (decimal)value).Should().Be((decimal)expected);

    [Fact] // inside every range and exactly on energy target → 100
    public void Score_is_100_for_perfect_menu()
    {
        // E 910 = 4·P + 9·L + 4·G with %P 15, %L 22, %G 63 → P 34.125, L 22.244, G 143.325; animal 40 %
        var s = F.Summary(910, 34.125m, 22.2444m, 143.325m, 13.65m);
        NutritionEngine.Score(s, F.Target()).Should().Be(100m);
    }

    [Fact] // energy 728 (80 %): 20 × 1.5 = 30 penalty; macros in range → 70
    public void Score_penalizes_energy_deviation()
    {
        var s = F.Summary(728, 27.3m, 17.8m, 114.66m, 10.92m);
        NutritionEngine.Score(s, F.Target()).Should().BeApproximately(70m, 1.5m);
    }

    [Theory]
    [InlineData(10, 13, 20, 3)]
    [InlineData(25, 13, 20, 5)]
    [InlineData(15, 13, 20, 0)]
    public void Deviation_outside_range(double v, double min, double max, double expected)
        => NutritionEngine.Deviation((decimal)v, (decimal)min, (decimal)max).Should().Be((decimal)expected);
}
