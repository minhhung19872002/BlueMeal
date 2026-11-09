using BlueMeal.Domain.Enums;

namespace BlueMeal.Application.Engines;

/// <summary>
/// Meal cost calculation (SPEC §6.3).
/// <code>
/// ingredient_cost = gross_g / grams_per_purchase_unit × unit_price
/// dish_cost       = Σ ingredient_cost / base_servings × serving_factor
/// menu_food_cost  = Σ dish_cost            (per serving)
/// menu_cost/suất  = menu_food_cost + overhead (each item: % of food cost or fixed per serving)
/// </code>
/// Unit prices are resolved by the caller according to the configured price mode (reference, last receipt,
/// weighted average, contract).
/// </summary>
public static class CostEngine
{
    public static IngredientCost IngredientCost(RecipeLine line, decimal scale)
    {
        var ing = line.Ingredient;
        var gross = line.GrossG * scale;
        var missing = ing.UnitPrice is null || ing.GramsPerUnit <= 0;
        var cost = missing ? 0m : gross / ing.GramsPerUnit * ing.UnitPrice!.Value;
        return new IngredientCost(ing.Id, ing.Code, ing.Name, gross, ing.UnitPrice, cost, missing, ing.Food?.GroupCode, ing.Food?.GroupName);
    }

    /// <summary>Cost of ONE serving of the dish multiplied by the serving factor.</summary>
    public static DishCost DishCost(DishData dish, decimal servingFactor = 1m)
    {
        if (dish.BaseServings <= 0) throw new ArgumentException("BaseServings must be > 0", nameof(dish));
        var scale = servingFactor / dish.BaseServings;
        var items = dish.Lines.Select(l => IngredientCost(l, scale)).ToList();
        return new DishCost(dish.DishId, dish.Code, dish.Name, servingFactor, items.Sum(i => i.Cost), items);
    }

    public static IReadOnlyList<OverheadCost> Overheads(decimal foodCostPerServing, IEnumerable<OverheadRule> rules)
        => rules.Select(r => new OverheadCost(r.Code, r.NameVi, r.NameEn,
            r.Mode == OverheadMode.PercentOfFood ? foodCostPerServing * r.Value / 100m : r.Value)).ToList();

    public static CostSummary ComputeMenu(IEnumerable<MenuDish> dishes, IEnumerable<OverheadRule> overheads, decimal servings = 1m, decimal? budget = null, decimal? agreedPrice = null)
    {
        var dishCosts = dishes.Select(d => DishCost(d.Dish, d.ServingFactor)).ToList();
        var food = dishCosts.Sum(d => d.Cost);
        var oh = Overheads(food, overheads);
        var overhead = oh.Sum(o => o.Amount);
        var perServing = food + overhead;
        var byGroup = dishCosts.SelectMany(d => d.Ingredients)
            .GroupBy(i => i.GroupCode ?? "OTHER")
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cost));
        return new CostSummary(food, overhead, perServing, servings, perServing * servings, dishCosts, oh, byGroup,
            dishCosts.SelectMany(d => d.Ingredients).Where(i => i.MissingPrice).Select(i => i.Code).Distinct().ToList(),
            budget, budget is null ? null : perServing - budget, agreedPrice, agreedPrice is null ? null : agreedPrice - perServing);
    }

    /// <summary>Ingredient demand (gross grams and purchase units) for a number of servings — used by issue/purchase generation.</summary>
    public static IReadOnlyList<(IngredientData Ingredient, decimal GrossG, decimal PurchaseUnits)> Demand(IEnumerable<MenuDish> dishes, decimal servings)
        => dishes.SelectMany(d => d.Dish.Lines.Select(l => (l.Ingredient, G: l.GrossG / d.Dish.BaseServings * d.ServingFactor * servings)))
            .GroupBy(x => x.Ingredient.Id)
            .Select(g =>
            {
                var ing = g.First().Ingredient;
                var grams = g.Sum(x => x.G);
                return (ing, grams, ing.GramsPerUnit > 0 ? grams / ing.GramsPerUnit : 0m);
            })
            .ToList();
}
