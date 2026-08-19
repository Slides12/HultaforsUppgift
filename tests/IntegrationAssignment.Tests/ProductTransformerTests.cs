using IntegrationAssignment.Models;
using IntegrationAssignment.Services;

namespace IntegrationAssignment.Tests;

public sealed class ProductTransformerTests
{
    [Fact]
    public void Transform_ExternalProduct_ReturnsExpectedCanonicalProduct()
    {
        var transformer = new ProductTransformer();
        var processedAtUtc = DateTimeOffset.Parse("2026-08-14T12:00:00Z");
        var externalProduct = new ExternalProduct(
            " P-1001 ",
            " Hammer ",
            249.90m,
            " sek ",
            25,
            " Tools ");

        var result = transformer.Transform(externalProduct, processedAtUtc);

        var expected = new CanonicalProduct(
            "P-1001",
            "Hammer",
            new Money(249.90m, "SEK"),
            25,
            "Tools",
            processedAtUtc);

        Assert.Equal(expected, result);
    }
}
