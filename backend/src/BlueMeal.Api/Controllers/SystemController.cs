using System.Reflection;
using BlueMeal.Api.Authorization;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers;

/// <summary>System information: version/changelog (NFR-08), public status (maintenance), notifications.</summary>
[Route("api/v1/system")]
public sealed class SystemController(AppDbContext db, ISettingsService settings, TimeProvider clock) : ApiControllerBase
{
    public static object BuildInfo()
    {
        var asm = typeof(SystemController).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        var version = Environment.GetEnvironmentVariable("BLUEMEAL_BUILD_VERSION") ?? info.Split('+')[0];
        var commit = Environment.GetEnvironmentVariable("BLUEMEAL_GIT_COMMIT") ?? (info.Contains('+') ? info.Split('+')[1] : "unknown");
        var buildTime = Environment.GetEnvironmentVariable("BLUEMEAL_BUILD_TIME") is { Length: > 0 } bt && bt != "unknown"
            ? bt : System.IO.File.GetLastWriteTimeUtc(asm.Location).ToString("yyyy-MM-ddTHH:mm:ssZ");
        return new { version, commit = commit.Length > 12 ? commit[..12] : commit, buildTime, runtime = Environment.Version.ToString() };
    }

    /// <summary>Public status used by the login page and the maintenance screen.</summary>
    [HttpGet("status"), AllowAnonymous]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var m = await settings.GetAsync<MaintenanceSettings>(SettingKeys.Maintenance, ct);
        var org = await db.Set<Organization>().AsNoTracking().Select(o => new { o.Name, o.NameEn, o.LogoFileId }).FirstOrDefaultAsync(ct);
        return Ok(new { maintenance = m.Enabled, maintenanceMessageVi = m.MessageVi, maintenanceMessageEn = m.MessageEn, organization = org, serverTime = clock.GetUtcNow() });
    }

    [HttpGet("version"), HasPermission(Permissions.SystemInfo.Version), AllowRestricted]
    public async Task<IActionResult> Version(CancellationToken ct)
    {
        var history = await db.Set<SystemVersion>().AsNoTracking().OrderByDescending(v => v.ReleaseDate).ThenByDescending(v => v.Version)
            .Select(v => new { v.Version, v.ReleaseDate, v.NotesVi, v.NotesEn }).ToListAsync(ct);
        return Ok(new { build = BuildInfo(), history });
    }

    [HttpGet("notifications"), HasPermission(Permissions.Self)]
    public async Task<IActionResult> Notifications([FromQuery] bool unreadOnly = false, [FromQuery] int take = 30, CancellationToken ct = default)
    {
        var q = db.Set<Notification>().AsNoTracking().Where(n => n.UserId == UserId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        var items = await q.OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(take, 1, 100))
            .Select(n => new { n.Id, type = n.Type.ToString(), n.TitleVi, n.TitleEn, n.BodyVi, n.BodyEn, n.Link, n.CreatedAt, n.ReadAt }).ToListAsync(ct);
        var unread = await db.Set<Notification>().CountAsync(n => n.UserId == UserId && n.ReadAt == null, ct);
        return Ok(new { items, unread });
    }

    [HttpPost("notifications/{id:guid}/read"), HasPermission(Permissions.Self)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await db.Set<Notification>().Where(n => n.Id == id && n.UserId == UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAt, clock.GetUtcNow()), ct);
        return NoContent();
    }

    [HttpPost("notifications/read-all"), HasPermission(Permissions.Self)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await db.Set<Notification>().Where(n => n.UserId == UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAt, clock.GetUtcNow()), ct);
        return NoContent();
    }
}
