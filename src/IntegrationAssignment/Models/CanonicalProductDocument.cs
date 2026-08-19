namespace IntegrationAssignment.Models;

public sealed record CanonicalProductDocument(
    string CorrelationId,
    DateTimeOffset ReceivedAtUtc,
    CanonicalProduct Product);
