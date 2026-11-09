using BlueMeal.Application.Catalog;
using BlueMeal.Domain.Common;
using FluentValidation;
using Mapster;

namespace BlueMeal.Api.Catalog;

/// <summary>Common read fields for catalog DTOs.</summary>
public abstract class CatalogDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;
    public string NameVi { get; set; } = default!;
    public string? NameEn { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public uint Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Common writable fields for catalog upserts (whitelisted — no audit/ids).</summary>
public abstract class CatalogUpsert : IHasVersion
{
    [ExcelColumn("Mã", "Code", Required = true, Order = 1, Width = 14)]
    public string Code { get; set; } = default!;
    [ExcelColumn("Tên (tiếng Việt)", "Name (Vietnamese)", Required = true, Order = 2, Width = 36)]
    public string NameVi { get; set; } = default!;
    [ExcelColumn("Tên (tiếng Anh)", "Name (English)", Order = 3, Width = 30)]
    public string? NameEn { get; set; }
    [ExcelColumn("Đang sử dụng", "Active", Order = 900, Width = 10)]
    public bool IsActive { get; set; } = true;
    [ExcelColumn("Thứ tự", "Order", Order = 901, Width = 8)]
    public int SortOrder { get; set; }
    public uint? Version { get; set; }
}

public abstract class CatalogUpsertValidator<T> : AbstractValidator<T> where T : CatalogUpsert
{
    protected CatalogUpsertValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches(@"^[A-Za-z0-9_.\-/]+$").WithMessage("validation.code");
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(300);
        RuleFor(x => x.NameEn).MaximumLength(300);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1_000_000);
    }
}

public static class MapsterSetup
{
    private static readonly HashSet<string> Protected = ["Id", "Version", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "IsDeleted", "SearchText"];

    /// <summary>
    /// Upsert DTO → entity mapping never touches identity/audit/concurrency columns (mass-assignment protection, SPEC §10.5).
    /// </summary>
    public static void Configure()
    {
        TypeAdapterConfig.GlobalSettings.Default
            .IgnoreMember((member, side) => side == MemberSide.Destination && Protected.Contains(member.Name)
                                            && member.Info is System.Reflection.MemberInfo mi && mi.DeclaringType is { } dt && typeof(Entity).IsAssignableFrom(dt))
            .IgnoreNullValues(false)
            .PreserveReference(false);
        TypeAdapterConfig.GlobalSettings.Compiler = exp => exp.Compile();
    }
}
