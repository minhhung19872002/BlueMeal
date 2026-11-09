using BlueMeal.Domain.Common;
using BlueMeal.Domain.Enums;

namespace BlueMeal.Domain.Entities;

public class Warehouse : CatalogEntity
{
    public WarehouseType Type { get; set; } = WarehouseType.Dry;
    public string? Location { get; set; }
    public Guid? KeeperEmployeeId { get; set; }
    public decimal? TempMin { get; set; }
    public decimal? TempMax { get; set; }
    public string? Note { get; set; }
}

public class FoodGroup : CatalogEntity
{
    public Guid? ParentId { get; set; }
    public FoodGroup? Parent { get; set; }
    /// <summary>Used to compute animal protein ratio (SPEC §5.3).</summary>
    public bool IsAnimalSource { get; set; }
}

public class Nutrient : CatalogEntity
{
    /// <summary>Display unit: kcal, g, mg, µg.</summary>
    public string Unit { get; set; } = "g";
    public NutrientGroup Group { get; set; }
    public bool IsCore { get; set; }
    /// <summary>Display decimals (energy 0, macro 1, micro 2 — SPEC §6.1).</summary>
    public int Decimals { get; set; } = 1;
}

/// <summary>Food composition table entry (bảng thành phần thực phẩm).</summary>
public class Food : CatalogEntity
{
    public Guid FoodGroupId { get; set; }
    public FoodGroup? FoodGroup { get; set; }
    /// <summary>Refuse rate % (tỷ lệ thải bỏ) — 0..100.</summary>
    public decimal RefuseRate { get; set; }
    /// <summary>Overrides group flag when not null.</summary>
    public bool? IsAnimalSourceOverride { get; set; }
    public DataSource Source { get; set; } = DataSource.Manual;
    public string? SourceVersion { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    /// <summary>Hash of last remote payload — used for conflict detection on sync.</summary>
    public string? RemoteHash { get; set; }
    /// <summary>True when a user edited a VDD_API-sourced record locally.</summary>
    public bool LocallyModified { get; set; }
    /// <summary>API-sourced data is locked by default (SPEC §7.3.3).</summary>
    public bool IsLocked { get; set; }
    public string? Note { get; set; }
    public ICollection<FoodNutrient> Nutrients { get; set; } = new List<FoodNutrient>();
}

public class FoodNutrient : Entity
{
    public Guid FoodId { get; set; }
    public Guid NutrientId { get; set; }
    public Nutrient? Nutrient { get; set; }
    public decimal ValuePer100gEdible { get; set; }
}

/// <summary>Food safety / quality spec per food or ingredient (SPEC §5.3).</summary>
public class FoodSafetySpec : AuditableEntity
{
    public Guid? FoodId { get; set; }
    public Guid? IngredientId { get; set; }
    public string? SensoryColor { get; set; }
    public string? SensorySmell { get; set; }
    public string? SensoryState { get; set; }
    public decimal? StorageTempMin { get; set; }
    public decimal? StorageTempMax { get; set; }
    public int? MaxShelfDaysAfterReceipt { get; set; }
    public string? RequiredDocuments { get; set; }
    public string? Allergens { get; set; }
    public string? Note { get; set; }
}

/// <summary>Purchased item (nguyên liệu) mapped to a food for nutrition computation.</summary>
public class Ingredient : CatalogEntity
{
    public Guid? FoodId { get; set; }
    public Food? Food { get; set; }
    public Guid? PurchaseUnitId { get; set; }
    public UnitOfMeasure? PurchaseUnit { get; set; }
    /// <summary>Grams per one purchase unit (quy đổi ra gram).</summary>
    public decimal GramsPerUnit { get; set; } = 1000m;
    public Guid? DefaultWarehouseId { get; set; }
    /// <summary>Minimum stock in purchase units.</summary>
    public decimal MinStock { get; set; }
    public int? ShelfLifeDays { get; set; }
    public string? StorageCondition { get; set; }
    /// <summary>Current reference price per purchase unit (VND).</summary>
    public decimal? ReferencePrice { get; set; }
    public Guid? PreferredPartnerId { get; set; }
    /// <summary>Allergen codes, comma separated.</summary>
    public string? Allergens { get; set; }
    public string? Barcode { get; set; }
    public string? Note { get; set; }
    public override IEnumerable<string?> ExtraSearchFields() => [Barcode];
}

public class IngredientPriceHistory : Entity
{
    public Guid IngredientId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal UnitPrice { get; set; }
    public PriceSource Source { get; set; }
    public Guid? SourceDocId { get; set; }
    public Guid? PartnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public class Dish : CatalogEntity
{
    public DishType DishType { get; set; }
    public CookingMethod CookingMethod { get; set; }
    public decimal? ServingWeightG { get; set; }
    public Guid? ImageFileId { get; set; }
    /// <summary>Comma separated tags.</summary>
    public string? Tags { get; set; }
    public string? Allergens { get; set; }
    public string? Description { get; set; }
    public ICollection<Recipe> Recipes { get; set; } = new List<Recipe>();
    public override IEnumerable<string?> ExtraSearchFields() => [Tags];
}

/// <summary>Versioned recipe (công thức cấu tạo món ăn).</summary>
public class Recipe : AuditableEntity
{
    public Guid DishId { get; set; }
    public Dish? Dish { get; set; }
    public int VersionNo { get; set; } = 1;
    public bool IsCurrent { get; set; }
    public decimal BaseServings { get; set; } = 1m;
    public string? Instructions { get; set; }
    public int? PrepTimeMinutes { get; set; }
    /// <summary>Cooking weight loss % (hao hụt khi chế biến) — affects cooked weight only (A-16).</summary>
    public decimal CookingLossRate { get; set; }
    public Guid? AuthorUserId { get; set; }
    public RecipeStatus Status { get; set; } = RecipeStatus.Draft;
    public string? Note { get; set; }
    public ICollection<RecipeItem> Items { get; set; } = new List<RecipeItem>();
}

public class RecipeItem : Entity
{
    public Guid RecipeId { get; set; }
    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    /// <summary>Gross (raw, as purchased) weight in grams for BaseServings.</summary>
    public decimal GrossG { get; set; }
    /// <summary>Optional override for net (edible) weight.</summary>
    public decimal? NetGOverride { get; set; }
    public string? PrepNote { get; set; }
    public int SortOrder { get; set; }
}

public class NutrientRetentionFactor : Entity
{
    public Guid NutrientId { get; set; }
    public CookingMethod CookingMethod { get; set; }
    public decimal Factor { get; set; } = 1m;
    public string? Source { get; set; }
}

/// <summary>Nutrition recommendation per subject group (danh mục khuyến nghị).</summary>
public class Recommendation : CatalogEntity
{
    public Guid? SubjectGroupId { get; set; }
    public SubjectGroup? SubjectGroup { get; set; }
    public string? Reference { get; set; }
    public int? ReferenceYear { get; set; }
    public decimal EnergyPerDayKcal { get; set; }
    /// <summary>Shift-meal share of daily need, %.</summary>
    public decimal MealSharePercent { get; set; } = 35m;
    public decimal ProteinPctMin { get; set; } = 13m;
    public decimal ProteinPctMax { get; set; } = 20m;
    public decimal LipidPctMin { get; set; } = 20m;
    public decimal LipidPctMax { get; set; } = 25m;
    public decimal GlucidPctMin { get; set; } = 55m;
    public decimal GlucidPctMax { get; set; } = 65m;
    public decimal AnimalProteinPctMin { get; set; } = 30m;
    public decimal AnimalProteinPctMax { get; set; } = 50m;
    public decimal? BudgetPerServing { get; set; }
    public string? Note { get; set; }
    public ICollection<RecommendationNutrient> Nutrients { get; set; } = new List<RecommendationNutrient>();
}

public class RecommendationNutrient : Entity
{
    public Guid RecommendationId { get; set; }
    public Guid NutrientId { get; set; }
    public Nutrient? Nutrient { get; set; }
    public decimal? MinPerMeal { get; set; }
    public decimal? MaxPerMeal { get; set; }
    public WarningLevel Level { get; set; } = WarningLevel.Warning;
}

public class ReportGroup : CatalogEntity
{
    public string? Description { get; set; }
    /// <summary>Optional permission required to see reports in this group (in addition to report.view).</summary>
    public string? RequiredPermission { get; set; }
    public ICollection<ReportDefinition> Reports { get; set; } = new List<ReportDefinition>();
}

/// <summary>A report available in the system (RPT-01…RPT-16), assignable to a report group.</summary>
public class ReportDefinition : Entity
{
    public string Code { get; set; } = default!;
    public string NameVi { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public Guid? ReportGroupId { get; set; }
    public ReportGroup? ReportGroup { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
