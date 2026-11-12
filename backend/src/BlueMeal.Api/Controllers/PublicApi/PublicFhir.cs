using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlueMeal.Api.Services;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.PublicApi;

/// <summary>
/// HL7 FHIR R4 (4.0.1) read-only server for exchanging menu nutrition data with HIS/HSBA (Thông tư 13/2025/TT-BYT orients health
/// data exchange to FHIR R4). Population level only: BlueMeal has no patient data, so <c>NutritionOrder.patient</c> carries the
/// standard data-absent-reason extension. Same OAuth2 client credentials tokens and scopes as the rest of the public API.
/// <list type="bullet">
/// <item><c>GET metadata</c> — CapabilityStatement (no token, as FHIR clients discover the server before authenticating)</item>
/// <item><c>GET NutritionOrder?date=</c>, <c>GET NutritionOrder/{id}</c> — published menus as oral diet plans (scope menu.read)</item>
/// <item><c>GET Observation?date=</c>, <c>GET Observation/{id}</c> — nutrient content per serving of each menu (scope nutrition.read)</item>
/// </list>
/// Errors are returned as OperationOutcome.
/// </summary>
[Route("api/v1/public/fhir/r4")]
public sealed partial class PublicFhirController(AppDbContext db, AnalysisService analysis, TimeProvider clock) : ControllerBase
{
    public const string FhirVersion = "4.0.1";
    public const string ContentType = "application/fhir+json; charset=utf-8";
    private const int MaxSearchResults = 200;

    /// <summary>Local code systems (no national code system for menu nutrients exists yet; VN Core IG has no nutrition profiles).</summary>
    public const string MenuSystem = "urn:bluemeal:menu", NutrientSystem = "urn:bluemeal:nutrient", ObservationCodeSystem = "urn:bluemeal:observation";

    private const string DataAbsentReason = "http://hl7.org/fhir/StructureDefinition/data-absent-reason";
    private const string Ucum = "http://unitsofmeasure.org";

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/v1/public/fhir/r4";

    // ------------------------------------------------------------------ capability

    [HttpGet("metadata"), AllowAnonymous]
    public IActionResult Metadata()
    {
        var version = typeof(PublicFhirController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
        JsonObject Resource(string type, string scope, string profile, string dateDoc) => new()
        {
            ["type"] = type,
            ["profile"] = profile,
            ["documentation"] = $"OAuth2 scope: {scope}",
            ["interaction"] = new JsonArray(new JsonObject { ["code"] = "read" }, new JsonObject { ["code"] = "search-type" }),
            ["searchParam"] = new JsonArray(new JsonObject
            {
                ["name"] = "date", ["definition"] = $"http://hl7.org/fhir/SearchParameter/clinical-date", ["type"] = "date", ["documentation"] = dateDoc,
            }),
        };
        var cs = new JsonObject
        {
            ["resourceType"] = "CapabilityStatement",
            ["status"] = "active",
            ["date"] = clock.GetUtcNow().ToString("yyyy-MM-dd"),
            ["publisher"] = "BlueMeal",
            ["kind"] = "instance",
            ["software"] = new JsonObject { ["name"] = "BlueMeal", ["version"] = version },
            ["implementation"] = new JsonObject { ["description"] = "BlueMeal — menu nutrition data (population level, no patient data)", ["url"] = BaseUrl },
            ["fhirVersion"] = FhirVersion,
            ["format"] = new JsonArray("json"),
            ["rest"] = new JsonArray(new JsonObject
            {
                ["mode"] = "server",
                ["security"] = new JsonObject
                {
                    ["service"] = new JsonArray(new JsonObject
                    {
                        ["coding"] = new JsonArray(new JsonObject { ["system"] = "http://terminology.hl7.org/CodeSystem/restful-security-service", ["code"] = "OAuth" }),
                    }),
                    ["description"] = "OAuth2 client credentials: POST /api/v1/public/token, then send the access token as Authorization: Bearer.",
                },
                ["resource"] = new JsonArray(
                    Resource("NutritionOrder", PublicScopes.MenuRead, "http://hl7.org/fhir/StructureDefinition/NutritionOrder", "Menu date (yyyy-MM-dd), required"),
                    Resource("Observation", PublicScopes.NutritionRead, "http://hl7.org/fhir/StructureDefinition/Observation", "Menu date (yyyy-MM-dd), required")),
            }),
        };
        return Fhir(cs);
    }

    // ------------------------------------------------------------------ NutritionOrder

    [HttpGet("NutritionOrder"), PublicScope(PublicScopes.MenuRead)]
    public async Task<IActionResult> SearchNutritionOrders([FromQuery] string? date, CancellationToken ct)
    {
        if (!TryDate(date, out var d)) return MissingDate();
        var menus = await LoadAsync(m => m.MenuDate == d, ct);
        return Fhir(SearchSet(menus.Select(m => ("NutritionOrder", m.Id, NutritionOrder(m)))));
    }

    [HttpGet("NutritionOrder/{id}"), PublicScope(PublicScopes.MenuRead)]
    public async Task<IActionResult> ReadNutritionOrder(string id, CancellationToken ct)
    {
        var m = await FindAsync(id, ct);
        return m is null ? NotFoundOutcome("NutritionOrder", id) : Fhir(NutritionOrder(m));
    }

    private static JsonObject NutritionOrder(MenuRow m)
    {
        var nutrients = new JsonArray();
        if (m.EnergyKcal is { } kcal) nutrients.Add(new JsonObject { ["modifier"] = NutrientCode("ENERGY", "Năng lượng", "Energy"), ["amount"] = Quantity(Math.Round(kcal, 0), "kcal") });
        return new JsonObject
        {
            ["resourceType"] = "NutritionOrder",
            ["id"] = m.Id,
            ["text"] = Narrative($"{m.Code} — {m.Date:dd/MM/yyyy}, {m.Shift}, {Population(m)}: {string.Join("; ", m.Dishes)}"),
            ["identifier"] = new JsonArray(new JsonObject { ["system"] = MenuSystem, ["value"] = m.Code }),
            ["status"] = "active",
            ["intent"] = "plan",
            // R4 requires a patient: menus are planned for a population (customer × subject group), not for a person
            ["patient"] = new JsonObject { ["extension"] = new JsonArray(new JsonObject { ["url"] = DataAbsentReason, ["valueCode"] = "not-applicable" }), ["display"] = Population(m) },
            ["dateTime"] = m.Date.ToString("yyyy-MM-dd"),
            ["oralDiet"] = new JsonObject
            {
                ["type"] = new JsonArray(new JsonObject { ["text"] = string.Join("; ", m.Dishes) }),
                ["schedule"] = new JsonArray(new JsonObject { ["event"] = new JsonArray(m.Date.ToString("yyyy-MM-dd")) }),
                ["nutrient"] = nutrients,
                ["instruction"] = $"{m.Shift}; {m.Servings} suất",
            },
            ["note"] = new JsonArray(new JsonObject { ["text"] = $"{m.Shift} — {Population(m)}; {m.Servings} suất" }),
        };
    }

    // ------------------------------------------------------------------ Observation (nutrient content)

    [HttpGet("Observation"), PublicScope(PublicScopes.NutritionRead)]
    public async Task<IActionResult> SearchObservations([FromQuery] string? date, CancellationToken ct)
    {
        if (!TryDate(date, out var d)) return MissingDate();
        var menus = await LoadAsync(m => m.MenuDate == d, ct);
        var entries = new List<(string, string, JsonObject)>();
        foreach (var m in menus) entries.Add(("Observation", ObservationId(m.Id), await ObservationAsync(m, ct)));
        return Fhir(SearchSet(entries));
    }

    [HttpGet("Observation/{id}"), PublicScope(PublicScopes.NutritionRead)]
    public async Task<IActionResult> ReadObservation(string id, CancellationToken ct)
    {
        var m = id.EndsWith(ObservationSuffix, StringComparison.Ordinal) ? await FindAsync(id[..^ObservationSuffix.Length], ct) : null;
        return m is null ? NotFoundOutcome("Observation", id) : Fhir(await ObservationAsync(m, ct));
    }

    private const string ObservationSuffix = "-nutrition";
    private static string ObservationId(string menuId) => menuId + ObservationSuffix;

    private async Task<JsonObject> ObservationAsync(MenuRow m, CancellationToken ct)
    {
        var a = await analysis.NutritionAsync(new AnalysisRequest("menu", null, m.Key, null, null, null, null, null, null), ct);
        var components = new JsonArray();
        foreach (var n in a.Nutrients)
            components.Add(new JsonObject { ["code"] = NutrientCode(n.Code, n.NameVi, n.NameEn), ["valueQuantity"] = Quantity(n.Value, n.Unit) });
        return new JsonObject
        {
            ["resourceType"] = "Observation",
            ["id"] = ObservationId(m.Id),
            ["text"] = Narrative($"{m.Code} — {m.Date:dd/MM/yyyy}: " + string.Join(", ", a.Nutrients.Select(n => $"{n.NameVi} {n.Value} {n.Unit}"))),
            ["identifier"] = new JsonArray(new JsonObject { ["system"] = MenuSystem, ["value"] = m.Code + ObservationSuffix }),
            ["status"] = "final",
            ["code"] = new JsonObject
            {
                ["coding"] = new JsonArray(new JsonObject { ["system"] = ObservationCodeSystem, ["code"] = "menu-nutrient-content", ["display"] = "Menu nutrient content per serving" }),
                ["text"] = "Giá trị dinh dưỡng một suất ăn theo thực đơn",
            },
            ["focus"] = new JsonArray(new JsonObject { ["reference"] = $"NutritionOrder/{m.Id}", ["display"] = m.Code }),
            ["effectiveDateTime"] = m.Date.ToString("yyyy-MM-dd"),
            ["note"] = new JsonArray(new JsonObject
            {
                ["text"] = $"P/L/G: {a.ProteinPct}% / {a.LipidPct}% / {a.GlucidPct}% năng lượng" + (a.TargetEnergyKcal is { } t ? $"; nhu cầu khuyến nghị {t} kcal ({a.EnergyPct}%)" : ""),
            }),
            ["component"] = components,
        };
    }

    // ------------------------------------------------------------------ data

    private sealed record MenuRow(Guid Key, string Id, string Code, DateOnly Date, string Shift, string? Customer, string? Subject, int Servings, decimal? EnergyKcal, List<string> Dishes);

    private async Task<List<MenuRow>> LoadAsync(System.Linq.Expressions.Expression<Func<Menu, bool>> filter, CancellationToken ct)
    {
        var rows = await db.Set<Menu>().AsNoTracking().Where(m => m.Status == MenuStatus.Published).Where(filter)
            .OrderBy(m => m.MenuDate).ThenBy(m => m.MealShift!.SortOrder).ThenBy(m => m.Code)
            .Select(m => new
            {
                m.Id, m.Code, m.MenuDate, Shift = m.MealShift!.NameVi, Customer = m.Customer!.NameVi, Subject = m.SubjectGroup!.NameVi, m.Servings, m.EnergyKcal,
                Dishes = m.Items.OrderBy(i => i.SortOrder).Select(i => i.Dish!.NameVi).ToList(),
            })
            .Take(MaxSearchResults).ToListAsync(ct);
        return rows.Select(m => new MenuRow(m.Id, FhirId(m.Code), m.Code, m.MenuDate, m.Shift, m.Customer, m.Subject, m.Servings, m.EnergyKcal, m.Dishes)).ToList();
    }

    /// <summary>Resource id = menu code (document numbers "TD…" are already valid FHIR ids).</summary>
    private async Task<MenuRow?> FindAsync(string id, CancellationToken ct) => (await LoadAsync(m => m.Code == id, ct)).FirstOrDefault();

    /// <summary>FHIR ids are [A-Za-z0-9\-\.]{1,64}.</summary>
    public static string FhirId(string code)
    {
        var id = InvalidIdChars().Replace(code, "-");
        return id.Length > 64 - ObservationSuffix.Length ? id[..(64 - ObservationSuffix.Length)] : id;
    }

    [GeneratedRegex(@"[^A-Za-z0-9\-\.]")]
    private static partial Regex InvalidIdChars();

    // ------------------------------------------------------------------ building blocks

    private static string Population(MenuRow m) => string.Join(" — ", new[] { m.Customer, m.Subject }.Where(s => !string.IsNullOrEmpty(s))) is { Length: > 0 } p ? p : "Population";

    /// <summary>Human-readable summary (dom-6); XHTML-escaped.</summary>
    private static JsonObject Narrative(string text) => new()
    {
        ["status"] = "generated",
        ["div"] = $"<div xmlns=\"http://www.w3.org/1999/xhtml\">{System.Security.SecurityElement.Escape(text)}</div>",
    };

    private static JsonObject NutrientCode(string code, string nameVi, string? nameEn) => new()
    {
        ["coding"] = new JsonArray(new JsonObject { ["system"] = NutrientSystem, ["code"] = code, ["display"] = nameEn ?? nameVi }),
        ["text"] = nameVi,
    };

    /// <summary>Quantity with its UCUM code when the unit maps to one (kcal, g, mg, µg, IU, %).</summary>
    private static JsonObject Quantity(decimal value, string unit)
    {
        var q = new JsonObject { ["value"] = value, ["unit"] = unit };
        if (UcumCode(unit) is { } code) { q["system"] = Ucum; q["code"] = code; }
        return q;
    }

    public static string? UcumCode(string unit) => unit.Trim() switch
    {
        "kcal" => "kcal",
        "kJ" => "kJ",
        "g" => "g",
        "mg" => "mg",
        "µg" or "μg" or "mcg" or "ug" => "ug",
        "IU" or "UI" => "[IU]",
        "%" => "%",
        _ => null,
    };

    private JsonObject SearchSet(IEnumerable<(string Type, string Id, JsonObject Resource)> resources)
    {
        var entries = new JsonArray();
        foreach (var (type, id, resource) in resources)
            entries.Add(new JsonObject { ["fullUrl"] = $"{BaseUrl}/{type}/{id}", ["resource"] = resource, ["search"] = new JsonObject { ["mode"] = "match" } });
        return new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["type"] = "searchset",
            ["timestamp"] = clock.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"),
            ["total"] = entries.Count,
            ["link"] = new JsonArray(new JsonObject { ["relation"] = "self", ["url"] = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}{Request.QueryString}" }),
            ["entry"] = entries,
        };
    }

    private static bool TryDate(string? s, out DateOnly d)
        => DateOnly.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d);

    private ContentResult MissingDate() => Outcome(400, "required", "Search parameter 'date' (yyyy-MM-dd) is required.");

    private ContentResult NotFoundOutcome(string type, string id) => Outcome(404, "not-found", $"{type}/{id} not found.");

    private static ContentResult Outcome(int status, string code, string diagnostics) => new()
    {
        StatusCode = status,
        ContentType = ContentType,
        Content = new JsonObject
        {
            ["resourceType"] = "OperationOutcome",
            ["issue"] = new JsonArray(new JsonObject { ["severity"] = "error", ["code"] = code, ["diagnostics"] = diagnostics }),
        }.ToJsonString(),
    };

    private static ContentResult Fhir(JsonObject resource) => new() { StatusCode = 200, ContentType = ContentType, Content = resource.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) };
}
