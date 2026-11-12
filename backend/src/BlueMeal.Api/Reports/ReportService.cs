using System.Globalization;
using System.Text.Json;
using BlueMeal.Api.Infrastructure;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Identity;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Reports;

public sealed record ReportInfo(string Code, string NameVi, string NameEn, string? GroupCode, string? GroupVi, string? GroupEn, int SortOrder, IReadOnlyList<string> Filters, IReadOnlyList<ReportMode> Modes);

/// <summary>Report catalog with permission checks, execution, Excel rendering and background export (SPEC §7.9).</summary>
public sealed class ReportService(AppDbContext db, IEnumerable<IReport> reports, ICurrentUser user, IExcelService excel, IFileStorage files, IBackgroundJobs jobs, TimeProvider clock)
{
    /// <summary>Exports above this row count run as a background export job.</summary>
    public const int SyncExportMaxRows = 3000;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IReport Find(string code) => reports.FirstOrDefault(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? throw new NotFoundException("report");

    private bool CanRun(IReport r, ReportDefinition? def)
        => user.Has(Permissions.Report.View) && (r.Permission is null || user.Has(r.Permission))
           && (def?.ReportGroup?.RequiredPermission is not { Length: > 0 } gp || user.Has(gp)) && def?.IsActive != false;

    public async Task<IReadOnlyList<ReportInfo>> CatalogAsync(CancellationToken ct)
    {
        var defs = await db.Set<ReportDefinition>().AsNoTracking().Include(d => d.ReportGroup).ToDictionaryAsync(d => d.Code, ct);
        return reports.OrderBy(r => r.Code).Where(r => CanRun(r, defs.GetValueOrDefault(r.Code))).Select(r =>
        {
            var d = defs.GetValueOrDefault(r.Code);
            return new ReportInfo(r.Code, d?.NameVi ?? r.Code, d?.NameEn ?? r.Code, d?.ReportGroup?.Code, d?.ReportGroup?.NameVi, d?.ReportGroup?.NameEn, d?.SortOrder ?? 0, r.Filters, r.Modes);
        }).OrderBy(r => r.Code).ToList();
    }

    public async Task<(ReportDefinition? Def, ReportResult Result)> RunAsync(string code, ReportFilter filter, CancellationToken ct)
    {
        var r = Find(code);
        var def = await db.Set<ReportDefinition>().AsNoTracking().Include(d => d.ReportGroup).FirstOrDefaultAsync(d => d.Code == r.Code, ct);
        if (!CanRun(r, def)) throw new ForbiddenException();
        if (filter.From is { } f && filter.To is { } t && (t < f || t.DayNumber - f.DayNumber > 400)) throw new BusinessRuleException("report.rangeTooLong", 400);
        return (def, await r.RunAsync(filter, ct));
    }

    /// <summary>Runs a report without the user permission check — only for callers that authorized the request themselves (public API scope, integration push).</summary>
    public Task<ReportResult> RunTrustedAsync(string code, ReportFilter filter, CancellationToken ct) => Find(code).RunAsync(filter, ct);

    private static string PeriodText(ReportFilter f, string lang)
        => f.From is null && f.To is null ? "" : $"{f.From:dd/MM/yyyy} – {f.To:dd/MM/yyyy}";

    /// <summary>Renders a report to Excel with the standard header and signature block.</summary>
    public async Task<(byte[] Content, string FileName)> ExcelAsync(string code, ReportFilter filter, CancellationToken ct)
    {
        var (def, result) = await RunAsync(code, filter, ct);
        var lang = user.Language;
        var org = await db.Set<Organization>().AsNoTracking().Select(o => new { o.Name, o.NameEn }).FirstOrDefaultAsync(ct);
        var title = lang == "en" ? def?.NameEn ?? code : def?.NameVi ?? code;
        var preparer = await db.Set<AppUser>().AsNoTracking().Where(u => u.Id == user.UserId).Select(u => u.FullName ?? u.UserName).FirstOrDefaultAsync(ct) ?? user.UserName ?? "-";
        var sheet = new ExcelSheet
        {
            Name = code,
            Header = new ExcelReportHeader((lang == "en" ? org?.NameEn : null) ?? org?.Name ?? "BlueMeal", $"{code} — {title}", PeriodText(filter, lang), preparer, clock.GetUtcNow(), true,
                lang == "en" ? result.NoteEn : result.NoteVi),
            Columns = result.Columns.Select(c => new ExcelColumn(c.Key, c.Header(lang), c.Width, c.ExcelFormat)).ToList(),
            Rows = result.Rows,
            TotalColumns = result.Columns.Where(c => c.Total).Select(c => c.Key).ToList(),
        };
        var name = $"{code}_{clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)):yyyyMMdd_HHmm}.xlsx";
        return (excel.Write(sheet), name);
    }

    /// <summary>Renders a report as CSV UTF-8 (data only: no header block, signatures or totals).</summary>
    public async Task<(byte[] Content, string FileName, int Rows)> CsvAsync(string code, ReportFilter filter, CancellationToken ct)
    {
        var (_, result) = await RunAsync(code, filter, ct);
        var columns = result.Columns.Select(c => new ExcelColumn(c.Key, c.Header(user.Language))).ToList();
        var name = $"{code}_{clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)):yyyyMMdd_HHmm}.csv";
        return (CsvWriter.Write(columns, result.Rows), name, result.Rows.Count);
    }

    /// <summary>Queues a background export (large reports) and returns the job id.</summary>
    public async Task<ExportJob> QueueAsync(string code, ReportFilter filter, CancellationToken ct)
    {
        var r = Find(code);
        var def = await db.Set<ReportDefinition>().AsNoTracking().Include(d => d.ReportGroup).FirstOrDefaultAsync(d => d.Code == r.Code, ct);
        if (!CanRun(r, def)) throw new ForbiddenException();
        var job = new ExportJob { ReportCode = r.Code, Parameters = JsonSerializer.Serialize(filter, Json), RequestedAt = clock.GetUtcNow(), RequestedBy = user.UserId ?? Guid.Empty, Language = user.Language };
        db.Add(job);
        await db.SaveChangesAsync(ct);
        await jobs.EnqueueAsync(JobNames.Export, new Dictionary<string, string> { ["id"] = job.Id.ToString() }, ct);
        return job;
    }

    /// <summary>Executes a queued export job (called by <see cref="ExportReportJob"/> with the requester's access).</summary>
    public async Task RunJobAsync(ExportJob job, CancellationToken ct)
    {
        job.Status = JobStatus.Running;
        await db.SaveChangesAsync(ct);
        try
        {
            var filter = JsonSerializer.Deserialize<ReportFilter>(job.Parameters ?? "{}", Json) ?? new ReportFilter();
            var (content, name) = await ExcelAsync(job.ReportCode, filter, ct);
            using var ms = new MemoryStream(content);
            var (stored, _, _) = await files.SaveAsync(ms, ".xlsx", ct);
            job.FilePath = stored;
            job.FileName = name;
            job.DownloadToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            job.ExpiresAt = clock.GetUtcNow().AddDays(7);
            job.Status = JobStatus.Succeeded;
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.Error = ex is AppException ae ? L.T(ae.Key, ae.Args) : ex.Message;
        }
        job.FinishedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Background export of a report to Excel (SPEC §7.9 "báo cáo nặng chạy nền qua export_job").</summary>
public sealed class ExportReportJob(AppDbContext db, ReportService reports, HttpCurrentUser user, PermissionService access, INotificationService notifications) : IAppJob
{
    public string Name => JobNames.Export;

    public async Task ExecuteAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        var job = await db.Set<ExportJob>().FirstOrDefaultAsync(j => j.Id == Guid.Parse(data["id"]), ct);
        if (job is null || job.Status != JobStatus.Queued) return;
        // act with the requester's permissions and data scopes
        user.OverrideUserId = job.RequestedBy;
        user.OverrideAccess = await access.LoadAsync(job.RequestedBy, ct);
        user.OverrideLanguage = job.Language;
        var culture = new CultureInfo(job.Language == "en" ? "en-US" : "vi-VN");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        await reports.RunJobAsync(job, ct);
        if (job.Status == JobStatus.Succeeded)
            await notifications.NotifyUsersAsync([job.RequestedBy], NotificationType.ExportReady, "notify.exportReady", [job.ReportCode], "/reports?tab=exports", ct);
    }
}
