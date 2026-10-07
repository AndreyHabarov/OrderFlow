using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts;
using OrderFlow.Messaging;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace OrderFlow.IntegrationTests.Messaging;

/// <summary>A real RabbitMQ broker for the messaging tests, shared by the whole group.</summary>
public class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4.3.6-alpine").Build();

    public virtual Task InitializeAsync() => _container.StartAsync();

    public virtual Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public virtual Dictionary<string, string?> Settings => new()
    {
        ["RabbitMq:Host"] = _container.Hostname,
        ["RabbitMq:Port"] = _container.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RabbitMq:User"] = "rabbitmq",
        ["RabbitMq:Password"] = "rabbitmq"
    };

    /// <summary>A service host that has the publisher plus whatever <paramref name="configure"/> adds (consumers, handlers).</summary>
    public IHost BuildHost(Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(Settings);
        builder.Logging.ClearProviders();
        builder.Services.AddRabbitMq(builder.Configuration);
        configure?.Invoke(builder.Services);
        return builder.Build();
    }

    public async Task<IConnection> OpenConnectionAsync()
    {
        var settings = Settings;
        var factory = new ConnectionFactory
        {
            HostName = settings["RabbitMq:Host"]!,
            Port = int.Parse(settings["RabbitMq:Port"]!, System.Globalization.CultureInfo.InvariantCulture),
            UserName = "rabbitmq",
            Password = "rabbitmq"
        };
        return await factory.CreateConnectionAsync();
    }

    /// <summary>Messages waiting in the queue (ready, not yet delivered to a consumer).</summary>
    public async Task<uint> ReadyMessagesAsync(string queue)
    {
        await using var connection = await OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    /// <summary>Declares a queue bound to the key without consuming it, so a test can inspect raw messages.</summary>
    public async Task DeclareBoundQueueAsync(string queue, string routingKey)
    {
        await using var connection = await OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(Topology.EventsExchange, ExchangeType.Topic, durable: true);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(queue, Topology.EventsExchange, routingKey);
    }

    /// <summary>Empties a queue (declaring it first) so leftovers of an earlier test cannot leak into this one.</summary>
    public async Task PurgeQueueAsync(Topology.QueueDefinition queue)
    {
        await using var connection = await OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(Topology.EventsExchange, ExchangeType.Topic, durable: true);
        await channel.QueueDeclareAsync(queue.Name, durable: true, exclusive: false, autoDelete: false);
        foreach (var key in queue.RoutingKeys)
        {
            await channel.QueueBindAsync(queue.Name, Topology.EventsExchange, key);
        }

        await channel.QueuePurgeAsync(queue.Name);
    }

    /// <summary>Reads messages from a queue that is bound but not consumed, until <paramref name="expected"/> arrived or time ran out.</summary>
    public async Task<List<T>> ReceiveAsync<T>(string queue, int expected, int seconds = 20)
    {
        var found = new List<T>();
        await using var connection = await OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (found.Count < expected && DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: true);
            if (result is null)
            {
                await Task.Delay(50);
                continue;
            }

            found.Add(System.Text.Json.JsonSerializer.Deserialize<T>(result.Body.Span, JsonOptions)!);
        }

        return found;
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static Topology.QueueDefinition NewQueue(params string[] routingKeys) => new($"test.{Guid.NewGuid():N}", routingKeys);

    public static async Task EventuallyAsync(Func<Task<bool>> condition, string because, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Timed out waiting for: {because}");
    }
}

[CollectionDefinition(Name)]
public sealed class RabbitMqTestGroup : ICollectionFixture<RabbitMqFixture>
{
    public const string Name = "rabbitmq";
}

/// <summary>Collects what handlers received so a test can wait for it.</summary>
public sealed class Sink
{
    public ConcurrentQueue<(StockReserved Message, MessageContext Context)> Received { get; } = new();

    public Task WaitForAsync(int count, int seconds = 15) =>
        RabbitMqFixture.EventuallyAsync(() => Task.FromResult(Received.Count >= count), $"{count} message(s) to be handled", seconds);
}

public sealed class RecordingHandler(Sink sink) : IMessageHandler<StockReserved>
{
    public Task HandleAsync(StockReserved message, MessageContext context, CancellationToken cancellationToken)
    {
        sink.Received.Enqueue((message, context));
        return Task.CompletedTask;
    }
}

/// <summary>Fails on the first delivery and succeeds when the broker redelivers the same message.</summary>
public sealed class FlakyHandler(Sink sink) : IMessageHandler<StockReserved>
{
    public Task HandleAsync(StockReserved message, MessageContext context, CancellationToken cancellationToken)
    {
        if (!context.Redelivered)
        {
            throw new InvalidOperationException("simulated transient failure");
        }

        sink.Received.Enqueue((message, context));
        return Task.CompletedTask;
    }
}

/// <summary>Signals when it starts and then waits for the test to let it finish.</summary>
public sealed class Gate
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _completed;

    public int Completed => Volatile.Read(ref _completed);

    public void MarkCompleted() => Interlocked.Increment(ref _completed);
}

public sealed class BlockingHandler(Gate gate) : IMessageHandler<StockReserved>
{
    public async Task HandleAsync(StockReserved message, MessageContext context, CancellationToken cancellationToken)
    {
        gate.Started.TrySetResult();
        await gate.Release.Task;
        gate.MarkCompleted();
    }
}
