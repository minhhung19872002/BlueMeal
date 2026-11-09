using System.Linq.Expressions;
using System.Reflection;
using BlueMeal.Application.Abstractions;
using BlueMeal.Domain.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueMeal.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid, IdentityUserClaim<Guid>, UserRole, IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>(options),
      IAppDbContext
{
    public const string Schema = "app";

    DbSet<T> IAppDbContext.Set<T>() => base.Set<T>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(18, 4);
        b.Properties<decimal?>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);
        b.HasPostgresExtension("pgcrypto");
        b.HasPostgresExtension("unaccent");
        b.HasPostgresExtension("pg_trgm");

        ConfigureIdentity(b);

        // Register every domain entity type
        var domainTypes = typeof(Entity).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Namespace == "BlueMeal.Domain.Entities"
                        && (typeof(Entity).IsAssignableFrom(t) || t == typeof(AuditLog) || t == typeof(RolePermission)))
            .ToList();
        foreach (var t in domainTypes) b.Entity(t);

        foreach (var et in b.Model.GetEntityTypes().ToList())
        {
            var clr = et.ClrType;
            var eb = b.Entity(clr);

            // Ids are generated client-side (SequentialGuid); tell EF so that new children added to tracked
            // collections are INSERTed instead of being treated as existing rows.
            if (typeof(Entity).IsAssignableFrom(clr))
                eb.Property(nameof(Entity.Id)).ValueGeneratedNever();

            if (typeof(IVersioned).IsAssignableFrom(clr))
                eb.Property(nameof(IVersioned.Version)).IsRowVersion();

            if (typeof(ISoftDelete).IsAssignableFrom(clr) && et.BaseType is null)
                eb.HasQueryFilter(SoftDeleteFilter(clr));

            if (typeof(ICatalog).IsAssignableFrom(clr))
            {
                eb.HasIndex(nameof(ICatalog.Code)).IsUnique().HasFilter("is_deleted = false");
                eb.Property(nameof(ICatalog.Code)).HasMaxLength(50).IsRequired();
                eb.Property(nameof(ICatalog.NameVi)).HasMaxLength(300).IsRequired();
                eb.Property(nameof(ICatalog.NameEn)).HasMaxLength(300);
                eb.Property(nameof(ICatalog.SearchText)).HasMaxLength(1000);
                eb.HasIndex(nameof(ICatalog.SearchText)).HasMethod("gin").HasOperators("gin_trgm_ops");
            }
            if (typeof(IHasSearchText).IsAssignableFrom(clr))
                eb.HasIndex(nameof(IHasSearchText.SearchText)).HasMethod("gin").HasOperators("gin_trgm_ops");

            // Column-level encryption (SPEC §10.4)
            foreach (var p in clr.GetProperties().Where(p => p.GetCustomAttribute<EncryptedAttribute>() != null))
                eb.Property(p.Name).HasConversion(EncryptedStringConverter.Instance).HasMaxLength(2000);
        }

        ConfigureSpecifics(b);
    }

    private static LambdaExpression SoftDeleteFilter(Type t)
    {
        var p = Expression.Parameter(t, "e");
        var body = Expression.Equal(Expression.Property(p, nameof(ISoftDelete.IsDeleted)), Expression.Constant(false));
        return Expression.Lambda(body, p);
    }

    private static void ConfigureIdentity(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.ToTable("app_user");
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Language).HasMaxLength(5);
            e.Property(x => x.Theme).HasMaxLength(10);
            e.HasMany(x => x.UserRoles).WithOne(x => x.User).HasForeignKey(x => x.UserId).IsRequired();
            e.HasQueryFilter(x => !x.IsDeleted);
            e.Property(x => x.MfaSecret).HasConversion(EncryptedStringConverter.Instance).HasMaxLength(2000);
        });
        b.Entity<AppRole>(e =>
        {
            e.ToTable("app_role");
            e.Property(x => x.NameVi).HasMaxLength(200).IsRequired();
            e.HasMany<UserRole>().WithOne(x => x.Role).HasForeignKey(x => x.RoleId).IsRequired();
            e.HasMany(x => x.RolePermissions).WithOne(x => x.Role).HasForeignKey(x => x.RoleId);
        });
        b.Entity<UserRole>(e => { e.ToTable("user_role"); e.HasQueryFilter(x => !x.User!.IsDeleted); });
        b.Entity<IdentityUserClaim<Guid>>().ToTable("user_claim");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("user_login");
        b.Entity<IdentityUserToken<Guid>>().ToTable("user_token");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claim");
    }

    private static void ConfigureSpecifics(ModelBuilder b)
    {
        b.Entity<Permission>(e => { e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(100); });
        b.Entity<RolePermission>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId);
        });
        b.Entity<UserDataScope>().HasIndex(x => new { x.UserId, x.ScopeType, x.ScopeId }).IsUnique();
        b.Entity<UserSession>(e =>
        {
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            e.HasIndex(x => x.PreviousTokenHash);
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
        });
        b.Entity<LoginHistory>().HasIndex(x => new { x.UserId, x.At });
        b.Entity<MfaRecoveryCode>().HasIndex(x => x.UserId);
        b.Entity<PasswordHistory>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<OneTimeToken>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<SystemVersion>().HasIndex(x => x.Version).IsUnique();
        b.Entity<AppSetting>(e => { e.HasIndex(x => x.Key).IsUnique(); e.Property(x => x.Value).HasColumnType("jsonb"); e.Property(x => x.Key).HasMaxLength(100); });
        b.Entity<UserPreference>(e => { e.HasIndex(x => new { x.UserId, x.Key }).IsUnique(); e.Property(x => x.Value).HasColumnType("jsonb"); });
        b.Entity<Notification>().HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
        b.Entity<ApiClient>().HasIndex(x => x.ClientId).IsUnique();

        // Append-only audit log: table is created by raw SQL in migration as a monthly-partitioned table (A-33).
        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_log", t => t.ExcludeFromMigrations());
            e.HasKey(x => new { x.Id, x.At });
            e.Property(x => x.OldValues).HasColumnType("jsonb");
            e.Property(x => x.NewValues).HasColumnType("jsonb");
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.UserAgent).HasMaxLength(500);
        });

        b.Entity<Department>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Province>().HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId);
        b.Entity<Ward>().HasOne(x => x.Province).WithMany().HasForeignKey(x => x.ProvinceId);
        b.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique().HasFilter("is_deleted = false");
            e.HasIndex(x => x.PhoneHash); e.HasIndex(x => x.EmailHash); e.HasIndex(x => x.NationalIdHash);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Partner>().HasOne(x => x.PartnerType).WithMany().HasForeignKey(x => x.PartnerTypeId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<PartnerContract>(e =>
        {
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId);
            e.HasMany(x => x.Prices).WithOne().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PartnerId, x.ContractNo });
        });
        b.Entity<PartnerCertificate>(e => { e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId); e.HasIndex(x => x.ExpiryDate); });
        b.Entity<PartnerRating>().HasIndex(x => x.PartnerId);
        b.Entity<Customer>().HasOne(x => x.CustomerType).WithMany().HasForeignKey(x => x.CustomerTypeId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<CustomerContact>().HasIndex(x => x.CustomerId);
        b.Entity<CustomerPrice>().HasIndex(x => new { x.CustomerId, x.ValidFrom });

        b.Entity<FoodGroup>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Food>(e =>
        {
            e.HasOne(x => x.FoodGroup).WithMany().HasForeignKey(x => x.FoodGroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Nutrients).WithOne().HasForeignKey(x => x.FoodId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FoodNutrient>(e =>
        {
            e.HasIndex(x => new { x.FoodId, x.NutrientId }).IsUnique();
            e.HasOne(x => x.Nutrient).WithMany().HasForeignKey(x => x.NutrientId);
        });
        b.Entity<FoodSafetySpec>(e => { e.HasIndex(x => x.FoodId); e.HasIndex(x => x.IngredientId); });
        b.Entity<Ingredient>(e =>
        {
            e.HasOne(x => x.Food).WithMany().HasForeignKey(x => x.FoodId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.PurchaseUnit).WithMany().HasForeignKey(x => x.PurchaseUnitId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Barcode);
        });
        b.Entity<IngredientPriceHistory>().HasIndex(x => new { x.IngredientId, x.EffectiveDate });
        b.Entity<Dish>().HasMany(x => x.Recipes).WithOne(x => x.Dish).HasForeignKey(x => x.DishId);
        b.Entity<Recipe>(e =>
        {
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DishId, x.VersionNo }).IsUnique();
            e.Property(x => x.Instructions).HasMaxLength(8000);
        });
        b.Entity<RecipeItem>().HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<NutrientRetentionFactor>().HasIndex(x => new { x.NutrientId, x.CookingMethod }).IsUnique();
        b.Entity<Recommendation>(e =>
        {
            e.HasOne(x => x.SubjectGroup).WithMany().HasForeignKey(x => x.SubjectGroupId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Nutrients).WithOne().HasForeignKey(x => x.RecommendationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RecommendationNutrient>(e =>
        {
            e.HasIndex(x => new { x.RecommendationId, x.NutrientId }).IsUnique();
            e.HasOne(x => x.Nutrient).WithMany().HasForeignKey(x => x.NutrientId);
        });
        b.Entity<ReportGroup>().HasMany(x => x.Reports).WithOne(x => x.ReportGroup).HasForeignKey(x => x.ReportGroupId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<ReportDefinition>().HasIndex(x => x.Code).IsUnique();

        b.Entity<MealStructureTemplate>().HasMany(x => x.Slots).WithOne().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MenuPlan>().HasIndex(x => new { x.FromDate, x.ToDate });
        b.Entity<Menu>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.MenuDate, x.MealShiftId, x.SubjectGroupId, x.CustomerId });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.PlanId);
            e.HasOne(x => x.MealShift).WithMany().HasForeignKey(x => x.MealShiftId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SubjectGroup).WithMany().HasForeignKey(x => x.SubjectGroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.MenuId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.Property(x => x.GeneratorExplanation).HasMaxLength(4000);
        });
        b.Entity<MenuItem>().HasOne(x => x.Dish).WithMany().HasForeignKey(x => x.DishId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MenuApproval>().HasIndex(x => new { x.MenuId, x.At });
        b.Entity<MealOrder>(e =>
        {
            e.HasIndex(x => new { x.CustomerId, x.OrderDate, x.MealShiftId });
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
        });
        b.Entity<Alert>(e =>
        {
            e.HasIndex(x => new { x.Status, x.Level, x.CreatedAt });
            e.HasIndex(x => x.DedupeKey);
            e.Property(x => x.MessageVi).HasMaxLength(1000); e.Property(x => x.MessageEn).HasMaxLength(1000);
        });

        b.Entity<StockReceipt>(e =>
        {
            e.HasIndex(x => x.DocNo).IsUnique();
            e.HasIndex(x => new { x.WarehouseId, x.DocDate });
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<StockReceiptLine>().HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockIssue>(e =>
        {
            e.HasIndex(x => x.DocNo).IsUnique();
            e.HasIndex(x => new { x.WarehouseId, x.DocDate });
            e.HasIndex(x => x.MenuId);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.IssueId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<StockIssueLine>().HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockTake>(e =>
        {
            e.HasIndex(x => x.DocNo).IsUnique();
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StockTakeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<StockTakeLine>(e => { e.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict); e.Ignore(x => x.Difference); });
        b.Entity<StockLedger>(e =>
        {
            e.HasIndex(x => new { x.WarehouseId, x.IngredientId, x.At });
            e.HasIndex(x => new { x.DocType, x.DocId });
            e.HasIndex(x => x.DocDate);
        });
        b.Entity<StockBalance>(e =>
        {
            e.HasIndex(x => new { x.WarehouseId, x.IngredientId }).IsUnique();
            e.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId);
        });
        b.Entity<StockLot>(e =>
        {
            e.HasIndex(x => new { x.WarehouseId, x.IngredientId, x.ExpiryDate });
            e.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId);
        });
        b.Entity<StockPeriodLock>().HasIndex(x => new { x.WarehouseId, x.Period }).IsUnique();
        b.Entity<PurchaseRequest>(e =>
        {
            e.HasIndex(x => x.DocNo).IsUnique();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.MenuIds).HasMaxLength(4000);
        });
        b.Entity<PurchaseRequestLine>().HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<FoodInspectionStep1>(e => { e.HasIndex(x => x.InspectedAt); e.HasIndex(x => x.ReceiptId); e.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId); e.Property(x => x.Criteria).HasColumnType("jsonb"); });
        b.Entity<FoodInspectionStep2>(e => { e.HasIndex(x => x.InspectDate); e.HasOne(x => x.Dish).WithMany().HasForeignKey(x => x.DishId); e.Property(x => x.Criteria).HasColumnType("jsonb"); });
        b.Entity<FoodInspectionStep3>(e => { e.HasIndex(x => x.InspectDate); e.HasOne(x => x.Dish).WithMany().HasForeignKey(x => x.DishId); e.Property(x => x.Criteria).HasColumnType("jsonb"); });
        b.Entity<FoodSample>(e => { e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => new { x.DisposedAt, x.PlannedDisposeAt }); e.HasOne(x => x.Dish).WithMany().HasForeignKey(x => x.DishId); });

        b.Entity<IntegrationEndpoint>(e => { e.Property(x => x.MappingJson).HasMaxLength(20000); });
        b.Entity<IntegrationLog>(e => { e.HasIndex(x => x.StartedAt); e.Property(x => x.RequestSummary).HasMaxLength(4000); e.Property(x => x.ResponseSummary).HasMaxLength(4000); e.Property(x => x.Error).HasMaxLength(4000); });
        b.Entity<SyncConflict>(e => { e.HasIndex(x => new { x.Status, x.DetectedAt }); e.Property(x => x.LocalJson).HasColumnType("jsonb"); e.Property(x => x.RemoteJson).HasColumnType("jsonb"); });
        b.Entity<BackupJob>(e => { e.HasIndex(x => x.RequestedAt); e.Property(x => x.Error).HasMaxLength(4000); });
        b.Entity<ExportJob>(e => { e.HasIndex(x => x.DownloadToken); e.HasIndex(x => new { x.RequestedBy, x.RequestedAt }); e.Property(x => x.Parameters).HasColumnType("jsonb"); e.Property(x => x.Error).HasMaxLength(4000); });
        b.Entity<FileAttachment>().HasIndex(x => new { x.OwnerType, x.OwnerId });
        b.Entity<DocCounter>(e => { e.ToTable("doc_counter"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(50); });
    }
}

/// <summary>Atomic counters for document numbers (PN-YYYYMMDD-####).</summary>
public class DocCounter
{
    public string Key { get; set; } = default!;
    public long Value { get; set; }
}

public static class EntityTypeBuilderExtensions
{
    public static EntityTypeBuilder<T> Json<T>(this EntityTypeBuilder<T> e, Expression<Func<T, string?>> prop) where T : class
    {
        e.Property(prop).HasColumnType("jsonb");
        return e;
    }
}
