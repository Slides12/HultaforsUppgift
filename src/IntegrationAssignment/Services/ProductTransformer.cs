using IntegrationAssignment.Models;

namespace IntegrationAssignment.Services;

public sealed class ProductTransformer
{
    public CanonicalProduct Transform(ExternalProduct product, DateTimeOffset processedAtUtc) =>
        new(
            Normalize(product.ProductId),
            Normalize(product.Name),
            new Money(product.Price, Normalize(product.Currency)?.ToUpperInvariant()),
            product.StockQuantity,
            Normalize(product.Category),
            processedAtUtc);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
