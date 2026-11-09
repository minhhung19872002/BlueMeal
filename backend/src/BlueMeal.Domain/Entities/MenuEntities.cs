using BlueMeal.Domain.Common;
using BlueMeal.Domain.Enums;

namespace BlueMeal.Domain.Entities;

public class MealShift : CatalogEntity
{
    public TimeOnly? ServeFrom { get; set; }
    public TimeOnly? ServeTo { get; set; }
}

public class MealStructureTemplate : CatalogEntity
{
    public string? Description { get; set; }
    public ICollection<MealStructureSlot> Slots { get; set; } = new List<MealStructureSlot>();
}

public class MealStructureSlot : Entity
{
    public Guid TemplateId { get; set; }
    public DishType DishType { get; set; }
    public int Quantity { get; set; } = 1;
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }
}

public class MenuPlan : AuditableEntity
{
    public string Name { get; set; } = default!;
    public PlanPeriod Period { get; set; } = PlanPeriod.Week;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? SubjectGroupId { get; set; }
    public Guid? RecommendationId { get; set; }
    public Guid? StructureTemplateId { get; set; }
    /// <summary>Comma separated meal shift ids.</summary>
    public string? MealShiftIds { get; set; }
    public decimal? BudgetPerServing { get; set; }
    public int? ExpectedServingsPerDay { get; set; }
    public int NoRepeatDays { get; set; } = 5;
    public PlanStatus Status { get; set; } = PlanStatus.Draft;
    public Guid? OwnerUserId { get; set; }
    public string? Note { get; set; }
}

/// <summary>Menu of one day × one shift × one subject group (± customer).</summary>
public class Menu : AuditableEntity
{
    public string Code { get; set; } = default!;
    public Guid? PlanId { get; set; }
    public DateOnly MenuDate { get; set; }
    public Guid MealShiftId { get; set; }
    public MealShift? MealShift { get; set; }
    public Guid? SubjectGroupId { get; set; }
    public SubjectGroup? SubjectGroup { get; set; }
    public Guid? RecommendationId { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? StructureTemplateId { get; set; }
    public int Servings { get; set; }
    public decimal? BudgetPerServing { get; set; }
    public MenuCreationMode CreationMode { get; set; } = MenuCreationMode.Manual;
    public MenuStatus Status { get; set; } = MenuStatus.Draft;
    /// <summary>Current approval level reached (0 = none, 1, 2).</summary>
    public int ApprovalLevel { get; set; }
    /// <summary>JSON snapshot: nutrition, structure, cost, warnings — recomputed on every change.</summary>
    public string? Snapshot { get; set; }
    public decimal? NutritionScore { get; set; }
    public decimal? CostPerServing { get; set; }
    public decimal? EnergyKcal { get; set; }
    public int CriticalWarnings { get; set; }
    public string? CriticalOverrideReason { get; set; }
    public Guid? CriticalOverrideBy { get; set; }
    /// <summary>Short explanation (bilingual JSON) from the generator.</summary>
    public string? GeneratorExplanation { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? Note { get; set; }
    public ICollection<MenuItem> Items { get; set; } = new List<MenuItem>();
}

public class MenuItem : Entity
{
    public Guid MenuId { get; set; }
    public Guid DishId { get; set; }
    public Dish? Dish { get; set; }
    /// <summary>Recipe version locked at approval; null means "current recipe".</summary>
    public Guid? RecipeId { get; set; }
    public decimal ServingFactor { get; set; } = 1m;
    public DishType Slot { get; set; }
    public int SortOrder { get; set; }
}

[NoAudit]
public class MenuApproval : Entity
{
    public Guid MenuId { get; set; }
    public ApprovalAction Action { get; set; }
    public int Level { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Comment { get; set; }
    public MenuStatus FromStatus { get; set; }
    public MenuStatus ToStatus { get; set; }
}

public class MealOrder : AuditableEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateOnly OrderDate { get; set; }
    public Guid MealShiftId { get; set; }
    public Guid? SubjectGroupId { get; set; }
    public int RegisteredServings { get; set; }
    public int? ActualServings { get; set; }
    public Guid? MenuId { get; set; }
    public MealOrderStatus Status { get; set; } = MealOrderStatus.Draft;
    public string? Note { get; set; }
}

/// <summary>Persisted alert for the alert center (/alerts) — SPEC §7.6.</summary>
[NoAudit]
public class Alert : Entity
{
    /// <summary>Rule code: W-ENERGY-LOW, W-CERT-EXPIRY…</summary>
    public string Code { get; set; } = default!;
    public WarningLevel Level { get; set; }
    /// <summary>Menu, Recipe, Stock, Certificate, Sample…</summary>
    public string Category { get; set; } = default!;
    public string MessageVi { get; set; } = default!;
    public string MessageEn { get; set; } = default!;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Link { get; set; }
    /// <summary>De-duplication key: same open alert is not created twice.</summary>
    public string DedupeKey { get; set; } = default!;
    public AlertStatus Status { get; set; } = AlertStatus.Open;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CustomerId { get; set; }
}
