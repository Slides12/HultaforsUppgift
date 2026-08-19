namespace IntegrationAssignment.Models;

public sealed record ExternalProduct(
    string? ProductId,
    string? Name,
    decimal Price,
    string? Currency,
    int StockQuantity,
    string? Category);
