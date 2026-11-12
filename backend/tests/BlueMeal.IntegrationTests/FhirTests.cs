using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.IntegrationTests.Infrastructure;

namespace BlueMeal.IntegrationTests;

/// <summary>HL7 FHIR R4 read-only server (TT 13/2025/TT-BYT): CapabilityStatement, NutritionOrder, Observation, OperationOutcome.</summary>
[Collection(nameof(ApiCollection))]
public sealed class FhirTests(ApiFactory f)
{
    private const string Base = "/api/v1/public/fhir/r4";

    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var text = await r.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private static async Task<JsonElement> Fhir(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/fhir+json");
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
    }

    /// <summary>Generates, approves and publishes one menu on a date no other test uses; returns its code.</summary>
    private async Task<string> PublishedMenuAsync(HttpClient c, DateOnly date)
    {
        string Id(JsonElement page) => page.GetProperty("items")[0].GetProperty("id").GetString()!;
        var shift = Id(await J(await c.GetAsync("/api/v1/meal-shifts?q=CA1")));
        var subject = Id(await J(await c.GetAsync("/api/v1/subject-groups?q=lao dong vua")));
        var customer = Id(await J(await c.GetAsync("/api/v1/customers?q=co khi")));
        var structure = Id(await J(await c.GetAsync("/api/v1/meal-structures?q=CHUAN")));
        var rec = Id(await J(await c.GetAsync("/api/v1/recommendations?q=KN-VUA")));
        var saved = await J(await c.PostAsJsonAsync("/api/v1/menus/generate", new
        {
            request = new
            {
                fromDate = date.ToString("yyyy-MM-dd"), toDate = date.ToString("yyyy-MM-dd"), mealShiftIds = new[] { shift }, subjectGroupId = subject, customerId = customer,
                recommendationId = rec, structureTemplateId = structure, budgetPerServing = 45000, noRepeatDays = 3, preferExpiringStock = false, newDishRatio = 0.2, seed = 7, servings = 120,
                skipWeekends = true,
            },
            save = true,
        }));
        var menuId = saved.GetProperty("saved")[0].GetString()!;
        (await c.PostAsJsonAsync($"/api/v1/menus/{menuId}/submit", new { comment = "FHIR test" })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"/api/v1/menus/{menuId}/approve", new { comment = "OK" })).EnsureSuccessStatusCode();
        (await c.PostAsync($"/api/v1/menus/{menuId}/publish", null)).EnsureSuccessStatusCode();
        return (await J(await c.GetAsync($"/api/v1/menus/{menuId}"))).GetProperty("code").GetString()!;
    }

    private async Task<HttpClient> ApiAsync(HttpClient admin, params string[] scopes)
    {
        var client = await J(await admin.PostAsJsonAsync("/api/v1/integration/clients", new { name = "HIS bệnh viện (FHIR)", scopes }));
        var token = await J(await f.CreateClient().PostAsync("/api/v1/public/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = client.GetProperty("clientId").GetString()!, ["client_secret"] = client.GetProperty("clientSecret").GetString()!,
        })));
        var api = f.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
        return api;
    }

    [Fact]
    public async Task Capability_statement_is_public_and_lists_the_resources()
    {
        var cs = await Fhir(await f.CreateClient().GetAsync($"{Base}/metadata"));
        cs.GetProperty("resourceType").GetString().Should().Be("CapabilityStatement");
        cs.GetProperty("fhirVersion").GetString().Should().Be("4.0.1");
        cs.GetProperty("kind").GetString().Should().Be("instance");
        var rest = cs.GetProperty("rest")[0];
        rest.GetProperty("security").GetProperty("service")[0].GetProperty("coding")[0].GetProperty("code").GetString().Should().Be("OAuth");
        rest.GetProperty("resource").EnumerateArray().Select(r => r.GetProperty("type").GetString()).Should().BeEquivalentTo("NutritionOrder", "Observation");
    }

    [Fact]
    public async Task Published_menu_as_nutrition_order_and_nutrient_observation()
    {
        var admin = await f.AdminAsync();
        var date = new DateOnly(2028, 3, 6);
        var code = await PublishedMenuAsync(admin, date);
        var api = await ApiAsync(admin, "menu.read", "nutrition.read");

        var bundle = await Fhir(await api.GetAsync($"{Base}/NutritionOrder?date={date:yyyy-MM-dd}"));
        bundle.GetProperty("resourceType").GetString().Should().Be("Bundle");
        bundle.GetProperty("type").GetString().Should().Be("searchset");
        var entry = bundle.GetProperty("entry").EnumerateArray().Single(e => e.GetProperty("resource").GetProperty("id").GetString() == code);
        entry.GetProperty("fullUrl").GetString().Should().StartWith("http").And.EndWith($"{Base}/NutritionOrder/{code}");
        entry.GetProperty("search").GetProperty("mode").GetString().Should().Be("match");
        var order = entry.GetProperty("resource");
        order.GetProperty("intent").GetString().Should().Be("plan");
        order.GetProperty("dateTime").GetString().Should().Be("2028-03-06");
        // population-level data: the mandatory patient reference says why it is absent
        order.GetProperty("patient").GetProperty("extension")[0].GetProperty("valueCode").GetString().Should().Be("not-applicable");
        var energy = order.GetProperty("oralDiet").GetProperty("nutrient")[0];
        energy.GetProperty("modifier").GetProperty("coding")[0].GetProperty("code").GetString().Should().Be("ENERGY");
        energy.GetProperty("amount").GetProperty("code").GetString().Should().Be("kcal");
        energy.GetProperty("amount").GetProperty("value").GetDecimal().Should().BeGreaterThan(300);

        (await Fhir(await api.GetAsync($"{Base}/NutritionOrder/{code}"))).GetProperty("id").GetString().Should().Be(code);

        var observations = await Fhir(await api.GetAsync($"{Base}/Observation?date={date:yyyy-MM-dd}"));
        var obs = observations.GetProperty("entry").EnumerateArray().Select(e => e.GetProperty("resource")).Single(r => r.GetProperty("id").GetString() == $"{code}-nutrition");
        obs.GetProperty("status").GetString().Should().Be("final");
        obs.GetProperty("focus")[0].GetProperty("reference").GetString().Should().Be($"NutritionOrder/{code}");
        var components = obs.GetProperty("component").EnumerateArray().ToList();
        components.Select(c => c.GetProperty("code").GetProperty("coding")[0].GetProperty("code").GetString()).Should().Contain(["ENERGY", "PROTEIN", "LIPID", "GLUCID"]);
        components.Single(c => c.GetProperty("code").GetProperty("coding")[0].GetProperty("code").GetString() == "PROTEIN")
            .GetProperty("valueQuantity").GetProperty("system").GetString().Should().Be("http://unitsofmeasure.org");
        (await Fhir(await api.GetAsync($"{Base}/Observation/{code}-nutrition"))).GetProperty("resourceType").GetString().Should().Be("Observation");    }

    [Fact]
    public async Task Errors_are_operation_outcomes_and_scopes_are_enforced()
    {
        var admin = await f.AdminAsync();
        var api = await ApiAsync(admin, "menu.read");

        var missing = await Fhir(await api.GetAsync($"{Base}/NutritionOrder"), HttpStatusCode.BadRequest);
        missing.GetProperty("resourceType").GetString().Should().Be("OperationOutcome");
        missing.GetProperty("issue")[0].GetProperty("code").GetString().Should().Be("required");

        var notFound = await Fhir(await api.GetAsync($"{Base}/NutritionOrder/TD-19000101-0001"), HttpStatusCode.NotFound);
        notFound.GetProperty("issue")[0].GetProperty("code").GetString().Should().Be("not-found");

        // nutrient content needs nutrition.read; no token at all → 401
        (await api.GetAsync($"{Base}/Observation?date=2026-01-05")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await f.CreateClient().GetAsync($"{Base}/NutritionOrder?date=2026-01-05")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
