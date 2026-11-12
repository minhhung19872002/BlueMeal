using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using BlueMeal.Application.Catalog;

namespace BlueMeal.UnitTests.Common;

/// <summary>OpenDocument Spreadsheet export (Thông tư 39/2017/TT-BTTTT: ".xls / .ods"), ODF 1.2 packaging and typed cells.</summary>
public class OdsWriterTests
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    private static ExcelSheet Sheet(bool header = true) => new()
    {
        Name = "Danh mục: NL/01",
        Header = header ? new ExcelReportHeader("Viện Dinh dưỡng", "Báo cáo nhập kho", "01/11/2026 – 12/11/2026", "Nguyễn Văn A", new DateTimeOffset(2026, 11, 12, 1, 30, 0, TimeSpan.Zero)) : null,
        Columns =
        [
            new("code", "Mã", 10, Required: true), new("name", "Tên", 30), new("qty", "Số lượng", 12, "#,##0.00"), new("pct", "Tỷ lệ", 8, "0.0\"%\""),
            new("date", "Ngày", 12), new("at", "Thời điểm", 16), new("active", "Hoạt động", 8),
        ],
        Rows =
        [
            new Dictionary<string, object?> { ["code"] = "NL01", ["name"] = "Thịt lợn <đùi> & \u0001xương", ["qty"] = 1234.5m, ["pct"] = 95.3m, ["date"] = new DateOnly(2026, 11, 12),
                ["at"] = new DateTimeOffset(2026, 11, 12, 1, 30, 0, TimeSpan.Zero), ["active"] = true },
            new Dictionary<string, object?> { ["code"] = "NL02", ["name"] = null, ["qty"] = 10, ["pct"] = null, ["date"] = null, ["at"] = null, ["active"] = false },
        ],
        TotalColumns = ["qty"],
    };

    private static (ZipArchive Zip, XDocument Content) Open(byte[] bytes)
    {
        var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var s = zip.GetEntry("content.xml")!.Open();
        return (zip, XDocument.Load(s));
    }

    [Fact]
    public void Mimetype_is_the_first_entry_and_stored_uncompressed()
    {
        var bytes = OdsWriter.Write(Sheet());

        // local file header of the first entry: compression method at offset 8 (0 = stored), file name at offset 30
        BitConverter.ToUInt16(bytes, 8).Should().Be(0);
        var nameLength = BitConverter.ToUInt16(bytes, 26);
        Encoding.ASCII.GetString(bytes, 30, nameLength).Should().Be("mimetype");
        var extraLength = BitConverter.ToUInt16(bytes, 28);
        Encoding.ASCII.GetString(bytes, 30 + nameLength + extraLength, OdsWriter.ContentType.Length).Should().Be(OdsWriter.ContentType);

        var (zip, _) = Open(bytes);
        using var m = zip.GetEntry("META-INF/manifest.xml")!.Open();
        var manifest = XDocument.Load(m);
        manifest.Descendants(Manifest + "file-entry").Select(e => (string?)e.Attribute(Manifest + "full-path")).Should().Contain(["/", "content.xml"]);
    }

    [Fact]
    public void Cells_are_typed_with_display_text()
    {
        var (_, content) = Open(OdsWriter.Write(Sheet()));
        var cells = content.Descendants(Table + "table-cell").ToList();

        var qty = cells.First(c => (string?)c.Attribute(Office + "value") == "1234.5");
        qty.Attribute(Office + "value-type")!.Value.Should().Be("float");

        var date = cells.First(c => (string?)c.Attribute(Office + "date-value") == "2026-11-12");
        date.Value.Should().Be("12/11/2026");

        // stored in UTC, shown in Vietnam time (+07:00)
        var at = cells.First(c => (string?)c.Attribute(Office + "date-value") == "2026-11-12T08:30:00");
        at.Value.Should().Be("12/11/2026 08:30");

        // XML special characters escaped by the writer, control characters dropped
        cells.Should().Contain(c => c.Value == "Thịt lợn <đùi> & xương");
        cells.Should().Contain(c => c.Value == "x");
    }

    [Fact]
    public void Writes_header_block_totals_with_formula_and_signatures()
    {
        var (_, content) = Open(OdsWriter.Write(Sheet()));
        var table = content.Descendants(Table + "table").Single();
        table.Attribute(Table + "name")!.Value.Should().Be("Danh mục- NL-01");

        var texts = table.Descendants(Text + "p").Select(p => p.Value).ToList();
        texts.Should().Contain("VIỆN DINH DƯỠNG").And.Contain("BÁO CÁO NHẬP KHO");
        texts.Should().Contain(t => t.Contains("12/11/2026 08:30"));

        var total = table.Descendants(Table + "table-cell").Single(c => c.Attribute(Table + "formula") is not null);
        total.Attribute(Office + "value")!.Value.Should().Be("1244.5");
        // header block (org, blank, title, period, preparer, blank) + header row = 7 rows, data on rows 8-9, qty is column C
        total.Attribute(Table + "formula")!.Value.Should().Be("of:=SUM([.C8:.C9])");

        table.Descendants(Table + "table-header-rows").Should().ContainSingle();
    }

    [Fact]
    public void Every_row_spans_exactly_the_sheet_width()
    {
        var (_, content) = Open(OdsWriter.Write(Sheet(), Sheet(header: false)));
        var tables = content.Descendants(Table + "table").ToList();
        tables.Select(t => t.Attribute(Table + "name")!.Value).Should().OnlyHaveUniqueItems();

        foreach (var row in tables.SelectMany(t => t.Elements().SelectMany(e => e.Name == Table + "table-header-rows" ? e.Elements() : [e])).Where(e => e.Name == Table + "table-row"))
        {
            var width = row.Elements().Sum(c => int.TryParse((string?)c.Attribute(Table + "number-columns-repeated"), out var n) ? n : 1);
            width.Should().Be(7, row.ToString());
        }
    }

    [Fact]
    public void Template_without_header_marks_required_columns()
    {
        var (_, content) = Open(OdsWriter.Write(Sheet(header: false)));
        content.Descendants(Text + "p").Select(p => p.Value).Should().Contain("Mã *");
        OdsWriter.FileName("wards_20261112.xlsx").Should().Be("wards_20261112.ods");
    }

    [Fact]
    public void Sheets_print_on_a4_one_page_wide_and_wide_sheets_turn_landscape()
    {
        XNamespace style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0", fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
        var wide = new ExcelSheet { Name = "Rộng", Columns = [.. Sheet().Columns, new("x1", "X1", 8), new("x2", "X2", 8)], Rows = Sheet().Rows };
        var (zip, content) = Open(OdsWriter.Write(Sheet(), wide));
        using var s = zip.GetEntry("styles.xml")!.Open();
        var styles = XDocument.Load(s);

        // table → table style → master page → page layout
        string Orientation(XElement table)
        {
            var tableStyle = content.Descendants(style + "style").Single(e => (string?)e.Attribute(style + "name") == (string?)table.Attribute(Table + "style-name"));
            var master = styles.Descendants(style + "master-page").Single(e => (string?)e.Attribute(style + "name") == (string?)tableStyle.Attribute(style + "master-page-name"));
            var layout = styles.Descendants(style + "page-layout").Single(e => (string?)e.Attribute(style + "name") == (string?)master.Attribute(style + "page-layout-name"));
            var props = layout.Element(style + "page-layout-properties")!;
            ((string?)props.Attribute(fo + "page-width") is "21cm" or "29.7cm").Should().BeTrue("A4");
            props.Attributes().Single(a => a.Name.LocalName == "scale-to-X").Value.Should().Be("1");
            return (string)props.Attribute(style + "print-orientation")!;
        }
        var tables = content.Descendants(Table + "table").ToList();
        Orientation(tables[0]).Should().Be("portrait");
        Orientation(tables[1]).Should().Be("landscape");

        // the print range covers exactly the written rows and columns (text overflowing past the last column is not printed)
        var sheets = new[] { Sheet(), Sheet(header: false), wide };
        (_, content) = Open(OdsWriter.Write(sheets));
        foreach (var t in content.Descendants(Table + "table"))
        {
            var rowCount = t.Descendants(Table + "table-row").Count();
            var cols = t.Elements(Table + "table-column").Count();
            var name = t.Attribute(Table + "name")!.Value;
            t.Attribute(Table + "print-ranges")!.Value.Should().Be($"'{name}'.A1:'{name}'.{(char)('A' + cols - 1)}{rowCount}");
        }
    }
}
