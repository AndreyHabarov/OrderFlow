namespace OrderFlow.Messaging;

/// <summary>Connection settings, bound from the <c>RabbitMq</c> configuration section (same keys the API already uses).</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Shown in the RabbitMQ management UI next to each connection.</summary>
    public string ClientName { get; set; } = "orderflow";
}

/// <summary>
/// Metadata of one message. It travels in AMQP properties and headers, not in the JSON body, so it is the same for
/// every message type: <c>message-id</c>, <c>correlation-id</c> (one id for the whole business flow),
/// <c>causation-id</c> (the message that caused this one) and the broker's <c>redelivered</c> flag.
/// </summary>
public sealed record MessageContext(
    Guid MessageId,
    string CorrelationId,
    Guid? CausationId,
    string RoutingKey,
    DateTimeOffset Timestamp,
    bool Redelivered)
{
    /// <summary>Starts a new flow, for example from an HTTP request that carries its own correlation id.</summary>
    public static MessageContext ForNewFlow(string correlationId) =>
        new(Guid.Empty, correlationId, null, string.Empty, DateTimeOffset.UtcNow, false);
}

public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a persistent message and waits for the broker's confirmation. Pass the context of the message being
    /// handled as <paramref name="cause"/> so correlation and causation ids are carried forward.
    /// Returns the id of the published message. Throws <see cref="MessagePublishException"/> when the broker does not
    /// confirm it or no queue is bound for its routing key.
    /// </summary>
    Task<Guid> PublishAsync<T>(T message, MessageContext? cause = null, CancellationToken cancellationToken = default)
        where T : class;
}

public interface IMessageHandler<in T>
    where T : class
{
    /// <summary>
    /// Handles one delivery. Returning normally acknowledges it; throwing makes the consumer requeue it. Delivery is
    /// at-least-once, so the handler must tolerate seeing the same message again.
    /// </summary>
    Task HandleAsync(T message, MessageContext context, CancellationToken cancellationToken);
}

/// <summary>Publishing failed: the broker refused the message or it was not routable.</summary>
public sealed class MessagePublishException : Exception
{
    public MessagePublishException(string message) : base(message)
    {
    }

    public MessagePublishException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public MessagePublishException()
    {
    }
}

public sealed class ConsumerOptions
{
    /// <summary>Maximum unacknowledged messages the broker sends to this consumer (flow control and fairness).</summary>
    public ushort Prefetch { get; set; } = 10;

    /// <summary>How long a graceful stop waits for the message being handled before giving up.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Pause before a failed message is returned to the queue, so a persistent failure does not spin the CPU.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
