using System.Text;
using System.Text.Json;
using IntegrationAssignment.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace IntegrationAssignment.Services;

public sealed class RabbitMqPublisher : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqOptions _rabbitMq;
    private readonly QueueOptions _queues;
    private readonly ExchangeOptions _exchanges;
    private readonly RoutingKeyOptions _routingKeys;
    private readonly ILogger<RabbitMqPublisher> _logger;

    // Semaphore to ensure that only one Function execution uses the shared channel at a time.
    private readonly SemaphoreSlim _channelLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    // Gets the RabbitMQ connection, queue, exchange, and routing settings from configuration.
    public RabbitMqPublisher(
        IOptions<RabbitMqOptions> rabbitMq,
        IOptions<QueueOptions> queues,
        IOptions<ExchangeOptions> exchanges,
        IOptions<RoutingKeyOptions> routingKeys,
        ILogger<RabbitMqPublisher> logger)
    {
        _rabbitMq = rabbitMq.Value;
        _queues = queues.Value;
        _exchanges = exchanges.Value;
        _routingKeys = routingKeys.Value;
        _logger = logger;
    }

    // Publishes a newly received product as JSON directly to the first queue.
    public Task PublishReceivedAsync<T>(T message, string correlationId, CancellationToken cancellationToken) =>
        PublishAsync(string.Empty, _queues.ProductReceived, message, correlationId, cancellationToken);

    // Publishes a product that failed validation as JSON to the invalid queue.
    public Task PublishInvalidAsync<T>(T message, string correlationId, CancellationToken cancellationToken) =>
        PublishAsync(
            _exchanges.ProductValidationResults,
            _routingKeys.ProductInvalid,
            message,
            correlationId,
            cancellationToken);

    // Send valid XML and publishes it to the valid queue.
    public Task PublishValidXmlAsync(
        string xml,
        string correlationId,
        CancellationToken cancellationToken) =>
        PublishRawAsync(
            _exchanges.ProductValidationResults,
            _routingKeys.ProductValid,
            Encoding.UTF8.GetBytes(xml),
            "application/xml",
            "utf-8",
            "product.delivery.v1",
            correlationId,
            cancellationToken);

    // Connects to RabbitMQ and creates the queues, exchange, and routing rules at startup.
    // The lock makes sure only one Function execution uses the shared channel at a time.
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            await GetChannelAsync(cancellationToken);

            _logger.LogInformation(
                "RabbitMQ topology initialized at stage {Stage} with status {Status}",
                "RabbitMqTopology",
                "Ready");
        }
        finally
        {
            _channelLock.Release();
        }
    }

    // Converts a C# object to JSON bytes and passes it to the raw publishing method.
    private async Task PublishAsync<T>(
        string exchange,
        string routingKey,
        T message,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);

        await PublishRawAsync(
            exchange,
            routingKey,
            body,
            "application/json",
            "utf-8",
            messageType: null,
            correlationId,
            cancellationToken);
    }

    // Adds message information and sends the supplied bytes to RabbitMQ.
    private async Task PublishRawAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> body,
        string contentType,
        string contentEncoding,
        string? messageType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                ContentType = contentType,
                ContentEncoding = contentEncoding,
                CorrelationId = correlationId,
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Type = messageType
            };

            await channel.BasicPublishAsync(
                exchange,
                routingKey,
                mandatory: true,
                properties,
                body,
                cancellationToken);

            _logger.LogInformation(
                "Message published at stage {Stage} with status {Status}. Exchange {Exchange}, RoutingKey {RoutingKey}, CorrelationId {CorrelationId}",
                "RabbitMqPublish",
                "Published",
                string.IsNullOrEmpty(exchange) ? "(default)" : exchange,
                routingKey,
                correlationId);
        }
        finally
        {
            _channelLock.Release();
        }
    }

    // Reuses the current channel, or creates a new connection and channel if needed.
    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_connection is not { IsOpen: true })
        {
            var factory = new ConnectionFactory
            {
                HostName = _rabbitMq.Host,
                Port = _rabbitMq.Port,
                UserName = _rabbitMq.Username,
                Password = _rabbitMq.Password,
                VirtualHost = _rabbitMq.VirtualHost,
                AutomaticRecoveryEnabled = true
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
        }

        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        _channel = await _connection.CreateChannelAsync(channelOptions, cancellationToken);
        await DeclareTopologyAsync(_channel, cancellationToken);

        return _channel;
    }

    // Creates the queues and exchange, then connects them using routing keys.
    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(
            _queues.ProductReceived,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            _queues.ProductValid,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            _queues.ProductInvalid,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            _exchanges.ProductValidationResults,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            _queues.ProductValid,
            _exchanges.ProductValidationResults,
            _routingKeys.ProductValid,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            _queues.ProductInvalid,
            _exchanges.ProductValidationResults,
            _routingKeys.ProductInvalid,
            cancellationToken: cancellationToken);
    }

    // Closes the RabbitMQ channel and connection when the application stops.
    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _channelLock.Dispose();
    }
}
