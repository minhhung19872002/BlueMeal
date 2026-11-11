using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using BlueMeal.IntegrationTests.Infrastructure;

namespace BlueMeal.IntegrationTests;

/// <summary>Phase P7 acceptance: sync from the VDD mock succeeds, conflicts are shown and resolved, public API with client credentials (JSON/XML/FHIR).</summary>
[Collection(nameof(ApiCollection))]
public sealed class IntegrationTests(ApiFactory f)
{
    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var text = await r.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private static object Endpoint(string code, string authType, object credentials) => new
    {
        code, nameVi = "Cổng dữ liệu Viện Dinh dưỡng (mock)", baseUrl = "http://mock-vdd", authType, credentials, timeoutSeconds = 10, syncCron = "0 2 * * *", isEnabled = true, isActive = true,
    };

    private static async Task<JsonElement> FoodByCode(HttpClient c, string code)
    {
        var id = (await J(await c.GetAsync($"/api/v1/foods?q={code}"))).GetProperty("items").EnumerateArray().First(x => x.GetProperty("code").GetString() == code).GetProperty("id").GetString();
        return await J(await c.GetAsync($"/api/v1/foods/{id}"));
    }

    private static decimal Nutrient(JsonElement food, string code)
        => food.GetProperty("nutrients").GetProperty(code).GetDecimal();

    [Fact]
    public async Task Sync_from_mock_with_conflicts_and_resolution()
    {
        var c = await f.AdminAsync();
        var mock = f.MockVddClient();
        (await mock.PostAsync("/__admin/reset", null)).EnsureSuccessStatusCode();

        var ep = await J(await c.PostAsJsonAsync("/api/v1/integration/endpoints", Endpoint("VDD-OAUTH", "OAuth2ClientCredentials", new { clientId = "bluemeal", clientSecret = "demo-vdd-secret" })));
        var epId = ep.GetProperty("id").GetString();
        ep.GetProperty("credentials").GetProperty("hasClientSecret").GetBoolean().Should().BeTrue();
        ep.GetRawText().Should().NotContain("demo-vdd-secret"); // write-only credentials
        var test = await J(await c.PostAsync($"/api/v1/integration/endpoints/{epId}/test", null));
        test.GetProperty("ok").GetBoolean().Should().BeTrue();
        test.GetProperty("nutrients").GetInt32().Should().Be(12);

        // 1. full sync: 17 new foods; M-NC01 exists locally as a manual record → conflict
        var s1 = (await J(await c.PostAsJsonAsync("/api/v1/integration/sync", new { endpointId = epId })))[0];
        s1.GetProperty("success").GetBoolean().Should().BeTrue(s1.ToString());
        s1.GetProperty("conflicts").GetInt32().Should().Be(1);
        var sauce = await FoodByCode(c, "VDD-1201");
        sauce.GetProperty("source").GetString().Should().Be("VddApi");
        sauce.GetProperty("isLocked").GetBoolean().Should().BeTrue();
        Nutrient(sauce, "NA").Should().Be(7720); // 7,72 g at the source → 7.720 mg locally
        var recs = await J(await c.GetAsync("/api/v1/recommendations?q=VDD-NCDD"));
        recs.GetProperty("totalItems").GetInt64().Should().Be(2);

        // 2. incremental: nothing changed at the source → no updates
        var s2 = (await J(await c.PostAsJsonAsync("/api/v1/integration/sync", new { endpointId = epId })))[0];
        s2.GetProperty("updated").GetInt32().Should().Be(0);
        s2.GetProperty("created").GetInt32().Should().Be(0);

        // 3. source changes an untouched food → updated; source changes a locally edited food → conflict
        (await mock.PostAsJsonAsync("/__admin/foods/VDD-0401", new { name_vi = "Cải bó xôi (rau chân vịt)" })).EnsureSuccessStatusCode();
        var broccoli = await FoodByCode(c, "VDD-0402");
        var edit = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(broccoli.GetRawText())!;
        edit["note"] = JsonSerializer.SerializeToElement("Đã hiệu chỉnh theo phòng thí nghiệm");
        (await c.PutAsJsonAsync($"/api/v1/foods/{broccoli.GetProperty("id").GetString()}", edit)).EnsureSuccessStatusCode();
        (await mock.PostAsJsonAsync("/__admin/foods/VDD-0402", new { composition = new { VITC = 92.5 } })).EnsureSuccessStatusCode();
        await Task.Delay(50);
        var s3 = (await J(await c.PostAsJsonAsync("/api/v1/integration/sync", new { endpointId = epId })))[0];
        s3.GetProperty("updated").GetInt32().Should().Be(1, s3.ToString());
        s3.GetProperty("conflicts").GetInt32().Should().Be(1);
        (await FoodByCode(c, "VDD-0401")).GetProperty("nameVi").GetString().Should().Be("Cải bó xôi (rau chân vịt)");

        var conflicts = (await J(await c.GetAsync("/api/v1/integration/conflicts"))).GetProperty("items").EnumerateArray().ToList();
        var broccoliConflict = conflicts.First(x => x.GetProperty("entityCode").GetString() == "VDD-0402");
        broccoliConflict.GetProperty("remote").GetProperty("nutrients").GetProperty("VIT_C").GetDecimal().Should().Be(92.5m);
        broccoliConflict.GetProperty("local").GetProperty("nutrients").GetProperty("VIT_C").GetDecimal().Should().Be(89);
        var riceConflict = conflicts.First(x => x.GetProperty("entityCode").GetString() == "M-NC01");

        // 4. resolve: accept remote for broccoli, keep local for rice
        (await c.PostAsJsonAsync($"/api/v1/integration/conflicts/{broccoliConflict.GetProperty("id").GetString()}/resolve", new { keep = "remote" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.PostAsJsonAsync($"/api/v1/integration/conflicts/{riceConflict.GetProperty("id").GetString()}/resolve", new { keep = "local" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Nutrient(await FoodByCode(c, "VDD-0402"), "VIT_C").Should().Be(92.5m);
        (await FoodByCode(c, "M-NC01")).GetProperty("source").GetString().Should().Be("Manual");
        (await J(await c.GetAsync("/api/v1/integration/conflicts"))).GetProperty("totalItems").GetInt64().Should().Be(0);
        (await c.PostAsJsonAsync($"/api/v1/integration/conflicts/{riceConflict.GetProperty("id").GetString()}/resolve", new { keep = "local" })).StatusCode.Should().Be((HttpStatusCode)422);

        // a full re-sync does not raise the kept-local conflict again for the same remote version
        var s4 = (await J(await c.PostAsJsonAsync("/api/v1/integration/sync", new { endpointId = epId, full = true })))[0];
        s4.GetProperty("conflicts").GetInt32().Should().Be(0, s4.ToString());

        // 5. transient failures are retried (Polly)
        (await mock.PostAsync("/__admin/fail?count=2", null)).EnsureSuccessStatusCode();
        var s5 = (await J(await c.PostAsJsonAsync("/api/v1/integration/sync", new { endpointId = epId })))[0];
        s5.GetProperty("success").GetBoolean().Should().BeTrue(s5.ToString());

        // 6. optional push of aggregated figures
        var push = await J(await c.PostAsJsonAsync($"/api/v1/integration/endpoints/{epId}/push", new { from = "2026-01-01", to = "2026-12-31" }));
        push.GetProperty("accepted").GetBoolean().Should().BeTrue();

        var logs = (await J(await c.GetAsync($"/api/v1/integration/logs?endpointId={epId}&pageSize=50"))).GetProperty("items").EnumerateArray().ToList();
        logs.Should().HaveCountGreaterThanOrEqualTo(6);
        logs.Should().Contain(l => l.GetProperty("operation").GetString() == "PushAggregatedReport");
        logs.First(l => l.GetProperty("operation").GetString() == "FullSync").GetProperty("requestSummary").GetString().Should().Contain("GetFoods");
    }

    [Fact]
    public async Task Api_key_and_basic_auth_and_failures_are_reported()
    {
        var c = await f.AdminAsync();
        var key = await J(await c.PostAsJsonAsync("/api/v1/integration/endpoints", Endpoint("VDD-KEY", "ApiKey", new { apiKey = "demo-vdd-key", apiKeyHeader = "X-Api-Key" })));
        (await J(await c.PostAsync($"/api/v1/integration/endpoints/{key.GetProperty("id").GetString()}/test", null))).GetProperty("ok").GetBoolean().Should().BeTrue();
        var basic = await J(await c.PostAsJsonAsync("/api/v1/integration/endpoints", Endpoint("VDD-BASIC", "Basic", new { username = "demo", password = "demo" })));
        (await J(await c.PostAsync($"/api/v1/integration/endpoints/{basic.GetProperty("id").GetString()}/test", null))).GetProperty("ok").GetBoolean().Should().BeTrue();
        var bad = await J(await c.PostAsJsonAsync("/api/v1/integration/endpoints", Endpoint("VDD-BAD", "ApiKey", new { apiKey = "wrong" })));
        var t = await J(await c.PostAsync($"/api/v1/integration/endpoints/{bad.GetProperty("id").GetString()}/test", null));
        t.GetProperty("ok").GetBoolean().Should().BeFalse();
        t.GetProperty("error").GetString().Should().Contain("401");
        // invalid mapping JSON is rejected
        var badMap = new Dictionary<string, object?> { ["code"] = "VDD-MAP", ["nameVi"] = "x", ["baseUrl"] = "http://mock-vdd", ["authType"] = "None", ["timeoutSeconds"] = 10, ["mappingJson"] = "{not json", ["isEnabled"] = true, ["isActive"] = true };
        (await c.PostAsJsonAsync("/api/v1/integration/endpoints", badMap)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Public_api_with_client_credentials_json_xml_and_fhir()
    {
        var c = await f.AdminAsync();
        var client = await J(await c.PostAsJsonAsync("/api/v1/integration/clients", new { name = "Hệ thống HRM nhà máy", scopes = new[] { "menu.read", "food.read" } }));
        var clientId = client.GetProperty("clientId").GetString()!;
        var secret = client.GetProperty("clientSecret").GetString()!;
        var anon = f.CreateClient();

        // token: wrong secret → 401, scope not granted → 400
        (await anon.PostAsync("/api/v1/public/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = "nope" })))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsync("/api/v1/public/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret, ["scope"] = "nutrition.read" })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var token = await J(await anon.PostAsync("/api/v1/public/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret })));
        token.GetProperty("token_type").GetString().Should().Be("Bearer");
        var bearer = token.GetProperty("access_token").GetString();

        (await anon.GetAsync("/api/v1/public/foods")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var api = f.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var foods = await J(await api.GetAsync("/api/v1/public/foods?pageSize=5"));
        foods.GetProperty("items").GetArrayLength().Should().Be(5);
        foods.GetProperty("items")[0].GetProperty("per100g").GetArrayLength().Should().BeGreaterThan(3);

        // XML via content negotiation
        var xmlReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/foods?pageSize=2");
        xmlReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        var xml = await api.SendAsync(xmlReq);
        xml.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        var doc = XDocument.Parse(await xml.Content.ReadAsStringAsync());
        doc.Root!.Name.LocalName.Should().Be("foods");
        doc.Root.Elements("food").Should().HaveCount(2);

        (await api.GetAsync("/api/v1/public/menus?from=2026-01-01&to=2026-01-31")).StatusCode.Should().Be(HttpStatusCode.OK);
        // scope not granted
        (await api.GetAsync("/api/v1/public/nutrition/summary?from=2026-01-01&to=2026-01-31")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // a user access token is not accepted by the public API
        (await c.GetAsync("/api/v1/public/foods")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var fhir = await api.GetAsync("/api/v1/public/fhir/r4/NutritionOrder?date=2026-01-05");
        fhir.Content.Headers.ContentType!.MediaType.Should().Be("application/fhir+json");
        var bundle = JsonDocument.Parse(await fhir.Content.ReadAsStringAsync()).RootElement;
        bundle.GetProperty("resourceType").GetString().Should().Be("Bundle");
        bundle.GetProperty("type").GetString().Should().Be("searchset");

        // disabling the client stops new tokens
        (await c.PostAsync($"/api/v1/integration/clients/{client.GetProperty("id").GetString()}/toggle", null)).EnsureSuccessStatusCode();
        (await anon.PostAsync("/api/v1/public/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret })))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OpenApi_document_is_exported_to_docs()
    {
        var c = await f.AdminAsync();
        (await c.PutAsJsonAsync("/api/v1/settings/system.swagger", new { enabledInProduction = true })).EnsureSuccessStatusCode();
        var r = await c.GetAsync("/swagger/v1/swagger.json");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await r.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;
        doc.GetProperty("paths").TryGetProperty("/api/v1/public/foods", out _).Should().BeTrue();
        // docs/api/openapi.json is generated from the running API (SPEC §7.10.6) when BLUEMEAL_EXPORT_ARTIFACTS=1
        if (RepoArtifacts.Directory("docs", "api") is { } dir)
            await File.WriteAllTextAsync(Path.Combine(dir, "openapi.json"), JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n"));
    }
}
