using BlueMeal.Application.Authorization;
using BlueMeal.Application.Engines;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using F = BlueMeal.Api.Reports.ReportFilters;

namespace BlueMeal.Api.Reports;

/// <summary>RPT-01 — receipts/issues statistics by ingredient, food group or supplier.</summary>
public sealed class Rpt01ReceiptIssueStats(ReportContext ctx) : IReport
{
    public string Code => "RPT-01";
    public string? Permission => Permissions.Stock.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Warehouse, F.FoodGroup, F.Partner, F.Mode];
    public IReadOnlyList<ReportMode> Modes => [new("ingredient", "Theo nguyên liệu", "By ingredient"), new("group", "Theo nhóm thực phẩm", "By food group"), new("partner", "Theo nhà cung cấp", "By supplier")];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today);
        var db = ctx.Db;
        var ledger = ctx.ScopeWarehouse(db.Set<StockLedger>().AsNoTracking(), l => l.WarehouseId).Where(l => l.DocDate >= from && l.DocDate <= to && (l.Type == LedgerType.In || l.Type == LedgerType.Out));
        if (f.WarehouseId is { } w) ledger = ledger.Where(l => l.WarehouseId == w);
        var q = from l in ledger
                join i in db.Set<Ingredient>().IgnoreQueryFilters() on l.IngredientId equals i.Id
                join r in db.Set<StockReceipt>().IgnoreQueryFilters() on l.DocId equals r.Id into rj
                from r in rj.DefaultIfEmpty()
                select new
                {
                    l.IngredientId, i.Code, i.NameVi, unit = i.PurchaseUnit!.Symbol, groupId = (Guid?)i.Food!.FoodGroupId, groupName = i.Food.FoodGroup!.NameVi, partnerId = r != null ? r.PartnerId : null,
                    partner = r != null ? r.Partner!.NameVi : null, l.Quantity, l.Amount, l.DocDate, l.DocId,
                };
        if (f.FoodGroupId is { } g) q = q.Where(x => x.groupId == g);
        if (f.PartnerId is { } p) q = q.Where(x => x.partnerId == p);
        var data = await q.ToListAsync(ct);
        var mode = f.Mode ?? "ingredient";
        var groups = mode switch
        {
            "group" => data.GroupBy(x => (Key: x.groupId?.ToString() ?? "", Code: "", Name: x.groupName ?? "—", Unit: (string?)null)),
            "partner" => data.Where(x => x.Quantity > 0).GroupBy(x => (Key: x.partnerId?.ToString() ?? "", Code: "", Name: x.partner ?? "—", Unit: (string?)null)),
            _ => data.GroupBy(x => (Key: x.IngredientId.ToString(), x.Code, Name: x.NameVi, Unit: x.unit)),
        };
        var rows = groups.OrderBy(g2 => g2.Key.Code).ThenBy(g2 => g2.Key.Name).Select(g2 => Row.Of(
            ("code", g2.Key.Code), ("name", g2.Key.Name), ("unit", g2.Key.Unit),
            ("inQty", mode == "ingredient" ? g2.Where(x => x.Quantity > 0).Sum(x => x.Quantity) : null), ("inValue", g2.Where(x => x.Quantity > 0).Sum(x => x.Amount)),
            ("outQty", mode == "ingredient" ? -g2.Where(x => x.Quantity < 0).Sum(x => x.Quantity) : null), ("outValue", -g2.Where(x => x.Quantity < 0).Sum(x => x.Amount)),
            ("docs", g2.Select(x => x.DocId).Distinct().Count()), ("firstDate", g2.Min(x => x.DocDate)), ("lastDate", g2.Max(x => x.DocDate)))).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("code", "Mã", "Code", Width: 14), new("name", mode == "partner" ? "Nhà cung cấp" : mode == "group" ? "Nhóm thực phẩm" : "Nguyên liệu", mode == "partner" ? "Supplier" : mode == "group" ? "Food group" : "Ingredient", Width: 32),
                new("unit", "ĐVT", "Unit", Width: 8), new("inQty", "SL nhập", "Qty in", "number", 3), new("inValue", "Giá trị nhập", "Value in", "money", Total: true),
                new("outQty", "SL xuất", "Qty out", "number", 3), new("outValue", "Giá trị xuất", "Value out", "money", Total: true), new("docs", "Số chứng từ", "Documents", "number", Width: 10),
                new("firstDate", "Từ ngày", "First", "date", Width: 12), new("lastDate", "Đến ngày", "Last", "date", Width: 12),
            ],
            Rows = rows,
        };
    }
}

/// <summary>RPT-02 — stock movement summary (nhập – xuất – tồn) for a period.</summary>
public sealed class Rpt02StockMovement(ReportContext ctx) : IReport
{
    public string Code => "RPT-02";
    public string? Permission => Permissions.Stock.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Warehouse, F.FoodGroup];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 30);
        var db = ctx.Db;
        var ledger = ctx.ScopeWarehouse(db.Set<StockLedger>().AsNoTracking(), l => l.WarehouseId).Where(l => l.DocDate <= to);
        if (f.WarehouseId is { } w) ledger = ledger.Where(l => l.WarehouseId == w);
        var sums = await ledger.GroupBy(l => l.IngredientId).Select(g => new
        {
            Id = g.Key,
            openQty = g.Where(x => x.DocDate < from).Sum(x => x.Quantity), openValue = g.Where(x => x.DocDate < from).Sum(x => x.Amount),
            inQty = g.Where(x => x.DocDate >= from && x.Quantity > 0).Sum(x => x.Quantity), inValue = g.Where(x => x.DocDate >= from && x.Quantity > 0).Sum(x => x.Amount),
            outQty = g.Where(x => x.DocDate >= from && x.Quantity < 0).Sum(x => -x.Quantity), outValue = g.Where(x => x.DocDate >= from && x.Quantity < 0).Sum(x => -x.Amount),
        }).ToListAsync(ct);
        var ids = sums.Select(s => s.Id).ToList();
        var ing = await db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters().Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.NameVi, unit = i.PurchaseUnit!.Symbol, groupId = (Guid?)i.Food!.FoodGroupId, groupName = i.Food.FoodGroup!.NameVi }).ToDictionaryAsync(i => i.Id, ct);
        var rows = sums.Where(s => ing.ContainsKey(s.Id) && (f.FoodGroupId is null || ing[s.Id].groupId == f.FoodGroupId))
            .Where(s => s.openQty != 0 || s.inQty != 0 || s.outQty != 0)
            .OrderBy(s => ing[s.Id].Code).Select(s => Row.Of(
                ("code", ing[s.Id].Code), ("name", ing[s.Id].NameVi), ("group", ing[s.Id].groupName), ("unit", ing[s.Id].unit),
                ("openQty", s.openQty), ("openValue", s.openValue), ("inQty", s.inQty), ("inValue", s.inValue), ("outQty", s.outQty), ("outValue", s.outValue),
                ("closeQty", s.openQty + s.inQty - s.outQty), ("closeValue", s.openValue + s.inValue - s.outValue))).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("code", "Mã", "Code", Width: 12), new("name", "Nguyên liệu", "Ingredient", Width: 30), new("group", "Nhóm", "Group", Width: 18), new("unit", "ĐVT", "Unit", Width: 8),
                new("openQty", "Tồn đầu SL", "Opening qty", "number", 3), new("openValue", "Tồn đầu GT", "Opening value", "money", Total: true),
                new("inQty", "Nhập SL", "In qty", "number", 3), new("inValue", "Nhập GT", "In value", "money", Total: true),
                new("outQty", "Xuất SL", "Out qty", "number", 3), new("outValue", "Xuất GT", "Out value", "money", Total: true),
                new("closeQty", "Tồn cuối SL", "Closing qty", "number", 3), new("closeValue", "Tồn cuối GT", "Closing value", "money", Total: true),
            ],
            Rows = rows,
        };
    }
}

/// <summary>RPT-03 — stock-take results: counted vs book, differences, causes.</summary>
public sealed class Rpt03StockTake(ReportContext ctx) : IReport
{
    public string Code => "RPT-03";
    public string? Permission => Permissions.StockTake.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Warehouse];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 90);
        var takes = ctx.ScopeWarehouse(ctx.Db.Set<StockTake>().AsNoTracking(), t => t.WarehouseId).Where(t => t.TakeDate >= from && t.TakeDate <= to && t.Status != StockTakeStatus.Cancelled);
        if (f.WarehouseId is { } w) takes = takes.Where(t => t.WarehouseId == w);
        var data = await takes.SelectMany(t => t.Lines.Select(l => new
        {
            t.DocNo, t.TakeDate, warehouse = t.Warehouse!.NameVi, status = t.Status, l.Ingredient!.Code, l.Ingredient.NameVi, unit = l.Ingredient.PurchaseUnit!.Symbol, l.LotNo,
            l.BookQty, l.ActualQty, l.UnitCost, l.Cause, l.Resolution,
        })).OrderBy(x => x.TakeDate).ThenBy(x => x.DocNo).ThenBy(x => x.Code).ToListAsync(ct);
        var rows = data.Select(x =>
        {
            var diff = InventoryEngine.Difference(x.BookQty, x.ActualQty);
            return Row.Of(("docNo", x.DocNo), ("date", x.TakeDate), ("warehouse", x.warehouse), ("code", x.Code), ("name", x.NameVi), ("unit", x.unit), ("lot", x.LotNo),
                ("book", x.BookQty), ("actual", x.ActualQty), ("diff", diff), ("diffValue", Math.Round(diff * x.UnitCost, 0)), ("cause", x.Cause), ("resolution", x.Resolution),
                ("status", x.status.ToString()));
        }).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("docNo", "Số phiếu", "Doc no.", Width: 18), new("date", "Ngày", "Date", "date", Width: 12), new("warehouse", "Kho", "Warehouse", Width: 14),
                new("code", "Mã", "Code", Width: 12), new("name", "Nguyên liệu", "Ingredient", Width: 28), new("unit", "ĐVT", "Unit", Width: 8), new("lot", "Số lô", "Lot", Width: 12),
                new("book", "Tồn sổ sách", "Book", "number", 3), new("actual", "Tồn thực tế", "Counted", "number", 3), new("diff", "Chênh lệch", "Difference", "number", 3),
                new("diffValue", "Giá trị chênh lệch", "Difference value", "money", Total: true), new("cause", "Nguyên nhân", "Cause", Width: 24), new("resolution", "Xử lý", "Resolution", Width: 24),
                new("status", "Trạng thái", "Status", Width: 12),
            ],
            Rows = rows,
        };
    }
}

/// <summary>RPT-04 — detailed stock card of one ingredient.</summary>
public sealed class Rpt04StockCard(ReportContext ctx) : IReport
{
    public string Code => "RPT-04";
    public string? Permission => Permissions.Stock.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Warehouse, F.Ingredient];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 30);
        var columns = new List<ReportColumn>
        {
            new("date", "Ngày", "Date", "date", Width: 12), new("docNo", "Số chứng từ", "Doc no.", Width: 18), new("type", "Diễn giải", "Description", Width: 20), new("lot", "Số lô", "Lot", Width: 12),
            new("inQty", "Nhập", "In", "number", 3), new("outQty", "Xuất", "Out", "number", 3), new("unitCost", "Đơn giá", "Unit cost", "money"),
            new("amount", "Thành tiền", "Amount", "money"), new("balanceQty", "Tồn", "Balance", "number", 3), new("balanceValue", "Giá trị tồn", "Balance value", "money"),
        };
        if (f.IngredientId is not { } ingId)
            return new ReportResult { Columns = columns, Rows = [], NoteVi = "Chọn nguyên liệu để xem thẻ kho.", NoteEn = "Select an ingredient to view its stock card." };
        var ledger = ctx.ScopeWarehouse(ctx.Db.Set<StockLedger>().AsNoTracking(), l => l.WarehouseId).Where(l => l.IngredientId == ingId);
        if (f.WarehouseId is { } w) ledger = ledger.Where(l => l.WarehouseId == w);
        var openQty = await ledger.Where(l => l.DocDate < from).SumAsync(l => (decimal?)l.Quantity, ct) ?? 0;
        var openValue = await ledger.Where(l => l.DocDate < from).SumAsync(l => (decimal?)l.Amount, ct) ?? 0;
        var moves = await ledger.Where(l => l.DocDate >= from && l.DocDate <= to).OrderBy(l => l.DocDate).ThenBy(l => l.At).ThenBy(l => l.Id)
            .Select(l => new { l.DocDate, l.DocNo, l.Type, lot = ctx.Db.Set<StockLot>().Where(x => x.Id == l.LotId).Select(x => x.LotNo).FirstOrDefault(), l.Quantity, l.UnitCost, l.Amount }).ToListAsync(ct);
        var names = new Dictionary<LedgerType, string> { [LedgerType.In] = ctx.T("Nhập kho", "Receipt"), [LedgerType.Out] = ctx.T("Xuất kho", "Issue"), [LedgerType.AdjustIn] = ctx.T("Điều chỉnh tăng", "Adjustment +"), [LedgerType.AdjustOut] = ctx.T("Điều chỉnh giảm", "Adjustment −") };
        var rows = new List<Dictionary<string, object?>> { Row.Of(("type", ctx.T("Tồn đầu kỳ", "Opening balance")), ("balanceQty", openQty), ("balanceValue", openValue)) };
        decimal q = openQty, v = openValue;
        foreach (var m in moves)
        {
            q += m.Quantity; v += m.Amount;
            rows.Add(Row.Of(("date", m.DocDate), ("docNo", m.DocNo), ("type", names[m.Type]), ("lot", m.lot), ("inQty", m.Quantity > 0 ? m.Quantity : null), ("outQty", m.Quantity < 0 ? -m.Quantity : null),
                ("unitCost", m.UnitCost), ("amount", Math.Abs(m.Amount)), ("balanceQty", q), ("balanceValue", v)));
        }
        rows.Add(Row.Of(("type", ctx.T("Tồn cuối kỳ", "Closing balance")), ("inQty", moves.Where(m => m.Quantity > 0).Sum(m => m.Quantity)), ("outQty", -moves.Where(m => m.Quantity < 0).Sum(m => m.Quantity)), ("balanceQty", q), ("balanceValue", v)));
        var ing = await ctx.Db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters().Where(i => i.Id == ingId).Select(i => new { i.Code, i.NameVi, unit = i.PurchaseUnit!.Symbol }).FirstOrDefaultAsync(ct);
        return new ReportResult { Columns = columns, Rows = rows, NoteVi = ing is null ? null : $"Nguyên liệu: {ing.Code} — {ing.NameVi} ({ing.unit})", NoteEn = ing is null ? null : $"Ingredient: {ing.Code} — {ing.NameVi} ({ing.unit})" };
    }
}

/// <summary>RPT-05 — lots expiring / expired and ingredients below minimum stock.</summary>
public sealed class Rpt05ExpiryAndMinimum(ReportContext ctx) : IReport
{
    public string Code => "RPT-05";
    public string? Permission => Permissions.Stock.View;
    public IReadOnlyList<string> Filters => [F.Warehouse, F.Days, F.FoodGroup];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var today = ctx.Today;
        var limit = today.AddDays(f.Days ?? 7);
        var lots = ctx.ScopeWarehouse(ctx.Db.Set<StockLot>().AsNoTracking(), l => l.WarehouseId).Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate <= limit);
        if (f.WarehouseId is { } w) lots = lots.Where(l => l.WarehouseId == w);
        if (f.FoodGroupId is { } g) lots = lots.Where(l => l.Ingredient!.Food!.FoodGroupId == g);
        var lotRows = await lots.OrderBy(l => l.ExpiryDate).Select(l => new
        {
            l.Ingredient!.Code, l.Ingredient.NameVi, unit = l.Ingredient.PurchaseUnit!.Symbol, warehouse = ctx.Db.Set<Warehouse>().Where(x => x.Id == l.WarehouseId).Select(x => x.NameVi).FirstOrDefault(),
            l.LotNo, l.ExpiryDate, l.Quantity, value = l.Quantity * l.UnitCost, l.Ingredient.MinStock,
        }).ToListAsync(ct);
        var balances = ctx.ScopeWarehouse(ctx.Db.Set<StockBalance>().AsNoTracking(), b => b.WarehouseId);
        if (f.WarehouseId is { } w2) balances = balances.Where(b => b.WarehouseId == w2);
        var low = await (from i in ctx.Db.Set<Ingredient>().AsNoTracking()
                         where i.IsActive && i.MinStock > 0 && (f.FoodGroupId == null || i.Food!.FoodGroupId == f.FoodGroupId)
                         let qty = balances.Where(b => b.IngredientId == i.Id).Sum(b => (decimal?)b.Quantity) ?? 0
                         where qty < i.MinStock
                         orderby i.Code
                         select new { i.Code, i.NameVi, unit = i.PurchaseUnit!.Symbol, qty, i.MinStock }).ToListAsync(ct);
        var rows = lotRows.Select(l => Row.Of(
            ("kind", l.ExpiryDate < today ? ctx.T("Đã hết hạn", "Expired") : ctx.T("Sắp hết hạn", "Expiring")), ("code", l.Code), ("name", l.NameVi), ("unit", l.unit), ("warehouse", l.warehouse), ("lot", l.LotNo),
            ("expiry", l.ExpiryDate), ("daysLeft", l.ExpiryDate!.Value.DayNumber - today.DayNumber), ("qty", l.Quantity), ("value", Math.Round(l.value, 0)), ("min", null))).ToList();
        rows.AddRange(low.Select(l => Row.Of(("kind", ctx.T("Dưới tồn tối thiểu", "Below minimum")), ("code", l.Code), ("name", l.NameVi), ("unit", l.unit), ("qty", l.qty), ("min", l.MinStock))));
        return new ReportResult
        {
            Columns =
            [
                new("kind", "Loại", "Kind", Width: 18), new("code", "Mã", "Code", Width: 12), new("name", "Nguyên liệu", "Ingredient", Width: 28), new("unit", "ĐVT", "Unit", Width: 8),
                new("warehouse", "Kho", "Warehouse", Width: 14), new("lot", "Số lô", "Lot", Width: 12), new("expiry", "HSD", "Expiry", "date", Width: 12), new("daysLeft", "Còn (ngày)", "Days left", "number", Width: 10),
                new("qty", "Số lượng", "Quantity", "number", 3), new("value", "Giá trị", "Value", "money", Total: true), new("min", "Tồn tối thiểu", "Minimum", "number", 3),
            ],
            Rows = rows,
        };
    }
}
