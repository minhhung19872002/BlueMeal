namespace BlueMeal.Domain.Common;

/// <summary>Root of all persistent entities. Ids are sequential-ish GUIDs to keep btree indexes compact.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = SequentialGuid.NewGuid();
}

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}

public interface ISoftDelete
{
    bool IsDeleted { get; set; }
}

/// <summary>Marker: entity carries a PostgreSQL xmin concurrency token mapped to <see cref="AuditableEntity.Version"/>.</summary>
public interface IVersioned
{
    uint Version { get; set; }
}

/// <summary>Business entity with common columns (SPEC §5): audit columns, soft delete and xmin concurrency.</summary>
public abstract class AuditableEntity : Entity, IAuditable, ISoftDelete, IVersioned
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public uint Version { get; set; }
}

public interface ICatalog
{
    string Code { get; set; }
    string NameVi { get; set; }
    string? NameEn { get; set; }
    bool IsActive { get; set; }
    int SortOrder { get; set; }
    string? SearchText { get; set; }
}

/// <summary>Catalog entity: code (unique), bilingual name, active flag, sort order and unaccented search text.</summary>
public abstract class CatalogEntity : AuditableEntity, ICatalog
{
    public string Code { get; set; } = default!;
    public string NameVi { get; set; } = default!;
    public string? NameEn { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    /// <summary>Lower-cased, diacritics-removed concatenation of searchable fields (see ASSUMPTIONS A-04).</summary>
    public string? SearchText { get; set; }

    /// <summary>Extra fields to include in <see cref="SearchText"/> besides code and names.</summary>
    public virtual IEnumerable<string?> ExtraSearchFields() => Array.Empty<string?>();
}

public interface IHasSearchText
{
    string? SearchText { get; set; }
    IEnumerable<string?> SearchFields();
}

/// <summary>Marks a string property to be stored encrypted (AES-256-GCM) at column level (SPEC §10.4).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EncryptedAttribute : Attribute;

/// <summary>Marks a property as sensitive: it is masked in audit diffs and for users without pii.view.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAttribute : Attribute;

/// <summary>Excludes the entity from automatic audit logging (high-volume technical tables).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NoAuditAttribute : Attribute;

public static class SequentialGuid
{
    private static long _counter = DateTime.UtcNow.Ticks;

    /// <summary>Generates a GUID whose leading bytes are time-ordered (similar to UUIDv7) to reduce index fragmentation.</summary>
    public static Guid NewGuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(bytes);
        var ticks = Interlocked.Increment(ref _counter);
        var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // big-endian unix ms in first 6 bytes (RFC 9562 layout)
        bytes[0] = (byte)(ms >> 40); bytes[1] = (byte)(ms >> 32); bytes[2] = (byte)(ms >> 24);
        bytes[3] = (byte)(ms >> 16); bytes[4] = (byte)(ms >> 8); bytes[5] = (byte)ms;
        bytes[6] = (byte)(0x70 | ((ticks >> 8) & 0x0F)); bytes[7] = (byte)ticks;
        bytes[8] = (byte)(0x80 | (bytes[8] & 0x3F));
        return new Guid(bytes, bigEndian: true);
    }
}
