namespace LibraryManagement.Shared.Contracts;

public static class RabbitMqConstants
{
    public const string Exchange = "library.events";
    public const string RoutingKey = "reservation.request";
    public const string QueueName = "reservation.requests";

    // Dead-letter queue
    public const string DeadLetterExchange = "library.dlx";
    public const string DeadLetterRoutingKey = "reservation.dead";
    public const string DeadLetterQueue = "reservation.dlq";

    public const int MaxRetries = 3;
}
