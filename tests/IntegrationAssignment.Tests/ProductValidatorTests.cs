using IntegrationAssignment.Models;
using IntegrationAssignment.Services;

namespace IntegrationAssignment.Tests;

public sealed class ProductValidatorTests
{
    private readonly ProductValidator _validator = new();

    [Fact]
    public void Validate_ValidProduct_Succeeds()
    {
        var product = CreateValidProduct();

        var result = _validator.Validate(product);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MissingProductId_Fails()
    {
        var product = CreateValidProduct() with { Id = null };

        var result = _validator.Validate(product);

        Assert.Contains("ProductId is required", result.Errors);
    }

    [Fact]
    public void Validate_NegativePrice_Fails()
    {
        var product = CreateValidProduct() with { UnitPrice = new Money(-1m, "SEK") };

        var result = _validator.Validate(product);

        Assert.Contains("Price must be greater than zero", result.Errors);
    }

    [Fact]
    public void Validate_NegativeStockQuantity_Fails()
    {
        var product = CreateValidProduct() with { AvailableQuantity = -1 };

        var result = _validator.Validate(product);

        Assert.Contains("StockQuantity must be zero or greater", result.Errors);
    }

    [Fact]
    public void Validate_UnsupportedCurrency_Fails()
    {
        var product = CreateValidProduct() with { UnitPrice = new Money(10m, "GBP") };

        var result = _validator.Validate(product);

        Assert.Contains("Currency must be SEK, EUR, or USD", result.Errors);
    }

    private static CanonicalProduct CreateValidProduct() =>
        new(
            "P-1001",
            "Hammer",
            new Money(249.90m, "SEK"),
            25,
            "Tools",
            DateTimeOffset.Parse("2026-08-14T12:00:00Z"));
}
