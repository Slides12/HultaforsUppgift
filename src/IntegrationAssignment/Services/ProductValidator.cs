using IntegrationAssignment.Models;

namespace IntegrationAssignment.Services;

public sealed class ProductValidator
{
    private static readonly HashSet<string> SupportedCurrencies =
        new(StringComparer.Ordinal) { "SEK", "EUR", "USD" };

    public ProductValidationResult Validate(CanonicalProduct product)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(product.Id))
        {
            errors.Add("ProductId is required");
        }

        if (string.IsNullOrWhiteSpace(product.DisplayName))
        {
            errors.Add("Name is required");
        }

        if (product.UnitPrice.Amount <= 0)
        {
            errors.Add("Price must be greater than zero");
        }

        if (product.AvailableQuantity < 0)
        {
            errors.Add("StockQuantity must be zero or greater");
        }

        if (product.UnitPrice.Currency is null ||
            !SupportedCurrencies.Contains(product.UnitPrice.Currency))
        {
            errors.Add("Currency must be SEK, EUR, or USD");
        }

        return new ProductValidationResult(errors);
    }
}
