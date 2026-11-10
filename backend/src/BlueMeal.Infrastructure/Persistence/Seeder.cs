using System.Text.Json;
using System.Text.RegularExpressions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlueMeal.Infrastructure.Persistence;

/// <summary>Idempotent reference-data seeding. Sample (non-official) data lives in <see cref="SampleDataSeeder"/>.</summary>
public sealed partial class Seeder(AppDbContext db, UserManager<AppUser> users, IConfiguration config, TimeProvider clock, ILogger<Seeder> log, SampleDataSeeder sample)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedPermissionsAndRolesAsync(ct);
        await SeedAdminAsync(ct);
        await SeedSettingsAsync(ct);
        await SeedOrganizationAsync(ct);
        await SeedVersionsAsync(ct);
        await SeedGeoAsync(ct);
        await SeedWardsAsync(ct);
        await SeedUnitsAsync(ct);
        await SeedNutrientsAsync(ct);
        await SeedFoodGroupsAsync(ct);
        await SeedReportsAsync(ct);
        await SeedMenuReferenceAsync(ct);
        if (config.GetValue("Seed:SampleData", true)) await sample.SeedAsync(ct);
        await SeedVddMockEndpointAsync(ct);
    }

    /// <summary>Dev/demo convenience: an integration endpoint pointing at tools/mock-vdd-api when Seed:VddMockUrl is set (never in production compose).</summary>
    private async Task SeedVddMockEndpointAsync(CancellationToken ct)
    {
        if (config["Seed:VddMockUrl"] is not { Length: > 0 } url || await db.Set<IntegrationEndpoint>().IgnoreQueryFilters().AnyAsync(ct)) return;
        db.Add(new IntegrationEndpoint
        {
            Code = "VDD-MOCK", NameVi = "Cổng dữ liệu Viện Dinh dưỡng (mock)", NameEn = "Nutrition institute portal (mock)", BaseUrl = url, AuthType = IntegrationAuthType.OAuth2ClientCredentials,
            Credentials = JsonSerializer.Serialize(new { clientId = "bluemeal", clientSecret = config["Seed:VddMockSecret"] ?? "demo-vdd-secret" }, Json), TimeoutSeconds = 15, SyncCron = "0 2 * * *", IsEnabled = true,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedPermissionsAndRolesAsync(CancellationToken ct)
    {
        var existing = await db.Set<Permission>().ToDictionaryAsync(p => p.Code, ct);
        var newCodes = new List<Permission>();
        foreach (var p in Permissions.All)
        {
            if (existing.TryGetValue(p.Code, out var e)) { e.NameVi = p.NameVi; e.NameEn = p.NameEn; e.Module = p.Module; e.Action = p.Action; e.SortOrder = p.SortOrder; existing.Remove(p.Code); }
            else { var np = new Permission { Code = p.Code, Module = p.Module, Action = p.Action, NameVi = p.NameVi, NameEn = p.NameEn, SortOrder = p.SortOrder }; db.Add(np); newCodes.Add(np); }
        }
        // permissions removed from code
        foreach (var stale in existing.Values)
        {
            await db.Set<RolePermission>().Where(rp => rp.PermissionId == stale.Id).ExecuteDeleteAsync(ct);
            db.Remove(stale);
        }
        await db.SaveChangesAsync(ct);

        foreach (var r in DefaultRoles.All)
        {
            var role = await db.Roles.Include(x => x.RolePermissions).FirstOrDefaultAsync(x => x.Name == r.Code, ct);
            var isNew = role is null;
            if (role is null)
            {
                role = new AppRole { Name = r.Code, NormalizedName = r.Code, NameVi = r.NameVi, NameEn = r.NameEn, Description = r.Description, IsSystem = true,
                    RequiresMfa = DefaultRoles.MfaMandatory.Contains(r.Code), ConcurrencyStamp = Guid.NewGuid().ToString() };
                db.Roles.Add(role);
            }
            // New role: grant all matching; existing role: grant only matching permissions that were just introduced.
            var candidates = isNew ? await db.Set<Permission>().ToListAsync(ct) : newCodes;
            foreach (var p in candidates.Where(p => r.Grants(p.Code)))
                if (!role.RolePermissions.Any(x => x.PermissionId == p.Id)) role.RolePermissions.Add(new RolePermission { PermissionId = p.Id });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        if (await users.Users.AnyAsync(ct)) return;
        var pwd = config["Seed:AdminPassword"] ?? "BlueMeal@2026!";
        var admin = new AppUser
        {
            UserName = "admin", FullName = "Quản trị hệ thống", Email = config["Seed:AdminEmail"] ?? "admin@bluemeal.local", EmailConfirmed = true,
            IsActive = true, MustChangePassword = true, MfaRequired = true, CreatedAt = clock.GetUtcNow(), PasswordChangedAt = clock.GetUtcNow(), LockoutEnabled = true,
        };
        var res = await users.CreateAsync(admin, pwd);
        if (!res.Succeeded) throw new InvalidOperationException("Cannot create admin: " + string.Join("; ", res.Errors.Select(e => e.Description)));
        var role = await db.Roles.FirstAsync(r => r.Name == DefaultRoles.SysAdmin, ct);
        db.Set<UserRole>().Add(new UserRole { UserId = admin.Id, RoleId = role.Id });
        await db.SaveChangesAsync(ct);
        log.LogWarning("Default admin account created: admin (must change password and enable MFA at first login)");
    }

    private async Task SeedSettingsAsync(CancellationToken ct)
    {
        var keys = await db.Set<AppSetting>().Select(s => s.Key).ToListAsync(ct);
        foreach (var (key, type) in SettingKeys.Types)
            if (!keys.Contains(key))
                db.Add(new AppSetting { Key = key, Value = JsonSerializer.Serialize(Activator.CreateInstance(type), type, Json), UpdatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);

        // Dev/test convenience: point SMTP at Mailpit when Seed:SmtpHost is configured (never in production compose).
        if (config["Seed:SmtpHost"] is { Length: > 0 } smtpHost)
        {
            var row = await db.Set<AppSetting>().FirstAsync(s => s.Key == SettingKeys.Smtp, ct);
            var smtp = JsonSerializer.Deserialize<SmtpSettings>(row.Value, Json) ?? new SmtpSettings();
            if (!smtp.Enabled)
            {
                smtp.Enabled = true; smtp.Host = smtpHost; smtp.Port = config.GetValue("Seed:SmtpPort", 1025); smtp.UseSsl = false;
                row.Value = JsonSerializer.Serialize(smtp, Json);
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task SeedOrganizationAsync(CancellationToken ct)
    {
        if (await db.Set<Organization>().AnyAsync(ct)) return;
        db.Add(new Organization
        {
            Name = "Viện Dinh dưỡng", NameEn = "National Institute of Nutrition", Address = "48B Tăng Bạt Hổ, Hai Bà Trưng, Hà Nội",
            Representative = "", RepresentativeTitle = "Viện trưởng", Website = "",
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Seeds system_version from CHANGELOG.md copied next to the binaries at build time (NFR-08).</summary>
    private async Task SeedVersionsAsync(CancellationToken ct)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        if (!File.Exists(path)) return;
        var text = await File.ReadAllTextAsync(path, ct);
        var matches = VersionHeader().Matches(text);
        var existing = await db.Set<SystemVersion>().ToDictionaryAsync(v => v.Version, ct);
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var start = m.Index + m.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var notes = text[start..end].Trim();
            var ver = m.Groups["v"].Value;
            var date = DateOnly.TryParse(m.Groups["d"].Value, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow);
            if (existing.TryGetValue(ver, out var e)) { e.NotesVi = notes; e.ReleaseDate = date; }
            else db.Add(new SystemVersion { Version = ver, ReleaseDate = date, NotesVi = notes });
        }
        await db.SaveChangesAsync(ct);
    }

    [GeneratedRegex(@"^##\s*\[(?<v>[^\]]+)\]\s*-\s*(?<d>\d{4}-\d{2}-\d{2})", RegexOptions.Multiline)]
    private static partial Regex VersionHeader();

    private async Task SeedGeoAsync(CancellationToken ct)
    {
        var vn = await db.Set<Country>().FirstOrDefaultAsync(c => c.Code == "VN", ct);
        if (vn is null)
        {
            vn = new Country { Code = "VN", NameVi = "Việt Nam", NameEn = "Viet Nam", Iso2 = "VN", SortOrder = 1 };
            db.Add(vn);
            var others = new (string Code, string Vi, string En)[] { ("LA", "Lào", "Laos"), ("KH", "Campuchia", "Cambodia"), ("CN", "Trung Quốc", "China"), ("TH", "Thái Lan", "Thailand"), ("JP", "Nhật Bản", "Japan"), ("KR", "Hàn Quốc", "Korea"), ("US", "Hoa Kỳ", "United States"), ("AU", "Úc", "Australia"), ("NZ", "New Zealand", "New Zealand") };
            var i = 2;
            foreach (var (c, v, e) in others) db.Add(new Country { Code = c, NameVi = v, NameEn = e, Iso2 = c, SortOrder = i++ });
            await db.SaveChangesAsync(ct);
        }
        if (await db.Set<Province>().AnyAsync(ct))
        {
            // Quảng Ninh, Bắc Ninh, Đồng Nai became centrally-run cities after 2025 (official DMDVHC list, ASSUMPTIONS A-13).
            // Only rows still carrying the original seeded name are updated; admin edits are kept.
            foreach (var (code, name) in new[] { ("22", "Quảng Ninh"), ("24", "Bắc Ninh"), ("75", "Đồng Nai") })
            {
                var p = await db.Set<Province>().FirstOrDefaultAsync(x => x.Code == code && x.NameVi == "Tỉnh " + name, ct);
                if (p is null) continue;
                p.NameVi = "Thành phố " + name; p.NameEn = name + " City"; p.Kind = "Thành phố";
            }
            await db.SaveChangesAsync(ct);
            return;
        }
        // 34 provincial-level units after the 2025 reorganization (Resolution 202/2025/QH15) — ASSUMPTIONS A-13.
        var provinces = new (string Code, string Name, string Kind)[]
        {
            ("01", "Hà Nội", "Thành phố"), ("04", "Cao Bằng", "Tỉnh"), ("08", "Tuyên Quang", "Tỉnh"), ("11", "Điện Biên", "Tỉnh"),
            ("12", "Lai Châu", "Tỉnh"), ("14", "Sơn La", "Tỉnh"), ("15", "Lào Cai", "Tỉnh"), ("19", "Thái Nguyên", "Tỉnh"),
            ("20", "Lạng Sơn", "Tỉnh"), ("22", "Quảng Ninh", "Thành phố"), ("24", "Bắc Ninh", "Thành phố"), ("25", "Phú Thọ", "Tỉnh"),
            ("31", "Hải Phòng", "Thành phố"), ("33", "Hưng Yên", "Tỉnh"), ("37", "Ninh Bình", "Tỉnh"), ("38", "Thanh Hóa", "Tỉnh"),
            ("40", "Nghệ An", "Tỉnh"), ("42", "Hà Tĩnh", "Tỉnh"), ("44", "Quảng Trị", "Tỉnh"), ("46", "Huế", "Thành phố"),
            ("48", "Đà Nẵng", "Thành phố"), ("51", "Quảng Ngãi", "Tỉnh"), ("52", "Gia Lai", "Tỉnh"), ("56", "Khánh Hòa", "Tỉnh"),
            ("66", "Đắk Lắk", "Tỉnh"), ("68", "Lâm Đồng", "Tỉnh"), ("75", "Đồng Nai", "Thành phố"), ("79", "Hồ Chí Minh", "Thành phố"),
            ("80", "Tây Ninh", "Tỉnh"), ("82", "Đồng Tháp", "Tỉnh"), ("86", "Vĩnh Long", "Tỉnh"), ("91", "An Giang", "Tỉnh"),
            ("92", "Cần Thơ", "Thành phố"), ("96", "Cà Mau", "Tỉnh"),
        };
        var order = 0;
        foreach (var (code, name, kind) in provinces)
            db.Add(new Province { Code = code, NameVi = (kind == "Thành phố" ? "Thành phố " : "Tỉnh ") + name, NameEn = (kind == "Thành phố" ? name + " City" : name + " Province"), Kind = kind, CountryId = vn.Id, SortOrder = ++order });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 3,321 commune-level units (2,599 xã, 709 phường, 13 đặc khu) from the official list of the Statistics Office
    /// (danhmuchanhchinh.nso.gov.vn, Decision 19/2025/QĐ-TTg) — SeedData/dvhc-wards.csv, ASSUMPTIONS A-13.
    /// Adds missing codes only: wards edited or deleted by an admin are never touched or re-created.
    /// </summary>
    private async Task SeedWardsAsync(CancellationToken ct)
    {
        var provinces = await db.Set<Province>().ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        var existing = (await db.Set<Ward>().IgnoreQueryFilters().Select(w => w.Code).ToListAsync(ct)).ToHashSet();
        await using var stream = typeof(Seeder).Assembly.GetManifestResourceStream("BlueMeal.Infrastructure.Persistence.SeedData.dvhc-wards.csv")
            ?? throw new InvalidOperationException("Embedded resource dvhc-wards.csv is missing");
        using var reader = new StreamReader(stream);
        await reader.ReadLineAsync(ct); // header: ma_tinh,ma_xa,ten_xa,loai
        var order = 0;
        var added = 0;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            order++;
            var f = line.Split(',');
            if (existing.Contains(f[1]) || !provinces.TryGetValue(f[0], out var provinceId)) continue;
            var suffix = f[3] switch { "Phường" => " Ward", "Đặc khu" => " Special Zone", _ => " Commune" };
            db.Add(new Ward { Code = f[1], NameVi = f[2], NameEn = ToAscii(f[2][(f[3].Length + 1)..]) + suffix, Kind = f[3], ProvinceId = provinceId, SortOrder = order });
            added++;
        }
        if (added == 0) return;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded {Count} commune-level administrative units (official list)", added);
    }

    /// <summary>"Đặc khu Côn Đảo" → "Dac khu Con Dao": diacritics removed, case kept (English display names).</summary>
    private static string ToAscii(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { 'đ' => 'd', 'Đ' => 'D', _ => c });
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private async Task SeedUnitsAsync(CancellationToken ct)
    {
        if (await db.Set<UnitOfMeasure>().AnyAsync(ct)) return;
        var units = new (string Code, string Vi, string En, UomType Type, decimal Factor, string Symbol)[]
        {
            ("G", "Gam", "Gram", UomType.Mass, 1m, "g"), ("KG", "Kilôgam", "Kilogram", UomType.Mass, 1000m, "kg"),
            ("MG", "Miligam", "Milligram", UomType.Mass, 0.001m, "mg"), ("UG", "Microgam", "Microgram", UomType.Mass, 0.000001m, "µg"),
            ("ML", "Mililít", "Millilitre", UomType.Volume, 1m, "ml"), ("L", "Lít", "Litre", UomType.Volume, 1000m, "l"),
            ("CAI", "Cái", "Piece", UomType.Count, 1m, "cái"), ("QUA", "Quả", "Fruit/egg", UomType.Count, 1m, "quả"),
            ("BO", "Bó", "Bunch", UomType.Count, 1m, "bó"), ("HOP", "Hộp", "Box", UomType.Count, 1m, "hộp"), ("GOI", "Gói", "Pack", UomType.Count, 1m, "gói"),
            ("KCAL", "Kilocalo", "Kilocalorie", UomType.Energy, 1m, "kcal"), ("KJ", "Kilojun", "Kilojoule", UomType.Energy, 0.239006m, "kJ"),
        };
        var i = 0;
        foreach (var u in units) db.Add(new UnitOfMeasure { Code = u.Code, NameVi = u.Vi, NameEn = u.En, Type = u.Type, FactorToBase = u.Factor, Symbol = u.Symbol, SortOrder = ++i });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedNutrientsAsync(CancellationToken ct)
    {
        if (await db.Set<Nutrient>().AnyAsync(ct)) return;
        var list = new (string Code, string Vi, string En, string Unit, NutrientGroup G, bool Core, int Dec)[]
        {
            ("ENERGY", "Năng lượng", "Energy", "kcal", NutrientGroup.Energy, true, 0),
            ("WATER", "Nước", "Water", "g", NutrientGroup.Macro, false, 1),
            ("PROTEIN", "Protein (chất đạm)", "Protein", "g", NutrientGroup.Macro, true, 1),
            ("LIPID", "Lipid (chất béo)", "Fat", "g", NutrientGroup.Macro, true, 1),
            ("GLUCID", "Glucid (carbohydrate)", "Carbohydrate", "g", NutrientGroup.Macro, true, 1),
            ("FIBER", "Chất xơ", "Dietary fibre", "g", NutrientGroup.Macro, true, 1),
            ("ASH", "Tro", "Ash", "g", NutrientGroup.Macro, false, 1),
            ("CA", "Canxi", "Calcium", "mg", NutrientGroup.Mineral, true, 2),
            ("P", "Phospho", "Phosphorus", "mg", NutrientGroup.Mineral, false, 2),
            ("FE", "Sắt", "Iron", "mg", NutrientGroup.Mineral, true, 2),
            ("ZN", "Kẽm", "Zinc", "mg", NutrientGroup.Mineral, false, 2),
            ("NA", "Natri", "Sodium", "mg", NutrientGroup.Mineral, true, 2),
            ("K", "Kali", "Potassium", "mg", NutrientGroup.Mineral, false, 2),
            ("MG", "Magie", "Magnesium", "mg", NutrientGroup.Mineral, false, 2),
            ("VIT_A", "Vitamin A", "Vitamin A", "µg", NutrientGroup.Vitamin, true, 2),
            ("BETA_CAROTENE", "Beta-caroten", "Beta-carotene", "µg", NutrientGroup.Vitamin, false, 2),
            ("VIT_B1", "Vitamin B1", "Thiamin", "mg", NutrientGroup.Vitamin, true, 2),
            ("VIT_B2", "Vitamin B2", "Riboflavin", "mg", NutrientGroup.Vitamin, false, 2),
            ("VIT_PP", "Vitamin PP", "Niacin", "mg", NutrientGroup.Vitamin, false, 2),
            ("VIT_C", "Vitamin C", "Vitamin C", "mg", NutrientGroup.Vitamin, true, 2),
            ("VIT_D", "Vitamin D", "Vitamin D", "µg", NutrientGroup.Vitamin, false, 2),
            ("CHOLESTEROL", "Cholesterol", "Cholesterol", "mg", NutrientGroup.Other, false, 2),
            ("SUGAR", "Đường tổng số", "Total sugars", "g", NutrientGroup.Other, false, 1),
            ("SFA", "Acid béo no", "Saturated fatty acids", "g", NutrientGroup.Other, false, 1),
        };
        var i = 0;
        foreach (var n in list) db.Add(new Nutrient { Code = n.Code, NameVi = n.Vi, NameEn = n.En, Unit = n.Unit, Group = n.G, IsCore = n.Core, Decimals = n.Dec, SortOrder = ++i });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedFoodGroupsAsync(CancellationToken ct)
    {
        if (await db.Set<FoodGroup>().AnyAsync(ct)) return;
        var groups = new (string Code, string Vi, string En, bool Animal)[]
        {
            ("NGU_COC", "Ngũ cốc và sản phẩm chế biến", "Cereals and products", false), ("KHOAI_CU", "Khoai củ và sản phẩm chế biến", "Starchy roots and tubers", false),
            ("HAT_DAM_BEO", "Hạt, quả giàu đạm và béo", "Nuts, seeds and legumes", false), ("RAU", "Rau, quả, củ dùng làm rau", "Vegetables", false),
            ("QUA_CHIN", "Quả chín", "Fruits", false), ("DAU_MO", "Dầu, mỡ, bơ", "Fats and oils", false),
            ("THIT", "Thịt và sản phẩm chế biến", "Meat and products", true), ("THUY_SAN", "Thủy sản và sản phẩm chế biến", "Fish and seafood", true),
            ("TRUNG", "Trứng và sản phẩm chế biến", "Eggs and products", true), ("SUA", "Sữa và sản phẩm chế biến", "Milk and products", true),
            ("DO_HOP", "Đồ hộp", "Canned foods", false), ("GIA_VI", "Gia vị, nước chấm", "Condiments and sauces", false),
            ("DO_NGOT", "Đồ ngọt (đường, bánh, mứt, kẹo)", "Sweets", false), ("NUOC_GIAI_KHAT", "Nước giải khát, bia, rượu", "Beverages", false),
        };
        var i = 0;
        foreach (var g in groups) db.Add(new FoodGroup { Code = g.Code, NameVi = g.Vi, NameEn = g.En, IsAnimalSource = g.Animal, SortOrder = ++i });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedReportsAsync(CancellationToken ct)
    {
        var groups = new (string Code, string Vi, string En)[] { ("KHO", "Kho", "Inventory"), ("DINH_DUONG", "Dinh dưỡng", "Nutrition"), ("THUC_DON", "Thực đơn", "Menus"), ("CHI_PHI", "Chi phí", "Cost"), ("ATTP", "An toàn thực phẩm", "Food safety"), ("HE_THONG", "Hệ thống", "System") };
        var map = new Dictionary<string, ReportGroup>();
        var gi = 0;
        foreach (var g in groups)
        {
            var e = await db.Set<ReportGroup>().FirstOrDefaultAsync(x => x.Code == g.Code, ct);
            if (e is null) { e = new ReportGroup { Code = g.Code, NameVi = g.Vi, NameEn = g.En, SortOrder = ++gi, RequiredPermission = g.Code == "HE_THONG" ? Permissions.Audit.View : null }; db.Add(e); }
            map[g.Code] = e;
        }
        var reports = new (string Code, string Vi, string En, string Group)[]
        {
            ("RPT-01", "Thống kê nhập/xuất thực phẩm", "Food receipts/issues statistics", "KHO"),
            ("RPT-02", "Nhập – xuất – tồn theo kỳ", "Stock movement summary", "KHO"),
            ("RPT-03", "Kiểm kê kho thực phẩm", "Stock take report", "KHO"),
            ("RPT-04", "Thẻ kho chi tiết theo nguyên liệu", "Stock card by ingredient", "KHO"),
            ("RPT-05", "Hàng sắp hết hạn / đã hết hạn / dưới tồn tối thiểu", "Expiring / expired / below-minimum stock", "KHO"),
            ("RPT-06", "Khuyến nghị dinh dưỡng – mức đáp ứng", "Nutrition recommendation compliance", "DINH_DUONG"),
            ("RPT-07", "Phân tích dinh dưỡng thực đơn", "Menu nutrition analysis", "DINH_DUONG"),
            ("RPT-08", "Thống kê cảnh báo chỉ tiêu", "Warning statistics", "DINH_DUONG"),
            ("RPT-09", "Tần suất sử dụng món ăn / nguyên liệu", "Dish / ingredient usage frequency", "THUC_DON"),
            ("RPT-10", "Thực đơn tuần/tháng (mẫu công bố)", "Weekly/monthly menu (published)", "THUC_DON"),
            ("RPT-11", "Chi phí suất ăn: kế hoạch và thực tế", "Meal cost: planned vs actual", "CHI_PHI"),
            ("RPT-12", "Biến động giá nguyên liệu", "Ingredient price changes", "CHI_PHI"),
            ("RPT-13", "Sổ kiểm thực 3 bước", "3-step food inspection log", "ATTP"),
            ("RPT-14", "Sổ lưu mẫu và hủy mẫu thức ăn", "Food sample retention log", "ATTP"),
            ("RPT-15", "Giấy chứng nhận nhà cung cấp sắp hết hạn", "Expiring supplier certificates", "ATTP"),
            ("RPT-16", "Nhật ký truy cập / thao tác hệ thống", "System access / activity log", "HE_THONG"),
        };
        var existing = await db.Set<ReportDefinition>().Select(r => r.Code).ToListAsync(ct);
        var ri = 0;
        foreach (var r in reports)
        {
            ri++;
            if (existing.Contains(r.Code)) continue;
            db.Add(new ReportDefinition { Code = r.Code, NameVi = r.Vi, NameEn = r.En, ReportGroup = map[r.Group], SortOrder = ri });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedMenuReferenceAsync(CancellationToken ct)
    {
        if (!await db.Set<MealShift>().AnyAsync(ct))
        {
            db.Add(new MealShift { Code = "CA1", NameVi = "Ca 1 (trưa)", NameEn = "Shift 1 (lunch)", ServeFrom = new TimeOnly(11, 0), ServeTo = new TimeOnly(12, 30), SortOrder = 1 });
            db.Add(new MealShift { Code = "CA2", NameVi = "Ca 2 (chiều)", NameEn = "Shift 2 (evening)", ServeFrom = new TimeOnly(17, 30), ServeTo = new TimeOnly(19, 0), SortOrder = 2 });
            db.Add(new MealShift { Code = "CA3", NameVi = "Ca 3 (đêm)", NameEn = "Shift 3 (night)", ServeFrom = new TimeOnly(0, 0), ServeTo = new TimeOnly(1, 0), SortOrder = 3 });
            db.Add(new MealShift { Code = "PHU", NameVi = "Bữa phụ", NameEn = "Snack", ServeFrom = new TimeOnly(15, 0), ServeTo = new TimeOnly(15, 30), SortOrder = 4 });
            await db.SaveChangesAsync(ct);
        }
        if (!await db.Set<MealStructureTemplate>().AnyAsync(ct))
        {
            var t = new MealStructureTemplate { Code = "CHUAN", NameVi = "Bữa ca chuẩn", NameEn = "Standard shift meal", Description = "1 món mặn + 1 món xào + 1 canh + 1 rau + cơm + tráng miệng", SortOrder = 1 };
            var slots = new (DishType T, bool Req)[] { (DishType.MainProtein, true), (DishType.StirFry, true), (DishType.Soup, true), (DishType.Vegetable, true), (DishType.Staple, true), (DishType.Dessert, false) };
            var i = 0;
            foreach (var s in slots) t.Slots.Add(new MealStructureSlot { DishType = s.T, Quantity = 1, IsRequired = s.Req, SortOrder = ++i });
            db.Add(t);
            var t2 = new MealStructureTemplate { Code = "TIET_KIEM", NameVi = "Bữa ca tiết kiệm", NameEn = "Economy shift meal", Description = "1 món mặn + 1 canh + 1 rau + cơm", SortOrder = 2 };
            i = 0;
            foreach (var s in new[] { DishType.MainProtein, DishType.Soup, DishType.Vegetable, DishType.Staple }) t2.Slots.Add(new MealStructureSlot { DishType = s, Quantity = 1, IsRequired = true, SortOrder = ++i });
            db.Add(t2);
            await db.SaveChangesAsync(ct);
        }
    }
}
