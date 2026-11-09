using BlueMeal.Application.Engines;
using BlueMeal.Domain.Enums;

namespace BlueMeal.UnitTests.Engines;

/// <summary>Hand-made reference data. Expected values in the tests are computed by hand from these numbers.</summary>
internal static class F
{
    public static FoodData Food(string code, decimal refuse, bool animal, params (string Code, decimal V)[] per100)
        => new(Guid.NewGuid(), code, refuse, animal, per100.ToDictionary(x => x.Code, x => x.V), animal ? "THIT" : "RAU", animal ? "Thịt" : "Rau");

    // per 100 g edible
    public static readonly FoodData Rice = Food("RICE", 0, false, ("ENERGY", 344), ("PROTEIN", 7.9m), ("LIPID", 1.0m), ("GLUCID", 75.9m), ("FE", 1.3m));
    public static readonly FoodData Pork = Food("PORK", 0, true, ("ENERGY", 139), ("PROTEIN", 19m), ("LIPID", 7m), ("GLUCID", 0m), ("NA", 76m));
    public static readonly FoodData Chicken = Food("CHICKEN", 52, true, ("ENERGY", 199), ("PROTEIN", 20.3m), ("LIPID", 13.1m), ("GLUCID", 0m));
    public static readonly FoodData Spinach = Food("SPINACH", 15, false, ("ENERGY", 23), ("PROTEIN", 3.2m), ("LIPID", 0m), ("GLUCID", 2.5m), ("VIT_C", 23m), ("CA", 100m));
    public static readonly FoodData Oil = Food("OIL", 0, false, ("ENERGY", 897), ("LIPID", 99.7m));

    public static IngredientData Ing(string code, FoodData? food, decimal gramsPerUnit, decimal? price, string? allergens = null)
        => new(Guid.NewGuid(), code, code, food, gramsPerUnit, price, allergens);

    public static readonly IngredientData IRice = Ing("NL-GAO", Rice, 1000, 18000);
    public static readonly IngredientData IPork = Ing("NL-THIT", Pork, 1000, 130000);
    public static readonly IngredientData IChicken = Ing("NL-GA", Chicken, 1000, 120000);
    public static readonly IngredientData ISpinach = Ing("NL-RAU", Spinach, 1000, 15000);
    public static readonly IngredientData IOil = Ing("NL-DAU", Oil, 920, 45000);
    public static readonly IngredientData IUnmapped = Ing("NL-LA", null, 1000, 10000);
    public static readonly IngredientData INoPrice = Ing("NL-FREE", Spinach, 1000, null);
    public static readonly IngredientData IShrimp = Ing("NL-TOM", Pork, 1000, 180000, "SHELLFISH");

    public static DishData Dish(string code, DishType type, CookingMethod method, decimal baseServings, params (IngredientData I, decimal G)[] lines)
        => new(Guid.NewGuid(), code, code, type, method, baseServings, lines.Select(l => new RecipeLine(l.I, l.G)).ToList());

    public static readonly DishData RiceDish = Dish("COM", DishType.Staple, CookingMethod.Boil, 1, (IRice, 130));
    public static readonly DishData PorkDish = Dish("THIT", DishType.MainProtein, CookingMethod.Boil, 1, (IPork, 100));
    public static readonly DishData SpinachDish = Dish("RAU", DishType.Vegetable, CookingMethod.Boil, 1, (ISpinach, 200));

    public static MealTarget Target(decimal energy = 910, IReadOnlyList<NutrientTarget>? nutrients = null, decimal? budget = 35000)
        => new(energy, 13, 20, 20, 25, 55, 65, 30, 50, nutrients ?? [], budget);

    public static NutritionSummary Summary(decimal energyTable, decimal p, decimal l, decimal g, decimal animalP = 0, params (string, decimal)[] extra)
    {
        var totals = new Dictionary<string, decimal> { ["ENERGY"] = energyTable, ["PROTEIN"] = p, ["LIPID"] = l, ["GLUCID"] = g };
        foreach (var (k, v) in extra) totals[k] = v;
        var dish = new DishNutrition(Guid.NewGuid(), "X", "X", DishType.Other, 1, 0, 0, totals, animalP, [], []);
        return NutritionEngine.Summarize([dish]);
    }
}
