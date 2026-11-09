using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Common;
using BlueMeal.Application.Engines;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Services;

/// <summary>
/// Applies <see cref="InventoryEngine"/> results to the database (SPEC §6.5): every posting writes the immutable ledger, the
/// materialized balance and lot quantities in one transaction, serialized per warehouse with a PostgreSQL advisory lock.
/// </summary>
public sealed class InventoryService(AppDbContext db, ISettingsService settings, ICurrentUser user, IDocNumberGenerator docs, TimeProvider clock)
{
    public const string DocReceipt = "Receipt";
    public const string DocIssue = "Issue";

    private DateTimeOffset Now => clock.GetUtcNow();
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);

    // ------------------------------------------------------------------ guards

    public async Task EnsureOpenPeriodAsync(Guid warehouseId, DateOnly date, CancellationToken ct)
    {
        var locks = await db.Set<StockPeriodLock>().AsNoTracking().Where(l => l.WarehouseId == null || l.WarehouseId == warehouseId).Select(l => l.Period).ToListAsync(ct);
        if (InventoryEngine.IsLocked(date, locks)) throw new BusinessRuleException("stock.periodLocked", $"{date:MM/yyyy}");
    }

    private async Task LockWarehouseAsync(Guid warehouseId, CancellationToken ct)
    {
        var bytes = warehouseId.ToByteArray();
        var key = BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [key], ct);
    }

    private async Task<StockBalance> BalanceAsync(Guid warehouseId, Guid ingredientId, CancellationToken ct)
    {
        var b = db.Set<StockBalance>().Local.FirstOrDefault(x => x.WarehouseId == warehouseId && x.IngredientId == ingredientId)
                ?? await db.Set<StockBalance>().FirstOrDefaultAsync(x => x.WarehouseId == warehouseId && x.IngredientId == ingredientId, ct);
        if (b is null)
        {
            b = new StockBalance { WarehouseId = warehouseId, IngredientId = ingredientId, UpdatedAt = Now };
            db.Add(b);
        }
        return b;
    }

    private async Task<List<StockLot>> LotsAsync(Guid warehouseId, Guid ingredientId, CancellationToken ct)
    {
        var tracked = await db.Set<StockLot>().Where(l => l.WarehouseId == warehouseId && l.IngredientId == ingredientId && l.Quantity > 0).ToListAsync(ct);
        // include lots added earlier in the same unit of work
        foreach (var l in db.Set<StockLot>().Local.Where(l => l.WarehouseId == warehouseId && l.IngredientId == ingredientId && l.Quantity > 0))
            if (!tracked.Contains(l)) tracked.Add(l);
        return tracked;
    }

    private void Ledger(StockBalance b, DateOnly docDate, Guid? lotId, LedgerType type, decimal qty, decimal unitCost, decimal amount, string docType, Guid docId, string docNo)
        => db.Add(new StockLedger
        {
            At = Now, DocDate = docDate, WarehouseId = b.WarehouseId, IngredientId = b.IngredientId, LotId = lotId, Type = type, Quantity = qty, UnitCost = unitCost, Amount = amount,
            BalanceQty = b.Quantity, BalanceValue = b.Value, DocType = docType, DocId = docId, DocNo = docNo, CreatedBy = user.UserId,
        });

    private static void Apply(StockBalance b, BalanceState s, DateTimeOffset now)
    {
        b.Quantity = s.Quantity;
        b.Value = s.Value;
        b.AverageCost = Math.Round(s.AverageCost, 4);
        b.UpdatedAt = now;
    }

    // ------------------------------------------------------------------ receipts

    /// <summary>Posts a draft receipt: balance (moving average), new lots, ledger and price history. Lines failing the quality check block posting (A-40).</summary>
    public async Task PostReceiptAsync(StockReceipt r, CancellationToken ct, LedgerType type = LedgerType.In)
    {
        if (r.Status != StockDocStatus.Draft) throw new BusinessRuleException("stock.notDraft");
        if (r.Lines.Count == 0 || r.Lines.Any(l => l.Quantity <= 0)) throw new BusinessRuleException("stock.noLines");
        if (r.Lines.Any(l => l.QualityResult == QualityResult.Fail)) throw new BusinessRuleException("stock.qualityFailed");
        await EnsureOpenPeriodAsync(r.WarehouseId, r.DocDate, ct);
        var own = db.Database.CurrentTransaction is null;
        await using var tx = own ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockWarehouseAsync(r.WarehouseId, ct);
        foreach (var line in r.Lines.OrderBy(l => l.SortOrder))
        {
            line.Amount = Math.Round(line.Quantity * line.UnitPrice, 2);
            var b = await BalanceAsync(r.WarehouseId, line.IngredientId, ct);
            Apply(b, InventoryEngine.Receive(new BalanceState(b.Quantity, b.Value), line.Quantity, line.UnitPrice), Now);
            var lot = new StockLot
            {
                WarehouseId = r.WarehouseId, IngredientId = line.IngredientId, LotNo = line.LotNo, MfgDate = line.MfgDate, ExpiryDate = line.ExpiryDate, ReceivedDate = r.DocDate,
                ReceiptLineId = line.Id, InitialQty = line.Quantity, Quantity = line.Quantity, UnitCost = line.UnitPrice,
            };
            db.Add(lot);
            Ledger(b, r.DocDate, lot.Id, type, line.Quantity, line.UnitPrice, line.Amount, DocReceipt, r.Id, r.DocNo);
            if (type == LedgerType.In && line.UnitPrice > 0)
                db.Add(new IngredientPriceHistory { IngredientId = line.IngredientId, EffectiveDate = r.DocDate, UnitPrice = line.UnitPrice, Source = PriceSource.Receipt, SourceDocId = r.Id, PartnerId = r.PartnerId });
        }
        r.TotalAmount = r.Lines.Sum(l => l.Amount);
        r.Status = StockDocStatus.Posted;
        r.PostedAt = Now;
        r.PostedBy = user.UserId;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------ issues

    /// <summary>Posts a draft issue: lot allocation (FEFO / FIFO), issue cost by the configured method, negative stock guard.</summary>
    public async Task PostIssueAsync(StockIssue issue, CancellationToken ct, LedgerType type = LedgerType.Out)
    {
        if (issue.Status != StockDocStatus.Draft) throw new BusinessRuleException("stock.notDraft");
        if (issue.Lines.Count == 0 || issue.Lines.Any(l => l.Quantity <= 0)) throw new BusinessRuleException("stock.noLines");
        await EnsureOpenPeriodAsync(issue.WarehouseId, issue.DocDate, ct);
        var cfg = await settings.GetAsync<InventorySettings>(SettingKeys.Inventory, ct);
        var own = db.Database.CurrentTransaction is null;
        await using var tx = own ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockWarehouseAsync(issue.WarehouseId, ct);
        var names = await db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters().Where(i => issue.Lines.Select(l => l.IngredientId).Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.NameVi, ct);
        foreach (var line in issue.Lines.OrderBy(l => l.SortOrder).ToList())
        {
            var b = await BalanceAsync(issue.WarehouseId, line.IngredientId, ct);
            var lots = await LotsAsync(issue.WarehouseId, line.IngredientId, ct);
            var states = lots.Select(l => new LotState(l.Id, l.LotNo, l.ExpiryDate, l.ReceivedDate, l.Quantity, l.UnitCost)).ToList();
            var result = InventoryEngine.Issue(new BalanceState(b.Quantity, b.Value), states, line.Quantity, cfg.CostMethod, cfg.AllowNegativeStock, line.LotId);
            if (result.Shortage > 0)
                throw new BusinessRuleException("stock.insufficient", names.GetValueOrDefault(line.IngredientId, line.IngredientId.ToString()), Math.Max(b.Quantity, 0), line.Quantity);
            Apply(b, result.After, Now);
            foreach (var a in result.Allocations)
            {
                if (a.LotId is { } lotId) lots.First(l => l.Id == lotId).Quantity -= a.Quantity;
                // ledger keeps the running balance after each allocation for the stock card
                Ledger(b, issue.DocDate, a.LotId, type, -a.Quantity, a.UnitCost, -a.Amount, DocIssue, issue.Id, issue.DocNo);
            }
            line.UnitCost = result.UnitCost;
            line.Amount = result.Amount;
            if (result.Allocations.FirstOrDefault(a => a.LotId != null) is { } first && line.LotId is null)
            {
                line.LotId = first.LotId;
                line.LotNo = string.Join(", ", result.Allocations.Where(a => a.LotNo != null).Select(a => a.LotNo).Distinct());
            }
        }
        issue.TotalAmount = issue.Lines.Sum(l => l.Amount);
        issue.Status = StockDocStatus.Posted;
        issue.PostedAt = Now;
        issue.PostedBy = user.UserId;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>FEFO lot suggestion for an ingredient in a warehouse.</summary>
    public async Task<IReadOnlyList<(LotState Lot, decimal Quantity)>> SuggestLotsAsync(Guid warehouseId, Guid ingredientId, decimal quantity, CancellationToken ct)
    {
        var lots = await db.Set<StockLot>().AsNoTracking().Where(l => l.WarehouseId == warehouseId && l.IngredientId == ingredientId && l.Quantity > 0).ToListAsync(ct);
        return InventoryEngine.SuggestFefo(lots.Select(l => new LotState(l.Id, l.LotNo, l.ExpiryDate, l.ReceivedDate, l.Quantity, l.UnitCost)), quantity);
    }

    // ------------------------------------------------------------------ stock takes

    /// <summary>Creates a stock take with book quantities snapshotted from the current balance (per lot when lots exist).</summary>
    public async Task<StockTake> StartTakeAsync(Guid warehouseId, DateOnly date, string? members, IReadOnlyCollection<Guid>? ingredientIds, string? note, CancellationToken ct)
    {
        await EnsureOpenPeriodAsync(warehouseId, date, ct);
        var take = new StockTake { DocNo = await docs.NextAsync("KK", date, ct), WarehouseId = warehouseId, TakeDate = date, Members = members, Note = note, Status = StockTakeStatus.Counting };
        var balances = await db.Set<StockBalance>().AsNoTracking().Where(b => b.WarehouseId == warehouseId && b.Quantity != 0 && (ingredientIds == null || ingredientIds.Count == 0 || ingredientIds.Contains(b.IngredientId))).ToListAsync(ct);
        var lots = await db.Set<StockLot>().AsNoTracking().Where(l => l.WarehouseId == warehouseId && l.Quantity > 0).ToListAsync(ct);
        foreach (var b in balances)
        {
            var own = lots.Where(l => l.IngredientId == b.IngredientId).OrderBy(l => l.ExpiryDate ?? DateOnly.MaxValue).ToList();
            var lotQty = own.Sum(l => l.Quantity);
            foreach (var l in own)
                take.Lines.Add(new StockTakeLine { IngredientId = b.IngredientId, LotId = l.Id, LotNo = l.LotNo, BookQty = l.Quantity, UnitCost = b.AverageCost });
            if (b.Quantity - lotQty != 0 || own.Count == 0)
                take.Lines.Add(new StockTakeLine { IngredientId = b.IngredientId, BookQty = b.Quantity - lotQty, UnitCost = b.AverageCost });
        }
        // ingredients requested but without stock are counted from zero
        foreach (var id in (ingredientIds ?? []).Where(id => balances.All(b => b.IngredientId != id)))
            take.Lines.Add(new StockTakeLine { IngredientId = id, BookQty = 0 });
        db.Add(take);
        await db.SaveChangesAsync(ct);
        return take;
    }

    /// <summary>Approves a stock take and posts the generated adjustment receipt (surplus) and issue (shortage) — SPEC §5.5.</summary>
    public async Task ApproveTakeAsync(StockTake take, CancellationToken ct)
    {
        if (take.Status is not (StockTakeStatus.Draft or StockTakeStatus.Counting)) throw new BusinessRuleException("stock.notDraft");
        if (take.Lines.Any(l => l.ActualQty is null)) throw new BusinessRuleException("stock.takeIncomplete");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var surplus = take.Lines.Where(l => InventoryEngine.Difference(l.BookQty, l.ActualQty) > 0).ToList();
        var shortage = take.Lines.Where(l => InventoryEngine.Difference(l.BookQty, l.ActualQty) < 0).ToList();
        var avg = await db.Set<StockBalance>().AsNoTracking().Where(b => b.WarehouseId == take.WarehouseId).ToDictionaryAsync(b => b.IngredientId, b => b.AverageCost, ct);
        if (surplus.Count > 0)
        {
            var r = new StockReceipt { DocNo = await docs.NextAsync("PN", take.TakeDate, ct), DocDate = take.TakeDate, WarehouseId = take.WarehouseId, Note = $"Điều chỉnh kiểm kê / Stock-take adjustment {take.DocNo}" };
            var i = 0;
            foreach (var l in surplus)
                r.Lines.Add(new StockReceiptLine { IngredientId = l.IngredientId, Quantity = InventoryEngine.Difference(l.BookQty, l.ActualQty), UnitPrice = l.UnitCost > 0 ? l.UnitCost : avg.GetValueOrDefault(l.IngredientId), LotNo = l.LotNo, Note = l.Cause, SortOrder = ++i });
            db.Add(r);
            await PostReceiptAsync(r, ct, LedgerType.AdjustIn);
            take.AdjustReceiptId = r.Id;
        }
        if (shortage.Count > 0)
        {
            var s = new StockIssue { DocNo = await docs.NextAsync("PX", take.TakeDate, ct), DocDate = take.TakeDate, WarehouseId = take.WarehouseId, Reason = IssueReason.Adjustment, StockTakeId = take.Id, Note = $"Điều chỉnh kiểm kê / Stock-take adjustment {take.DocNo}" };
            var i = 0;
            foreach (var l in shortage)
                s.Lines.Add(new StockIssueLine { IngredientId = l.IngredientId, LotId = l.LotId, LotNo = l.LotNo, Quantity = -InventoryEngine.Difference(l.BookQty, l.ActualQty), Note = l.Cause, SortOrder = ++i });
            db.Add(s);
            await PostIssueAsync(s, ct, LedgerType.AdjustOut);
            take.AdjustIssueId = s.Id;
        }
        take.Status = StockTakeStatus.Approved;
        take.ApprovedAt = Now;
        take.ApprovedBy = user.UserId;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------ demand from menus

    /// <summary>Ingredient demand (purchase units) of the given menus: recipe gross × servings × factor (locked recipe version when approved).</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> DemandAsync(IReadOnlyCollection<Guid> menuIds, CancellationToken ct)
    {
        var items = await db.Set<MenuItem>().AsNoTracking().Where(i => menuIds.Contains(i.MenuId))
            .Join(db.Set<Menu>().AsNoTracking(), i => i.MenuId, m => m.Id, (i, m) => new { i.DishId, i.RecipeId, i.ServingFactor, m.Servings }).ToListAsync(ct);
        var dishIds = items.Select(i => i.DishId).Distinct().ToList();
        var lockedIds = items.Where(i => i.RecipeId != null).Select(i => i.RecipeId!.Value).Distinct().ToList();
        var recipes = await db.Set<Recipe>().AsNoTracking().IgnoreQueryFilters().Include(r => r.Items)
            .Where(r => lockedIds.Contains(r.Id) || (dishIds.Contains(r.DishId) && r.IsCurrent && !r.IsDeleted)).ToListAsync(ct);
        var ingredientIds = recipes.SelectMany(r => r.Items).Select(x => x.IngredientId).Distinct().ToList();
        var grams = await db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters().Where(i => ingredientIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.GramsPerUnit, ct);
        var lines = new List<DemandInput>();
        foreach (var it in items)
        {
            var recipe = (it.RecipeId is { } rid ? recipes.FirstOrDefault(r => r.Id == rid) : null) ?? recipes.FirstOrDefault(r => r.DishId == it.DishId && r.IsCurrent);
            if (recipe is null) continue;
            lines.AddRange(recipe.Items.Select(x => new DemandInput(x.IngredientId, x.GrossG, recipe.BaseServings, it.Servings, it.ServingFactor, grams.GetValueOrDefault(x.IngredientId, 1000m))));
        }
        return InventoryEngine.Demand(lines);
    }

    private async Task<Guid> DefaultWarehouseAsync(CancellationToken ct)
    {
        var cfg = await settings.GetAsync<InventorySettings>(SettingKeys.Inventory, ct);
        if (cfg.DefaultWarehouseId is { } w) return w;
        return await db.Set<Warehouse>().AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Code).Select(x => x.Id).FirstOrDefaultAsync(ct)
               is var id && id != Guid.Empty ? id : throw new BusinessRuleException("stock.noWarehouse");
    }

    /// <summary>Planned issues (Draft, IsPlanned) for an approved menu, one per warehouse, with FEFO lots (SPEC §6.5).</summary>
    public async Task<IReadOnlyList<StockIssue>> PlanIssuesForMenuAsync(Menu menu, CancellationToken ct)
    {
        if (menu.Status is not (MenuStatus.Approved or MenuStatus.Published)) throw new BusinessRuleException("stock.menuNotApproved");
        var existing = await db.Set<StockIssue>().AnyAsync(i => i.MenuId == menu.Id && i.Status != StockDocStatus.Cancelled, ct);
        if (existing) throw new ConflictException("stock.issueExists", menu.Code);
        var demand = await DemandAsync([menu.Id], ct);
        if (demand.Count == 0) throw new BusinessRuleException("stock.noLines");
        var fallback = await DefaultWarehouseAsync(ct);
        var whs = await db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters().Where(i => demand.Keys.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.DefaultWarehouseId ?? fallback, ct);
        var result = new List<StockIssue>();
        foreach (var g in demand.GroupBy(d => whs.GetValueOrDefault(d.Key, fallback)))
        {
            var issue = new StockIssue { DocNo = await docs.NextAsync("PX", menu.MenuDate, ct), DocDate = menu.MenuDate, WarehouseId = g.Key, Reason = IssueReason.ForMenu, MenuId = menu.Id, IsPlanned = true, Note = menu.Code };
            var i = 0;
            foreach (var (ingredientId, qty) in g.OrderBy(x => x.Key))
            {
                var lots = await SuggestLotsAsync(g.Key, ingredientId, qty, ct);
                if (lots.Count == 0) issue.Lines.Add(new StockIssueLine { IngredientId = ingredientId, Quantity = qty, SortOrder = ++i });
                var covered = 0m;
                foreach (var (lot, q) in lots)
                {
                    issue.Lines.Add(new StockIssueLine { IngredientId = ingredientId, LotId = lot.LotId, LotNo = lot.LotNo, Quantity = q, SortOrder = ++i });
                    covered += q;
                }
                if (lots.Count > 0 && covered < qty) issue.Lines.Add(new StockIssueLine { IngredientId = ingredientId, Quantity = qty - covered, SortOrder = ++i });
            }
            db.Add(issue);
            result.Add(issue);
        }
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Purchase request from approved menus in a date range (SPEC §5.5): demand − available − … + minimum stock.</summary>
    public async Task<PurchaseRequest> CreatePurchaseRequestAsync(DateOnly from, DateOnly to, Guid? warehouseId, IReadOnlyCollection<Guid>? menuIds, string? note, CancellationToken ct)
    {
        var q = db.Set<Menu>().AsNoTracking().Where(m => m.Status == MenuStatus.Approved || m.Status == MenuStatus.Published);
        q = menuIds is { Count: > 0 } ? q.Where(m => menuIds.Contains(m.Id)) : q.Where(m => m.MenuDate >= from && m.MenuDate <= to);
        var ids = await q.Select(m => m.Id).ToListAsync(ct);
        if (ids.Count == 0) throw new BusinessRuleException("stock.noApprovedMenus");
        var demand = await DemandAsync(ids, ct);
        var ingIds = demand.Keys.ToList();
        var available = await db.Set<StockBalance>().AsNoTracking().Where(b => ingIds.Contains(b.IngredientId) && (warehouseId == null || b.WarehouseId == warehouseId))
            .GroupBy(b => b.IngredientId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        // quantities already reserved by planned (not yet posted) issues are not available
        var reserved = await db.Set<StockIssueLine>().AsNoTracking()
            .Join(db.Set<StockIssue>().AsNoTracking().Where(i => i.Status == StockDocStatus.Draft && i.IsPlanned && (i.MenuId == null || !ids.Contains(i.MenuId.Value))), l => l.IssueId, i => i.Id, (l, i) => l)
            .Where(l => ingIds.Contains(l.IngredientId)).GroupBy(l => l.IngredientId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var ings = await db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters().Where(i => ingIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var prices = await db.Set<StockBalance>().AsNoTracking().Where(b => ingIds.Contains(b.IngredientId) && b.Quantity > 0)
            .GroupBy(b => b.IngredientId).Select(g => new { g.Key, Avg = g.Sum(x => x.Value) / g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Avg, ct);
        var pr = new PurchaseRequest
        {
            DocNo = await docs.NextAsync("DX", Today, ct), DocDate = Today, NeedDate = menuIds is { Count: > 0 } ? null : from, WarehouseId = warehouseId, MenuIds = string.Join(',', ids), Note = note,
        };
        foreach (var (id, req) in demand.OrderBy(d => ings.GetValueOrDefault(d.Key)?.Code))
        {
            var ing = ings.GetValueOrDefault(id);
            var s = InventoryEngine.Suggest(id, req, available.GetValueOrDefault(id) - reserved.GetValueOrDefault(id), ing?.MinStock ?? 0);
            var price = prices.TryGetValue(id, out var p) ? Math.Round(p, 2) : ing?.ReferencePrice;
            pr.Lines.Add(new PurchaseRequestLine { IngredientId = id, RequiredQty = s.Required, AvailableQty = s.Available, MinStock = s.MinStock, SuggestedQty = s.Suggested, OrderQty = s.Suggested, EstimatedPrice = price, PartnerId = ing?.PreferredPartnerId });
        }
        pr.EstimatedAmount = Math.Round(pr.Lines.Sum(l => (l.OrderQty ?? 0) * (l.EstimatedPrice ?? 0)), 0);
        db.Add(pr);
        await db.SaveChangesAsync(ct);
        return pr;
    }

    /// <summary>Draft receipt(s) from an approved purchase request, grouped by supplier.</summary>
    public async Task<IReadOnlyList<StockReceipt>> ReceiptsFromRequestAsync(PurchaseRequest pr, Guid? warehouseId, CancellationToken ct)
    {
        if (pr.Status is not (PurchaseRequestStatus.Approved or PurchaseRequestStatus.Ordered)) throw new BusinessRuleException("stock.requestNotApproved");
        var wh = warehouseId ?? pr.WarehouseId ?? await DefaultWarehouseAsync(ct);
        var result = new List<StockReceipt>();
        foreach (var g in pr.Lines.Where(l => (l.OrderQty ?? l.SuggestedQty) > 0).GroupBy(l => l.PartnerId))
        {
            var r = new StockReceipt { DocNo = await docs.NextAsync("PN", Today, ct), DocDate = Today, WarehouseId = wh, PartnerId = g.Key, PurchaseRequestId = pr.Id, Note = pr.DocNo };
            var i = 0;
            foreach (var l in g)
                r.Lines.Add(new StockReceiptLine { IngredientId = l.IngredientId, Quantity = l.OrderQty ?? l.SuggestedQty, UnitPrice = l.EstimatedPrice ?? 0, Amount = Math.Round((l.OrderQty ?? l.SuggestedQty) * (l.EstimatedPrice ?? 0), 2), SortOrder = ++i });
            db.Add(r);
            result.Add(r);
        }
        if (result.Count == 0) throw new BusinessRuleException("stock.noLines");
        pr.Status = PurchaseRequestStatus.Ordered;
        await db.SaveChangesAsync(ct);
        return result;
    }
}
