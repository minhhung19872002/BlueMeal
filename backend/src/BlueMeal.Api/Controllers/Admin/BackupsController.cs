using System.Security.Cryptography;
using BlueMeal.Api.Auth;
using BlueMeal.Api.Authorization;
using BlueMeal.Api.Services;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Admin;

/// <summary>Backup history, manual backup and download with re-authentication (SPEC §10.7). Restore is done with deploy/backup/restore.(sh|ps1).</summary>
[Route("api/v1/backups")]
public sealed class BackupsController(AppDbContext db, BackupService backups, AuthService auth, ICacheService cache, IAuditWriter audit, ISettingsService settings) : ApiControllerBase
{
    [HttpGet, HasPermission(Permissions.SystemInfo.Backup)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, CancellationToken ct)
    {
        await backups.ImportScheduledAsync(ct);
        var query = db.Set<BackupJob>().AsNoTracking();
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(b => b.RequestedAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        var users = await db.Set<AppUser>().AsNoTracking().Where(u => rows.Select(r => r.RequestedBy).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
        var cfg = await settings.GetAsync<BackupSettings>(SettingKeys.Backup, ct);
        return Ok(new
        {
            items = rows.Select(b => new
            {
                b.Id, type = b.Type.ToString(), b.RequestedAt, b.StartedAt, b.FinishedAt, b.SizeBytes, b.Sha256, b.Location, status = b.Status.ToString(), b.Error, b.Note,
                requestedBy = b.RequestedBy is { } u ? users.GetValueOrDefault(u) : null, available = backups.ArchiveFile(b) is not null,
            }),
            q.Page, q.PageSize, totalItems = total,
            policy = new { cfg.Cron, cfg.KeepDaily, cfg.KeepWeekly, cfg.KeepMonthly, cfg.MirrorPath },
        });
    }

    public sealed record ManualInput(string? Note);

    [HttpPost, HasPermission(Permissions.SystemInfo.Backup)]
    public async Task<IActionResult> Manual([FromBody] ManualInput input, CancellationToken ct)
    {
        if (input.Note is { Length: > 500 }) throw new AppException("error.validation");
        var job = await backups.RequestManualAsync(input.Note, ct);
        await audit.WriteAsync(AuditAction.Backup, nameof(BackupJob), job.Id.ToString(), null, new { type = "Manual", input.Note }, ct: ct);
        return Accepted(new { job.Id, status = job.Status.ToString() });
    }

    public sealed record ReauthInput(string Password, string? Code);

    /// <summary>Step 1 of a download: re-enter password + MFA code; returns a single-use token valid for 2 minutes.</summary>
    [HttpPost("{id:guid}/download-token"), HasPermission(Permissions.SystemInfo.Backup)]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> DownloadToken(Guid id, [FromBody] ReauthInput input, CancellationToken ct)
    {
        var job = await db.Set<BackupJob>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("backup");
        if (backups.ArchiveFile(job) is null) throw new BusinessRuleException("backup.notAvailable");
        if (!await auth.ReauthenticateAsync(UserId, input.Password ?? "", input.Code, ct))
        {
            await audit.WriteAsync(AuditAction.Backup, nameof(BackupJob), id.ToString(), null, null, false, "download re-authentication failed", ct);
            throw new AppException("auth.reauthFailed", 403);
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await cache.SetAsync($"backup-dl:{token}", $"{UserId:N}:{id:N}", TimeSpan.FromMinutes(2), ct);
        return Ok(new { token, expiresIn = 120 });
    }

    /// <summary>Step 2: streams the encrypted archive (the passphrase is never handled by the application).</summary>
    [HttpGet("{id:guid}/download"), HasPermission(Permissions.SystemInfo.Backup)]
    public async Task<IActionResult> Download(Guid id, [FromQuery] string token, CancellationToken ct)
    {
        var key = $"backup-dl:{token}";
        var value = await cache.GetAsync<string>(key, ct);
        if (value != $"{UserId:N}:{id:N}") throw new AppException("auth.reauthFailed", 403);
        await cache.RemoveAsync(key, ct);
        var job = await db.Set<BackupJob>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("backup");
        var path = backups.ArchiveFile(job) ?? throw new BusinessRuleException("backup.notAvailable");
        await audit.WriteAsync(AuditAction.Backup, nameof(BackupJob), id.ToString(), null, new { download = job.Location, job.Sha256 }, ct: ct);
        return PhysicalFile(path, "application/octet-stream", job.Location, enableRangeProcessing: true);
    }
}
