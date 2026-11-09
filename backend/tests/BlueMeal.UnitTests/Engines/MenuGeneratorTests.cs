using BlueMeal.Application.Engines;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Enums;

namespace BlueMeal.UnitTests.Engines;

/// <summary>SPEC §6.4 — automatic menu generation.</summary>
public class MenuGeneratorTests
{
    private static readonly StructureSlot[] Standard =
    [
        new(DishType.MainProtein, 1, true), new(DishType.StirFry, 1, true), new(DishType.Soup, 1, true),
        new(DishType.Vegetable, 1, true), new(DishType.Staple, 1, true), new(DishType.Dessert, 1, false),
    ];

    /// <summary>Synthetic candidates with realistic per-serving values (deterministic from the index).</summary>
    private static List<DishCandidate> Candidates(int perType = 8, string? allergenOnFirstMain = null)
    {
        var list = new List<DishCandidate>();
        var methods = new[] { CookingMethod.Boil, CookingMethod.StirFry, CookingMethod.DeepFry, CookingMethod.Braise, CookingMethod.Steam, CookingMethod.Simmer };
        foreach (var type in new[] { DishType.MainProtein, DishType.StirFry, DishType.Soup, DishType.Vegetable, DishType.Staple, DishType.Dessert })
        {
            for (var i = 0; i < perType; i++)
            {
                var (e, p, l, g, animal, cost) = type switch
                {
                    DishType.MainProtein => (220m + i * 10, 18m + i % 3, 14m + i % 4, 3m, 16m, 14000m + i * 800),
                    DishType.StirFry => (120m + i * 5, 7m, 7m + i % 3, 8m, 3m, 6000m + i * 300),
                    DishType.Soup => (45m + i * 3, 3m, 1m, 6m, 1m, 2500m + i * 200),
                    DishType.Vegetable => (40m, 3m, 1m + i % 2, 4m, 0m, 2000m + i * 100),
                    DishType.Staple => (440m + i * 4, 10m, 1.3m, 98m, 0m, 2400m),
                    _ => (90m, 1m, 0.2m, 21m, 0m, 2500m),
                };
                var method = type is DishType.Staple or DishType.Dessert ? CookingMethod.None : methods[(i + (int)type) % methods.Length];
                var allergens = type == DishType.MainProtein && i == 0 ? allergenOnFirstMain : null;
                var dish = new DishData(Guid.NewGuid(), $"{type}-{i}", $"{type} {i}", type, method, 1,
                    [new RecipeLine(F.Ing("X", F.Rice, 1000, 1000), 1)], allergens);
                list.Add(new DishCandidate(dish, new Dictionary<string, decimal> { ["ENERGY"] = e, ["PROTEIN"] = p, ["LIPID"] = l, ["GLUCID"] = g, ["NA"] = 150 }, animal, cost, false));
            }
        }
        return list;
    }

    private static GeneratorRequest Request(List<DishCandidate> c, int days = 5, int seed = 7, int noRepeat = 3, decimal? budget = 40000,
        HashSet<Guid>? required = null, HashSet<Guid>? excluded = null, string[]? allergens = null)
        => new(Enumerable.Range(0, days).Select(i => new DateOnly(2026, 3, 2).AddDays(i)).ToList(), Standard, F.Target(910, budget: budget), c, noRepeat,
            required ?? [], excluded ?? [], allergens ?? [], false, 0.2m, seed, new Dictionary<DateOnly, IReadOnlyCollection<Guid>>());

    private static readonly MenuGenerator Gen = new(new GeneratorSettings());

    [Fact]
    public void Same_seed_gives_same_menus()
    {
        var c = Candidates();
        var a = Gen.Generate(Request(c, seed: 42));
        var b = Gen.Generate(Request(c, seed: 42));
        a.Days.Select(d => string.Join(',', d.Best.Dishes.Select(x => x.Dish.Code)))
            .Should().Equal(b.Days.Select(d => string.Join(',', d.Best.Dishes.Select(x => x.Dish.Code))));
    }

    [Fact]
    public void Every_day_follows_the_meal_structure()
    {
        var r = Gen.Generate(Request(Candidates()));
        r.Days.Should().HaveCount(5);
        foreach (var d in r.Days)
            d.Best.Dishes.Select(x => x.Dish.Type).Should().BeEquivalentTo(Standard.Select(s => s.Type));
    }

    [Fact]
    public void No_dish_repeats_within_the_no_repeat_window()
    {
        var r = Gen.Generate(Request(Candidates(), days: 10, noRepeat: 3));
        for (var i = 0; i < r.Days.Count; i++)
            for (var j = Math.Max(0, i - 3); j < i; j++)
                r.Days[i].Best.Dishes.Select(x => x.Dish.DishId).Intersect(r.Days[j].Best.Dishes.Select(x => x.Dish.DishId))
                    .Should().BeEmpty($"day {i} vs day {j}");
    }

    [Fact]
    public void At_most_one_fried_dish_per_meal()
    {
        var r = Gen.Generate(Request(Candidates(), days: 10));
        r.Days.Should().OnlyContain(d => d.Best.Dishes.Count(x => x.Dish.Method == CookingMethod.DeepFry) <= 1);
    }

    [Fact]
    public void Budget_is_respected_when_feasible()
    {
        var r = Gen.Generate(Request(Candidates(), days: 5, budget: 35000));
        r.Days.Should().OnlyContain(d => d.Best.CostPerServing <= 35000);
    }

    [Fact]
    public void Energy_close_to_target()
    {
        var r = Gen.Generate(Request(Candidates()));
        r.Days.Should().OnlyContain(d => Math.Abs(d.Best.EnergyKcal - 910) / 910 < 0.15m);
    }

    [Fact]
    public void Excluded_and_allergen_dishes_are_never_used_and_required_dish_is_placed()
    {
        var c = Candidates(allergenOnFirstMain: "SHELLFISH");
        var allergenDish = c.First(x => x.Dish.Allergens == "SHELLFISH").Dish.DishId;
        var excluded = c.First(x => x.Dish.Type == DishType.Soup).Dish.DishId;
        var required = c.Last(x => x.Dish.Type == DishType.MainProtein).Dish.DishId;
        var r = Gen.Generate(Request(c, days: 7, required: [required], excluded: [excluded], allergens: ["shellfish"]));
        var used = r.Days.SelectMany(d => d.Best.Dishes.Select(x => x.Dish.DishId)).ToList();
        used.Should().NotContain(allergenDish).And.NotContain(excluded).And.Contain(required);
        r.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Alternatives_and_bilingual_explanation_are_returned()
    {
        var d = Gen.Generate(Request(Candidates(), days: 1)).Days.Single();
        d.Alternatives.Should().HaveCount(3);
        d.Alternatives.Should().OnlyContain(a => a.Score.Total >= d.Best.Score.Total);
        d.ExplanationVi.Should().Contain("kcal").And.Contain("P:L:G");
        d.ExplanationEn.Should().Contain("Energy");
    }

    [Fact]
    public void Missing_slot_candidates_are_reported()
    {
        var c = Candidates().Where(x => x.Dish.Type != DishType.Soup).ToList();
        var r = Gen.Generate(Request(c, days: 1));
        r.Problems.Should().Contain(p => p.StartsWith("menu.noCandidates|Soup"));
    }

    [Fact]
    public void Swap_suggestions_improve_the_score()
    {
        var c = Candidates();
        var day = Gen.Generate(Request(c, days: 1)).Days.Single();
        // force a bad staple (huge energy) and ask for alternatives for that slot
        var bad = day.Best.Dishes.ToArray();
        var idx = Array.FindIndex(bad, x => x.Dish.Type == DishType.Staple);
        bad[idx] = bad[idx] with { Nutrients = new Dictionary<string, decimal> { ["ENERGY"] = 1500, ["PROTEIN"] = 10, ["LIPID"] = 40, ["GLUCID"] = 200 } };
        var pool = c.Where(x => x.Dish.Type == DishType.Staple).ToList();
        var s = Gen.SwapSuggestions(bad, idx, pool, F.Target(910), new HashSet<Guid>());
        s.Should().NotBeEmpty();
        s.Should().BeInAscendingOrder(x => x.Score.Total);
        s.Count.Should().BeLessThanOrEqualTo(5);
    }

    [Fact] // SPEC §6.4: one month (≈ 26 days × 1 shift) under 5 s with 500 dishes
    public void Performance_one_month_with_500_dishes_under_5_seconds()
    {
        var c = Candidates(perType: 84); // 6 types × 84 ≈ 504 dishes
        var r = Gen.Generate(Request(c, days: 26, noRepeat: 7));
        r.Days.Should().HaveCount(26);
        r.ElapsedMs.Should().BeLessThan(5000);
    }
}
