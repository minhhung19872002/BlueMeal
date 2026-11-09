using System.Net;
using BlueMeal.IntegrationTests.Infrastructure;

namespace BlueMeal.IntegrationTests;

/// <summary>
/// Every catalog serves an Excel import template; the templates are written to tools/import-templates/ for the
/// hand-over package (initial data conversion — SPEC §15).
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class ImportTemplateExportTests(ApiFactory f)
{
    public static readonly string[] Catalogs =
    [
        "countries", "provinces", "wards", "units", "departments", "employees", "subject-groups", "partner-types", "partners", "customer-types", "customers",
        "warehouses", "food-groups", "nutrients", "foods", "food-safety-specs", "ingredients", "dishes", "recommendations", "report-groups", "meal-shifts", "meal-structures",
    ];

    [Fact]
    public async Task Import_templates_are_served_and_exported_to_tools()
    {
        var c = await f.AdminAsync();
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tools", "import-templates"));
        var writeFiles = Directory.Exists(Path.GetDirectoryName(dir));
        if (writeFiles) Directory.CreateDirectory(dir);
        foreach (var catalog in Catalogs)
        {
            var r = await c.GetAsync($"/api/v1/{catalog}/import/template");
            r.StatusCode.Should().Be(HttpStatusCode.OK, catalog);
            var bytes = await r.Content.ReadAsByteArrayAsync();
            bytes.Take(2).Should().Equal((byte)'P', (byte)'K');
            if (writeFiles) await File.WriteAllBytesAsync(Path.Combine(dir, $"mau-nhap-{catalog}.xlsx"), bytes);
        }
    }
}
