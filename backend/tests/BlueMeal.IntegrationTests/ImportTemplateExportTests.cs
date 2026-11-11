using System.Net;
using BlueMeal.IntegrationTests.Infrastructure;

namespace BlueMeal.IntegrationTests;

/// <summary>
/// Every catalog — and the recipe and opening-stock imports — serves an Excel import template. With BLUEMEAL_EXPORT_ARTIFACTS=1 the templates are also written to
/// tools/import-templates/ for the hand-over package (initial data conversion — SPEC §15), see <see cref="RepoArtifacts"/>.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class ImportTemplateExportTests(ApiFactory f)
{
    public static readonly string[] Catalogs =
    [
        "countries", "provinces", "wards", "units", "departments", "employees", "subject-groups", "partner-types", "partners", "customer-types", "customers",
        "warehouses", "food-groups", "nutrients", "foods", "food-safety-specs", "ingredients", "dishes", "recommendations", "report-groups", "meal-shifts", "meal-structures",
        "recipes", "stock-receipts", // document imports: recipe lines, opening stock
    ];

    [Fact]
    public async Task Import_templates_are_served_and_exported_to_tools()
    {
        var c = await f.AdminAsync();
        var dir = RepoArtifacts.Directory("tools", "import-templates");
        foreach (var catalog in Catalogs)
        {
            var r = await c.GetAsync($"/api/v1/{catalog}/import/template");
            r.StatusCode.Should().Be(HttpStatusCode.OK, catalog);
            var bytes = await r.Content.ReadAsByteArrayAsync();
            bytes.Take(2).Should().Equal((byte)'P', (byte)'K');
            if (dir is not null) await File.WriteAllBytesAsync(Path.Combine(dir, $"mau-nhap-{(catalog == "stock-receipts" ? "opening-stock" : catalog)}.xlsx"), bytes);
        }
    }
}
