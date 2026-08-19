namespace IntegrationAssignment.Models;

public sealed record CanonicalProduct(
    string? Id,
    string? DisplayName,
    Money UnitPrice,
    int AvailableQuantity,
    string? ProductCategory,
    DateTimeOffset ProcessedAtUtc);
