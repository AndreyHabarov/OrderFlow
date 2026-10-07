using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace OrderFlow.Messaging;

/// <summary>
/// Publishes through one channel with publisher confirms. A confirm means the broker took responsibility for the
/// message (written to disk for persistent messages on durable queues); it does NOT mean a consumer processed it.
/// The channel is not thread-safe, so publishes are serialized.
/// </summary>
internal sealed partial class RabbitMqPublisher(RabbitMqConnection connection, TimeProvider timeProvider, ILogger<RabbitMqPublisher> logger)
    : IMessagePublisher, IAsyncDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task<Guid> PublishAsync<T>(T message, MessageContext? cause = null, CancellationToken cancellationToken = default)
        where T : class
    {
        var routingKey = MessageCatalog.RoutingKeyOf<T>();
        var messageId = Guid.NewGuid();
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = messageId.ToString(),
            CorrelationId = cause?.CorrelationId ?? messageId.ToString("N"),
            Type = routingKey,
            Timestamp = new AmqpTimestamp(timeProvider.GetUtcNow().ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?> { ["version"] = 1 }
        };
        if (cause is { MessageId: var causationId } && causationId != Guid.Empty)
        {
            properties.Headers["causation-id"] = causationId.ToString();
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);

            // mandatory: if no queue is bound for the routing key the broker returns the message instead of silently
            // dropping it. Pure announcements (RequiresConsumer = false) may legitimately have no subscriber.
            await channel.BasicPublishAsync(Topology.EventsExchange, routingKey, MessageCatalog.RequiresConsumer<T>(), properties, body, cancellationToken);
            LogPublished(routingKey, messageId, properties.CorrelationId);
            return messageId;
        }
        catch (PublishException ex)
        {
            throw new MessagePublishException($"The broker did not accept {routingKey} (nacked or unroutable).", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        var amqp = await connection.GetAsync(cancellationToken);
        _channel = await amqp.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqConnection.DeclareExchangeAsync(_channel, cancellationToken);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {RoutingKey} {MessageId} (correlation {CorrelationId})")]
    private partial void LogPublished(string routingKey, Guid messageId, string? correlationId);
}
