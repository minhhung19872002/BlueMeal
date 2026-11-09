using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.IntegrationTests.Infrastructure;

namespace BlueMeal.IntegrationTests;

/// <summary>Phase P4 acceptance: generate → edit → submit → approve → publish; workflow rules; plans; orders.</summary>
[Collection(nameof(ApiCollection))]
public sealed class MenuTests(ApiFactory f)
{
    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var text = await r.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private sealed record Refs(string ShiftId, string SubjectId, string CustomerId, string StructureId, string RecId);
    private static int _dayOffset;

    private async Task<(HttpClient C, Refs R)> Setup()
    {
        var c = await f.AdminAsync();
        string Id(JsonElement page) => page.GetProperty("items")[0].GetProperty("id").GetString()!;
        var shift = Id(await J(await c.GetAsync("/api/v1/meal-shifts?q=CA1")));
        var subject = Id(await J(await c.GetAsync("/api/v1/subject-groups?q=lao dong vua")));
        var customer = Id(await J(await c.GetAsync("/api/v1/customers?q=co khi")));
        var structure = Id(await J(await c.GetAsync("/api/v1/meal-structures?q=CHUAN")));
        var rec = Id(await J(await c.GetAsync("/api/v1/recommendations?q=KN-VUA")));
        return (c, new Refs(shift, subject, customer, structure, rec));
    }

    private static DateOnly NextDays(int days)
    {
        var start = new DateOnly(2027, 1, 4).AddDays(Interlocked.Add(ref _dayOffset, days + 7));
        return start;
    }

    private static int WorkDays(DateOnly from, DateOnly to) => Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Count(i => from.AddDays(i).DayOfWeek != DayOfWeek.Sunday);

    private static object GenBody(Refs r, DateOnly from, DateOnly to, bool save, int seed = 11) => new
    {
        request = new
        {
            fromDate = from.ToString("yyyy-MM-dd"), toDate = to.ToString("yyyy-MM-dd"), mealShiftIds = new[] { r.ShiftId }, subjectGroupId = r.SubjectId, customerId = r.CustomerId,
            recommendationId = r.RecId, structureTemplateId = r.StructureId, budgetPerServing = 45000, noRepeatDays = 3, preferExpiringStock = false, newDishRatio = 0.2, seed, servings = 350,
            skipWeekends = true,
        },
        save,
    };

    [Fact]
    public async Task Generate_edit_submit_approve_publish_end_to_end()
    {
        var (c, r) = await Setup();
        var from = NextDays(5);
        var preview = await J(await c.PostAsJsonAsync("/api/v1/menus/generate", GenBody(r, from, from.AddDays(4), save: false)));
        preview.GetProperty("days").GetArrayLength().Should().Be(WorkDays(from, from.AddDays(4)));
        preview.GetProperty("days")[0].GetProperty("alternatives").GetArrayLength().Should().Be(3);
        preview.GetProperty("days")[0].GetProperty("explanationVi").GetString().Should().Contain("kcal");

        var saved = await J(await c.PostAsJsonAsync("/api/v1/menus/generate", GenBody(r, from, from.AddDays(4), save: true)));
        var ids = saved.GetProperty("saved").EnumerateArray().Select(x => x.GetString()!).ToList();
        ids.Should().HaveCount(WorkDays(from, from.AddDays(4)));
        var menuId = ids[0];
        var menu = await J(await c.GetAsync($"/api/v1/menus/{menuId}"));
        menu.GetProperty("status").GetString().Should().Be("Draft");
        menu.GetProperty("creationMode").GetString().Should().Be("Auto");
        menu.GetProperty("snapshot").GetProperty("nutrition").GetProperty("energyKcal").GetDecimal().Should().BeGreaterThan(500);

        // edit: change serving factor of the staple, servings 400
        var items = menu.GetProperty("items").EnumerateArray().Select(i => new { dishId = i.GetProperty("dishId").GetString(), servingFactor = i.GetProperty("dishType").GetString() == "Staple" ? 1.2m : 1m, slot = i.GetProperty("dishType").GetString() }).ToList();
        (await c.PutAsJsonAsync($"/api/v1/menus/{menuId}", new { menuDate = from.ToString("yyyy-MM-dd"), mealShiftId = r.ShiftId, subjectGroupId = r.SubjectId, customerId = r.CustomerId, recommendationId = r.RecId, structureTemplateId = r.StructureId, servings = 400, budgetPerServing = 45000, items }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var edited = await J(await c.GetAsync($"/api/v1/menus/{menuId}"));
        var critical = edited.GetProperty("criticalWarnings").GetInt32();
        (await c.PostAsJsonAsync($"/api/v1/menus/{menuId}/submit", new { comment = critical > 0 ? "Chấp nhận do thiếu nguyên liệu" : null })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.PostAsJsonAsync($"/api/v1/menus/{menuId}/approve", new { comment = "OK" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var approved = await J(await c.GetAsync($"/api/v1/menus/{menuId}"));
        approved.GetProperty("status").GetString().Should().Be("Approved");
        approved.GetProperty("items").EnumerateArray().Should().OnlyContain(i => i.GetProperty("recipeId").ValueKind == JsonValueKind.String, "recipe versions are locked at approval");

        // approved menu is locked
        (await c.PutAsJsonAsync($"/api/v1/menus/{menuId}", new { menuDate = from.ToString("yyyy-MM-dd"), mealShiftId = r.ShiftId, servings = 1, items }))
            .StatusCode.Should().Be((HttpStatusCode)422);

        (await c.PostAsync($"/api/v1/menus/{menuId}/publish", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var pub = await J(await c.GetAsync($"/api/v1/menus/published?from={from:yyyy-MM-dd}&to={from.AddDays(6):yyyy-MM-dd}&customerId={r.CustomerId}"));
        pub.GetProperty("menus").GetArrayLength().Should().Be(1);
        pub.GetRawText().Should().NotContain("cost");

        var history = (await J(await c.GetAsync($"/api/v1/menus/{menuId}"))).GetProperty("approvals").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        history.Should().ContainInOrder("Submit", "Approve", "Publish");
    }

    [Fact]
    public async Task Critical_warning_blocks_submit_without_reason_and_reject_requires_comment()
    {
        var (c, r) = await Setup();
        var date = NextDays(1);
        // only a dessert → missing required slots → Critical (W-STRUCTURE)
        var dessert = (await J(await c.GetAsync("/api/v1/dishes?q=chuoi tieu"))).GetProperty("items")[0].GetProperty("id").GetString();
        var created = await J(await c.PostAsJsonAsync("/api/v1/menus", new { menuDate = date.ToString("yyyy-MM-dd"), mealShiftId = r.ShiftId, subjectGroupId = r.SubjectId, structureTemplateId = r.StructureId, servings = 10, items = new[] { new { dishId = dessert, servingFactor = 1 } } }));
        var id = created.GetProperty("id").GetString();
        var menu = await J(await c.GetAsync($"/api/v1/menus/{id}"));
        menu.GetProperty("criticalWarnings").GetInt32().Should().BeGreaterThan(0);
        menu.GetProperty("snapshot").GetProperty("warnings").EnumerateArray().Should().Contain(w => w.GetProperty("code").GetString() == "W-STRUCTURE");

        var blocked = await c.PostAsJsonAsync($"/api/v1/menus/{id}/submit", new { });
        blocked.StatusCode.Should().Be((HttpStatusCode)422);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("menu.criticalBlocked");

        (await c.PostAsJsonAsync($"/api/v1/menus/{id}/submit", new { comment = "Thử nghiệm" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.PostAsJsonAsync($"/api/v1/menus/{id}/reject", new { comment = "" })).StatusCode.Should().Be((HttpStatusCode)422);
        (await c.PostAsJsonAsync($"/api/v1/menus/{id}/return", new { comment = "Bổ sung món mặn" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await J(await c.GetAsync($"/api/v1/menus/{id}"))).GetProperty("status").GetString().Should().Be("Returned");
    }

    [Fact]
    public async Task Approver_without_override_permission_cannot_approve_critical_menu()
    {
        var (c, r) = await Setup();
        var date = NextDays(1);
        var dessert = (await J(await c.GetAsync("/api/v1/dishes?q=sua chua"))).GetProperty("items")[0].GetProperty("id").GetString();
        var id = (await J(await c.PostAsJsonAsync("/api/v1/menus", new { menuDate = date.ToString("yyyy-MM-dd"), mealShiftId = r.ShiftId, subjectGroupId = r.SubjectId, structureTemplateId = r.StructureId, servings = 10, items = new[] { new { dishId = dessert } } }))).GetProperty("id").GetString();
        (await c.PostAsJsonAsync($"/api/v1/menus/{id}/submit", new { comment = "lý do" })).EnsureSuccessStatusCode();
        var roleId = (await J(await c.PostAsJsonAsync("/api/v1/roles", new { code = "APPROVER_NO_OVERRIDE", nameVi = "Duyệt không chấp nhận critical", isActive = true }))).GetProperty("id").GetString();
        (await c.PutAsJsonAsync($"/api/v1/roles/{roleId}/permissions", new { permissions = new[] { "menu.view", "menu.approve" } })).EnsureSuccessStatusCode();
        var (u, p) = await f.CreateUserAsync("approver_noov", "APPROVER_NO_OVERRIDE");
        var approver = await f.LoginAsync(u, p);
        (await approver.PostAsJsonAsync($"/api/v1/menus/{id}/approve", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // admin has menu.override_critical
        (await c.PostAsJsonAsync($"/api/v1/menus/{id}/approve", new { })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var actions = (await J(await c.GetAsync($"/api/v1/menus/{id}"))).GetProperty("approvals").EnumerateArray().Select(a => a.GetProperty("action").GetString());
        actions.Should().Contain("OverrideCritical");
    }

    [Fact]
    public async Task Two_level_approval_and_recall()
    {
        var (c, r) = await Setup();
        (await c.PutAsJsonAsync("/api/v1/settings/menu.approval", new { levels = 2, notifyByEmail = false })).EnsureSuccessStatusCode();
        try
        {
            var from = NextDays(1);
            var id = (await J(await c.PostAsJsonAsync("/api/v1/menus/generate", GenBody(r, from, from, true, seed: 99)))).GetProperty("saved")[0].GetString();
            var m = await J(await c.GetAsync($"/api/v1/menus/{id}"));
            (await c.PostAsJsonAsync($"/api/v1/menus/{id}/submit", new { comment = m.GetProperty("criticalWarnings").GetInt32() > 0 ? "x" : null })).EnsureSuccessStatusCode();
            (await c.PostAsJsonAsync($"/api/v1/menus/{id}/approve", new { })).EnsureSuccessStatusCode();
            var afterL1 = await J(await c.GetAsync($"/api/v1/menus/{id}"));
            afterL1.GetProperty("status").GetString().Should().Be("Submitted");
            afterL1.GetProperty("approvalLevel").GetInt32().Should().Be(1);
            (await c.PostAsJsonAsync($"/api/v1/menus/{id}/approve", new { })).EnsureSuccessStatusCode();
            (await J(await c.GetAsync($"/api/v1/menus/{id}"))).GetProperty("status").GetString().Should().Be("Approved");

            (await c.PostAsJsonAsync($"/api/v1/menus/{id}/recall", new { comment = "" })).StatusCode.Should().Be((HttpStatusCode)422);
            (await c.PostAsJsonAsync($"/api/v1/menus/{id}/recall", new { comment = "Đổi món theo yêu cầu khách" })).EnsureSuccessStatusCode();
            var recalled = await J(await c.GetAsync($"/api/v1/menus/{id}"));
            recalled.GetProperty("status").GetString().Should().Be("Draft");
            recalled.GetProperty("items").EnumerateArray().Should().OnlyContain(i => i.GetProperty("recipeId").ValueKind == JsonValueKind.Null);
        }
        finally
        {
            (await c.PutAsJsonAsync("/api/v1/settings/menu.approval", new { levels = 1, notifyByEmail = true })).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Plan_calendar_copy_week_and_swap_suggestions()
    {
        var (c, r) = await Setup();
        var from = NextDays(7);
        var planId = (await J(await c.PostAsJsonAsync("/api/v1/menu-plans", new { name = "Kế hoạch tuần thử", period = "Week", fromDate = from.ToString("yyyy-MM-dd"), toDate = from.AddDays(13).ToString("yyyy-MM-dd"),
            customerId = r.CustomerId, subjectGroupId = r.SubjectId, recommendationId = r.RecId, structureTemplateId = r.StructureId, mealShiftIds = new[] { r.ShiftId }, budgetPerServing = 45000, noRepeatDays = 3, status = "InProgress" }))).GetProperty("id").GetString();
        var body = GenBody(r, from, from.AddDays(5), true);
        var gen = JsonSerializer.SerializeToElement(body);
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(gen.GetProperty("request").GetRawText())!;
        req["planId"] = planId!;
        var saved = await J(await c.PostAsJsonAsync("/api/v1/menus/generate", new { request = req, save = true }));
        var expected = WorkDays(from, from.AddDays(5)); // Sundays skipped
        saved.GetProperty("saved").GetArrayLength().Should().Be(expected);

        var cal = await J(await c.GetAsync($"/api/v1/menu-plans/{planId}/calendar"));
        cal.GetProperty("cells").GetArrayLength().Should().Be(expected);
        var copied = await J(await c.PostAsJsonAsync($"/api/v1/menu-plans/{planId}/copy-week", new { sourceWeekStart = from.ToString("yyyy-MM-dd"), targetWeekStart = from.AddDays(7).ToString("yyyy-MM-dd") }));
        // every menu of the source week (first 7 days) is duplicated
        copied.GetProperty("created").GetInt32().Should().Be(WorkDays(from, from.AddDays(Math.Min(5, 6))));

        var menuId = cal.GetProperty("cells")[0].GetProperty("id").GetString();
        var itemId = cal.GetProperty("cells")[0].GetProperty("items")[0].GetProperty("id").GetString();
        var sug = await J(await c.GetAsync($"/api/v1/menus/{menuId}/suggestions?itemId={itemId}"));
        sug.ValueKind.Should().Be(JsonValueKind.Array);
        sug.GetArrayLength().Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task Meal_orders_summary_and_customer_scope()
    {
        var (c, r) = await Setup();
        var date = NextDays(1);
        (await c.PostAsJsonAsync("/api/v1/meal-orders", new { customerId = r.CustomerId, orderDate = date.ToString("yyyy-MM-dd"), mealShiftId = r.ShiftId, registeredServings = 420, status = "Confirmed" })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/v1/meal-orders", new { customerId = r.CustomerId, orderDate = date.ToString("yyyy-MM-dd"), mealShiftId = r.ShiftId, registeredServings = 1, status = "Draft" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        var sum = await J(await c.GetAsync($"/api/v1/meal-orders/summary?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"));
        sum[0].GetProperty("registered").GetInt32().Should().Be(420);

        // a customer representative scoped to another customer cannot see this order
        var other = (await J(await c.GetAsync("/api/v1/customers?q=may xuat khau"))).GetProperty("items")[0].GetProperty("id").GetString();
        var (u, p) = await f.CreateUserAsync("rep_other", "CUSTOMER_REP");
        var uid = (await J(await c.GetAsync("/api/v1/users?q=rep_other"))).GetProperty("items")[0].GetProperty("id").GetString();
        (await c.PutAsJsonAsync($"/api/v1/users/{uid}/scopes", new { scopes = new[] { new { type = "Customer", id = other } } })).EnsureSuccessStatusCode();
        var rep = await f.LoginAsync(u, p);
        (await J(await rep.GetAsync($"/api/v1/meal-orders?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"))).GetProperty("totalItems").GetInt64().Should().Be(0);
        (await rep.PostAsJsonAsync("/api/v1/meal-orders", new { customerId = r.CustomerId, orderDate = date.AddDays(1).ToString("yyyy-MM-dd"), mealShiftId = r.ShiftId, registeredServings = 5, status = "Draft" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
