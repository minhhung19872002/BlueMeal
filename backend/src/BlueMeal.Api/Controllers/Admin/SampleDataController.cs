using BlueMeal.Api.Authorization;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Common;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Admin;

/// <summary>Status and one-click removal of the seeded [DỮ LIỆU MẪU] sample data (ASSUMPTIONS A-14).</summary>
[Route("api/v1/system/sample-data")]
public sealed class SampleDataController(AppDbContext db, ISettingsService settings, IAuditWriter audit, ICacheService cache, TimeProvider clock) : ApiControllerBase
{
    [HttpGet, HasPermission(Permissions.SystemInfo.Setting)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var s = await settings.GetAsync<SampleDataSettings>(SettingKeys.SampleData, ct);
        return Ok(new { s.Loaded, s.Purged, s.PurgedAt, counts = s.Ids.ToDictionary(k => k.Key, k => k.Value.Count) });
    }

    /// <summary>Soft-deletes sample catalog rows (history kept, codes freed for official data); hard-deletes technical rows.</summary>
    [HttpPost("purge"), HasPermission(Permissions.SystemInfo.SampleData)]
    public async Task<IActionResult> Purge(CancellationToken ct)
    {
        var s = await settings.GetAsync<SampleDataSettings>(SettingKeys.SampleData, ct);
        var result = new Dictionary<string, int>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var (typeName, ids) in s.Ids)
        {
            var type = typeof(Entity).Assembly.GetType("BlueMeal.Domain.Entities." + typeName);
            if (type is null || ids.Count == 0) continue;
            var et = db.Model.FindEntityType(type);
            if (et is null) continue;
            var table = $"{et.GetSchema() ?? AppDbContext.Schema}.{et.GetTableName()}";
            var sql = typeof(ISoftDelete).IsAssignableFrom(type)
                ? $"UPDATE {table} SET is_deleted = true, updated_at = now() WHERE id = ANY(@ids) AND is_deleted = false"
                : $"DELETE FROM {table} WHERE id = ANY(@ids)";
            result[typeName] = await db.Database.ExecuteSqlRawAsync(sql, [new Npgsql.NpgsqlParameter("ids", ids.ToArray())], ct);
        }
        await tx.CommitAsync(ct);
        s.Purged = true;
        s.PurgedAt = clock.GetUtcNow();
        await settings.SetAsync(SettingKeys.SampleData, s, ct);
        foreach (var region in new[] { "catalog:food", "catalog:Dish", "catalog:Recommendation" }) await cache.InvalidateRegionAsync(region, ct);
        await audit.WriteAsync(AuditAction.Delete, "SampleData", null, newValues: result, message: "sample data purged", ct: ct);
        return Ok(result);
    }
}
