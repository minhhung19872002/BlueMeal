using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json.Nodes;

namespace MockVddApi;

public sealed class MockOptions
{
    public string ApiKey { get; set; } = "demo-vdd-key";
    public string BasicUser { get; set; } = "demo";
    public string BasicPassword { get; set; } = "demo";
    public string ClientId { get; set; } = "bluemeal";
    public string ClientSecret { get; set; } = "demo-vdd-secret";
    public bool RequireAuth { get; set; } = true;
}

/// <summary>
/// Mock of the (not yet specified) Viện Dinh dưỡng data portal — the assumed contract is described in README.md (ASSUMPTIONS A-25).
/// snake_case JSON, INFOODS nutrient tagnames, units per composition value, incremental sync by <c>modified_since</c>.
/// </summary>
public static class MockServer
{
    public static void Configure(IServiceCollection services, IConfiguration config)
    {
        services.Configure<MockOptions>(config.GetSection("Mock"));
        services.AddSingleton<MockStore>();
    }

    public static void Map(WebApplication app)
    {
        var opts = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MockOptions>>().Value;
        var store = app.Services.GetRequiredService<MockStore>();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapPost("/oauth/token", async (HttpRequest req) =>
        {
            var form = await req.ReadFormAsync();
            if (form["grant_type"] != "client_credentials" || form["client_id"] != opts.ClientId || form["client_secret"] != opts.ClientSecret)
                return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            var token = Convert.ToHexString(Guid.NewGuid().ToByteArray());
            store.Tokens[token] = DateTimeOffset.UtcNow.AddHours(1);
            return Results.Json(new { access_token = token, token_type = "Bearer", expires_in = 3600 });
        });

        // authentication + fault injection for every /api call
        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/api")) { await next(); return; }
            Interlocked.Increment(ref store.ApiCalls);
            if (store.FailNext > 0 && Interlocked.Decrement(ref store.FailNext) >= 0) { ctx.Response.StatusCode = 503; return; }
            if (opts.RequireAuth && !Authorized(ctx.Request, opts, store)) { ctx.Response.StatusCode = 401; await ctx.Response.WriteAsJsonAsync(new { error = "unauthorized" }); return; }
            await next();
        });

        app.MapGet("/api/v1/nutrients", () => Results.Json(new { data = MockStore.Nutrients }));
        app.MapGet("/api/v1/food-groups", (string? modified_since) => Results.Json(new { data = store.Groups.Where(g => Since(g, modified_since)).ToList() }));
        app.MapGet("/api/v1/foods", (string? modified_since, int? page, int? page_size) =>
        {
            var p = Math.Max(1, page ?? 1);
            var size = Math.Clamp(page_size ?? 50, 1, 500);
            var all = store.Foods.Values.Where(f => Since(f, modified_since)).OrderBy(f => (string)f["food_code"]!).ToList();
            return Results.Json(new { data = all.Skip((p - 1) * size).Take(size).ToList(), page = p, page_size = size, total = all.Count });
        });
        app.MapGet("/api/v1/recommendations", (string? modified_since) => Results.Json(new { data = store.Recommendations.Where(r => Since(r, modified_since)).ToList() }));
        app.MapPost("/api/v1/reports/aggregated", async (HttpRequest req) =>
        {
            var body = await JsonNode.ParseAsync(req.Body);
            store.Pushed.Add(body!);
            return Results.Json(new { accepted = true, receipt_id = $"VDD-{DateTime.UtcNow:yyyyMMddHHmmss}-{store.Pushed.Count:000}", message = "Đã tiếp nhận / Received" });
        });

        // ---- admin endpoints (demo and automated tests) -----------------------------------------------------------
        app.MapPost("/__admin/reset", () => { store.Reset(); return Results.NoContent(); });
        app.MapPost("/__admin/fail", (int count) => { store.FailNext = count; return Results.NoContent(); });
        app.MapGet("/__admin/stats", () => Results.Json(new { apiCalls = store.ApiCalls, pushed = store.Pushed.Count }));
        app.MapPost("/__admin/foods/{code}", async (string code, HttpRequest req) =>
        {
            if (!store.Foods.TryGetValue(code, out var food)) return Results.NotFound();
            var patch = (await JsonNode.ParseAsync(req.Body))!.AsObject();
            foreach (var (k, v) in patch)
            {
                if (k == "composition" && v is JsonObject comp)
                {
                    foreach (var (nk, nv) in comp)
                    {
                        var arr = food["composition"]!.AsArray();
                        var item = arr.FirstOrDefault(x => string.Equals((string?)x!["nutrient_code"], nk, StringComparison.OrdinalIgnoreCase));
                        if (item is null) arr.Add(new JsonObject { ["nutrient_code"] = nk.ToUpperInvariant(), ["value"] = nv!.DeepClone(), ["unit"] = "g" });
                        else item["value"] = nv!.DeepClone();
                    }
                }
                else food[k] = v?.DeepClone();
            }
            food["version"] = ((int?)food["version"] ?? 1) + 1;
            food["updated_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            return Results.Json(food);
        });
    }

    private static bool Authorized(HttpRequest req, MockOptions o, MockStore store)
    {
        if (req.Headers.TryGetValue("X-Api-Key", out var key) && key == o.ApiKey) return true;
        var auth = req.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.Ordinal) && store.Tokens.TryGetValue(auth[7..], out var exp) && exp > DateTimeOffset.UtcNow) return true;
        if (auth.StartsWith("Basic ", StringComparison.Ordinal))
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..]));
            return raw == $"{o.BasicUser}:{o.BasicPassword}";
        }
        return false;
    }

    private static bool Since(JsonNode node, string? since)
        => since is null || !DateTimeOffset.TryParse(since, out var s) || !DateTimeOffset.TryParse((string?)node["updated_at"], out var u) || u > s;
}

/// <summary>In-memory data of the mock portal.</summary>
public sealed class MockStore
{
    public readonly ConcurrentDictionary<string, DateTimeOffset> Tokens = new();
    public readonly ConcurrentBag<JsonNode> Pushed = [];
    public int ApiCalls;
    public int FailNext;
    public List<JsonObject> Groups { get; private set; } = [];
    public ConcurrentDictionary<string, JsonObject> Foods { get; private set; } = new();
    public List<JsonObject> Recommendations { get; private set; } = [];

    public MockStore() => Reset();

    private const string Seeded = "2026-01-15T00:00:00Z";

    public static readonly object[] Nutrients =
    [
        new { nutrient_code = "ENERC_KCAL", name_vi = "Năng lượng", name_en = "Energy", unit = "kcal", group = "Energy" },
        new { nutrient_code = "WATER", name_vi = "Nước", name_en = "Water", unit = "g", group = "Other" },
        new { nutrient_code = "PROCNT", name_vi = "Protein", name_en = "Protein", unit = "g", group = "Macro" },
        new { nutrient_code = "FAT", name_vi = "Lipid", name_en = "Fat", unit = "g", group = "Macro" },
        new { nutrient_code = "CHOCDF", name_vi = "Glucid", name_en = "Carbohydrate", unit = "g", group = "Macro" },
        new { nutrient_code = "FIBTG", name_vi = "Chất xơ", name_en = "Dietary fibre", unit = "g", group = "Macro" },
        new { nutrient_code = "CA", name_vi = "Canxi", name_en = "Calcium", unit = "mg", group = "Mineral" },
        new { nutrient_code = "FE", name_vi = "Sắt", name_en = "Iron", unit = "mg", group = "Mineral" },
        new { nutrient_code = "NA", name_vi = "Natri", name_en = "Sodium", unit = "mg", group = "Mineral" },
        new { nutrient_code = "VITC", name_vi = "Vitamin C", name_en = "Vitamin C", unit = "mg", group = "Vitamin" },
        new { nutrient_code = "VITA_RAE", name_vi = "Vitamin A", name_en = "Vitamin A", unit = "µg", group = "Vitamin" },
        new { nutrient_code = "THIA", name_vi = "Vitamin B1", name_en = "Thiamin", unit = "mg", group = "Vitamin" },
    ];

    // code, vi, en, group, refuse, kcal, water, protein, fat, carb, fibre, Ca, Fe, Na(g!), vitC, vitA(µg), B1
    private static readonly (string, string, string, string, decimal, decimal, decimal, decimal, decimal, decimal, decimal, decimal, decimal, decimal, decimal, decimal, decimal)[] FoodRows =
    [
        ("M-NC01", "Gạo tẻ máy", "Rice, milled", "NGU_COC", 0, 344, 14.0m, 7.9m, 1.0m, 75.9m, 0.4m, 30, 1.3m, 0.005m, 0, 0, 0.10m),
        ("VDD-0101", "Gạo lứt", "Brown rice", "NGU_COC", 0, 351, 13.0m, 7.5m, 2.7m, 74.9m, 3.4m, 33, 1.8m, 0.007m, 0, 0, 0.41m),
        ("VDD-0102", "Yến mạch", "Oats", "NGU_COC", 0, 389, 8.2m, 16.9m, 6.9m, 66.3m, 10.6m, 54, 4.7m, 0.002m, 0, 0, 0.76m),
        ("VDD-0201", "Khoai sọ", "Taro corm", "KHOAI_CU", 15, 109, 70.0m, 1.8m, 0.1m, 26.0m, 1.2m, 44, 0.8m, 0.010m, 4, 0, 0.09m),
        ("VDD-0301", "Đậu Hà Lan hạt", "Green peas", "HAT_DAM_BEO", 0, 342, 11.0m, 22.0m, 1.2m, 60.0m, 6.0m, 57, 4.4m, 0.005m, 4, 38, 0.72m),
        ("VDD-0401", "Cải bó xôi", "Spinach", "RAU", 20, 23, 91.4m, 2.9m, 0.4m, 3.6m, 2.2m, 99, 2.7m, 0.079m, 28, 469, 0.08m),
        ("VDD-0402", "Bông cải xanh", "Broccoli", "RAU", 30, 34, 89.3m, 2.8m, 0.4m, 6.6m, 2.6m, 47, 0.7m, 0.033m, 89, 31, 0.07m),
        ("VDD-0403", "Cà rốt", "Carrot", "RAU", 15, 41, 88.3m, 0.9m, 0.2m, 9.6m, 2.8m, 33, 0.3m, 0.069m, 6, 835, 0.07m),
        ("VDD-0404", "Bí đỏ", "Pumpkin", "RAU", 25, 26, 91.6m, 1.0m, 0.1m, 6.5m, 0.5m, 21, 0.8m, 0.001m, 9, 426, 0.05m),
        ("VDD-0501", "Chuối tiêu", "Banana", "QUA_CHIN", 36, 89, 74.9m, 1.1m, 0.3m, 22.8m, 2.6m, 5, 0.3m, 0.001m, 9, 3, 0.03m),
        ("VDD-0502", "Cam", "Orange", "QUA_CHIN", 30, 47, 86.8m, 0.9m, 0.1m, 11.8m, 2.4m, 40, 0.1m, 0.000m, 53, 11, 0.09m),
        ("VDD-0701", "Thịt bò thăn", "Beef tenderloin", "THIT", 0, 158, 70.0m, 21.0m, 8.0m, 0, 0, 12, 2.5m, 0.060m, 0, 0, 0.09m),
        ("VDD-0702", "Ức gà", "Chicken breast", "THIT", 0, 120, 74.0m, 22.5m, 2.6m, 0, 0, 11, 0.4m, 0.045m, 0, 9, 0.07m),
        ("VDD-0801", "Cá thu", "Mackerel", "THUY_SAN", 35, 166, 69.0m, 18.2m, 10.3m, 0, 0, 20, 1.0m, 0.090m, 0, 40, 0.11m),
        ("VDD-0802", "Tôm sú", "Tiger prawn", "THUY_SAN", 40, 82, 79.0m, 18.4m, 1.0m, 0, 0, 79, 0.8m, 0.150m, 0, 15, 0.03m),
        ("VDD-0901", "Trứng vịt", "Duck egg", "TRUNG", 12, 184, 70.0m, 13.0m, 14.2m, 1.0m, 0, 71, 3.2m, 0.140m, 0, 360, 0.15m),
        ("VDD-1001", "Sữa chua không đường", "Plain yogurt", "SUA", 0, 61, 88.0m, 3.5m, 3.3m, 4.7m, 0, 121, 0.1m, 0.046m, 1, 27, 0.03m),
        ("VDD-1201", "Nước mắm", "Fish sauce", "GIA_VI", 0, 35, 71.0m, 5.1m, 0, 3.6m, 0, 43, 0.8m, 7.720m, 0, 0, 0.01m),
    ];

    public void Reset()
    {
        Groups = new[]
        {
            ("NGU_COC", "Ngũ cốc và sản phẩm chế biến", "Cereals and products", false), ("KHOAI_CU", "Khoai củ và sản phẩm chế biến", "Starchy roots and tubers", false),
            ("HAT_DAM_BEO", "Hạt, quả giàu đạm và béo", "Nuts, seeds and legumes", false), ("RAU", "Rau, quả, củ dùng làm rau", "Vegetables", false),
            ("QUA_CHIN", "Quả chín", "Fruits", false), ("THIT", "Thịt và sản phẩm chế biến", "Meat and products", true), ("THUY_SAN", "Thủy sản và sản phẩm chế biến", "Fish and seafood", true),
            ("TRUNG", "Trứng và sản phẩm chế biến", "Eggs and products", true), ("SUA", "Sữa và sản phẩm chế biến", "Milk and products", true), ("GIA_VI", "Gia vị, nước chấm", "Condiments and sauces", false),
        }.Select(g => new JsonObject { ["group_code"] = g.Item1, ["name_vi"] = g.Item2, ["name_en"] = g.Item3, ["parent_code"] = null, ["animal_source"] = g.Item4, ["updated_at"] = Seeded }).ToList();

        Foods = new ConcurrentDictionary<string, JsonObject>(FoodRows.ToDictionary(f => f.Item1, f => new JsonObject
        {
            ["food_code"] = f.Item1, ["name_vi"] = f.Item2, ["name_en"] = f.Item3, ["group_code"] = f.Item4, ["refuse_pct"] = f.Item5, ["version"] = 1, ["updated_at"] = Seeded,
            ["composition"] = new JsonArray(
                Comp("ENERC_KCAL", f.Item6, "kcal"), Comp("WATER", f.Item7, "g"), Comp("PROCNT", f.Item8, "g"), Comp("FAT", f.Item9, "g"), Comp("CHOCDF", f.Item10, "g"),
                Comp("FIBTG", f.Item11, "g"), Comp("CA", f.Item12, "mg"), Comp("FE", f.Item13, "mg"), Comp("NA", f.Item14, "g"), Comp("VITC", f.Item15, "mg"),
                Comp("VITA_RAE", f.Item16, "µg"), Comp("THIA", f.Item17, "mg")),
        }));

        Recommendations =
        [
            new JsonObject
            {
                ["rec_code"] = "VDD-NCDD-2016-NAM-VUA", ["name_vi"] = "Nhu cầu dinh dưỡng – nam 20-29, lao động vừa", ["name_en"] = "Requirements – men 20-29, moderate work",
                ["reference"] = "Nhu cầu dinh dưỡng khuyến nghị cho người Việt Nam (Bộ Y tế)", ["year"] = 2016, ["energy_kcal_day"] = 2570, ["meal_share_pct"] = 35,
                ["protein_pct"] = new JsonArray(13, 20), ["lipid_pct"] = new JsonArray(20, 25), ["glucid_pct"] = new JsonArray(55, 65), ["updated_at"] = Seeded,
                ["nutrients"] = new JsonArray(new JsonObject { ["nutrient_code"] = "CA", ["min"] = 800 }, new JsonObject { ["nutrient_code"] = "FE", ["min"] = 11 }, new JsonObject { ["nutrient_code"] = "NA", ["max"] = 2000 }),
            },
            new JsonObject
            {
                ["rec_code"] = "VDD-NCDD-2016-NU-VUA", ["name_vi"] = "Nhu cầu dinh dưỡng – nữ 20-29, lao động vừa", ["name_en"] = "Requirements – women 20-29, moderate work",
                ["reference"] = "Nhu cầu dinh dưỡng khuyến nghị cho người Việt Nam (Bộ Y tế)", ["year"] = 2016, ["energy_kcal_day"] = 2110, ["meal_share_pct"] = 35,
                ["protein_pct"] = new JsonArray(13, 20), ["lipid_pct"] = new JsonArray(20, 25), ["glucid_pct"] = new JsonArray(55, 65), ["updated_at"] = Seeded,
                ["nutrients"] = new JsonArray(new JsonObject { ["nutrient_code"] = "CA", ["min"] = 800 }, new JsonObject { ["nutrient_code"] = "FE", ["min"] = 26 }),
            },
        ];
        Interlocked.Exchange(ref ApiCalls, 0);
        FailNext = 0;
    }

    private static JsonObject Comp(string code, decimal value, string unit) => new() { ["nutrient_code"] = code, ["value"] = value, ["unit"] = unit };
}
