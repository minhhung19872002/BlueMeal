using BlueMeal.Api.Reports;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Application.Engines;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Services;

public sealed record AnalysisRequest(string Scope, Guid? DishId, Guid? MenuId, DateOnly? From, DateOnly? To, Guid? CustomerId, Guid? SubjectGroupId, Guid? MealShiftId, Guid? RecommendationId);

public sealed record NutrientAnalysis(string Code, string NameVi, string? NameEn, string Unit, string Group, int Decimals, decimal Value, decimal? Min, decimal? Max, decimal? Target, decimal? Pct, bool BelowMin, bool AboveMax);
public sealed record TrendPoint(DateOnly Date, decimal Energy, decimal? Target, decimal? Pct, int Menus);
public sealed record PeriodAverage(string Period, DateOnly From, decimal Energy, decimal? Target, decimal ProteinPct, decimal LipidPct, decimal GlucidPct, int Menus);
public sealed record WarningCount(string Code, string Level, int Count, string MessageVi, string MessageEn);

public sealed record AnalysisResult(
    string Scope, string Title, int MenuCount, decimal EnergyKcal, decimal? TargetEnergyKcal, decimal? EnergyPct,
    decimal ProteinPct, decimal LipidPct, decimal GlucidPct, decimal AnimalProteinPct, decimal ProteinG, decimal LipidG, decimal GlucidG,
    IReadOnlyList<NutrientAnalysis> Nutrients, IReadOnlyList<TrendPoint> Trend, IReadOnlyList<PeriodAverage> Weekly, IReadOnlyList<PeriodAverage> Monthly, IReadOnlyList<WarningCount> Warnings);

/// <summary>Nutrition analysis for one dish, one menu or a date range (SPEC §7.5), dashboard figures (SPEC §7.9).</summary>
public sealed class AnalysisService(AppDbContext db, MenuService menus, ReportContext ctx, ICurrentUser user, ISettingsService settings, TimeProvider clock)
{
    private async Task<List<(string Code, string NameVi, string? NameEn, string Unit, NutrientGroup Group, int Decimals)>> NutrientMetaAsync(CancellationToken ct)
        => (await db.Set<Nutrient>().AsNoTracking().Where(n => n.IsActive).OrderBy(n => n.Group).ThenBy(n => n.SortOrder).ThenBy(n => n.Code)
            .Select(n => new { n.Code, n.NameVi, n.NameEn, n.Unit, n.Group, n.Decimals }).ToListAsync(ct))
            .Select(n => (n.Code, n.NameVi, n.NameEn, n.Unit, n.Group, n.Decimals)).ToList();

    public async Task<AnalysisResult> NutritionAsync(AnalysisRequest r, CancellationToken ct)
    {
        var meta = await NutrientMetaAsync(ct);
        switch (r.Scope)
        {
            case "dish":
            {
                var dish = await db.Set<Dish>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == r.DishId, ct) ?? throw new NotFoundException("dish");
                var snap = await menus.ComputeAsync(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), r.SubjectGroupId, r.CustomerId, r.RecommendationId, null, 1, null, [(dish.Id, 1m, null)], null, ct);
                return Single("dish", $"{dish.Code} — {dish.NameVi}", snap with { Warnings = snap.Warnings.Where(w => w.Code != WarningCodes.Structure).ToList() }, meta);
            }
            case "menu":
            {
                var m = await db.Set<Menu>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.MenuId, ct) ?? throw new NotFoundException("menu");
                if (user.CustomerScope is { } cs && (m.CustomerId is null || !cs.Contains(m.CustomerId.Value))) throw new NotFoundException("menu");
                var snap = m.Snapshot is null ? null : System.Text.Json.JsonSerializer.Deserialize<MenuSnapshot>(m.Snapshot, MenuService.Json);
                if (snap is null) throw new BusinessRuleException("menu.noSnapshot");
                return Single("menu", $"{m.Code} — {m.MenuDate:dd/MM/yyyy}", snap, meta);
            }
            default:
                return await RangeAsync(r, meta, ct);
        }
    }

    private static AnalysisResult Single(string scope, string title, MenuSnapshot s, List<(string Code, string NameVi, string? NameEn, string Unit, NutrientGroup Group, int Decimals)> meta)
    {
        var n = s.Nutrition;
        var compliance = (n.Compliance ?? []).ToDictionary(c => c.Code);
        var nutrients = meta.Where(m => n.Totals.ContainsKey(m.Code) || compliance.ContainsKey(m.Code)).Select(m =>
        {
            var c = compliance.GetValueOrDefault(m.Code);
            var value = m.Code == NutrientCodes.Energy ? n.EnergyKcal : n.Totals.GetValueOrDefault(m.Code);
            return new NutrientAnalysis(m.Code, m.NameVi, m.NameEn, m.Unit, m.Group.ToString(), m.Decimals, Math.Round(value, m.Decimals), c?.Min, c?.Max, c?.Target, c?.Pct is { } p ? Math.Round(p, 1) : null,
                c?.BelowMin ?? false, c?.AboveMax ?? false);
        }).ToList();
        var target = s.TargetEnergyKcal;
        return new AnalysisResult(scope, title, 1, Math.Round(n.EnergyKcal, 0), target, target is > 0 ? Math.Round(n.EnergyKcal / target.Value * 100, 1) : null,
            Math.Round(n.ProteinPct, 1), Math.Round(n.LipidPct, 1), Math.Round(n.GlucidPct, 1), Math.Round(n.AnimalProteinPct, 1), Math.Round(n.ProteinG, 1), Math.Round(n.LipidG, 1), Math.Round(n.GlucidG, 1),
            nutrients, [], [], [], s.Warnings.Select(w => new WarningCount(w.Code, w.Level.ToString(), 1, w.MessageVi, w.MessageEn)).ToList());
    }

    private async Task<AnalysisResult> RangeAsync(AnalysisRequest r, List<(string Code, string NameVi, string? NameEn, string Unit, NutrientGroup Group, int Decimals)> meta, CancellationToken ct)
    {
        var to = r.To ?? ctx.Today;
        var from = r.From ?? to.AddDays(-6);
        if (to < from || to.DayNumber - from.DayNumber > 400) throw new BusinessRuleException("report.rangeTooLong", 400);
        var list = (await ctx.MenusAsync(from, to, new ReportFilter { CustomerId = r.CustomerId, SubjectGroupId = r.SubjectGroupId, MealShiftId = r.MealShiftId }, ct))
            .Where(m => m.Snapshot is not null).ToList();
        if (list.Count == 0)
            return new AnalysisResult("range", $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}", 0, 0, null, null, 0, 0, 0, 0, 0, 0, 0, [], [], [], [], []);
        decimal Avg(Func<ReportContext.MenuWithSnapshot, decimal> f) => list.Average(f);
        var compliance = list.SelectMany(m => m.Snapshot!.Nutrition.Compliance ?? []).GroupBy(c => c.Code).ToDictionary(g => g.Key, g => g.ToList());
        var nutrients = meta.Where(m => compliance.ContainsKey(m.Code) || list.Any(x => x.Snapshot!.Nutrition.Totals.ContainsKey(m.Code))).Select(m =>
        {
            var value = m.Code == NutrientCodes.Energy ? Avg(x => x.Snapshot!.Nutrition.EnergyKcal) : Avg(x => x.Snapshot!.Nutrition.Totals.GetValueOrDefault(m.Code));
            var cs = compliance.GetValueOrDefault(m.Code) ?? [];
            decimal? AvgN(Func<ComplianceItem, decimal?> f) { var vals = cs.Select(f).Where(v => v != null).Select(v => v!.Value).ToList(); return vals.Count > 0 ? vals.Average() : null; }
            var min = AvgN(c => c.Min);
            var max = AvgN(c => c.Max);
            var target = AvgN(c => c.Target);
            return new NutrientAnalysis(m.Code, m.NameVi, m.NameEn, m.Unit, m.Group.ToString(), m.Decimals, Math.Round(value, m.Decimals), min, max, target,
                target is > 0 ? Math.Round(value / target.Value * 100, 1) : null, min is { } mn && value < mn, max is { } mx && value > mx);
        }).ToList();
        var targets = list.Where(m => m.Snapshot!.TargetEnergyKcal > 0).ToList();
        decimal? targetAvg = targets.Count > 0 ? targets.Average(m => m.Snapshot!.TargetEnergyKcal!.Value) : null;
        var energy = Avg(x => x.Snapshot!.Nutrition.EnergyKcal);
        var trend = list.GroupBy(m => m.MenuDate).OrderBy(g => g.Key).Select(g =>
        {
            var e = g.Average(m => m.Snapshot!.Nutrition.EnergyKcal);
            var tg = g.Where(m => m.Snapshot!.TargetEnergyKcal > 0).ToList();
            decimal? t = tg.Count > 0 ? tg.Average(m => m.Snapshot!.TargetEnergyKcal!.Value) : null;
            return new TrendPoint(g.Key, Math.Round(e, 0), t is null ? null : Math.Round(t.Value, 0), t is > 0 ? Math.Round(e / t.Value * 100, 1) : null, g.Count());
        }).ToList();
        IReadOnlyList<PeriodAverage> Periods(string mode) => list.GroupBy(m => ReportContext.PeriodKey(m.MenuDate, mode)).OrderBy(g => g.Min(m => m.MenuDate)).Select(g =>
        {
            var tg = g.Where(m => m.Snapshot!.TargetEnergyKcal > 0).ToList();
            return new PeriodAverage(g.Key, g.Min(m => m.MenuDate), Math.Round(g.Average(m => m.Snapshot!.Nutrition.EnergyKcal), 0),
                tg.Count > 0 ? Math.Round(tg.Average(m => m.Snapshot!.TargetEnergyKcal!.Value), 0) : null, Math.Round(g.Average(m => m.Snapshot!.Nutrition.ProteinPct), 1),
                Math.Round(g.Average(m => m.Snapshot!.Nutrition.LipidPct), 1), Math.Round(g.Average(m => m.Snapshot!.Nutrition.GlucidPct), 1), g.Count());
        }).ToList();
        var warnings = list.SelectMany(m => m.Snapshot!.Warnings).GroupBy(w => (w.Code, w.Level)).OrderByDescending(g => g.Key.Level).ThenByDescending(g => g.Count())
            .Select(g => new WarningCount(g.Key.Code, g.Key.Level.ToString(), g.Count(), g.First().MessageVi, g.First().MessageEn)).ToList();
        return new AnalysisResult("range", $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}", list.Count, Math.Round(energy, 0), targetAvg is null ? null : Math.Round(targetAvg.Value, 0),
            targetAvg is > 0 ? Math.Round(energy / targetAvg.Value * 100, 1) : null, Math.Round(Avg(x => x.Snapshot!.Nutrition.ProteinPct), 1), Math.Round(Avg(x => x.Snapshot!.Nutrition.LipidPct), 1),
            Math.Round(Avg(x => x.Snapshot!.Nutrition.GlucidPct), 1), Math.Round(Avg(x => x.Snapshot!.Nutrition.AnimalProteinPct), 1), Math.Round(Avg(x => x.Snapshot!.Nutrition.ProteinG), 1),
            Math.Round(Avg(x => x.Snapshot!.Nutrition.LipidG), 1), Math.Round(Avg(x => x.Snapshot!.Nutrition.GlucidG), 1), nutrients, trend, Periods("week"), Periods("month"), warnings);
    }

    // ------------------------------------------------------------------ dashboard (SPEC §7.9, by role/permission)

    public async Task<object> DashboardAsync(CancellationToken ct)
    {
        var today = ctx.Today;
        var now = clock.GetUtcNow();
        var result = new Dictionary<string, object?>();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        if (user.Has(Permissions.Menu.Approve) || user.Has(Permissions.Menu.ApproveFinal))
            result["pendingApproval"] = await db.Set<Menu>().CountAsync(m => m.Status == MenuStatus.Submitted, ct);
        if (user.Has(Permissions.Menu.Create))
            result["drafts"] = await db.Set<Menu>().CountAsync(m => (m.Status == MenuStatus.Draft || m.Status == MenuStatus.Returned) && m.OwnerUserId == user.UserId, ct);
        if (user.Has(Permissions.Alert.View))
        {
            var q = db.Set<Alert>().AsNoTracking().Where(a => a.Status != AlertStatus.Resolved && a.Level == WarningLevel.Critical);
            if (user.WarehouseScope is { } ws) q = q.Where(a => a.WarehouseId == null || ws.Contains(a.WarehouseId.Value));
            result["criticalAlerts"] = await q.CountAsync(ct);
            result["latestAlerts"] = await q.OrderByDescending(a => a.CreatedAt).Take(5).Select(a => new { a.Id, a.Code, a.MessageVi, a.MessageEn, a.Link, a.CreatedAt }).ToListAsync(ct);
        }
        if (user.Has(Permissions.Menu.View))
        {
            var todays = await ctx.MenusAsync(today, today, new ReportFilter(), ct);
            result["servingsToday"] = todays.Sum(m => m.Servings);
            result["servingsByShift"] = todays.GroupBy(m => m.Shift).Select(g => new { shift = g.Key, servings = g.Sum(m => m.Servings), menus = g.Count() }).ToList();
        }
        if (user.Has(Permissions.Nutrition.View))
        {
            var last7 = (await ctx.MenusAsync(today.AddDays(-6), today, new ReportFilter(), ct)).Where(m => m.Snapshot?.TargetEnergyKcal > 0).ToList();
            result["energyPct7d"] = last7.Count == 0 ? null : Math.Round(last7.Average(m => m.Snapshot!.Nutrition.EnergyKcal / m.Snapshot.TargetEnergyKcal!.Value * 100), 1);
            result["energyTrend"] = last7.GroupBy(m => m.MenuDate).OrderBy(g => g.Key)
                .Select(g => new { date = g.Key, pct = Math.Round(g.Average(m => m.Snapshot!.Nutrition.EnergyKcal / m.Snapshot.TargetEnergyKcal!.Value * 100), 1) }).ToList();
        }
        if (user.Has(Permissions.Cost.View) || user.Has(Permissions.Menu.ViewCost))
        {
            var week = await ctx.MenusAsync(monday, monday.AddDays(6), new ReportFilter(), ct);
            var servings = week.Sum(m => m.Servings);
            result["costPerServingWeek"] = servings == 0 ? null : Math.Round(week.Sum(m => (m.CostPerServing ?? 0) * m.Servings) / servings, 0);
            var budgets = week.Where(m => m.Snapshot?.Cost?.Budget is > 0).ToList();
            result["budgetPerServingWeek"] = budgets.Count == 0 ? null : Math.Round(budgets.Average(m => m.Snapshot!.Cost!.Budget!.Value), 0);
        }
        if (user.Has(Permissions.Stock.View))
        {
            var ws = await settings.GetAsync<WarningSettings>(SettingKeys.Warning, ct);
            var limit = today.AddDays(ws.StockExpiryWarnDays);
            var lots = ctx.ScopeWarehouse(db.Set<StockLot>().AsNoTracking(), l => l.WarehouseId).Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate <= limit);
            result["expiringLots"] = await lots.CountAsync(ct);
            result["expiringList"] = await lots.OrderBy(l => l.ExpiryDate).Take(5).Select(l => new { l.Id, ingredient = l.Ingredient!.NameVi, ingredientEn = l.Ingredient.NameEn, l.LotNo, l.ExpiryDate, l.Quantity, unit = l.Ingredient.PurchaseUnit!.Symbol }).ToListAsync(ct);
        }
        if (user.Has(Permissions.Sample.View))
            result["samplesDue"] = await db.Set<FoodSample>().CountAsync(s => s.DisposedAt == null && s.PlannedDisposeAt <= now, ct);
        return result;
    }
}
