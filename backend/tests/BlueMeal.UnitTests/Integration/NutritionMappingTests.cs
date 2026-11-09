using System.Text.Json;
using BlueMeal.Application.Integration;
using FluentAssertions;
using Xunit;

namespace BlueMeal.UnitTests.Integration;

/// <summary>Mapping layer (SPEC §7.10.3): field paths, code aliases, unit conversion, hashing — no HTTP involved.</summary>
public class NutritionMappingTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    private static readonly Dictionary<string, string> Units = new(StringComparer.OrdinalIgnoreCase) { ["ENERGY"] = "kcal", ["PROTEIN"] = "g", ["NA"] = "mg", ["VIT_A"] = "µg", ["CA"] = "mg" };

    [Fact]
    public void Default_mapping_reads_foods_with_code_aliases_and_unit_conversion()
    {
        var root = J("""
        { "data": [ { "food_code": "VDD-1", "name_vi": "Nước mắm", "name_en": "Fish sauce", "group_code": "GIA_VI", "refuse_pct": 0, "version": 3, "updated_at": "2026-05-01T10:00:00Z",
                      "composition": [ { "nutrient_code": "ENERC_KCAL", "value": 35, "unit": "kcal" }, { "nutrient_code": "NA", "value": 7.72, "unit": "g" },
                                       { "nutrient_code": "PROCNT", "value": "5.1", "unit": "g" }, { "nutrient_code": "CA", "value": null, "unit": "mg" } ] } ],
          "page": 1, "page_size": 50, "total": 17 }
        """);
        var page = NutritionMapper.Foods(root, new MappingConfig(), Units, 1, 50);
        page.TotalItems.Should().Be(17);
        var f = page.Items.Should().ContainSingle().Subject;
        f.Code.Should().Be("VDD-1");
        f.GroupCode.Should().Be("GIA_VI");
        f.Version.Should().Be("3");
        f.ModifiedAt.Should().Be(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));
        f.Nutrients["ENERGY"].Should().Be(35);
        f.Nutrients["NA"].Should().Be(7720);      // 7,72 g → 7.720 mg
        f.Nutrients["PROTEIN"].Should().Be(5.1m); // numeric string accepted
        f.Nutrients.Should().NotContainKey("CA");  // null value skipped
    }

    [Fact]
    public void Custom_mapping_changes_paths_and_prefix_without_code_changes()
    {
        var m = MappingConfig.Parse("""
        { "itemsPath": "result.items", "totalPath": "result.count", "codePrefix": "VN-",
          "food": { "code": "id", "nameVi": "ten", "groupCode": "nhom", "composition": "tp", "nutrientCode": "ma", "value": "gt", "unit": "dv" },
          "nutrientCodes": { "E": "ENERGY" }, "foodGroupCodes": { "N1": "NGU_COC" } }
        """);
        var root = J("""{ "result": { "count": 1, "items": [ { "id": "001", "ten": "Gạo", "nhom": "N1", "tp": [ { "ma": "E", "gt": 1440, "dv": "kJ" } ] } ] } }""");
        var f = NutritionMapper.Foods(root, m, Units, 1, 10).Items.Single();
        f.Code.Should().Be("VN-001");
        f.GroupCode.Should().Be("VN-NGU_COC");
        f.Nutrients["ENERGY"].Should().Be(344.1686m); // 1440 kJ × 0,239006
    }

    [Fact]
    public void Missing_required_field_or_unknown_unit_raise_mapping_errors()
    {
        var noCode = J("""{ "data": [ { "name_vi": "x", "group_code": "G" } ] }""");
        var act = () => NutritionMapper.Foods(noCode, new MappingConfig(), Units, 1, 10);
        act.Should().Throw<MappingException>().Which.Field.Should().Be("food.code");
        var badUnit = J("""{ "data": [ { "food_code": "A", "name_vi": "x", "group_code": "G", "composition": [ { "nutrient_code": "NA", "value": 1, "unit": "oz" } ] } ] }""");
        var act2 = () => NutritionMapper.Foods(badUnit, new MappingConfig(), Units, 1, 10);
        act2.Should().Throw<MappingException>().Which.Field.Should().Be("unit oz>mg");
    }

    [Fact]
    public void Food_groups_nutrients_and_recommendations()
    {
        var groups = NutritionMapper.FoodGroups(J("""{ "data": [ { "group_code": "THIT", "name_vi": "Thịt", "animal_source": true, "parent_code": null } ] }"""), new MappingConfig());
        groups.Single().IsAnimalSource.Should().BeTrue();
        var nutrients = NutritionMapper.Nutrients(J("""{ "data": [ { "nutrient_code": "VITC", "name_vi": "Vitamin C", "unit": "mg", "group": "Vitamin" } ] }"""), new MappingConfig());
        nutrients.Single().Code.Should().Be("VIT_C");
        var recs = NutritionMapper.Recommendations(J("""
        { "data": [ { "rec_code": "R1", "name_vi": "Nam lao động vừa", "energy_kcal_day": 2570, "meal_share_pct": 35, "protein_pct": [13, 20], "lipid_pct": { "min": 18, "max": 25 },
                      "nutrients": [ { "nutrient_code": "CA", "min": 800 }, { "nutrient_code": "NA", "max": 2000 } ] } ] }
        """), new MappingConfig());
        var r = recs.Single();
        r.EnergyPerDayKcal.Should().Be(2570);
        (r.ProteinPctMin, r.ProteinPctMax, r.LipidPctMin, r.GlucidPctMin).Should().Be((13m, 20m, 18m, 55m));
        r.Nutrients.Should().HaveCount(2);
        r.Nutrients[1].Max.Should().Be(2000);
    }

    [Fact]
    public void Hash_ignores_modified_time_and_dictionary_order()
    {
        var a = new ExtFood("A", "x", null, "G", 0, new Dictionary<string, decimal> { ["ENERGY"] = 1, ["PROTEIN"] = 2.50m }, "1", DateTimeOffset.UtcNow);
        var b = a with { Nutrients = new Dictionary<string, decimal> { ["PROTEIN"] = 2.5m, ["ENERGY"] = 1 }, ModifiedAt = null };
        NutritionMapper.Hash(a).Should().Be(NutritionMapper.Hash(b));
        NutritionMapper.Hash(a with { NameVi = "y" }).Should().NotBe(NutritionMapper.Hash(a));
    }

    [Fact]
    public void Default_mapping_json_round_trips()
    {
        var m = MappingConfig.Parse(MappingConfig.DefaultJson);
        m.Paths["foods"].Should().Be("/api/v1/foods");
        m.NutrientCodes["procnt"].Should().Be("PROTEIN");
        MappingConfig.Parse(null).ItemsPath.Should().Be("data");
    }
}
