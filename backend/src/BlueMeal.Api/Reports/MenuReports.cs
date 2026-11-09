using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Engines;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using F = BlueMeal.Api.Reports.ReportFilters;

namespace BlueMeal.Api.Reports;

/// <summary>RPT-06 — nutrition reached vs recommendation per day/week/month × subject × customer; days met / not met.</summary>
public sealed class Rpt06RecommendationCompliance(ReportContext ctx, ISettingsService settings) : IReport
{
    public string Code => "RPT-06";
    public string? Permission => Permissions.Nutrition.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Customer, F.Subject, F.MealShift, F.Mode];
    public IReadOnlyList<ReportMode> Modes => ReportContext.PeriodModes;

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 30);
        var ws = await settings.GetAsync<WarningSettings>(SettingKeys.Warning, ct);
        var menus = (await ctx.MenusAsync(from, to, f, ct)).Where(m => m.Snapshot is not null).ToList();
        bool Met(ReportContext.MenuWithSnapshot m)
        {
            var target = m.Snapshot!.TargetEnergyKcal ?? 0;
            var pct = target > 0 ? m.Snapshot.Nutrition.EnergyKcal / target * 100 : 100;
            return pct >= ws.EnergyWarnLowPct && pct <= ws.EnergyWarnHighPct && m.Snapshot.Warnings.All(w => w.Level != WarningLevel.Critical);
        }
        var rows = menus.GroupBy(m => (Period: ReportContext.PeriodKey(m.MenuDate, f.Mode), m.Subject, m.Customer))
            .OrderBy(g => g.Min(m => m.MenuDate)).ThenBy(g => g.Key.Subject).ThenBy(g => g.Key.Customer)
            .Select(g =>
            {
                var energy = g.Average(m => m.Snapshot!.Nutrition.EnergyKcal);
                var targets = g.Where(m => m.Snapshot!.TargetEnergyKcal > 0).ToList();
                var target = targets.Count > 0 ? targets.Average(m => m.Snapshot!.TargetEnergyKcal!.Value) : (decimal?)null;
                var days = g.GroupBy(m => m.MenuDate).ToList();
                var met = days.Count(d => d.All(Met));
                return Row.Of(("period", g.Key.Period), ("subject", g.Key.Subject), ("customer", g.Key.Customer), ("menus", g.Count()),
                    ("energy", Math.Round(energy, 0)), ("target", target is null ? null : Math.Round(target.Value, 0)), ("pct", target is > 0 ? Math.Round(energy / target.Value * 100, 1) : null),
                    ("p", Math.Round(g.Average(m => m.Snapshot!.Nutrition.ProteinPct), 1)), ("l", Math.Round(g.Average(m => m.Snapshot!.Nutrition.LipidPct), 1)),
                    ("g", Math.Round(g.Average(m => m.Snapshot!.Nutrition.GlucidPct), 1)), ("animal", Math.Round(g.Average(m => m.Snapshot!.Nutrition.AnimalProteinPct), 1)),
                    ("daysMet", met), ("daysNotMet", days.Count - met));
            }).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("period", "Kỳ", "Period", Width: 12), new("subject", "Đối tượng", "Subject", Width: 22), new("customer", "Khách hàng", "Customer", Width: 24), new("menus", "Số thực đơn", "Menus", "number", Width: 10),
                new("energy", "NL TB (kcal)", "Avg energy (kcal)", "number"), new("target", "NL khuyến nghị", "Target energy", "number"), new("pct", "% đáp ứng NL", "% energy", "percent", 1),
                new("p", "P (%)", "P (%)", "number", 1, 8), new("l", "L (%)", "L (%)", "number", 1, 8), new("g", "G (%)", "G (%)", "number", 1, 8), new("animal", "Đạm ĐV/tổng (%)", "Animal protein (%)", "number", 1),
                new("daysMet", "Số ngày đạt", "Days met", "number", Width: 10, Total: true), new("daysNotMet", "Số ngày không đạt", "Days not met", "number", Width: 10, Total: true),
            ],
            Rows = rows,
            NoteVi = $"Ngày đạt: năng lượng trong khoảng {ws.EnergyWarnLowPct:0}–{ws.EnergyWarnHighPct:0}% khuyến nghị và không có cảnh báo nghiêm trọng.",
            NoteEn = $"A day is met when energy is within {ws.EnergyWarnLowPct:0}–{ws.EnergyWarnHighPct:0}% of the target and there is no critical warning.",
        };
    }
}

/// <summary>RPT-07 — nutrition analysis of each menu (energy, macros, core micronutrients).</summary>
public sealed class Rpt07MenuNutrition(ReportContext ctx) : IReport
{
    public string Code => "RPT-07";
    public string? Permission => Permissions.Nutrition.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Customer, F.Subject, F.MealShift];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 7);
        var menus = (await ctx.MenusAsync(from, to, f, ct)).Where(m => m.Snapshot is not null).ToList();
        var nutrients = await ctx.Db.Set<Nutrient>().AsNoTracking().Where(n => n.IsActive && n.IsCore && n.Code != NutrientCodes.Energy).OrderBy(n => n.SortOrder).ThenBy(n => n.Code)
            .Select(n => new { n.Code, n.NameVi, n.NameEn, n.Unit, n.Decimals }).Take(24).ToListAsync(ct);
        var columns = new List<ReportColumn>
        {
            new("date", "Ngày", "Date", "date", Width: 12), new("shift", "Ca", "Shift", Width: 12), new("code", "Mã thực đơn", "Menu", Width: 18), new("customer", "Khách hàng", "Customer", Width: 22),
            new("energy", "Năng lượng (kcal)", "Energy (kcal)", "number"), new("p", "P (%)", "P (%)", "number", 1, 8), new("l", "L (%)", "L (%)", "number", 1, 8), new("g", "G (%)", "G (%)", "number", 1, 8),
        };
        columns.AddRange(nutrients.Select(n => new ReportColumn("n_" + n.Code, $"{n.NameVi} ({n.Unit})", $"{n.NameEn ?? n.NameVi} ({n.Unit})", "number", n.Decimals, 12)));
        var rows = menus.Select(m =>
        {
            var r = Row.Of(("date", m.MenuDate), ("shift", m.Shift), ("code", m.Code), ("customer", m.Customer), ("energy", Math.Round(m.Snapshot!.Nutrition.EnergyKcal, 0)),
                ("p", Math.Round(m.Snapshot.Nutrition.ProteinPct, 1)), ("l", Math.Round(m.Snapshot.Nutrition.LipidPct, 1)), ("g", Math.Round(m.Snapshot.Nutrition.GlucidPct, 1)));
            foreach (var n in nutrients) r["n_" + n.Code] = m.Snapshot.Nutrition.Totals.TryGetValue(n.Code, out var v) ? Math.Round(v, n.Decimals) : null;
            return r;
        }).ToList();
        if (rows.Count > 0)
        {
            var avg = Row.Of(("code", ctx.T("Trung bình", "Average")), ("energy", Math.Round(menus.Average(m => m.Snapshot!.Nutrition.EnergyKcal), 0)),
                ("p", Math.Round(menus.Average(m => m.Snapshot!.Nutrition.ProteinPct), 1)), ("l", Math.Round(menus.Average(m => m.Snapshot!.Nutrition.LipidPct), 1)), ("g", Math.Round(menus.Average(m => m.Snapshot!.Nutrition.GlucidPct), 1)));
            foreach (var n in nutrients) avg["n_" + n.Code] = Math.Round(menus.Average(m => m.Snapshot!.Nutrition.Totals.GetValueOrDefault(n.Code)), n.Decimals);
            rows.Add(avg);
        }
        return new ReportResult { Columns = columns, Rows = rows };
    }
}

/// <summary>RPT-08 — statistics of nutrition/cost warnings on official menus.</summary>
public sealed class Rpt08WarningStats(ReportContext ctx) : IReport
{
    public string Code => "RPT-08";
    public string? Permission => Permissions.Nutrition.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Customer, F.Subject];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 30);
        var menus = (await ctx.MenusAsync(from, to, f, ct)).Where(m => m.Snapshot is not null).ToList();
        var warnings = menus.SelectMany(m => m.Snapshot!.Warnings.Select(w => (m, w))).ToList();
        var rows = warnings.GroupBy(x => (x.w.Code, x.w.Level)).OrderByDescending(g => g.Key.Level).ThenByDescending(g => g.Count())
            .Select(g => Row.Of(("code", g.Key.Code), ("level", g.Key.Level.ToString()), ("count", g.Count()), ("menus", g.Select(x => x.m.Id).Distinct().Count()),
                ("pct", menus.Count == 0 ? 0 : Math.Round(g.Select(x => x.m.Id).Distinct().Count() * 100m / menus.Count, 1)), ("first", g.Min(x => x.m.MenuDate)), ("last", g.Max(x => x.m.MenuDate)),
                ("example", ctx.En ? g.First().w.MessageEn : g.First().w.MessageVi))).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("code", "Mã cảnh báo", "Warning code", Width: 18), new("level", "Mức", "Level", Width: 10), new("count", "Số lần", "Occurrences", "number", Total: true),
                new("menus", "Số thực đơn", "Menus", "number"), new("pct", "% thực đơn", "% of menus", "percent", 1), new("first", "Lần đầu", "First", "date", Width: 12),
                new("last", "Gần nhất", "Last", "date", Width: 12), new("example", "Ví dụ", "Example", Width: 60),
            ],
            Rows = rows,
            NoteVi = $"Tổng số thực đơn trong kỳ: {menus.Count}", NoteEn = $"Menus in the period: {menus.Count}",
        };
    }
}

/// <summary>RPT-09 — usage frequency of dishes or ingredients in official menus.</summary>
public sealed class Rpt09UsageFrequency(ReportContext ctx) : IReport
{
    public string Code => "RPT-09";
    public string? Permission => Permissions.Menu.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Customer, F.Subject, F.Mode];
    public IReadOnlyList<ReportMode> Modes => [new("dish", "Theo món ăn", "By dish"), new("ingredient", "Theo nguyên liệu", "By ingredient")];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 30);
        var menus = await ctx.MenusAsync(from, to, f, ct);
        var ids = menus.Select(m => m.Id).ToList();
        var items = await ctx.Db.Set<MenuItem>().AsNoTracking().Where(i => ids.Contains(i.MenuId))
            .Select(i => new { i.MenuId, i.DishId, i.Dish!.Code, i.Dish.NameVi, i.Dish.NameEn, i.Dish.DishType, i.RecipeId, i.ServingFactor }).ToListAsync(ct);
        var byMenu = menus.ToDictionary(m => m.Id);
        if (f.Mode == "ingredient")
        {
            var dishIds = items.Select(i => i.DishId).Distinct().ToList();
            var locked = items.Where(i => i.RecipeId != null).Select(i => i.RecipeId!.Value).Distinct().ToList();
            var recipes = await ctx.Db.Set<Recipe>().AsNoTracking().IgnoreQueryFilters().Where(r => locked.Contains(r.Id) || (dishIds.Contains(r.DishId) && r.IsCurrent && !r.IsDeleted))
                .Select(r => new { r.Id, r.DishId, r.IsCurrent, r.BaseServings, items = r.Items.Select(x => new { x.IngredientId, x.GrossG, x.Ingredient!.Code, x.Ingredient.NameVi, x.Ingredient.GramsPerUnit, unit = x.Ingredient.PurchaseUnit!.Symbol }) })
                .ToListAsync(ct);
            var uses = items.SelectMany(i =>
            {
                var r = (i.RecipeId is { } rid ? recipes.FirstOrDefault(x => x.Id == rid) : null) ?? recipes.FirstOrDefault(x => x.DishId == i.DishId && x.IsCurrent);
                var servings = byMenu[i.MenuId].Servings;
                return r is null ? [] : r.items.Select(x => new { i.MenuId, x.IngredientId, x.Code, x.NameVi, x.unit, qty = x.GrossG * servings * i.ServingFactor / Math.Max(r.BaseServings, 1) / Math.Max(x.GramsPerUnit, 1) });
            }).ToList();
            var rows = uses.GroupBy(u => (u.IngredientId, u.Code, u.NameVi, u.unit)).OrderByDescending(g => g.Select(x => x.MenuId).Distinct().Count()).ThenBy(g => g.Key.Code)
                .Select(g => Row.Of(("code", g.Key.Code), ("name", g.Key.NameVi), ("unit", g.Key.unit), ("menus", g.Select(x => x.MenuId).Distinct().Count()),
                    ("qty", Math.Round(g.Sum(x => x.qty), 3)), ("last", g.Max(x => byMenu[x.MenuId].MenuDate)))).ToList();
            return new ReportResult
            {
                Columns = [new("code", "Mã", "Code", Width: 12), new("name", "Nguyên liệu", "Ingredient", Width: 30), new("unit", "ĐVT", "Unit", Width: 8), new("menus", "Số thực đơn sử dụng", "Menus", "number"),
                    new("qty", "Tổng nhu cầu", "Total demand", "number", 3), new("last", "Dùng gần nhất", "Last used", "date", Width: 12)],
                Rows = rows,
            };
        }
        var total = Math.Max(menus.Count, 1);
        var dishRows = items.GroupBy(i => (i.DishId, i.Code, i.NameVi, i.NameEn, i.DishType)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Code)
            .Select(g => Row.Of(("code", g.Key.Code), ("name", ctx.En && g.Key.NameEn != null ? g.Key.NameEn : g.Key.NameVi), ("type", g.Key.DishType.ToString()), ("times", g.Count()),
                ("pct", Math.Round(g.Count() * 100m / total, 1)), ("servings", g.Sum(i => byMenu[i.MenuId].Servings)), ("last", g.Max(i => byMenu[i.MenuId].MenuDate)))).ToList();
        return new ReportResult
        {
            Columns = [new("code", "Mã", "Code", Width: 12), new("name", "Món ăn", "Dish", Width: 30), new("type", "Loại món", "Type", Width: 14), new("times", "Số lần", "Times", "number", Total: true),
                new("pct", "% thực đơn", "% of menus", "percent", 1), new("servings", "Tổng suất", "Servings", "number", Total: true), new("last", "Dùng gần nhất", "Last used", "date", Width: 12)],
            Rows = dishRows,
        };
    }
}

/// <summary>RPT-10 — weekly/monthly menu in the published layout (no costs).</summary>
public sealed class Rpt10PublishedMenu(ReportContext ctx) : IReport
{
    public string Code => "RPT-10";
    public string? Permission => Permissions.Menu.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Customer, F.Subject];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = (f.From ?? ctx.Today, f.To ?? ctx.Today.AddDays(6));
        var menus = (await ctx.MenusAsync(from, to, f, ct)).Where(m => m.Status == MenuStatus.Published).ToList();
        var ids = menus.Select(m => m.Id).ToList();
        var dishes = await ctx.Db.Set<MenuItem>().AsNoTracking().Where(i => ids.Contains(i.MenuId)).OrderBy(i => i.SortOrder)
            .Select(i => new { i.MenuId, i.Dish!.NameVi, i.Dish.NameEn }).ToListAsync(ct);
        var culture = new System.Globalization.CultureInfo(ctx.En ? "en-US" : "vi-VN");
        var rows = menus.Select(m => Row.Of(("date", m.MenuDate), ("weekday", culture.DateTimeFormat.GetDayName(m.MenuDate.DayOfWeek)), ("shift", m.Shift), ("customer", m.Customer), ("subject", m.Subject),
            ("dishes", string.Join("; ", dishes.Where(d => d.MenuId == m.Id).Select(d => ctx.En && d.NameEn != null ? d.NameEn : d.NameVi))), ("energy", m.EnergyKcal is { } e ? Math.Round(e, 0) : null))).ToList();
        return new ReportResult
        {
            Columns = [new("date", "Ngày", "Date", "date", Width: 12), new("weekday", "Thứ", "Day", Width: 12), new("shift", "Ca ăn", "Shift", Width: 14), new("customer", "Khách hàng", "Customer", Width: 22),
                new("subject", "Đối tượng", "Subject", Width: 20), new("dishes", "Thực đơn", "Dishes", Width: 70), new("energy", "Năng lượng (kcal)", "Energy (kcal)", "number")],
            Rows = rows,
        };
    }
}
