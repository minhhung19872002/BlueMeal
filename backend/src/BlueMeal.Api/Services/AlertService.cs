using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Services;

public static class AlertCodes
{
    public const string CertExpiry = "W-CERT-EXPIRY";
    public const string StockExpiry = "W-STOCK-EXPIRY";
    public const string StockMin = "W-STOCK-MIN";
    public const string SampleDispose = "W-SAMPLE-DISPOSE";
    public static readonly string[] Daily = [CertExpiry, StockExpiry, StockMin, SampleDispose];
}

public sealed record AlertRunResult(int Created, int Resolved, int Open);

/// <summary>
/// Daily operational alerts (SPEC §6.5, §7.8): supplier certificates and stock lots close to expiry, stock below minimum,
/// food samples due for disposal. Alerts are de-duplicated by key and resolved automatically when the condition disappears.
/// </summary>
public sealed class AlertService(AppDbContext db, ISettingsService settings, INotificationService notifications, TimeProvider clock)
{
    private sealed record Candidate(string Code, WarningLevel Level, string Category, string Vi, string En, string EntityType, Guid EntityId, string Link, string Key, Guid? WarehouseId = null);

    public async Task<AlertRunResult> RunDailyAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var ws = await settings.GetAsync<WarningSettings>(SettingKeys.Warning, ct);
        var candidates = new List<Candidate>();

        // supplier certificates (food safety facility, health check, …)
        var certLimit = today.AddDays(ws.CertExpiryWarnDays);
        var certs = await db.Set<PartnerCertificate>().AsNoTracking().Where(c => c.ExpiryDate != null && c.ExpiryDate <= certLimit)
            .Select(c => new { c.Id, c.PartnerId, c.Number, c.CertificateType, c.ExpiryDate, partner = c.Partner!.NameVi, active = c.Partner.IsActive }).ToListAsync(ct);
        foreach (var c in certs.Where(c => c.active))
        {
            var expired = c.ExpiryDate < today;
            var d = c.ExpiryDate!.Value;
            candidates.Add(new Candidate(AlertCodes.CertExpiry, expired ? WarningLevel.Critical : WarningLevel.Warning, "Certificate",
                expired ? $"Giấy chứng nhận {c.Number} của {c.partner} đã hết hạn ngày {d:dd/MM/yyyy}" : $"Giấy chứng nhận {c.Number} của {c.partner} sẽ hết hạn ngày {d:dd/MM/yyyy}",
                expired ? $"Certificate {c.Number} of {c.partner} expired on {d:dd/MM/yyyy}" : $"Certificate {c.Number} of {c.partner} expires on {d:dd/MM/yyyy}",
                nameof(PartnerCertificate), c.Id, $"/catalog/partners?id={c.PartnerId}", $"{AlertCodes.CertExpiry}:{c.Id}:{(expired ? "x" : "w")}"));
        }

        // lots close to expiry
        var lotLimit = today.AddDays(ws.StockExpiryWarnDays);
        var lots = await db.Set<StockLot>().AsNoTracking().Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate <= lotLimit)
            .Select(l => new { l.Id, l.WarehouseId, l.LotNo, l.ExpiryDate, l.Quantity, l.IngredientId, ingredient = l.Ingredient!.NameVi, unit = l.Ingredient.PurchaseUnit!.Symbol }).ToListAsync(ct);
        foreach (var l in lots)
        {
            var expired = l.ExpiryDate < today;
            var d = l.ExpiryDate!.Value;
            var lot = l.LotNo is null ? "" : $" (lô/lot {l.LotNo})";
            candidates.Add(new Candidate(AlertCodes.StockExpiry, expired ? WarningLevel.Critical : WarningLevel.Warning, "Stock",
                $"{l.ingredient}{lot}: còn {l.Quantity:0.###} {l.unit}, {(expired ? "đã hết hạn" : "hết hạn")} ngày {d:dd/MM/yyyy}",
                $"{l.ingredient}{lot}: {l.Quantity:0.###} {l.unit} left, {(expired ? "expired on" : "expires on")} {d:dd/MM/yyyy}",
                nameof(StockLot), l.Id, $"/stock/balance?ingredientId={l.IngredientId}", $"{AlertCodes.StockExpiry}:{l.Id}:{(expired ? "x" : "w")}", l.WarehouseId));
        }

        // stock below minimum (all warehouses together)
        var low = await (from i in db.Set<Ingredient>().AsNoTracking()
                         where i.IsActive && i.MinStock > 0
                         let qty = db.Set<StockBalance>().Where(b => b.IngredientId == i.Id).Sum(b => (decimal?)b.Quantity) ?? 0
                         where qty < i.MinStock
                         select new { i.Id, i.NameVi, i.MinStock, qty, unit = i.PurchaseUnit!.Symbol }).ToListAsync(ct);
        foreach (var i in low)
            candidates.Add(new Candidate(AlertCodes.StockMin, i.qty <= 0 ? WarningLevel.Critical : WarningLevel.Warning, "Stock",
                $"{i.NameVi}: tồn {i.qty:0.###} {i.unit} dưới mức tối thiểu {i.MinStock:0.###}", $"{i.NameVi}: stock {i.qty:0.###} {i.unit} below minimum {i.MinStock:0.###}",
                nameof(Ingredient), i.Id, $"/stock/balance?ingredientId={i.Id}", $"{AlertCodes.StockMin}:{i.Id}"));

        // food samples due for disposal
        var samples = await db.Set<FoodSample>().AsNoTracking().Where(s => s.DisposedAt == null && s.PlannedDisposeAt <= now)
            .Select(s => new { s.Id, s.Code, s.PlannedDisposeAt, dish = s.Dish!.NameVi }).ToListAsync(ct);
        foreach (var s in samples)
        {
            var at = s.PlannedDisposeAt.ToOffset(TimeSpan.FromHours(7));
            candidates.Add(new Candidate(AlertCodes.SampleDispose, now - s.PlannedDisposeAt > TimeSpan.FromHours(24) ? WarningLevel.Warning : WarningLevel.Info, "Sample",
                $"Mẫu {s.Code} ({s.dish}) đến hạn hủy lúc {at:HH:mm dd/MM/yyyy}", $"Sample {s.Code} ({s.dish}) due for disposal at {at:HH:mm dd/MM/yyyy}",
                nameof(FoodSample), s.Id, $"/safety/samples?id={s.Id}", $"{AlertCodes.SampleDispose}:{s.Id}"));
        }

        var open = await db.Set<Alert>().Where(a => AlertCodes.Daily.Contains(a.Code) && a.Status != AlertStatus.Resolved).ToListAsync(ct);
        var keys = candidates.Select(c => c.Key).ToHashSet();
        var resolved = 0;
        foreach (var a in open.Where(a => !keys.Contains(a.DedupeKey)))
        {
            a.Status = AlertStatus.Resolved;
            a.ResolvedAt = now;
            a.ResolutionNote = "auto";
            resolved++;
        }
        var openKeys = open.Where(a => a.Status != AlertStatus.Resolved).Select(a => a.DedupeKey).ToHashSet();
        var created = candidates.Where(c => !openKeys.Contains(c.Key)).Select(c => new Alert
        {
            Code = c.Code, Level = c.Level, Category = c.Category, MessageVi = c.Vi, MessageEn = c.En, EntityType = c.EntityType, EntityId = c.EntityId, Link = c.Link,
            DedupeKey = c.Key, CreatedAt = now, WarehouseId = c.WarehouseId,
        }).ToList();
        db.AddRange(created);
        await db.SaveChangesAsync(ct);
        if (created.Count > 0)
            await notifications.NotifyPermissionAsync(Permissions.Alert.View, NotificationType.Alert, "notify.alert",
                [$"{created.Count} cảnh báo mới / new alerts ({string.Join(", ", created.GroupBy(c => c.Code).Select(g => $"{g.Key}: {g.Count()}"))})"], "/alerts", ct);
        return new AlertRunResult(created.Count, resolved, candidates.Count);
    }
}

/// <summary>Recurring job (06:00 Asia/Ho_Chi_Minh by default) — see JobNames.DailyAlerts.</summary>
public sealed class DailyAlertsJob(AlertService alerts) : IAppJob
{
    public string Name => JobNames.DailyAlerts;
    public Task ExecuteAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct) => alerts.RunDailyAsync(ct);
}
