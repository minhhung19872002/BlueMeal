using System.Diagnostics;
using System.Globalization;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Enums;

namespace BlueMeal.Application.Engines;

/// <summary>A dish that may be placed in a slot, with precomputed per-serving nutrition and cost.</summary>
public sealed record DishCandidate(
    DishData Dish,
    IReadOnlyDictionary<string, decimal> Nutrients,
    decimal AnimalProteinG,
    decimal Cost,
    bool MissingData,
    bool UsesExpiringStock = false,
    int? DaysSinceLastUse = null);

public sealed record GeneratorRequest(
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<StructureSlot> Structure,
    MealTarget Target,
    IReadOnlyList<DishCandidate> Candidates,
    int NoRepeatDays,
    IReadOnlySet<Guid> RequiredDishIds,
    IReadOnlySet<Guid> ExcludedDishIds,
    IReadOnlyCollection<string> AllergensToAvoid,
    bool PreferExpiringStock,
    decimal NewDishRatio,
    int Seed,
    /// <summary>Dishes used on days before the first generated day: date → dish ids (repeat constraint across periods).</summary>
    IReadOnlyDictionary<DateOnly, IReadOnlyCollection<Guid>> History,
    int Alternatives = 3);

public sealed record ScoreBreakdown(decimal Total, decimal Energy, decimal Plg, decimal AnimalProtein, decimal Micro, decimal Cost, decimal Repeat, decimal CookingMethod);

public sealed record MenuProposal(IReadOnlyList<DishCandidate> Dishes, ScoreBreakdown Score, decimal EnergyKcal, decimal CostPerServing, decimal ProteinPct, decimal LipidPct, decimal GlucidPct, decimal AnimalProteinPct);

public sealed record DayResult(DateOnly Date, MenuProposal Best, IReadOnlyList<MenuProposal> Alternatives, string ExplanationVi, string ExplanationEn);

public sealed record GeneratorResult(IReadOnlyList<DayResult> Days, IReadOnlyList<string> Problems, long ElapsedMs, int Seed);

/// <summary>
/// Automatic menu generation (SPEC §6.4). Deterministic for a given seed.
/// <list type="number">
/// <item>Filter candidates per slot: active, matching type, has a current recipe, no forbidden allergen, no missing nutrition data.</item>
/// <item>For each day, draw K weighted random combinations (prefer long-unused dishes, dishes using expiring stock, varied cooking methods, ≤ 1 fried dish).</item>
/// <item>score = w1·|E−E*|/E* + w2·Σdev(%P,%L,%G) + w3·dev(animal) + w4·Σmicro + w5·max(0,cost−budget)/budget + w6·repeat + w7·same-method.</item>
/// <item>Local search (swap one dish of one slot, ≤ N iterations) on the top 10.</item>
/// <item>Return the best and 3 alternatives; days are generated sequentially so the no-repeat rule spans days.</item>
/// </list>
/// </summary>
public sealed class MenuGenerator(GeneratorSettings settings)
{
    private const string P = NutrientCodes.Protein, Lp = NutrientCodes.Lipid, G = NutrientCodes.Glucid, E = NutrientCodes.Energy;

    public GeneratorResult Generate(GeneratorRequest req)
    {
        var sw = Stopwatch.StartNew();
        var rnd = new Random(req.Seed);
        var problems = new List<string>();
        var avoid = req.AllergensToAvoid.Select(a => a.Trim().ToUpperInvariant()).Where(a => a.Length > 0).ToHashSet();

        // 1) eligible candidates per slot type
        var eligible = req.Candidates
            .Where(c => !req.ExcludedDishIds.Contains(c.Dish.DishId) && !c.MissingData && c.Dish.Lines.Count > 0)
            .Where(c => avoid.Count == 0 || !WarningEngine.Split(c.Dish.Allergens).Concat(c.Dish.Lines.SelectMany(l => WarningEngine.Split(l.Ingredient.Allergens))).Any(avoid.Contains))
            .GroupBy(c => c.Dish.Type).ToDictionary(g => g.Key, g => g.ToList());
        var slots = req.Structure.SelectMany(s => Enumerable.Repeat(s, Math.Max(1, s.Quantity))).ToList();
        foreach (var s in slots.Where(s => !eligible.ContainsKey(s.Type) || eligible[s.Type].Count == 0).DistinctBy(s => s.Type))
            problems.Add($"menu.noCandidates|{s.Type}");

        var usedOn = req.History.ToDictionary(k => k.Key, v => v.Value.ToHashSet());
        var pendingRequired = req.RequiredDishIds.ToHashSet();
        var days = new List<DayResult>();

        foreach (var day in req.Days.OrderBy(d => d))
        {
            var recent = usedOn.Where(k => k.Key < day && k.Key >= day.AddDays(-req.NoRepeatDays)).SelectMany(k => k.Value).ToHashSet();
            // required dishes still to place: fix the first that fits a slot
            var fixedDishes = new Dictionary<int, DishCandidate>();
            foreach (var reqId in pendingRequired.ToList())
            {
                var cand = req.Candidates.FirstOrDefault(c => c.Dish.DishId == reqId);
                if (cand is null) continue;
                var idx = slots.FindIndex(s => s.Type == cand.Dish.Type);
                if (idx < 0 || fixedDishes.ContainsKey(idx) || recent.Contains(reqId)) continue;
                fixedDishes[idx] = cand;
                pendingRequired.Remove(reqId);
            }

            var pools = slots.Select((s, i) =>
            {
                if (fixedDishes.TryGetValue(i, out var f)) return new List<DishCandidate> { f };
                var all = eligible.GetValueOrDefault(s.Type) ?? [];
                var fresh = all.Where(c => !recent.Contains(c.Dish.DishId)).ToList();
                return fresh.Count > 0 ? fresh : all; // fall back (repeat penalty applies) when the pool is exhausted
            }).ToList();

            if (pools.Any(p => p.Count == 0))
            {
                // keep going with the slots that can be filled; structure warning will flag the gap
                pools = pools.Where(p => p.Count > 0).ToList();
            }
            if (pools.Count == 0) { problems.Add($"menu.noCandidates|{day:yyyy-MM-dd}"); continue; }

            var weights = pools.Select(p => p.Select(c => Weight(c, req)).ToArray()).ToList();

            // 2) K weighted random combinations
            var seen = new HashSet<string>();
            var combos = new List<(DishCandidate[] Dishes, ScoreBreakdown Score)>();
            for (var k = 0; k < Math.Max(1, settings.Candidates); k++)
            {
                var combo = new DishCandidate[pools.Count];
                for (var i = 0; i < pools.Count; i++) combo[i] = Pick(pools[i], weights[i], rnd, combo, i);
                var key = Key(combo);
                if (!seen.Add(key)) continue;
                combos.Add((combo, Score(combo, req.Target, recent)));
            }

            // 3–4) local search on the best ones
            var top = combos.OrderBy(c => c.Score.Total).Take(Math.Max(1, settings.TopForLocalSearch)).ToList();
            var improved = new List<(DishCandidate[] Dishes, ScoreBreakdown Score)>();
            foreach (var start in top)
            {
                var cur = (DishCandidate[])start.Dishes.Clone();
                var curScore = start.Score;
                for (var it = 0; it < settings.MaxLocalSearchIterations; it++)
                {
                    var slot = rnd.Next(pools.Count);
                    if (pools[slot].Count <= 1) continue;
                    var candidate = pools[slot][rnd.Next(pools[slot].Count)];
                    if (candidate == cur[slot] || cur.Where((_, j) => j != slot).Any(d => d.Dish.DishId == candidate.Dish.DishId)) continue;
                    var trial = (DishCandidate[])cur.Clone();
                    trial[slot] = candidate;
                    var s = Score(trial, req.Target, recent);
                    if (s.Total < curScore.Total) { cur = trial; curScore = s; }
                }
                improved.Add((cur, curScore));
            }

            var ranked = improved.Concat(combos).GroupBy(c => Key(c.Dishes)).Select(g => g.OrderBy(x => x.Score.Total).First())
                .OrderBy(c => c.Score.Total).ToList();
            var best = ranked[0];
            var alternatives = ranked.Skip(1).Take(req.Alternatives).Select(c => Proposal(c.Dishes, c.Score)).ToList();
            var bestProposal = Proposal(best.Dishes, best.Score);
            var (vi, en) = Explain(bestProposal, req.Target, recent);
            days.Add(new DayResult(day, bestProposal, alternatives, vi, en));
            usedOn[day] = best.Dishes.Select(d => d.Dish.DishId).ToHashSet();
        }

        foreach (var r in pendingRequired) problems.Add($"menu.requiredNotPlaced|{r}");
        sw.Stop();
        return new GeneratorResult(days, problems, sw.ElapsedMilliseconds, req.Seed);
    }

    /// <summary>Top N alternatives for one slot of an existing menu that lower the score (SPEC §7.4.3 "đổi món").</summary>
    public IReadOnlyList<(DishCandidate Dish, ScoreBreakdown Score)> SwapSuggestions(IReadOnlyList<DishCandidate> current, int slotIndex, IReadOnlyList<DishCandidate> pool,
        MealTarget target, IReadOnlySet<Guid> recent, int take = 5)
    {
        var baseScore = Score(current.ToArray(), target, recent);
        var others = current.Where((_, i) => i != slotIndex).Select(d => d.Dish.DishId).ToHashSet();
        return pool.Where(c => c.Dish.DishId != current[slotIndex].Dish.DishId && !others.Contains(c.Dish.DishId) && !c.MissingData)
            .Select(c =>
            {
                var trial = current.ToArray();
                trial[slotIndex] = c;
                return (c, Score(trial, target, recent));
            })
            .Where(x => x.Item2.Total < baseScore.Total)
            .OrderBy(x => x.Item2.Total).Take(take).ToList();
    }

    // ------------------------------------------------------------------ scoring

    public ScoreBreakdown Score(IReadOnlyList<DishCandidate> combo, MealTarget t, IReadOnlySet<Guid> recent)
    {
        decimal Sum(string code) => combo.Sum(d => d.Nutrients.GetValueOrDefault(code));
        var p = Sum(P); var l = Sum(Lp); var g = Sum(G);
        var energyTable = Sum(E);
        var eMacro = p * 4 + l * 9 + g * 4;
        var energy = energyTable > 0 ? energyTable : eMacro;
        var pctP = eMacro == 0 ? 0 : p * 4 / eMacro * 100;
        var pctL = eMacro == 0 ? 0 : l * 9 / eMacro * 100;
        var pctG = eMacro == 0 ? 0 : g * 4 / eMacro * 100;
        var animal = p == 0 ? 0 : combo.Sum(d => d.AnimalProteinG) / p * 100;
        var cost = combo.Sum(d => d.Cost);

        var sEnergy = t.EnergyKcal <= 0 ? 0 : Math.Abs(energy - t.EnergyKcal) / t.EnergyKcal;
        var sPlg = (NutritionEngine.Deviation(pctP, t.ProteinPctMin, t.ProteinPctMax) + NutritionEngine.Deviation(pctL, t.LipidPctMin, t.LipidPctMax)
                    + NutritionEngine.Deviation(pctG, t.GlucidPctMin, t.GlucidPctMax)) / 100m;
        var sAnimal = NutritionEngine.Deviation(animal, t.AnimalProteinPctMin, t.AnimalProteinPctMax) / 100m;
        var sMicro = 0m;
        foreach (var nt in t.Nutrients)
        {
            var v = Sum(nt.Code);
            if (nt.Min is { } mn && mn > 0 && v < mn) sMicro += (mn - v) / mn;
            if (nt.Max is { } mx && mx > 0 && v > mx) sMicro += (v - mx) / mx;
        }
        var sCost = t.BudgetPerServing is { } b && b > 0 ? Math.Max(0, cost - b) / b : 0;
        var sRepeat = (decimal)combo.Count(d => recent.Contains(d.Dish.DishId));
        var methods = combo.Where(d => d.Dish.Method is not (CookingMethod.None or CookingMethod.Raw or CookingMethod.Other)).GroupBy(d => d.Dish.Method);
        var sMethod = (decimal)methods.Sum(g2 => g2.Count() - 1);
        var fried = combo.Count(d => d.Dish.Method == CookingMethod.DeepFry);
        if (fried > settings.MaxFriedPerMeal) sMethod += 10 * (fried - settings.MaxFriedPerMeal); // hard-ish constraint

        var total = settings.W1Energy * sEnergy + settings.W2Plg * sPlg + settings.W3AnimalProtein * sAnimal + settings.W4Micro * sMicro
                    + settings.W5Cost * sCost + settings.W6Repeat * sRepeat + settings.W7CookingMethod * sMethod;
        return new ScoreBreakdown(total, sEnergy, sPlg, sAnimal, sMicro, sCost, sRepeat, sMethod);
    }

    private decimal Weight(DishCandidate c, GeneratorRequest req)
    {
        var w = 1m;
        // prefer dishes not used for a long time (or never)
        w += c.DaysSinceLastUse is null ? 1m + req.NewDishRatio * 2m : Math.Min(1m, c.DaysSinceLastUse.Value / 30m);
        if (req.PreferExpiringStock && c.UsesExpiringStock) w += 1.5m;
        return w;
    }

    private DishCandidate Pick(List<DishCandidate> pool, decimal[] weights, Random rnd, DishCandidate[] partial, int index)
    {
        // diversity: down-weight already used cooking methods and a second fried dish
        var adjusted = new decimal[pool.Count];
        var friedSoFar = partial.Take(index).Count(d => d is not null && d.Dish.Method == CookingMethod.DeepFry);
        var methods = partial.Take(index).Where(d => d is not null).Select(d => d.Dish.Method).ToHashSet();
        var ids = partial.Take(index).Where(d => d is not null).Select(d => d.Dish.DishId).ToHashSet();
        for (var i = 0; i < pool.Count; i++)
        {
            var w = weights[i];
            if (ids.Contains(pool[i].Dish.DishId)) w = 0;
            else if (pool[i].Dish.Method == CookingMethod.DeepFry && friedSoFar >= settings.MaxFriedPerMeal) w *= 0.05m;
            else if (methods.Contains(pool[i].Dish.Method)) w *= 0.5m;
            adjusted[i] = w;
        }
        var total = adjusted.Sum();
        if (total <= 0) return pool[rnd.Next(pool.Count)];
        var r = (decimal)rnd.NextDouble() * total;
        for (var i = 0; i < pool.Count; i++)
        {
            r -= adjusted[i];
            if (r <= 0) return pool[i];
        }
        return pool[^1];
    }

    private static string Key(IEnumerable<DishCandidate> combo) => string.Join(',', combo.Select(d => d.Dish.DishId).OrderBy(x => x));

    private MenuProposal Proposal(DishCandidate[] dishes, ScoreBreakdown score)
    {
        decimal Sum(string code) => dishes.Sum(d => d.Nutrients.GetValueOrDefault(code));
        var p = Sum(P); var l = Sum(Lp); var g = Sum(G);
        var eMacro = p * 4 + l * 9 + g * 4;
        var e = Sum(E) > 0 ? Sum(E) : eMacro;
        return new MenuProposal(dishes, score, e, dishes.Sum(d => d.Cost),
            eMacro == 0 ? 0 : p * 4 / eMacro * 100, eMacro == 0 ? 0 : l * 9 / eMacro * 100, eMacro == 0 ? 0 : g * 4 / eMacro * 100,
            p == 0 ? 0 : dishes.Sum(d => d.AnimalProteinG) / p * 100);
    }

    private static readonly CultureInfo Vi = new("vi-VN");
    private static readonly CultureInfo En = new("en-US");

    /// <summary>Short bilingual explanation of why the combination was chosen.</summary>
    public static (string Vi, string En) Explain(MenuProposal p, MealTarget t, IReadOnlySet<Guid> recent)
    {
        var pct = t.EnergyKcal > 0 ? p.EnergyKcal / t.EnergyKcal * 100 : 0;
        var vi = new List<string> { $"Năng lượng {p.EnergyKcal.ToString("N0", Vi)} kcal ({pct.ToString("N0", Vi)}% mục tiêu)" };
        var en = new List<string> { $"Energy {p.EnergyKcal.ToString("N0", En)} kcal ({pct.ToString("N0", En)}% of target)" };
        vi.Add($"P:L:G = {p.ProteinPct.ToString("N0", Vi)}:{p.LipidPct.ToString("N0", Vi)}:{p.GlucidPct.ToString("N0", Vi)}");
        en.Add($"P:L:G = {p.ProteinPct.ToString("N0", En)}:{p.LipidPct.ToString("N0", En)}:{p.GlucidPct.ToString("N0", En)}");
        vi.Add($"đạm động vật {p.AnimalProteinPct.ToString("N0", Vi)}%");
        en.Add($"animal protein {p.AnimalProteinPct.ToString("N0", En)}%");
        if (t.BudgetPerServing is { } b)
        {
            vi.Add(p.CostPerServing <= b ? $"chi phí thực phẩm {p.CostPerServing.ToString("N0", Vi)} ₫ trong ngân sách" : $"chi phí {p.CostPerServing.ToString("N0", Vi)} ₫ vượt ngân sách {b.ToString("N0", Vi)} ₫");
            en.Add(p.CostPerServing <= b ? $"food cost {p.CostPerServing.ToString("N0", En)} VND within budget" : $"cost {p.CostPerServing.ToString("N0", En)} VND over budget {b.ToString("N0", En)} VND");
        }
        var methods = p.Dishes.Select(d => d.Dish.Method).Distinct().Count();
        vi.Add($"{methods} phương pháp chế biến khác nhau");
        en.Add($"{methods} different cooking methods");
        if (p.Dishes.Any(d => recent.Contains(d.Dish.DishId))) { vi.Add("có món lặp lại do thiếu lựa chọn"); en.Add("some dishes repeat due to limited choice"); }
        if (p.Dishes.Any(d => d.UsesExpiringStock)) { vi.Add("ưu tiên dùng nguyên liệu tồn kho sắp hết hạn"); en.Add("uses stock close to expiry"); }
        return (string.Join("; ", vi) + ".", string.Join("; ", en) + ".");
    }
}
