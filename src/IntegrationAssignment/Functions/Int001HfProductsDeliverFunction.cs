using System.Xml;
using System.Xml.Linq;
using IntegrationAssignment.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace IntegrationAssignment.Functions;

public sealed class Int001HfProductsDeliverFunction(
    ProductApiClient productApiClient,
    ILogger<Int001HfProductsDeliverFunction> logger)
{
    private const string FunctionName = "int001-hf-products-deliver";

    [Function(FunctionName)]
    public async Task RunAsync(
        [RabbitMQTrigger(
            "%ProductValidQueueName%",
            ConnectionStringSetting = "RabbitMQConnection")]
        string message,
        CancellationToken cancellationToken)
    {
        XDocument document;
        string correlationId;

        try
        {
            document = XDocument.Parse(message);
            var parsedCorrelationId = document.Root?.Element("CorrelationId")?.Value;
            if (string.IsNullOrWhiteSpace(parsedCorrelationId))
            {
                throw new XmlException("The canonical XML has no CorrelationId.");
            }

            correlationId = parsedCorrelationId;
        }
        catch (XmlException exception)
        {
            logger.LogError(
                exception,
                "Canonical XML could not be read by {FunctionName} at stage {Stage} with status {Status}",
                FunctionName,
                "ReadCanonicalXml",
                "Failed");

            throw;
        }

        logger.LogInformation(
            "Product delivery started by {FunctionName} at stage {Stage} with status {Status}. ProductId {ProductId}, CorrelationId {CorrelationId}",
            FunctionName,
            "DeliverProduct",
            "Started",
            document.Root?.Element("Product")?.Element("Id")?.Value,
            correlationId);

        await productApiClient.SendAsync(
            message,
            correlationId,
            cancellationToken);
    }
}
