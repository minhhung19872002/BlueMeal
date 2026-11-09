using BlueMeal.Domain.Common;
using BlueMeal.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace BlueMeal.Domain.Entities;

/// <summary>The single organization that operates the software (SPEC §5.1).</summary>
public class Organization : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string? NameEn { get; set; }
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public Guid? LogoFileId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Representative { get; set; }
    public string? RepresentativeTitle { get; set; }
    public string? Website { get; set; }
}

public class Department : CatalogEntity
{
    public Guid? ParentId { get; set; }
    public Department? Parent { get; set; }
    public string? Description { get; set; }
}

public class Country : CatalogEntity
{
    public string? Iso2 { get; set; }
}

public class Province : CatalogEntity
{
    public Guid CountryId { get; set; }
    public Country? Country { get; set; }
    /// <summary>"Tỉnh" or "Thành phố".</summary>
    public string? Kind { get; set; }
}

public class Ward : CatalogEntity
{
    public Guid ProvinceId { get; set; }
    public Province? Province { get; set; }
    /// <summary>"Xã", "Phường" or "Đặc khu".</summary>
    public string? Kind { get; set; }
}

public class UnitOfMeasure : CatalogEntity
{
    public UomType Type { get; set; }
    /// <summary>Multiplier to the base unit of its type (g, ml, piece, kcal).</summary>
    public decimal FactorToBase { get; set; } = 1m;
    public string? Symbol { get; set; }
}

public class Employee : AuditableEntity, IHasSearchText
{
    public string Code { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public string? Position { get; set; }
    public Gender Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [Encrypted, Sensitive] public string? Phone { get; set; }
    public string? PhoneHash { get; set; }
    [Encrypted, Sensitive] public string? Email { get; set; }
    public string? EmailHash { get; set; }
    [Encrypted, Sensitive] public string? NationalId { get; set; }
    public string? NationalIdHash { get; set; }
    public Guid? UserId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsAnonymized { get; set; }
    public string? SearchText { get; set; }
    public IEnumerable<string?> SearchFields() => [Code, FullName, Position];
}

public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = default!;
    public Guid? EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public bool MfaRequired { get; set; }
    [Encrypted, Sensitive] public string? MfaSecret { get; set; }
    public bool EmailOtpEnabled { get; set; }
    public string Language { get; set; } = "vi";
    public string Theme { get; set; } = "light";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsDeleted { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class AppRole : IdentityRole<Guid>
{
    public string NameVi { get; set; } = default!;
    public string? NameEn { get; set; }
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool RequiresMfa { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class UserRole : IdentityUserRole<Guid>
{
    public AppUser? User { get; set; }
    public AppRole? Role { get; set; }
}

[NoAudit]
public class Permission : Entity
{
    public string Code { get; set; } = default!;
    public string Module { get; set; } = default!;
    public string Action { get; set; } = default!;
    public string NameVi { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public int SortOrder { get; set; }
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public AppRole? Role { get; set; }
    public Guid PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

public class UserDataScope : Entity
{
    public Guid UserId { get; set; }
    public DataScopeType ScopeType { get; set; }
    public Guid ScopeId { get; set; }
}

[NoAudit]
public class UserSession : Entity
{
    public Guid UserId { get; set; }
    public string RefreshTokenHash { get; set; } = default!;
    /// <summary>Hash of the previous refresh token: re-use detection after rotation.</summary>
    public string? PreviousTokenHash { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? Device { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public bool IsMfaSetupOnly { get; set; }
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

[NoAudit]
public class LoginHistory : Entity
{
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = default!;
    public DateTimeOffset At { get; set; }
    public LoginResult Result { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? Reason { get; set; }
}

[NoAudit]
public class MfaRecoveryCode : Entity
{
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = default!;
    public DateTimeOffset? UsedAt { get; set; }
}

[NoAudit]
public class PasswordHistory : Entity
{
    public Guid UserId { get; set; }
    public string PasswordHash { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
}

[NoAudit]
public class OneTimeToken : Entity
{
    public Guid UserId { get; set; }
    /// <summary>PasswordReset, EmailOtp.</summary>
    public string Purpose { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

[NoAudit]
public class SystemVersion : Entity
{
    public string Version { get; set; } = default!;
    public DateOnly ReleaseDate { get; set; }
    public string NotesVi { get; set; } = default!;
    public string? NotesEn { get; set; }
}

public class AppSetting : Entity, IVersioned
{
    public string Key { get; set; } = default!;
    /// <summary>JSON document.</summary>
    public string Value { get; set; } = "{}";
    public string? Description { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public uint Version { get; set; }
}

/// <summary>Append-only centralized audit log (SPEC §10.3). Partitioned monthly by <see cref="At"/>.</summary>
[NoAudit]
public class AuditLog
{
    public Guid Id { get; set; } = SequentialGuid.NewGuid();
    public DateTimeOffset At { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public AuditAction Action { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    /// <summary>JSON before values (sensitive fields masked).</summary>
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public string? TraceId { get; set; }
}

[NoAudit]
public class Notification : Entity
{
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public string TitleVi { get; set; } = default!;
    public string? TitleEn { get; set; }
    public string? BodyVi { get; set; }
    public string? BodyEn { get; set; }
    public string? Link { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>Uploaded file metadata. Content is stored on the files volume under a random name (SPEC §10.4).</summary>
public class FileAttachment : AuditableEntity
{
    public string OriginalName { get; set; } = default!;
    public string StoredName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long Size { get; set; }
    public string Sha256 { get; set; } = default!;
    /// <summary>Owner entity type (e.g. PartnerCertificate) — used for permission checks on download.</summary>
    public string? OwnerType { get; set; }
    public Guid? OwnerId { get; set; }
}

/// <summary>Per-user preferences: table column layouts, favourite report filters…</summary>
[NoAudit]
public class UserPreference : Entity
{
    public Guid UserId { get; set; }
    public string Key { get; set; } = default!;
    public string Value { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>OAuth2 client for the public API (ASSUMPTIONS A-26).</summary>
public class ApiClient : AuditableEntity
{
    public string ClientId { get; set; } = default!;
    public string SecretHash { get; set; } = default!;
    public string Name { get; set; } = default!;
    /// <summary>Space separated scopes.</summary>
    public string Scopes { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastUsedAt { get; set; }
}
