using System.Text.Json;
using BlueMeal.Api.Authorization;
using BlueMeal.Api.Reports;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Analysis;

/// <summary>Reports RPT-01…RPT-16 (SPEC §7.9): web view, Excel export (background for large ones), favorite filters.</summary>
[Route("api/v1/reports")]
public sealed class ReportsController(AppDbContext db, ReportService reports, IFileStorage files, IAuditWriter audit, TimeProvider clock) : ApiControllerBase
{
    private const int WebMaxRows = 2000;

    [HttpGet, HasPermission(Permissions.Report.View)]
    public async Task<IActionResult> Catalog(CancellationToken ct) => Ok(await reports.CatalogAsync(ct));

    /// <summary>Runs a report for the web view (first 2,000 rows; export for the full data).</summary>
    [HttpPost("{code}/run"), HasPermission(Permissions.Report.View)]
    public async Task<IActionResult> Run(string code, [FromBody] ReportFilter filter, CancellationToken ct)
    {
        var (_, r) = await reports.RunAsync(code, filter, ct);
        var lang = CurrentUser.Language;
        return Ok(new
        {
            columns = r.Columns.Select(c => new { c.Key, title = c.Header(lang), c.Type, c.Decimals, c.Total }),
            rows = r.Rows.Take(WebMaxRows), totalRows = r.Rows.Count, truncated = r.Rows.Count > WebMaxRows, note = lang == "en" ? r.NoteEn : r.NoteVi,
            totals = r.Columns.Where(c => c.Total).ToDictionary(c => c.Key, c => r.Rows.Sum(x => x.TryGetValue(c.Key, out var v) && v is not null ? Convert.ToDecimal(v) : 0m)),
        });
    }

    /// <summary>Excel export: small reports are returned directly; large ones are queued and announced by notification.</summary>
    [HttpPost("{code}/export"), HasPermission(Permissions.Report.Export)]
    public async Task<IActionResult> Export(string code, [FromBody] ReportFilter filter, [FromQuery] bool background = false, [FromQuery] string? format = null, CancellationToken ct = default)
    {
        if (IsCsv(format))
        {
            // CSV UTF-8 for data exchange (TT 39/2017/TT-BTTTT): plain data, cheap to build, always returned directly
            var (content, name, count) = await reports.CsvAsync(code, filter, ct);
            await audit.WriteAsync(AuditAction.Export, "Report", code, null, filter, message: $"{count} rows (csv)", ct: ct);
            return File(content, CsvWriter.ContentType, name);
        }
        if (!background)
        {
            var (_, r) = await reports.RunAsync(code, filter, ct);
            if (r.Rows.Count <= ReportService.SyncExportMaxRows)
            {
                var (content, name) = await reports.ExcelAsync(code, filter, ct);
                await audit.WriteAsync(AuditAction.Export, "Report", code, null, filter, message: $"{r.Rows.Count} rows", ct: ct);
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
            }
        }
        var job = await reports.QueueAsync(code, filter, ct);
        await audit.WriteAsync(AuditAction.Export, "Report", code, null, filter, message: "background", ct: ct);
        return Accepted(new { jobId = job.Id, status = job.Status.ToString() });
    }

    [HttpGet("exports"), HasPermission(Permissions.Report.Export)]
    public async Task<IActionResult> Exports(CancellationToken ct)
        => Ok(await db.Set<ExportJob>().AsNoTracking().Where(j => j.RequestedBy == UserId).OrderByDescending(j => j.RequestedAt).Take(50)
            .Select(j => new { j.Id, j.ReportCode, status = j.Status.ToString(), j.RequestedAt, j.FinishedAt, j.FileName, j.ExpiresAt, j.Error }).ToListAsync(ct));

    [HttpGet("exports/{id:guid}/download"), HasPermission(Permissions.Report.Export)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var j = await db.Set<ExportJob>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.RequestedBy == UserId, ct) ?? throw new NotFoundException("export");
        if (j.Status != JobStatus.Succeeded || j.FilePath is null || j.ExpiresAt < clock.GetUtcNow()) throw new BusinessRuleException("report.exportExpired");
        return File(files.OpenRead(j.FilePath), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", j.FileName ?? $"{j.ReportCode}.xlsx");
    }

    // ------------------------------------------------------------------ favorite filters (per user)

    public sealed record Favorite(string Name, ReportFilter Filter);

    private string Key(string code) => $"report.favorites.{code.ToUpperInvariant()}";

    [HttpGet("{code}/favorites"), HasPermission(Permissions.Report.View)]
    public async Task<IActionResult> Favorites(string code, CancellationToken ct)
    {
        var p = await db.Set<UserPreference>().AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId && x.Key == Key(code), ct);
        return Ok(p is null ? [] : JsonSerializer.Deserialize<List<Favorite>>(p.Value, ReportService.Json) ?? []);
    }

    [HttpPut("{code}/favorites"), HasPermission(Permissions.Report.View)]
    public async Task<IActionResult> SaveFavorites(string code, [FromBody] List<Favorite> favorites, CancellationToken ct)
    {
        if (favorites.Count > 20 || favorites.Any(f => string.IsNullOrWhiteSpace(f.Name) || f.Name.Length > 100)) throw new AppException("error.validation");
        var p = await db.Set<UserPreference>().FirstOrDefaultAsync(x => x.UserId == UserId && x.Key == Key(code), ct);
        if (p is null) { p = new UserPreference { UserId = UserId, Key = Key(code) }; db.Add(p); }
        p.Value = JsonSerializer.Serialize(favorites, ReportService.Json);
        p.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
