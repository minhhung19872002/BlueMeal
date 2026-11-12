using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.IntegrationTests.Infrastructure;
using ClosedXML.Excel;

namespace BlueMeal.IntegrationTests;

/// <summary>Phase P6 acceptance: 16 reports viewable and exported to Excel, analysis, cost sheet, dashboard, background export, favorites, scopes.</summary>
[Collection(nameof(ApiCollection))]
public sealed class ReportTests(ApiFactory f)
{
    private static int _seq;

    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var text = await r.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private static readonly string[] Codes = Enumerable.Range(1, 16).Select(i => $"RPT-{i:00}").ToArray();

    private async Task<(HttpClient C, string Wh, string Ing)> StockSetup()
    {
        var c = await f.AdminAsync();
        var n = Interlocked.Increment(ref _seq);
        var wh = await J(await c.PostAsJsonAsync("/api/v1/warehouses", new { code = $"BC-{n}-{Guid.NewGuid():N}"[..20], nameVi = $"Kho báo cáo {n}", type = "Dry", isActive = true }));
        var ing = await J(await c.PostAsJsonAsync("/api/v1/ingredients", new { code = $"NL-BC-{n}-{Guid.NewGuid():N}"[..20], nameVi = $"Nguyên liệu báo cáo {n}", gramsPerUnit = 1000, minStock = 0, referencePrice = 10000, isActive = true }));
        return (c, wh.GetProperty("id").GetString()!, ing.GetProperty("id").GetString()!);
    }

    private static async Task Receive(HttpClient c, string wh, string ing, string date, decimal qty, decimal price)
    {
        var id = (await J(await c.PostAsJsonAsync("/api/v1/stock-receipts", new { docDate = date, warehouseId = wh, lines = new[] { new { ingredientId = ing, quantity = qty, unitPrice = price, qualityResult = "Pass" } } }))).GetProperty("id").GetString();
        await J(await c.PostAsync($"/api/v1/stock-receipts/{id}/post", null));
    }

    private static async Task Issue(HttpClient c, string wh, string ing, string date, decimal qty)
    {
        var id = (await J(await c.PostAsJsonAsync("/api/v1/stock-issues", new { docDate = date, warehouseId = wh, reason = "Other", lines = new[] { new { ingredientId = ing, quantity = qty } } }))).GetProperty("id").GetString();
        await J(await c.PostAsync($"/api/v1/stock-issues/{id}/post", null));
    }

    [Fact]
    public async Task All_sixteen_reports_run_and_export_to_excel()
    {
        var c = await f.AdminAsync();
        var catalog = await J(await c.GetAsync("/api/v1/reports"));
        catalog.EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().BeEquivalentTo(Codes);
        foreach (var code in Codes)
        {
            var filter = new { from = "2026-01-01", to = "2026-12-31", days = 30 };
            var run = await J(await c.PostAsJsonAsync($"/api/v1/reports/{code}/run", filter));
            run.GetProperty("columns").GetArrayLength().Should().BeGreaterThan(3, code);

            var x = await c.PostAsJsonAsync($"/api/v1/reports/{code}/export", filter);
            // more than 3,000 rows (e.g. the audit log of the whole test run) switches to a background export
            if (x.StatusCode == HttpStatusCode.Accepted && run.GetProperty("totalRows").GetInt32() > 3000) continue;
            x.StatusCode.Should().Be(HttpStatusCode.OK, code);
            x.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            using var wb = new XLWorkbook(await x.Content.ReadAsStreamAsync());
            var ws = wb.Worksheets.First();
            // header block: organization, title with code, period, preparer
            var text = string.Join("|", ws.RangeUsed()!.CellsUsed().Take(40).Select(cell => cell.GetString()));
            text.Should().Contain(code);
            text.Should().Contain("01/01/2026");
            text.Should().Contain("Người lập");
        }
    }

    [Fact]
    public async Task Stock_movement_report_matches_ledger()
    {
        var (c, wh, ing) = await StockSetup();
        await Receive(c, wh, ing, "2026-03-05", 10, 20_000);   // before period
        await Receive(c, wh, ing, "2026-04-02", 5, 26_000);    // in period
        await Issue(c, wh, ing, "2026-04-10", 6);               // average (200.000 + 130.000) / 15 = 22.000 → 132.000
        var r = await J(await c.PostAsJsonAsync("/api/v1/reports/RPT-02/run", new { from = "2026-04-01", to = "2026-04-30", warehouseId = wh }));
        r.GetProperty("rows").GetArrayLength().Should().Be(1);
        var row = r.GetProperty("rows")[0];
        row.GetProperty("openQty").GetDecimal().Should().Be(10);
        row.GetProperty("openValue").GetDecimal().Should().Be(200_000);
        row.GetProperty("inQty").GetDecimal().Should().Be(5);
        row.GetProperty("inValue").GetDecimal().Should().Be(130_000);
        row.GetProperty("outQty").GetDecimal().Should().Be(6);
        row.GetProperty("outValue").GetDecimal().Should().Be(132_000);
        row.GetProperty("closeQty").GetDecimal().Should().Be(9);
        row.GetProperty("closeValue").GetDecimal().Should().Be(198_000);
        r.GetProperty("totals").GetProperty("closeValue").GetDecimal().Should().Be(198_000);

        var rpt01 = await J(await c.PostAsJsonAsync("/api/v1/reports/RPT-01/run", new { from = "2026-03-01", to = "2026-04-30", warehouseId = wh }));
        rpt01.GetProperty("rows")[0].GetProperty("inValue").GetDecimal().Should().Be(330_000);
        var card = await J(await c.PostAsJsonAsync("/api/v1/reports/RPT-04/run", new { from = "2026-04-01", to = "2026-04-30", warehouseId = wh, ingredientId = ing }));
        // opening row + 2 movements + closing row
        card.GetProperty("rows").GetArrayLength().Should().Be(4);
        card.GetProperty("rows")[3].GetProperty("balanceQty").GetDecimal().Should().Be(9);
    }

    [Fact]
    public async Task Warehouse_scoped_user_only_sees_own_warehouse_in_reports()
    {
        var (admin, wh, ing) = await StockSetup();
        var (_, other, ing2) = await StockSetup();
        await Receive(admin, wh, ing, "2026-05-02", 1, 1000);
        await Receive(admin, other, ing2, "2026-05-02", 1, 1000);
        var name = $"keeper_rpt_{Guid.NewGuid():N}"[..20];
        var (u, pwd) = await f.CreateUserAsync(name, "STOREKEEPER");
        var uid = (await J(await admin.GetAsync($"/api/v1/users?q={name}"))).GetProperty("items")[0].GetProperty("id").GetString();
        (await admin.PutAsJsonAsync($"/api/v1/users/{uid}/scopes", new { scopes = new[] { new { type = "Warehouse", id = wh } } })).EnsureSuccessStatusCode();
        var keeper = await f.LoginAsync(u, pwd);
        var r = await J(await keeper.PostAsJsonAsync("/api/v1/reports/RPT-02/run", new { from = "2026-05-01", to = "2026-05-31" }));
        var codes = r.GetProperty("rows").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
        codes.Should().Contain(n => n!.StartsWith("Nguyên liệu báo cáo"));
        r.GetProperty("rows").EnumerateArray().Should().OnlyContain(x => x.GetProperty("closeQty").GetDecimal() >= 0);
        // the other warehouse's ingredient is not visible
        var ing2Name = (await J(await admin.GetAsync($"/api/v1/ingredients/{ing2}"))).GetProperty("nameVi").GetString();
        codes.Should().NotContain(ing2Name);
        // audit log report needs audit.view
        (await keeper.PostAsJsonAsync("/api/v1/reports/RPT-16/run", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Background_export_and_favorites()
    {
        var c = await f.AdminAsync();
        var queued = await J(await c.PostAsJsonAsync("/api/v1/reports/RPT-16/export?background=true", new { from = "2026-01-01", to = "2026-12-31" }));
        var jobId = queued.GetProperty("jobId").GetString();
        JsonElement job = default;
        for (var i = 0; i < 60; i++)
        {
            job = (await J(await c.GetAsync("/api/v1/reports/exports"))).EnumerateArray().First(j => j.GetProperty("id").GetString() == jobId);
            if (job.GetProperty("status").GetString() is "Succeeded" or "Failed") break;
            await Task.Delay(250);
        }
        job.GetProperty("status").GetString().Should().Be("Succeeded", job.ToString());
        var file = await c.GetAsync($"/api/v1/reports/exports/{jobId}/download");
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        (await file.Content.ReadAsByteArrayAsync()).Take(2).Should().Equal((byte)'P', (byte)'K');

        (await c.PutAsJsonAsync("/api/v1/reports/RPT-02/favorites", new[] { new { name = "Kho khô tháng này", filter = new { mode = "x", days = 3 } } })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var fav = await J(await c.GetAsync("/api/v1/reports/RPT-02/favorites"));
        fav[0].GetProperty("name").GetString().Should().Be("Kho khô tháng này");
        fav[0].GetProperty("filter").GetProperty("days").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Analysis_cost_sheet_and_dashboard()
    {
        var c = await f.AdminAsync();
        string Id(JsonElement page) => page.GetProperty("items")[0].GetProperty("id").GetString()!;
        var shift = Id(await J(await c.GetAsync("/api/v1/meal-shifts?q=CA1")));
        var rec = Id(await J(await c.GetAsync("/api/v1/recommendations?q=KN-VUA")));
        var customer = Id(await J(await c.GetAsync("/api/v1/customers?q=co khi")));
        var rice = Id(await J(await c.GetAsync("/api/v1/dishes?q=com trang")));
        var chicken = Id(await J(await c.GetAsync("/api/v1/dishes?q=ga kho gung")));
        var date = new DateOnly(2027, 9, 1).AddDays(Interlocked.Increment(ref _seq)).ToString("yyyy-MM-dd");
        var menu = (await J(await c.PostAsJsonAsync("/api/v1/menus", new { menuDate = date, mealShiftId = shift, recommendationId = rec, customerId = customer, servings = 120, items = new[] { new { dishId = rice, servingFactor = 1m }, new { dishId = chicken, servingFactor = 1m } } }))).GetProperty("id").GetString();
        (await c.PostAsJsonAsync($"/api/v1/menus/{menu}/submit", new { comment = "Báo cáo" })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"/api/v1/menus/{menu}/approve", new { comment = "OK" })).EnsureSuccessStatusCode();
        if ((await J(await c.GetAsync($"/api/v1/menus/{menu}"))).GetProperty("status").GetString() == "Submitted")
            (await c.PostAsJsonAsync($"/api/v1/menus/{menu}/approve", new { comment = "OK" })).EnsureSuccessStatusCode();

        var single = await J(await c.PostAsJsonAsync("/api/v1/analysis/nutrition", new { scope = "menu", menuId = menu }));
        single.GetProperty("energyKcal").GetDecimal().Should().BeGreaterThan(300);
        single.GetProperty("nutrients").EnumerateArray().Should().Contain(n => n.GetProperty("code").GetString() == "PROTEIN");
        var range = await J(await c.PostAsJsonAsync("/api/v1/analysis/nutrition", new { scope = "range", from = date, to = date, customerId = customer }));
        range.GetProperty("menuCount").GetInt32().Should().Be(1);
        range.GetProperty("trend").GetArrayLength().Should().Be(1);
        range.GetProperty("energyKcal").GetDecimal().Should().Be(single.GetProperty("energyKcal").GetDecimal());
        var dish = await J(await c.PostAsJsonAsync("/api/v1/analysis/nutrition", new { scope = "dish", dishId = chicken, recommendationId = rec }));
        dish.GetProperty("nutrients").GetArrayLength().Should().BeGreaterThan(3);

        var sheet = await J(await c.GetAsync($"/api/v1/cost-sheet?from={date}&to={date}&customerId={customer}"));
        var row = sheet.GetProperty("rows")[0];
        row.GetProperty("servings").GetInt32().Should().Be(120);
        row.GetProperty("plannedTotal").GetDecimal().Should().BeGreaterThan(0);
        row.GetProperty("actualFood").GetDecimal().Should().Be(0);
        row.GetProperty("revenue").GetDecimal().Should().BeGreaterThan(0);

        var dash = await J(await c.GetAsync("/api/v1/dashboard"));
        dash.TryGetProperty("pendingApproval", out _).Should().BeTrue();
        dash.TryGetProperty("criticalAlerts", out _).Should().BeTrue();
        dash.TryGetProperty("expiringLots", out _).Should().BeTrue();
        dash.TryGetProperty("samplesDue", out _).Should().BeTrue();

        // thresholds: validator rejects inconsistent values, valid update is audited
        var cfg = await J(await c.GetAsync("/api/v1/alerts/config"));
        var bad = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(cfg.GetRawText())!;
        bad["energyWarnLowPct"] = JsonSerializer.SerializeToElement(50);
        bad["energyCriticalLowPct"] = JsonSerializer.SerializeToElement(80);
        (await c.PutAsJsonAsync("/api/v1/alerts/config", bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutAsJsonAsync("/api/v1/alerts/config", cfg)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact] // regression: local-day bounds must be sent to PostgreSQL as UTC
    public async Task Date_filtered_safety_lists_work()
    {
        var c = await f.AdminAsync();
        foreach (var url in new[] { "/api/v1/inspections/step1?from=2026-01-01&to=2026-12-31", "/api/v1/food-samples?from=2026-01-01&to=2026-12-31", "/api/v1/inspections/step2?from=2026-01-01&to=2026-12-31" })
            (await c.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, url);
    }

    [Fact]
    public async Task Catalogs_audit_log_and_reports_export_to_csv_utf8()
    {
        var c = await f.AdminAsync();
        static async Task<string[]> Csv(HttpResponseMessage r)
        {
            if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
            r.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
            r.Content.Headers.ContentDisposition!.FileNameStar.Should().EndWith(".csv");
            var bytes = await r.Content.ReadAsByteArrayAsync();
            bytes.Take(3).Should().Equal((byte)0xEF, (byte)0xBB, (byte)0xBF);
            var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            text.Should().EndWith("\r\n");
            return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        }

        var provinces = await Csv(await c.GetAsync("/api/v1/provinces/export?format=csv&q=ho%20chi%20minh"));
        provinces[0].Should().NotContain("*");
        provinces.Skip(1).Should().Contain(l => l.Contains("Thành phố Hồ Chí Minh"));

        var audit = await Csv(await c.GetAsync("/api/v1/audit-logs/export?format=csv"));
        audit.Length.Should().BeGreaterThan(1);
        audit[1].Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T");

        // reports: data rows only (no organization/title block), synchronous whatever the size
        var report = await Csv(await c.PostAsJsonAsync("/api/v1/reports/RPT-16/export?format=csv", new { from = "2026-01-01", to = "2026-12-31" }));
        string.Join("\n", report).Should().NotContain("Người lập");
        report.Length.Should().BeGreaterThan(1);
    }
}
