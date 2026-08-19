namespace IntegrationAssignment.Models;

public sealed record ProductEnvelope<T>(
    string CorrelationId,
    DateTimeOffset ReceivedAtUtc,
    T Payload);
