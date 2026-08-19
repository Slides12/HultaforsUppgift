using System.Text;
using Microsoft.Extensions.Logging;

namespace IntegrationAssignment.Services;

public sealed class ProductApiClient(
    HttpClient httpClient,
    ILogger<ProductApiClient> logger)
{
    public async Task SendAsync(
        string xml,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "products")
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };
        request.Headers.Add("X-Correlation-Id", correlationId);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Target API rejected product at stage {Stage} with status {Status}. CorrelationId {CorrelationId}, HttpStatusCode {HttpStatusCode}",
                "DeliverProduct",
                "Failed",
                correlationId,
                (int)response.StatusCode);

            throw new HttpRequestException(
                $"Target API returned HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        logger.LogInformation(
            "Product sent to target at stage {Stage} with status {Status}. CorrelationId {CorrelationId}, HttpStatusCode {HttpStatusCode}",
            "DeliverProduct",
            "Delivered",
            correlationId,
            (int)response.StatusCode);
    }
}
