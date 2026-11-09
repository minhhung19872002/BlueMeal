using BlueMeal.Application.Engines;
using BlueMeal.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BlueMeal.UnitTests.Engines;

public class InventoryEngineTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static LotState Lot(Guid id, string no, int expDay, int recvDay, decimal qty, decimal cost)
        => new(id, no, expDay == 0 ? null : new DateOnly(2026, 11, expDay), new DateOnly(2026, 11, recvDay), qty, cost);

    [Fact] // 10 kg × 50.000 + 10 kg × 60.000 → 20 kg, 1.100.000 ₫, average 55.000 ₫
    public void Moving_average_after_two_receipts()
    {
        var b = InventoryEngine.Receive(BalanceState.Empty, 10, 50_000);
        b = InventoryEngine.Receive(b, 10, 60_000);
        b.Quantity.Should().Be(20);
        b.Value.Should().Be(1_100_000);
        b.AverageCost.Should().Be(55_000);
    }

    [Fact] // 3 kg @ 30.000 + 7 kg @ 40.000 = 370.000 / 10 = 37.000
    public void Moving_average_with_uneven_quantities()
    {
        var b = InventoryEngine.Receive(InventoryEngine.Receive(BalanceState.Empty, 3, 30_000), 7, 40_000);
        b.AverageCost.Should().Be(37_000);
    }

    [Fact] // issue 5 of 20 kg @ 55.000 → 275.000; remaining 15 kg, 825.000
    public void Issue_moving_average_values_at_average_cost()
    {
        var before = new BalanceState(20, 1_100_000);
        var r = InventoryEngine.Issue(before, [Lot(A, "L1", 20, 1, 10, 50_000), Lot(B, "L2", 25, 2, 10, 60_000)], 5, InventoryCostMethod.MovingAverage, false);
        r.Amount.Should().Be(275_000);
        r.UnitCost.Should().Be(55_000);
        r.After.Should().Be(new BalanceState(15, 825_000));
        r.Allocations.Should().ContainSingle().Which.LotId.Should().Be(A);
    }

    [Fact] // FIFO: 12 kg = 10 kg L1 @ 50.000 + 2 kg L2 @ 60.000 = 620.000
    public void Issue_fifo_uses_lot_costs_in_receipt_order()
    {
        var before = new BalanceState(20, 1_100_000);
        var r = InventoryEngine.Issue(before, [Lot(B, "L2", 15, 2, 10, 60_000), Lot(A, "L1", 25, 1, 10, 50_000)], 12, InventoryCostMethod.Fifo, false);
        r.Amount.Should().Be(620_000);
        r.Allocations.Select(a => (a.LotId, a.Quantity)).Should().Equal((A, 10m), (B, 2m));
        r.After.Should().Be(new BalanceState(8, 480_000));
        r.UnitCost.Should().Be(51_666.6667m);
    }

    [Fact] // FEFO: lot with the earliest expiry first even if received later; lots without expiry last
    public void Fefo_orders_by_expiry_then_receipt()
    {
        var lots = new[] { Lot(A, "L1", 0, 1, 5, 1), Lot(B, "L2", 20, 3, 5, 1), Lot(C, "L3", 12, 5, 5, 1) };
        InventoryEngine.FefoOrder(lots).Select(l => l.LotId).Should().Equal(C, B, A);
        InventoryEngine.SuggestFefo(lots, 7).Select(s => (s.Lot.LotId, s.Quantity)).Should().Equal((C, 5m), (B, 2m));
    }

    [Fact]
    public void Fefo_ignores_empty_lots()
        => InventoryEngine.FefoOrder([Lot(A, "L1", 5, 1, 0, 1), Lot(B, "L2", 9, 1, 1, 1)]).Should().ContainSingle().Which.LotId.Should().Be(B);

    [Fact]
    public void Requested_lot_is_consumed_first()
    {
        var r = InventoryEngine.Issue(new BalanceState(10, 100), [Lot(A, "L1", 5, 1, 5, 10), Lot(B, "L2", 9, 2, 5, 10)], 3, InventoryCostMethod.MovingAverage, false, B);
        r.Allocations.Should().ContainSingle().Which.LotId.Should().Be(B);
    }

    [Fact] // not enough stock and negative stock disabled → nothing issued, shortage reported
    public void Negative_stock_is_blocked_by_default()
    {
        var before = new BalanceState(4, 200_000);
        var r = InventoryEngine.Issue(before, [Lot(A, "L1", 5, 1, 4, 50_000)], 6.5m, InventoryCostMethod.MovingAverage, false);
        r.Shortage.Should().Be(2.5m);
        r.Allocations.Should().BeEmpty();
        r.After.Should().Be(before);
    }

    [Fact] // allowed: 6 kg from 4 kg @ 50.000 → whole value 200.000 + 2 kg @ average 50.000 = 300.000; balance −2 kg
    public void Negative_stock_when_allowed()
    {
        var r = InventoryEngine.Issue(new BalanceState(4, 200_000), [Lot(A, "L1", 5, 1, 4, 50_000)], 6, InventoryCostMethod.MovingAverage, true);
        r.Shortage.Should().Be(0);
        r.Amount.Should().Be(300_000);
        r.After.Quantity.Should().Be(-2);
        r.After.Value.Should().Be(-100_000);
        // next receipt of 5 kg @ 60.000 resets the valuation: 3 kg × 60.000
        InventoryEngine.Receive(r.After, 5, 60_000).Should().Be(new BalanceState(3, 180_000));
    }

    [Fact] // 3 kg @ 10.000/3 each: issuing everything takes the full value, no rounding residue
    public void Issuing_all_stock_clears_the_value()
    {
        var b = InventoryEngine.Receive(BalanceState.Empty, 3, 3_333.33m); // value 9.999,99
        var r = InventoryEngine.Issue(b, [], 3, InventoryCostMethod.MovingAverage, false);
        r.Amount.Should().Be(9_999.99m);
        r.After.Should().Be(new BalanceState(0, 0));
    }

    [Fact]
    public void Receive_rejects_invalid_quantities()
    {
        var act = () => InventoryEngine.Receive(BalanceState.Empty, 0, 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
        var act2 = () => InventoryEngine.Receive(BalanceState.Empty, 1, -1);
        act2.Should().Throw<ArgumentOutOfRangeException>();
        var act3 = () => InventoryEngine.Issue(BalanceState.Empty, [], 0, InventoryCostMethod.Fifo, true);
        act3.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory] // stock-take difference = actual − book
    [InlineData(10, 12.0, 2)]
    [InlineData(10, 7.5, -2.5)]
    [InlineData(10, null, 0)]
    public void Stock_take_difference(decimal book, double? actual, decimal expected)
        => InventoryEngine.Difference(book, actual is null ? null : (decimal)actual).Should().Be(expected);

    [Fact] // 150 g gross for 1 serving × 200 servings × 1,2 / 1000 g per kg = 36 kg; + 2 menus of the same ingredient
    public void Demand_from_menus_in_purchase_units()
    {
        var d = InventoryEngine.Demand([
            new DemandInput(A, 150, 1, 200, 1.2m, 1000),
            new DemandInput(A, 500, 10, 100, 1, 1000), // 500 g for 10 servings → 5 kg for 100
            new DemandInput(B, 30, 1, 100, 1, 1),      // unit = gram
            new DemandInput(C, 30, 0, 100, 1, 1),      // invalid base servings ignored
        ]);
        d[A].Should().Be(41);
        d[B].Should().Be(3000);
        d.Should().NotContainKey(C);
    }

    [Theory] // max(0, required − available + min)
    [InlineData(40, 10, 5, 35)]
    [InlineData(10, 30, 5, 0)]
    [InlineData(10, -4, 0, 10)] // negative balance counts as zero
    public void Purchase_suggestion(decimal req, decimal avail, decimal min, decimal expected)
        => InventoryEngine.Suggest(A, req, avail, min).Suggested.Should().Be(expected);

    [Fact]
    public void Period_lock_closes_the_month_and_earlier_ones()
    {
        var locks = new[] { new DateOnly(2026, 10, 1) };
        InventoryEngine.IsLocked(new DateOnly(2026, 10, 31), locks).Should().BeTrue();
        InventoryEngine.IsLocked(new DateOnly(2026, 9, 2), locks).Should().BeTrue();
        InventoryEngine.IsLocked(new DateOnly(2026, 11, 1), locks).Should().BeFalse();
        InventoryEngine.IsLocked(new DateOnly(2026, 11, 1), []).Should().BeFalse();
    }
}
