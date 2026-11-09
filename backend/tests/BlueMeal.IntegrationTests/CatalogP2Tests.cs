using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.IntegrationTests.Infrastructure;
using ClosedXML.Excel;
using Npgsql;

namespace BlueMeal.IntegrationTests;

/// <summary>Phase P2 acceptance: base catalogs, Excel import performance, accent-insensitive search, data scopes.</summary>
[Collection(nameof(ApiCollection))]
public sealed class CatalogP2Tests(ApiFactory f)
{
    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
    }

    [Fact]
    public async Task Sample_data_is_seeded_and_clearly_labelled()
    {
        var c = await f.AdminAsync();
        (await J(await c.GetAsync("/api/v1/foods?pageSize=1"))).GetProperty("totalItems").GetInt64().Should().BeGreaterThanOrEqualTo(80);
        (await J(await c.GetAsync("/api/v1/ingredients?pageSize=1"))).GetProperty("totalItems").GetInt64().Should().BeGreaterThanOrEqualTo(40);
        var food = (await J(await c.GetAsync("/api/v1/foods?q=gao te"))).GetProperty("items")[0];
        food.GetProperty("sourceVersion").GetString().Should().Be("SAMPLE");
        food.GetProperty("note").GetString().Should().StartWith("[DỮ LIỆU MẪU]");
        food.GetProperty("nutrients").GetProperty("ENERGY").GetDecimal().Should().BeGreaterThan(300);
        var status = await J(await c.GetAsync("/api/v1/system/sample-data"));
        status.GetProperty("loaded").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("thit lon", "Thịt lợn")]
    [InlineData("THỊT LỢN", "Thịt lợn")]
    [InlineData("ca ro phi", "Cá rô phi")]
    [InlineData("dau phu", "Đậu phụ")]
    public async Task Search_is_accent_insensitive(string query, string expected)
    {
        var c = await f.AdminAsync();
        var res = await J(await c.GetAsync("/api/v1/foods?q=" + Uri.EscapeDataString(query)));
        res.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("nameVi").GetString()).Should().Contain(n => n!.StartsWith(expected));
    }

    [Fact]
    public async Task Import_500_foods_from_excel_under_30_seconds()
    {
        var c = await f.AdminAsync();
        var template = await c.GetAsync("/api/v1/foods/import/template");
        template.EnsureSuccessStatusCode();
        using var wb = new XLWorkbook(await template.Content.ReadAsStreamAsync());
        var ws = wb.Worksheet(1);
        var headers = ws.Row(1).CellsUsed().Select(x => x.GetString().TrimEnd('*', ' ')).ToList();
        int Col(string startsWith) => headers.FindIndex(h => h.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase)) + 1;
        var cCode = Col("Mã"); var cName = Col("Tên (tiếng Việt)"); var cGroup = Col("Mã nhóm"); var cRefuse = Col("Tỷ lệ thải bỏ");
        var cEnergy = Col("ENERGY"); var cProt = Col("PROTEIN"); var cLip = Col("LIPID"); var cGlu = Col("GLUCID"); var cNa = Col("NA ");
        new[] { cCode, cName, cGroup, cRefuse, cEnergy, cProt, cLip, cGlu }.Should().OnlyContain(x => x > 0);
        var rnd = new Random(42);
        for (var i = 0; i < 500; i++)
        {
            var r = i + 2;
            ws.Cell(r, cCode).Value = $"IMP-{i:0000}";
            ws.Cell(r, cName).Value = $"Thực phẩm nhập thử số {i}";
            ws.Cell(r, cGroup).Value = i % 2 == 0 ? "RAU" : "THIT";
            ws.Cell(r, cRefuse).Value = rnd.Next(0, 40);
            var p = Math.Round(rnd.NextDouble() * 20, 1); var l = Math.Round(rnd.NextDouble() * 15, 1); var g = Math.Round(rnd.NextDouble() * 30, 1);
            ws.Cell(r, cProt).Value = p; ws.Cell(r, cLip).Value = l; ws.Cell(r, cGlu).Value = g; ws.Cell(r, cEnergy).Value = Math.Round(p * 4 + l * 9 + g * 4);
            if (cNa > 0) ws.Cell(r, cNa).Value = rnd.Next(1, 500);
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var sw = Stopwatch.StartNew();
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(ms.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "foods.xlsx");
        var preview = await J(await c.PostAsync("/api/v1/foods/import/preview", form));
        preview.GetProperty("validRows").GetInt32().Should().Be(500, preview.GetProperty("rows").ToString());
        var commit = await J(await c.PostAsJsonAsync("/api/v1/foods/import/commit", new { token = preview.GetProperty("token").GetString(), skipErrors = false }));
        sw.Stop();
        commit.GetProperty("created").GetInt32().Should().Be(500);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "SPEC P2: importing 500 foods must take < 30 s");

        var imported = (await J(await c.GetAsync("/api/v1/foods?q=thuc pham nhap thu so 123"))).GetProperty("items")[0];
        imported.GetProperty("nutrients").GetProperty("PROTEIN").GetDecimal().Should().BeGreaterThanOrEqualTo(0);

        // re-import the same file → all rows become updates (idempotent upsert by code)
        var form2 = new MultipartFormDataContent { { new ByteArrayContent(ms.ToArray()), "file", "foods.xlsx" } };
        var preview2 = await J(await c.PostAsync("/api/v1/foods/import/preview", form2));
        preview2.GetProperty("updateCount").GetInt32().Should().Be(500);
    }

    [Fact]
    public async Task Food_export_contains_one_column_per_nutrient()
    {
        var c = await f.AdminAsync();
        var res = await c.GetAsync("/api/v1/foods/export?q=gao");
        res.EnsureSuccessStatusCode();
        using var wb = new XLWorkbook(await res.Content.ReadAsStreamAsync());
        var text = wb.Worksheet(1).CellsUsed().Select(x => x.GetString()).ToList();
        text.Should().Contain(t => t.StartsWith("ENERGY")).And.Contain(t => t.StartsWith("PROTEIN")).And.Contain("M-NC01");
    }

    [Fact]
    public async Task Food_nutrient_grid_inline_edit_and_api_lock()
    {
        var c = await f.AdminAsync();
        var grid = await J(await c.GetAsync("/api/v1/foods/grid?q=ca chua&nutrients=ENERGY,VIT_C"));
        var row = grid.GetProperty("items")[0];
        var id = row.GetProperty("id").GetString();
        (await c.PutAsJsonAsync($"/api/v1/foods/{id}/nutrients", new Dictionary<string, decimal?> { ["VIT_C"] = 41.5m })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await J(await c.GetAsync($"/api/v1/foods/{id}"));
        after.GetProperty("nutrients").GetProperty("VIT_C").GetDecimal().Should().Be(41.5m);

        // lock as API data → a nutritionist (no food.unlock) cannot edit
        (await c.PostAsync($"/api/v1/foods/{id}/data-lock?locked=true", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        // a role with food.edit but without food.unlock cannot modify locked (API-sourced) data
        var roleId = (await J(await c.PostAsJsonAsync("/api/v1/roles", new { code = "FOOD_EDITOR", nameVi = "Sửa thực phẩm", isActive = true }))).GetProperty("id").GetString();
        (await c.PutAsJsonAsync($"/api/v1/roles/{roleId}/permissions", new { permissions = new[] { "food.view", "food.edit" } })).EnsureSuccessStatusCode();
        var (u2, p2) = await f.CreateUserAsync("food_editor", "FOOD_EDITOR");
        var e = await f.LoginAsync(u2, p2);
        var locked = await e.PutAsJsonAsync($"/api/v1/foods/{id}/nutrients", new Dictionary<string, decimal?> { ["VIT_C"] = 1 });
        locked.StatusCode.Should().Be((HttpStatusCode)423);
    }

    [Fact]
    public async Task Warehouse_scope_hides_other_warehouses_and_blocks_idor()
    {
        var admin = await f.AdminAsync();
        var whs = (await J(await admin.GetAsync("/api/v1/warehouses"))).GetProperty("items").EnumerateArray().ToList();
        whs.Count.Should().BeGreaterThanOrEqualTo(3);
        var allowed = whs[0].GetProperty("id").GetString()!;
        var other = whs[1].GetProperty("id").GetString()!;
        var (u, pwd) = await f.CreateUserAsync("keeper_scoped", "STOREKEEPER");
        var users = await J(await admin.GetAsync("/api/v1/users?q=keeper_scoped"));
        var uid = users.GetProperty("items")[0].GetProperty("id").GetString();
        (await admin.PutAsJsonAsync($"/api/v1/users/{uid}/scopes", new { scopes = new[] { new { type = "Warehouse", id = allowed } } })).EnsureSuccessStatusCode();

        var keeper = await f.LoginAsync(u, pwd);
        var list = await J(await keeper.GetAsync("/api/v1/warehouses"));
        list.GetProperty("totalItems").GetInt64().Should().Be(1);
        (await keeper.GetAsync($"/api/v1/warehouses/{allowed}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await keeper.GetAsync($"/api/v1/warehouses/{other}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customer_rep_only_sees_own_customer()
    {
        var admin = await f.AdminAsync();
        var custs = (await J(await admin.GetAsync("/api/v1/customers"))).GetProperty("items").EnumerateArray().ToList();
        custs.Count.Should().BeGreaterThanOrEqualTo(3);
        var mine = custs[0].GetProperty("id").GetString()!;
        // give CUSTOMER_REP-like role customer.view to call the catalog, scoped to one customer
        var roleId = (await J(await admin.PostAsJsonAsync("/api/v1/roles", new { code = "CUST_VIEWER", nameVi = "Xem khách hàng", isActive = true }))).GetProperty("id").GetString();
        (await admin.PutAsJsonAsync($"/api/v1/roles/{roleId}/permissions", new { permissions = new[] { "customer.view", "meal_order.view" } })).EnsureSuccessStatusCode();
        var (u, pwd) = await f.CreateUserAsync("cust_viewer", "CUST_VIEWER");
        var uid = (await J(await admin.GetAsync("/api/v1/users?q=cust_viewer"))).GetProperty("items")[0].GetProperty("id").GetString();
        (await admin.PutAsJsonAsync($"/api/v1/users/{uid}/scopes", new { scopes = new[] { new { type = "Customer", id = mine } } })).EnsureSuccessStatusCode();
        var c = await f.LoginAsync(u, pwd);
        (await J(await c.GetAsync("/api/v1/customers"))).GetProperty("totalItems").GetInt64().Should().Be(1);
        (await c.GetAsync($"/api/v1/customers/{custs[1].GetProperty("id").GetString()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/v1/customers/{custs[1].GetProperty("id").GetString()}/orders")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Partner_contracts_certificates_and_ratings()
    {
        var c = await f.AdminAsync();
        var partner = (await J(await c.GetAsync("/api/v1/partners?q=thuy san"))).GetProperty("items")[0];
        partner.GetProperty("nextCertificateExpiry").ValueKind.Should().NotBe(JsonValueKind.Null);
        var pid = partner.GetProperty("id").GetString();
        var certs = await J(await c.GetAsync($"/api/v1/partners/{pid}/certificates"));
        certs[0].GetProperty("daysToExpiry").GetInt32().Should().BeLessThan(0); // sample: already expired

        var ing = (await J(await c.GetAsync("/api/v1/ingredients?q=ca ro phi"))).GetProperty("items")[0].GetProperty("id").GetString();
        (await J(await c.PostAsJsonAsync($"/api/v1/partners/{pid}/contracts", new
        {
            contractNo = "HD-01/2026", signedDate = "2026-01-02", validFrom = "2026-01-05", validTo = "2026-12-31", value = 500_000_000, status = "Active",
            prices = new[] { new { ingredientId = ing, unitPrice = 52000 } },
        }))).GetProperty("id").GetString().Should().NotBeNullOrEmpty();
        var contracts = await J(await c.GetAsync($"/api/v1/partners/{pid}/contracts"));
        contracts[0].GetProperty("prices")[0].GetProperty("unitPrice").GetDecimal().Should().Be(52000);

        (await c.PostAsJsonAsync($"/api/v1/partners/{pid}/ratings", new { ratedDate = "2026-02-01", quality = 5, delivery = 4, price = 3 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await J(await c.GetAsync($"/api/v1/partners/{pid}"))).GetProperty("rating").GetDecimal().Should().Be(4);
    }

    [Fact]
    public async Task Ingredient_reference_price_change_is_recorded_in_history()
    {
        var c = await f.AdminAsync();
        var ing = (await J(await c.GetAsync("/api/v1/ingredients?q=gao te"))).GetProperty("items")[0];
        var id = ing.GetProperty("id").GetString();
        (await c.PostAsJsonAsync($"/api/v1/ingredients/{id}/prices", new { effectiveDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"), unitPrice = 19500 })).EnsureSuccessStatusCode();
        (await J(await c.GetAsync($"/api/v1/ingredients/{id}"))).GetProperty("referencePrice").GetDecimal().Should().Be(19500);
        (await J(await c.GetAsync($"/api/v1/ingredients/{id}/prices")))[0].GetProperty("unitPrice").GetDecimal().Should().Be(19500);
    }
}
