using BlueMeal.Api.Authorization;
using BlueMeal.Api.Reports;
using BlueMeal.Api.Services;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Application.Settings;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BlueMeal.Api.Controllers.Analysis;

/// <summary>Nutrition analysis (SPEC §7.5), meal cost sheet (SPEC §7.7) and the role dashboard (SPEC §7.9).</summary>
[Route("api/v1")]
public sealed class AnalysisController(AnalysisService analysis, CostSheetService costSheet, ReportService reports, ReportContext ctx) : ApiControllerBase
{
    [HttpPost("analysis/nutrition"), HasPermission(Permissions.Nutrition.View)]
    public async Task<IActionResult> Nutrition([FromBody] AnalysisRequest req, CancellationToken ct) => Ok(await analysis.NutritionAsync(req, ct));

    /// <summary>Excel of the analysis table: range scope uses RPT-07 (per menu), otherwise the single nutrient table.</summary>
    [HttpPost("analysis/nutrition/export"), HasPermission(Permissions.Nutrition.Export)]
    public async Task<IActionResult> NutritionExport([FromBody] AnalysisRequest req, CancellationToken ct)
    {
        var (content, name) = await reports.ExcelAsync("RPT-07", new ReportFilter { From = req.From, To = req.To, CustomerId = req.CustomerId, SubjectGroupId = req.SubjectGroupId, MealShiftId = req.MealShiftId }, ct);
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    [HttpGet("cost-sheet"), HasPermission(Permissions.Cost.View)]
    public async Task<IActionResult> CostSheet([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? mode, [FromQuery] Guid? customerId, [FromQuery] Guid? subjectGroupId, CancellationToken ct)
    {
        var t = to ?? ctx.Today;
        var f = from ?? t.AddDays(-29);
        if (t < f || t.DayNumber - f.DayNumber > 400) throw new BusinessRuleException("report.rangeTooLong", 400);
        var rows = await costSheet.BuildAsync(f, t, mode ?? "day", new ReportFilter { CustomerId = customerId, SubjectGroupId = subjectGroupId }, ct);
        var servings = rows.Sum(r => r.Servings);
        return Ok(new
        {
            from = f, to = t, rows,
            totals = new
            {
                servings, plannedTotal = rows.Sum(r => r.PlannedTotal), actualTotal = rows.Sum(r => r.ActualTotal), revenue = rows.Sum(r => r.Revenue), margin = rows.Sum(r => r.Margin),
                variance = rows.Sum(r => r.Variance), plannedPerServing = servings == 0 ? (decimal?)null : Math.Round(rows.Sum(r => r.PlannedTotal) / servings, 0),
                actualPerServing = servings == 0 || rows.Sum(r => r.ActualFood) == 0 ? (decimal?)null : Math.Round(rows.Sum(r => r.ActualTotal) / servings, 0),
            },
        });
    }

    [HttpGet("dashboard"), HasPermission(Permissions.Self)]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await analysis.DashboardAsync(ct));
}

public sealed class WarningSettingsValidator : AbstractValidator<WarningSettings>
{
    public WarningSettingsValidator()
    {
        RuleFor(x => x.EnergyCriticalLowPct).InclusiveBetween(0, 100).LessThanOrEqualTo(x => x.EnergyWarnLowPct);
        RuleFor(x => x.EnergyWarnLowPct).InclusiveBetween(0, 100);
        RuleFor(x => x.EnergyWarnHighPct).InclusiveBetween(100, 300);
        RuleFor(x => x.EnergyCriticalHighPct).GreaterThanOrEqualTo(x => x.EnergyWarnHighPct).LessThanOrEqualTo(400);
        RuleFor(x => x.CostCriticalOverPct).InclusiveBetween(0, 200);
        RuleFor(x => x.RepeatDishDays).InclusiveBetween(0, 60);
        RuleFor(x => x.CertExpiryWarnDays).InclusiveBetween(0, 365);
        RuleFor(x => x.StockExpiryWarnDays).InclusiveBetween(0, 365);
    }
}
