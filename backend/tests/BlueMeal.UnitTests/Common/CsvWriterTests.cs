using System.Text;
using BlueMeal.Application.Catalog;

namespace BlueMeal.UnitTests.Common;

/// <summary>CSV for data exchange (Thông tư 39/2017/TT-BTTTT): RFC 4180, UTF-8 with BOM, invariant formats.</summary>
public class CsvWriterTests
{
    private static readonly ExcelColumn[] Columns =
    [
        new("code", "Mã*"), new("name", "Tên"), new("qty", "Số lượng"), new("date", "Ngày"), new("active", "Hoạt động"),
    ];

    [Fact]
    public void Writes_utf8_bom_header_and_crlf_rows()
    {
        var bytes = CsvWriter.Write(Columns,
        [
            new Dictionary<string, object?> { ["code"] = "NL01", ["name"] = "Thịt lợn, đùi", ["qty"] = 1234.5m, ["date"] = new DateOnly(2026, 11, 12), ["active"] = true },
            new Dictionary<string, object?> { ["code"] = "NL02", ["name"] = "Rau \"sạch\"\nmuống", ["qty"] = -2m, ["date"] = null },
        ]);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.Should().Be(
            "Mã,Tên,Số lượng,Ngày,Hoạt động\r\n" +
            "NL01,\"Thịt lợn, đùi\",1234.5,2026-11-12,true\r\n" +
            "NL02,\"Rau \"\"sạch\"\"\nmuống\",-2,,\r\n");
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+84 912", "'+84 912")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("-5", "-5")]
    [InlineData("- gạch đầu dòng", "- gạch đầu dòng")]
    [InlineData("Bình thường", "Bình thường")]
    public void Guards_text_that_spreadsheets_would_evaluate(string input, string expected)
        => CsvWriter.Format(input).Should().Be(expected);

    [Fact]
    public void Formats_values_with_invariant_culture()
    {
        CsvWriter.Format(0.125m).Should().Be("0.125");
        CsvWriter.Format(1000000m).Should().Be("1000000");
        CsvWriter.Format(new DateTimeOffset(2026, 11, 12, 8, 30, 0, TimeSpan.FromHours(7))).Should().Be("2026-11-12T08:30:00+07:00");
        CsvWriter.Format(new[] { "a", "b" }).Should().Be("a; b");
        CsvWriter.Format(DayOfWeek.Monday).Should().Be("Monday");
        CsvWriter.FileName("wards_20261112.xlsx").Should().Be("wards_20261112.csv");
    }
}
