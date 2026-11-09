using BlueMeal.Application.Authorization;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using F = BlueMeal.Api.Reports.ReportFilters;

namespace BlueMeal.Api.Reports;

public sealed record CostSheetRow(string Period, DateOnly FirstDate, Guid? CustomerId, string? Customer, int Menus, int Servings, decimal PlannedFood, decimal PlannedOverhead, decimal PlannedTotal,
    decimal ActualFood, decimal ActualTotal, decimal Revenue, decimal Variance, decimal Margin, decimal? PlannedPerServing, decimal? ActualPerServing, decimal? Budget);

/// <summary>
/// Meal cost sheet (SPEC §7.7) by day/week/month × customer: servings, planned cost (menu snapshot), actual cost (posted issues of
/// the menus + planned overhead), revenue at the agreed price, variance and margin.
/// </summary>
public sealed class CostSheetService(ReportContext ctx)
{
    public async Task<IReadOnlyList<CostSheetRow>> BuildAsync(DateOnly from, DateOnly to, string? mode, ReportFilter f, CancellationToken ct)
    {
        var menus = await ctx.MenusAsync(from, to, f, ct);
        var ids = menus.Select(m => m.Id).ToList();
        var actual = await ctx.Db.Set<StockIssue>().AsNoTracking().Where(i => i.MenuId != null && ids.Contains(i.MenuId.Value) && i.Status == StockDocStatus.Posted)
            .GroupBy(i => i.MenuId!.Value).Select(g => new { g.Key, Amount = g.Sum(x => x.TotalAmount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
        var customerIds = menus.Where(m => m.CustomerId != null).Select(m => m.CustomerId!.Value).Distinct().ToList();
        var prices = await ctx.Db.Set<CustomerPrice>().AsNoTracking().Where(p => customerIds.Contains(p.CustomerId) && p.ValidFrom <= to && (p.ValidTo == null || p.ValidTo >= from)).ToListAsync(ct);
        var agreed = await ctx.Db.Set<Customer>().AsNoTracking().IgnoreQueryFilters().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.AgreedPricePerServing, ct);
        decimal PriceOf(ReportContext.MenuWithSnapshot m)
        {
            if (m.CustomerId is not { } cid) return 0;
            var p = prices.Where(x => x.CustomerId == cid && x.ValidFrom <= m.MenuDate && (x.ValidTo == null || x.ValidTo >= m.MenuDate)
                                      && (x.MealShiftId == null || x.MealShiftId == m.MealShiftId) && (x.SubjectGroupId == null || x.SubjectGroupId == m.SubjectGroupId))
                .OrderByDescending(x => (x.MealShiftId != null ? 2 : 0) + (x.SubjectGroupId != null ? 1 : 0)).ThenByDescending(x => x.ValidFrom).FirstOrDefault();
            return p?.PricePerServing ?? agreed.GetValueOrDefault(cid) ?? 0;
        }
        return menus.GroupBy(m => (Period: ReportContext.PeriodKey(m.MenuDate, mode), m.CustomerId, m.Customer)).OrderBy(g => g.Min(m => m.MenuDate)).ThenBy(g => g.Key.Customer)
            .Select(g =>
            {
                var servings = g.Sum(m => m.Servings);
                var plannedFood = g.Sum(m => (m.Snapshot?.Cost?.FoodCostPerServing ?? m.CostPerServing ?? 0) * m.Servings);
                var plannedOverhead = g.Sum(m => (m.Snapshot?.Cost?.OverheadPerServing ?? 0) * m.Servings);
                var plannedTotal = g.Sum(m => (m.CostPerServing ?? m.Snapshot?.Cost?.CostPerServing ?? 0) * m.Servings);
                var actualFood = g.Sum(m => actual.GetValueOrDefault(m.Id));
                var actualTotal = actualFood + plannedOverhead;
                var revenue = g.Sum(m => PriceOf(m) * m.Servings);
                var budgets = g.Where(m => m.Snapshot?.Cost?.Budget is > 0).ToList();
                return new CostSheetRow(g.Key.Period, g.Min(m => m.MenuDate), g.Key.CustomerId, g.Key.Customer, g.Count(), servings, Math.Round(plannedFood, 0), Math.Round(plannedOverhead, 0),
                    Math.Round(plannedTotal, 0), Math.Round(actualFood, 0), Math.Round(actualTotal, 0), Math.Round(revenue, 0), Math.Round(actualTotal - plannedTotal, 0), Math.Round(revenue - actualTotal, 0),
                    servings > 0 ? Math.Round(plannedTotal / servings, 0) : null, servings > 0 && actualFood > 0 ? Math.Round(actualTotal / servings, 0) : null,
                    budgets.Count > 0 ? Math.Round(budgets.Average(m => m.Snapshot!.Cost!.Budget!.Value), 0) : null);
            }).ToList();
    }
}

/// <summary>RPT-11 — meal cost planned vs actual by customer.</summary>
public sealed class Rpt11MealCost(ReportContext ctx, CostSheetService sheet) : IReport
{
    public string Code => "RPT-11";
    public string? Permission => Permissions.Cost.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Customer, F.Subject, F.Mode];
    public IReadOnlyList<ReportMode> Modes => ReportContext.PeriodModes;

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 30);
        var rows = (await sheet.BuildAsync(from, to, f.Mode ?? "week", f, ct)).Select(r => Row.Of(
            ("period", r.Period), ("customer", r.Customer), ("menus", r.Menus), ("servings", r.Servings), ("plannedTotal", r.PlannedTotal), ("plannedPer", r.PlannedPerServing), ("budget", r.Budget),
            ("actualFood", r.ActualFood), ("actualTotal", r.ActualTotal), ("actualPer", r.ActualPerServing), ("variance", r.Variance), ("revenue", r.Revenue), ("margin", r.Margin))).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("period", "Kỳ", "Period", Width: 12), new("customer", "Khách hàng", "Customer", Width: 24), new("menus", "Số thực đơn", "Menus", "number", Width: 10),
                new("servings", "Số suất", "Servings", "number", Total: true), new("plannedTotal", "Chi phí kế hoạch", "Planned cost", "money", Total: true), new("plannedPer", "KH/suất", "Planned/serving", "money"),
                new("budget", "Ngân sách/suất", "Budget/serving", "money"), new("actualFood", "Thực phẩm thực xuất", "Actual food issued", "money", Total: true),
                new("actualTotal", "Chi phí thực tế", "Actual cost", "money", Total: true), new("actualPer", "TT/suất", "Actual/serving", "money"), new("variance", "Chênh lệch TT − KH", "Variance", "money", Total: true),
                new("revenue", "Doanh thu", "Revenue", "money", Total: true), new("margin", "Lãi gộp", "Margin", "money", Total: true),
            ],
            Rows = rows,
            NoteVi = "Chi phí thực tế = giá trị phiếu xuất đã ghi sổ của thực đơn + chi phí chung kế hoạch. Doanh thu theo đơn giá thỏa thuận với khách hàng.",
            NoteEn = "Actual cost = value of posted issues of the menus + planned overhead. Revenue at the customer's agreed price.",
        };
    }
}

/// <summary>RPT-12 — ingredient price changes over a period.</summary>
public sealed class Rpt12PriceChanges(ReportContext ctx) : IReport
{
    public string Code => "RPT-12";
    public string? Permission => Permissions.Cost.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.FoodGroup, F.Partner, F.Ingredient];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 90);
        var q = ctx.Db.Set<IngredientPriceHistory>().AsNoTracking().Where(p => p.EffectiveDate >= from && p.EffectiveDate <= to);
        if (f.IngredientId is { } i) q = q.Where(p => p.IngredientId == i);
        if (f.PartnerId is { } pt) q = q.Where(p => p.PartnerId == pt);
        var data = await (from p in q
                          join ing in ctx.Db.Set<Ingredient>().IgnoreQueryFilters() on p.IngredientId equals ing.Id
                          where f.FoodGroupId == null || ing.Food!.FoodGroupId == f.FoodGroupId
                          select new { p.IngredientId, ing.Code, ing.NameVi, unit = ing.PurchaseUnit!.Symbol, ing.ReferencePrice, p.EffectiveDate, p.UnitPrice }).ToListAsync(ct);
        var rows = data.GroupBy(x => (x.IngredientId, x.Code, x.NameVi, x.unit, x.ReferencePrice)).OrderBy(g => g.Key.Code).Select(g =>
        {
            var ordered = g.OrderBy(x => x.EffectiveDate).ToList();
            var first = ordered[0].UnitPrice;
            var last = ordered[^1].UnitPrice;
            return Row.Of(("code", g.Key.Code), ("name", g.Key.NameVi), ("unit", g.Key.unit), ("reference", g.Key.ReferencePrice), ("first", first), ("last", last), ("min", g.Min(x => x.UnitPrice)),
                ("max", g.Max(x => x.UnitPrice)), ("avg", Math.Round(g.Average(x => x.UnitPrice), 0)), ("change", first == 0 ? null : Math.Round((last - first) / first * 100, 1)), ("count", g.Count()),
                ("lastDate", ordered[^1].EffectiveDate));
        }).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("code", "Mã", "Code", Width: 12), new("name", "Nguyên liệu", "Ingredient", Width: 28), new("unit", "ĐVT", "Unit", Width: 8), new("reference", "Giá tham chiếu", "Reference", "money"),
                new("first", "Giá đầu kỳ", "First price", "money"), new("last", "Giá cuối kỳ", "Last price", "money"), new("min", "Thấp nhất", "Min", "money"), new("max", "Cao nhất", "Max", "money"),
                new("avg", "Bình quân", "Average", "money"), new("change", "Biến động (%)", "Change (%)", "percent", 1), new("count", "Số lần nhập", "Receipts", "number", Width: 10),
                new("lastDate", "Ngày gần nhất", "Last date", "date", Width: 12),
            ],
            Rows = rows,
        };
    }
}

/// <summary>RPT-13 — three-step food inspection log.</summary>
public sealed class Rpt13InspectionLog(ReportContext ctx) : IReport
{
    public string Code => "RPT-13";
    public string? Permission => Permissions.Inspection.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Mode];
    public IReadOnlyList<ReportMode> Modes => [new("all", "Cả 3 bước", "All steps"), new("1", "Bước 1", "Step 1"), new("2", "Bước 2", "Step 2"), new("3", "Bước 3", "Step 3")];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 7);
        var start = ReportContext.StartOf(from);
        var end = ReportContext.StartOf(to.AddDays(1));
        var mode = f.Mode ?? "all";
        var rows = new List<Dictionary<string, object?>>();
        if (mode is "all" or "1")
            rows.AddRange((await ctx.Db.Set<FoodInspectionStep1>().AsNoTracking().Where(x => x.InspectedAt >= start && x.InspectedAt < end).OrderBy(x => x.InspectedAt)
                .Select(x => new { x.InspectedAt, x.Ingredient!.NameVi, partner = ctx.Db.Set<Partner>().Where(p => p.Id == x.PartnerId).Select(p => p.NameVi).FirstOrDefault(), x.Quantity, x.LotNo, x.ExpiryDate, x.Temperature, x.OriginDocsOk, x.SensoryNote, x.Result, x.Action, x.Inspector })
                .ToListAsync(ct)).Select(x => Row.Of(("step", 1), ("at", x.InspectedAt), ("item", x.NameVi), ("detail", string.Join("; ", new[] { x.partner, x.LotNo is null ? null : ctx.T("Lô ", "Lot ") + x.LotNo, x.ExpiryDate is null ? null : ctx.T("HSD ", "Exp ") + x.ExpiryDate.Value.ToString("dd/MM/yyyy"), x.OriginDocsOk ? ctx.T("Đủ giấy tờ", "Documents OK") : ctx.T("Thiếu giấy tờ", "Missing documents") }.Where(s => s != null))),
                    ("temp", x.Temperature), ("sensory", x.SensoryNote), ("result", x.Result.ToString()), ("action", x.Action), ("inspector", x.Inspector))));
        if (mode is "all" or "2")
            rows.AddRange((await ctx.Db.Set<FoodInspectionStep2>().AsNoTracking().Where(x => x.InspectDate >= from && x.InspectDate <= to).OrderBy(x => x.InspectDate).ThenBy(x => x.CookStart)
                .Select(x => new { x.InspectDate, x.CookStart, x.CookEnd, dish = x.Dish!.NameVi, x.Hygiene, x.Participants, x.SensoryNote, x.Result, x.Action, x.Inspector }).ToListAsync(ct))
                .Select(x => Row.Of(("step", 2), ("at", (object?)x.CookStart ?? x.InspectDate), ("item", x.dish), ("detail", string.Join("; ", new[] { x.Participants, x.Hygiene, x.CookEnd is null ? null : ctx.T("Kết thúc ", "End ") + x.CookEnd.Value.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm") }.Where(s => !string.IsNullOrEmpty(s)))),
                    ("sensory", x.SensoryNote), ("result", x.Result.ToString()), ("action", x.Action), ("inspector", x.Inspector))));
        if (mode is "all" or "3")
            rows.AddRange((await ctx.Db.Set<FoodInspectionStep3>().AsNoTracking().Where(x => x.InspectDate >= from && x.InspectDate <= to).OrderBy(x => x.InspectDate).ThenBy(x => x.PortionTime)
                .Select(x => new { x.InspectDate, x.PortionTime, dish = x.Dish!.NameVi, x.Temperature, x.Utensils, x.SensoryNote, x.Result, x.Action, x.Inspector }).ToListAsync(ct))
                .Select(x => Row.Of(("step", 3), ("at", (object?)x.PortionTime ?? x.InspectDate), ("item", x.dish), ("detail", x.Utensils), ("temp", x.Temperature), ("sensory", x.SensoryNote),
                    ("result", x.Result.ToString()), ("action", x.Action), ("inspector", x.Inspector))));
        return new ReportResult
        {
            Columns =
            [
                new("step", "Bước", "Step", "number", Width: 6), new("at", "Thời gian", "Time", "datetime", Width: 16), new("item", "Thực phẩm / món", "Food / dish", Width: 28),
                new("detail", "Thông tin", "Details", Width: 40), new("temp", "Nhiệt độ (°C)", "Temp (°C)", "number", 1, 10), new("sensory", "Cảm quan", "Sensory", Width: 24),
                new("result", "Kết quả", "Result", Width: 12), new("action", "Biện pháp xử lý", "Action", Width: 24), new("inspector", "Người kiểm tra", "Inspector", Width: 18),
            ],
            Rows = rows,
        };
    }
}

/// <summary>RPT-14 — food sample retention and disposal log.</summary>
public sealed class Rpt14SampleLog(ReportContext ctx) : IReport
{
    public string Code => "RPT-14";
    public string? Permission => Permissions.Sample.View;
    public IReadOnlyList<string> Filters => [F.DateRange];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 30);
        var start = ReportContext.StartOf(from);
        var end = ReportContext.StartOf(to.AddDays(1));
        var now = ctx.Now;
        var data = await ctx.Db.Set<FoodSample>().AsNoTracking().Where(s => s.SampledAt >= start && s.SampledAt < end).OrderBy(s => s.SampledAt)
            .Select(s => new { s.Code, dish = s.Dish!.NameVi, menu = ctx.Db.Set<Menu>().Where(m => m.Id == s.MenuId).Select(m => m.Code).FirstOrDefault(), s.SampledAt, s.WeightG, s.StorageTemp, s.Sampler, s.PlannedDisposeAt, s.DisposedAt, s.Disposer, s.Note })
            .ToListAsync(ct);
        var rows = data.Select(s => Row.Of(("code", s.Code), ("dish", s.dish), ("menu", s.menu), ("sampledAt", s.SampledAt), ("weight", s.WeightG), ("temp", s.StorageTemp), ("sampler", s.Sampler),
            ("planned", s.PlannedDisposeAt), ("disposedAt", s.DisposedAt), ("disposer", s.Disposer),
            ("state", s.DisposedAt != null ? ctx.T("Đã hủy", "Disposed") : s.PlannedDisposeAt <= now ? ctx.T("Đến hạn hủy", "Due") : ctx.T("Đang lưu", "Kept")), ("note", s.Note))).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("code", "Mã mẫu", "Sample", Width: 18), new("dish", "Món ăn", "Dish", Width: 26), new("menu", "Thực đơn", "Menu", Width: 18), new("sampledAt", "Lấy mẫu", "Sampled", "datetime", Width: 16),
                new("weight", "Khối lượng (g)", "Weight (g)", "number", Width: 10), new("temp", "Nhiệt độ BQ (°C)", "Storage (°C)", "number", 1, 10), new("sampler", "Người lấy", "Sampler", Width: 18),
                new("planned", "Hủy dự kiến", "Planned disposal", "datetime", Width: 16), new("disposedAt", "Hủy thực tế", "Disposed", "datetime", Width: 16), new("disposer", "Người hủy", "Disposer", Width: 18),
                new("state", "Tình trạng", "State", Width: 12), new("note", "Ghi chú", "Note", Width: 24),
            ],
            Rows = rows,
        };
    }
}

/// <summary>RPT-15 — supplier certificates expiring within N days (or expired).</summary>
public sealed class Rpt15ExpiringCertificates(ReportContext ctx) : IReport
{
    public string Code => "RPT-15";
    public string? Permission => Permissions.Partner.View;
    public IReadOnlyList<string> Filters => [F.Days, F.Partner];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var today = ctx.Today;
        var limit = today.AddDays(f.Days ?? 30);
        var q = ctx.Db.Set<PartnerCertificate>().AsNoTracking().Where(c => c.ExpiryDate != null && c.ExpiryDate <= limit && c.Partner!.IsActive);
        if (f.PartnerId is { } p) q = q.Where(c => c.PartnerId == p);
        var data = await q.OrderBy(c => c.ExpiryDate).Select(c => new { partner = c.Partner!.NameVi, partnerCode = c.Partner.Code, c.CertificateType, c.Number, c.Issuer, c.IssuedDate, c.ExpiryDate }).ToListAsync(ct);
        var rows = data.Select(c => Row.Of(("partnerCode", c.partnerCode), ("partner", c.partner), ("type", c.CertificateType), ("number", c.Number), ("issuer", c.Issuer), ("issued", c.IssuedDate),
            ("expiry", c.ExpiryDate), ("daysLeft", c.ExpiryDate!.Value.DayNumber - today.DayNumber), ("state", c.ExpiryDate < today ? ctx.T("Đã hết hạn", "Expired") : ctx.T("Sắp hết hạn", "Expiring")))).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("partnerCode", "Mã NCC", "Supplier code", Width: 12), new("partner", "Nhà cung cấp", "Supplier", Width: 28), new("type", "Loại giấy", "Type", Width: 20), new("number", "Số", "Number", Width: 16),
                new("issuer", "Nơi cấp", "Issuer", Width: 22), new("issued", "Ngày cấp", "Issued", "date", Width: 12), new("expiry", "Hết hạn", "Expiry", "date", Width: 12),
                new("daysLeft", "Còn (ngày)", "Days left", "number", Width: 10), new("state", "Tình trạng", "State", Width: 12),
            ],
            Rows = rows,
        };
    }
}

/// <summary>RPT-16 — system access / activity log (admin).</summary>
public sealed class Rpt16AuditLog(ReportContext ctx) : IReport
{
    public string Code => "RPT-16";
    public string? Permission => Permissions.Audit.View;
    public IReadOnlyList<string> Filters => [F.DateRange, F.Q];

    public async Task<ReportResult> RunAsync(ReportFilter f, CancellationToken ct)
    {
        var (from, to) = f.Range(ctx.Today, 7);
        var start = ReportContext.StartOf(from);
        var end = ReportContext.StartOf(to.AddDays(1));
        var q = ctx.Db.Set<AuditLog>().AsNoTracking().Where(a => a.At >= start && a.At < end);
        if (f.UserId is { } u) q = q.Where(a => a.UserId == u);
        if (!string.IsNullOrWhiteSpace(f.Q)) q = q.Where(a => EF.Functions.ILike(a.EntityType ?? "", "%" + f.Q + "%") || EF.Functions.ILike(a.UserName ?? "", "%" + f.Q + "%") || EF.Functions.ILike(a.Message ?? "", "%" + f.Q + "%"));
        var data = await q.OrderByDescending(a => a.At).Take(20_000).Select(a => new { a.At, a.UserName, a.Ip, a.Action, a.EntityType, a.EntityId, a.Success, a.Message }).ToListAsync(ct);
        var rows = data.Select(a => Row.Of(("at", a.At), ("user", a.UserName), ("ip", a.Ip), ("action", a.Action.ToString()), ("entity", a.EntityType), ("entityId", a.EntityId),
            ("success", a.Success ? ctx.T("Thành công", "Success") : ctx.T("Thất bại", "Failed")), ("message", a.Message))).ToList();
        return new ReportResult
        {
            Columns =
            [
                new("at", "Thời điểm", "Time", "datetime", Width: 16), new("user", "Người dùng", "User", Width: 16), new("ip", "IP", "IP", Width: 14), new("action", "Thao tác", "Action", Width: 14),
                new("entity", "Đối tượng", "Entity", Width: 18), new("entityId", "Mã bản ghi", "Record id", Width: 36), new("success", "Kết quả", "Result", Width: 10), new("message", "Ghi chú", "Message", Width: 40),
            ],
            Rows = rows,
        };
    }
}
