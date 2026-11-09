using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using BlueMeal.Api.Auth;
using BlueMeal.Api.Services;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BlueMeal.Api.Controllers.PublicApi;

public static class PublicScopes
{
    public const string MenuRead = "menu.read";
    public const string NutritionRead = "nutrition.read";
    public const string FoodRead = "food.read";
    public static readonly string[] All = [MenuRead, NutritionRead, FoodRead];
}

/// <summary>Marks a public API action (client credentials token with the given scope) — enforced by PermissionEnforcementMiddleware.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class PublicScopeAttribute(string scope) : Attribute
{
    public string Scope { get; } = scope;
}

/// <summary>Issues and validates OAuth2 client-credentials tokens for <c>/api/v1/public/*</c> (A-26). Separate audience and derived key.</summary>
public sealed class PublicTokenService(JwtOptions options, TimeProvider clock)
{
    public const string Audience = "bluemeal-public";
    public const int LifetimeSeconds = 3600;
    private readonly SymmetricSecurityKey _key = new(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Key), Encoding.UTF8.GetBytes("bluemeal-public-api")));

    public string Issue(ApiClient client, IReadOnlyCollection<string> scopes)
    {
        var now = clock.GetUtcNow();
        var token = new JwtSecurityToken(options.Issuer, Audience,
            [new Claim("client_id", client.ClientId), new Claim("scope", string.Join(' ', scopes)), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))],
            now.UtcDateTime, now.AddSeconds(LifetimeSeconds).UtcDateTime, new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? Validate(string token)
    {
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = options.Issuer, ValidateAudience = true, ValidAudience = Audience, ValidateIssuerSigningKey = true, IssuerSigningKey = _key,
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            }, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }
}

// ---------------------------------------------------------------------------------------------- DTOs (classes: JSON and XML)

[XmlRoot("token")]
public sealed class TokenResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("token_type")] public string TokenType { get; set; } = "Bearer";
    [System.Text.Json.Serialization.JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("scope")] public string Scope { get; set; } = "";
}

public sealed class PublicDish
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameEn { get; set; }
    public string Type { get; set; } = "";
}

[XmlType("menu")]
public sealed class PublicMenu
{
    public string Code { get; set; } = "";
    /// <summary>yyyy-MM-dd</summary>
    public string Date { get; set; } = "";
    public string Shift { get; set; } = "";
    public string? Customer { get; set; }
    public string? CustomerCode { get; set; }
    public string? Subject { get; set; }
    public int Servings { get; set; }
    public decimal? EnergyKcal { get; set; }
    [XmlArrayItem("dish")] public List<PublicDish> Dishes { get; set; } = [];
}

[XmlRoot("menus")]
public sealed class PublicMenuList
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    [XmlElement("menu")] public List<PublicMenu> Items { get; set; } = [];
}

public sealed class PublicNutrient
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Value { get; set; }
    public decimal? Target { get; set; }
    public decimal? PercentOfTarget { get; set; }
}

[XmlRoot("menuNutrition")]
public sealed class PublicMenuNutrition
{
    public string Code { get; set; } = "";
    public string Date { get; set; } = "";
    public decimal EnergyKcal { get; set; }
    public decimal? TargetEnergyKcal { get; set; }
    public decimal ProteinPct { get; set; }
    public decimal LipidPct { get; set; }
    public decimal GlucidPct { get; set; }
    [XmlArrayItem("nutrient")] public List<PublicNutrient> Nutrients { get; set; } = [];
}

public sealed class PublicFoodNutrient
{
    public string Code { get; set; } = "";
    public decimal Value { get; set; }
}

[XmlType("food")]
public sealed class PublicFood
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameEn { get; set; }
    public string Group { get; set; } = "";
    public decimal RefuseRate { get; set; }
    public string Source { get; set; } = "";
    /// <summary>ISO-8601 UTC.</summary>
    public string? UpdatedAt { get; set; }
    [XmlArrayItem("nutrient")] public List<PublicFoodNutrient> Per100g { get; set; } = [];
}

[XmlRoot("foods")]
public sealed class PublicFoodPage
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long TotalItems { get; set; }
    [XmlElement("food")] public List<PublicFood> Items { get; set; } = [];
}

[XmlType("row")]
public sealed class PublicSummaryRow
{
    public string Period { get; set; } = "";
    public string? Subject { get; set; }
    public string? Customer { get; set; }
    public int Menus { get; set; }
    public decimal EnergyKcal { get; set; }
    public decimal? TargetEnergyKcal { get; set; }
    public decimal? PercentOfTarget { get; set; }
    public int DaysMet { get; set; }
    public int DaysNotMet { get; set; }
}

[XmlRoot("nutritionSummary")]
public sealed class PublicSummary
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    [XmlElement("row")] public List<PublicSummaryRow> Rows { get; set; } = [];
}

// ---------------------------------------------------------------------------------------------- controller

/// <summary>
/// Public API for other systems (SPEC §7.10.6): OAuth2 client credentials (<c>POST /api/v1/public/token</c>) + scopes,
/// JSON by default and XML with <c>Accept: application/xml</c>; minimal HL7 FHIR R4 export (A-43).
/// </summary>
[Route("api/v1/public")]
[Produces("application/json", "application/xml")]
public sealed class PublicApiController(AppDbContext db, PublicTokenService tokens, AnalysisService analysis, Reports.ReportService reports, TimeProvider clock) : ControllerBase
{
    public sealed class TokenRequest
    {
        [FromForm(Name = "grant_type")] public string? GrantType { get; set; }
        [FromForm(Name = "client_id")] public string? ClientId { get; set; }
        [FromForm(Name = "client_secret")] public string? ClientSecret { get; set; }
        [FromForm(Name = "scope")] public string? Scope { get; set; }
    }

    /// <summary>OAuth2 token endpoint (client_credentials, form-encoded or HTTP Basic client authentication).</summary>
    [HttpPost("token"), AllowAnonymous, Consumes("application/x-www-form-urlencoded")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> Token([FromForm] TokenRequest req, CancellationToken ct)
    {
        var (id, secret) = (req.ClientId, req.ClientSecret);
        var basic = Request.Headers.Authorization.ToString();
        if (basic.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(basic[6..])).Split(':', 2);
            if (raw.Length == 2) (id, secret) = (Uri.UnescapeDataString(raw[0]), Uri.UnescapeDataString(raw[1]));
        }
        if (req.GrantType != "client_credentials") return OAuthError("unsupported_grant_type", 400);
        var client = await db.Set<ApiClient>().FirstOrDefaultAsync(c => c.ClientId == id && c.IsActive, ct);
        if (client is null || string.IsNullOrEmpty(secret) || new PasswordHasher<ApiClient>().VerifyHashedPassword(client, client.SecretHash, secret) == PasswordVerificationResult.Failed)
            return OAuthError("invalid_client", 401);
        var allowed = client.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var requested = string.IsNullOrWhiteSpace(req.Scope) ? allowed : req.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requested.Any(s => !allowed.Contains(s))) return OAuthError("invalid_scope", 400);
        client.LastUsedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Ok(new TokenResponse { AccessToken = tokens.Issue(client, requested), ExpiresIn = PublicTokenService.LifetimeSeconds, Scope = string.Join(' ', requested) });
    }

    private ObjectResult OAuthError(string error, int status) => StatusCode(status, new Dictionary<string, string> { ["error"] = error });

    /// <summary>Published menus of a period (dishes and energy only, no costs).</summary>
    [HttpGet("menus"), PublicScope(PublicScopes.MenuRead)]
    public async Task<ActionResult<PublicMenuList>> Menus([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string? customerCode, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62) throw new AppException("error.validation");
        var q = db.Set<Menu>().AsNoTracking().Where(m => m.Status == MenuStatus.Published && m.MenuDate >= from && m.MenuDate <= to);
        if (!string.IsNullOrWhiteSpace(customerCode)) q = q.Where(m => m.Customer!.Code == customerCode);
        var rows = (await q.OrderBy(m => m.MenuDate).ThenBy(m => m.MealShift!.SortOrder).Select(m => new
        {
            m.MenuDate,
            Menu = new PublicMenu
            {
            Code = m.Code, Shift = m.MealShift!.NameVi, Customer = m.Customer!.NameVi, CustomerCode = m.Customer.Code, Subject = m.SubjectGroup!.NameVi, Servings = m.Servings,
            EnergyKcal = m.EnergyKcal, Dishes = m.Items.OrderBy(i => i.SortOrder).Select(i => new PublicDish { Code = i.Dish!.Code, Name = i.Dish.NameVi, NameEn = i.Dish.NameEn, Type = i.Dish.DishType.ToString() }).ToList(),
            },
        }).Take(1000).ToListAsync(ct)).Select(x => { x.Menu.Date = x.MenuDate.ToString("yyyy-MM-dd"); return x.Menu; }).ToList();
        return new PublicMenuList { From = from.ToString("yyyy-MM-dd"), To = to.ToString("yyyy-MM-dd"), Items = rows };
    }

    [HttpGet("menus/{code}/nutrition"), PublicScope(PublicScopes.NutritionRead)]
    public async Task<ActionResult<PublicMenuNutrition>> MenuNutrition(string code, CancellationToken ct)
    {
        var m = await db.Set<Menu>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == code && (x.Status == MenuStatus.Published || x.Status == MenuStatus.Approved), ct) ?? throw new NotFoundException("menu");
        var a = await analysis.NutritionAsync(new AnalysisRequest("menu", null, m.Id, null, null, null, null, null, null), ct);
        return new PublicMenuNutrition
        {
            Code = m.Code, Date = m.MenuDate.ToString("yyyy-MM-dd"), EnergyKcal = a.EnergyKcal, TargetEnergyKcal = a.TargetEnergyKcal, ProteinPct = a.ProteinPct, LipidPct = a.LipidPct, GlucidPct = a.GlucidPct,
            Nutrients = a.Nutrients.Select(n => new PublicNutrient { Code = n.Code, Name = n.NameVi, Unit = n.Unit, Value = n.Value, Target = n.Target, PercentOfTarget = n.Pct }).ToList(),
        };
    }

    /// <summary>Food composition table (incremental with modifiedSince).</summary>
    [HttpGet("foods"), PublicScope(PublicScopes.FoodRead)]
    public async Task<ActionResult<PublicFoodPage>> Foods([FromQuery] DateTimeOffset? modifiedSince, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, PageQuery.MaxPageSize);
        var q = db.Set<Food>().AsNoTracking().Where(f => f.IsActive);
        if (modifiedSince is { } s) { var since = s.ToUniversalTime(); q = q.Where(f => (f.UpdatedAt ?? f.CreatedAt) > since); }
        var total = await q.LongCountAsync(ct);
        var items = (await q.OrderBy(f => f.Code).Skip((page - 1) * pageSize).Take(pageSize).Select(f => new
        {
            At = f.UpdatedAt ?? f.CreatedAt,
            Food = new PublicFood
            {
                Code = f.Code, Name = f.NameVi, NameEn = f.NameEn, Group = f.FoodGroup!.Code, RefuseRate = f.RefuseRate, Source = f.Source.ToString(),
                Per100g = f.Nutrients.OrderBy(n => n.Nutrient!.SortOrder).Select(n => new PublicFoodNutrient { Code = n.Nutrient!.Code, Value = n.ValuePer100gEdible }).ToList(),
            },
        }).ToListAsync(ct)).Select(x => { x.Food.UpdatedAt = x.At.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"); return x.Food; }).ToList();
        return new PublicFoodPage { Page = page, PageSize = pageSize, TotalItems = total, Items = items };
    }

    /// <summary>Aggregated nutrition compliance (RPT-06) by month × subject × customer.</summary>
    [HttpGet("nutrition/summary"), PublicScope(PublicScopes.NutritionRead)]
    public async Task<ActionResult<PublicSummary>> Summary([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string? mode, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 400) throw new AppException("error.validation");
        var r = await reports.RunTrustedAsync("RPT-06", new Reports.ReportFilter { From = from, To = to, Mode = mode ?? "month" }, ct);
        static decimal? D(object? v) => v is null ? null : Convert.ToDecimal(v);
        return new PublicSummary
        {
            From = from.ToString("yyyy-MM-dd"), To = to.ToString("yyyy-MM-dd"),
            Rows = r.Rows.Select(x => new PublicSummaryRow
            {
                Period = (string)x["period"]!, Subject = x["subject"] as string, Customer = x["customer"] as string, Menus = (int)(D(x["menus"]) ?? 0), EnergyKcal = D(x["energy"]) ?? 0,
                TargetEnergyKcal = D(x["target"]), PercentOfTarget = D(x["pct"]), DaysMet = (int)(D(x["daysMet"]) ?? 0), DaysNotMet = (int)(D(x["daysNotMet"]) ?? 0),
            }).ToList(),
        };
    }

    /// <summary>
    /// Minimal HL7 FHIR R4 export (A-43): a searchset Bundle of NutritionOrder resources, one per published menu
    /// (oralDiet with the dishes as free text and the energy as nutrient modifier). No patient-level data.
    /// </summary>
    [HttpGet("fhir/r4/NutritionOrder"), PublicScope(PublicScopes.MenuRead)]
    public async Task<IActionResult> FhirNutritionOrders([FromQuery] DateOnly date, CancellationToken ct)
    {
        var menus = (await Menus(date, date, null, ct)).Value!.Items;
        var bundle = new Dictionary<string, object?>
        {
            ["resourceType"] = "Bundle", ["type"] = "searchset", ["total"] = menus.Count, ["timestamp"] = clock.GetUtcNow().ToString("O"),
            ["entry"] = menus.Select(m => new Dictionary<string, object?>
            {
                ["fullUrl"] = $"urn:bluemeal:menu:{m.Code}",
                ["resource"] = new Dictionary<string, object?>
                {
                    ["resourceType"] = "NutritionOrder", ["id"] = m.Code, ["status"] = "active", ["intent"] = "plan", ["dateTime"] = m.Date,
                    ["identifier"] = new[] { new { system = "urn:bluemeal:menu", value = m.Code } },
                    ["note"] = new[] { new { text = $"{m.Shift}{(m.Customer is null ? "" : " — " + m.Customer)}; {m.Servings} suất" } },
                    ["oralDiet"] = new Dictionary<string, object?>
                    {
                        ["type"] = new[] { new { text = string.Join("; ", m.Dishes.Select(d => d.Name)) } },
                        ["nutrient"] = m.EnergyKcal is null ? Array.Empty<object>() : new object[]
                        {
                            new { modifier = new { coding = new[] { new { system = "http://snomed.info/sct", code = "33463005", display = "Energy" } }, text = "Energy" }, amount = new { value = Math.Round(m.EnergyKcal.Value, 0), unit = "kcal", system = "http://unitsofmeasure.org", code = "kcal" } },
                        },
                    },
                },
            }).ToList(),
        };
        return new ContentResult { Content = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = false }), ContentType = "application/fhir+json; charset=utf-8", StatusCode = 200 };
    }
}
