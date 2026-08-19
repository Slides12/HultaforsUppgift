using System.Globalization;
using System.Xml.Linq;
using IntegrationAssignment.Models;

namespace IntegrationAssignment.Services;

public sealed class ProductXmlSerializer
{
    public const string SchemaVersion = "1.0";

    public string Serialize(CanonicalProductDocument canonicalDocument)
    {
        var product = canonicalDocument.Product;
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "ProductDelivery",
                new XAttribute("schemaVersion", SchemaVersion),
                new XElement("CorrelationId", canonicalDocument.CorrelationId),
                new XElement(
                    "ReceivedAtUtc",
                    canonicalDocument.ReceivedAtUtc.ToString(
                        "O",
                        CultureInfo.InvariantCulture)),
                new XElement(
                    "ProcessedAtUtc",
                    product.ProcessedAtUtc.ToString(
                        "O",
                        CultureInfo.InvariantCulture)),
                new XElement(
                    "Product",
                    new XElement("Id", product.Id ?? string.Empty),
                    new XElement("DisplayName", product.DisplayName ?? string.Empty),
                    new XElement(
                        "UnitPrice",
                        new XElement(
                            "Amount",
                            product.UnitPrice.Amount.ToString(CultureInfo.InvariantCulture)),
                        new XElement(
                            "Currency",
                            product.UnitPrice.Currency ?? string.Empty)),
                    new XElement("AvailableQuantity", product.AvailableQuantity),
                    new XElement(
                        "ProductCategory",
                        product.ProductCategory ?? string.Empty))));

        return document.ToString();
    }
}
