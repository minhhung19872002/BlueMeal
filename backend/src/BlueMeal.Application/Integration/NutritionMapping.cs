using System.Globalization;
using System.Text.Json;
using BlueMeal.Application.Common;

namespace BlueMeal.Application.Integration;

/// <summary>
/// Field mapping between the remote API and the canonical records (SPEC §7.10.3), stored as JSON in
/// <c>integration_endpoint.mapping_json</c>. Paths use dot notation ("data.items"). When the real VDD specification
/// is published only this JSON changes, not the code (A-25).
/// </summary>
public sealed class MappingConfig
{
    public Dictionary<string, string> Paths { get; set; } = new()
    {
        ["nutrients"] = "/api/v1/nutrients", ["foodGroups"] = "/api/v1/food-groups", ["foods"] = "/api/v1/foods",
        ["recommendations"] = "/api/v1/recommendations", ["push"] = "/api/v1/reports/aggregated", ["token"] = "/oauth/token",
    };
    /// <summary>Query string parameter names.</summary>
    public Dictionary<string, string> Query { get; set; } = new() { ["modifiedSince"] = "modified_since", ["page"] = "page", ["pageSize"] = "page_size" };
    /// <summary>Envelope: where the item array and the total count are.</summary>
    public string ItemsPath { get; set; } = "data";
    public string TotalPath { get; set; } = "total";

    public Dictionary<string, string> Nutrient { get; set; } = new() { ["code"] = "nutrient_code", ["nameVi"] = "name_vi", ["nameEn"] = "name_en", ["unit"] = "unit", ["group"] = "group" };
    public Dictionary<string, string> FoodGroup { get; set; } = new()
    { ["code"] = "group_code", ["nameVi"] = "name_vi", ["nameEn"] = "name_en", ["parentCode"] = "parent_code", ["isAnimalSource"] = "animal_source", ["modifiedAt"] = "updated_at" };
    public Dictionary<string, string> Food { get; set; } = new()
    {
        ["code"] = "food_code", ["nameVi"] = "name_vi", ["nameEn"] = "name_en", ["groupCode"] = "group_code", ["refuseRate"] = "refuse_pct", ["version"] = "version",
        ["modifiedAt"] = "updated_at", ["composition"] = "composition", ["nutrientCode"] = "nutrient_code", ["value"] = "value", ["unit"] = "unit",
    };
    public Dictionary<string, string> Recommendation { get; set; } = new()
    {
        ["code"] = "rec_code", ["nameVi"] = "name_vi", ["nameEn"] = "name_en", ["reference"] = "reference", ["referenceYear"] = "year", ["energyPerDayKcal"] = "energy_kcal_day",
        ["mealSharePercent"] = "meal_share_pct", ["proteinPct"] = "protein_pct", ["lipidPct"] = "lipid_pct", ["glucidPct"] = "glucid_pct", ["modifiedAt"] = "updated_at",
        ["nutrients"] = "nutrients", ["nutrientCode"] = "nutrient_code", ["min"] = "min", ["max"] = "max",
    };
    /// <summary>Remote nutrient code → local code (e.g. INFOODS tagnames).</summary>
    public Dictionary<string, string> NutrientCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENERC_KCAL"] = "ENERGY", ["WATER"] = "WATER", ["PROCNT"] = "PROTEIN", ["FAT"] = "LIPID", ["CHOCDF"] = "GLUCID", ["FIBTG"] = "FIBER",
        ["CA"] = "CA", ["FE"] = "FE", ["NA"] = "NA", ["VITC"] = "VIT_C", ["VITA_RAE"] = "VIT_A", ["THIA"] = "VIT_B1",
    };
    /// <summary>Remote food group code → local code (identity when absent).</summary>
    public Dictionary<string, string> FoodGroupCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Unit conversion factors "from>to" (value_local = value_remote × factor).</summary>
    public Dictionary<string, decimal> UnitFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mg>g"] = 0.001m, ["g>mg"] = 1000m, ["µg>mg"] = 0.001m, ["ug>mg"] = 0.001m, ["mg>µg"] = 1000m, ["mg>ug"] = 1000m, ["µg>g"] = 0.000001m, ["kJ>kcal"] = 0.239006m, ["kcal>kJ"] = 4.184m,
    };
    /// <summary>Prefix added to local codes of synchronized foods/groups when the remote code space overlaps (empty = none).</summary>
    public string CodePrefix { get; set; } = "";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static MappingConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MappingConfig();
        var m = JsonSerializer.Deserialize<MappingConfig>(json, Json) ?? new MappingConfig();
        // the deserializer creates ordinal dictionaries — codes and units are matched case-insensitively
        m.NutrientCodes = new Dictionary<string, string>(m.NutrientCodes, StringComparer.OrdinalIgnoreCase);
        m.FoodGroupCodes = new Dictionary<string, string>(m.FoodGroupCodes, StringComparer.OrdinalIgnoreCase);
        m.UnitFactors = new Dictionary<string, decimal>(m.UnitFactors, StringComparer.OrdinalIgnoreCase);
        return m;
    }

    public static string DefaultJson => JsonSerializer.Serialize(new MappingConfig(), Json);
}

/// <summary>Pure mapping functions from remote JSON to canonical records (unit tested without HTTP).</summary>
public static class NutritionMapper
{
    public static JsonElement? Get(JsonElement e, string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var cur = e;
        foreach (var part in path.Split('.'))
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(part, out var next)) return null;
            cur = next;
        }
        return cur.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : cur;
    }

    private static string? Str(JsonElement e, Dictionary<string, string> map, string key)
        => map.TryGetValue(key, out var p) && Get(e, p) is { } v ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText() : null;

    private static decimal? Dec(JsonElement? v)
        => v is not { } x ? null : x.ValueKind switch
        {
            JsonValueKind.Number => x.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(x.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };

    private static decimal? Dec(JsonElement e, Dictionary<string, string> map, string key) => map.TryGetValue(key, out var p) ? Dec(Get(e, p)) : null;

    private static DateTimeOffset? Date(JsonElement e, Dictionary<string, string> map, string key)
        => Str(e, map, key) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.ToUniversalTime() : null;

    private static bool Bool(JsonElement e, Dictionary<string, string> map, string key)
        => map.TryGetValue(key, out var p) && Get(e, p) is { } v && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b) || (v.ValueKind == JsonValueKind.Number && v.GetDecimal() != 0));

    private static string Required(string? value, string field) => string.IsNullOrWhiteSpace(value) ? throw new MappingException(field) : value.Trim();

    public static IEnumerable<JsonElement> Items(JsonElement root, MappingConfig m)
    {
        var arr = root.ValueKind == JsonValueKind.Array ? root : Get(root, m.ItemsPath);
        return arr is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().ToList() : [];
    }

    public static long? Total(JsonElement root, MappingConfig m) => Dec(Get(root, m.TotalPath)) is { } d ? (long)d : null;

    /// <summary>Converts a value between units using the configured factors (case-insensitive, identity when equal or unknown).</summary>
    public static decimal Convert(decimal value, string? from, string? to, MappingConfig m)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return value;
        return m.UnitFactors.TryGetValue($"{from}>{to}", out var f) ? value * f : throw new MappingException($"unit {from}>{to}");
    }

    public static string LocalNutrientCode(string remote, MappingConfig m) => m.NutrientCodes.TryGetValue(remote, out var l) ? l : remote.ToUpperInvariant();
    public static string LocalGroupCode(string remote, MappingConfig m) => m.CodePrefix + (m.FoodGroupCodes.TryGetValue(remote, out var l) ? l : remote);

    public static IReadOnlyList<ExtNutrient> Nutrients(JsonElement root, MappingConfig m)
        => Items(root, m).Select(e => new ExtNutrient(LocalNutrientCode(Required(Str(e, m.Nutrient, "code"), "nutrient.code"), m), Required(Str(e, m.Nutrient, "nameVi"), "nutrient.nameVi"),
            Str(e, m.Nutrient, "nameEn"), Str(e, m.Nutrient, "unit") ?? "g", Str(e, m.Nutrient, "group"))).ToList();

    public static IReadOnlyList<ExtFoodGroup> FoodGroups(JsonElement root, MappingConfig m)
        => Items(root, m).Select(e => new ExtFoodGroup(LocalGroupCode(Required(Str(e, m.FoodGroup, "code"), "foodGroup.code"), m), Required(Str(e, m.FoodGroup, "nameVi"), "foodGroup.nameVi"),
            Str(e, m.FoodGroup, "nameEn"), Str(e, m.FoodGroup, "parentCode") is { } p ? LocalGroupCode(p, m) : null, Bool(e, m.FoodGroup, "isAnimalSource"), Date(e, m.FoodGroup, "modifiedAt"))).ToList();

    /// <summary>Maps foods; composition values are converted to the local unit of each nutrient (<paramref name="localUnits"/>).</summary>
    public static PagedResult<ExtFood> Foods(JsonElement root, MappingConfig m, IReadOnlyDictionary<string, string> localUnits, int page, int pageSize)
    {
        var items = Items(root, m).Select(e =>
        {
            var nutrients = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (m.Food.TryGetValue("composition", out var cp) && Get(e, cp) is { ValueKind: JsonValueKind.Array } comp)
                foreach (var c in comp.EnumerateArray())
                {
                    var code = LocalNutrientCode(Required(Str(c, m.Food, "nutrientCode"), "food.composition.code"), m);
                    if (Dec(c, m.Food, "value") is not { } v) continue;
                    nutrients[code] = Math.Round(Convert(v, Str(c, m.Food, "unit"), localUnits.GetValueOrDefault(code), m), 4);
                }
            return new ExtFood(m.CodePrefix + Required(Str(e, m.Food, "code"), "food.code"), Required(Str(e, m.Food, "nameVi"), "food.nameVi"), Str(e, m.Food, "nameEn"),
                LocalGroupCode(Required(Str(e, m.Food, "groupCode"), "food.groupCode"), m), Dec(e, m.Food, "refuseRate") ?? 0, nutrients, Str(e, m.Food, "version"), Date(e, m.Food, "modifiedAt"));
        }).ToList();
        return new PagedResult<ExtFood>(items, page, pageSize, Total(root, m) ?? items.Count);
    }

    private static (decimal Min, decimal Max) Range(JsonElement e, Dictionary<string, string> map, string key, decimal dMin, decimal dMax)
    {
        if (!map.TryGetValue(key, out var p) || Get(e, p) is not { } v) return (dMin, dMax);
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2) return (Dec(v[0]) ?? dMin, Dec(v[1]) ?? dMax);
        if (v.ValueKind == JsonValueKind.Object) return (Dec(Get(v, "min")) ?? dMin, Dec(Get(v, "max")) ?? dMax);
        return (dMin, dMax);
    }

    public static IReadOnlyList<ExtRecommendation> Recommendations(JsonElement root, MappingConfig m)
        => Items(root, m).Select(e =>
        {
            var p = Range(e, m.Recommendation, "proteinPct", 13, 20);
            var l = Range(e, m.Recommendation, "lipidPct", 20, 25);
            var g = Range(e, m.Recommendation, "glucidPct", 55, 65);
            var nutrients = new List<ExtRecommendationNutrient>();
            if (m.Recommendation.TryGetValue("nutrients", out var np) && Get(e, np) is { ValueKind: JsonValueKind.Array } arr)
                nutrients.AddRange(arr.EnumerateArray().Select(n => new ExtRecommendationNutrient(LocalNutrientCode(Required(Str(n, m.Recommendation, "nutrientCode"), "rec.nutrient.code"), m),
                    Dec(n, m.Recommendation, "min"), Dec(n, m.Recommendation, "max"))));
            return new ExtRecommendation(Required(Str(e, m.Recommendation, "code"), "rec.code"), Required(Str(e, m.Recommendation, "nameVi"), "rec.nameVi"), Str(e, m.Recommendation, "nameEn"),
                Str(e, m.Recommendation, "reference"), Dec(e, m.Recommendation, "referenceYear") is { } y ? (int)y : null, Dec(e, m.Recommendation, "energyPerDayKcal") ?? throw new MappingException("rec.energy"),
                Dec(e, m.Recommendation, "mealSharePercent") ?? 35, p.Min, p.Max, l.Min, l.Max, g.Min, g.Max, nutrients, Date(e, m.Recommendation, "modifiedAt"));
        }).ToList();

    /// <summary>Stable hash of a canonical record, used to detect remote changes (sync conflicts).</summary>
    public static string Hash<T>(T record)
    {
        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        // dictionaries are serialized in insertion order — normalize by sorting keys of nested objects
        var normalized = Normalize(JsonDocument.Parse(json).RootElement);
        return System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)))[..32].ToLowerInvariant();
    }

    private static string Normalize(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", e.EnumerateObject().Where(p => p.Name != "modifiedAt").OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => $"\"{p.Name}\":{Normalize(p.Value)}")) + "}",
        JsonValueKind.Array => "[" + string.Join(",", e.EnumerateArray().Select(Normalize)) + "]",
        JsonValueKind.Number => e.GetDecimal().ToString("0.####", CultureInfo.InvariantCulture),
        _ => e.GetRawText(),
    };
}

/// <summary>Remote payload does not match the mapping (missing required field or unknown unit conversion).</summary>
public sealed class MappingException(string field) : Exception($"Mapping error: {field}")
{
    public string Field { get; } = field;
}
