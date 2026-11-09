using BlueMeal.Api.Authorization;
using BlueMeal.Api.Catalog;
using BlueMeal.Api.Controllers.Admin;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using FluentValidation;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Catalogs;

// ================================================================== Subject groups (đối tượng) — SPEC §7.2

public sealed class SubjectGroupDto : CatalogDto
{
    public Gender Gender { get; set; }
    public int? AgeFrom { get; set; }
    public int? AgeTo { get; set; }
    public LaborIntensity LaborIntensity { get; set; }
    public string? SpecialConditions { get; set; }
    public string? Allergens { get; set; }
    public string? Note { get; set; }
    public Guid? DefaultRecommendationId { get; set; }
}

public sealed class SubjectGroupUpsert : CatalogUpsert
{
    [ExcelColumn("Giới tính (Male/Female/Common)", "Gender", Order = 4)] public Gender Gender { get; set; }
    [ExcelColumn("Tuổi từ", "Age from", Order = 5)] public int? AgeFrom { get; set; }
    [ExcelColumn("Tuổi đến", "Age to", Order = 6)] public int? AgeTo { get; set; }
    [ExcelColumn("Mức độ lao động", "Labor intensity", Required = true, Order = 7)] public LaborIntensity LaborIntensity { get; set; } = LaborIntensity.Moderate;
    [ExcelColumn("Điều kiện đặc thù", "Special conditions", Order = 8, Width = 30)] public string? SpecialConditions { get; set; }
    [ExcelColumn("Chất gây dị ứng cần tránh", "Allergens", Order = 9)] public string? Allergens { get; set; }
    [ExcelColumn("Ghi chú", "Note", Order = 10, Width = 30)] public string? Note { get; set; }
    [ExcelColumn("Mã khuyến nghị mặc định", "Default recommendation code", Order = 11), ExcelRef(typeof(Recommendation))] public Guid? DefaultRecommendationId { get; set; }
}

public sealed class SubjectGroupUpsertValidator : CatalogUpsertValidator<SubjectGroupUpsert>
{
    public SubjectGroupUpsertValidator()
    {
        RuleFor(x => x.AgeFrom).InclusiveBetween(14, 100).When(x => x.AgeFrom is not null);
        RuleFor(x => x.AgeTo).InclusiveBetween(14, 100).When(x => x.AgeTo is not null);
        RuleFor(x => x).Must(x => x.AgeFrom is null || x.AgeTo is null || x.AgeFrom <= x.AgeTo).WithMessage("error.validation");
        RuleFor(x => x.LaborIntensity).IsInEnum();
    }
}

[Route("api/v1/subject-groups"), PermissionModule("subject")]
public sealed class SubjectGroupsController : CatalogControllerBase<SubjectGroup, SubjectGroupDto, SubjectGroupUpsert>
{
    protected override string ExportTitle => L.Pick("Danh muc doi tuong", "Subject groups");
    protected override IQueryable<SubjectGroup> ApplyFilters(IQueryable<SubjectGroup> q, IQueryCollection query)
        => Enum.TryParse<LaborIntensity>(query["laborIntensity"], true, out var li) ? q.Where(x => x.LaborIntensity == li) : q;
}

// ================================================================== Partner types / partners (nhà cung cấp)

public sealed class SimpleTypeDto : CatalogDto { public string? Description { get; set; } }
public sealed class SimpleTypeUpsert : CatalogUpsert { [ExcelColumn("Mô tả", "Description", Order = 4, Width = 40)] public string? Description { get; set; } }
public sealed class SimpleTypeUpsertValidator : CatalogUpsertValidator<SimpleTypeUpsert>;

[Route("api/v1/partner-types"), PermissionModule("partner")]
public sealed class PartnerTypesController : CatalogControllerBase<PartnerType, SimpleTypeDto, SimpleTypeUpsert>
{
    protected override string ExportTitle => L.Pick("Loai doi tac", "Partner types");
    protected override async Task BeforeDeleteAsync(PartnerType e, CancellationToken ct)
    { if (await Db.Set<Partner>().AnyAsync(p => p.PartnerTypeId == e.Id, ct)) throw new ConflictException("error.inUse"); }
}

public sealed class PartnerDto : CatalogDto
{
    public Guid? PartnerTypeId { get; set; }
    public string? PartnerTypeNameVi { get; set; }
    public string? TaxCode { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public Guid? ProvinceId { get; set; }
    public Guid? WardId { get; set; }
    public string? BankAccount { get; set; }
    public string? BankName { get; set; }
    public string? SuppliedGroups { get; set; }
    public decimal? Rating { get; set; }
    public string? Note { get; set; }
    /// <summary>Nearest certificate expiry (for the list badge).</summary>
    public DateOnly? NextCertificateExpiry { get; set; }
}

public sealed class PartnerUpsert : CatalogUpsert
{
    [ExcelColumn("Mã loại đối tác", "Partner type code", Order = 4), ExcelRef(typeof(PartnerType))] public Guid? PartnerTypeId { get; set; }
    [ExcelColumn("Mã số thuế", "Tax code", Order = 5)] public string? TaxCode { get; set; }
    [ExcelColumn("Người liên hệ", "Contact person", Order = 6)] public string? ContactPerson { get; set; }
    [ExcelColumn("Điện thoại", "Phone", Order = 7)] public string? Phone { get; set; }
    [ExcelColumn("Email", "Email", Order = 8, Width = 24)] public string? Email { get; set; }
    [ExcelColumn("Địa chỉ", "Address", Order = 9, Width = 40)] public string? Address { get; set; }
    [ExcelColumn("Mã tỉnh/thành", "Province code", Order = 10), ExcelRef(typeof(Province))] public Guid? ProvinceId { get; set; }
    [ExcelColumn("Mã xã/phường", "Ward code", Order = 11), ExcelRef(typeof(Ward))] public Guid? WardId { get; set; }
    [ExcelColumn("Số tài khoản", "Bank account", Order = 12)] public string? BankAccount { get; set; }
    [ExcelColumn("Ngân hàng", "Bank", Order = 13)] public string? BankName { get; set; }
    [ExcelColumn("Nhóm hàng cung cấp", "Supplied groups", Order = 14, Width = 30)] public string? SuppliedGroups { get; set; }
    [ExcelColumn("Ghi chú", "Note", Order = 15, Width = 30)] public string? Note { get; set; }
}

public sealed class PartnerUpsertValidator : CatalogUpsertValidator<PartnerUpsert>
{
    public PartnerUpsertValidator()
    {
        RuleFor(x => x.TaxCode).Matches(@"^\d{10}(-\d{3})?$").When(x => !string.IsNullOrEmpty(x.TaxCode));
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
    }
}

[Route("api/v1/partners"), PermissionModule("partner")]
public sealed class PartnersController(FileService files) : CatalogControllerBase<Partner, PartnerDto, PartnerUpsert>
{
    protected override string ExportTitle => L.Pick("Danh muc doi tac", "Partners");

    protected override IQueryable<Partner> ApplyFilters(IQueryable<Partner> q, IQueryCollection query)
        => Guid.TryParse(query["partnerTypeId"], out var t) ? q.Where(x => x.PartnerTypeId == t) : q;

    protected override IQueryable<PartnerDto> Project(IQueryable<Partner> q)
        => q.Select(p => new PartnerDto
        {
            Id = p.Id, Code = p.Code, NameVi = p.NameVi, NameEn = p.NameEn, IsActive = p.IsActive, SortOrder = p.SortOrder, Version = p.Version, CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt,
            PartnerTypeId = p.PartnerTypeId, PartnerTypeNameVi = p.PartnerType!.NameVi, TaxCode = p.TaxCode, ContactPerson = p.ContactPerson, Phone = p.Phone, Email = p.Email,
            Address = p.Address, ProvinceId = p.ProvinceId, WardId = p.WardId, BankAccount = p.BankAccount, BankName = p.BankName, SuppliedGroups = p.SuppliedGroups, Rating = p.Rating, Note = p.Note,
            NextCertificateExpiry = Db.Set<PartnerCertificate>().Where(c => c.PartnerId == p.Id && c.ExpiryDate != null).Min(c => c.ExpiryDate),
        });

    // ---- contracts

    public sealed record ContractDto(Guid Id, string ContractNo, DateOnly SignedDate, DateOnly ValidFrom, DateOnly? ValidTo, decimal Value, Guid? FileId, ContractStatus Status, string? Note, uint Version, IReadOnlyList<ContractPriceDto> Prices);
    public sealed record ContractPriceDto(Guid IngredientId, string? IngredientCode, string? IngredientName, decimal UnitPrice);
    public sealed record ContractUpsert(string ContractNo, DateOnly SignedDate, DateOnly ValidFrom, DateOnly? ValidTo, decimal Value, Guid? FileId, ContractStatus Status, string? Note, uint? Version, List<ContractPriceInput>? Prices);
    public sealed record ContractPriceInput(Guid IngredientId, decimal UnitPrice);

    [HttpGet("{id:guid}/contracts"), HasPermission(Permissions.Partner.View)]
    public async Task<IActionResult> Contracts(Guid id, CancellationToken ct)
    {
        var list = await Db.Set<PartnerContract>().AsNoTracking().Include(c => c.Prices).Where(c => c.PartnerId == id).OrderByDescending(c => c.ValidFrom).ToListAsync(ct);
        var ingIds = list.SelectMany(c => c.Prices.Select(p => p.IngredientId)).Distinct().ToList();
        var ings = await Db.Set<Ingredient>().AsNoTracking().IgnoreQueryFilters().Where(i => ingIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => (i.Code, i.NameVi), ct);
        return Ok(list.Select(c => new ContractDto(c.Id, c.ContractNo, c.SignedDate, c.ValidFrom, c.ValidTo, c.Value, c.FileId, c.Status, c.Note, c.Version,
            c.Prices.Select(p => new ContractPriceDto(p.IngredientId, ings.GetValueOrDefault(p.IngredientId).Code, ings.GetValueOrDefault(p.IngredientId).NameVi, p.UnitPrice)).ToList())));
    }

    [HttpPost("{id:guid}/contracts"), HasPermission(Permissions.Partner.Edit)]
    public async Task<IActionResult> AddContract(Guid id, [FromBody] ContractUpsert req, CancellationToken ct)
    {
        _ = await Db.Set<Partner>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("partner");
        ValidateContract(req);
        var c = new PartnerContract { PartnerId = id };
        ApplyContract(c, req);
        Db.Set<PartnerContract>().Add(c);
        await Db.SaveChangesAsync(ct);
        await LinkFile(req.FileId, nameof(PartnerContract), c.Id, ct);
        return Ok(new { c.Id });
    }

    [HttpPut("{id:guid}/contracts/{contractId:guid}"), HasPermission(Permissions.Partner.Edit)]
    public async Task<IActionResult> UpdateContract(Guid id, Guid contractId, [FromBody] ContractUpsert req, CancellationToken ct)
    {
        ValidateContract(req);
        var c = await Db.Set<PartnerContract>().Include(x => x.Prices).FirstOrDefaultAsync(x => x.Id == contractId && x.PartnerId == id, ct) ?? throw new NotFoundException("contract");
        if (req.Version is { } v) Db.ChangeTracker.Entries<PartnerContract>().First(e => e.Entity == c).Property(e => e.Version).OriginalValue = v;
        ApplyContract(c, req);
        await Db.SaveChangesAsync(ct);
        await LinkFile(req.FileId, nameof(PartnerContract), c.Id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/contracts/{contractId:guid}"), HasPermission(Permissions.Partner.Edit)]
    public async Task<IActionResult> DeleteContract(Guid id, Guid contractId, CancellationToken ct)
    {
        var c = await Db.Set<PartnerContract>().FirstOrDefaultAsync(x => x.Id == contractId && x.PartnerId == id, ct) ?? throw new NotFoundException("contract");
        Db.Set<PartnerContract>().Remove(c);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void ValidateContract(ContractUpsert r)
    {
        if (string.IsNullOrWhiteSpace(r.ContractNo) || r.Value < 0 || (r.ValidTo is { } to && to < r.ValidFrom))
            throw new AppException("error.validation") { Errors = new Dictionary<string, string[]> { ["contract"] = [L.T("error.validation")] } };
    }

    private static void ApplyContract(PartnerContract c, ContractUpsert r)
    {
        c.ContractNo = r.ContractNo.Trim(); c.SignedDate = r.SignedDate; c.ValidFrom = r.ValidFrom; c.ValidTo = r.ValidTo; c.Value = r.Value;
        c.FileId = r.FileId; c.Status = r.Status; c.Note = r.Note;
        c.Prices.Clear();
        foreach (var p in (r.Prices ?? []).Where(p => p.UnitPrice >= 0).DistinctBy(p => p.IngredientId)) c.Prices.Add(new PartnerContractPrice { IngredientId = p.IngredientId, UnitPrice = p.UnitPrice });
    }

    private async Task LinkFile(Guid? fileId, string ownerType, Guid ownerId, CancellationToken ct)
    {
        if (fileId is null) return;
        await Db.Set<FileAttachment>().Where(f => f.Id == fileId && f.OwnerType == ownerType && (f.OwnerId == null || f.OwnerId == ownerId))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.OwnerId, ownerId), ct);
    }

    // ---- food safety certificates (GCN ATTP, health checks, lab tests…)

    public sealed record CertificateDto(Guid Id, string CertificateType, string Number, string? Issuer, DateOnly? IssuedDate, DateOnly? ExpiryDate, Guid? FileId, string? Note, uint Version, int? DaysToExpiry);
    public sealed record CertificateUpsert(string CertificateType, string Number, string? Issuer, DateOnly? IssuedDate, DateOnly? ExpiryDate, Guid? FileId, string? Note, uint? Version);

    [HttpGet("{id:guid}/certificates"), HasPermission(Permissions.Partner.View)]
    public async Task<IActionResult> Certificates(Guid id, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var list = await Db.Set<PartnerCertificate>().AsNoTracking().Where(c => c.PartnerId == id).OrderBy(c => c.ExpiryDate).ToListAsync(ct);
        return Ok(list.Select(c => new CertificateDto(c.Id, c.CertificateType, c.Number, c.Issuer, c.IssuedDate, c.ExpiryDate, c.FileId, c.Note, c.Version,
            c.ExpiryDate is { } e ? e.DayNumber - today.DayNumber : null)));
    }

    [HttpPost("{id:guid}/certificates"), HasPermission(Permissions.Partner.Edit)]
    public async Task<IActionResult> AddCertificate(Guid id, [FromBody] CertificateUpsert req, CancellationToken ct)
    {
        _ = await Db.Set<Partner>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("partner");
        if (string.IsNullOrWhiteSpace(req.Number) || string.IsNullOrWhiteSpace(req.CertificateType)) throw new AppException("error.validation");
        var c = req.Adapt<PartnerCertificate>();
        c.PartnerId = id;
        Db.Set<PartnerCertificate>().Add(c);
        await Db.SaveChangesAsync(ct);
        await LinkFile(req.FileId, nameof(PartnerCertificate), c.Id, ct);
        return Ok(new { c.Id });
    }

    [HttpPut("{id:guid}/certificates/{certId:guid}"), HasPermission(Permissions.Partner.Edit)]
    public async Task<IActionResult> UpdateCertificate(Guid id, Guid certId, [FromBody] CertificateUpsert req, CancellationToken ct)
    {
        var c = await Db.Set<PartnerCertificate>().FirstOrDefaultAsync(x => x.Id == certId && x.PartnerId == id, ct) ?? throw new NotFoundException("certificate");
        if (req.Version is { } v) Db.ChangeTracker.Entries<PartnerCertificate>().First(e => e.Entity == c).Property(e => e.Version).OriginalValue = v;
        req.Adapt(c);
        await Db.SaveChangesAsync(ct);
        await LinkFile(req.FileId, nameof(PartnerCertificate), c.Id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/certificates/{certId:guid}"), HasPermission(Permissions.Partner.Edit)]
    public async Task<IActionResult> DeleteCertificate(Guid id, Guid certId, CancellationToken ct)
    {
        var c = await Db.Set<PartnerCertificate>().FirstOrDefaultAsync(x => x.Id == certId && x.PartnerId == id, ct) ?? throw new NotFoundException("certificate");
        Db.Set<PartnerCertificate>().Remove(c);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- ratings & receipt history

    public sealed record RatingUpsert(DateOnly RatedDate, int Quality, int Delivery, int Price, string? Comment);

    [HttpGet("{id:guid}/ratings"), HasPermission(Permissions.Partner.View)]
    public async Task<IActionResult> Ratings(Guid id, CancellationToken ct)
        => Ok(await Db.Set<PartnerRating>().AsNoTracking().Where(r => r.PartnerId == id).OrderByDescending(r => r.RatedDate)
            .Select(r => new { r.Id, r.RatedDate, r.Quality, r.Delivery, r.Price, r.Comment, average = (r.Quality + r.Delivery + r.Price) / 3.0m, r.CreatedBy }).ToListAsync(ct));

    [HttpPost("{id:guid}/ratings"), HasPermission(Permissions.Partner.Edit)]
    public async Task<IActionResult> AddRating(Guid id, [FromBody] RatingUpsert req, CancellationToken ct)
    {
        var p = await Db.Set<Partner>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("partner");
        if (new[] { req.Quality, req.Delivery, req.Price }.Any(v => v is < 1 or > 5)) throw new AppException("error.validation");
        Db.Set<PartnerRating>().Add(new PartnerRating { PartnerId = id, RatedDate = req.RatedDate, Quality = req.Quality, Delivery = req.Delivery, Price = req.Price, Comment = req.Comment });
        await Db.SaveChangesAsync(ct);
        p.Rating = Math.Round(await Db.Set<PartnerRating>().Where(r => r.PartnerId == id).AverageAsync(r => (r.Quality + r.Delivery + r.Price) / 3.0m, ct), 2);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/receipts"), HasPermission(Permissions.Partner.View)]
    public async Task<IActionResult> Receipts(Guid id, [FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await Db.Set<StockReceipt>().AsNoTracking().Where(r => r.PartnerId == id).OrderByDescending(r => r.DocDate).Take(Math.Clamp(take, 1, 500))
            .Select(r => new { r.Id, r.DocNo, r.DocDate, warehouse = r.Warehouse!.NameVi, r.InvoiceNo, r.TotalAmount, status = r.Status.ToString(), lines = r.Lines.Count }).ToListAsync(ct));
}

// ================================================================== Customer types / customers (đơn vị đặt suất ăn)

[Route("api/v1/customer-types"), PermissionModule("customer")]
public sealed class CustomerTypesController : CatalogControllerBase<CustomerType, SimpleTypeDto, SimpleTypeUpsert>
{
    protected override string ExportTitle => L.Pick("Loai khach hang", "Customer types");
    protected override async Task BeforeDeleteAsync(CustomerType e, CancellationToken ct)
    { if (await Db.Set<Customer>().AnyAsync(p => p.CustomerTypeId == e.Id, ct)) throw new ConflictException("error.inUse"); }
}

public sealed class CustomerDto : CatalogDto
{
    public Guid? CustomerTypeId { get; set; }
    public string? CustomerTypeNameVi { get; set; }
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public Guid? ProvinceId { get; set; }
    public Guid? WardId { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Guid? DefaultSubjectGroupId { get; set; }
    public int? WorkerCount { get; set; }
    public decimal? AgreedPricePerServing { get; set; }
    public string? Shifts { get; set; }
    public string? Allergens { get; set; }
    public string? Note { get; set; }
}

public sealed class CustomerUpsert : CatalogUpsert
{
    [ExcelColumn("Mã loại khách hàng", "Customer type code", Order = 4), ExcelRef(typeof(CustomerType))] public Guid? CustomerTypeId { get; set; }
    [ExcelColumn("Mã số thuế", "Tax code", Order = 5)] public string? TaxCode { get; set; }
    [ExcelColumn("Địa chỉ", "Address", Order = 6, Width = 40)] public string? Address { get; set; }
    [ExcelColumn("Mã tỉnh/thành", "Province code", Order = 7), ExcelRef(typeof(Province))] public Guid? ProvinceId { get; set; }
    [ExcelColumn("Mã xã/phường", "Ward code", Order = 8), ExcelRef(typeof(Ward))] public Guid? WardId { get; set; }
    [ExcelColumn("Người liên hệ", "Contact person", Order = 9)] public string? ContactPerson { get; set; }
    [ExcelColumn("Điện thoại", "Phone", Order = 10)] public string? Phone { get; set; }
    [ExcelColumn("Email", "Email", Order = 11, Width = 24)] public string? Email { get; set; }
    [ExcelColumn("Mã đối tượng mặc định", "Default subject code", Order = 12), ExcelRef(typeof(SubjectGroup))] public Guid? DefaultSubjectGroupId { get; set; }
    [ExcelColumn("Số lao động", "Workers", Order = 13)] public int? WorkerCount { get; set; }
    [ExcelColumn("Đơn giá suất ăn (₫)", "Price per serving (VND)", Order = 14, Format = "#,##0")] public decimal? AgreedPricePerServing { get; set; }
    [ExcelColumn("Ca làm việc", "Working shifts", Order = 15)] public string? Shifts { get; set; }
    [ExcelColumn("Chất gây dị ứng cần tránh", "Allergens", Order = 16)] public string? Allergens { get; set; }
    [ExcelColumn("Ghi chú", "Note", Order = 17, Width = 30)] public string? Note { get; set; }
}

public sealed class CustomerUpsertValidator : CatalogUpsertValidator<CustomerUpsert>
{
    public CustomerUpsertValidator()
    {
        RuleFor(x => x.TaxCode).Matches(@"^\d{10}(-\d{3})?$").When(x => !string.IsNullOrEmpty(x.TaxCode));
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.WorkerCount).GreaterThanOrEqualTo(0).When(x => x.WorkerCount is not null);
        RuleFor(x => x.AgreedPricePerServing).GreaterThanOrEqualTo(0).When(x => x.AgreedPricePerServing is not null);
    }
}

/// <summary>Customers with data scope: CUSTOMER_REP users only see their own customers (SPEC §4, §10.2).</summary>
[Route("api/v1/customers"), PermissionModule("customer")]
public sealed class CustomersController : CatalogControllerBase<Customer, CustomerDto, CustomerUpsert>
{
    protected override string ExportTitle => L.Pick("Danh muc khach hang", "Customers");

    protected override IQueryable<Customer> BaseQuery(IQueryable<Customer> q)
    {
        var scope = CurrentUser.CustomerScope;
        return scope is null ? q : q.Where(c => scope.Contains(c.Id));
    }

    protected override IQueryable<Customer> ApplyFilters(IQueryable<Customer> q, IQueryCollection query)
        => Guid.TryParse(query["customerTypeId"], out var t) ? q.Where(x => x.CustomerTypeId == t) : q;

    public sealed record ContactUpsert(string FullName, string? Position, string? Phone, string? Email, bool IsPrimary, string? Note);
    public sealed record PriceUpsert(Guid? MealShiftId, Guid? SubjectGroupId, DateOnly ValidFrom, DateOnly? ValidTo, decimal PricePerServing, string? Note);

    private async Task EnsureScope(Guid id, CancellationToken ct)
    {
        if (!await BaseQuery(Db.Set<Customer>().AsNoTracking()).AnyAsync(c => c.Id == id, ct)) throw new NotFoundException("customer");
    }

    [HttpGet("{id:guid}/contacts"), HasPermission(Permissions.Customer.View)]
    public async Task<IActionResult> Contacts(Guid id, CancellationToken ct)
    {
        await EnsureScope(id, ct);
        var list = await Db.Set<CustomerContact>().AsNoTracking().Where(c => c.CustomerId == id).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.FullName).ToListAsync(ct);
        var mask = !CurrentUser.Has(Permissions.Pii.View);
        return Ok(list.Select(c => new { c.Id, c.FullName, c.Position, phone = mask ? Masking.Phone(c.Phone) : c.Phone, email = mask ? Masking.Email(c.Email) : c.Email, c.IsPrimary, c.Note }));
    }

    [HttpPost("{id:guid}/contacts"), HasPermission(Permissions.Customer.Edit)]
    public async Task<IActionResult> AddContact(Guid id, [FromBody] ContactUpsert req, CancellationToken ct)
    {
        await EnsureScope(id, ct);
        if (string.IsNullOrWhiteSpace(req.FullName)) throw new AppException("error.validation");
        var c = req.Adapt<CustomerContact>();
        c.CustomerId = id;
        Db.Set<CustomerContact>().Add(c);
        await Db.SaveChangesAsync(ct);
        return Ok(new { c.Id });
    }

    [HttpPut("{id:guid}/contacts/{contactId:guid}"), HasPermission(Permissions.Customer.Edit)]
    public async Task<IActionResult> UpdateContact(Guid id, Guid contactId, [FromBody] ContactUpsert req, CancellationToken ct)
    {
        await EnsureScope(id, ct);
        var c = await Db.Set<CustomerContact>().FirstOrDefaultAsync(x => x.Id == contactId && x.CustomerId == id, ct) ?? throw new NotFoundException("contact");
        var keepPhone = req.Phone?.Contains('*') == true; var keepEmail = req.Email?.Contains('*') == true;
        var (oldPhone, oldEmail) = (c.Phone, c.Email);
        req.Adapt(c);
        if (keepPhone) c.Phone = oldPhone;
        if (keepEmail) c.Email = oldEmail;
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/contacts/{contactId:guid}"), HasPermission(Permissions.Customer.Edit)]
    public async Task<IActionResult> DeleteContact(Guid id, Guid contactId, CancellationToken ct)
    {
        await EnsureScope(id, ct);
        var c = await Db.Set<CustomerContact>().FirstOrDefaultAsync(x => x.Id == contactId && x.CustomerId == id, ct) ?? throw new NotFoundException("contact");
        Db.Set<CustomerContact>().Remove(c);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/prices"), HasPermission(Permissions.Customer.View)]
    public async Task<IActionResult> Prices(Guid id, CancellationToken ct)
    {
        await EnsureScope(id, ct);
        return Ok(await (from p in Db.Set<CustomerPrice>().AsNoTracking()
                         where p.CustomerId == id
                         join s in Db.Set<MealShift>().AsNoTracking() on p.MealShiftId equals s.Id into sj
                         from s in sj.DefaultIfEmpty()
                         join g in Db.Set<SubjectGroup>().AsNoTracking() on p.SubjectGroupId equals g.Id into gj
                         from g in gj.DefaultIfEmpty()
                         orderby p.ValidFrom descending
                         select new { p.Id, p.MealShiftId, shift = s.NameVi, p.SubjectGroupId, subject = g.NameVi, p.ValidFrom, p.ValidTo, p.PricePerServing, p.Note }).ToListAsync(ct));
    }

    [HttpPost("{id:guid}/prices"), HasPermission(Permissions.Customer.Edit)]
    public async Task<IActionResult> AddPrice(Guid id, [FromBody] PriceUpsert req, CancellationToken ct)
    {
        await EnsureScope(id, ct);
        if (req.PricePerServing < 0 || (req.ValidTo is { } to && to < req.ValidFrom)) throw new AppException("error.validation");
        var p = req.Adapt<CustomerPrice>();
        p.CustomerId = id;
        Db.Set<CustomerPrice>().Add(p);
        // keep the "current" agreed price on the customer for quick display
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        if (req.ValidFrom <= today && (req.ValidTo is null || req.ValidTo >= today) && req.MealShiftId is null && req.SubjectGroupId is null)
            await Db.Set<Customer>().Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.AgreedPricePerServing, req.PricePerServing), ct);
        await Db.SaveChangesAsync(ct);
        return Ok(new { p.Id });
    }

    [HttpDelete("{id:guid}/prices/{priceId:guid}"), HasPermission(Permissions.Customer.Edit)]
    public async Task<IActionResult> DeletePrice(Guid id, Guid priceId, CancellationToken ct)
    {
        await EnsureScope(id, ct);
        var p = await Db.Set<CustomerPrice>().FirstOrDefaultAsync(x => x.Id == priceId && x.CustomerId == id, ct) ?? throw new NotFoundException("price");
        Db.Set<CustomerPrice>().Remove(p);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/orders"), HasPermission(Permissions.MealOrder.View)]
    public async Task<IActionResult> Orders(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        await EnsureScope(id, ct);
        var q = Db.Set<MealOrder>().AsNoTracking().Where(o => o.CustomerId == id);
        if (from is not null) q = q.Where(o => o.OrderDate >= from);
        if (to is not null) q = q.Where(o => o.OrderDate <= to);
        return Ok(await q.OrderByDescending(o => o.OrderDate).Take(500)
            .Select(o => new { o.Id, o.OrderDate, o.MealShiftId, o.RegisteredServings, o.ActualServings, status = o.Status.ToString(), o.MenuId }).ToListAsync(ct));
    }

    [HttpGet("{id:guid}/published-menus"), HasPermission(Permissions.Menu.View)]
    public async Task<IActionResult> PublishedMenus(Guid id, CancellationToken ct)
    {
        await EnsureScope(id, ct);
        return Ok(await Db.Set<Menu>().AsNoTracking().Where(m => m.CustomerId == id && m.Status == MenuStatus.Published).OrderByDescending(m => m.MenuDate).Take(100)
            .Select(m => new { m.Id, m.Code, m.MenuDate, shift = m.MealShift!.NameVi, m.Servings, m.EnergyKcal }).ToListAsync(ct));
    }
}
