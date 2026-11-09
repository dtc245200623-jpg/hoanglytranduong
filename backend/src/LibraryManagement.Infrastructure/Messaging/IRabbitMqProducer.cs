namespace LibraryManagement.Infrastructure.Messaging;

public interface IRabbitMqProducer
{
    Task PublishAsync<T>(string routingKey, T message, string? correlationId = null, IDictionary<string, object>? headers = null);
}
