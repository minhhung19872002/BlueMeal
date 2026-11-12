using System.Globalization;
using System.Text;

namespace BlueMeal.Application.Catalog;

/// <summary>
/// CSV for data exchange (Thông tư 39/2017/TT-BTTTT, định dạng trao đổi dữ liệu): RFC 4180, UTF-8 with BOM (so that
/// Excel opens Vietnamese text correctly), comma separator, CRLF line endings, one header row, no report header or
/// totals. Values use invariant formats: numbers with a dot decimal separator and no grouping, ISO 8601 dates.
/// </summary>
public static class CsvWriter
{
    public const string ContentType = "text/csv; charset=utf-8";

    public static byte[] Write(IReadOnlyList<ExcelColumn> columns, IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(',', columns.Select(c => Escape(c.Header.TrimEnd('*', ' ')))).Append("\r\n");
        foreach (var row in rows)
        {
            sb.AppendJoin(',', columns.Select(c => Escape(Format(row.TryGetValue(c.Key, out var v) ? v : null))));
            sb.Append("\r\n");
        }
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return [.. preamble, .. body];
    }

    public static byte[] Write(ExcelSheet sheet) => Write(sheet.Columns, sheet.Rows);

    /// <summary>Same file name with a .csv extension.</summary>
    public static string FileName(string xlsxName) => Path.ChangeExtension(xlsxName, ".csv");

    public static string Format(object? v) => v switch
    {
        null => "",
        string s => GuardFormula(s),
        bool b => b ? "true" : "false",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString(dt.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.############################", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        System.Collections.IEnumerable list => GuardFormula(string.Join("; ", list.Cast<object?>().Select(Format))),
        _ => GuardFormula(v.ToString() ?? ""),
    };

    /// <summary>
    /// Text starting with a formula trigger is prefixed with an apostrophe so that spreadsheet programs do not
    /// evaluate it (CSV injection). Negative numbers are numbers, not text, and are never altered.
    /// </summary>
    private static string GuardFormula(string s)
    {
        if (s.Length == 0) return s;
        var risky = s[0] is '=' or '+' or '@' or '\t' or '\r'
                    || (s[0] == '-' && s.Length > 1 && !char.IsDigit(s[1]) && s[1] != ' ' && s[1] != '.');
        return risky ? "'" + s : s;
    }

    private static string Escape(string s)
        => s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
