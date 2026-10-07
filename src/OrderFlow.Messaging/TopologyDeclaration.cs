using Microsoft.Extensions.Hosting;
using OrderFlow.Contracts;
using RabbitMQ.Client;

namespace OrderFlow.Messaging;

/// <summary>
/// Declares the exchange and a set of queues with their bindings when the service starts. Declaring is idempotent.
/// A publisher uses it so its messages always have a queue to wait in, even if the consuming service has never run yet
/// (otherwise the broker would return them as unroutable and they would be lost).
/// </summary>
internal sealed class TopologyDeclaration(RabbitMqConnection connection, IReadOnlyList<Topology.QueueDefinition> queues) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var amqp = await connection.GetAsync(cancellationToken);
        await using var channel = await amqp.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqConnection.DeclareExchangeAsync(channel, cancellationToken);
        foreach (var queue in queues)
        {
            await channel.QueueDeclareAsync(queue.Name, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            foreach (var routingKey in queue.RoutingKeys)
            {
                await channel.QueueBindAsync(queue.Name, Topology.EventsExchange, routingKey, cancellationToken: cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
