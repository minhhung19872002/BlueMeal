using BlueMeal.Domain.Common;
using BlueMeal.Domain.Enums;

namespace BlueMeal.Domain.Entities;

/// <summary>Worker subject group (danh mục đối tượng) — SPEC §5.2.</summary>
public class SubjectGroup : CatalogEntity
{
    public Gender Gender { get; set; }
    public int? AgeFrom { get; set; }
    public int? AgeTo { get; set; }
    public LaborIntensity LaborIntensity { get; set; } = LaborIntensity.Moderate;
    /// <summary>Special working conditions: hot, toxic… (comma separated or free text).</summary>
    public string? SpecialConditions { get; set; }
    /// <summary>Allergens to avoid for this group (comma separated codes).</summary>
    public string? Allergens { get; set; }
    public string? Note { get; set; }
    public Guid? DefaultRecommendationId { get; set; }
}

public class PartnerType : CatalogEntity
{
    public string? Description { get; set; }
}

public class Partner : CatalogEntity
{
    public Guid? PartnerTypeId { get; set; }
    public PartnerType? PartnerType { get; set; }
    public string? TaxCode { get; set; }
    public string? ContactPerson { get; set; }
    [Encrypted, Sensitive] public string? Phone { get; set; }
    [Encrypted, Sensitive] public string? Email { get; set; }
    public string? Address { get; set; }
    public Guid? ProvinceId { get; set; }
    public Guid? WardId { get; set; }
    public string? BankAccount { get; set; }
    public string? BankName { get; set; }
    /// <summary>Supplied goods groups (comma separated food group codes).</summary>
    public string? SuppliedGroups { get; set; }
    /// <summary>Average rating 0–5 computed from <see cref="PartnerRating"/>.</summary>
    public decimal? Rating { get; set; }
    public string? Note { get; set; }
    public override IEnumerable<string?> ExtraSearchFields() => [TaxCode, ContactPerson];
}

public class PartnerContract : AuditableEntity
{
    public Guid PartnerId { get; set; }
    public Partner? Partner { get; set; }
    public string ContractNo { get; set; } = default!;
    public DateOnly SignedDate { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal Value { get; set; }
    public Guid? FileId { get; set; }
    public ContractStatus Status { get; set; } = ContractStatus.Active;
    public string? Note { get; set; }
    public ICollection<PartnerContractPrice> Prices { get; set; } = new List<PartnerContractPrice>();
}

/// <summary>Contracted unit price per ingredient (used by CostEngine mode "Contract").</summary>
public class PartnerContractPrice : Entity
{
    public Guid ContractId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal UnitPrice { get; set; }
}

public class PartnerCertificate : AuditableEntity
{
    public Guid PartnerId { get; set; }
    public Partner? Partner { get; set; }
    /// <summary>FoodSafetyFacility, HealthCheck, LabTest, BusinessLicense, Other.</summary>
    public string CertificateType { get; set; } = default!;
    public string Number { get; set; } = default!;
    public string? Issuer { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public Guid? FileId { get; set; }
    public string? Note { get; set; }
}

public class PartnerRating : AuditableEntity
{
    public Guid PartnerId { get; set; }
    public DateOnly RatedDate { get; set; }
    /// <summary>1..5 for quality, delivery, price.</summary>
    public int Quality { get; set; }
    public int Delivery { get; set; }
    public int Price { get; set; }
    public string? Comment { get; set; }
}

public class CustomerType : CatalogEntity
{
    public string? Description { get; set; }
}

/// <summary>Unit that orders shift meals (company A, B…).</summary>
public class Customer : CatalogEntity
{
    public Guid? CustomerTypeId { get; set; }
    public CustomerType? CustomerType { get; set; }
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public Guid? ProvinceId { get; set; }
    public Guid? WardId { get; set; }
    public string? ContactPerson { get; set; }
    [Encrypted, Sensitive] public string? Phone { get; set; }
    [Encrypted, Sensitive] public string? Email { get; set; }
    public Guid? DefaultSubjectGroupId { get; set; }
    public int? WorkerCount { get; set; }
    /// <summary>Agreed price per serving (VND) — current value; history in <see cref="CustomerPrice"/>.</summary>
    public decimal? AgreedPricePerServing { get; set; }
    /// <summary>Working shifts description, e.g. "Ca 1, Ca 2".</summary>
    public string? Shifts { get; set; }
    /// <summary>Declared allergens to avoid (comma separated codes).</summary>
    public string? Allergens { get; set; }
    public string? Note { get; set; }
    public override IEnumerable<string?> ExtraSearchFields() => [TaxCode, ContactPerson];
}

public class CustomerContact : AuditableEntity
{
    public Guid CustomerId { get; set; }
    public string FullName { get; set; } = default!;
    public string? Position { get; set; }
    [Encrypted, Sensitive] public string? Phone { get; set; }
    [Encrypted, Sensitive] public string? Email { get; set; }
    public bool IsPrimary { get; set; }
    public string? Note { get; set; }
}

public class CustomerPrice : AuditableEntity
{
    public Guid CustomerId { get; set; }
    public Guid? MealShiftId { get; set; }
    public Guid? SubjectGroupId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal PricePerServing { get; set; }
    public string? Note { get; set; }
}
