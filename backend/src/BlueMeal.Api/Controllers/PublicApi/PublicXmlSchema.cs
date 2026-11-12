using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace BlueMeal.Api.Controllers.PublicApi;

/// <summary>
/// XML Schema (XSD 1.0) of the public API's XML responses (Thông tư 39/2017/TT-BTTTT: dữ liệu XML trao đổi phải có
/// lược đồ XSD mô tả cấu trúc). It is generated from the same types the XML formatter serializes, so the schema and the
/// responses cannot drift apart. Served at <c>GET /api/v1/public/schema.xsd</c> and copied to docs/api/public-api.xsd.
/// </summary>
public static class PublicXmlSchema
{
    /// <summary>Root documents of the XML responses: token, menus, menuNutrition, foods, nutritionSummary.</summary>
    public static readonly Type[] RootTypes = [typeof(TokenResponse), typeof(PublicMenuList), typeof(PublicMenuNutrition), typeof(PublicFoodPage), typeof(PublicSummary)];

    private static readonly Lazy<XmlSchemaSet> Set = new(Build);
    private static readonly Lazy<string> Text = new(Write);

    /// <summary>The compiled schema set, for validating documents.</summary>
    public static XmlSchemaSet SchemaSet => Set.Value;

    /// <summary>The schema as an XSD document (UTF-8, LF line endings).</summary>
    public static string Xsd => Text.Value;

    private static XmlSchemaSet Build()
    {
        var schemas = new XmlSchemas();
        var exporter = new XmlSchemaExporter(schemas);
        var importer = new XmlReflectionImporter();
        foreach (var t in RootTypes) exporter.ExportTypeMapping(importer.ImportTypeMapping(t));

        var schema = schemas.Cast<XmlSchema>().Single();
        schema.Items.Insert(0, new XmlSchemaAnnotation
        {
            Items =
            {
                new XmlSchemaDocumentation
                {
                    Language = "vi",
                    Markup = [new XmlDocument().CreateTextNode(
                        "BlueMeal — lược đồ XML của API công khai /api/v1/public (Accept: application/xml), " +
                        "theo Thông tư 39/2017/TT-BTTTT. Số thập phân dùng dấu chấm; ngày dạng yyyy-MM-dd; thời điểm ISO 8601 UTC. " +
                        "Phần tử có nillable=\"true\" nhận xsi:nil=\"true\" khi không có giá trị.")],
                },
            },
        });

        var set = new XmlSchemaSet();
        set.Add(schema);
        set.Compile();
        return set;
    }

    private static string Write()
    {
        var schema = SchemaSet.Schemas().Cast<XmlSchema>().Single();
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, NewLineChars = "\n", Encoding = Encoding.UTF8 }))
            schema.Write(w);
        // StringBuilder output declares utf-16; the file and the HTTP response are UTF-8
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"") + "\n";
    }
}
