using System.Text;
using System.Text.Json;
using LibraryManagement.Shared.Contracts;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace LibraryManagement.Infrastructure.Messaging;

public class RabbitMqProducer : IRabbitMqProducer
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly ILogger<RabbitMqProducer> _logger;

    public RabbitMqProducer(RabbitMqConnectionManager connectionManager, ILogger<RabbitMqProducer> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public Task PublishAsync<T>(string routingKey, T message, string? correlationId = null, IDictionary<string, object>? headers = null)
    {
        using var channel = _connectionManager.CreateModel();

        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.CorrelationId = correlationId ?? Guid.NewGuid().ToString();
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        if (headers != null)
        {
            properties.Headers = headers;
        }

        channel.BasicPublish(
            exchange: RabbitMqConstants.Exchange,
            routingKey: routingKey,
            basicProperties: properties,
            body: body
        );

        _logger.LogInformation(
            "--> Published message to RabbitMQ [Exchange: {Exchange}, RoutingKey: {RoutingKey}, CorrelationId: {CorrelationId}]",
            RabbitMqConstants.Exchange,
            routingKey,
            properties.CorrelationId
        );

        return Task.CompletedTask;
    }
}
