using System.Globalization;
using BlueMeal.Application.Catalog;
using BlueMeal.Application.Common;
using ClosedXML.Excel;

namespace BlueMeal.Infrastructure.Excel;

/// <summary>
/// ClosedXML-based Excel writer/reader. Reports get the standard header block (organization, title, period,
/// preparer, date) and signature block, Vietnamese number formats (SPEC §7.9).
/// </summary>
public sealed class ExcelService : IExcelService
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#E8F3EB");
    private static readonly XLColor Brand = XLColor.FromHtml("#2F6B43");

    public byte[] Write(params ExcelSheet[] sheets)
    {
        using var wb = new XLWorkbook();
        foreach (var s in sheets) WriteSheet(wb, s);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteSheet(XLWorkbook wb, ExcelSheet s)
    {
        var ws = wb.Worksheets.Add(SafeSheetName(s.Name));
        ws.Style.Font.FontName = "Times New Roman";
        ws.Style.Font.FontSize = 11;
        var cols = s.Columns;
        var lastCol = Math.Max(cols.Count, 1);
        var row = 1;

        if (s.Header is { } h)
        {
            ws.Cell(row, 1).Value = h.OrganizationName.ToUpper(CultureInfo.CurrentCulture);
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 1, row, Math.Max(1, lastCol / 2)).Merge();
            row += 2;
            ws.Cell(row, 1).Value = h.Title.ToUpper(CultureInfo.CurrentCulture);
            ws.Range(row, 1, row, lastCol).Merge().Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(Brand)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row++;
            if (!string.IsNullOrEmpty(h.Period))
            {
                ws.Cell(row, 1).Value = $"{L.T("excel.period")}: {h.Period}";
                ws.Range(row, 1, row, lastCol).Merge().Style.Font.SetItalic().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                row++;
            }
            ws.Cell(row, 1).Value = $"{L.T("excel.preparedBy")}: {h.PreparedBy}    {L.T("excel.preparedAt")}: {h.PreparedAt.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm}";
            ws.Range(row, 1, row, lastCol).Merge().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row++;
            if (!string.IsNullOrEmpty(h.Note))
            {
                ws.Cell(row, 1).Value = h.Note;
                ws.Range(row, 1, row, lastCol).Merge().Style.Font.SetItalic().Font.SetFontColor(XLColor.FromHtml("#B45309"));
                row++;
            }
            row++;
        }

        if (s.GroupHeaders is { Count: > 0 })
        {
            foreach (var (startKey, span, text) in s.GroupHeaders)
            {
                var idx = cols.ToList().FindIndex(c => c.Key == startKey) + 1;
                if (idx <= 0) continue;
                ws.Cell(row, idx).Value = text;
                var rg = ws.Range(row, idx, row, idx + span - 1).Merge();
                rg.Style.Font.SetBold().Fill.SetBackgroundColor(HeaderFill).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                    .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            }
            row++;
        }

        var headerRow = row;
        for (var i = 0; i < cols.Count; i++)
        {
            var c = ws.Cell(row, i + 1);
            c.Value = cols[i].Header + (cols[i].Required && s.Header is null ? " *" : "");
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = HeaderFill;
            c.Style.Alignment.WrapText = true;
            c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Column(i + 1).Width = cols[i].Width;
            if (!string.IsNullOrEmpty(cols[i].Hint)) c.CreateComment().AddText(cols[i].Hint);
        }
        row++;
        var firstData = row;
        foreach (var r in s.Rows)
        {
            for (var i = 0; i < cols.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                SetValue(cell, r.TryGetValue(cols[i].Key, out var v) ? v : null, cols[i].Format);
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            row++;
        }
        var lastData = row - 1;

        if (s.TotalColumns.Count > 0 && lastData >= firstData)
        {
            ws.Cell(row, 1).Value = L.T("excel.total");
            ws.Cell(row, 1).Style.Font.Bold = true;
            for (var i = 0; i < cols.Count; i++)
            {
                if (!s.TotalColumns.Contains(cols[i].Key)) continue;
                var cell = ws.Cell(row, i + 1);
                var letter = ws.Column(i + 1).ColumnLetter();
                cell.FormulaA1 = $"SUM({letter}{firstData}:{letter}{lastData})";
                cell.Style.NumberFormat.Format = cols[i].Format ?? "#,##0";
                cell.Style.Font.Bold = true;
            }
            ws.Range(row, 1, row, lastCol).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Fill.SetBackgroundColor(HeaderFill);
            row++;
        }

        if (s.Header is { Signatures: true })
        {
            row += 2;
            var third = Math.Max(1, lastCol / 3);
            var blocks = new[] { (1, L.T("excel.signPreparer")), (third + 1, L.T("excel.signChief")), (Math.Max(third * 2 + 1, lastCol - 1), L.T("excel.signDirector")) };
            foreach (var (col, title) in blocks)
            {
                ws.Cell(row, col).Value = title;
                ws.Cell(row, col).Style.Font.Bold = true;
                ws.Cell(row + 1, col).Value = L.T("excel.signHint");
                ws.Cell(row + 1, col).Style.Font.Italic = true;
            }
        }

        if (s.Notes is { Count: > 0 })
        {
            row += 2;
            foreach (var n in s.Notes) { ws.Cell(row++, 1).Value = n; }
        }

        if (cols.Count > 0)
        {
            ws.SheetView.FreezeRows(headerRow);
            if (lastData >= firstData) ws.Range(headerRow, 1, lastData, cols.Count).SetAutoFilter();
        }
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = cols.Count > 8 ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
    }

    private static void SetValue(IXLCell cell, object? v, string? format)
    {
        switch (v)
        {
            case null: return;
            case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = format ?? "#,##0.##"; break;
            case double db: cell.Value = db; cell.Style.NumberFormat.Format = format ?? "#,##0.##"; break;
            case float f: cell.Value = f; cell.Style.NumberFormat.Format = format ?? "#,##0.##"; break;
            case int or long or short: cell.Value = Convert.ToDouble(v); cell.Style.NumberFormat.Format = format ?? "#,##0"; break;
            case bool b: cell.Value = b ? "x" : ""; cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; break;
            case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.NumberFormat.Format = "dd/mm/yyyy"; break;
            case DateTime dt: cell.Value = dt; cell.Style.NumberFormat.Format = format ?? "dd/mm/yyyy hh:mm"; break;
            case DateTimeOffset dto: cell.Value = dto.ToOffset(TimeSpan.FromHours(7)).DateTime; cell.Style.NumberFormat.Format = format ?? "dd/mm/yyyy hh:mm"; break;
            case TimeOnly t: cell.Value = t.ToString("HH:mm"); break;
            default: cell.Value = v.ToString(); break;
        }
    }

    public ExcelReadResult Read(Stream stream, IReadOnlyCollection<string>? knownHeaders = null)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used is null) return new ExcelReadResult([], []);
        var lastRow = used.LastRow().RowNumber();
        var lastCol = used.LastColumn().ColumnNumber();

        // Detect header row: first row among the first 15 matching most known headers (or most non-empty cells).
        var headerRowNo = 1;
        var best = -1;
        var known = knownHeaders?.Select(Norm).ToHashSet();
        for (var r = 1; r <= Math.Min(15, lastRow); r++)
        {
            var cells = Enumerable.Range(1, lastCol).Select(c => ws.Cell(r, c).GetString().Trim()).Where(s => s.Length > 0).ToList();
            var score = known is null ? cells.Count : cells.Count(c => known.Contains(Norm(c)));
            if (score > best) { best = score; headerRowNo = r; }
        }
        var headers = Enumerable.Range(1, lastCol).Select(c => ws.Cell(headerRowNo, c).GetString().Trim().TrimEnd('*').Trim()).ToList();
        var rows = new List<ExcelReadRow>();
        for (var r = headerRowNo + 1; r <= lastRow; r++)
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var any = false;
            for (var c = 1; c <= lastCol; c++)
            {
                if (string.IsNullOrEmpty(headers[c - 1])) continue;
                var cell = ws.Cell(r, c);
                string? val = cell.DataType switch
                {
                    XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
                    XLDataType.Boolean => cell.GetBoolean() ? "true" : "false",
                    _ => cell.GetString().Trim(),
                };
                if (string.IsNullOrEmpty(val)) val = null; else any = true;
                dict[headers[c - 1]] = val;
            }
            if (any) rows.Add(new ExcelReadRow(r, dict));
        }
        return new ExcelReadResult(headers, rows);
    }

    public static string Norm(string s) => BlueMeal.Domain.Common.TextNormalizer.Normalize(s.TrimEnd('*').Trim());

    private static string SafeSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var s = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return s.Length > 31 ? s[..31] : s;
    }
}
