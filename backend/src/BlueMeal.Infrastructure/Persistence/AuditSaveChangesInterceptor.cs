using System.Reflection;
using System.Text.Json;
using BlueMeal.Application.Abstractions;
using BlueMeal.Domain.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlueMeal.Infrastructure.Persistence;

/// <summary>
/// Fills audit columns, converts deletes to soft deletes, maintains search_text and HMAC hash columns, and appends
/// JSON-diff rows to audit_log in the same transaction (SPEC §10.3).
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock, IEncryptionService encryption) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static readonly HashSet<string> IgnoredProps = [nameof(AuditableEntity.Version), nameof(ICatalog.SearchText), nameof(AuditableEntity.UpdatedAt), nameof(AuditableEntity.UpdatedBy), "ConcurrencyStamp", "SecurityStamp", "PasswordHash"];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Process(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Process(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void Process(DbContext? ctx)
    {
        if (ctx is null) return;
        var now = clock.GetUtcNow();
        var userId = currentUser.UserId;
        var audits = new List<AuditLog>();

        foreach (var entry in ctx.ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;
            var entity = entry.Entity;

            if (entry.State == EntityState.Deleted && entity is ISoftDelete sd)
            {
                entry.State = EntityState.Modified;
                sd.IsDeleted = true;
                AuditDelete(entry, audits, now);
                if (entity is IAuditable a1) { a1.UpdatedAt = now; a1.UpdatedBy = userId; }
                continue;
            }

            if (entity is IAuditable a)
            {
                if (entry.State == EntityState.Added) { a.CreatedAt = now; a.CreatedBy ??= userId; }
                else if (entry.State == EntityState.Modified) { a.UpdatedAt = now; a.UpdatedBy = userId; entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false; entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false; }
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                if (entity is CatalogEntity c)
                    c.SearchText = TextNormalizer.Build(new[] { c.Code, c.NameVi, c.NameEn }.Concat(c.ExtraSearchFields()));
                else if (entity is IHasSearchText hs)
                    hs.SearchText = TextNormalizer.Build(hs.SearchFields());
                UpdateHashColumns(entity);
            }

            if (entity.GetType().GetCustomAttribute<NoAuditAttribute>() is not null) continue;

            if (entry.State == EntityState.Added) audits.Add(Build(entry, AuditAction.Create, null, Values(entry, current: true, onlyModified: false), now));
            else if (entry.State == EntityState.Modified)
            {
                var (oldV, newV) = Diff(entry);
                if (newV.Count > 0) audits.Add(Build(entry, AuditAction.Update, oldV, newV, now));
            }
            else if (entry.State == EntityState.Deleted) audits.Add(Build(entry, AuditAction.Delete, Values(entry, current: false, onlyModified: false), null, now));
        }

        if (audits.Count > 0) ctx.Set<AuditLog>().AddRange(audits);
    }

    private void AuditDelete(EntityEntry entry, List<AuditLog> audits, DateTimeOffset now)
    {
        if (entry.Entity.GetType().GetCustomAttribute<NoAuditAttribute>() is not null) return;
        audits.Add(Build(entry, AuditAction.Delete, Values(entry, current: false, onlyModified: false), null, now));
    }

    /// <summary>For property "X" marked [Encrypted] with sibling "XHash", compute keyed hash for exact-match search.</summary>
    private void UpdateHashColumns(object entity)
    {
        var type = entity.GetType();
        foreach (var p in type.GetProperties().Where(p => p.GetCustomAttribute<EncryptedAttribute>() != null))
        {
            var hashProp = type.GetProperty(p.Name + "Hash");
            if (hashProp is null || hashProp.PropertyType != typeof(string)) continue;
            hashProp.SetValue(entity, encryption.Hash(p.GetValue(entity) as string));
        }
    }

    private AuditLog Build(EntityEntry entry, AuditAction action, Dictionary<string, object?>? oldV, Dictionary<string, object?>? newV, DateTimeOffset now)
    {
        var id = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString();
        return new AuditLog
        {
            At = now,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Ip = currentUser.Ip,
            UserAgent = Truncate(currentUser.UserAgent, 500),
            TraceId = currentUser.TraceId,
            Action = action,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = id,
            OldValues = oldV is null ? null : JsonSerializer.Serialize(oldV, Json),
            NewValues = newV is null ? null : JsonSerializer.Serialize(newV, Json),
            Success = true,
        };
    }

    private static Dictionary<string, object?> Values(EntityEntry entry, bool current, bool onlyModified)
    {
        var d = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            if (IgnoredProps.Contains(p.Metadata.Name) || p.Metadata.Name.EndsWith("Hash", StringComparison.Ordinal)) continue;
            if (onlyModified && !p.IsModified) continue;
            d[Camel(p.Metadata.Name)] = Mask(p.Metadata.PropertyInfo, current ? p.CurrentValue : p.OriginalValue);
        }
        return d;
    }

    private static (Dictionary<string, object?> Old, Dictionary<string, object?> New) Diff(EntityEntry entry)
    {
        var o = new Dictionary<string, object?>();
        var n = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            if (!p.IsModified || IgnoredProps.Contains(p.Metadata.Name) || p.Metadata.Name.EndsWith("Hash", StringComparison.Ordinal)) continue;
            if (Equals(p.OriginalValue, p.CurrentValue)) continue;
            o[Camel(p.Metadata.Name)] = Mask(p.Metadata.PropertyInfo, p.OriginalValue);
            n[Camel(p.Metadata.Name)] = Mask(p.Metadata.PropertyInfo, p.CurrentValue);
        }
        return (o, n);
    }

    private static object? Mask(PropertyInfo? pi, object? value)
    {
        if (value is null) return null;
        if (pi?.GetCustomAttribute<SensitiveAttribute>() is not null) return "***";
        return value is string s && s.Length > 1000 ? s[..1000] + "…" : value;
    }

    private static string Camel(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];
    private static string? Truncate(string? s, int n) => s is null || s.Length <= n ? s : s[..n];
}
