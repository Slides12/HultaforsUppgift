using System.Xml.Linq;
using System.Xml.Schema;
using IntegrationAssignment.Models;
using IntegrationAssignment.Services;

namespace IntegrationAssignment.Tests;

public sealed class ProductXmlSerializerTests
{
    private static readonly DateTimeOffset ReceivedAtUtc =
        DateTimeOffset.Parse("2026-08-14T11:59:00Z");

    private static readonly DateTimeOffset ProcessedAtUtc =
        DateTimeOffset.Parse("2026-08-14T12:00:00Z");

    [Fact]
    public void Serialize_CanonicalDocument_ReturnsReceiverReadyXml()
    {
        var serializer = new ProductXmlSerializer();

        var xml = serializer.Serialize(CreateCanonicalDocument());
        var document = XDocument.Parse(xml);

        Assert.Equal("ProductDelivery", document.Root?.Name.LocalName);
        Assert.Equal(ProductXmlSerializer.SchemaVersion, document.Root?.Attribute("schemaVersion")?.Value);
        Assert.Equal("correlation-123", document.Root?.Element("CorrelationId")?.Value);
        Assert.Equal("2026-08-14T11:59:00.0000000+00:00", document.Root?.Element("ReceivedAtUtc")?.Value);
        Assert.Equal("2026-08-14T12:00:00.0000000+00:00", document.Root?.Element("ProcessedAtUtc")?.Value);
        Assert.Equal("P-1001", document.Root?.Element("Product")?.Element("Id")?.Value);
        Assert.Equal(
            "SEK",
            document.Root?
                .Element("Product")?
                .Element("UnitPrice")?
                .Element("Currency")?
                .Value);
        Assert.Null(document.Root?.Element("OriginalJson"));
    }

    [Fact]
    public void Validate_SerializedCanonicalDocument_Succeeds()
    {
        var serializer = new ProductXmlSerializer();
        var validator = new ProductXmlValidator();
        var xml = serializer.Serialize(CreateCanonicalDocument());

        var exception = Record.Exception(() => validator.Validate(xml));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_XmlMissingRequiredProductId_Throws()
    {
        const string xml = """
            <ProductDelivery schemaVersion="1.0">
              <CorrelationId>correlation-123</CorrelationId>
              <ReceivedAtUtc>2026-08-14T11:59:00Z</ReceivedAtUtc>
              <ProcessedAtUtc>2026-08-14T12:00:00Z</ProcessedAtUtc>
              <Product>
                <DisplayName>Hammer</DisplayName>
                <UnitPrice><Amount>249.90</Amount><Currency>SEK</Currency></UnitPrice>
                <AvailableQuantity>25</AvailableQuantity>
                <ProductCategory>Tools</ProductCategory>
              </Product>
            </ProductDelivery>
            """;
        var validator = new ProductXmlValidator();

        Assert.Throws<XmlSchemaValidationException>(() => validator.Validate(xml));
    }

    private static CanonicalProductDocument CreateCanonicalDocument() =>
        new(
            "correlation-123",
            ReceivedAtUtc,
            new CanonicalProduct(
                "P-1001",
                "Hammer",
                new Money(249.90m, "SEK"),
                25,
                "Tools",
                ProcessedAtUtc));
}
