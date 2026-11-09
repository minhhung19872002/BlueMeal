using BlueMeal.Domain.Enums;

namespace BlueMeal.Application.Settings;

/// <summary>Keys of rows in table app_setting. Each maps to a typed model below (SPEC §7.1.13).</summary>
public static class SettingKeys
{
    public const string PasswordPolicy = "security.password";
    public const string SessionPolicy = "security.session";
    public const string MfaPolicy = "security.mfa";
    public const string Warning = "nutrition.warning";
    public const string Inventory = "inventory";
    public const string Cost = "cost";
    public const string Smtp = "smtp";
    public const string Backup = "backup";
    public const string Sample = "food_safety.sample";
    public const string Generator = "menu.generator";
    public const string Approval = "menu.approval";
    public const string Audit = "audit";
    public const string Inspection = "food_safety.inspection";
    public const string SampleData = "sample_data";
    public const string Maintenance = "system.maintenance";
    public const string Swagger = "system.swagger";

    public static readonly Dictionary<string, Type> Types = new()
    {
        [PasswordPolicy] = typeof(PasswordPolicySettings), [SessionPolicy] = typeof(SessionPolicySettings), [MfaPolicy] = typeof(MfaPolicySettings),
        [Warning] = typeof(WarningSettings), [Inventory] = typeof(InventorySettings), [Cost] = typeof(CostSettings), [Smtp] = typeof(SmtpSettings),
        [Backup] = typeof(BackupSettings), [Sample] = typeof(SampleSettings), [Generator] = typeof(GeneratorSettings), [Approval] = typeof(ApprovalSettings),
        [Audit] = typeof(AuditSettings), [Inspection] = typeof(InspectionFormSettings), [SampleData] = typeof(SampleDataSettings),
        [Maintenance] = typeof(MaintenanceSettings), [Swagger] = typeof(SwaggerSettings),
    };
}

public class PasswordPolicySettings
{
    public int MinLength { get; set; } = 10;
    public bool RequireUpper { get; set; } = true;
    public bool RequireLower { get; set; } = true;
    public bool RequireDigit { get; set; } = true;
    public bool RequireSpecial { get; set; } = true;
    public int HistoryCount { get; set; } = 5;
    public int ExpiryDays { get; set; } = 90;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int CaptchaAfterFailures { get; set; } = 3;
}

public class SessionPolicySettings
{
    public int IdleTimeoutMinutes { get; set; } = 30;
    public int MaxConcurrentSessions { get; set; } = 3;
    public int RefreshTokenDays { get; set; } = 7;
    public int AccessTokenMinutes { get; set; } = 15;
}

public class MfaPolicySettings
{
    /// <summary>Role codes for which MFA is mandatory. SYS_ADMIN, MANAGER, MENU_APPROVER, ACCOUNTANT are always included.</summary>
    public List<string> RequiredRoles { get; set; } = ["SYS_ADMIN", "MANAGER", "MENU_APPROVER", "ACCOUNTANT"];
    public bool AllowEmailOtp { get; set; } = true;
}

/// <summary>Warning thresholds (SPEC §6.2). Percent values are relative to the meal target.</summary>
public class WarningSettings
{
    public decimal EnergyWarnLowPct { get; set; } = 90m;
    public decimal EnergyWarnHighPct { get; set; } = 110m;
    public decimal EnergyCriticalLowPct { get; set; } = 80m;
    public decimal EnergyCriticalHighPct { get; set; } = 120m;
    public decimal CostCriticalOverPct { get; set; } = 10m;
    public int RepeatDishDays { get; set; } = 5;
    public int CertExpiryWarnDays { get; set; } = 30;
    public int StockExpiryWarnDays { get; set; } = 3;
    public bool PlgEnabled { get; set; } = true;
    public bool AnimalProteinEnabled { get; set; } = true;
    public WarningLevel PlgLevel { get; set; } = WarningLevel.Warning;
    public WarningLevel AnimalProteinLevel { get; set; } = WarningLevel.Warning;
    public WarningLevel MissingDataLevel { get; set; } = WarningLevel.Warning;
    public WarningLevel AllergenLevel { get; set; } = WarningLevel.Warning;
}

public class InventorySettings
{
    public InventoryCostMethod CostMethod { get; set; } = InventoryCostMethod.MovingAverage;
    public bool AllowNegativeStock { get; set; }
    /// <summary>Default warehouse used for planned issues when the ingredient has none.</summary>
    public Guid? DefaultWarehouseId { get; set; }
}

public class OverheadItem
{
    public string Code { get; set; } = default!;
    public string NameVi { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public OverheadMode Mode { get; set; }
    public decimal Value { get; set; }
}

public class CostSettings
{
    public CostPriceMode PriceMode { get; set; } = CostPriceMode.WeightedAverage;
    public List<OverheadItem> Overheads { get; set; } =
    [
        new() { Code = "FUEL", NameVi = "Chất đốt", NameEn = "Fuel", Mode = OverheadMode.PercentOfFood, Value = 3m },
        new() { Code = "LABOR", NameVi = "Nhân công", NameEn = "Labor", Mode = OverheadMode.FixedPerServing, Value = 3000m },
        new() { Code = "DEPRECIATION", NameVi = "Khấu hao", NameEn = "Depreciation", Mode = OverheadMode.PercentOfFood, Value = 2m },
        new() { Code = "SUPPLIES", NameVi = "Vật tư tiêu hao", NameEn = "Consumables", Mode = OverheadMode.FixedPerServing, Value = 500m },
    ];
}

public class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? UserName { get; set; }
    /// <summary>Stored encrypted (see SettingsService).</summary>
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@bluemeal.local";
    public string FromName { get; set; } = "BlueMeal";
    public bool Enabled { get; set; }
}

public class BackupSettings
{
    /// <summary>Cron (container local time) for scheduled backups — default 01:00 daily.</summary>
    public string Cron { get; set; } = "0 1 * * *";
    public int KeepDaily { get; set; } = 7;
    public int KeepWeekly { get; set; } = 4;
    public int KeepMonthly { get; set; } = 6;
    /// <summary>Optional external mount (NAS) to mirror backups to.</summary>
    public string? MirrorPath { get; set; }
}

public class SampleSettings
{
    public int RetentionHours { get; set; } = 24;
    public decimal MinWeightG { get; set; } = 100m;
}

/// <summary>MenuGenerator weights (SPEC §6.4).</summary>
public class GeneratorSettings
{
    public decimal W1Energy { get; set; } = 10m;
    public decimal W2Plg { get; set; } = 4m;
    public decimal W3AnimalProtein { get; set; } = 2m;
    public decimal W4Micro { get; set; } = 1m;
    public decimal W5Cost { get; set; } = 8m;
    public decimal W6Repeat { get; set; } = 3m;
    public decimal W7CookingMethod { get; set; } = 1m;
    public int Candidates { get; set; } = 200;
    public int TopForLocalSearch { get; set; } = 10;
    public int MaxLocalSearchIterations { get; set; } = 500;
    public int MaxFriedPerMeal { get; set; } = 1;
}

public class ApprovalSettings
{
    /// <summary>1 or 2 levels (A-19).</summary>
    public int Levels { get; set; } = 1;
    public bool NotifyByEmail { get; set; } = true;
}

public class AuditSettings
{
    public int RetentionMonths { get; set; } = 12;
    public bool LogReads { get; set; }
}

public class InspectionCriterion
{
    public string Code { get; set; } = default!;
    public string LabelVi { get; set; } = default!;
    public string LabelEn { get; set; } = default!;
    /// <summary>bool | text | number</summary>
    public string Type { get; set; } = "bool";
}

/// <summary>Configurable 3-step inspection forms (A-23) — default follows MOH decision 1246/QĐ-BYT.</summary>
public class InspectionFormSettings
{
    public List<InspectionCriterion> Step1 { get; set; } =
    [
        new() { Code = "SENSORY", LabelVi = "Cảm quan (màu, mùi, trạng thái) đạt", LabelEn = "Sensory (color, smell, state) OK" },
        new() { Code = "PACKAGING", LabelVi = "Bao bì, nhãn mác nguyên vẹn", LabelEn = "Packaging and labels intact" },
        new() { Code = "ORIGIN", LabelVi = "Có giấy tờ nguồn gốc, xuất xứ", LabelEn = "Origin documents available" },
        new() { Code = "EXPIRY", LabelVi = "Còn hạn sử dụng", LabelEn = "Within expiry date" },
        new() { Code = "TEMP", LabelVi = "Nhiệt độ khi nhận (°C)", LabelEn = "Receiving temperature (°C)", Type = "number" },
    ];
    public List<InspectionCriterion> Step2 { get; set; } =
    [
        new() { Code = "KITCHEN_HYGIENE", LabelVi = "Vệ sinh khu vực chế biến", LabelEn = "Processing area hygiene" },
        new() { Code = "STAFF_HYGIENE", LabelVi = "Vệ sinh cá nhân người chế biến", LabelEn = "Food handler hygiene" },
        new() { Code = "UTENSILS", LabelVi = "Dụng cụ sống/chín riêng biệt", LabelEn = "Separate raw/cooked utensils" },
        new() { Code = "SENSORY_DISH", LabelVi = "Cảm quan món ăn sau chế biến", LabelEn = "Dish sensory after cooking" },
    ];
    public List<InspectionCriterion> Step3 { get; set; } =
    [
        new() { Code = "SERVE_TEMP", LabelVi = "Nhiệt độ món khi chia (°C)", LabelEn = "Serving temperature (°C)", Type = "number" },
        new() { Code = "SENSORY_SERVE", LabelVi = "Cảm quan trước khi ăn", LabelEn = "Sensory before eating" },
        new() { Code = "SERVE_UTENSILS", LabelVi = "Dụng cụ chia, chứa đựng sạch", LabelEn = "Clean serving utensils" },
        new() { Code = "COVERED", LabelVi = "Thức ăn được che đậy", LabelEn = "Food covered" },
    ];
}

/// <summary>Ids of seeded sample data, for quick purge (ASSUMPTIONS A-14).</summary>
public class SampleDataSettings
{
    public bool Loaded { get; set; }
    public Dictionary<string, List<Guid>> Ids { get; set; } = new();
}

public class MaintenanceSettings
{
    public bool Enabled { get; set; }
    public string? MessageVi { get; set; }
    public string? MessageEn { get; set; }
}

public class SwaggerSettings
{
    /// <summary>Allow Swagger UI in production (admin toggle) — SPEC §2.</summary>
    public bool EnabledInProduction { get; set; }
}
