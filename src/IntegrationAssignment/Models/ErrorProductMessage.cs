namespace IntegrationAssignment.Models;

public sealed record ErrorProductMessage(
    string CorrelationId,
    string? ProductId,
    string ErrorCode,
    IReadOnlyList<string> Errors,
    ExternalProduct OriginalPayload);
