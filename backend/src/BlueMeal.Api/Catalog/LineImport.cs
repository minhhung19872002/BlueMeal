using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using BlueMeal.Infrastructure.Excel;

namespace BlueMeal.Api.Catalog;

/// <summary>Column of a document-line import sheet (one Excel row = one line of a recipe / receipt).</summary>
public sealed record LineImportColumn(string Key, string HeaderVi, string HeaderEn, bool Required = false, double Width = 18, string? Format = null, string? HintVi = null, string? HintEn = null)
{
    public string Header => L.Pick(HeaderVi, HeaderEn);
}

/// <summary>Commit of a previewed line import: the preview token, and whether to import the valid documents when some rows have errors.</summary>
public sealed record LineImportCommitRequest(string Token, bool SkipErrors = true);

/// <summary>One parsed sheet row: values by column key, plus the errors collected while reading it.</summary>
public sealed class LineImportRow(int rowNumber, Dictionary<string, string?> values)
{
    public int RowNumber { get; } = rowNumber;
    public List<string> Errors { get; } = [];

    public string? Text(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    /// <summary>Parses a typed value; on a bad value records "invalid" and returns default.</summary>
    public T? Get<T>(LineImportColumn col)
    {
        var raw = Text(col.Key);
        if (raw is null) return default;
        try { return (T?)ImportValueParser.Parse(raw, typeof(T)); }
        catch (Exception e) when (e is FormatException or OverflowException) { Errors.Add(L.T("import.invalid", col.Header, raw)); return default; }
    }

    /// <summary>Parses a number column; records "invalid" (and returns null) when it is not a number or outside the allowed range.</summary>
    public decimal? Number(LineImportColumn col, Func<decimal, bool> valid)
    {
        var v = Get<decimal?>(col);
        if (v is { } d && !valid(d)) { Errors.Add(L.T("import.invalid", col.Header, Text(col.Key)!)); return null; }
        return v;
    }
}

/// <summary>
/// Excel import for documents made of lines (recipes, opening stock) — the same template → preview → commit flow as
/// catalogs (<see cref="CatalogControllerBase{TEntity, TDto, TUpsert}"/>), but rows are grouped into documents by the caller.
/// </summary>
public static class LineImport
{
    public const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private sealed record CacheEntry(Guid UserId, string Kind, string Content, string? Options);

    public static byte[] Template(IExcelService excel, IReadOnlyList<LineImportColumn> cols, IEnumerable<string> extraNotes, IEnumerable<IReadOnlyDictionary<string, object?>>? sample = null)
    {
        var notes = new List<string> { L.Pick("Cột có dấu * là bắt buộc. Mỗi dòng là một dòng chi tiết.", "Columns marked * are required. Each row is one detail line.") };
        notes.AddRange(extraNotes);
        foreach (var c in cols)
            if (L.Pick(c.HintVi ?? "", c.HintEn ?? c.HintVi ?? "") is { Length: > 0 } hint) notes.Add($"• {c.Header}: {hint}");
        return excel.Write(
            new ExcelSheet { Name = L.Pick("Du lieu", "Data"), Columns = cols.Select(c => new ExcelColumn(c.Key, c.Header, c.Width, c.Format, c.Required)).ToList(), Rows = sample ?? [] },
            new ExcelSheet { Name = L.Pick("Huong dan", "Guide"), Notes = notes });
    }

    /// <summary>Reads the first sheet; headers are matched in either language (and by key). Required columns are checked per row.</summary>
    public static List<LineImportRow> Read(IExcelService excel, byte[] bytes, IReadOnlyList<LineImportColumn> cols)
    {
        var map = new Dictionary<string, string>();
        foreach (var c in cols)
        {
            map[ExcelService.Norm(c.Key)] = c.Key;
            map[ExcelService.Norm(c.HeaderVi)] = c.Key;
            map[ExcelService.Norm(c.HeaderEn)] = c.Key;
        }
        using var ms = new MemoryStream(bytes);
        var read = excel.Read(ms, map.Keys.ToList());
        if (read.Rows.Count == 0) throw new AppException("import.noSheet");
        var rows = new List<LineImportRow>();
        foreach (var r in read.Rows)
        {
            var values = new Dictionary<string, string?>();
            foreach (var (header, raw) in r.Values)
                if (map.TryGetValue(ExcelService.Norm(header), out var key)) values[key] = raw;
            var row = new LineImportRow(r.RowNumber, values);
            foreach (var c in cols.Where(c => c.Required && row.Text(c.Key) is null)) row.Errors.Add(L.T("import.required", c.Header));
            rows.Add(row);
        }
        return rows;
    }

    public static async Task<byte[]> ReadUploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new AppException("import.noSheet");
        if (file.Length > 20 * 1024 * 1024) throw new AppException("error.fileSize", 413, 20);
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new AppException("error.fileType", 415);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        // xlsx = zip: "PK" magic number (do not trust the client MIME type)
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B) throw new AppException("error.fileType", 415);
        return bytes;
    }

    /// <summary>Keeps the uploaded file for 30 minutes so commit re-validates exactly what was previewed.</summary>
    public static async Task<string> StashAsync(ICacheService cache, Guid userId, string kind, byte[] bytes, string? options, CancellationToken ct)
    {
        var token = Guid.NewGuid().ToString("N");
        await cache.SetAsync("import:" + token, new CacheEntry(userId, kind, Convert.ToBase64String(bytes), options), TimeSpan.FromMinutes(30), ct);
        return token;
    }

    public static async Task<(byte[] Bytes, string? Options)> TakeAsync(ICacheService cache, Guid userId, string kind, string token, CancellationToken ct)
    {
        var e = await cache.GetAsync<CacheEntry>("import:" + token, ct);
        if (e is null || e.UserId != userId || e.Kind != kind) throw new AppException("error.notFound", 404, "import");
        return (Convert.FromBase64String(e.Content), e.Options);
    }

    public static Task ForgetAsync(ICacheService cache, string token, CancellationToken ct) => cache.RemoveAsync("import:" + token, ct);

    /// <summary>
    /// Per-row results for the preview grid. Rows of a document that has any error are all reported with that document's
    /// errors, because a document (a recipe, a receipt) is imported whole or not at all.
    /// </summary>
    public static List<ImportRowResult> Results<TKey>(IEnumerable<(LineImportRow Row, TKey Doc, string? Code)> rows, Func<TKey, ImportAction> action) where TKey : notnull
    {
        var list = rows.ToList();
        var failed = list.Where(x => x.Row.Errors.Count > 0).Select(x => x.Doc).ToHashSet();
        return list.Select(x =>
        {
            var errors = x.Row.Errors.Count > 0 ? x.Row.Errors
                : failed.Contains(x.Doc) ? [L.Pick("Bỏ qua vì dòng khác cùng chứng từ bị lỗi.", "Skipped because another line of the same document has errors.")]
                : [];
            return new ImportRowResult(x.Row.RowNumber, x.Code, action(x.Doc), errors);
        }).ToList();
    }

    public static ImportPreview Preview(string token, IReadOnlyList<ImportRowResult> results, int createDocs, int updateDocs)
    {
        var valid = results.Count(r => r.Errors.Count == 0);
        return new ImportPreview(token, results.Count, valid, results.Count - valid, createDocs, updateDocs, results);
    }
}
