namespace BlueMeal.Domain.Enums;

public enum Gender { Common = 0, Male = 1, Female = 2 }

public enum LaborIntensity { Light = 1, Moderate = 2, Heavy = 3, VeryHeavy = 4 }

public enum UomType { Mass = 1, Volume = 2, Count = 3, Energy = 4 }

public enum WarehouseType { Dry = 1, Chilled = 2, Frozen = 3, Other = 9 }

public enum NutrientGroup { Energy = 1, Macro = 2, Mineral = 3, Vitamin = 4, Other = 9 }

public enum DataSource { Manual = 1, Import = 2, VddApi = 3 }

public enum DishType { MainProtein = 1, StirFry = 2, Soup = 3, Vegetable = 4, Staple = 5, Dessert = 6, Drink = 7, Other = 9 }

public enum CookingMethod { None = 0, Boil = 1, Steam = 2, StirFry = 3, DeepFry = 4, Braise = 5, Grill = 6, Simmer = 7, Raw = 8, Bake = 9, Other = 99 }

public enum RecipeStatus { Draft = 1, Active = 2, Archived = 3 }

public enum WarningLevel { Info = 1, Warning = 2, Critical = 3 }

public enum PlanPeriod { Week = 1, Month = 2, Custom = 3 }

public enum PlanStatus { Draft = 1, InProgress = 2, Completed = 3, Cancelled = 9 }

public enum MenuCreationMode { Manual = 1, Auto = 2, Copied = 3 }

/// <summary>Menu workflow (SPEC §7.4.5): Draft → Submitted → (Approved | Rejected | Returned) → Published → (Cancelled).</summary>
public enum MenuStatus { Draft = 1, Submitted = 2, Approved = 3, Rejected = 4, Returned = 5, Published = 6, Cancelled = 9 }

public enum ApprovalAction { Submit = 1, Approve = 2, Reject = 3, Return = 4, Publish = 5, Cancel = 6, Recall = 7, OverrideCritical = 8 }

public enum MealOrderStatus { Draft = 1, Confirmed = 2, Adjusted = 3, Closed = 4, Cancelled = 9 }

public enum QualityResult { Pass = 1, Fail = 2, ConditionalPass = 3 }

public enum IssueReason { ForMenu = 1, Disposal = 2, Return = 3, Transfer = 4, Adjustment = 5, Other = 9 }

public enum StockDocStatus { Draft = 1, Posted = 2, Cancelled = 9 }

public enum StockTakeStatus { Draft = 1, Counting = 2, Approved = 3, Cancelled = 9 }

public enum LedgerType { In = 1, Out = 2, AdjustIn = 3, AdjustOut = 4 }

public enum PriceSource { Receipt = 1, Manual = 2, Contract = 3 }

/// <summary>Unit price source used by CostEngine (SPEC §6.3).</summary>
public enum CostPriceMode { ReferencePrice = 1, LastReceipt = 2, WeightedAverage = 3, Contract = 4 }

public enum InventoryCostMethod { MovingAverage = 1, Fifo = 2 }

public enum OverheadMode { PercentOfFood = 1, FixedPerServing = 2 }

public enum ContractStatus { Draft = 1, Active = 2, Expired = 3, Terminated = 4 }

public enum PurchaseRequestStatus { Draft = 1, Submitted = 2, Approved = 3, Ordered = 4, Received = 5, Cancelled = 9 }

public enum AlertStatus { Open = 1, Acknowledged = 2, Resolved = 3 }

public enum AuditAction
{
    Login = 1, Logout = 2, LoginFailed = 3, Create = 4, Update = 5, Delete = 6, Approve = 7, Reject = 8,
    Export = 9, Import = 10, PermissionChange = 11, ConfigChange = 12, Backup = 13, Restore = 14, SyncRun = 15,
    View = 16, PasswordChange = 17, MfaChange = 18, SessionRevoke = 19, PiiAccess = 20, Submit = 21, Publish = 22,
    Recall = 23, Lock = 24, Unlock = 25, Post = 26, Consent = 27, Other = 99,
}

public enum LoginResult { Success = 1, Failed = 2, Locked = 3, MfaRequired = 4, MfaFailed = 5, Disabled = 6 }

public enum BackupType { Scheduled = 1, Manual = 2 }

public enum JobStatus { Queued = 1, Running = 2, Succeeded = 3, Failed = 4, Expired = 5 }

public enum IntegrationAuthType { None = 0, ApiKey = 1, Basic = 2, OAuth2ClientCredentials = 3 }

public enum SyncConflictStatus { Pending = 1, KeptLocal = 2, AcceptedRemote = 3 }

public enum DataScopeType { Warehouse = 1, Customer = 2 }

public enum NotificationType { Info = 1, MenuPendingApproval = 2, MenuApprovalResult = 3, Alert = 4, ExportReady = 5, System = 9 }
