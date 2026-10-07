using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace OrderFlow.Messaging;

/// <summary>
/// Consumes one queue with manual acknowledgements.
/// <list type="bullet">
/// <item>Handled successfully: ack. Handler threw: wait a moment, then nack with requeue (the message is tried again).</item>
/// <item>Unknown routing key or unreadable body: nack WITHOUT requeue, because retrying can never succeed
/// (stage 3 routes these to a dead-letter queue instead of losing them).</item>
/// <item>Prefetch limits how many unacknowledged messages the broker pushes at once; messages are handled one at a time.</item>
/// <item>Graceful stop: cancel the consumer so no new messages arrive, wait for the message in progress, then close.
/// Anything unacknowledged at that point goes back to the queue automatically.</item>
/// </list>
/// </summary>
internal sealed partial class RabbitMqConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    Topology.QueueDefinition queue,
    IReadOnlyDictionary<string, HandlerRegistration> registrations,
    ConsumerOptions options,
    ILogger<RabbitMqConsumer> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _workLock = new();
    private IChannel? _channel;
    private string? _consumerTag;
    private int _activeHandlers;
    private TaskCompletionSource _drained = CompletedSignal();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var missing = queue.RoutingKeys.Where(key => !registrations.ContainsKey(key)).ToList();
        if (missing.Count > 0)
        {
            // A bound key without a handler would deliver messages nobody can process; fail at startup, not at 3 a.m.
            throw new InvalidOperationException($"Queue {queue.Name} is bound to {string.Join(", ", missing)} but has no handler for it.");
        }

        var amqp = await connection.GetAsync(cancellationToken);
        _channel = await amqp.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqConnection.DeclareExchangeAsync(_channel, cancellationToken);
        await _channel.QueueDeclareAsync(queue.Name, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        foreach (var routingKey in queue.RoutingKeys)
        {
            await _channel.QueueBindAsync(queue.Name, Topology.EventsExchange, routingKey, cancellationToken: cancellationToken);
        }

        await _channel.BasicQosAsync(prefetchSize: 0, options.Prefetch, global: false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;
        _consumerTag = await _channel.BasicConsumeAsync(queue.Name, autoAck: false, consumer, cancellationToken);
        LogStarted(queue.Name, options.Prefetch);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            if (_consumerTag is not null)
            {
                await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken);
            }

            Task drained;
            lock (_workLock)
            {
                drained = _drained.Task;
            }

            var finished = await Task.WhenAny(drained, Task.Delay(options.ShutdownTimeout, cancellationToken)) == drained;
            if (!finished)
            {
                LogShutdownTimeout(queue.Name, options.ShutdownTimeout);
                await _shutdown.CancelAsync();
                await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
            }

            await _channel.CloseAsync(cancellationToken);
        }
        catch (AlreadyClosedException)
        {
            // the connection is already gone; unacknowledged messages were requeued by the broker
        }
        finally
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        LogStopped(queue.Name);
    }

    public void Dispose() => _shutdown.Dispose();

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs delivery)
    {
        BeginWork();
        try
        {
            await ProcessAsync(delivery);
        }
        finally
        {
            EndWork();
        }
    }

    private async Task ProcessAsync(BasicDeliverEventArgs delivery)
    {
        var channel = _channel!;
        var routingKey = delivery.RoutingKey;

        if (!registrations.TryGetValue(routingKey, out var registration))
        {
            LogRejected(routingKey, "no handler is registered for this routing key");
            await NackAsync(channel, delivery, requeue: false);
            return;
        }

        object message;
        try
        {
            message = JsonSerializer.Deserialize(delivery.Body.Span, registration.MessageType, RabbitMqPublisher.JsonOptions)
                      ?? throw new JsonException("The message body is null.");
        }
        catch (JsonException ex)
        {
            LogRejected(routingKey, ex.Message);
            await NackAsync(channel, delivery, requeue: false);
            return;
        }

        var context = ReadContext(delivery);
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = context.MessageId,
            ["CorrelationId"] = context.CorrelationId,
            ["RoutingKey"] = routingKey
        });

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await registration.InvokeAsync(scope.ServiceProvider, message, context, _shutdown.Token);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
            LogHandled(routingKey, context.MessageId, context.Redelivered);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogHandlerFailed(ex, routingKey, context.MessageId);
            if (!_shutdown.IsCancellationRequested)
            {
                await Task.Delay(options.RetryDelay);
            }

            await NackAsync(channel, delivery, requeue: true);
        }
    }

    private static async Task NackAsync(IChannel channel, BasicDeliverEventArgs delivery, bool requeue)
    {
        try
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue);
        }
        catch (AlreadyClosedException)
        {
            // channel closed while we were failing; the broker already requeued the message
        }
    }

    private static MessageContext ReadContext(BasicDeliverEventArgs delivery)
    {
        var properties = delivery.BasicProperties;
        Guid? causation = null;
        if (properties.Headers is { } headers && headers.TryGetValue("causation-id", out var raw) && raw is byte[] bytes
            && Guid.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var parsed))
        {
            causation = parsed;
        }

        return new MessageContext(
            Guid.TryParse(properties.MessageId, out var messageId) ? messageId : Guid.Empty,
            properties.CorrelationId ?? string.Empty,
            causation,
            delivery.RoutingKey,
            DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime),
            delivery.Redelivered);
    }

    private void BeginWork()
    {
        lock (_workLock)
        {
            if (_activeHandlers++ == 0)
            {
                _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private void EndWork()
    {
        lock (_workLock)
        {
            if (--_activeHandlers == 0)
            {
                _drained.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming {Queue} (prefetch {Prefetch})")]
    private partial void LogStarted(string queue, ushort prefetch);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped consuming {Queue}")]
    private partial void LogStopped(string queue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Queue}: the message in progress did not finish within {Timeout}; cancelling it")]
    private partial void LogShutdownTimeout(string queue, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RoutingKey} {MessageId} (redelivered: {Redelivered})")]
    private partial void LogHandled(string routingKey, Guid messageId, bool redelivered);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler failed for {RoutingKey} {MessageId}; the message will be requeued")]
    private partial void LogHandlerFailed(Exception exception, string routingKey, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rejected {RoutingKey} without requeue: {Reason}")]
    private partial void LogRejected(string routingKey, string reason);
}
