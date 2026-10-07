using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderFlow.Contracts;

namespace OrderFlow.Messaging;

/// <summary>How to run the handler for one message type: resolve it from a fresh scope and call it.</summary>
internal sealed record HandlerRegistration(Type MessageType, Func<IServiceProvider, object, MessageContext, CancellationToken, Task> InvokeAsync);

public sealed class ConsumerBuilder
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<string, HandlerRegistration> _registrations = [];

    internal ConsumerBuilder(IServiceCollection services) => _services = services;

    internal IReadOnlyDictionary<string, HandlerRegistration> Registrations => _registrations;

    /// <summary>Handles messages of type <typeparamref name="TMessage"/> with <typeparamref name="THandler"/> (one instance per message).</summary>
    public ConsumerBuilder Handle<TMessage, THandler>()
        where TMessage : class
        where THandler : class, IMessageHandler<TMessage>
    {
        var routingKey = MessageCatalog.RoutingKeyOf<TMessage>();
        _services.AddScoped<THandler>();
        _registrations[routingKey] = new HandlerRegistration(
            typeof(TMessage),
            (provider, message, context, token) => provider.GetRequiredService<THandler>().HandleAsync((TMessage)message, context, token));
        return this;
    }
}

public static class MessagingServiceCollectionExtensions
{
    /// <summary>Registers the shared connection and the publisher. Reads the <c>RabbitMq</c> configuration section.</summary>
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<RabbitMqConnection>();
        services.TryAddSingleton<RabbitMqPublisher>();
        services.TryAddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
        return services;
    }

    /// <summary>
    /// Adds a hosted service that consumes <paramref name="queue"/>, declaring the queue and its bindings on start.
    /// Every routing key bound to the queue must have a handler.
    /// </summary>
    public static IServiceCollection AddMessageConsumer(
        this IServiceCollection services,
        Topology.QueueDefinition queue,
        Action<ConsumerBuilder> configure,
        Action<ConsumerOptions>? configureOptions = null)
    {
        var builder = new ConsumerBuilder(services);
        configure(builder);

        var options = new ConsumerOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton<IHostedService>(sp => new RabbitMqConsumer(
            sp.GetRequiredService<RabbitMqConnection>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            queue,
            builder.Registrations,
            options,
            sp.GetRequiredService<ILogger<RabbitMqConsumer>>()));
        return services;
    }
}
