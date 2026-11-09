using BlueMeal.Api.Authorization;
using BlueMeal.Api.Services;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Analysis;

/// <summary>Alert center (SPEC §7.6): list, acknowledge/resolve, run the daily checks on demand.</summary>
[Route("api/v1/alerts")]
public sealed class AlertsController(AppDbContext db, AlertService alerts, TimeProvider clock) : ApiControllerBase
{
    [HttpGet, HasPermission(Permissions.Alert.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] AlertStatus? status, [FromQuery] WarningLevel? level, [FromQuery] string? code,
        [FromQuery] string? category, CancellationToken ct)
    {
        var query = db.Set<Alert>().AsNoTracking();
        if (CurrentUser.WarehouseScope is { } ws) query = query.Where(a => a.WarehouseId == null || ws.Contains(a.WarehouseId.Value));
        if (CurrentUser.CustomerScope is { } cs) query = query.Where(a => a.CustomerId == null || cs.Contains(a.CustomerId.Value));
        query = status is null ? query.Where(a => a.Status != AlertStatus.Resolved) : query.Where(a => a.Status == status);
        if (level is not null) query = query.Where(a => a.Level == level);
        if (!string.IsNullOrWhiteSpace(code)) query = query.Where(a => a.Code == code);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category == category);
        if (!string.IsNullOrWhiteSpace(q.Q)) query = query.Where(a => EF.Functions.ILike(EF.Functions.Unaccent(a.MessageVi), EF.Functions.Unaccent("%" + q.Q + "%")) || EF.Functions.ILike(a.MessageEn, "%" + q.Q + "%"));
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(a => a.Level).ThenByDescending(a => a.CreatedAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(a => new
        {
            a.Id, a.Code, level = a.Level.ToString(), a.Category, a.MessageVi, a.MessageEn, a.EntityType, a.EntityId, a.Link, status = a.Status.ToString(), a.CreatedAt, a.ResolvedAt, a.ResolutionNote,
        }).ToListAsync(ct);
        var counts = await query.GroupBy(a => a.Level).Select(g => new { level = g.Key.ToString(), count = g.Count() }).ToListAsync(ct);
        return Ok(new { items = rows, q.Page, q.PageSize, totalItems = total, counts });
    }

    public sealed record ResolveInput(string? Note, bool AcknowledgeOnly);

    [HttpPost("{id:guid}/resolve"), HasPermission(Permissions.Alert.Resolve)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveInput input, CancellationToken ct)
    {
        var a = await db.Set<Alert>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("alert");
        a.Status = input.AcknowledgeOnly ? AlertStatus.Acknowledged : AlertStatus.Resolved;
        if (!input.AcknowledgeOnly) { a.ResolvedAt = clock.GetUtcNow(); a.ResolvedBy = CurrentUser.UserId; }
        a.ResolutionNote = input.Note;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Runs the daily checks now (normally scheduled at 06:00).</summary>
    [HttpPost("run-daily"), HasPermission(Permissions.Alert.Config)]
    public async Task<IActionResult> RunDaily(CancellationToken ct) => Ok(await alerts.RunDailyAsync(ct));
}
