using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.IntegrationTests.Infrastructure;

namespace BlueMeal.IntegrationTests;

/// <summary>Phase P3: dishes, versioned recipes, realtime computation, recommendations.</summary>
[Collection(nameof(ApiCollection))]
public sealed class RecipeTests(ApiFactory f)
{
    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
    }

    private async Task<(HttpClient C, string RiceId, string OilId)> Setup()
    {
        var c = await f.AdminAsync();
        var rice = (await J(await c.GetAsync("/api/v1/ingredients?q=gao te bac huong"))).GetProperty("items")[0].GetProperty("id").GetString()!;
        var oil = (await J(await c.GetAsync("/api/v1/ingredients?q=dau an thuc vat"))).GetProperty("items")[0].GetProperty("id").GetString()!;
        return (c, rice, oil);
    }

    [Fact] // sample rice per 100 g: E(table) 344, P 7.9, L 1.0, G 75.9. 130 g → E(table) 1.3 × 344 = 447.2; E(macros) = 4×10.27 + 9×1.3 + 4×98.67 = 447.46; cost 130/1000 × 18 000 = 2 340
    public async Task Realtime_compute_matches_hand_calculation()
    {
        var (c, rice, _) = await Setup();
        var r = await J(await c.PostAsJsonAsync("/api/v1/recipes/compute", new { cookingMethod = "Boil", dishType = "Staple", baseServings = 1, cookingLossRate = 0, items = new[] { new { ingredientId = rice, grossG = 130 } }, servings = 350 }));
        var n = r.GetProperty("nutrition");
        n.GetProperty("proteinG").GetDecimal().Should().Be(10.27m);
        n.GetProperty("energyTableKcal").GetDecimal().Should().Be(447.2m);
        n.GetProperty("energyFromMacrosKcal").GetDecimal().Should().Be(447.46m);
        // price mode default = weighted average → falls back to the reference price when there is no stock yet
        var price = (await J(await c.GetAsync($"/api/v1/ingredients/{rice}"))).GetProperty("referencePrice").GetDecimal();
        r.GetProperty("cost").GetProperty("foodCostPerServing").GetDecimal().Should().Be(130m / 1000m * price);
        var scaled = r.GetProperty("scaled")[0];
        scaled.GetProperty("grossG").GetDecimal().Should().Be(45500m);
        scaled.GetProperty("purchaseUnits").GetDecimal().Should().Be(45.5m);
    }

    [Fact]
    public async Task Versioning_activate_archives_previous_and_compare()
    {
        var (c, rice, oil) = await Setup();
        var dish = (await J(await c.PostAsJsonAsync("/api/v1/dishes", new { code = "MON-TEST-VER", nameVi = "Cơm rang thử", dishType = "Staple", cookingMethod = "StirFry", isActive = true }))).GetProperty("id").GetString();
        var v1 = await J(await c.PostAsJsonAsync("/api/v1/recipes", new { dishId = dish, baseServings = 10, cookingLossRate = 10, items = new[] { new { ingredientId = rice, grossG = 1200 }, new { ingredientId = oil, grossG = 50 } } }));
        v1.GetProperty("versionNo").GetInt32().Should().Be(1);
        var v1Id = v1.GetProperty("id").GetString();
        (await J(await c.GetAsync($"/api/v1/recipes/{v1Id}"))).GetProperty("isCurrent").GetBoolean().Should().BeTrue(); // first recipe is current

        var v2 = await J(await c.PostAsJsonAsync($"/api/v1/recipes/{v1Id}/clone", new { }));
        var v2Id = v2.GetProperty("id").GetString();
        v2.GetProperty("versionNo").GetInt32().Should().Be(2);
        var detail = await J(await c.GetAsync($"/api/v1/recipes/{v2Id}"));
        detail.GetProperty("status").GetString().Should().Be("Draft");
        (await c.PutAsJsonAsync($"/api/v1/recipes/{v2Id}", new { dishId = dish, baseServings = 10, cookingLossRate = 10, items = new[] { new { ingredientId = rice, grossG = 1400 }, new { ingredientId = oil, grossG = 40 } } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        // an active recipe cannot be edited in place
        (await c.PutAsJsonAsync($"/api/v1/recipes/{v1Id}", new { dishId = dish, baseServings = 10, cookingLossRate = 0, items = new[] { new { ingredientId = rice, grossG = 1 } } }))
            .StatusCode.Should().Be((HttpStatusCode)422);

        var cmp = await J(await c.GetAsync($"/api/v1/recipes/compare?a={v1Id}&b={v2Id}"));
        cmp.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("ingredientId").GetString() == rice).GetProperty("diff").GetDecimal().Should().Be(20m); // (1400−1200)/10

        (await c.PostAsync($"/api/v1/recipes/{v2Id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await J(await c.GetAsync($"/api/v1/recipes/{v1Id}"))).GetProperty("status").GetString().Should().Be("Archived");
        var quick = await J(await c.GetAsync($"/api/v1/dishes/{dish}/quick"));
        quick.GetProperty("recipeId").GetString().Should().Be(v2Id);
        quick.GetProperty("nutrition").GetProperty("proteinG").GetDecimal().Should().Be(11.06m); // 140 g rice × 7.9 / 100

        // the list carries kcal and cost of one serving per version (two versions of the same dish → computed separately)
        var listed = (await J(await c.GetAsync($"/api/v1/recipes?dishId={dish}"))).GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!);
        listed[v2Id!].GetProperty("energyKcal").GetDecimal().Should().Be(Math.Round(quick.GetProperty("nutrition").GetProperty("energyKcal").GetDecimal(), 0));
        listed[v1Id!].GetProperty("energyKcal").GetDecimal().Should().BeLessThan(listed[v2Id!].GetProperty("energyKcal").GetDecimal()); // 120 g vs 140 g rice
        listed[v2Id!].GetProperty("costPerServing").GetDecimal().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Price_mode_contract_uses_active_contract_price()
    {
        var (c, rice, _) = await Setup();
        var partner = (await J(await c.GetAsync("/api/v1/partners?q=gao va gia vi"))).GetProperty("items")[0].GetProperty("id").GetString();
        await J(await c.PostAsJsonAsync($"/api/v1/partners/{partner}/contracts", new
        {
            contractNo = "HD-GAO", signedDate = "2026-01-01", validFrom = "2026-01-01", validTo = "2099-12-31", value = 1, status = "Active",
            prices = new[] { new { ingredientId = rice, unitPrice = 16000 } },
        }));
        var cost = await J(await c.GetAsync("/api/v1/settings/cost"));
        (await c.PutAsJsonAsync("/api/v1/settings/cost", new { priceMode = "Contract", overheads = cost.GetProperty("overheads") })).EnsureSuccessStatusCode();
        try
        {
            var r = await J(await c.PostAsJsonAsync("/api/v1/recipes/compute", new { baseServings = 1, cookingLossRate = 0, items = new[] { new { ingredientId = rice, grossG = 1000 } } }));
            r.GetProperty("cost").GetProperty("foodCostPerServing").GetDecimal().Should().Be(16000m);
        }
        finally
        {
            (await c.PutAsJsonAsync("/api/v1/settings/cost", new { priceMode = "WeightedAverage", overheads = cost.GetProperty("overheads") })).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Recommendation_with_nutrient_targets()
    {
        var c = await f.AdminAsync();
        var na = (await J(await c.GetAsync("/api/v1/nutrients?q=natri"))).GetProperty("items")[0].GetProperty("id").GetString();
        var rec = await J(await c.PostAsJsonAsync("/api/v1/recommendations", new
        {
            code = "KN-TEST", nameVi = "Khuyến nghị thử", energyPerDayKcal = 2400, mealSharePercent = 40, proteinPctMin = 13, proteinPctMax = 20, lipidPctMin = 20, lipidPctMax = 25,
            glucidPctMin = 55, glucidPctMax = 65, animalProteinPctMin = 30, animalProteinPctMax = 50, isActive = true,
            nutrients = new[] { new { nutrientId = na, maxPerMeal = 700, level = "Critical" } },
        }));
        rec.GetProperty("mealEnergyKcal").GetDecimal().Should().Be(960m);
        rec.GetProperty("nutrients")[0].GetProperty("nutrientCode").GetString().Should().Be("NA");
        // invalid range rejected
        (await c.PostAsJsonAsync("/api/v1/recommendations", new { code = "KN-BAD", nameVi = "x", energyPerDayKcal = 2400, mealSharePercent = 40, proteinPctMin = 25, proteinPctMax = 20 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sample_dishes_have_current_recipes()
    {
        var c = await f.AdminAsync();
        var dishes = await J(await c.GetAsync("/api/v1/dishes?pageSize=100"));
        dishes.GetProperty("totalItems").GetInt64().Should().BeGreaterThanOrEqualTo(30);
        dishes.GetProperty("items").EnumerateArray().Where(d => d.GetProperty("code").GetString()!.StartsWith("MON-") && !d.GetProperty("code").GetString()!.Contains("TEST"))
            .Should().OnlyContain(d => d.GetProperty("currentRecipeId").ValueKind == JsonValueKind.String);
    }
}
