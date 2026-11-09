using BlueMeal.Api.Authorization;
using BlueMeal.Api.Infrastructure;
using BlueMeal.Api.Services;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Stock;

/// <summary>Stock balances (current or at a date), stock card, lots and period locks — SPEC §7.8.</summary>
[Route("api/v1/stock")]
public sealed class StockController(AppDbContext db, IAuditWriter audit, TimeProvider clock) : StockControllerBase(db)
{
    /// <summary>
    /// Balance per warehouse × ingredient. Without <paramref name="date"/> the materialized balance is used; with a date it is
    /// summed from the ledger (documents dated up to that day), so back-dated documents are taken into account.
    /// </summary>
    [HttpGet("balances"), HasPermission(Permissions.Stock.View)]
    public async Task<IActionResult> Balances([FromQuery(Name = "")] PageQuery q, [FromQuery] Guid? warehouseId, [FromQuery] DateOnly? date, [FromQuery] bool? belowMin,
        [FromQuery] bool includeZero = false, CancellationToken ct = default)
    {
        if (warehouseId is { } w) EnsureWarehouse(w);
        IQueryable<BalanceRow> rows;
        if (date is { } d)
        {
            rows = ScopeWarehouse(Db.Set<StockLedger>().AsNoTracking(), l => l.WarehouseId).Where(l => l.DocDate <= d && (warehouseId == null || l.WarehouseId == warehouseId))
                .GroupBy(l => new { l.WarehouseId, l.IngredientId })
                .Select(g => new BalanceRow { WarehouseId = g.Key.WarehouseId, IngredientId = g.Key.IngredientId, Quantity = g.Sum(x => x.Quantity), Value = g.Sum(x => x.Amount) });
        }
        else
        {
            rows = ScopeWarehouse(Db.Set<StockBalance>().AsNoTracking(), b => b.WarehouseId).Where(b => warehouseId == null || b.WarehouseId == warehouseId)
                .Select(b => new BalanceRow { WarehouseId = b.WarehouseId, IngredientId = b.IngredientId, Quantity = b.Quantity, Value = b.Value });
        }
        if (!includeZero) rows = rows.Where(r => r.Quantity != 0);
        var joined = from r in rows
                     join i in Db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters() on r.IngredientId equals i.Id
                     join wh in Db.Set<Warehouse>().AsNoTracking().IgnoreQueryFilters() on r.WarehouseId equals wh.Id
                     select new { r, i, wh };
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = "%" + TextNormalizerProxy.Normalize(q.Q) + "%";
            joined = joined.Where(x => EF.Functions.ILike(x.i.SearchText!, term));
        }
        if (belowMin == true) joined = joined.Where(x => x.i.MinStock > 0 && x.r.Quantity < x.i.MinStock);
        var total = await joined.LongCountAsync(ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
        var page = await joined.OrderBy(x => x.i.Code).ThenBy(x => x.wh.Code).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(x => new
        {
            x.r.WarehouseId, warehouse = x.wh.NameVi, x.r.IngredientId, ingredientCode = x.i.Code, ingredientName = x.i.NameVi, ingredientNameEn = x.i.NameEn, unit = x.i.PurchaseUnit!.Symbol,
            x.r.Quantity, x.r.Value, averageCost = x.r.Quantity > 0 ? x.r.Value / x.r.Quantity : 0, x.i.MinStock, belowMin = x.i.MinStock > 0 && x.r.Quantity < x.i.MinStock,
            nearestExpiry = Db.Set<StockLot>().Where(l => l.WarehouseId == x.r.WarehouseId && l.IngredientId == x.r.IngredientId && l.Quantity > 0 && l.ExpiryDate != null).Min(l => l.ExpiryDate),
        }).ToListAsync(ct);
        var totalValue = await joined.SumAsync(x => x.r.Value, ct);
        return Ok(new { items = page, q.Page, q.PageSize, totalItems = total, totalValue, today });
    }

    public sealed class BalanceRow
    {
        public Guid WarehouseId { get; set; }
        public Guid IngredientId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Value { get; set; }
    }

    /// <summary>Stock card (thẻ kho): opening balance, movements with running balance, closing balance.</summary>
    [HttpGet("card"), HasPermission(Permissions.Stock.View)]
    public async Task<IActionResult> Card([FromQuery] Guid ingredientId, [FromQuery] Guid? warehouseId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 400) throw new AppException("error.validation");
        if (warehouseId is { } w) EnsureWarehouse(w);
        var ledger = ScopeWarehouse(Db.Set<StockLedger>().AsNoTracking(), l => l.WarehouseId).Where(l => l.IngredientId == ingredientId && (warehouseId == null || l.WarehouseId == warehouseId));
        var opening = await ledger.Where(l => l.DocDate < from).GroupBy(_ => 1).Select(g => new { Qty = g.Sum(x => x.Quantity), Value = g.Sum(x => x.Amount) }).FirstOrDefaultAsync(ct);
        var moves = await ledger.Where(l => l.DocDate >= from && l.DocDate <= to).OrderBy(l => l.DocDate).ThenBy(l => l.At).ThenBy(l => l.Id).ToListAsync(ct);
        var lotIds = moves.Where(m => m.LotId != null).Select(m => m.LotId!.Value).Distinct().ToList();
        var lots = await Db.Set<StockLot>().AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => new { l.LotNo, l.ExpiryDate }, ct);
        var ing = (await IngredientsAsync([ingredientId], ct)).GetValueOrDefault(ingredientId);
        decimal qty = opening?.Qty ?? 0, value = opening?.Value ?? 0;
        var rows = moves.Select(m =>
        {
            qty += m.Quantity; value += m.Amount;
            var lot = m.LotId is { } lid ? lots.GetValueOrDefault(lid) : null;
            return new
            {
                m.Id, m.DocDate, m.DocType, m.DocId, m.DocNo, type = m.Type.ToString(), lotNo = lot?.LotNo, expiryDate = lot?.ExpiryDate, inQty = m.Quantity > 0 ? m.Quantity : 0,
                outQty = m.Quantity < 0 ? -m.Quantity : 0, m.UnitCost, amount = m.Amount, balanceQty = qty, balanceValue = value,
            };
        }).ToList();
        return Ok(new
        {
            ingredient = new { id = ingredientId, code = ing.Code, name = ing.Name, nameEn = ing.NameEn, unit = ing.Unit }, from, to,
            opening = new { quantity = opening?.Qty ?? 0, value = opening?.Value ?? 0 }, rows,
            totals = new { inQty = rows.Sum(r => r.inQty), outQty = rows.Sum(r => r.outQty) }, closing = new { quantity = qty, value },
        });
    }

    /// <summary>Lots with remaining quantity; <paramref name="expiringDays"/> limits to lots expiring within N days (or already expired).</summary>
    [HttpGet("lots"), HasPermission(Permissions.Stock.View)]
    public async Task<IActionResult> Lots([FromQuery] Guid? warehouseId, [FromQuery] Guid? ingredientId, [FromQuery] int? expiringDays, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
        var q = ScopeWarehouse(Db.Set<StockLot>().AsNoTracking(), l => l.WarehouseId).Where(l => l.Quantity > 0);
        if (warehouseId is not null) q = q.Where(l => l.WarehouseId == warehouseId);
        if (ingredientId is not null) q = q.Where(l => l.IngredientId == ingredientId);
        if (expiringDays is { } days) { var limit = today.AddDays(days); q = q.Where(l => l.ExpiryDate != null && l.ExpiryDate <= limit); }
        var rows = await q.OrderBy(l => l.ExpiryDate ?? DateOnly.MaxValue).ThenBy(l => l.ReceivedDate).Take(500).Select(l => new
        {
            l.Id, l.WarehouseId, warehouse = Db.Set<Warehouse>().Where(w => w.Id == l.WarehouseId).Select(w => w.NameVi).FirstOrDefault(), l.IngredientId, ingredientCode = l.Ingredient!.Code,
            ingredientName = l.Ingredient.NameVi, ingredientNameEn = l.Ingredient.NameEn, unit = l.Ingredient.PurchaseUnit!.Symbol, l.LotNo, l.MfgDate, l.ExpiryDate, l.ReceivedDate, l.InitialQty, l.Quantity, l.UnitCost,
            daysLeft = l.ExpiryDate == null ? (int?)null : l.ExpiryDate.Value.DayNumber - today.DayNumber,
        }).ToListAsync(ct);
        return Ok(rows);
    }

    // ------------------------------------------------------------------ period locks (khóa sổ)

    public sealed record LockInput(Guid? WarehouseId, DateOnly Period);

    [HttpGet("period-locks"), HasPermission(Permissions.Stock.View)]
    public async Task<IActionResult> PeriodLocks(CancellationToken ct)
        => Ok(await Db.Set<StockPeriodLock>().AsNoTracking().OrderByDescending(l => l.Period).Select(l => new
        {
            l.Id, l.WarehouseId, warehouse = Db.Set<Warehouse>().Where(w => w.Id == l.WarehouseId).Select(w => w.NameVi).FirstOrDefault(), l.Period, l.LockedAt,
            lockedBy = Db.Set<AppUser>().Where(u => u.Id == l.LockedBy).Select(u => u.UserName).FirstOrDefault(),
        }).ToListAsync(ct));

    /// <summary>Locks a month (and implicitly the earlier ones, A-39). Draft documents in the period must be posted or deleted first.</summary>
    [HttpPost("period-locks"), HasPermission(Permissions.Stock.Lock)]
    public async Task<IActionResult> Lock([FromBody] LockInput input, CancellationToken ct)
    {
        var period = new DateOnly(input.Period.Year, input.Period.Month, 1);
        var end = period.AddMonths(1);
        if (input.WarehouseId is { } w) EnsureWarehouse(w);
        var drafts = await Db.Set<StockReceipt>().CountAsync(r => r.Status == StockDocStatus.Draft && r.DocDate < end && (input.WarehouseId == null || r.WarehouseId == input.WarehouseId), ct)
                   + await Db.Set<StockIssue>().CountAsync(r => r.Status == StockDocStatus.Draft && !r.IsPlanned && r.DocDate < end && (input.WarehouseId == null || r.WarehouseId == input.WarehouseId), ct);
        if (drafts > 0) throw new BusinessRuleException("stock.draftsInPeriod", drafts);
        if (await Db.Set<StockPeriodLock>().AnyAsync(l => l.WarehouseId == input.WarehouseId && l.Period == period, ct)) throw new ConflictException("error.duplicateCode", $"{period:MM/yyyy}");
        var l = new StockPeriodLock { WarehouseId = input.WarehouseId, Period = period, LockedAt = clock.GetUtcNow(), LockedBy = CurrentUser.UserId };
        Db.Add(l);
        await Db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.Update, nameof(StockPeriodLock), l.Id.ToString(), null, new { input.WarehouseId, period }, ct: ct);
        return Ok(new { l.Id });
    }

    [HttpDelete("period-locks/{id:guid}"), HasPermission(Permissions.Stock.Lock)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct)
    {
        var l = await Db.Set<StockPeriodLock>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("lock");
        Db.Remove(l);
        await Db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.Delete, nameof(StockPeriodLock), id.ToString(), new { l.WarehouseId, l.Period }, null, ct: ct);
        return NoContent();
    }
}

/// <summary>Thin indirection so the controller does not depend on Domain internals for search text normalization.</summary>
internal static class TextNormalizerProxy
{
    public static string Normalize(string s) => Domain.Common.TextNormalizer.Normalize(s);
}

// ---------------------------------------------------------------------------------------------- purchase requests

public sealed record GeneratePurchaseInput(DateOnly From, DateOnly To, Guid? WarehouseId, IReadOnlyList<Guid>? MenuIds, string? Note);
public sealed record PurchaseLineInput(Guid Id, decimal? OrderQty, decimal? EstimatedPrice, Guid? PartnerId);
public sealed record PurchaseUpdateInput(DateOnly? NeedDate, string? Note, IReadOnlyList<PurchaseLineInput> Lines, uint? Version);

public sealed class GeneratePurchaseInputValidator : AbstractValidator<GeneratePurchaseInput>
{
    public GeneratePurchaseInputValidator() => RuleFor(x => x).Must(x => x.To >= x.From && x.To.DayNumber - x.From.DayNumber <= 62).WithMessage("validation.dateRange");
}

/// <summary>Purchase requests (dự trù / đề xuất mua) generated from approved menus — SPEC §5.5, §6.5.</summary>
[Route("api/v1/purchase-requests")]
public sealed class PurchaseRequestsController(AppDbContext db, InventoryService inventory) : StockControllerBase(db)
{
    [HttpGet, HasPermission(Permissions.PurchaseRequest.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] PurchaseRequestStatus? status, CancellationToken ct)
    {
        var query = Db.Set<PurchaseRequest>().AsNoTracking();
        if (status is not null) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(q.Q)) query = query.Where(r => EF.Functions.ILike(r.DocNo, "%" + q.Q + "%"));
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(r => r.DocDate).ThenByDescending(r => r.DocNo).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(r => new
        {
            r.Id, r.DocNo, r.DocDate, r.NeedDate, r.WarehouseId, status = r.Status.ToString(), r.EstimatedAmount, lineCount = r.Lines.Count, menuCount = r.MenuIds == null ? 0 : (r.MenuIds.Length + 1) / 37, r.Note, r.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    private async Task<PurchaseRequest> LoadAsync(Guid id, CancellationToken ct)
        => await Db.Set<PurchaseRequest>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("purchase request");

    [HttpGet("{id:guid}"), HasPermission(Permissions.PurchaseRequest.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        var ing = await IngredientsAsync(r.Lines.Select(l => l.IngredientId), ct);
        var partnerIds = r.Lines.Where(l => l.PartnerId != null).Select(l => l.PartnerId!.Value).Distinct().ToList();
        var partners = await Db.Set<Partner>().AsNoTracking().IgnoreQueryFilters().Where(p => partnerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameVi, ct);
        var menuIds = (r.MenuIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
        var menus = await Db.Set<Menu>().AsNoTracking().Where(m => menuIds.Contains(m.Id)).OrderBy(m => m.MenuDate).Select(m => new { m.Id, m.Code, m.MenuDate, m.Servings }).ToListAsync(ct);
        var receipts = await Db.Set<StockReceipt>().AsNoTracking().Where(x => x.PurchaseRequestId == id).Select(x => new { x.Id, x.DocNo, status = x.Status.ToString() }).ToListAsync(ct);
        return Ok(new
        {
            r.Id, r.DocNo, r.DocDate, r.NeedDate, r.WarehouseId, status = r.Status.ToString(), r.EstimatedAmount, r.Note, r.Version, menus, receipts,
            lines = r.Lines.OrderBy(l => ing.GetValueOrDefault(l.IngredientId).Code).Select(l => new
            {
                l.Id, l.IngredientId, ingredientCode = ing.GetValueOrDefault(l.IngredientId).Code, ingredientName = ing.GetValueOrDefault(l.IngredientId).Name,
                ingredientNameEn = ing.GetValueOrDefault(l.IngredientId).NameEn, unit = ing.GetValueOrDefault(l.IngredientId).Unit, l.RequiredQty, l.AvailableQty, l.MinStock, l.SuggestedQty, l.OrderQty,
                l.EstimatedPrice, amount = Math.Round((l.OrderQty ?? 0) * (l.EstimatedPrice ?? 0), 0), l.PartnerId, partner = l.PartnerId is { } p ? partners.GetValueOrDefault(p) : null,
            }),
        });
    }

    [HttpPost("generate"), HasPermission(Permissions.PurchaseRequest.Create), Idempotent]
    public async Task<IActionResult> Generate([FromBody] GeneratePurchaseInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        if (input.WarehouseId is { } w) EnsureWarehouse(w);
        var pr = await inventory.CreatePurchaseRequestAsync(input.From, input.To, input.WarehouseId, input.MenuIds, input.Note, ct);
        return Ok(new { pr.Id, pr.DocNo, lineCount = pr.Lines.Count, pr.EstimatedAmount });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.PurchaseRequest.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] PurchaseUpdateInput input, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        if (r.Status is not (PurchaseRequestStatus.Draft or PurchaseRequestStatus.Submitted)) throw new BusinessRuleException("stock.notDraft");
        SetVersion(r, input.Version);
        r.NeedDate = input.NeedDate; r.Note = input.Note;
        foreach (var l in input.Lines)
        {
            var line = r.Lines.FirstOrDefault(x => x.Id == l.Id) ?? throw new NotFoundException("line");
            if (l.OrderQty is < 0 || l.EstimatedPrice is < 0) throw new AppException("error.validation");
            line.OrderQty = l.OrderQty; line.EstimatedPrice = l.EstimatedPrice; line.PartnerId = l.PartnerId;
        }
        r.EstimatedAmount = Math.Round(r.Lines.Sum(l => (l.OrderQty ?? 0) * (l.EstimatedPrice ?? 0)), 0);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<IActionResult> Transition(Guid id, PurchaseRequestStatus[] from, PurchaseRequestStatus to, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        if (!from.Contains(r.Status)) throw new BusinessRuleException("menu.invalidTransition", r.Status.ToString(), to.ToString());
        r.Status = to;
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit"), HasPermission(Permissions.PurchaseRequest.Edit)]
    public Task<IActionResult> Submit(Guid id, CancellationToken ct) => Transition(id, [PurchaseRequestStatus.Draft], PurchaseRequestStatus.Submitted, ct);

    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.PurchaseRequest.Approve)]
    public Task<IActionResult> Approve(Guid id, CancellationToken ct) => Transition(id, [PurchaseRequestStatus.Draft, PurchaseRequestStatus.Submitted], PurchaseRequestStatus.Approved, ct);

    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.PurchaseRequest.Edit)]
    public Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Transition(id, [PurchaseRequestStatus.Draft, PurchaseRequestStatus.Submitted, PurchaseRequestStatus.Approved], PurchaseRequestStatus.Cancelled, ct);

    /// <summary>Creates draft receipts (one per supplier) from an approved request.</summary>
    [HttpPost("{id:guid}/receipts"), HasPermission(Permissions.StockReceipt.Create), Idempotent]
    public async Task<IActionResult> CreateReceipts(Guid id, [FromQuery] Guid? warehouseId, CancellationToken ct)
    {
        if (warehouseId is { } w) EnsureWarehouse(w);
        var r = await LoadAsync(id, ct);
        var receipts = await inventory.ReceiptsFromRequestAsync(r, warehouseId, ct);
        return Ok(receipts.Select(x => new { x.Id, x.DocNo, lineCount = x.Lines.Count }));
    }
}
