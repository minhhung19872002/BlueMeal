using BlueMeal.Api.Authorization;
using BlueMeal.Api.Infrastructure;
using BlueMeal.Api.Services;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Stock;

// ---------------------------------------------------------------------------------------------- inputs

public sealed record ReceiptLineInput(Guid IngredientId, decimal Quantity, Guid? UnitId, decimal UnitPrice, string? LotNo, DateOnly? MfgDate, DateOnly? ExpiryDate,
    QualityResult? QualityResult, decimal? ReceivedTemp, string? Note, Guid? PhotoFileId);

public sealed record ReceiptInput(DateOnly DocDate, Guid WarehouseId, Guid? PartnerId, Guid? ContractId, string? InvoiceNo, DateOnly? InvoiceDate, string? Note,
    IReadOnlyList<ReceiptLineInput> Lines, uint? Version);

public sealed record IssueLineInput(Guid IngredientId, Guid? LotId, string? LotNo, decimal Quantity, string? Note);

public sealed record IssueInput(DateOnly DocDate, Guid WarehouseId, IssueReason Reason, Guid? MenuId, Guid? TargetWarehouseId, string? Note, IReadOnlyList<IssueLineInput> Lines, uint? Version);

public sealed class ReceiptInputValidator : AbstractValidator<ReceiptInput>
{
    public ReceiptInputValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.InvoiceNo).MaximumLength(50);
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.IngredientId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0).LessThan(10_000_000);
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).LessThan(1_000_000_000);
            l.RuleFor(x => x.LotNo).MaximumLength(50);
            l.RuleFor(x => x.ExpiryDate).GreaterThanOrEqualTo(x => x.MfgDate).When(x => x.ExpiryDate != null && x.MfgDate != null).WithMessage("validation.expiryBeforeMfg");
            l.RuleFor(x => x.ReceivedTemp).InclusiveBetween(-60, 100).When(x => x.ReceivedTemp != null);
            l.RuleFor(x => x.Note).MaximumLength(500);
        });
    }
}

public sealed class IssueInputValidator : AbstractValidator<IssueInput>
{
    public IssueInputValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.TargetWarehouseId).NotEmpty().When(x => x.Reason == IssueReason.Transfer);
        RuleFor(x => x.TargetWarehouseId).NotEqual(x => x.WarehouseId).When(x => x.TargetWarehouseId != null);
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.IngredientId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0).LessThan(10_000_000);
            l.RuleFor(x => x.Note).MaximumLength(500);
        });
    }
}

/// <summary>Shared helpers: warehouse data scope (SPEC §10.2) and ingredient labels.</summary>
public abstract class StockControllerBase(AppDbContext db) : ApiControllerBase
{
    protected AppDbContext Db => db;

    protected void EnsureWarehouse(Guid warehouseId)
    {
        if (CurrentUser.WarehouseScope is { } scope && !scope.Contains(warehouseId)) throw new NotFoundException("warehouse");
    }

    protected IQueryable<T> ScopeWarehouse<T>(IQueryable<T> q, System.Linq.Expressions.Expression<Func<T, Guid>> warehouse)
    {
        if (CurrentUser.WarehouseScope is not { } scope) return q;
        var ids = scope.ToList();
        var p = warehouse.Parameters[0];
        var body = System.Linq.Expressions.Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)], System.Linq.Expressions.Expression.Constant(ids), warehouse.Body);
        return q.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, p));
    }

    protected async Task<Dictionary<Guid, (string Code, string Name, string? NameEn, string? Unit)>> IngredientsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters().Where(i => list.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.NameVi, i.NameEn, Unit = i.PurchaseUnit!.Symbol })
            .ToDictionaryAsync(i => i.Id, i => (i.Code, i.NameVi, i.NameEn, (string?)i.Unit), ct);
    }

    protected void SetVersion<T>(T entity, uint? version) where T : class, IVersioned
    {
        if (version is { } v) db.Entry(entity).Property(nameof(IVersioned.Version)).OriginalValue = v;
    }
}

// ---------------------------------------------------------------------------------------------- receipts

/// <summary>Stock receipts (phiếu nhập) with incoming quality check — SPEC §7.8, §5.5.</summary>
[Route("api/v1/stock-receipts")]
public sealed class StockReceiptsController(AppDbContext db, InventoryService inventory, IDocNumberGenerator docs, ISettingsService settings) : StockControllerBase(db)
{
    [HttpGet, HasPermission(Permissions.StockReceipt.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? partnerId, [FromQuery] StockDocStatus? status, CancellationToken ct)
    {
        var query = ScopeWarehouse(Db.Set<StockReceipt>().AsNoTracking(), r => r.WarehouseId);
        if (from is not null) query = query.Where(r => r.DocDate >= from);
        if (to is not null) query = query.Where(r => r.DocDate <= to);
        if (warehouseId is not null) query = query.Where(r => r.WarehouseId == warehouseId);
        if (partnerId is not null) query = query.Where(r => r.PartnerId == partnerId);
        if (status is not null) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(q.Q)) query = query.Where(r => EF.Functions.ILike(r.DocNo, "%" + q.Q + "%") || EF.Functions.ILike(r.InvoiceNo ?? "", "%" + q.Q + "%"));
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(r => r.DocDate).ThenByDescending(r => r.DocNo).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(r => new
        {
            r.Id, r.DocNo, r.DocDate, r.WarehouseId, warehouse = r.Warehouse!.NameVi, r.PartnerId, partner = r.Partner!.NameVi, r.InvoiceNo, status = r.Status.ToString(), r.TotalAmount,
            lineCount = r.Lines.Count, failed = r.Lines.Count(l => l.QualityResult == QualityResult.Fail), r.PurchaseRequestId, r.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    private async Task<StockReceipt> LoadAsync(Guid id, CancellationToken ct)
    {
        var r = await Db.Set<StockReceipt>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("receipt");
        EnsureWarehouse(r.WarehouseId);
        return r;
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.StockReceipt.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        var ing = await IngredientsAsync(r.Lines.Select(l => l.IngredientId), ct);
        var wh = await Db.Set<Warehouse>().AsNoTracking().IgnoreQueryFilters().Where(w => w.Id == r.WarehouseId).Select(w => w.NameVi).FirstOrDefaultAsync(ct);
        var partner = r.PartnerId is null ? null : await Db.Set<Partner>().AsNoTracking().IgnoreQueryFilters().Where(p => p.Id == r.PartnerId).Select(p => new { p.Code, p.NameVi, p.Address }).FirstOrDefaultAsync(ct);
        var inspections = await Db.Set<FoodInspectionStep1>().AsNoTracking().Where(i => i.ReceiptId == id).Select(i => new { i.Id, i.ReceiptLineId, result = i.Result.ToString(), i.Inspector, i.InspectedAt }).ToListAsync(ct);
        return Ok(new
        {
            r.Id, r.DocNo, r.DocDate, r.WarehouseId, warehouse = wh, r.PartnerId, partner, r.ContractId, r.PurchaseRequestId, r.InvoiceNo, r.InvoiceDate, status = r.Status.ToString(),
            r.PostedAt, r.TotalAmount, r.Note, r.Version,
            lines = r.Lines.OrderBy(l => l.SortOrder).Select(l => new
            {
                l.Id, l.IngredientId, ingredientCode = ing.GetValueOrDefault(l.IngredientId).Code, ingredientName = ing.GetValueOrDefault(l.IngredientId).Name,
                ingredientNameEn = ing.GetValueOrDefault(l.IngredientId).NameEn, unit = ing.GetValueOrDefault(l.IngredientId).Unit, l.Quantity, l.UnitId, l.UnitPrice, l.Amount,
                l.LotNo, l.MfgDate, l.ExpiryDate, qualityResult = l.QualityResult?.ToString(), l.ReceivedTemp, l.Note, l.PhotoFileId,
            }),
            inspections,
        });
    }

    private static void Apply(StockReceipt r, ReceiptInput i)
    {
        r.DocDate = i.DocDate; r.WarehouseId = i.WarehouseId; r.PartnerId = i.PartnerId; r.ContractId = i.ContractId; r.InvoiceNo = i.InvoiceNo?.Trim(); r.InvoiceDate = i.InvoiceDate; r.Note = i.Note;
        r.Lines.Clear();
        var n = 0;
        foreach (var l in i.Lines)
            r.Lines.Add(new StockReceiptLine
            {
                ReceiptId = r.Id, IngredientId = l.IngredientId, Quantity = l.Quantity, UnitId = l.UnitId, UnitPrice = l.UnitPrice, Amount = Math.Round(l.Quantity * l.UnitPrice, 2), LotNo = l.LotNo?.Trim(),
                MfgDate = l.MfgDate, ExpiryDate = l.ExpiryDate, QualityResult = l.QualityResult, ReceivedTemp = l.ReceivedTemp, Note = l.Note, PhotoFileId = l.PhotoFileId, SortOrder = ++n,
            });
        r.TotalAmount = r.Lines.Sum(l => l.Amount);
    }

    [HttpPost, HasPermission(Permissions.StockReceipt.Create), Idempotent]
    public async Task<IActionResult> Create([FromBody] ReceiptInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        EnsureWarehouse(input.WarehouseId);
        await inventory.EnsureOpenPeriodAsync(input.WarehouseId, input.DocDate, ct);
        var r = new StockReceipt { DocNo = await docs.NextAsync("PN", input.DocDate, ct) };
        Apply(r, input);
        Db.Add(r);
        await Db.SaveChangesAsync(ct);
        return Ok(new { r.Id, r.DocNo });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.StockReceipt.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ReceiptInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        EnsureWarehouse(input.WarehouseId);
        var r = await LoadAsync(id, ct);
        if (r.Status != StockDocStatus.Draft) throw new BusinessRuleException("stock.notDraft");
        await inventory.EnsureOpenPeriodAsync(input.WarehouseId, input.DocDate, ct);
        SetVersion(r, input.Version);
        Db.RemoveRange(r.Lines);
        Apply(r, input);
        Db.AddRange(r.Lines);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.StockReceipt.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        if (r.Status != StockDocStatus.Draft) throw new BusinessRuleException("stock.notDraft");
        Db.Remove(r);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Posts the receipt and records step-1 inspection entries for lines with a quality result (SPEC §5.6 bước 1).</summary>
    [HttpPost("{id:guid}/post"), HasPermission(Permissions.StockReceipt.Post), Idempotent]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        await inventory.PostReceiptAsync(r, ct);
        var existing = await Db.Set<FoodInspectionStep1>().Where(i => i.ReceiptId == id).Select(i => i.ReceiptLineId).ToListAsync(ct);
        var form = await settings.GetAsync<Application.Settings.InspectionFormSettings>(Application.Settings.SettingKeys.Inspection, ct);
        foreach (var l in r.Lines.Where(l => l.QualityResult != null && !existing.Contains(l.Id)))
            Db.Add(new FoodInspectionStep1
            {
                InspectedAt = r.PostedAt ?? DateTimeOffset.UtcNow, ReceiptId = r.Id, ReceiptLineId = l.Id, IngredientId = l.IngredientId, PartnerId = r.PartnerId, Quantity = l.Quantity,
                LotNo = l.LotNo, ExpiryDate = l.ExpiryDate, Temperature = l.ReceivedTemp, Result = l.QualityResult!.Value, SensoryNote = l.Note, OriginDocsOk = l.QualityResult != QualityResult.Fail,
                Criteria = System.Text.Json.JsonSerializer.Serialize(form.Step1.ToDictionary(c => c.Code, c => c.Type == "number" ? (object?)l.ReceivedTemp : l.QualityResult != QualityResult.Fail)),
                Inspector = CurrentUser.FullName ?? CurrentUser.UserName ?? "-", InspectorUserId = CurrentUser.UserId,
            });
        await Db.SaveChangesAsync(ct);
        return Ok(new { r.Id, r.DocNo, status = r.Status.ToString(), r.TotalAmount });
    }
}

// ---------------------------------------------------------------------------------------------- issues

/// <summary>Stock issues (phiếu xuất): manual or planned from approved menus with FEFO lots — SPEC §7.8, §6.5.</summary>
[Route("api/v1/stock-issues")]
public sealed class StockIssuesController(AppDbContext db, InventoryService inventory, IDocNumberGenerator docs) : StockControllerBase(db)
{
    [HttpGet, HasPermission(Permissions.StockIssue.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? warehouseId,
        [FromQuery] IssueReason? reason, [FromQuery] StockDocStatus? status, [FromQuery] Guid? menuId, [FromQuery] bool? planned, CancellationToken ct)
    {
        var query = ScopeWarehouse(Db.Set<StockIssue>().AsNoTracking(), r => r.WarehouseId);
        if (from is not null) query = query.Where(r => r.DocDate >= from);
        if (to is not null) query = query.Where(r => r.DocDate <= to);
        if (warehouseId is not null) query = query.Where(r => r.WarehouseId == warehouseId);
        if (reason is not null) query = query.Where(r => r.Reason == reason);
        if (status is not null) query = query.Where(r => r.Status == status);
        if (menuId is not null) query = query.Where(r => r.MenuId == menuId);
        if (planned is not null) query = query.Where(r => r.IsPlanned == planned);
        if (!string.IsNullOrWhiteSpace(q.Q)) query = query.Where(r => EF.Functions.ILike(r.DocNo, "%" + q.Q + "%"));
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(r => r.DocDate).ThenByDescending(r => r.DocNo).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(r => new
        {
            r.Id, r.DocNo, r.DocDate, r.WarehouseId, warehouse = r.Warehouse!.NameVi, reason = r.Reason.ToString(), r.MenuId,
            menu = Db.Set<Menu>().Where(m => m.Id == r.MenuId).Select(m => m.Code).FirstOrDefault(), status = r.Status.ToString(), r.IsPlanned, r.TotalAmount, lineCount = r.Lines.Count, r.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    private async Task<StockIssue> LoadAsync(Guid id, CancellationToken ct)
    {
        var r = await Db.Set<StockIssue>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("issue");
        EnsureWarehouse(r.WarehouseId);
        return r;
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.StockIssue.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        var ing = await IngredientsAsync(r.Lines.Select(l => l.IngredientId), ct);
        var wh = await Db.Set<Warehouse>().AsNoTracking().IgnoreQueryFilters().Where(w => w.Id == r.WarehouseId).Select(w => w.NameVi).FirstOrDefaultAsync(ct);
        var menu = r.MenuId is null ? null : await Db.Set<Menu>().AsNoTracking().Where(m => m.Id == r.MenuId).Select(m => new { m.Id, m.Code, m.MenuDate, m.Servings }).FirstOrDefaultAsync(ct);
        var available = await Db.Set<StockBalance>().AsNoTracking().Where(b => b.WarehouseId == r.WarehouseId && r.Lines.Select(l => l.IngredientId).Contains(b.IngredientId))
            .ToDictionaryAsync(b => b.IngredientId, b => b.Quantity, ct);
        return Ok(new
        {
            r.Id, r.DocNo, r.DocDate, r.WarehouseId, warehouse = wh, reason = r.Reason.ToString(), r.MenuId, menu, r.TargetWarehouseId, r.StockTakeId, status = r.Status.ToString(), r.IsPlanned,
            r.PostedAt, r.TotalAmount, r.Note, r.Version,
            lines = r.Lines.OrderBy(l => l.SortOrder).Select(l => new
            {
                l.Id, l.IngredientId, ingredientCode = ing.GetValueOrDefault(l.IngredientId).Code, ingredientName = ing.GetValueOrDefault(l.IngredientId).Name,
                ingredientNameEn = ing.GetValueOrDefault(l.IngredientId).NameEn, unit = ing.GetValueOrDefault(l.IngredientId).Unit, l.LotId, l.LotNo, l.Quantity, l.UnitCost, l.Amount, l.Note,
                available = available.GetValueOrDefault(l.IngredientId),
            }),
        });
    }

    private static void Apply(StockIssue r, IssueInput i)
    {
        r.DocDate = i.DocDate; r.WarehouseId = i.WarehouseId; r.Reason = i.Reason; r.MenuId = i.MenuId; r.TargetWarehouseId = i.TargetWarehouseId; r.Note = i.Note;
        r.Lines.Clear();
        var n = 0;
        foreach (var l in i.Lines)
            r.Lines.Add(new StockIssueLine { IssueId = r.Id, IngredientId = l.IngredientId, LotId = l.LotId, LotNo = l.LotNo, Quantity = l.Quantity, Note = l.Note, SortOrder = ++n });
    }

    [HttpPost, HasPermission(Permissions.StockIssue.Create), Idempotent]
    public async Task<IActionResult> Create([FromBody] IssueInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        EnsureWarehouse(input.WarehouseId);
        await inventory.EnsureOpenPeriodAsync(input.WarehouseId, input.DocDate, ct);
        var r = new StockIssue { DocNo = await docs.NextAsync("PX", input.DocDate, ct) };
        Apply(r, input);
        Db.Add(r);
        await Db.SaveChangesAsync(ct);
        return Ok(new { r.Id, r.DocNo });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.StockIssue.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] IssueInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        EnsureWarehouse(input.WarehouseId);
        var r = await LoadAsync(id, ct);
        if (r.Status != StockDocStatus.Draft) throw new BusinessRuleException("stock.notDraft");
        await inventory.EnsureOpenPeriodAsync(input.WarehouseId, input.DocDate, ct);
        SetVersion(r, input.Version);
        Db.RemoveRange(r.Lines);
        var planned = r.IsPlanned;
        Apply(r, input);
        r.IsPlanned = planned;
        Db.AddRange(r.Lines);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.StockIssue.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        if (r.Status != StockDocStatus.Draft) throw new BusinessRuleException("stock.notDraft");
        Db.Remove(r);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Posts the issue; a transfer also posts the matching receipt into the target warehouse at the same cost.</summary>
    [HttpPost("{id:guid}/post"), HasPermission(Permissions.StockIssue.Post), Idempotent]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        await inventory.PostIssueAsync(r, ct);
        Guid? receiptId = null;
        if (r.Reason == IssueReason.Transfer && r.TargetWarehouseId is { } target)
        {
            var rc = new StockReceipt { DocNo = await docs.NextAsync("PN", r.DocDate, ct), DocDate = r.DocDate, WarehouseId = target, Note = $"Chuyển kho / Transfer {r.DocNo}" };
            var n = 0;
            foreach (var l in r.Lines)
            {
                var lot = l.LotId is null ? null : await Db.Set<StockLot>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == l.LotId, ct);
                rc.Lines.Add(new StockReceiptLine { IngredientId = l.IngredientId, Quantity = l.Quantity, UnitPrice = l.UnitCost, LotNo = lot?.LotNo ?? l.LotNo, MfgDate = lot?.MfgDate, ExpiryDate = lot?.ExpiryDate, SortOrder = ++n });
            }
            Db.Add(rc);
            await inventory.PostReceiptAsync(rc, ct, LedgerType.In);
            receiptId = rc.Id;
        }
        await tx.CommitAsync(ct);
        return Ok(new { r.Id, r.DocNo, status = r.Status.ToString(), r.TotalAmount, receiptId });
    }

    /// <summary>FEFO lot suggestion for one ingredient (SPEC §6.5).</summary>
    [HttpGet("suggest-lots"), HasPermission(Permissions.StockIssue.View)]
    public async Task<IActionResult> SuggestLots([FromQuery] Guid warehouseId, [FromQuery] Guid ingredientId, [FromQuery] decimal quantity, CancellationToken ct)
    {
        EnsureWarehouse(warehouseId);
        var lots = await inventory.SuggestLotsAsync(warehouseId, ingredientId, quantity, ct);
        return Ok(lots.Select(x => new { x.Lot.LotId, x.Lot.LotNo, x.Lot.ExpiryDate, available = x.Lot.Quantity, quantity = x.Quantity, x.Lot.UnitCost }));
    }

    /// <summary>Generates planned issues (quantity per recipe × servings, FEFO lots) from an approved menu.</summary>
    [HttpPost("from-menu/{menuId:guid}"), HasPermission(Permissions.StockIssue.Create), Idempotent]
    public async Task<IActionResult> FromMenu(Guid menuId, CancellationToken ct)
    {
        var menu = await Db.Set<Menu>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId, ct) ?? throw new NotFoundException("menu");
        var issues = await inventory.PlanIssuesForMenuAsync(menu, ct);
        return Ok(issues.Select(i => new { i.Id, i.DocNo, i.WarehouseId, lineCount = i.Lines.Count }));
    }
}

// ---------------------------------------------------------------------------------------------- stock takes

public sealed record StartTakeInput(Guid WarehouseId, DateOnly TakeDate, string? Members, IReadOnlyList<Guid>? IngredientIds, string? Note);
public sealed record TakeCountInput(Guid Id, decimal? ActualQty, string? Cause, string? Resolution);
public sealed record TakeAddLineInput(Guid IngredientId, string? LotNo, decimal ActualQty, string? Cause);
public sealed record TakeUpdateInput(string? Members, string? Note, IReadOnlyList<TakeCountInput> Lines, IReadOnlyList<TakeAddLineInput>? NewLines, uint? Version);

public sealed class TakeUpdateInputValidator : AbstractValidator<TakeUpdateInput>
{
    public TakeUpdateInputValidator()
    {
        RuleForEach(x => x.Lines).ChildRules(l => { l.RuleFor(x => x.ActualQty).GreaterThanOrEqualTo(0).When(x => x.ActualQty != null); l.RuleFor(x => x.Cause).MaximumLength(500); l.RuleFor(x => x.Resolution).MaximumLength(500); });
        RuleForEach(x => x.NewLines).ChildRules(l => { l.RuleFor(x => x.IngredientId).NotEmpty(); l.RuleFor(x => x.ActualQty).GreaterThanOrEqualTo(0); });
        RuleFor(x => x.Members).MaximumLength(1000);
    }
}

/// <summary>Stock takes (kiểm kê): book quantity snapshot, mobile counting, approval with automatic adjustment documents.</summary>
[Route("api/v1/stock-takes")]
public sealed class StockTakesController(AppDbContext db, InventoryService inventory) : StockControllerBase(db)
{
    [HttpGet, HasPermission(Permissions.StockTake.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] Guid? warehouseId, [FromQuery] StockTakeStatus? status, CancellationToken ct)
    {
        var query = ScopeWarehouse(Db.Set<StockTake>().AsNoTracking(), r => r.WarehouseId);
        if (warehouseId is not null) query = query.Where(r => r.WarehouseId == warehouseId);
        if (status is not null) query = query.Where(r => r.Status == status);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(r => r.TakeDate).ThenByDescending(r => r.DocNo).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(r => new
        {
            r.Id, r.DocNo, r.TakeDate, r.WarehouseId, warehouse = r.Warehouse!.NameVi, status = r.Status.ToString(), r.Members, lineCount = r.Lines.Count,
            counted = r.Lines.Count(l => l.ActualQty != null), differences = r.Lines.Count(l => l.ActualQty != null && l.ActualQty != l.BookQty), r.AdjustReceiptId, r.AdjustIssueId, r.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    private async Task<StockTake> LoadAsync(Guid id, CancellationToken ct)
    {
        var r = await Db.Set<StockTake>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("stock take");
        EnsureWarehouse(r.WarehouseId);
        return r;
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.StockTake.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, ct);
        var ing = await IngredientsAsync(r.Lines.Select(l => l.IngredientId), ct);
        var wh = await Db.Set<Warehouse>().AsNoTracking().IgnoreQueryFilters().Where(w => w.Id == r.WarehouseId).Select(w => w.NameVi).FirstOrDefaultAsync(ct);
        var docNos = await Db.Set<StockReceipt>().AsNoTracking().Where(x => x.Id == r.AdjustReceiptId).Select(x => x.DocNo)
            .Concat(Db.Set<StockIssue>().AsNoTracking().Where(x => x.Id == r.AdjustIssueId).Select(x => x.DocNo)).ToListAsync(ct);
        return Ok(new
        {
            r.Id, r.DocNo, r.TakeDate, r.WarehouseId, warehouse = wh, status = r.Status.ToString(), r.Members, r.Note, r.ApprovedAt, r.AdjustReceiptId, r.AdjustIssueId, adjustDocs = docNos, r.Version,
            lines = r.Lines.OrderBy(l => ing.GetValueOrDefault(l.IngredientId).Code).ThenBy(l => l.LotNo).Select(l => new
            {
                l.Id, l.IngredientId, ingredientCode = ing.GetValueOrDefault(l.IngredientId).Code, ingredientName = ing.GetValueOrDefault(l.IngredientId).Name,
                ingredientNameEn = ing.GetValueOrDefault(l.IngredientId).NameEn, unit = ing.GetValueOrDefault(l.IngredientId).Unit, l.LotId, l.LotNo, l.BookQty, l.ActualQty,
                difference = Application.Engines.InventoryEngine.Difference(l.BookQty, l.ActualQty), l.UnitCost, l.Cause, l.Resolution,
            }),
        });
    }

    [HttpPost, HasPermission(Permissions.StockTake.Create), Idempotent]
    public async Task<IActionResult> Start([FromBody] StartTakeInput input, CancellationToken ct)
    {
        EnsureWarehouse(input.WarehouseId);
        var t = await inventory.StartTakeAsync(input.WarehouseId, input.TakeDate, input.Members, input.IngredientIds, input.Note, ct);
        return Ok(new { t.Id, t.DocNo, lineCount = t.Lines.Count });
    }

    /// <summary>Saves counted quantities (works on mobile: partial saves allowed).</summary>
    [HttpPut("{id:guid}"), HasPermission(Permissions.StockTake.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] TakeUpdateInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var t = await LoadAsync(id, ct);
        if (t.Status is not (StockTakeStatus.Draft or StockTakeStatus.Counting)) throw new BusinessRuleException("stock.notDraft");
        SetVersion(t, input.Version);
        t.Members = input.Members; t.Note = input.Note;
        foreach (var c in input.Lines)
        {
            var line = t.Lines.FirstOrDefault(l => l.Id == c.Id) ?? throw new NotFoundException("stock take line");
            line.ActualQty = c.ActualQty; line.Cause = c.Cause; line.Resolution = c.Resolution;
        }
        if (input.NewLines is { Count: > 0 })
        {
            var avg = await Db.Set<StockBalance>().AsNoTracking().Where(b => b.WarehouseId == t.WarehouseId).ToDictionaryAsync(b => b.IngredientId, b => b.AverageCost, ct);
            var refPrice = await Db.Set<Ingredient>().AsNoTracking().Where(i => input.NewLines.Select(n => n.IngredientId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.ReferencePrice ?? 0, ct);
            foreach (var n in input.NewLines)
            {
                var line = new StockTakeLine { StockTakeId = t.Id, IngredientId = n.IngredientId, LotNo = n.LotNo, BookQty = 0, ActualQty = n.ActualQty, Cause = n.Cause, UnitCost = avg.TryGetValue(n.IngredientId, out var a) && a > 0 ? a : refPrice.GetValueOrDefault(n.IngredientId) };
                t.Lines.Add(line);
                Db.Add(line);
            }
        }
        t.Status = StockTakeStatus.Counting;
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.StockTake.Approve), Idempotent]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var t = await LoadAsync(id, ct);
        await inventory.ApproveTakeAsync(t, ct);
        return Ok(new { t.Id, status = t.Status.ToString(), t.AdjustReceiptId, t.AdjustIssueId });
    }

    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.StockTake.Edit)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var t = await LoadAsync(id, ct);
        if (t.Status is not (StockTakeStatus.Draft or StockTakeStatus.Counting)) throw new BusinessRuleException("stock.notDraft");
        t.Status = StockTakeStatus.Cancelled;
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }
}
