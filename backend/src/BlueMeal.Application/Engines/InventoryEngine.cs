using BlueMeal.Domain.Enums;

namespace BlueMeal.Application.Engines;

/// <summary>Balance of one ingredient in one warehouse (quantities in purchase units, values in VND).</summary>
public sealed record BalanceState(decimal Quantity, decimal Value)
{
    public static readonly BalanceState Empty = new(0m, 0m);
    public decimal AverageCost => Quantity > 0 ? Value / Quantity : 0m;
}

public sealed record LotState(Guid LotId, string? LotNo, DateOnly? ExpiryDate, DateOnly ReceivedDate, decimal Quantity, decimal UnitCost);

public sealed record LotAllocation(Guid? LotId, string? LotNo, decimal Quantity, decimal UnitCost, decimal Amount);

/// <summary>Result of an issue: lot allocations, average unit cost, value issued, balance after and missing quantity.</summary>
public sealed record IssueResult(IReadOnlyList<LotAllocation> Allocations, decimal UnitCost, decimal Amount, BalanceState After, decimal Shortage);

/// <summary>One recipe line multiplied by a menu's servings, used to compute ingredient demand.</summary>
public sealed record DemandInput(Guid IngredientId, decimal GrossG, decimal BaseServings, decimal Servings, decimal ServingFactor, decimal GramsPerUnit);

public sealed record PurchaseSuggestion(Guid IngredientId, decimal Required, decimal Available, decimal MinStock, decimal Suggested);

/// <summary>
/// Inventory valuation and allocation rules (SPEC §6.5): moving weighted average (default) or FIFO by lot, FEFO lot
/// suggestion, negative stock guard, menu demand and purchase suggestions. Pure functions — persistence lives in the
/// infrastructure service that applies these results to the ledger in one transaction.
/// </summary>
public static class InventoryEngine
{
    public const int QtyDecimals = 4;
    public const int MoneyDecimals = 2;

    private static decimal Q(decimal v) => Math.Round(v, QtyDecimals, MidpointRounding.AwayFromZero);
    private static decimal M(decimal v) => Math.Round(v, MoneyDecimals, MidpointRounding.AwayFromZero);

    /// <summary>Receipt: value grows by qty × cost; the moving average follows. A negative balance is reset to the receipt cost.</summary>
    public static BalanceState Receive(BalanceState before, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitCost < 0) throw new ArgumentOutOfRangeException(nameof(unitCost));
        var qty = Q(before.Quantity + quantity);
        if (before.Quantity < 0)
            // stock was issued below zero (allowed by configuration): the remaining quantity is valued at the new cost
            return new BalanceState(qty, M(Math.Max(qty, 0) * unitCost));
        return new BalanceState(qty, M(before.Value + quantity * unitCost));
    }

    /// <summary>FEFO: earliest expiry first (no expiry last), then oldest receipt.</summary>
    public static IReadOnlyList<LotState> FefoOrder(IEnumerable<LotState> lots)
        => lots.Where(l => l.Quantity > 0).OrderBy(l => l.ExpiryDate ?? DateOnly.MaxValue).ThenBy(l => l.ReceivedDate).ThenBy(l => l.LotNo).ToList();

    /// <summary>FIFO: oldest receipt first.</summary>
    public static IReadOnlyList<LotState> FifoOrder(IEnumerable<LotState> lots)
        => lots.Where(l => l.Quantity > 0).OrderBy(l => l.ReceivedDate).ThenBy(l => l.ExpiryDate ?? DateOnly.MaxValue).ThenBy(l => l.LotNo).ToList();

    /// <summary>Suggested lots for an issue (FEFO), without valuation.</summary>
    public static IReadOnlyList<(LotState Lot, decimal Quantity)> SuggestFefo(IEnumerable<LotState> lots, decimal quantity)
    {
        var result = new List<(LotState, decimal)>();
        var remaining = quantity;
        foreach (var lot in FefoOrder(lots))
        {
            if (remaining <= 0) break;
            var take = Math.Min(lot.Quantity, remaining);
            result.Add((lot, take));
            remaining -= take;
        }
        return result;
    }

    /// <summary>
    /// Issue <paramref name="quantity"/>. Lots are consumed FEFO (moving average) or FIFO (FIFO costing); a requested lot is consumed first.
    /// When stock is insufficient and negative stock is not allowed, nothing is issued and <see cref="IssueResult.Shortage"/> is set.
    /// </summary>
    public static IssueResult Issue(BalanceState before, IReadOnlyList<LotState> lots, decimal quantity, InventoryCostMethod method, bool allowNegative, Guid? requestedLotId = null)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var available = Math.Max(before.Quantity, 0);
        if (quantity > available && !allowNegative)
            return new IssueResult([], 0m, 0m, before, Q(quantity - available));

        var ordered = (method == InventoryCostMethod.Fifo ? FifoOrder(lots) : FefoOrder(lots)).ToList();
        if (requestedLotId is { } rid && ordered.FindIndex(l => l.LotId == rid) is var idx and > 0)
        {
            var lot = ordered[idx];
            ordered.RemoveAt(idx);
            ordered.Insert(0, lot);
        }

        var avg = before.AverageCost;
        var allocations = new List<LotAllocation>();
        var remaining = quantity;
        foreach (var lot in ordered)
        {
            if (remaining <= 0) break;
            var take = Math.Min(lot.Quantity, remaining);
            var cost = method == InventoryCostMethod.Fifo ? lot.UnitCost : avg;
            allocations.Add(new LotAllocation(lot.LotId, lot.LotNo, Q(take), cost, M(take * cost)));
            remaining -= take;
        }
        if (remaining > 0)
        {
            // quantity not covered by lots (legacy balance or negative stock): valued at the average cost
            var cost = avg > 0 ? avg : ordered.LastOrDefault()?.UnitCost ?? 0m;
            allocations.Add(new LotAllocation(null, null, Q(remaining), cost, M(remaining * cost)));
        }

        var amount = allocations.Sum(a => a.Amount);
        var afterQty = Q(before.Quantity - quantity);
        // issuing the whole balance takes the whole value, so no rounding residue stays in stock
        if (afterQty == 0 && before.Quantity > 0) amount = before.Value;
        var after = new BalanceState(afterQty, afterQty == 0 ? 0m : M(before.Value - amount));
        var unitCost = quantity > 0 ? Math.Round(amount / quantity, 4) : 0m;
        return new IssueResult(allocations, unitCost, M(amount), after, 0m);
    }

    /// <summary>Signed stock-take difference: actual − book (positive = surplus → adjustment receipt, negative = shortage → adjustment issue).</summary>
    public static decimal Difference(decimal bookQty, decimal? actualQty) => Q((actualQty ?? bookQty) - bookQty);

    /// <summary>Ingredient demand in purchase units: Σ grossG × servings × factor / baseServings / gramsPerUnit.</summary>
    public static IReadOnlyDictionary<Guid, decimal> Demand(IEnumerable<DemandInput> lines)
        => lines.Where(l => l.BaseServings > 0 && l.GramsPerUnit > 0)
            .GroupBy(l => l.IngredientId)
            .ToDictionary(g => g.Key, g => Q(g.Sum(l => l.GrossG * l.Servings * l.ServingFactor / l.BaseServings / l.GramsPerUnit)));

    /// <summary>Purchase suggestion (SPEC §5.5): max(0, required − available + minimum stock).</summary>
    public static PurchaseSuggestion Suggest(Guid ingredientId, decimal required, decimal available, decimal minStock)
        => new(ingredientId, required, available, minStock, Q(Math.Max(0, required - Math.Max(available, 0) + minStock)));

    /// <summary>True when the document date falls in a locked month; locking a month also closes every earlier month (A-39).</summary>
    public static bool IsLocked(DateOnly docDate, IEnumerable<DateOnly> lockedPeriods)
    {
        var month = new DateOnly(docDate.Year, docDate.Month, 1);
        return lockedPeriods.Any(p => new DateOnly(p.Year, p.Month, 1) >= month);
    }
}
