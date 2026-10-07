using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Contracts;
using OrderFlow.Messaging;
using RabbitMQ.Client;

namespace OrderFlow.IntegrationTests.Messaging;

[Collection(RabbitMqTestGroup.Name)]
public sealed class MessagingTests(RabbitMqFixture rabbit)
{
    private static readonly string StockReservedKey = MessageCatalog.RoutingKeyOf<StockReserved>();

    private static StockReserved SampleMessage() => new(Guid.NewGuid(), Guid.NewGuid(), 25m, "USD");

    private static TestHost ConsumerHost(RabbitMqFixture rabbit, Topology.QueueDefinition queue, Sink sink, Action<IServiceCollection>? extra = null) =>
        rabbit.BuildHost(services =>
        {
            services.AddSingleton(sink);
            services.AddMessageConsumer(queue, builder => builder.Handle<StockReserved, RecordingHandler>(), options =>
            {
                options.RetryDelay = TimeSpan.FromMilliseconds(100);
            });
            extra?.Invoke(services);
        });

    [Fact]
    public async Task A_published_message_reaches_the_handler_with_payload_and_envelope()
    {
        var queue = RabbitMqFixture.NewQueue(StockReservedKey);
        var sink = new Sink();
        await using var host = ConsumerHost(rabbit, queue, sink);
        await host.StartAsync();

        var message = SampleMessage();
        var publisher = host.Services.GetRequiredService<IMessagePublisher>();
        var id = await publisher.PublishAsync(message, MessageContext.ForNewFlow("flow-42"));
        await sink.WaitForAsync(1);

        var (received, context) = sink.Received.Single();
        Assert.Equal(message, received);
        Assert.Equal(id, context.MessageId);
        Assert.Equal("flow-42", context.CorrelationId);
        Assert.Equal(StockReservedKey, context.RoutingKey);
        Assert.False(context.Redelivered);
        await host.StopAsync();
    }

    [Fact]
    public async Task Messages_are_persistent_and_causation_is_carried_forward()
    {
        var queue = RabbitMqFixture.NewQueue(StockReservedKey);
        await rabbit.DeclareBoundQueueAsync(queue.Name, StockReservedKey);
        await using var host = rabbit.BuildHost();
        var publisher = host.Services.GetRequiredService<IMessagePublisher>();

        var firstId = await publisher.PublishAsync(SampleMessage(), MessageContext.ForNewFlow("flow-7"));
        var cause = new MessageContext(firstId, "flow-7", null, StockReservedKey, DateTimeOffset.UtcNow, false);
        var secondId = await publisher.PublishAsync(SampleMessage(), cause);

        await using var connection = await rabbit.OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var first = await channel.BasicGetAsync(queue.Name, autoAck: true);
        var second = await channel.BasicGetAsync(queue.Name, autoAck: true);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(DeliveryModes.Persistent, first.BasicProperties.DeliveryMode);
        Assert.Equal("application/json", first.BasicProperties.ContentType);
        Assert.Equal(StockReservedKey, first.BasicProperties.Type);
        Assert.Equal(firstId.ToString(), first.BasicProperties.MessageId);
        Assert.Equal("flow-7", second.BasicProperties.CorrelationId);
        Assert.Equal(secondId.ToString(), second.BasicProperties.MessageId);
        var causation = second.BasicProperties.Headers!["causation-id"];
        Assert.Equal(firstId.ToString(), Encoding.UTF8.GetString((byte[])causation!));
    }

    [Fact]
    public async Task A_message_nobody_is_bound_to_is_refused_instead_of_silently_dropped()
    {
        await using var host = rabbit.BuildHost();
        var publisher = host.Services.GetRequiredService<IMessagePublisher>();

        // No queue is bound for stock.reservation-failed in these tests, and this message requires a consumer.
        await Assert.ThrowsAsync<MessagePublishException>(() =>
            publisher.PublishAsync(new StockReservationFailed(Guid.NewGuid(), "no consumer")));
    }

    [Fact]
    public async Task A_pure_announcement_without_subscribers_is_published_without_error()
    {
        // order.confirmed is a fact: nobody may be listening yet, and that must not fail the publisher.
        await using var host = rabbit.BuildHost();

        var id = await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(new OrderConfirmed(Guid.NewGuid(), Guid.NewGuid()));

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task A_failing_handler_gets_the_same_message_again_and_then_the_queue_is_empty()
    {
        var queue = RabbitMqFixture.NewQueue(StockReservedKey);
        var sink = new Sink();
        await using var host = rabbit.BuildHost(services =>
        {
            services.AddSingleton(sink);
            services.AddMessageConsumer(queue, builder => builder.Handle<StockReserved, FlakyHandler>(), o => o.RetryDelay = TimeSpan.FromMilliseconds(100));
        });
        await host.StartAsync();

        await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(SampleMessage());
        await sink.WaitForAsync(1);

        Assert.True(sink.Received.Single().Context.Redelivered);
        await RabbitMqFixture.EventuallyAsync(async () => await rabbit.ReadyMessagesAsync(queue.Name) == 0, "the queue to drain");
        await host.StopAsync();
    }

    [Fact]
    public async Task Messages_wait_in_the_durable_queue_while_the_consumer_is_down_and_are_processed_in_order_after_it_starts()
    {
        var queue = RabbitMqFixture.NewQueue(StockReservedKey);
        var sink = new Sink();

        await using (var firstRun = ConsumerHost(rabbit, queue, sink))
        {
            await firstRun.StartAsync(); // declares the queue and its binding
            await firstRun.StopAsync();
        }

        await using var publisherHost = rabbit.BuildHost();
        var publisher = publisherHost.Services.GetRequiredService<IMessagePublisher>();
        var messages = Enumerable.Range(0, 3).Select(_ => SampleMessage()).ToList();
        foreach (var message in messages)
        {
            await publisher.PublishAsync(message);
        }

        Assert.Equal(3u, await rabbit.ReadyMessagesAsync(queue.Name));
        Assert.Empty(sink.Received);

        await using var secondRun = ConsumerHost(rabbit, queue, sink);
        await secondRun.StartAsync();
        await sink.WaitForAsync(3);

        Assert.Equal(messages, sink.Received.Select(r => r.Message).ToList());
        await secondRun.StopAsync();
    }

    [Fact]
    public async Task A_graceful_stop_waits_for_the_message_in_progress_and_acknowledges_it()
    {
        var queue = RabbitMqFixture.NewQueue(StockReservedKey);
        var gate = new Gate();
        await using var host = rabbit.BuildHost(services =>
        {
            services.AddSingleton(gate);
            services.AddMessageConsumer(queue, builder => builder.Handle<StockReserved, BlockingHandler>());
        });
        await host.StartAsync();
        await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(SampleMessage());
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var stopping = host.StopAsync();
        await Task.Delay(500);
        Assert.False(stopping.IsCompleted, "StopAsync must wait for the handler that is still running");

        gate.Release.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(1, gate.Completed);
        Assert.Equal(0u, await rabbit.ReadyMessagesAsync(queue.Name)); // acknowledged, so not requeued
    }

    [Fact]
    public async Task Prefetch_limits_how_many_messages_are_pushed_to_a_busy_consumer()
    {
        var queue = RabbitMqFixture.NewQueue(StockReservedKey);
        var gate = new Gate();
        await using var host = rabbit.BuildHost(services =>
        {
            services.AddSingleton(gate);
            services.AddMessageConsumer(queue, builder => builder.Handle<StockReserved, BlockingHandler>(), o => o.Prefetch = 2);
        });
        await host.StartAsync();
        var publisher = host.Services.GetRequiredService<IMessagePublisher>();
        for (var i = 0; i < 5; i++)
        {
            await publisher.PublishAsync(SampleMessage());
        }

        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // One message is being handled and one more is buffered locally (prefetch 2): only 3 stay in the queue.
        await RabbitMqFixture.EventuallyAsync(async () => await rabbit.ReadyMessagesAsync(queue.Name) == 3, "the broker to hold back 3 messages");

        gate.Release.SetResult();
        await RabbitMqFixture.EventuallyAsync(async () => await rabbit.ReadyMessagesAsync(queue.Name) == 0 && gate.Completed == 5, "all 5 to be handled");
        await host.StopAsync();
    }

    [Fact]
    public async Task An_unreadable_message_is_rejected_without_requeue_and_does_not_block_the_queue()
    {
        var queue = RabbitMqFixture.NewQueue(StockReservedKey);
        var sink = new Sink();
        await using var host = ConsumerHost(rabbit, queue, sink);
        await host.StartAsync();

        await using (var connection = await rabbit.OpenConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
        {
            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent, MessageId = Guid.NewGuid().ToString() };
            await channel.BasicPublishAsync(Topology.EventsExchange, StockReservedKey, mandatory: false, properties, Encoding.UTF8.GetBytes("{ not json"));
        }

        await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(SampleMessage());
        await sink.WaitForAsync(1); // the good message behind the bad one is still processed

        await RabbitMqFixture.EventuallyAsync(async () => await rabbit.ReadyMessagesAsync(queue.Name) == 0, "the queue to drain");
        Assert.Single(sink.Received);
        await host.StopAsync();
    }

    [Fact]
    public async Task Declared_topology_keeps_messages_for_a_consumer_that_has_never_started()
    {
        // Start order must not matter: the publishing service declares the queue, so nothing is returned as unroutable.
        var queue = RabbitMqFixture.NewQueue(StockReservedKey);
        await using var host = rabbit.BuildHost(services => services.AddTopology([queue]));
        await host.StartAsync();

        await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(SampleMessage());

        Assert.Equal(1u, await rabbit.ReadyMessagesAsync(queue.Name));
        await host.StopAsync();
    }

    [Fact]
    public async Task Stopping_the_same_consumer_concurrently_is_safe()
    {
        // Found in CI: a shutdown that overlapped another one made the second StopAsync dispose a channel the first
        // had already released (NullReferenceException). Stopping must be idempotent and thread-safe.
        var queue = RabbitMqFixture.NewQueue(StockReservedKey);
        await using var host = ConsumerHost(rabbit, queue, new Sink());
        await host.StartAsync();
        var consumer = host.Services.GetServices<IHostedService>().Single(service => service.GetType().Name == "RabbitMqConsumer");

        var stops = Enumerable.Range(0, 8).Select(_ => consumer.StopAsync(CancellationToken.None)).ToArray();

        await Task.WhenAll(stops);
    }

    [Fact]
    public async Task A_consumer_refuses_to_start_when_a_bound_key_has_no_handler()
    {
        var queue = RabbitMqFixture.NewQueue(StockReservedKey, MessageCatalog.RoutingKeyOf<PaymentFailed>());
        await using var host = rabbit.BuildHost(services =>
        {
            services.AddSingleton(new Sink());
            services.AddMessageConsumer(queue, builder => builder.Handle<StockReserved, RecordingHandler>());
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }
}
