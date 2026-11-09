using BlueMeal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BlueMeal.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<T> Set<T>() where T : class;
    EntityEntry<T> Entry<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    string? FullName { get; }
    string? Ip { get; }
    string? UserAgent { get; }
    string? TraceId { get; }
    string Language { get; }
    IReadOnlySet<string> Permissions { get; }
    bool Has(string permission);
    /// <summary>Warehouse ids the user may access; null = unrestricted.</summary>
    IReadOnlySet<Guid>? WarehouseScope { get; }
    /// <summary>Customer ids the user may access; null = unrestricted.</summary>
    IReadOnlySet<Guid>? CustomerScope { get; }
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    /// <summary>Invalidate all keys sharing a logical region (implemented with a region version counter).</summary>
    Task InvalidateRegionAsync(string region, CancellationToken ct = default);
    Task<string> RegionKeyAsync(string region, string key, CancellationToken ct = default);
    /// <summary>Atomic set-if-not-exists; returns false if the key exists.</summary>
    Task<bool> TrySetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);
    Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}

public interface IEncryptionService
{
    string? Encrypt(string? plain);
    string? Decrypt(string? cipher);
    /// <summary>Deterministic keyed hash (HMAC-SHA256) for exact-match search on encrypted columns.</summary>
    string? Hash(string? value);
}

public interface IFileStorage
{
    Task<(string StoredName, long Size, string Sha256)> SaveAsync(Stream content, string extension, CancellationToken ct = default);
    Stream OpenRead(string storedName);
    Task DeleteAsync(string storedName, CancellationToken ct = default);
    string RootPath { get; }
}

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

public interface IAuditWriter
{
    Task WriteAsync(AuditAction action, string? entityType = null, string? entityId = null, object? oldValues = null,
        object? newValues = null, bool success = true, string? message = null, CancellationToken ct = default);
}

public interface INotificationService
{
    Task NotifyUsersAsync(IEnumerable<Guid> userIds, NotificationType type, string titleKey, object[] args, string? link, CancellationToken ct = default);
    Task NotifyPermissionAsync(string permission, NotificationType type, string titleKey, object[] args, string? link, CancellationToken ct = default);
}

public interface ISettingsService
{
    Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : new();
    Task SetAsync<T>(string key, T value, CancellationToken ct = default);
}

/// <summary>Background job queue (Quartz.NET persistent store — ASSUMPTIONS A-34).</summary>
public interface IBackgroundJobs
{
    /// <summary>Queue a named job (see <see cref="IAppJob.Name"/>) to run once as soon as a worker is free.</summary>
    Task EnqueueAsync(string jobName, IDictionary<string, string>? data = null, CancellationToken ct = default);
}

/// <summary>A background job executed by the worker. Implementations are resolved by <see cref="Name"/>.</summary>
public interface IAppJob
{
    string Name { get; }
    Task ExecuteAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct);
}

/// <summary>Generates document numbers like PN-20260101-0001 (atomic per prefix and day).</summary>
public interface IDocNumberGenerator
{
    Task<string> NextAsync(string prefix, DateOnly date, CancellationToken ct = default);
}

public static class JobNames
{
    public const string VddSync = "vdd-sync";
    public const string Backup = "backup";
    public const string DailyAlerts = "daily-alerts";
    public const string Housekeeping = "housekeeping";
    public const string Export = "export-report";
    public const string GenerateMenus = "generate-menus";
}
