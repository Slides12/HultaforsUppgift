using System.Text.Json;
using IntegrationAssignment.Models;
using IntegrationAssignment.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace IntegrationAssignment.Functions;

public sealed class Int001HfProductsProcessFunction(
    ProductTransformer transformer,
    ProductValidator validator,
    ProductXmlSerializer xmlSerializer,
    ProductXmlValidator xmlValidator,
    RabbitMqPublisher publisher,
    ILogger<Int001HfProductsProcessFunction> logger)
{
    private const string FunctionName = "int001-hf-products-process";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Function(FunctionName)]
    public async Task RunAsync(
        [RabbitMQTrigger(
            "%ProductReceivedQueueName%",
            ConnectionStringSetting = "RabbitMQConnection")]
        string message,
        CancellationToken cancellationToken)
    {
        ProductEnvelope<ExternalProduct> envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<ProductEnvelope<ExternalProduct>>(
                message,
                SerializerOptions) ?? throw new JsonException("The product envelope was empty.");
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "RabbitMQ message could not be deserialized by {FunctionName} at stage {Stage} with status {Status}",
                FunctionName,
                "Deserialize",
                "Failed");

            throw;
        }

        logger.LogInformation(
            "Product transformation started by {FunctionName} at stage {Stage} with status {Status}. ProductId {ProductId}, CorrelationId {CorrelationId}",
            FunctionName,
            "Transform",
            "Started",
            envelope.Payload.ProductId,
            envelope.CorrelationId);

        var canonicalProduct = transformer.Transform(envelope.Payload, DateTimeOffset.UtcNow);

        logger.LogInformation(
            "Product transformed by {FunctionName} at stage {Stage} with status {Status}. ProductId {ProductId}, CorrelationId {CorrelationId}",
            FunctionName,
            "Transform",
            "Completed",
            canonicalProduct.Id,
            envelope.CorrelationId);

        var validation = validator.Validate(canonicalProduct);

        if (!validation.IsValid)
        {
            var errorMessage = new ErrorProductMessage(
                envelope.CorrelationId,
                envelope.Payload.ProductId,
                "INVALID_PRODUCT",
                validation.Errors,
                envelope.Payload);

            await publisher.PublishInvalidAsync(
                errorMessage,
                envelope.CorrelationId,
                cancellationToken);

            logger.LogWarning(
                "Product validation failed in {FunctionName} at stage {Stage} with status {Status}. ProductId {ProductId}, CorrelationId {CorrelationId}, ErrorCode {ErrorCode}, Errors {Errors}",
                FunctionName,
                "Validate",
                "Rejected",
                envelope.Payload.ProductId,
                envelope.CorrelationId,
                errorMessage.ErrorCode,
                validation.Errors);

            return;
        }

        var canonicalDocument = new CanonicalProductDocument(
            envelope.CorrelationId,
            envelope.ReceivedAtUtc,
            canonicalProduct);

        string xml;
        try
        {
            xml = xmlSerializer.Serialize(canonicalDocument);
            xmlValidator.Validate(xml);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Canonical XML creation failed in {FunctionName} at stage {Stage} with status {Status}. ProductId {ProductId}, CorrelationId {CorrelationId}",
                FunctionName,
                "CreateCanonicalXml",
                "Failed",
                canonicalProduct.Id,
                envelope.CorrelationId);

            throw;
        }

        await publisher.PublishValidXmlAsync(
            xml,
            envelope.CorrelationId,
            cancellationToken);

        logger.LogInformation(
            "Canonical product XML validated and routed by {FunctionName} at stage {Stage} with status {Status}. ProductId {ProductId}, CorrelationId {CorrelationId}",
            FunctionName,
            "PublishCanonicalXml",
            "Accepted",
            canonicalProduct.Id,
            envelope.CorrelationId);
    }
}
