using BlueMeal.Domain.Common;
using BlueMeal.Domain.Enums;

namespace BlueMeal.Domain.Entities;

public class StockReceipt : AuditableEntity
{
    /// <summary>PN-YYYYMMDD-####.</summary>
    public string DocNo { get; set; } = default!;
    public DateOnly DocDate { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public Guid? PartnerId { get; set; }
    public Partner? Partner { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? PurchaseRequestId { get; set; }
    public string? InvoiceNo { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public StockDocStatus Status { get; set; } = StockDocStatus.Draft;
    public DateTimeOffset? PostedAt { get; set; }
    public Guid? PostedBy { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public ICollection<StockReceiptLine> Lines { get; set; } = new List<StockReceiptLine>();
}

public class StockReceiptLine : Entity
{
    public Guid ReceiptId { get; set; }
    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    /// <summary>Quantity in purchase units.</summary>
    public decimal Quantity { get; set; }
    public Guid? UnitId { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string? LotNo { get; set; }
    public DateOnly? MfgDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public QualityResult? QualityResult { get; set; }
    public decimal? ReceivedTemp { get; set; }
    public string? Note { get; set; }
    public Guid? PhotoFileId { get; set; }
    public int SortOrder { get; set; }
}

public class StockIssue : AuditableEntity
{
    /// <summary>PX-YYYYMMDD-####.</summary>
    public string DocNo { get; set; } = default!;
    public DateOnly DocDate { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public IssueReason Reason { get; set; } = IssueReason.ForMenu;
    public Guid? MenuId { get; set; }
    public Guid? TargetWarehouseId { get; set; }
    public Guid? StockTakeId { get; set; }
    public StockDocStatus Status { get; set; } = StockDocStatus.Draft;
    /// <summary>Planned issues are generated from approved menus and must be confirmed.</summary>
    public bool IsPlanned { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public Guid? PostedBy { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public ICollection<StockIssueLine> Lines { get; set; } = new List<StockIssueLine>();
}

public class StockIssueLine : Entity
{
    public Guid IssueId { get; set; }
    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public Guid? LotId { get; set; }
    public string? LotNo { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Issue unit cost computed by the inventory cost method when posted.</summary>
    public decimal UnitCost { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Adjustment receipts (from stock-take surplus) reuse StockReceipt; shortages reuse StockIssue (Reason = Adjustment).</summary>
public class StockTake : AuditableEntity
{
    /// <summary>KK-YYYYMMDD-####.</summary>
    public string DocNo { get; set; } = default!;
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public DateOnly TakeDate { get; set; }
    /// <summary>Committee members (free text / names).</summary>
    public string? Members { get; set; }
    public StockTakeStatus Status { get; set; } = StockTakeStatus.Draft;
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public Guid? AdjustReceiptId { get; set; }
    public Guid? AdjustIssueId { get; set; }
    public string? Note { get; set; }
    public ICollection<StockTakeLine> Lines { get; set; } = new List<StockTakeLine>();
}

public class StockTakeLine : Entity
{
    public Guid StockTakeId { get; set; }
    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public Guid? LotId { get; set; }
    public string? LotNo { get; set; }
    public decimal BookQty { get; set; }
    public decimal? ActualQty { get; set; }
    public decimal Difference => (ActualQty ?? BookQty) - BookQty;
    public decimal UnitCost { get; set; }
    public string? Cause { get; set; }
    public string? Resolution { get; set; }
}

/// <summary>Immutable ledger of every stock movement (sổ cái kho).</summary>
[NoAudit]
public class StockLedger : Entity
{
    public DateTimeOffset At { get; set; }
    public DateOnly DocDate { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid IngredientId { get; set; }
    public Guid? LotId { get; set; }
    public LedgerType Type { get; set; }
    /// <summary>Signed quantity: + in, − out.</summary>
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>Signed value.</summary>
    public decimal Amount { get; set; }
    public decimal BalanceQty { get; set; }
    public decimal BalanceValue { get; set; }
    public string DocType { get; set; } = default!;
    public Guid DocId { get; set; }
    public string DocNo { get; set; } = default!;
    public Guid? CreatedBy { get; set; }
}

/// <summary>Current balance per warehouse × ingredient (materialized, updated in the same transaction).</summary>
[NoAudit]
public class StockBalance : Entity, IVersioned
{
    public Guid WarehouseId { get; set; }
    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public decimal Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }
}

/// <summary>Lot-level balance for FEFO suggestions and FIFO costing.</summary>
[NoAudit]
public class StockLot : Entity, IVersioned
{
    public Guid WarehouseId { get; set; }
    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public string? LotNo { get; set; }
    public DateOnly? MfgDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public Guid? ReceiptLineId { get; set; }
    public decimal InitialQty { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public uint Version { get; set; }
}

public class StockPeriodLock : Entity
{
    public Guid? WarehouseId { get; set; }
    /// <summary>First day of locked month.</summary>
    public DateOnly Period { get; set; }
    public DateTimeOffset LockedAt { get; set; }
    public Guid? LockedBy { get; set; }
}

public class PurchaseRequest : AuditableEntity
{
    /// <summary>DX-YYYYMMDD-####.</summary>
    public string DocNo { get; set; } = default!;
    public DateOnly DocDate { get; set; }
    public DateOnly? NeedDate { get; set; }
    public Guid? WarehouseId { get; set; }
    /// <summary>Comma separated menu ids the demand was computed from.</summary>
    public string? MenuIds { get; set; }
    public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Draft;
    public decimal EstimatedAmount { get; set; }
    public string? Note { get; set; }
    public ICollection<PurchaseRequestLine> Lines { get; set; } = new List<PurchaseRequestLine>();
}

public class PurchaseRequestLine : Entity
{
    public Guid RequestId { get; set; }
    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public decimal RequiredQty { get; set; }
    public decimal AvailableQty { get; set; }
    public decimal MinStock { get; set; }
    /// <summary>max(0, required − available + minStock).</summary>
    public decimal SuggestedQty { get; set; }
    public decimal? OrderQty { get; set; }
    public decimal? EstimatedPrice { get; set; }
    public Guid? PartnerId { get; set; }
}
