using System.Text.Json;
using BlueMeal.Api.Services;
using BlueMeal.Application.Abstractions;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Reports;

/// <summary>Common report filter (SPEC §7.9): date range, warehouse, customer, subject, food group … Each report declares which ones it uses.</summary>
public sealed class ReportFilter
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? SubjectGroupId { get; set; }
    public Guid? FoodGroupId { get; set; }
    public Guid? IngredientId { get; set; }
    public Guid? PartnerId { get; set; }
    public Guid? MealShiftId { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>Report specific grouping/mode (e.g. ingredient|group|partner, day|week|month, dish|ingredient).</summary>
    public string? Mode { get; set; }
    /// <summary>Look-ahead days for expiry reports.</summary>
    public int? Days { get; set; }
    public string? Q { get; set; }

    public (DateOnly From, DateOnly To) Range(DateOnly today, int defaultDays = 30)
        => (From ?? today.AddDays(-defaultDays), To ?? today);
}

public static class ReportFilters
{
    public const string DateRange = "dateRange", Warehouse = "warehouse", Customer = "customer", Subject = "subject", FoodGroup = "foodGroup",
        Ingredient = "ingredient", Partner = "partner", MealShift = "mealShift", User = "user", Mode = "mode", Days = "days", Q = "q";
}

/// <summary>Column of a report: localized header, value type drives web and Excel formatting (Vietnamese number format).</summary>
public sealed record ReportColumn(string Key, string Vi, string En, string Type = "text", int Decimals = 0, double Width = 16, bool Total = false)
{
    public string Header(string lang) => lang == "en" ? En : Vi;

    public string? ExcelFormat => Type switch
    {
        "money" => "#,##0",
        "number" => Decimals == 0 ? "#,##0" : "#,##0." + new string('0', Decimals),
        "percent" => Decimals == 0 ? "0\"%\"" : "0." + new string('0', Decimals) + "\"%\"",
        "date" => "dd/mm/yyyy",
        "datetime" => "dd/mm/yyyy hh:mm",
        _ => null,
    };
}

public sealed class ReportResult
{
    public required IReadOnlyList<ReportColumn> Columns { get; init; }
    public required List<Dictionary<string, object?>> Rows { get; init; }
    public string? NoteVi { get; init; }
    public string? NoteEn { get; init; }
}

/// <summary>Mode option shown as a select in the report filter bar.</summary>
public sealed record ReportMode(string Value, string Vi, string En);

/// <summary>One of the 16 reports (RPT-01 … RPT-16). Implementations are discovered by DI scanning.</summary>
public interface IReport
{
    string Code { get; }
    /// <summary>Permission required in addition to report.view (e.g. audit.view for RPT-16, cost.view for cost reports).</summary>
    string? Permission => null;
    IReadOnlyList<string> Filters { get; }
    IReadOnlyList<ReportMode> Modes => [];
    Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct);
}

/// <summary>Shared helpers for reports (scoping, today's date, menu snapshot loading).</summary>
public sealed class ReportContext(AppDbContext db, ICurrentUser user, TimeProvider clock)
{
    public AppDbContext Db => db;
    public ICurrentUser User => user;
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
    public DateTimeOffset Now => clock.GetUtcNow();
    public bool En => user.Language == "en";
    /// <summary>Localized text value for report cells (columns have their own bilingual headers).</summary>
    public string T(string vi, string en) => En ? en : vi;

    /// <summary>Start of a Vietnam day as UTC (Npgsql only accepts UTC offsets for timestamptz).</summary>
    public static DateTimeOffset StartOf(DateOnly d) => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();

    public IQueryable<T> ScopeWarehouse<T>(IQueryable<T> q, System.Linq.Expressions.Expression<Func<T, Guid>> warehouse)
    {
        if (user.WarehouseScope is not { } scope) return q;
        var ids = scope.ToList();
        var body = System.Linq.Expressions.Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)], System.Linq.Expressions.Expression.Constant(ids), warehouse.Body);
        return q.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, warehouse.Parameters[0]));
    }

    public sealed record MenuWithSnapshot(Guid Id, string Code, DateOnly MenuDate, Guid MealShiftId, string? Shift, Guid? CustomerId, string? Customer, Guid? SubjectGroupId, string? Subject,
        int Servings, decimal? CostPerServing, decimal? EnergyKcal, MenuStatus Status, MenuSnapshot? Snapshot);

    /// <summary>Official (approved/published) menus of a range with their stored snapshot.</summary>
    public async Task<List<MenuWithSnapshot>> MenusAsync(DateOnly from, DateOnly to, ReportFilter f, CancellationToken ct, bool includeDrafts = false)
    {
        var q = db.Set<Menu>().AsNoTracking().Where(m => m.MenuDate >= from && m.MenuDate <= to && m.Status != MenuStatus.Cancelled);
        if (!includeDrafts) q = q.Where(m => m.Status == MenuStatus.Approved || m.Status == MenuStatus.Published);
        if (user.CustomerScope is { } cs) q = q.Where(m => m.CustomerId != null && cs.Contains(m.CustomerId.Value));
        if (f.CustomerId is { } c) q = q.Where(m => m.CustomerId == c);
        if (f.SubjectGroupId is { } s) q = q.Where(m => m.SubjectGroupId == s);
        if (f.MealShiftId is { } sh) q = q.Where(m => m.MealShiftId == sh);
        var rows = await q.OrderBy(m => m.MenuDate).ThenBy(m => m.MealShift!.SortOrder).Select(m => new
        {
            m.Id, m.Code, m.MenuDate, m.MealShiftId, shift = m.MealShift!.NameVi, m.CustomerId, customer = m.Customer!.NameVi, m.SubjectGroupId, subject = m.SubjectGroup!.NameVi,
            m.Servings, m.CostPerServing, m.EnergyKcal, m.Status, m.Snapshot,
        }).Take(5000).ToListAsync(ct);
        return rows.Select(r => new MenuWithSnapshot(r.Id, r.Code, r.MenuDate, r.MealShiftId, r.shift, r.CustomerId, r.customer, r.SubjectGroupId, r.subject, r.Servings, r.CostPerServing, r.EnergyKcal, r.Status,
            r.Snapshot is null ? null : JsonSerializer.Deserialize<MenuSnapshot>(r.Snapshot, MenuService.Json))).ToList();
    }

    public static string PeriodKey(DateOnly d, string? mode) => mode switch
    {
        "week" => $"{System.Globalization.ISOWeek.GetYear(d.ToDateTime(TimeOnly.MinValue))}-W{System.Globalization.ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue)):00}",
        "month" => $"{d:MM/yyyy}",
        _ => $"{d:dd/MM/yyyy}",
    };

    public static readonly ReportMode[] PeriodModes = [new("day", "Theo ngày", "By day"), new("week", "Theo tuần", "By week"), new("month", "Theo tháng", "By month")];
}

/// <summary>Small helpers to build rows.</summary>
internal static class Row
{
    public static Dictionary<string, object?> Of(params (string Key, object? Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);
}
