using System.Text.Json;
using IntegrationAssignment.Models;
using IntegrationAssignment.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace IntegrationAssignment.Functions;

public sealed class Int001HfProductsIngestFunction(
    RabbitMqPublisher publisher,
    ILogger<Int001HfProductsIngestFunction> logger)
{
    private const string FunctionName = "int001-hf-products-ingest";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Function(FunctionName)]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "products")] HttpRequest request)
    {
        ExternalProduct? product;

        try
        {
            product = await JsonSerializer.DeserializeAsync<ExternalProduct>(
                request.Body,
                SerializerOptions,
                request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            logger.LogWarning(
                "Malformed product JSON rejected by {FunctionName} at stage {Stage} with status {Status}",
                FunctionName,
                "Ingest",
                "Rejected");

            return new BadRequestObjectResult(new
            {
                errorCode = "MALFORMED_JSON",
                message = "The request body must contain a valid JSON product."
            });
        }

        if (product is null)
        {
            return new BadRequestObjectResult(new
            {
                errorCode = "EMPTY_BODY",
                message = "The request body must contain a JSON product."
            });
        }

        var correlationId = GetCorrelationId(request);
        var envelope = new ProductEnvelope<ExternalProduct>(
            correlationId,
            DateTimeOffset.UtcNow,
            product);

        logger.LogInformation(
            "Product received by {FunctionName} at stage {Stage} with status {Status}. ProductId {ProductId}, CorrelationId {CorrelationId}, ReceivedAtUtc {ReceivedAtUtc}",
            FunctionName,
            "Ingest",
            "Accepted",
            envelope.Payload.ProductId,
            envelope.CorrelationId,
            envelope.ReceivedAtUtc);

        try
        {
            await publisher.PublishReceivedAsync(
                envelope,
                correlationId,
                request.HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Product could not be queued by {FunctionName} at stage {Stage} with status {Status}. ProductId {ProductId}, CorrelationId {CorrelationId}",
                FunctionName,
                "QueueProduct",
                "Failed",
                product.ProductId,
                correlationId);

            return new ObjectResult(new
            {
                errorCode = "MESSAGING_UNAVAILABLE",
                message = "The product could not be accepted for asynchronous processing.",
                correlationId
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }

        request.HttpContext.Response.Headers["X-Correlation-Id"] = correlationId;

        return new AcceptedResult(location: null, value: new { correlationId });
    }

    private static string GetCorrelationId(HttpRequest request)
    {
        var suppliedCorrelationId = request.Headers["X-Correlation-Id"]
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return suppliedCorrelationId?.Trim() ?? Guid.NewGuid().ToString();
    }
}
