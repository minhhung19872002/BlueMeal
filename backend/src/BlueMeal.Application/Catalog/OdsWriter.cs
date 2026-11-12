using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using BlueMeal.Application.Common;

namespace BlueMeal.Application.Catalog;

/// <summary>
/// OpenDocument Spreadsheet writer (.ods, ODF 1.2 / ISO/IEC 26300) — the open spreadsheet format named by
/// Thông tư 39/2017/TT-BTTTT (".xls / .ods", nhóm bắt buộc). Same layout as the Excel export: report header block,
/// bold header row, typed cells (numbers, dates) with Vietnamese display formats, totals (SUM formulas with the
/// computed value cached) and signature block. Written with the BCL only: a zip with an uncompressed "mimetype" entry
/// first, the manifest, styles.xml (A4 print setup) and content.xml.
/// </summary>
public static class OdsWriter
{
    public const string ContentType = "application/vnd.oasis.opendocument.spreadsheet";

    private const string NsOffice = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string NsStyle = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string NsTable = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string NsText = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string NsFo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private const string NsNumber = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";
    private const string NsOf = "urn:oasis:names:tc:opendocument:xmlns:of:1.2";
    private const string NsManifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    private const string NsLoExt = "urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0";

    private const string HeaderFill = "#e8f3eb", Brand = "#2f6b43", Border = "0.5pt solid #000000";

    /// <summary>Same file name with an .ods extension.</summary>
    public static string FileName(string xlsxName) => Path.ChangeExtension(xlsxName, ".ods");

    public static byte[] Write(params ExcelSheet[] sheets)
    {
        var numberFormats = new NumberFormats();
        var tables = sheets.Select(s => (Sheet: s, Rows: s.Rows.ToList())).ToList();
        foreach (var (s, _) in tables)
            foreach (var c in s.Columns) numberFormats.Register(c.Format);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // ODF 1.2 part 3 §3.3: "mimetype" is the first entry, stored uncompressed, so the type can be sniffed
            using (var s = zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())
                s.Write(Encoding.ASCII.GetBytes(ContentType));
            WriteXml(zip, "META-INF/manifest.xml", WriteManifest);
            WriteXml(zip, "styles.xml", WriteStylesPart);
            WriteXml(zip, "content.xml", w => WriteContent(w, tables, numberFormats));
        }
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ styles.xml: print setup

    private const string MasterPortrait = "A4Portrait", MasterLandscape = "A4Landscape";

    /// <summary>Same rule as the Excel export: more than 8 columns print landscape.</summary>
    private static bool IsLandscape(ExcelSheet s) => s.Columns.Count > 8;

    /// <summary>
    /// A4 page layouts, scaled to one page wide (Excel's FitToPages(1, 0)) with a page-number footer. Fit-to-width only
    /// became standard in ODF 1.3 (style:scale-to-X/Y); an ODF 1.2 document carries it as LibreOffice's loext: extension.
    /// </summary>
    private static void WriteStylesPart(XmlWriter w)
    {
        w.WriteStartElement("office", "document-styles", NsOffice);
        foreach (var (p, ns) in new[] { ("style", NsStyle), ("text", NsText), ("fo", NsFo), ("loext", NsLoExt) })
            w.WriteAttributeString("xmlns", p, null, ns);
        w.WriteAttributeString("office", "version", NsOffice, "1.2");

        w.WriteStartElement("office", "automatic-styles", NsOffice);
        PageLayout("pm-portrait", landscape: false);
        PageLayout("pm-landscape", landscape: true);
        w.WriteEndElement();

        w.WriteStartElement("office", "master-styles", NsOffice);
        MasterPage(MasterPortrait, "pm-portrait");
        MasterPage(MasterLandscape, "pm-landscape");
        w.WriteEndElement();
        w.WriteEndElement();

        void PageLayout(string name, bool landscape)
        {
            w.WriteStartElement("style", "page-layout", NsStyle);
            w.WriteAttributeString("style", "name", NsStyle, name);
            w.WriteStartElement("style", "page-layout-properties", NsStyle);
            w.WriteAttributeString("fo", "page-width", NsFo, landscape ? "29.7cm" : "21cm");
            w.WriteAttributeString("fo", "page-height", NsFo, landscape ? "21cm" : "29.7cm");
            w.WriteAttributeString("style", "print-orientation", NsStyle, landscape ? "landscape" : "portrait");
            foreach (var side in new[] { "top", "bottom", "left", "right" })
                w.WriteAttributeString("fo", "margin-" + side, NsFo, side is "top" or "bottom" ? "1.5cm" : "1.2cm");
            w.WriteAttributeString("loext", "scale-to-X", NsLoExt, "1");
            w.WriteAttributeString("loext", "scale-to-Y", NsLoExt, "0");
            w.WriteEndElement();
            w.WriteStartElement("style", "footer-style", NsStyle);
            w.WriteStartElement("style", "header-footer-properties", NsStyle);
            w.WriteAttributeString("fo", "min-height", NsFo, "0.6cm");
            w.WriteAttributeString("fo", "margin-top", NsFo, "0.25cm");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        void MasterPage(string name, string layout)
        {
            w.WriteStartElement("style", "master-page", NsStyle);
            w.WriteAttributeString("style", "name", NsStyle, name);
            w.WriteAttributeString("style", "page-layout-name", NsStyle, layout);
            w.WriteStartElement("style", "header", NsStyle);
            w.WriteAttributeString("style", "display", NsStyle, "false");
            w.WriteEndElement();
            w.WriteStartElement("style", "footer", NsStyle);
            w.WriteStartElement("text", "p", NsText);
            w.WriteStartElement("text", "page-number", NsText);
            w.WriteString("1");
            w.WriteEndElement();
            w.WriteString(" / ");
            w.WriteStartElement("text", "page-count", NsText);
            w.WriteString("1");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }
    }

    private static void WriteXml(ZipArchive zip, string name, Action<XmlWriter> body)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var w = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        w.WriteStartDocument();
        body(w);
        w.WriteEndDocument();
    }

    private static void WriteManifest(XmlWriter w)
    {
        w.WriteStartElement("manifest", "manifest", NsManifest);
        w.WriteAttributeString("manifest", "version", NsManifest, "1.2");
        FileEntry("/", ContentType, "1.2");
        FileEntry("styles.xml", "text/xml", null);
        FileEntry("content.xml", "text/xml", null);
        w.WriteEndElement();

        void FileEntry(string path, string type, string? version)
        {
            w.WriteStartElement("manifest", "file-entry", NsManifest);
            w.WriteAttributeString("manifest", "full-path", NsManifest, path);
            if (version is not null) w.WriteAttributeString("manifest", "version", NsManifest, version);
            w.WriteAttributeString("manifest", "media-type", NsManifest, type);
            w.WriteEndElement();
        }
    }

    // ------------------------------------------------------------------ content

    private static void WriteContent(XmlWriter w, List<(ExcelSheet Sheet, List<IReadOnlyDictionary<string, object?>> Rows)> tables, NumberFormats nf)
    {
        w.WriteStartElement("office", "document-content", NsOffice);
        foreach (var (p, ns) in new[] { ("style", NsStyle), ("table", NsTable), ("text", NsText), ("fo", NsFo), ("number", NsNumber), ("of", NsOf) })
            w.WriteAttributeString("xmlns", p, null, ns);
        w.WriteAttributeString("office", "version", NsOffice, "1.2");

        w.WriteStartElement("office", "automatic-styles", NsOffice);
        WriteStyles(w, tables.Select(t => t.Sheet).ToList(), nf);
        w.WriteEndElement();

        w.WriteStartElement("office", "body", NsOffice);
        w.WriteStartElement("office", "spreadsheet", NsOffice);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tables.Count; i++) WriteTable(w, i, tables[i].Sheet, tables[i].Rows, nf, usedNames);
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteStyles(XmlWriter w, IReadOnlyList<ExcelSheet> sheets, NumberFormats nf)
    {
        // column widths: the Excel width unit is roughly one character ≈ 0.19 cm
        for (var t = 0; t < sheets.Count; t++)
            for (var i = 0; i < sheets[t].Columns.Count; i++)
                Style(w, $"co{t}_{i}", "table-column", col => col.Attr("column-width", NsStyle, Cm(sheets[t].Columns[i].Width * 0.19)), null);

        // each sheet prints on the A4 master page matching the Excel export's orientation (styles.xml)
        for (var t = 0; t < sheets.Count; t++)
        {
            w.WriteStartElement("style", "style", NsStyle);
            w.WriteAttributeString("style", "name", NsStyle, $"ta{t}");
            w.WriteAttributeString("style", "family", NsStyle, "table");
            w.WriteAttributeString("style", "master-page-name", NsStyle, IsLandscape(sheets[t]) ? MasterLandscape : MasterPortrait);
            w.WriteStartElement("style", "table-properties", NsStyle);
            w.WriteAttributeString("table", "display", NsTable, "true");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        DateStyle(w, "Ndate", withTime: false);
        DateStyle(w, "Ndatetime", withTime: true);
        foreach (var (format, name) in nf.All) NumberStyle(w, name, format);

        Style(w, "ce-org", "table-cell", null, text => text.Attr("font-weight", NsFo, "bold"));
        Style(w, "ce-title", "table-cell", cell => cell.Attr("text-align", NsFo, "center", paragraph: true),
            text => text.Attr("font-weight", NsFo, "bold").Attr("font-size", NsFo, "14pt").Attr("color", NsFo, Brand));
        Style(w, "ce-center", "table-cell", cell => cell.Attr("text-align", NsFo, "center", paragraph: true), null);
        Style(w, "ce-italic-center", "table-cell", cell => cell.Attr("text-align", NsFo, "center", paragraph: true), text => text.Attr("font-style", NsFo, "italic"));
        Style(w, "ce-note", "table-cell", null, text => text.Attr("font-style", NsFo, "italic").Attr("color", NsFo, "#b45309"));
        Style(w, "ce-bold", "table-cell", null, text => text.Attr("font-weight", NsFo, "bold"));
        Style(w, "ce-italic", "table-cell", null, text => text.Attr("font-style", NsFo, "italic"));
        Style(w, "ce-head", "table-cell",
            cell => cell.Attr("background-color", NsFo, HeaderFill).Attr("border", NsFo, Border).Attr("wrap-option", NsFo, "wrap")
                .Attr("vertical-align", NsStyle, "middle").Attr("text-align", NsFo, "center", paragraph: true),
            text => text.Attr("font-weight", NsFo, "bold"));
        Style(w, "ce-total", "table-cell", cell => cell.Attr("background-color", NsFo, HeaderFill).Attr("border", NsFo, Border), text => text.Attr("font-weight", NsFo, "bold"));
        Style(w, "ce-text", "table-cell", cell => cell.Attr("border", NsFo, Border), null);
        Style(w, "ce-bool", "table-cell", cell => cell.Attr("border", NsFo, Border).Attr("text-align", NsFo, "center", paragraph: true), null);
        Style(w, "ce-date", "table-cell", cell => cell.Attr("border", NsFo, Border), null, "Ndate");
        Style(w, "ce-datetime", "table-cell", cell => cell.Attr("border", NsFo, Border), null, "Ndatetime");
        foreach (var (_, name) in nf.All)
        {
            Style(w, "ce-" + name, "table-cell", cell => cell.Attr("border", NsFo, Border), null, name);
            Style(w, "ce-total-" + name, "table-cell", cell => cell.Attr("background-color", NsFo, HeaderFill).Attr("border", NsFo, Border),
                text => text.Attr("font-weight", NsFo, "bold"), name);
        }
    }

    private static void Style(XmlWriter w, string name, string family, Action<Props>? cell, Action<Props>? text, string? dataStyle = null)
    {
        w.WriteStartElement("style", "style", NsStyle);
        w.WriteAttributeString("style", "name", NsStyle, name);
        w.WriteAttributeString("style", "family", NsStyle, family);
        if (dataStyle is not null) w.WriteAttributeString("style", "data-style-name", NsStyle, dataStyle);
        if (family == "table-column")
        {
            var p = new Props();
            cell?.Invoke(p);
            p.Write(w, "table-column-properties");
        }
        else
        {
            var c = new Props();
            cell?.Invoke(c);
            c.Write(w, "table-cell-properties");
            c.WriteParagraph(w);
            var t = new Props();
            text?.Invoke(t);
            t.Write(w, "text-properties");
        }
        w.WriteEndElement();
    }

    private static void DateStyle(XmlWriter w, string name, bool withTime)
    {
        w.WriteStartElement("number", "date-style", NsNumber);
        w.WriteAttributeString("style", "name", NsStyle, name);
        Part("day", true); Literal("/"); Part("month", true); Literal("/"); Part("year", true);
        if (withTime) { Literal(" "); Part("hours", true); Literal(":"); Part("minutes", true); }
        w.WriteEndElement();

        void Part(string el, bool longStyle)
        {
            w.WriteStartElement("number", el, NsNumber);
            if (longStyle) w.WriteAttributeString("number", "style", NsNumber, "long");
            w.WriteEndElement();
        }
        void Literal(string s) => w.WriteElementString("number", "text", NsNumber, s);
    }

    private static void NumberStyle(XmlWriter w, string name, NumberFormat f)
    {
        w.WriteStartElement("number", "number-style", NsNumber);
        w.WriteAttributeString("style", "name", NsStyle, name);
        w.WriteStartElement("number", "number", NsNumber);
        w.WriteAttributeString("number", "decimal-places", NsNumber, f.Decimals.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("number", "min-decimal-places", NsNumber, f.MinDecimals.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("number", "min-integer-digits", NsNumber, "1");
        if (f.Grouping) w.WriteAttributeString("number", "grouping", NsNumber, "true");
        w.WriteEndElement();
        // values of "percent" report columns are already percentages (95.3), the sign is only a suffix
        if (f.Percent) w.WriteElementString("number", "text", NsNumber, "%");
        w.WriteEndElement();
    }

    private static void WriteTable(XmlWriter w, int index, ExcelSheet s, List<IReadOnlyDictionary<string, object?>> rows, NumberFormats nf, HashSet<string> usedNames)
    {
        var cols = s.Columns;
        var lastCol = Math.Max(cols.Count, 1);
        w.WriteStartElement("table", "table", NsTable);
        var tableName = UniqueName(s.Name, usedNames);
        w.WriteAttributeString("table", "name", NsTable, tableName);
        w.WriteAttributeString("table", "style-name", NsTable, $"ta{index}");
        // explicit print range = the sheet's own columns and rows: without it LibreOffice prints text overflowing past the
        // last column too, and one long value (an audit diff) shrinks the whole fit-to-width page to an unreadable scale
        var sheetRef = "'" + tableName.Replace("'", "''", StringComparison.Ordinal) + "'";
        w.WriteAttributeString("table", "print-ranges", NsTable, $"{sheetRef}.A1:{sheetRef}.{ColumnLetter(lastCol - 1)}{RowCount(s, rows.Count)}");
        for (var i = 0; i < cols.Count; i++)
        {
            w.WriteStartElement("table", "table-column", NsTable);
            w.WriteAttributeString("table", "style-name", NsTable, $"co{index}_{i}");
            w.WriteEndElement();
        }

        var rowNo = 0;
        if (s.Header is { } h)
        {
            Row(w, lastCol, [new(0, Text(h.OrganizationName.ToUpper(CultureInfo.CurrentCulture)), "ce-org", Math.Max(1, lastCol / 2))]);
            Row(w, lastCol, []);
            Row(w, lastCol, [new(0, Text(h.Title.ToUpper(CultureInfo.CurrentCulture)), "ce-title", lastCol)]);
            rowNo += 3;
            if (!string.IsNullOrEmpty(h.Period)) { Row(w, lastCol, [new(0, Text($"{L.T("excel.period")}: {h.Period}"), "ce-italic-center", lastCol)]); rowNo++; }
            Row(w, lastCol, [new(0, Text($"{L.T("excel.preparedBy")}: {h.PreparedBy}    {L.T("excel.preparedAt")}: {h.PreparedAt.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm}"), "ce-center", lastCol)]);
            rowNo++;
            if (!string.IsNullOrEmpty(h.Note)) { Row(w, lastCol, [new(0, Text(h.Note), "ce-note", lastCol)]); rowNo++; }
            Row(w, lastCol, []);
            rowNo++;
        }

        if (s.GroupHeaders is { Count: > 0 })
        {
            var cells = new List<Cell>();
            foreach (var (startKey, span, text) in s.GroupHeaders)
            {
                var idx = cols.ToList().FindIndex(c => c.Key == startKey);
                if (idx >= 0) cells.Add(new Cell(idx, Text(text), "ce-head", span));
            }
            Row(w, lastCol, cells);
            rowNo++;
        }

        w.WriteStartElement("table", "table-header-rows", NsTable);
        Row(w, lastCol, cols.Select((c, i) => new Cell(i, Text(c.Header + (c.Required && s.Header is null ? " *" : "")), "ce-head")).ToList());
        w.WriteEndElement();
        rowNo++;

        var firstData = rowNo + 1;
        foreach (var r in rows)
        {
            Row(w, lastCol, cols.Select((c, i) => DataCell(i, r.TryGetValue(c.Key, out var v) ? v : null, c.Format, nf)).ToList());
            rowNo++;
        }
        var lastData = rowNo;

        if (s.TotalColumns.Count > 0 && rows.Count > 0)
        {
            var cells = new List<Cell> { new(0, Text(L.T("excel.total")), "ce-total") };
            for (var i = 0; i < cols.Count; i++)
            {
                if (!s.TotalColumns.Contains(cols[i].Key)) continue;
                var sum = rows.Sum(r => r.TryGetValue(cols[i].Key, out var v) && v is not null and not string and not bool ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m);
                var letter = ColumnLetter(i);
                var name = nf.Name(cols[i].Format ?? "#,##0");
                cells.Add(new Cell(i, new CellValue("float", sum.ToString(CultureInfo.InvariantCulture), null, $"of:=SUM([.{letter}{firstData}:.{letter}{lastData}])"), "ce-total-" + name));
            }
            for (var i = 1; i < lastCol; i++)
                if (cells.All(c => c.Col != i)) cells.Add(new Cell(i, null, "ce-total"));
            Row(w, lastCol, cells);
        }

        if (s.Header is { Signatures: true })
        {
            Row(w, lastCol, []);
            Row(w, lastCol, []);
            var third = Math.Max(1, lastCol / 3);
            int[] at = [0, third, Math.Max(third * 2, lastCol - 2)];
            Row(w, lastCol, Distinct(at).Zip([L.T("excel.signPreparer"), L.T("excel.signChief"), L.T("excel.signDirector")], (c, t) => new Cell(c, Text(t), "ce-bold")).ToList());
            Row(w, lastCol, Distinct(at).Select(c => new Cell(c, Text(L.T("excel.signHint")), "ce-italic")).ToList());
        }

        if (s.Notes is { Count: > 0 })
        {
            Row(w, lastCol, []);
            foreach (var n in s.Notes) Row(w, lastCol, [new Cell(0, Text(n), null)]);
        }
        w.WriteEndElement();
    }

    /// <summary>Number of rows <see cref="WriteTable"/> writes for the sheet (kept in step with it; a unit test compares the two).</summary>
    private static int RowCount(ExcelSheet s, int dataRows)
    {
        var n = 0;
        if (s.Header is { } h) n += 5 + (string.IsNullOrEmpty(h.Period) ? 0 : 1) + (string.IsNullOrEmpty(h.Note) ? 0 : 1);
        if (s.GroupHeaders is { Count: > 0 }) n++;
        n += 1 + dataRows;
        if (s.TotalColumns.Count > 0 && dataRows > 0) n++;
        if (s.Header is { Signatures: true }) n += 4;
        if (s.Notes is { Count: > 0 }) n += 1 + s.Notes.Count;
        return n;
    }

    private static IEnumerable<int> Distinct(int[] cols)
    {
        var seen = new HashSet<int>();
        foreach (var c in cols) if (seen.Add(c)) yield return c;
    }

    private static Cell DataCell(int col, object? v, string? format, NumberFormats nf) => v switch
    {
        null => new Cell(col, null, "ce-text"),
        decimal or double or float => new Cell(col, Number(v), "ce-" + nf.Name(format ?? "#,##0.##")),
        int or long or short => new Cell(col, Number(v), "ce-" + nf.Name(format ?? "#,##0")),
        bool b => new Cell(col, Text(b ? "x" : ""), "ce-bool"),
        DateOnly d => new Cell(col, new CellValue("date", d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)), "ce-date"),
        DateTime dt => DateTimeCell(col, dt),
        DateTimeOffset dto => DateTimeCell(col, dto.ToOffset(TimeSpan.FromHours(7)).DateTime),
        TimeOnly t => new Cell(col, Text(t.ToString("HH:mm", CultureInfo.InvariantCulture)), "ce-text"),
        System.Collections.IEnumerable list and not string => new Cell(col, Text(string.Join("; ", list.Cast<object?>())), "ce-text"),
        _ => new Cell(col, Text(v.ToString() ?? ""), "ce-text"),
    };

    private static Cell DateTimeCell(int col, DateTime dt) => dt.TimeOfDay == TimeSpan.Zero
        ? new Cell(col, new CellValue("date", dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)), "ce-date")
        : new Cell(col, new CellValue("date", dt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), dt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)), "ce-datetime");

    private static CellValue Number(object v)
        => new("float", Convert.ToDecimal(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), null);

    private static CellValue Text(string s) => new("string", null, s);

    private sealed record CellValue(string Type, string? Value, string? Display, string? Formula = null);
    private sealed record Cell(int Col, CellValue? Value, string? Style, int Span = 1);

    /// <summary>Writes one row of <paramref name="width"/> columns: given cells at their positions, merged spans, empty cells between.</summary>
    private static void Row(XmlWriter w, int width, IReadOnlyList<Cell> cells)
    {
        w.WriteStartElement("table", "table-row", NsTable);
        var byCol = cells.Where(c => c.Col < width).GroupBy(c => c.Col).ToDictionary(g => g.Key, g => g.First());
        var empty = 0;
        for (var col = 0; col < width;)
        {
            if (!byCol.TryGetValue(col, out var c)) { empty++; col++; continue; }
            EmptyCells(w, empty);
            empty = 0;
            var span = Math.Clamp(c.Span, 1, width - col);
            w.WriteStartElement("table", "table-cell", NsTable);
            if (c.Style is not null) w.WriteAttributeString("table", "style-name", NsTable, c.Style);
            if (span > 1) w.WriteAttributeString("table", "number-columns-spanned", NsTable, span.ToString(CultureInfo.InvariantCulture));
            if (c.Value is { } v)
            {
                if (v.Formula is not null) w.WriteAttributeString("table", "formula", NsTable, v.Formula);
                w.WriteAttributeString("office", "value-type", NsOffice, v.Type);
                if (v.Type == "float") w.WriteAttributeString("office", "value", NsOffice, v.Value);
                if (v.Type == "date") w.WriteAttributeString("office", "date-value", NsOffice, v.Value);
                var display = v.Display ?? v.Value;
                if (!string.IsNullOrEmpty(display)) w.WriteElementString("text", "p", NsText, XmlSafe(display));
            }
            w.WriteEndElement();
            if (span > 1)
            {
                w.WriteStartElement("table", "covered-table-cell", NsTable);
                if (span > 2) w.WriteAttributeString("table", "number-columns-repeated", NsTable, (span - 1).ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }
            col += span;
        }
        EmptyCells(w, empty);
        w.WriteEndElement();
    }

    private static void EmptyCells(XmlWriter w, int count)
    {
        if (count <= 0) return;
        w.WriteStartElement("table", "table-cell", NsTable);
        if (count > 1) w.WriteAttributeString("table", "number-columns-repeated", NsTable, count.ToString(CultureInfo.InvariantCulture));
        w.WriteEndElement();
    }

    /// <summary>Drops characters XML 1.0 cannot carry (control characters in free text would otherwise fail the export).</summary>
    private static string XmlSafe(string s)
        => s.Any(ch => char.IsControl(ch) && ch is not '\t' and not '\n' and not '\r') ? new string(s.Where(ch => !char.IsControl(ch) || ch is '\t' or '\n' or '\r').ToArray()) : s;

    private static string UniqueName(string name, HashSet<string> used)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']', '\'' };
        var baseName = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (baseName.Length == 0) baseName = "Data";
        var candidate = baseName;
        for (var i = 2; !used.Add(candidate); i++) candidate = $"{baseName} ({i})";
        return candidate;
    }

    public static string ColumnLetter(int index)
    {
        var s = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    private static string Cm(double cm) => cm.ToString("0.###", CultureInfo.InvariantCulture) + "cm";

    // ------------------------------------------------------------------ helpers

    /// <summary>Collects properties for a style element; paragraph properties (alignment) go to their own element.</summary>
    private sealed class Props
    {
        private readonly List<(string Name, string Ns, string Value)> _attrs = [];
        private readonly List<(string Name, string Ns, string Value)> _paragraph = [];

        public Props Attr(string name, string ns, string value, bool paragraph = false)
        {
            (paragraph ? _paragraph : _attrs).Add((name, ns, value));
            return this;
        }

        public void Write(XmlWriter w, string element)
        {
            if (_attrs.Count == 0) return;
            w.WriteStartElement("style", element, NsStyle);
            foreach (var (n, ns, v) in _attrs) w.WriteAttributeString(Prefix(ns), n, ns, v);
            w.WriteEndElement();
        }

        public void WriteParagraph(XmlWriter w)
        {
            if (_paragraph.Count == 0) return;
            w.WriteStartElement("style", "paragraph-properties", NsStyle);
            foreach (var (n, ns, v) in _paragraph) w.WriteAttributeString(Prefix(ns), n, ns, v);
            w.WriteEndElement();
        }

        private static string Prefix(string ns) => ns == NsFo ? "fo" : "style";
    }

    private sealed record NumberFormat(int Decimals, int MinDecimals, bool Grouping, bool Percent);

    /// <summary>Maps Excel number formats ("#,##0", "#,##0.00", "0.0\"%\"") to named ODF number styles.</summary>
    private sealed class NumberFormats
    {
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private readonly List<(NumberFormat Format, string Name)> _all = [];

        public NumberFormats()
        {
            Register("#,##0");
            Register("#,##0.##");
        }

        public IReadOnlyList<(NumberFormat Format, string Name)> All => _all;

        public void Register(string? excelFormat)
        {
            if (excelFormat is null || IsDateFormat(excelFormat) || _names.ContainsKey(excelFormat)) return;
            var name = "N" + _all.Count.ToString(CultureInfo.InvariantCulture);
            _names[excelFormat] = name;
            _all.Add((Parse(excelFormat), name));
        }

        public string Name(string excelFormat)
        {
            if (IsDateFormat(excelFormat)) excelFormat = "#,##0.##";
            return _names.TryGetValue(excelFormat, out var n) ? n : _names["#,##0.##"];
        }

        private static bool IsDateFormat(string f) => f.Contains("yy", StringComparison.OrdinalIgnoreCase) || f.Contains("dd", StringComparison.OrdinalIgnoreCase);

        private static NumberFormat Parse(string f)
        {
            var percent = f.Contains('%');
            var core = f.Split('"')[0];
            var dot = core.IndexOf('.');
            var frac = dot < 0 ? "" : new string(core[(dot + 1)..].TakeWhile(ch => ch is '0' or '#').ToArray());
            return new NumberFormat(frac.Length, frac.Count(ch => ch == '0'), core.Contains(','), percent);
        }
    }
}
