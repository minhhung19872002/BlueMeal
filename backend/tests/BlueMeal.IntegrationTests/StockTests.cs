using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.Api.Services;
using BlueMeal.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BlueMeal.IntegrationTests;

/// <summary>Phase P5 acceptance: stock balance, moving average, FEFO, negative stock guard, stock-take adjustments, period lock, QC, purchase requests, samples, alerts.</summary>
[Collection(nameof(ApiCollection))]
public sealed class StockTests(ApiFactory f)
{
    private static int _seq;

    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var text = await r.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    /// <summary>Own warehouse + ingredient per test so balances do not interfere.</summary>
    private async Task<(HttpClient C, string Wh, string Ing)> Setup(decimal minStock = 0)
    {
        var c = await f.AdminAsync();
        var n = Interlocked.Increment(ref _seq);
        var wh = await J(await c.PostAsJsonAsync("/api/v1/warehouses", new { code = $"KT-{n}-{Guid.NewGuid():N}"[..20], nameVi = $"Kho kiểm thử {n}", type = "Dry", isActive = true }));
        var ing = await J(await c.PostAsJsonAsync("/api/v1/ingredients", new { code = $"NL-KT-{n}-{Guid.NewGuid():N}"[..20], nameVi = $"Nguyên liệu kiểm thử {n}", gramsPerUnit = 1000, minStock, referencePrice = 50000, isActive = true }));
        return (c, wh.GetProperty("id").GetString()!, ing.GetProperty("id").GetString()!);
    }

    private static object Receipt(string wh, string ing, string date, params (decimal Qty, decimal Price, string? Lot, string? Exp, string? Qc)[] lines) => new
    {
        docDate = date, warehouseId = wh, lines = lines.Select(l => new { ingredientId = ing, quantity = l.Qty, unitPrice = l.Price, lotNo = l.Lot, expiryDate = l.Exp, qualityResult = l.Qc ?? "Pass", receivedTemp = 4 }).ToArray(),
    };

    private static async Task<string> PostReceipt(HttpClient c, object body)
    {
        var id = (await J(await c.PostAsJsonAsync("/api/v1/stock-receipts", body))).GetProperty("id").GetString()!;
        await J(await c.PostAsync($"/api/v1/stock-receipts/{id}/post", null));
        return id;
    }

    private static async Task<HttpResponseMessage> PostIssue(HttpClient c, string wh, string ing, string date, decimal qty, string? lotId = null)
    {
        var id = (await J(await c.PostAsJsonAsync("/api/v1/stock-issues", new { docDate = date, warehouseId = wh, reason = "Other", lines = new[] { new { ingredientId = ing, lotId, quantity = qty } } }))).GetProperty("id").GetString()!;
        return await c.PostAsync($"/api/v1/stock-issues/{id}/post", null);
    }

    private static async Task<JsonElement> Balance(HttpClient c, string wh, string? date = null)
        => (await J(await c.GetAsync($"/api/v1/stock/balances?warehouseId={wh}{(date is null ? "" : "&date=" + date)}"))).GetProperty("items")[0];

    [Fact]
    public async Task Opening_stock_import_creates_posted_receipts_per_warehouse()
    {
        var (c, wh, ing) = await Setup();
        var whCode = (await J(await c.GetAsync($"/api/v1/warehouses/{wh}"))).GetProperty("code").GetString();
        var ingCode = (await J(await c.GetAsync($"/api/v1/ingredients/{ing}"))).GetProperty("code").GetString();
        static Dictionary<string, object?> Row(string? whc, string? ingc, object? qty, object? price, string? lot = null, object? mfg = null, object? exp = null) => new()
        {
            ["Mã kho"] = whc, ["Mã nguyên liệu"] = ingc, ["Số lượng"] = qty, ["Đơn giá (₫)"] = price, ["Số lô"] = lot, ["Ngày sản xuất"] = mfg, ["Hạn sử dụng"] = exp,
        };
        var xlsx = await ImportSheet.FromTemplateAsync(c, "stock-receipts",
        [
            Row(whCode, ingCode, 10m, 50_000m, "TDK-A", new DateTime(2026, 6, 1), new DateTime(2026, 12, 31)),
            Row(whCode, ingCode, 5m, 62_000m, "TDK-B", null, "31/01/2027"),
            Row("KHO-KHONG-TON-TAI", ingCode, 1m, 1m),
            Row(whCode + "-X", ingCode, 1m, 1m, "TDK-C", "2026-06-10", "2026-06-01"), // unknown warehouse and expiry before mfg
        ]);
        var preview = await J(await ImportSheet.PreviewAsync(c, "stock-receipts", xlsx, "?docDate=2026-06-30&post=true"));
        preview.GetProperty("validRows").GetInt32().Should().Be(2, preview.GetProperty("rows").ToString());
        preview.GetProperty("errorRows").GetInt32().Should().Be(2);
        preview.GetProperty("rows")[3].GetProperty("errors").GetArrayLength().Should().Be(2);
        var commit = await J(await c.PostAsJsonAsync("/api/v1/stock-receipts/import/commit", new { token = preview.GetProperty("token").GetString(), skipErrors = true }));
        commit.GetProperty("created").GetInt32().Should().Be(1); // one receipt for the one valid warehouse

        var receipt = (await J(await c.GetAsync($"/api/v1/stock-receipts?warehouseId={wh}"))).GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        receipt.GetProperty("status").GetString().Should().Be("Posted");
        receipt.GetProperty("docDate").GetString().Should().Be("2026-06-30");
        receipt.GetProperty("totalAmount").GetDecimal().Should().Be(810_000);
        var b = await Balance(c, wh);
        b.GetProperty("quantity").GetDecimal().Should().Be(15);
        b.GetProperty("value").GetDecimal().Should().Be(810_000);
        var lots = (await J(await c.GetAsync($"/api/v1/stock/lots?warehouseId={wh}&ingredientId={ing}"))).EnumerateArray().ToList();
        lots.Select(l => l.GetProperty("lotNo").GetString()).Should().BeEquivalentTo(["TDK-A", "TDK-B"]);

        // the opening stock is the opening balance of the following period (stock card and RPT-02)
        var card = await J(await c.GetAsync($"/api/v1/stock/card?ingredientId={ing}&warehouseId={wh}&from=2026-07-01&to=2026-07-31"));
        card.GetProperty("opening").GetProperty("quantity").GetDecimal().Should().Be(15);
        var rpt = await J(await c.PostAsJsonAsync("/api/v1/reports/RPT-02/run", new { from = "2026-07-01", to = "2026-07-31", warehouseId = wh }));
        rpt.GetProperty("rows")[0].GetProperty("openValue").GetDecimal().Should().Be(810_000);
    }

    [Fact]
    public async Task Moving_average_issue_cost_balance_and_stock_card()
    {
        var (c, wh, ing) = await Setup();
        await PostReceipt(c, Receipt(wh, ing, "2026-08-03", (10, 50_000, "L1", "2026-12-31", null)));
        await PostReceipt(c, Receipt(wh, ing, "2026-08-05", (10, 60_000, "L2", "2027-01-31", null)));
        var b = await Balance(c, wh);
        b.GetProperty("quantity").GetDecimal().Should().Be(20);
        b.GetProperty("value").GetDecimal().Should().Be(1_100_000);
        b.GetProperty("averageCost").GetDecimal().Should().Be(55_000);

        (await PostIssue(c, wh, ing, "2026-08-06", 5)).StatusCode.Should().Be(HttpStatusCode.OK);
        b = await Balance(c, wh);
        b.GetProperty("quantity").GetDecimal().Should().Be(15);
        b.GetProperty("value").GetDecimal().Should().Be(825_000);

        // balance at a date is computed from the ledger
        (await Balance(c, wh, "2026-08-04")).GetProperty("quantity").GetDecimal().Should().Be(10);

        var card = await J(await c.GetAsync($"/api/v1/stock/card?ingredientId={ing}&warehouseId={wh}&from=2026-08-04&to=2026-08-31"));
        card.GetProperty("opening").GetProperty("quantity").GetDecimal().Should().Be(10);
        var rows = card.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(2);
        rows[1].GetProperty("outQty").GetDecimal().Should().Be(5);
        rows[1].GetProperty("amount").GetDecimal().Should().Be(-275_000);
        card.GetProperty("closing").GetProperty("quantity").GetDecimal().Should().Be(15);

        // posted documents are locked
        var receipts = await J(await c.GetAsync($"/api/v1/stock-receipts?warehouseId={wh}"));
        var rid = receipts.GetProperty("items")[0].GetProperty("id").GetString();
        (await c.DeleteAsync($"/api/v1/stock-receipts/{rid}")).StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Fefo_suggestion_and_allocation_take_earliest_expiry_first()
    {
        var (c, wh, ing) = await Setup();
        // the later receipt expires first
        await PostReceipt(c, Receipt(wh, ing, "2026-08-03", (5, 40_000, "LATE", "2027-03-01", null), (5, 40_000, "SOON", "2026-12-01", null)));
        var lots = await J(await c.GetAsync($"/api/v1/stock-issues/suggest-lots?warehouseId={wh}&ingredientId={ing}&quantity=7"));
        lots[0].GetProperty("lotNo").GetString().Should().Be("SOON");
        lots[0].GetProperty("quantity").GetDecimal().Should().Be(5);
        lots[1].GetProperty("lotNo").GetString().Should().Be("LATE");
        lots[1].GetProperty("quantity").GetDecimal().Should().Be(2);

        (await PostIssue(c, wh, ing, "2026-08-04", 6)).StatusCode.Should().Be(HttpStatusCode.OK);
        var remaining = await J(await c.GetAsync($"/api/v1/stock/lots?warehouseId={wh}&ingredientId={ing}"));
        remaining.GetArrayLength().Should().Be(1);
        remaining[0].GetProperty("lotNo").GetString().Should().Be("LATE");
        remaining[0].GetProperty("quantity").GetDecimal().Should().Be(4);
    }

    [Fact]
    public async Task Negative_stock_is_rejected()
    {
        var (c, wh, ing) = await Setup();
        await PostReceipt(c, Receipt(wh, ing, "2026-08-03", (3, 10_000, null, null, null)));
        var r = await PostIssue(c, wh, ing, "2026-08-04", 3.5m);
        r.StatusCode.Should().Be((HttpStatusCode)422);
        (await r.Content.ReadAsStringAsync()).Should().Contain("Không đủ tồn kho");
        (await Balance(c, wh)).GetProperty("quantity").GetDecimal().Should().Be(3);
    }

    [Fact]
    public async Task Stock_take_approval_posts_adjustments()
    {
        var (c, wh, ing) = await Setup();
        await PostReceipt(c, Receipt(wh, ing, "2026-08-03", (10, 20_000, "A", "2026-12-01", null), (6, 20_000, "B", "2027-01-01", null)));
        var take = await J(await c.PostAsJsonAsync("/api/v1/stock-takes", new { warehouseId = wh, takeDate = "2026-08-10", members = "Tổ kiểm kê: An, Bình" }));
        var tid = take.GetProperty("id").GetString();
        var detail = await J(await c.GetAsync($"/api/v1/stock-takes/{tid}"));
        var lines = detail.GetProperty("lines").EnumerateArray().ToList();
        lines.Should().HaveCount(2);
        var la = lines.First(l => l.GetProperty("lotNo").GetString() == "A");
        var lb = lines.First(l => l.GetProperty("lotNo").GetString() == "B");
        la.GetProperty("bookQty").GetDecimal().Should().Be(10);

        // approval requires every line counted
        (await c.PutAsJsonAsync($"/api/v1/stock-takes/{tid}", new { members = "An, Bình", lines = new[] { new { id = la.GetProperty("id").GetString(), actualQty = (decimal?)8.5m, cause = "Hao hụt" } } })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.PostAsync($"/api/v1/stock-takes/{tid}/approve", null)).StatusCode.Should().Be((HttpStatusCode)422);
        (await c.PutAsJsonAsync($"/api/v1/stock-takes/{tid}", new { members = "An, Bình", lines = new[] { new { id = lb.GetProperty("id").GetString(), actualQty = (decimal?)7m, cause = "Nhập thiếu sổ" } } })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var approved = await J(await c.PostAsync($"/api/v1/stock-takes/{tid}/approve", null));
        approved.GetProperty("adjustReceiptId").GetString().Should().NotBeNull();
        approved.GetProperty("adjustIssueId").GetString().Should().NotBeNull();
        // 16 − 1,5 + 1 = 15,5
        (await Balance(c, wh)).GetProperty("quantity").GetDecimal().Should().Be(15.5m);
        var lots = (await J(await c.GetAsync($"/api/v1/stock/lots?warehouseId={wh}"))).EnumerateArray().ToList();
        lots.First(l => l.GetProperty("lotNo").GetString() == "A").GetProperty("quantity").GetDecimal().Should().Be(8.5m);
        (await c.PutAsJsonAsync($"/api/v1/stock-takes/{tid}", new { lines = Array.Empty<object>() })).StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Period_lock_blocks_posting_into_locked_month()
    {
        var (c, wh, ing) = await Setup();
        var draft = (await J(await c.PostAsJsonAsync("/api/v1/stock-receipts", Receipt(wh, ing, "2026-05-20", (1, 1000, null, null, null))))).GetProperty("id").GetString();
        // drafts in the period prevent locking
        (await c.PostAsJsonAsync("/api/v1/stock/period-locks", new { warehouseId = wh, period = "2026-05-01" })).StatusCode.Should().Be((HttpStatusCode)422);
        (await c.DeleteAsync($"/api/v1/stock-receipts/{draft}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var lockId = (await J(await c.PostAsJsonAsync("/api/v1/stock/period-locks", new { warehouseId = wh, period = "2026-05-01" }))).GetProperty("id").GetString();

        var r = await c.PostAsJsonAsync("/api/v1/stock-receipts", Receipt(wh, ing, "2026-05-31", (1, 1000, null, null, null)));
        r.StatusCode.Should().Be((HttpStatusCode)422);
        (await r.Content.ReadAsStringAsync()).Should().Contain("05/2026");
        // earlier months are closed too (A-39), later ones stay open
        (await c.PostAsJsonAsync("/api/v1/stock-receipts", Receipt(wh, ing, "2026-04-10", (1, 1000, null, null, null)))).StatusCode.Should().Be((HttpStatusCode)422);
        (await c.PostAsJsonAsync("/api/v1/stock-receipts", Receipt(wh, ing, "2026-06-01", (1, 1000, null, null, null)))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await c.DeleteAsync($"/api/v1/stock/period-locks/{lockId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.PostAsJsonAsync("/api/v1/stock-receipts", Receipt(wh, ing, "2026-05-31", (1, 1000, null, null, null)))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Quality_check_fail_blocks_posting_and_posting_records_step1()
    {
        var (c, wh, ing) = await Setup();
        var id = (await J(await c.PostAsJsonAsync("/api/v1/stock-receipts", Receipt(wh, ing, "2026-08-03", (2, 1000, "X", null, "Fail"))))).GetProperty("id").GetString();
        var r = await c.PostAsync($"/api/v1/stock-receipts/{id}/post", null);
        r.StatusCode.Should().Be((HttpStatusCode)422);

        var ok = await PostReceipt(c, Receipt(wh, ing, "2026-08-03", (2, 1000, "Y", null, "ConditionalPass")));
        var step1 = await J(await c.GetAsync($"/api/v1/inspections/step1?receiptId={ok}"));
        step1.GetProperty("items").GetArrayLength().Should().Be(1);
        step1.GetProperty("items")[0].GetProperty("result").GetString().Should().Be("ConditionalPass");
    }

    [Fact]
    public async Task Idempotency_key_replays_the_first_response()
    {
        var (c, wh, ing) = await Setup();
        var key = Guid.NewGuid().ToString();
        HttpRequestMessage Req() => new(HttpMethod.Post, "/api/v1/stock-receipts") { Content = JsonContent.Create(Receipt(wh, ing, "2026-08-03", (1, 1000, null, null, null))), Headers = { { "Idempotency-Key", key } } };
        var first = await J(await c.SendAsync(Req()));
        var second = await c.SendAsync(Req());
        second.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
        (await J(second)).GetProperty("id").GetString().Should().Be(first.GetProperty("id").GetString());
        (await J(await c.GetAsync($"/api/v1/stock-receipts?warehouseId={wh}"))).GetProperty("totalItems").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Approved_menu_generates_planned_issue_and_purchase_request()
    {
        var c = await f.AdminAsync();
        string Id(JsonElement page) => page.GetProperty("items")[0].GetProperty("id").GetString()!;
        var shift = Id(await J(await c.GetAsync("/api/v1/meal-shifts?q=CA2")));
        var rec = Id(await J(await c.GetAsync("/api/v1/recommendations?q=KN-VUA")));
        var dish = Id(await J(await c.GetAsync("/api/v1/dishes?q=com trang")));
        var dish2 = Id(await J(await c.GetAsync("/api/v1/dishes?q=ga kho gung")));
        var date = new DateOnly(2027, 6, 1).AddDays(Interlocked.Increment(ref _seq)).ToString("yyyy-MM-dd");
        var menu = (await J(await c.PostAsJsonAsync("/api/v1/menus", new { menuDate = date, mealShiftId = shift, recommendationId = rec, servings = 200, items = new[] { new { dishId = dish, servingFactor = 1m }, new { dishId = dish2, servingFactor = 1m } } }))).GetProperty("id").GetString();
        (await c.PostAsJsonAsync($"/api/v1/menus/{menu}/submit", new { comment = "Kiểm thử kho" })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"/api/v1/menus/{menu}/approve", new { comment = "OK" })).EnsureSuccessStatusCode();
        var m = await J(await c.GetAsync($"/api/v1/menus/{menu}"));
        if (m.GetProperty("status").GetString() == "Submitted") (await c.PostAsJsonAsync($"/api/v1/menus/{menu}/approve", new { comment = "OK" })).EnsureSuccessStatusCode();

        var issues = await J(await c.PostAsync($"/api/v1/stock-issues/from-menu/{menu}", null));
        issues.GetArrayLength().Should().BeGreaterThan(0);
        var issue = await J(await c.GetAsync($"/api/v1/stock-issues/{issues[0].GetProperty("id").GetString()}"));
        issue.GetProperty("isPlanned").GetBoolean().Should().BeTrue();
        issue.GetProperty("reason").GetString().Should().Be("ForMenu");
        issue.GetProperty("lines").GetArrayLength().Should().BeGreaterThan(0);
        // second generation for the same menu is refused
        (await c.PostAsync($"/api/v1/stock-issues/from-menu/{menu}", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var pr = await J(await c.PostAsJsonAsync("/api/v1/purchase-requests/generate", new { from = date, to = date, menuIds = new[] { menu } }));
        pr.GetProperty("lineCount").GetInt32().Should().BeGreaterThan(0);
        var prd = await J(await c.GetAsync($"/api/v1/purchase-requests/{pr.GetProperty("id").GetString()}"));
        // 200 servings of rice: recipe grams × 200 / 1000 per kg — required > 0 and suggestion = max(0, required − available + min)
        foreach (var l in prd.GetProperty("lines").EnumerateArray())
        {
            var expected = Math.Max(0, l.GetProperty("requiredQty").GetDecimal() - Math.Max(0, l.GetProperty("availableQty").GetDecimal()) + l.GetProperty("minStock").GetDecimal());
            l.GetProperty("suggestedQty").GetDecimal().Should().Be(Math.Round(expected, 4));
        }
        var prId = pr.GetProperty("id").GetString();
        (await c.PostAsync($"/api/v1/purchase-requests/{prId}/receipts", null)).StatusCode.Should().Be((HttpStatusCode)422);
        (await c.PostAsync($"/api/v1/purchase-requests/{prId}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var receipts = await J(await c.PostAsync($"/api/v1/purchase-requests/{prId}/receipts", null));
        receipts.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Food_sample_rules_and_daily_alerts()
    {
        var (c, wh, ing) = await Setup(minStock: 50);
        var dish = (await J(await c.GetAsync("/api/v1/dishes?q=canh"))).GetProperty("items")[0].GetProperty("id").GetString();
        var sampledAt = DateTimeOffset.UtcNow.AddHours(-30);
        (await c.PostAsJsonAsync("/api/v1/food-samples", new { dishId = dish, sampledAt, weightG = 50 })).StatusCode.Should().Be((HttpStatusCode)422);
        var fresh = await J(await c.PostAsJsonAsync("/api/v1/food-samples", new { dishId = dish, sampledAt = DateTimeOffset.UtcNow, weightG = 150 }));
        // local offsets (+07:00, as the API itself returns) are stored as the same instant, not rejected with 500
        var local = DateTimeOffset.UtcNow.AddMinutes(-5).ToOffset(TimeSpan.FromHours(7));
        var withOffset = await J(await c.PostAsJsonAsync("/api/v1/food-samples", new { dishId = dish, sampledAt = local.ToString("yyyy-MM-dd'T'HH:mm:sszzz"), weightG = 150 }));
        await using (var conn = new Npgsql.NpgsqlConnection(f.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand($"SELECT sampled_at FROM app.food_sample WHERE id = '{withOffset.GetProperty("id").GetString()}'", conn);
            ((DateTime)(await cmd.ExecuteScalarAsync())!).Should().BeCloseTo(local.UtcDateTime, TimeSpan.FromSeconds(1));
        }
        (await c.PostAsJsonAsync($"/api/v1/food-samples/{fresh.GetProperty("id").GetString()}/dispose", new { })).StatusCode.Should().Be((HttpStatusCode)422);
        var old = await J(await c.PostAsJsonAsync("/api/v1/food-samples", new { dishId = dish, sampledAt, weightG = 150, storageTemp = 3 }));
        var oldId = old.GetProperty("id").GetString();

        // lot expiring tomorrow + stock below the minimum of 50
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(1).ToString("yyyy-MM-dd");
        await PostReceipt(c, Receipt(wh, ing, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).ToString("yyyy-MM-dd"), (5, 10_000, "EXP", tomorrow, null)));

        var run = await J(await c.PostAsync("/api/v1/alerts/run-daily", null));
        run.GetProperty("created").GetInt32().Should().BeGreaterThan(0);
        var alerts = (await J(await c.GetAsync("/api/v1/alerts?pageSize=200"))).GetProperty("items").EnumerateArray().ToList();
        alerts.Should().Contain(a => a.GetProperty("code").GetString() == AlertCodes.SampleDispose && a.GetProperty("entityId").GetString() == oldId);
        alerts.Should().Contain(a => a.GetProperty("code").GetString() == AlertCodes.StockExpiry && a.GetProperty("messageVi").GetString()!.Contains("EXP"));
        alerts.Should().Contain(a => a.GetProperty("code").GetString() == AlertCodes.StockMin && a.GetProperty("entityId").GetString() == ing);

        // running again does not duplicate; disposing resolves the sample alert
        var again = await J(await c.PostAsync("/api/v1/alerts/run-daily", null));
        var count = (await J(await c.GetAsync("/api/v1/alerts?pageSize=200"))).GetProperty("items").EnumerateArray().Count(a => a.GetProperty("entityId").GetString() == oldId);
        count.Should().Be(1);
        (await c.PostAsJsonAsync($"/api/v1/food-samples/{oldId}/dispose", new { note = "Hủy đúng quy định" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await J(await c.GetAsync("/api/v1/alerts?pageSize=200"))).GetProperty("items").EnumerateArray().Should().NotContain(a => a.GetProperty("entityId").GetString() == oldId);

        // the recurring job is registered and resolvable by name
        using var scope = f.Services.CreateScope();
        scope.ServiceProvider.GetServices<BlueMeal.Application.Abstractions.IAppJob>().Should().Contain(j => j.Name == "daily-alerts");
    }
}
