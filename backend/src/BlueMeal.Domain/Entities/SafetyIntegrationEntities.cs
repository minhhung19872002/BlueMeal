using BlueMeal.Domain.Common;
using BlueMeal.Domain.Enums;

namespace BlueMeal.Domain.Entities;

/// <summary>Step 1 — check of incoming food before processing (kiểm tra trước khi chế biến).</summary>
public class FoodInspectionStep1 : AuditableEntity
{
    public DateTimeOffset InspectedAt { get; set; }
    public Guid? ReceiptId { get; set; }
    public Guid? ReceiptLineId { get; set; }
    public Guid IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public Guid? PartnerId { get; set; }
    public decimal? Quantity { get; set; }
    public string? LotNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    /// <summary>JSON of configurable criteria results (A-23).</summary>
    public string? Criteria { get; set; }
    public string? SensoryNote { get; set; }
    public bool OriginDocsOk { get; set; }
    public string? OriginDocs { get; set; }
    public decimal? Temperature { get; set; }
    public QualityResult Result { get; set; } = QualityResult.Pass;
    public string? Action { get; set; }
    public string Inspector { get; set; } = default!;
    public Guid? InspectorUserId { get; set; }
}

/// <summary>Step 2 — check during processing (kiểm tra trong quá trình chế biến).</summary>
public class FoodInspectionStep2 : AuditableEntity
{
    public Guid? MenuId { get; set; }
    public Guid? DishId { get; set; }
    public Dish? Dish { get; set; }
    public DateOnly InspectDate { get; set; }
    public string? Hygiene { get; set; }
    public string? Participants { get; set; }
    public DateTimeOffset? CookStart { get; set; }
    public DateTimeOffset? CookEnd { get; set; }
    public string? Criteria { get; set; }
    public string? SensoryNote { get; set; }
    public QualityResult Result { get; set; } = QualityResult.Pass;
    public string? Action { get; set; }
    public string Inspector { get; set; } = default!;
}

/// <summary>Step 3 — check before eating (kiểm tra trước khi ăn).</summary>
public class FoodInspectionStep3 : AuditableEntity
{
    public Guid? MenuId { get; set; }
    public Guid? DishId { get; set; }
    public Dish? Dish { get; set; }
    public DateOnly InspectDate { get; set; }
    public DateTimeOffset? PortionTime { get; set; }
    public decimal? Temperature { get; set; }
    public string? Utensils { get; set; }
    public string? Criteria { get; set; }
    public string? SensoryNote { get; set; }
    public QualityResult Result { get; set; } = QualityResult.Pass;
    public string? Action { get; set; }
    public string Inspector { get; set; } = default!;
}

/// <summary>Food sample retention (lưu mẫu thức ăn).</summary>
public class FoodSample : AuditableEntity
{
    public string Code { get; set; } = default!;
    public Guid? MenuId { get; set; }
    public Guid DishId { get; set; }
    public Dish? Dish { get; set; }
    public DateTimeOffset SampledAt { get; set; }
    public decimal WeightG { get; set; }
    public decimal? StorageTemp { get; set; }
    public string Sampler { get; set; } = default!;
    public DateTimeOffset PlannedDisposeAt { get; set; }
    public DateTimeOffset? DisposedAt { get; set; }
    public string? Disposer { get; set; }
    public string? Note { get; set; }
}

public class IntegrationEndpoint : CatalogEntity
{
    public string BaseUrl { get; set; } = default!;
    public IntegrationAuthType AuthType { get; set; }
    /// <summary>JSON credential bag (apiKey, header, username, password, tokenUrl, clientId, clientSecret, scope) — encrypted.</summary>
    [Encrypted, Sensitive] public string? Credentials { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>Cron expression for scheduled sync (default 02:00 daily).</summary>
    public string? SyncCron { get; set; } = "0 2 * * *";
    /// <summary>Field mapping JSON (A-25).</summary>
    public string? MappingJson { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public bool IsEnabled { get; set; } = true;
}

[NoAudit]
public class IntegrationLog : Entity
{
    public Guid? EndpointId { get; set; }
    public string Direction { get; set; } = "Outbound";
    public string Operation { get; set; } = default!;
    public DateTimeOffset StartedAt { get; set; }
    public int DurationMs { get; set; }
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public int RecordsReceived { get; set; }
    public int RecordsCreated { get; set; }
    public int RecordsUpdated { get; set; }
    public int Conflicts { get; set; }
    public string? RequestSummary { get; set; }
    public string? ResponseSummary { get; set; }
    public string? Error { get; set; }
    public Guid? TriggeredBy { get; set; }
}

[NoAudit]
public class SyncConflict : Entity
{
    public Guid? EndpointId { get; set; }
    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }
    public string EntityCode { get; set; } = default!;
    public string LocalJson { get; set; } = default!;
    public string RemoteJson { get; set; } = default!;
    public DateTimeOffset DetectedAt { get; set; }
    public SyncConflictStatus Status { get; set; } = SyncConflictStatus.Pending;
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
}

[NoAudit]
public class BackupJob : Entity
{
    public BackupType Type { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? Location { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public Guid? RequestedBy { get; set; }
    public string? Error { get; set; }
    public string? Note { get; set; }
}

[NoAudit]
public class ExportJob : Entity
{
    public string ReportCode { get; set; } = default!;
    public string? Parameters { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public Guid RequestedBy { get; set; }
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    /// <summary>Random download token (link valid until <see cref="ExpiresAt"/>).</summary>
    public string? DownloadToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Error { get; set; }
    public string Language { get; set; } = "vi";
}
