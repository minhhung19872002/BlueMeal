using BlueMeal.Application.Common;

namespace BlueMeal.Application.Integration;

// Canonical (source-independent) records produced by the mapping layer (SPEC §7.10). Codes are local BlueMeal codes.

public sealed record ExtFoodGroup(string Code, string NameVi, string? NameEn, string? ParentCode, bool IsAnimalSource, DateTimeOffset? ModifiedAt);

public sealed record ExtNutrient(string Code, string NameVi, string? NameEn, string Unit, string? Group);

/// <summary>Food with its composition per 100 g edible portion, values already converted to the local nutrient unit.</summary>
public sealed record ExtFood(string Code, string NameVi, string? NameEn, string GroupCode, decimal RefuseRate, IReadOnlyDictionary<string, decimal> Nutrients, string? Version, DateTimeOffset? ModifiedAt);

public sealed record ExtRecommendationNutrient(string Code, decimal? Min, decimal? Max);

public sealed record ExtRecommendation(string Code, string NameVi, string? NameEn, string? Reference, int? ReferenceYear, decimal EnergyPerDayKcal, decimal MealSharePercent,
    decimal ProteinPctMin, decimal ProteinPctMax, decimal LipidPctMin, decimal LipidPctMax, decimal GlucidPctMin, decimal GlucidPctMax,
    IReadOnlyList<ExtRecommendationNutrient> Nutrients, DateTimeOffset? ModifiedAt);

public sealed record NutritionSummaryRow(string Period, string? Subject, string? Customer, int Menus, int Servings, decimal EnergyKcal, decimal? TargetEnergyKcal, decimal ProteinPct, decimal LipidPct, decimal GlucidPct);

public sealed record NutritionSummaryPayload(string Organization, DateOnly From, DateOnly To, IReadOnlyList<NutritionSummaryRow> Rows);

public sealed record PushResult(bool Accepted, string? ReceiptId, string? Message);

/// <summary>Source of nutrition reference data (Viện Dinh dưỡng portal). The real API is not specified yet — ASSUMPTIONS A-25.</summary>
public interface INutritionDataProvider
{
    Task<IReadOnlyList<ExtFoodGroup>> GetFoodGroupsAsync(DateTimeOffset? modifiedSince, CancellationToken ct);
    Task<PagedResult<ExtFood>> GetFoodsAsync(DateTimeOffset? modifiedSince, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<ExtNutrient>> GetNutrientsAsync(CancellationToken ct);
    Task<IReadOnlyList<ExtRecommendation>> GetRecommendationsAsync(DateTimeOffset? modifiedSince, CancellationToken ct);
    Task<PushResult> PushAggregatedReportAsync(NutritionSummaryPayload payload, CancellationToken ct);
}
