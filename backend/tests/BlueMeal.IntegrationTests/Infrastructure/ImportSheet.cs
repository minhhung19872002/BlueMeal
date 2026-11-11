using System.Net.Http.Headers;
using ClosedXML.Excel;

namespace BlueMeal.IntegrationTests.Infrastructure;

/// <summary>Builds an import file the way a user does: download the template, fill rows under its headers, upload it for preview.</summary>
public static class ImportSheet
{
    /// <summary>Fills the template of <paramref name="resource"/> (rows keyed by header text without the "*") and returns the xlsx bytes.</summary>
    public static async Task<byte[]> FromTemplateAsync(HttpClient c, string resource, IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var template = await c.GetAsync($"/api/v1/{resource}/import/template");
        template.EnsureSuccessStatusCode();
        using var wb = new XLWorkbook(await template.Content.ReadAsStreamAsync());
        var ws = wb.Worksheet(1);
        var headers = ws.Row(1).CellsUsed().ToDictionary(x => x.GetString().TrimEnd('*', ' '), x => x.Address.ColumnNumber);
        var r = 2;
        foreach (var row in rows)
        {
            foreach (var (header, value) in row)
            {
                if (!headers.TryGetValue(header, out var col)) throw new KeyNotFoundException($"Template of {resource} has no column '{header}' (has: {string.Join(", ", headers.Keys)})");
                ws.Cell(r, col).Value = value switch { null => Blank.Value, decimal d => d, int i => i, DateTime dt => dt, _ => value.ToString() };
            }
            r++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static Task<HttpResponseMessage> PreviewAsync(HttpClient c, string resource, byte[] xlsx, string query = "")
    {
        var file = new ByteArrayContent(xlsx);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return c.PostAsync($"/api/v1/{resource}/import/preview{query}", new MultipartFormDataContent { { file, "file", $"{resource}.xlsx" } });
    }
}
