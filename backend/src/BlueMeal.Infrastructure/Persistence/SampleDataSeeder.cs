using System.Text.Json;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlueMeal.Infrastructure.Persistence;

/// <summary>
/// Seeds clearly-labelled SAMPLE data (ASSUMPTIONS A-14): ~80 foods, 40 ingredients, 30 dishes with recipes,
/// 3 recommendations, warehouses, suppliers and customers. Every created id is stored in app_setting
/// "sample_data" so that it can be purged in one action (POST /api/v1/system/sample-data/purge).
/// </summary>
public sealed class SampleDataSeeder(AppDbContext db, TimeProvider clock, ILogger<SampleDataSeeder> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, List<Guid>> _ids = new();

    private void Track(string type, Guid id) => (_ids.TryGetValue(type, out var l) ? l : _ids[type] = []).Add(id);

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var settingRow = await db.Set<AppSetting>().FirstOrDefaultAsync(s => s.Key == SettingKeys.SampleData, ct);
        var current = settingRow is null ? new SampleDataSettings() : JsonSerializer.Deserialize<SampleDataSettings>(settingRow.Value, Json) ?? new SampleDataSettings();
        if (current.Loaded || current.Purged || await db.Set<Food>().IgnoreQueryFilters().AnyAsync(f => f.SourceVersion == SampleData.Version, ct)) return;

        var note = SampleData.Label + " Số liệu minh họa phục vụ demo/đào tạo, không phải số liệu chính thức. / Illustrative demo data, not official figures.";
        var groups = await db.Set<FoodGroup>().ToDictionaryAsync(g => g.Code, ct);
        var nutrients = await db.Set<Nutrient>().ToDictionaryAsync(n => n.Code, ct);
        var units = await db.Set<UnitOfMeasure>().ToDictionaryAsync(u => u.Code, ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().AddHours(7).DateTime);

        // ---- warehouses
        var whs = new Dictionary<string, Warehouse>
        {
            ["DRY"] = new() { Code = "KHO-KHO", NameVi = "Kho khô", NameEn = "Dry store", Type = WarehouseType.Dry, Location = "Tầng 1 – nhà bếp", TempMin = 15, TempMax = 30, Note = note, SortOrder = 1 },
            ["CHILL"] = new() { Code = "KHO-MAT", NameVi = "Kho mát", NameEn = "Chiller", Type = WarehouseType.Chilled, Location = "Tầng 1 – nhà bếp", TempMin = 0, TempMax = 5, Note = note, SortOrder = 2 },
            ["FROZEN"] = new() { Code = "KHO-DONG", NameVi = "Kho đông lạnh", NameEn = "Freezer", Type = WarehouseType.Frozen, Location = "Tầng 1 – nhà bếp", TempMin = -25, TempMax = -18, Note = note, SortOrder = 3 },
        };
        foreach (var w in whs.Values) { db.Add(w); Track(nameof(Warehouse), w.Id); }

        // ---- subject groups + recommendations
        var subjects = new Dictionary<LaborIntensity, SubjectGroup>();
        var i = 0;
        foreach (var r in SampleData.Recommendations)
        {
            var sg = new SubjectGroup
            {
                Code = r.Code.Replace("KN-", "DT-"), NameVi = r.Labor switch { LaborIntensity.Light => "Người lao động – lao động nhẹ", LaborIntensity.Heavy => "Người lao động – lao động nặng", _ => "Người lao động – lao động vừa" },
                NameEn = r.Labor switch { LaborIntensity.Light => "Workers – light work", LaborIntensity.Heavy => "Workers – heavy work", _ => "Workers – moderate work" },
                Gender = Gender.Common, AgeFrom = 18, AgeTo = 60, LaborIntensity = r.Labor, Note = note, SortOrder = ++i,
                SpecialConditions = r.Labor == LaborIntensity.Heavy ? "Nóng, nặng nhọc" : null,
            };
            db.Add(sg); Track(nameof(SubjectGroup), sg.Id);
            subjects[r.Labor] = sg;
            var rec = new Recommendation
            {
                Code = r.Code, NameVi = r.Vi, NameEn = r.En, SubjectGroupId = sg.Id, Reference = SampleData.Label + " Tham khảo minh họa — thay bằng khuyến nghị chính thức của Viện Dinh dưỡng",
                ReferenceYear = 2026, EnergyPerDayKcal = r.Kcal, MealSharePercent = 35, BudgetPerServing = r.Budget, Note = note, SortOrder = i,
            };
            var mealKcal = r.Kcal * 0.35m;
            void Target(string code, decimal? min, decimal? max, WarningLevel level) => rec.Nutrients.Add(new RecommendationNutrient { RecommendationId = rec.Id, NutrientId = nutrients[code].Id, MinPerMeal = min, MaxPerMeal = max, Level = level });
            Target("FIBER", 7, null, WarningLevel.Info);
            Target("CA", 170, null, WarningLevel.Warning);
            Target("FE", 4, null, WarningLevel.Warning);
            Target("VIT_C", 25, null, WarningLevel.Info);
            Target("NA", null, 800, WarningLevel.Warning);
            Target("LIPID", null, Math.Round(mealKcal * 0.25m / 9, 1) + 5, WarningLevel.Info);
            db.Add(rec); Track(nameof(Recommendation), rec.Id);
            sg.DefaultRecommendationId = rec.Id;
        }

        // ---- foods with nutrients per 100 g edible portion
        var foods = new Dictionary<string, Food>();
        i = 0;
        foreach (var f in SampleData.Foods)
        {
            var food = new Food
            {
                Code = f.Code, NameVi = f.Vi, NameEn = f.En, FoodGroupId = groups[f.Group].Id, RefuseRate = f.Refuse, Source = DataSource.Manual,
                SourceVersion = SampleData.Version, Note = note, SortOrder = ++i,
            };
            var energy = Math.Round(f.P * 4 + f.L * 9 + f.G * 4, 0);
            var water = Math.Max(0, Math.Round(100 - f.P - f.L - f.G - f.Fib - 1, 1));
            void N(string code, decimal v) { if (v > 0 || code is "ENERGY" or "PROTEIN" or "LIPID" or "GLUCID") food.Nutrients.Add(new FoodNutrient { FoodId = food.Id, NutrientId = nutrients[code].Id, ValuePer100gEdible = v }); }
            N("ENERGY", energy); N("WATER", water); N("PROTEIN", f.P); N("LIPID", f.L); N("GLUCID", f.G); N("FIBER", f.Fib);
            N("CA", f.Ca); N("FE", f.Fe); N("NA", f.Na); N("VIT_C", f.C); N("VIT_A", f.A); N("VIT_B1", f.B1);
            db.Add(food); Track(nameof(Food), food.Id);
            foods[f.Code] = food;
        }

        // ---- retention factors (illustrative): vitamin C/B1 losses by cooking method
        if (!await db.Set<NutrientRetentionFactor>().AnyAsync(ct))
        {
            foreach (var (m, c, b1) in new[] { (CookingMethod.Boil, 0.55m, 0.70m), (CookingMethod.StirFry, 0.70m, 0.80m), (CookingMethod.DeepFry, 0.60m, 0.75m), (CookingMethod.Braise, 0.50m, 0.65m), (CookingMethod.Steam, 0.75m, 0.85m), (CookingMethod.Simmer, 0.55m, 0.70m) })
            {
                var r1 = new NutrientRetentionFactor { NutrientId = nutrients["VIT_C"].Id, CookingMethod = m, Factor = c, Source = SampleData.Label };
                var r2 = new NutrientRetentionFactor { NutrientId = nutrients["VIT_B1"].Id, CookingMethod = m, Factor = b1, Source = SampleData.Label };
                db.AddRange(r1, r2); Track(nameof(NutrientRetentionFactor), r1.Id); Track(nameof(NutrientRetentionFactor), r2.Id);
            }
        }

        // ---- partners (suppliers) with certificates
        var supplierType = new PartnerType { Code = "NCC-TP", NameVi = "Nhà cung cấp thực phẩm", NameEn = "Food supplier", SortOrder = 1 };
        db.Add(supplierType); Track(nameof(PartnerType), supplierType.Id);
        var partnerSeeds = new (string Code, string Vi, string Groups, int CertDays)[]
        {
            ("NCC-RAU", "Hợp tác xã rau an toàn Đông Anh (mẫu)", "RAU,QUA_CHIN", 200),
            ("NCC-THIT", "Công ty TNHH Thực phẩm sạch Hà Nội (mẫu)", "THIT,TRUNG", 20),
            ("NCC-THUYSAN", "Công ty CP Thủy sản Bắc Bộ (mẫu)", "THUY_SAN", -5),
            ("NCC-KHO", "Đại lý gạo và gia vị Minh An (mẫu)", "NGU_COC,GIA_VI,DAU_MO,DO_NGOT", 365),
        };
        var partners = new Dictionary<string, Partner>();
        i = 0;
        foreach (var p in partnerSeeds)
        {
            var partner = new Partner { Code = p.Code, NameVi = p.Vi, PartnerTypeId = supplierType.Id, SuppliedGroups = p.Groups, ContactPerson = "Nguyễn Văn A", Phone = "0900000" + (100 + i), Address = "Hà Nội", Note = note, SortOrder = ++i };
            db.Add(partner); Track(nameof(Partner), partner.Id);
            partners[p.Code] = partner;
            var cert = new PartnerCertificate { PartnerId = partner.Id, CertificateType = "FoodSafetyFacility", Number = $"ATTP-{i:000}/2025", Issuer = "Sở Y tế (mẫu)", IssuedDate = today.AddYears(-2), ExpiryDate = today.AddDays(p.CertDays), Note = SampleData.Label };
            db.Add(cert); Track(nameof(PartnerCertificate), cert.Id);
        }

        // ---- ingredients
        var ingredients = new Dictionary<string, Ingredient>();
        i = 0;
        foreach (var g in SampleData.Ingredients)
        {
            var food = foods[g.Food];
            var preferred = SampleData.Foods.First(x => x.Code == g.Food).Group switch
            {
                "RAU" or "QUA_CHIN" => "NCC-RAU", "THIT" or "TRUNG" => "NCC-THIT", "THUY_SAN" => "NCC-THUYSAN", _ => "NCC-KHO",
            };
            var ing = new Ingredient
            {
                Code = g.Code, NameVi = g.Vi, NameEn = g.En, FoodId = food.Id, PurchaseUnitId = units[g.Unit].Id, GramsPerUnit = g.Grams, DefaultWarehouseId = whs[g.Wh].Id,
                MinStock = g.Min, ShelfLifeDays = g.Shelf, ReferencePrice = g.Price, PreferredPartnerId = partners[preferred].Id, Allergens = g.Allergens, Note = note, SortOrder = ++i,
                StorageCondition = g.Wh switch { "CHILL" => "0–5 °C", "FROZEN" => "≤ −18 °C", _ => "Khô ráo, thoáng mát" },
            };
            db.Add(ing); Track(nameof(Ingredient), ing.Id);
            ingredients[g.Code] = ing;
            var ph = new IngredientPriceHistory { IngredientId = ing.Id, EffectiveDate = today.AddDays(-30), UnitPrice = Math.Round(g.Price * 0.97m / 100m) * 100m, Source = PriceSource.Manual, CreatedAt = clock.GetUtcNow() };
            var ph2 = new IngredientPriceHistory { IngredientId = ing.Id, EffectiveDate = today, UnitPrice = g.Price, Source = PriceSource.Manual, CreatedAt = clock.GetUtcNow() };
            db.AddRange(ph, ph2); Track(nameof(IngredientPriceHistory), ph.Id); Track(nameof(IngredientPriceHistory), ph2.Id);
        }

        // ---- dishes with an active recipe each
        i = 0;
        foreach (var d in SampleData.Dishes)
        {
            var allergens = string.Join(',', d.Items.Select(x => ingredients[x.Ing].Allergens).Where(a => a is not null).Distinct());
            var dish = new Dish
            {
                Code = d.Code, NameVi = d.Vi, NameEn = d.En, DishType = d.Type, CookingMethod = d.Method, Description = note, SortOrder = ++i,
                Allergens = allergens.Length == 0 ? null : allergens, Tags = SampleData.Label,
                ServingWeightG = Math.Round(d.Items.Sum(x => x.G * (1 - SampleData.Foods.First(f => f.Code == SampleData.Ingredients.First(g => g.Code == x.Ing).Food).Refuse / 100m)), 0),
            };
            var recipe = new Recipe
            {
                DishId = dish.Id, VersionNo = 1, IsCurrent = true, BaseServings = 1, Status = RecipeStatus.Active, Instructions = SampleData.Label + " Sơ chế, chế biến theo quy trình bếp ăn tập thể.",
                PrepTimeMinutes = d.Type == DishType.Staple ? 40 : 30, CookingLossRate = d.Method switch { CookingMethod.DeepFry => 20, CookingMethod.Braise => 25, CookingMethod.Boil => 10, _ => 15 },
            };
            var order = 0;
            foreach (var (ing, g) in d.Items) recipe.Items.Add(new RecipeItem { RecipeId = recipe.Id, IngredientId = ingredients[ing].Id, GrossG = g, SortOrder = ++order });
            dish.Recipes.Add(recipe);
            db.Add(dish); Track(nameof(Dish), dish.Id); Track(nameof(Recipe), recipe.Id);
        }

        // ---- customers
        var custType = new CustomerType { Code = "KH-DN", NameVi = "Doanh nghiệp sản xuất", NameEn = "Manufacturing company", SortOrder = 1 };
        db.Add(custType); Track(nameof(CustomerType), custType.Id);
        var customers = new (string Code, string Vi, LaborIntensity Labor, int Workers, decimal Price)[]
        {
            ("KH-MAYXK", "Công ty May xuất khẩu Thăng Long (mẫu)", LaborIntensity.Light, 850, 32000),
            ("KH-COKHI", "Công ty Cơ khí chính xác Hà Nội (mẫu)", LaborIntensity.Moderate, 420, 37000),
            ("KH-THEP", "Nhà máy Cán thép Bắc Ninh (mẫu)", LaborIntensity.Heavy, 600, 42000),
        };
        i = 0;
        foreach (var c in customers)
        {
            var cust = new Customer
            {
                Code = c.Code, NameVi = c.Vi, CustomerTypeId = custType.Id, DefaultSubjectGroupId = subjects[c.Labor].Id, WorkerCount = c.Workers, AgreedPricePerServing = c.Price,
                Shifts = "Ca 1, Ca 2", ContactPerson = "Phòng Hành chính", Address = "Hà Nội", Note = note, SortOrder = ++i,
            };
            db.Add(cust); Track(nameof(Customer), cust.Id);
            var price = new CustomerPrice { CustomerId = cust.Id, ValidFrom = today.AddMonths(-1), PricePerServing = c.Price, Note = SampleData.Label };
            db.Add(price); Track(nameof(CustomerPrice), price.Id);
        }

        await db.SaveChangesAsync(ct);

        // record ids for quick purge
        var setting = new SampleDataSettings { Loaded = true, Ids = _ids };
        if (settingRow is null) db.Add(settingRow = new AppSetting { Key = SettingKeys.SampleData });
        settingRow.Value = JsonSerializer.Serialize(setting, Json);
        settingRow.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        log.LogWarning("Seeded SAMPLE data ({Foods} foods, {Ingredients} ingredients, {Dishes} dishes) — labelled [DỮ LIỆU MẪU], not official figures.",
            SampleData.Foods.Length, SampleData.Ingredients.Length, SampleData.Dishes.Length);
    }
}
