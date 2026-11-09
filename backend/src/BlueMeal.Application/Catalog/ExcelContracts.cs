namespace BlueMeal.Application.Catalog;

/// <summary>Declares an Excel import/export column on an upsert DTO property (bilingual header).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ExcelColumnAttribute(string headerVi, string headerEn) : Attribute
{
    public string HeaderVi { get; } = headerVi;
    public string HeaderEn { get; } = headerEn;
    public bool Required { get; init; }
    public int Order { get; init; }
    public double Width { get; init; } = 18;
    /// <summary>Excel number format (e.g. "#,##0", "#,##0.0", "dd/mm/yyyy").</summary>
    public string? Format { get; init; }
    /// <summary>Hint shown in the template's instruction sheet.</summary>
    public string? HintVi { get; init; }
    public string? HintEn { get; init; }
}

/// <summary>Guid foreign key exported/imported as the referenced catalog's code.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ExcelRefAttribute(Type entity) : Attribute
{
    public Type Entity { get; } = entity;
}

/// <summary>Property excluded from Excel import/export.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ExcelIgnoreAttribute : Attribute;

public sealed record ExcelColumn(string Key, string Header, double Width = 18, string? Format = null, bool Required = false, string? Hint = null);

/// <summary>Report header block (SPEC §7.9): organization, title, period, preparer, date, signatures.</summary>
public sealed record ExcelReportHeader(string OrganizationName, string Title, string? Period, string PreparedBy, DateTimeOffset PreparedAt, bool Signatures = true, string? Note = null, string? LogoPath = null);

public sealed class ExcelSheet
{
    public string Name { get; init; } = "Data";
    public ExcelReportHeader? Header { get; init; }
    public IReadOnlyList<ExcelColumn> Columns { get; init; } = [];
    public IEnumerable<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];
    /// <summary>Column keys to total in a footer row.</summary>
    public IReadOnlyList<string> TotalColumns { get; init; } = [];
    /// <summary>Optional instruction lines (template sheet).</summary>
    public IReadOnlyList<string>? Notes { get; init; }
    /// <summary>Optional multi-row group header: (startColumnKey, span, text).</summary>
    public IReadOnlyList<(string StartKey, int Span, string Text)>? GroupHeaders { get; init; }
}

public interface IExcelService
{
    byte[] Write(params ExcelSheet[] sheets);
    /// <summary>Reads the first worksheet: header row (row containing most non-empty cells within the first 10 rows) → list of rows keyed by header text.</summary>
    ExcelReadResult Read(Stream stream, IReadOnlyCollection<string>? knownHeaders = null);
}

public sealed record ExcelReadRow(int RowNumber, IReadOnlyDictionary<string, string?> Values);
public sealed record ExcelReadResult(IReadOnlyList<string> Headers, IReadOnlyList<ExcelReadRow> Rows);

public enum ImportAction { Create, Update, Skip }
public sealed record ImportRowResult(int RowNumber, string? Code, ImportAction Action, IReadOnlyList<string> Errors);
public sealed record ImportPreview(string Token, int TotalRows, int ValidRows, int ErrorRows, int CreateCount, int UpdateCount, IReadOnlyList<ImportRowResult> Rows);
public sealed record ImportCommitResult(int Created, int Updated, int Skipped, IReadOnlyList<ImportRowResult> Errors);
