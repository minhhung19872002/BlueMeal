using System.Text.Json;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Common;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueMeal.Api.Services;

/// <summary>Manifest written by the backup sidecar (deploy/backup/backup.sh) for every run.</summary>
public sealed record BackupManifest(string Id, string Type, string Status, string? File, long? Size, string? Sha256, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Error);

/// <summary>
/// Backups (SPEC §10.7): the sidecar container performs pg_dump + files, encryption and retention. The application requests
/// manual backups through <c>/backups/requests/&lt;id&gt;.req</c>, imports manifests into <c>backup_job</c> for the history
/// screen and serves downloads after re-authentication.
/// </summary>
public sealed class BackupService(AppDbContext db, IConfiguration config, ICurrentUser user, IBackgroundJobs jobs, TimeProvider clock, ILogger<BackupService> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string Root => config["Storage:BackupPath"] ?? "/backups";
    private string Requests => Path.Combine(Root, "requests");
    private string Manifests => Path.Combine(Root, "manifests");
    public string Archive => Path.Combine(Root, "archive");

    public async Task<BackupJob> RequestManualAsync(string? note, CancellationToken ct)
    {
        if (await db.Set<BackupJob>().AnyAsync(b => b.Type == BackupType.Manual && (b.Status == JobStatus.Queued || b.Status == JobStatus.Running), ct))
            throw new ConflictException("backup.inProgress");
        var job = new BackupJob { Type = BackupType.Manual, RequestedAt = clock.GetUtcNow(), RequestedBy = user.UserId, Note = note };
        db.Add(job);
        await db.SaveChangesAsync(ct);
        await jobs.EnqueueAsync(JobNames.Backup, new Dictionary<string, string> { ["id"] = job.Id.ToString() }, ct);
        return job;
    }

    private BackupManifest? ReadManifest(string path)
    {
        try { return JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(path), Json); }
        catch (Exception ex) when (ex is IOException or JsonException) { log.LogWarning(ex, "Unreadable backup manifest {Path}", path); return null; }
    }

    private static void Apply(BackupJob job, BackupManifest m)
    {
        job.Status = m.Status == "Succeeded" ? JobStatus.Succeeded : JobStatus.Failed;
        job.StartedAt = m.StartedAt?.ToUniversalTime() ?? job.StartedAt;
        job.FinishedAt = m.FinishedAt?.ToUniversalTime();
        job.SizeBytes = m.Size;
        job.Sha256 = m.Sha256;
        job.Location = m.File;
        job.Error = m.Error;
    }

    /// <summary>Runs on the worker: hands the request to the sidecar and waits for its manifest.</summary>
    public async Task RunManualAsync(Guid id, TimeSpan timeout, CancellationToken ct)
    {
        var job = await db.Set<BackupJob>().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (job is null || job.Status != JobStatus.Queued) return;
        job.Status = JobStatus.Running;
        job.StartedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        try
        {
            Directory.CreateDirectory(Requests);
            await File.WriteAllTextAsync(Path.Combine(Requests, $"{id}.req"), JsonSerializer.Serialize(new { id, requestedBy = job.RequestedBy, at = job.RequestedAt }, Json), ct);
            var manifest = Path.Combine(Manifests, $"{id}.json");
            var deadline = clock.GetUtcNow() + timeout;
            while (!File.Exists(manifest))
            {
                if (clock.GetUtcNow() > deadline) throw new TimeoutException("backup sidecar did not answer");
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            await Task.Delay(200, ct); // let the writer finish the file
            if (ReadManifest(manifest) is { } m) Apply(job, m);
            else { job.Status = JobStatus.Failed; job.Error = "manifest unreadable"; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
            job.FinishedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Imports manifests of scheduled backups (written by cron in the sidecar) into the history.</summary>
    public async Task ImportScheduledAsync(CancellationToken ct)
    {
        if (!Directory.Exists(Manifests)) return;
        var known = (await db.Set<BackupJob>().AsNoTracking().Where(b => b.Location != null).Select(b => b.Location!).ToListAsync(ct)).ToHashSet();
        foreach (var path in Directory.EnumerateFiles(Manifests, "sched-*.json"))
        {
            if (ReadManifest(path) is not { } m || (m.File is { } f && known.Contains(f))) continue;
            var job = new BackupJob { Type = BackupType.Scheduled, RequestedAt = m.StartedAt?.ToUniversalTime() ?? clock.GetUtcNow() };
            Apply(job, m);
            db.Add(job);
            if (m.File is { } file) known.Add(file);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Archive path of a succeeded backup that still exists (retention may have removed it).</summary>
    public string? ArchiveFile(BackupJob job)
    {
        if (job.Status != JobStatus.Succeeded || string.IsNullOrEmpty(job.Location) || job.Location.Contains('/') || job.Location.Contains('\\') || job.Location.Contains("..")) return null;
        var path = Path.Combine(Archive, job.Location);
        return File.Exists(path) ? path : null;
    }
}

/// <summary>Quartz job for a manual ("bất thường") backup.</summary>
public sealed class BackupRequestJob(BackupService backups, IConfiguration config) : IAppJob
{
    public string Name => JobNames.Backup;
    public Task ExecuteAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct)
        => data.TryGetValue("id", out var id) && Guid.TryParse(id, out var g)
            ? backups.RunManualAsync(g, TimeSpan.FromMinutes(config.GetValue("Backup:TimeoutMinutes", 60)), ct)
            : backups.ImportScheduledAsync(ct);
}

/// <summary>
/// Nightly housekeeping (03:30): expired export files, old notifications and one-time tokens, audit partitions
/// (create ahead / drop beyond the retention period with the owner connection — the app role cannot delete audit rows),
/// import of scheduled backup manifests.
/// </summary>
public sealed class HousekeepingJob(AppDbContext db, IFileStorage files, ISettingsService settings, BackupService backups, IConfiguration config, TimeProvider clock, ILogger<HousekeepingJob> log) : IAppJob
{
    public string Name => JobNames.Housekeeping;

    public async Task ExecuteAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var expired = await db.Set<ExportJob>().Where(j => j.Status == JobStatus.Succeeded && j.ExpiresAt < now).ToListAsync(ct);
        foreach (var j in expired)
        {
            if (j.FilePath is { } p) await files.DeleteAsync(p, ct);
            j.Status = JobStatus.Expired;
            j.FilePath = null;
        }
        await db.SaveChangesAsync(ct);
        var notificationsRemoved = await db.Set<Notification>().Where(n => n.CreatedAt < now.AddDays(-180)).ExecuteDeleteAsync(ct);
        var tokensRemoved = await db.Set<OneTimeToken>().Where(t => t.ExpiresAt < now.AddDays(-7)).ExecuteDeleteAsync(ct);

        var audit = await settings.GetAsync<AuditSettings>(SettingKeys.Audit, ct);
        var dropped = 0;
        if (config.GetConnectionString("Migration") is { Length: > 0 } owner && audit.RetentionMonths >= 12)
        {
            await using var conn = new NpgsqlConnection(owner);
            await conn.OpenAsync(ct);
            await using (var c1 = new NpgsqlCommand("SELECT app.ensure_audit_partitions(3)", conn)) await c1.ExecuteNonQueryAsync(ct);
            await using var c2 = new NpgsqlCommand("SELECT app.drop_old_audit_partitions(@m)", conn);
            c2.Parameters.AddWithValue("m", audit.RetentionMonths);
            dropped = Convert.ToInt32(await c2.ExecuteScalarAsync(ct));
        }
        await backups.ImportScheduledAsync(ct);
        log.LogInformation("Housekeeping: {Exports} export files expired, {Notifications} notifications and {Tokens} tokens removed, {Partitions} audit partitions dropped",
            expired.Count, notificationsRemoved, tokensRemoved, dropped);
    }
}
