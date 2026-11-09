using LibraryManagement.Shared.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace LibraryManagement.Infrastructure.Messaging;

public class RabbitMqConnectionManager : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private IConnection? _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public RabbitMqConnectionManager(IConfiguration configuration, ILogger<RabbitMqConnectionManager> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConnected => _connection is { IsOpen: true } && !_disposed;

    public IConnection GetConnection()
    {
        if (IsConnected && _connection != null)
        {
            return _connection;
        }

        lock (_lock)
        {
            if (IsConnected && _connection != null)
            {
                return _connection;
            }

            var hostName = _configuration["RabbitMQ:Host"] ?? "localhost";
            var portString = _configuration["RabbitMQ:Port"] ?? "5672";
            int.TryParse(portString, out var port);
            if (port == 0) port = 5672;

            var userName = _configuration["RabbitMQ:UserName"] ?? "guest";
            var password = _configuration["RabbitMQ:Password"] ?? "guest";
            var virtualHost = _configuration["RabbitMQ:VirtualHost"] ?? "/";

            _logger.LogInformation("Connecting to RabbitMQ at {Host}:{Port} (User: {UserName})...", hostName, port, userName);

            var factory = new ConnectionFactory
            {
                HostName = hostName,
                Port = port,
                UserName = userName,
                Password = password,
                VirtualHost = virtualHost,
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            const int maxRetries = 10;
            for (var retry = 1; retry <= maxRetries; retry++)
            {
                try
                {
                    _connection = factory.CreateConnection();
                    _logger.LogInformation("Successfully connected to RabbitMQ broker.");
                    InitializeTopology(_connection);
                    return _connection;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to RabbitMQ (attempt {Attempt}/{MaxRetries}). Retrying in 3s...", retry, maxRetries);
                    if (retry == maxRetries) throw;
                    Thread.Sleep(3000);
                }
            }

            throw new InvalidOperationException("Could not connect to RabbitMQ.");
        }
    }

    public IModel CreateModel()
    {
        return GetConnection().CreateModel();
    }

    private void InitializeTopology(IConnection connection)
    {
        try
        {
            using var channel = connection.CreateModel();

            // 1. Declare Dead Letter Exchange & Queue
            channel.ExchangeDeclare(
                exchange: RabbitMqConstants.DeadLetterExchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false
            );

            channel.QueueDeclare(
                queue: RabbitMqConstants.DeadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false
            );

            channel.QueueBind(
                queue: RabbitMqConstants.DeadLetterQueue,
                exchange: RabbitMqConstants.DeadLetterExchange,
                routingKey: RabbitMqConstants.DeadLetterRoutingKey
            );

            // 2. Declare Main Exchange
            channel.ExchangeDeclare(
                exchange: RabbitMqConstants.Exchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false
            );

            // 3. Declare Main Queue with Dead Letter routing
            var queueArgs = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", RabbitMqConstants.DeadLetterExchange },
                { "x-dead-letter-routing-key", RabbitMqConstants.DeadLetterRoutingKey }
            };

            channel.QueueDeclare(
                queue: RabbitMqConstants.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs
            );

            channel.QueueBind(
                queue: RabbitMqConstants.QueueName,
                exchange: RabbitMqConstants.Exchange,
                routingKey: RabbitMqConstants.RoutingKey
            );

            _logger.LogInformation("RabbitMQ topology (Exchange, Queues, DLQ) declared successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RabbitMQ topology.");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while disposing RabbitMQ connection.");
        }
    }
}
