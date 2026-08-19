using System.Xml;
using System.Xml.Schema;

namespace IntegrationAssignment.Services;

public sealed class ProductXmlValidator
{
    private const string SchemaResourceName =
        "IntegrationAssignment.Schemas.ProductDelivery.xsd";

    private static readonly XmlSchemaSet Schemas = LoadSchemas();

    public void Validate(string xml)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            Schemas = Schemas,
            ValidationType = ValidationType.Schema,
            XmlResolver = null
        };
        settings.ValidationEventHandler += (_, args) => errors.Add(args.Message);

        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);

        while (xmlReader.Read())
        {
        }

        if (errors.Count > 0)
        {
            throw new XmlSchemaValidationException(
                $"The canonical product XML failed schema validation: {string.Join("; ", errors)}");
        }
    }

    private static XmlSchemaSet LoadSchemas()
    {
        using var schemaStream = typeof(ProductXmlValidator).Assembly
            .GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded XML schema '{SchemaResourceName}' was not found.");
        using var schemaReader = XmlReader.Create(
            schemaStream,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });

        var schemas = new XmlSchemaSet { XmlResolver = null };
        schemas.Add(targetNamespace: null, schemaReader);
        schemas.Compile();
        return schemas;
    }
}
