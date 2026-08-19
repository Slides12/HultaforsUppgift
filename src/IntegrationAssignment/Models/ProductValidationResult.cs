namespace IntegrationAssignment.Models;

public sealed record ProductValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
